using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Infrastructure.Common;

/// <summary>Gemeinsame Hilfen für die REST-Kalender-Provider (Graph, Google).</summary>
internal static class RestCalendarHelpers
{
    /// <summary>Zerlegt eine Kalender-ID "{Provider}:{Konto}:{native ID}" (native IDs können ':' nicht enthalten, Rest wird übernommen).</summary>
    public static string NativeId(CalendarInfo calendar) => calendar.Id.Split(':', 3) is [_, _, var native] ? native : calendar.Id;

    /// <summary>Herkunftsmarker als kompaktes JSON (Graph: ein Property-Wert).</summary>
    public static string MarkerJson(OriginMarker marker) => JsonSerializer.Serialize(new { p = marker.OriginProvider.ToString(), o = marker.OriginId, h = marker.SyncHash });

    public static OriginMarker? ParseMarkerJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(json);
            var provider = Enum.TryParse<ProviderKind>(node?["p"]?.GetValue<string>(), out var p) ? p : ProviderKind.InMemory;
            var origin = node?["o"]?.GetValue<string>();
            var hash = node?["h"]?.GetValue<string>();
            return origin is null || hash is null ? null : new OriginMarker(provider, origin, hash);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Sendet eine Anfrage mit Bearer-Token und wandelt typische Fehler in Results um.</summary>
    public static async Task<Result<JsonNode?>> SendAsync(
        HttpClient http, IAccessTokenSource tokens, HttpMethod method, string url, JsonNode? body, string providerName, CancellationToken ct, Action<HttpRequestMessage>? configure = null)
    {
        var token = await tokens.GetAccessTokenAsync(ct).ConfigureAwait(false);
        if (token.IsFailure)
        {
            return Result.Failure<JsonNode?>(token.Error);
        }

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        configure?.Invoke(request);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return Result.Failure<JsonNode?>(Errors.NotFound("Termin/Kalender"));
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                return Result.Failure<JsonNode?>(Errors.NotConnected(providerName));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<JsonNode?>(Errors.ProviderFailure(providerName, $"HTTP {(int)response.StatusCode}"));
            }

            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Result.Success(string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text));
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<JsonNode?>(Errors.ProviderFailure(providerName, $"nicht erreichbar ({ex.HttpRequestError})"));
        }
        catch (JsonException)
        {
            return Result.Failure<JsonNode?>(Errors.ProviderFailure(providerName, "ungültige Antwort"));
        }
    }

    public static DateTimeOffset ParseUtc(string? text, DateTimeOffset fallback)
        => DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var v) ? v.ToUniversalTime() : fallback;

    /// <summary>Windows-Zeitzonen (Outlook) → IANA.</summary>
    public static string ToIana(string? tz)
        => string.IsNullOrWhiteSpace(tz) ? "UTC" : TimeZoneInfo.TryConvertWindowsIdToIanaId(tz, out var iana) ? iana : tz;
}
