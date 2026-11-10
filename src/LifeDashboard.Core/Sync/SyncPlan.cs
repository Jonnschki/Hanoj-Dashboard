using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Sync;

/// <summary>Art einer geplanten Änderung.</summary>
public enum SyncActionKind
{
    Create,
    Update,
    Delete,
    Unchanged,

    /// <summary>Konflikt erkannt; je nach Strategie wird trotzdem aktualisiert oder übersprungen.</summary>
    Conflict,

    /// <summary>Löschung vorgemerkt, wartet auf Bestätigung aus dem Dry-Run-Report.</summary>
    PendingDeleteConfirmation,

    Skipped,
}

/// <summary>Ergebnis einer ausgeführten Aktion.</summary>
public enum SyncActionOutcome
{
    /// <summary>Nur geplant (Dry-Run oder keine Ausführung nötig).</summary>
    Planned,
    Succeeded,
    Failed,
}

/// <summary>Eine geplante (und ggf. ausgeführte) Änderung.</summary>
/// <param name="Kind">Art der Änderung.</param>
/// <param name="OriginKey">Ledger-Schlüssel des Originals.</param>
/// <param name="OriginProvider">Anbieter des Originals.</param>
/// <param name="SourceCalendarId">Quellkalender (null bei Aktivitäten).</param>
/// <param name="OriginNativeId">Native ID des Originals.</param>
/// <param name="Target">Zielkalender.</param>
/// <param name="Title">Lesbarer Titel für den Report (bei BusyOnly der Platzhaltertitel).</param>
/// <param name="Mirror">Zu schreibender Inhalt (bei Create/Update/Conflict-Update).</param>
/// <param name="ExistingTargetEventId">Bestehende Spiegelung (bei Update/Delete).</param>
/// <param name="Reason">Begründung, z. B. Konfliktdetails.</param>
public sealed record SyncAction(
    SyncActionKind Kind,
    string OriginKey,
    ProviderKind OriginProvider,
    string? SourceCalendarId,
    string OriginNativeId,
    CalendarInfo Target,
    string Title,
    CalendarEvent? Mirror,
    string? ExistingTargetEventId,
    string? Reason)
{
    /// <summary>Bei Konflikten: soll die Spiegelung trotzdem überschrieben werden?</summary>
    public bool ConflictOverwrites { get; init; }

    /// <summary>Beginn des Originals (wird ins Ledger übernommen).</summary>
    public DateTimeOffset OriginStartUtc { get; init; }

    /// <summary>Inhalts-Hash der Spiegelung.</summary>
    public string SyncHash { get; init; } = string.Empty;

    public SyncActionOutcome Outcome { get; init; } = SyncActionOutcome.Planned;

    public string? Error { get; init; }

    /// <summary>True, wenn diese Aktion etwas im Zielkalender verändert.</summary>
    public bool IsWrite => Kind is SyncActionKind.Create or SyncActionKind.Update or SyncActionKind.Delete
        || (Kind == SyncActionKind.Conflict && ConflictOverwrites);
}

/// <summary>Vollständiger Plan eines Sync-Laufs, bevor etwas geschrieben wird.</summary>
public sealed record SyncPlan(IReadOnlyList<SyncAction> Actions)
{
    public IEnumerable<SyncAction> Writes => Actions.Where(a => a.IsWrite);
}
