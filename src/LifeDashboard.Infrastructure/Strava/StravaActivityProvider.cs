using System.Text.Json.Serialization;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Common;

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
/// Strava-API. STUB – Umsetzung in Phase 5 (OAuth, Paging über /athlete/activities?after=..., Token-Refresh).
/// Rate-Limits (Standard 100 Requests/15 min, 1000/Tag) werden über den Resilience-Handler
/// (Retry mit Backoff, beachtet Retry-After bei HTTP 429) abgefangen.
/// </summary>
public sealed class StravaActivityProvider(HttpClient http, ISecureTokenStore tokens) : IActivityProvider
{
    internal HttpClient Http { get; } = http;

    internal ISecureTokenStore Tokens { get; } = tokens;

    public ProviderKind Kind => ProviderKind.Strava;

    public Task<Result<IReadOnlyList<SportActivity>>> GetActivitiesAsync(DateTimeOffset afterUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<IReadOnlyList<SportActivity>>(Errors.NotImplementedYet("Strava-Import (Phase 5)")));
}

/// <summary>Strava-Login. STUB – Phase 5.</summary>
public sealed class StravaAccountConnector() : NotImplementedAccountConnector(ProviderKind.Strava, "Strava-Login (Phase 5)");
