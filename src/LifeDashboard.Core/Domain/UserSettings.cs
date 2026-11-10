using LifeDashboard.Core.Sync;

namespace LifeDashboard.Core.Domain;

/// <summary>
/// Vom Nutzer in der App geänderte Einstellungen (persistiert über <see cref="Abstractions.IUserSettingsStore"/>).
/// Technische Einstellungen (Client-IDs, Sync-Fenster, Konfliktstrategie) bleiben in appsettings.json.
/// Neue Eigenschaften brauchen immer einen sinnvollen Default, damit ältere gespeicherte Stände weiter ladbar sind.
/// </summary>
public sealed record UserSettings
{
    /// <summary>
    /// True, sobald der Nutzer die Kalenderauswahl einmal gespeichert hat.
    /// Vorher gelten alle eigenen Kalender als ausgewählt.
    /// </summary>
    public bool HasCalendarSelection { get; init; }

    /// <summary>IDs der eigenen Kalender, die untereinander gespiegelt werden.</summary>
    public IReadOnlyList<string> SelectedCalendarIds { get; init; } = [];

    /// <summary>Zielkalender für Sportaktivitäten (null = automatisch: erster eigener Kalender mit "Sport" im Namen).</summary>
    public string? ActivityTargetCalendarId { get; init; }

    /// <summary>Detailstufe für privat ↔ Arbeit. Default: nur "Beschäftigt".</summary>
    public MirrorDetailLevel PrivateWorkDetailLevel { get; init; } = MirrorDetailLevel.BusyOnly;

    /// <summary>Uhrzeit des Daily Digests (lokal).</summary>
    public TimeOnly DigestTime { get; init; } = new(6, 30);

    /// <summary>Eigene Watchlist. Leer = Watchlist aus appsettings.json verwenden.</summary>
    public IReadOnlyList<string> Watchlist { get; init; } = [];

    /// <summary>
    /// Abonnierte ICS-Kalender fremder Personen/Firmen (immer read-only).
    /// Die URL selbst ist geheim und liegt NICHT hier, sondern im <see cref="Abstractions.ISecureTokenStore"/>
    /// unter <see cref="IcsSubscription.SecretKey"/>.
    /// </summary>
    public IReadOnlyList<IcsSubscription> IcsSubscriptions { get; init; } = [];

    /// <summary>Spiegelungsregeln aus den Einstellungen. Gleiche Kategorie spiegelt immer voll.</summary>
    public SyncRules ToRules() => new()
    {
        Rules =
        [
            new(CalendarCategory.Private, CalendarCategory.Private, MirrorDetailLevel.Full),
            new(CalendarCategory.Work, CalendarCategory.Work, MirrorDetailLevel.Full),
            new(CalendarCategory.Private, CalendarCategory.Work, PrivateWorkDetailLevel),
            new(CalendarCategory.Work, CalendarCategory.Private, PrivateWorkDetailLevel),
        ],
    };

    /// <summary>Eigene Watchlist oder, falls leer, die übergebene Standardliste.</summary>
    public IReadOnlyList<string> EffectiveWatchlist(IReadOnlyList<string> fallback) => Watchlist.Count > 0 ? Watchlist : fallback;

    /// <summary>Zerlegt eine kommagetrennte Eingabe in bereinigte, eindeutige Symbole (Großbuchstaben).</summary>
    public static IReadOnlyList<string> ParseWatchlist(string? input) => (input ?? string.Empty)
        .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(s => s.ToUpperInvariant())
        .Distinct(StringComparer.Ordinal)
        .ToList();
}
