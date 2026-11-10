using System.Text.Json.Serialization;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using System.Net.Http.Json;

namespace LifeDashboard.Infrastructure.Strava;

/// <summary>Ausschnitt aus Strava "SummaryActivity" (GET /athlete/activities).</summary>
public sealed record StravaActivityDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sport_type")] string? SportType,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("start_date")] DateTimeOffset StartDate,
    [property: JsonPropertyName("timezone")] string? Timezone,
    [property: JsonPropertyName("elapsed_time")] int ElapsedTimeSeconds,
    [property: JsonPropertyName("moving_time")] int MovingTimeSeconds,
    [property: JsonPropertyName("distance")] double DistanceMeters);

/// <summary>Wandelt Strava-DTOs in Domain-Objekte um.</summary>
public static class StravaMapper
{
    public static SportActivity ToDomain(StravaActivityDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var id = dto.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new SportActivity(
            Id: id,
            Provider: ProviderKind.Strava,
            Name: dto.Name,
            SportType: dto.SportType ?? dto.Type ?? "Workout",
            StartUtc: dto.StartDate.ToUniversalTime(),
            TimeZoneId: ParseTimeZone(dto.Timezone),
            ElapsedTime: TimeSpan.FromSeconds(dto.ElapsedTimeSeconds),
            MovingTime: TimeSpan.FromSeconds(dto.MovingTimeSeconds),
            DistanceMeters: dto.DistanceMeters,
            Url: $"https://www.strava.com/activities/{id}");
    }

    /// <summary>Strava liefert z. B. "(GMT+01:00) Europe/Vienna" → "Europe/Vienna".</summary>
    public static string ParseTimeZone(string? stravaTimezone)
    {
        if (string.IsNullOrWhiteSpace(stravaTimezone))
        {
            return "UTC";
        }

        var idx = stravaTimezone.LastIndexOf(") ", StringComparison.Ordinal);
        return idx >= 0 ? stravaTimezone[(idx + 2)..].Trim() : stravaTimezone.Trim();
    }
}

/// <summary>
/// Strava-API: GET /athlete/activities?after=…&amp;per_page=100&amp;page=n (seitenweise, bis eine Seite leer ist).
/// Rate-Limits (Standard 100 Requests/15 min, 1000/Tag) fängt der Resilience-Handler ab
/// (Retry mit Backoff, beachtet Retry-After bei HTTP 429).
/// Ist kein Strava-Konto verbunden, liefert der Provider eine leere Liste (kein Fehler).
/// </summary>
public sealed class StravaActivityProvider(HttpClient http, IAccessTokenSource tokens) : IActivityProvider
{
    private const int PageSize = 100;
    private const int MaxPages = 20;

    public ProviderKind Kind => ProviderKind.Strava;

    public async Task<Result<IReadOnlyList<SportActivity>>> GetActivitiesAsync(DateTimeOffset afterUtc, CancellationToken cancellationToken = default)
    {
        var token = await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token.IsFailure)
        {
            return token.Error.Code == "auth.not_connected"
                ? Result.Success<IReadOnlyList<SportActivity>>([])
                : Result.Failure<IReadOnlyList<SportActivity>>(token.Error);
        }

        var result = new List<SportActivity>();
        for (var page = 1; page <= MaxPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"athlete/activities?after={afterUtc.ToUnixTimeSeconds()}&per_page={PageSize}&page={page}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Value);
            try
            {
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return Result.Failure<IReadOnlyList<SportActivity>>(Errors.NotConnected("Strava"));
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<IReadOnlyList<SportActivity>>(Errors.ProviderFailure("Strava", $"HTTP {(int)response.StatusCode}"));
                }

                var items = await response.Content.ReadFromJsonAsync<List<StravaActivityDto>>(cancellationToken).ConfigureAwait(false) ?? [];
                result.AddRange(items.Select(StravaMapper.ToDomain));
                if (items.Count < PageSize)
                {
                    break;
                }
            }
            catch (HttpRequestException ex)
            {
                return Result.Failure<IReadOnlyList<SportActivity>>(Errors.ProviderFailure("Strava", $"nicht erreichbar ({ex.HttpRequestError})"));
            }
        }

        return Result.Success<IReadOnlyList<SportActivity>>(result);
    }
}

/// <summary>Strava-OAuth (kein PKCE; Client-Secret erforderlich). Athletenname kommt mit der Token-Antwort.</summary>
public static class StravaOAuth
{
    public static Auth.OAuthSettings Create(string clientId, string? clientSecret) => new(
        ProviderKind.Strava,
        new Uri("https://www.strava.com/oauth/authorize"),
        new Uri("https://www.strava.com/oauth/token"),
        clientId,
        clientSecret,
        "read,activity:read_all",
        UsePkce: false,
        new Dictionary<string, string> { ["approval_prompt"] = "auto" });

    public static string? AthleteName(System.Text.Json.JsonElement tokenResponse)
        => tokenResponse.TryGetProperty("athlete", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Object
            ? $"{(a.TryGetProperty("firstname", out var f) ? f.GetString() : null)} {(a.TryGetProperty("lastname", out var l) ? l.GetString() : null)}".Trim()
            : null;
}
