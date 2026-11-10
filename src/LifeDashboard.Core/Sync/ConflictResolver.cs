using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Sync;

/// <summary>Entscheidet, ob eine manuell geänderte Spiegelung überschrieben wird.</summary>
public sealed class ConflictResolver(ConflictStrategy strategy)
{
    public ConflictStrategy Strategy { get; } = strategy;

    /// <summary>
    /// Die Spiegelung wurde im Zielkalender manuell geändert, wenn ihr Server-Zeitstempel neuer ist
    /// als der Zeitstempel, den der Server bei unserem letzten Schreiben gemeldet hat.
    /// </summary>
    public static bool TargetWasModified(LedgerEntry entry, CalendarEvent currentTarget)
        => currentTarget.LastModifiedUtc > entry.TargetLastModifiedUtc;

    /// <summary>Liefert true, wenn das Original die (manuell geänderte) Spiegelung überschreiben soll.</summary>
    public bool SourceShouldWin(DateTimeOffset sourceModifiedUtc, DateTimeOffset targetModifiedUtc) => Strategy switch
    {
        ConflictStrategy.SourceWins => true,
        ConflictStrategy.TargetWins => false,
        ConflictStrategy.LastModifiedWins => sourceModifiedUtc >= targetModifiedUtc,
        _ => throw new InvalidOperationException($"Unknown strategy {Strategy}."),
    };
}
