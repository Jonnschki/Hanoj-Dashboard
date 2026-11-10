using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Sync;

/// <summary>Parameter eines Sync-Laufs.</summary>
public sealed record SyncRequest
{
    /// <summary>IDs der EIGENEN Kalender, die untereinander gespiegelt werden. Fremde Kalender werden ignoriert.</summary>
    public required IReadOnlyCollection<string> MirrorCalendarIds { get; init; }

    /// <summary>Zeitfenster für Termine.</summary>
    public required DateRange Window { get; init; }

    /// <summary>Nur planen, nichts schreiben.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Löschungen ausführen? Default false: Löschungen erscheinen nur im Report und warten auf Bestätigung.</summary>
    public bool ConfirmDeletes { get; init; }

    /// <summary>Zielkalender für Sportaktivitäten (null = kein Import).</summary>
    public string? ActivityTargetCalendarId { get; init; }

    /// <summary>Aktivitäten ab diesem Zeitpunkt importieren.</summary>
    public DateTimeOffset? ActivitiesSinceUtc { get; init; }

    /// <summary>Spiegelungsregeln für diesen Lauf (z. B. aus den Nutzereinstellungen). Null = Regeln der Engine.</summary>
    public SyncRules? Rules { get; init; }
}

/// <summary>Fortschritt für die UI.</summary>
public sealed record SyncProgress(double Fraction, string Message);
