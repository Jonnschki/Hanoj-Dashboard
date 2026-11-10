namespace LifeDashboard.Core.Domain;

/// <summary>Sportaktivität (z. B. aus Strava).</summary>
/// <param name="Id">Native ID (Strava-Activity-ID).</param>
/// <param name="Provider">Herkunft.</param>
/// <param name="Name">Titel der Aktivität.</param>
/// <param name="SportType">Sportart, z. B. "Run" oder "Ride".</param>
/// <param name="StartUtc">Start (UTC).</param>
/// <param name="TimeZoneId">Zeitzone der Aktivität.</param>
/// <param name="ElapsedTime">Gesamtdauer.</param>
/// <param name="MovingTime">Bewegungszeit.</param>
/// <param name="DistanceMeters">Distanz in Metern.</param>
/// <param name="Url">Link zur Aktivität.</param>
public sealed record SportActivity(
    string Id,
    ProviderKind Provider,
    string Name,
    string SportType,
    DateTimeOffset StartUtc,
    string TimeZoneId,
    TimeSpan ElapsedTime,
    TimeSpan MovingTime,
    double DistanceMeters,
    string? Url);

/// <summary>Kurs eines Wertpapiers oder Index.</summary>
public sealed record MarketQuote(
    string Symbol,
    string? Name,
    decimal Price,
    decimal Change,
    decimal ChangePercent,
    string Currency,
    DateTimeOffset AsOfUtc)
{
    /// <summary>Datenquelle dieses Kurses (z. B. "Yahoo Finance" oder "Finnhub").</summary>
    public string? Source { get; init; }
}

/// <summary>Status eines Sync-Laufs.</summary>
public enum SyncRunStatus
{
    Running,
    Succeeded,
    CompletedWithErrors,
    Failed,
    Cancelled,
}

/// <summary>Zusammenfassung eines Sync-Laufs (für Historie und Dashboard-Kachel).</summary>
public sealed record SyncRunRecord(
    Guid Id,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc,
    bool DryRun,
    SyncRunStatus Status,
    int Created,
    int Updated,
    int Deleted,
    int Conflicts,
    int Errors);

/// <summary>Status eines Ledger-Eintrags.</summary>
public enum LedgerStatus
{
    Active,
    Conflict,
    Error,
}

/// <summary>
/// Eine Zeile im Sync-Ledger: verknüpft ein Original mit genau einer Spiegelung in einem Zielkalender.
/// Eindeutiger Schlüssel ist (<see cref="OriginKey"/>, <see cref="TargetCalendarId"/>).
/// </summary>
/// <param name="OriginKey">Schlüssel des Originals (<see cref="Domain.OriginKey"/>).</param>
/// <param name="OriginProvider">Anbieter des Originals.</param>
/// <param name="SourceCalendarId">Quellkalender (null bei Aktivitäten).</param>
/// <param name="OriginNativeId">Native ID des Originals.</param>
/// <param name="OriginStartUtc">Beginn des Originals, um Löschungen auf das Sync-Fenster zu begrenzen.</param>
/// <param name="TargetCalendarId">Zielkalender.</param>
/// <param name="TargetEventId">ID der Spiegelung im Zielkalender.</param>
/// <param name="SyncHash">Hash des zuletzt geschriebenen Inhalts.</param>
/// <param name="TargetLastModifiedUtc">Server-Änderungszeitpunkt der Spiegelung direkt nach unserem Schreiben (Konflikterkennung).</param>
/// <param name="LastSyncedUtc">Lokaler Zeitpunkt des letzten Schreibens (nur Info).</param>
/// <param name="Status">Status.</param>
public sealed record LedgerEntry(
    string OriginKey,
    ProviderKind OriginProvider,
    string? SourceCalendarId,
    string OriginNativeId,
    DateTimeOffset OriginStartUtc,
    string TargetCalendarId,
    string TargetEventId,
    string SyncHash,
    DateTimeOffset TargetLastModifiedUtc,
    DateTimeOffset LastSyncedUtc,
    LedgerStatus Status);
