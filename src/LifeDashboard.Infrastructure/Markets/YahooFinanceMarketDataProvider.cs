using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using Microsoft.Extensions.Logging;

namespace LifeDashboard.Infrastructure.Markets;

/// <summary>
/// Kurse von Yahoo Finance über den (inoffiziellen) Chart-Endpunkt v8.
/// ACHTUNG: Yahoo bietet keine offizielle API; die Nutzung ist eine Grauzone und kann jederzeit brechen.
/// Bewusst auf Wunsch des Nutzers für den privaten Gebrauch eingebaut, mit Finnhub als Fallback
/// (siehe <see cref="FallbackMarketDataProvider"/>).
/// Symbole im Yahoo-Format: ^ATX, ^GDAXI, ^STOXX50E, ^GSPC, AAPL, VOE.VI, EURUSD=X.
/// </summary>
public sealed class YahooFinanceMarketDataProvider(HttpClient http, IClock clock, ILogger<YahooFinanceMarketDataProvider> logger) : IMarketDataProvider
{
    public string Name => "Yahoo Finance";

    public async Task<Result<IReadOnlyList<MarketQuote>>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        using var gate = new SemaphoreSlim(4);
        var anyNetworkError = false;

        var tasks = symbols.Distinct(StringComparer.OrdinalIgnoreCase).Select(async symbol =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LoadAsync(symbol, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Yahoo {Symbol}: {Type}", symbol, ex.GetType().Name);
                anyNetworkError = true;
                return null;
            }
            finally
            {
                gate.Release();
            }
        });

        var quotes = (await Task.WhenAll(tasks).ConfigureAwait(false)).OfType<MarketQuote>().ToList();
        if (quotes.Count == 0 && anyNetworkError)
        {
            return Result.Failure<IReadOnlyList<MarketQuote>>(Errors.ProviderFailure(Name, "nicht erreichbar"));
        }

        return Result.Success<IReadOnlyList<MarketQuote>>(quotes);
    }

    private async Task<MarketQuote?> LoadAsync(string symbol, CancellationToken ct)
    {
        using var response = await http.GetAsync($"v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=1d", ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation("Yahoo: Symbol {Symbol} unbekannt", symbol);
            return null;
        }

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<ChartResponse>(ct).ConfigureAwait(false);
        var meta = dto?.Chart?.Result?.FirstOrDefault()?.Meta;
        if (meta?.RegularMarketPrice is not { } price)
        {
            return null;
        }

        var previous = meta.ChartPreviousClose ?? meta.PreviousClose;
        var change = previous is { } p ? price - p : 0m;
        var percent = meta.RegularMarketChangePercent ?? (previous is { } q && q != 0 ? change / q * 100m : 0m);
        var asOf = meta.RegularMarketTime is { } t ? DateTimeOffset.FromUnixTimeSeconds(t) : clock.UtcNow;

        return new MarketQuote(
            meta.Symbol ?? symbol,
            MarketSymbols.DisplayName(meta.Symbol ?? symbol) ?? meta.ShortName ?? meta.LongName,
            decimal.Round(price, 4),
            decimal.Round(change, 4),
            decimal.Round(percent, 2),
            meta.Currency ?? string.Empty,
            asOf)
        {
            Source = Name,
        };
    }

    internal sealed record ChartResponse([property: JsonPropertyName("chart")] ChartBody? Chart);

    internal sealed record ChartBody([property: JsonPropertyName("result")] IReadOnlyList<ChartResult>? Result);

    internal sealed record ChartResult([property: JsonPropertyName("meta")] ChartMeta? Meta);

    internal sealed record ChartMeta(
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("shortName")] string? ShortName,
        [property: JsonPropertyName("longName")] string? LongName,
        [property: JsonPropertyName("regularMarketPrice")] decimal? RegularMarketPrice,
        [property: JsonPropertyName("chartPreviousClose")] decimal? ChartPreviousClose,
        [property: JsonPropertyName("previousClose")] decimal? PreviousClose,
        [property: JsonPropertyName("regularMarketChangePercent")] decimal? RegularMarketChangePercent,
        [property: JsonPropertyName("regularMarketTime")] long? RegularMarketTime);
}

/// <summary>Lesbare Namen für gängige Yahoo-Symbole.</summary>
public static class MarketSymbols
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["^ATX"] = "ATX",
        ["^GDAXI"] = "DAX",
        ["^STOXX50E"] = "Euro Stoxx 50",
        ["^GSPC"] = "S&P 500",
        ["^NDX"] = "Nasdaq 100",
        ["^DJI"] = "Dow Jones",
        ["^SSMI"] = "SMI",
        ["^FTSE"] = "FTSE 100",
        ["^N225"] = "Nikkei 225",
        ["EURUSD=X"] = "EUR/USD",
        ["GC=F"] = "Gold",
        ["BZ=F"] = "Brent",
        ["BTC-EUR"] = "Bitcoin (EUR)",
    };

    public static string? DisplayName(string symbol) => Names.GetValueOrDefault(symbol);
}

/// <summary>
/// Fragt zuerst die primäre Quelle (Yahoo). Fehlt ein Symbol oder ist die Quelle nicht erreichbar,
/// werden die fehlenden Symbole bei der Fallback-Quelle (Finnhub) nachgefragt.
/// </summary>
public sealed class FallbackMarketDataProvider(IMarketDataProvider primary, IMarketDataProvider? fallback, ILogger<FallbackMarketDataProvider> logger) : IMarketDataProvider
{
    public string Name => fallback is null ? primary.Name : $"{primary.Name} (Fallback: {fallback.Name})";

    public async Task<Result<IReadOnlyList<MarketQuote>>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var first = await primary.GetQuotesAsync(symbols, cancellationToken).ConfigureAwait(false);
        var quotes = first.IsSuccess ? first.Value.ToList() : [];
        var missing = symbols.Where(s => !quotes.Any(q => string.Equals(q.Symbol, s, StringComparison.OrdinalIgnoreCase))).ToList();

        if (missing.Count == 0 || fallback is null)
        {
            return first.IsSuccess || quotes.Count > 0 ? Result.Success<IReadOnlyList<MarketQuote>>(quotes) : first;
        }

        logger.LogInformation("{Primary}: {Count} Symbole fehlen, frage {Fallback}", primary.Name, missing.Count, fallback.Name);
        var second = await fallback.GetQuotesAsync(missing, cancellationToken).ConfigureAwait(false);
        if (second.IsSuccess)
        {
            quotes.AddRange(second.Value);
        }

        if (quotes.Count == 0 && first.IsFailure)
        {
            return first;
        }

        // Reihenfolge der Watchlist beibehalten.
        var order = symbols.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i, StringComparer.OrdinalIgnoreCase);
        return Result.Success<IReadOnlyList<MarketQuote>>(quotes.OrderBy(q => order.GetValueOrDefault(q.Symbol, int.MaxValue)).ToList());
    }
}
