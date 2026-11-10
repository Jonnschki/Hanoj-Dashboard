using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LifeDashboard.Infrastructure.Markets;

/// <summary>
/// Finnhub (https://finnhub.io) – offizielle REST-API, Free-Tier ca. 60 Requests/Minute.
/// Free-Tier deckt v. a. US-Werte ab; Indizes daher über ETFs (SPY ≈ S&amp;P 500, QQQ ≈ Nasdaq 100).
/// Endpoint: GET /quote?symbol=SYM → { c, d, dp, t } (aktuell, Änderung, Änderung %, Unix-Zeit).
/// </summary>
public sealed class FinnhubMarketDataProvider(HttpClient http, IOptions<MarketDataOptions> options, ILogger<FinnhubMarketDataProvider> logger) : IMarketDataProvider
{
    public string Name => "Finnhub";

    public async Task<Result<IReadOnlyList<MarketQuote>>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result.Failure<IReadOnlyList<MarketQuote>>(new Error("config.missing", "Finnhub-API-Key fehlt (appsettings.Local.json → MarketData:ApiKey)."));
        }

        var quotes = new List<MarketQuote>();
        foreach (var symbol in symbols)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"quote?symbol={Uri.EscapeDataString(symbol)}");
                request.Headers.Add("X-Finnhub-Token", apiKey); // Key im Header statt in der URL (landet nicht in Logs).
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var dto = await response.Content.ReadFromJsonAsync<FinnhubQuoteDto>(cancellationToken).ConfigureAwait(false);

                // Finnhub liefert für unbekannte Symbole Nullen statt 404.
                if (dto is null || dto.Timestamp == 0)
                {
                    logger.LogWarning("Unbekanntes Symbol {Symbol}", symbol);
                    continue;
                }

                quotes.Add(new MarketQuote(symbol, MarketSymbols.DisplayName(symbol), dto.Current, dto.Change ?? 0, dto.ChangePercent ?? 0, "USD", DateTimeOffset.FromUnixTimeSeconds(dto.Timestamp)) { Source = Name });
            }
            catch (HttpRequestException ex)
            {
                return Result.Failure<IReadOnlyList<MarketQuote>>(Errors.ProviderFailure(Name, ex.Message));
            }
        }

        return Result.Success<IReadOnlyList<MarketQuote>>(quotes);
    }

    internal sealed record FinnhubQuoteDto(
        [property: JsonPropertyName("c")] decimal Current,
        [property: JsonPropertyName("d")] decimal? Change,
        [property: JsonPropertyName("dp")] decimal? ChangePercent,
        [property: JsonPropertyName("t")] long Timestamp);
}

/// <summary>Platzhalter-Kurse ohne Netzwerk (Demo-Modus / kein API-Key).</summary>
public sealed class DemoMarketDataProvider(IClock clock) : IMarketDataProvider
{
    public string Name => "Demo-Daten";

    public Task<Result<IReadOnlyList<MarketQuote>>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        IReadOnlyList<MarketQuote> quotes = symbols
            .Select((s, i) => new MarketQuote(s, null, 100m + (i * 37.5m), (i % 2 == 0 ? 1 : -1) * 0.8m * (i + 1), (i % 2 == 0 ? 1 : -1) * 0.45m * (i + 1), "USD", clock.UtcNow))
            .ToList();
        return Task.FromResult(Result.Success(quotes));
    }
}
