using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Abstractions;

/// <summary>
/// Lokales Gedächtnis des Syncs (SQLite): welches Original wurde wohin gespiegelt, mit welchem Hash.
/// Grundlage für Idempotenz, Löschungen und Schleifenschutz.
/// </summary>
public interface ISyncLedger
{
    /// <summary>Sucht die Spiegelung eines Originals in einem Zielkalender.</summary>
    Task<LedgerEntry?> FindAsync(string originKey, string targetCalendarId, CancellationToken cancellationToken = default);

    /// <summary>Sucht, ob ein Termin selbst eine von der App erzeugte Spiegelung ist.</summary>
    Task<LedgerEntry?> FindByTargetEventAsync(string targetCalendarId, string targetEventId, CancellationToken cancellationToken = default);

    /// <summary>Alle Einträge, deren Original aus dem angegebenen Quellkalender stammt.</summary>
    Task<IReadOnlyList<LedgerEntry>> GetBySourceCalendarAsync(string sourceCalendarId, CancellationToken cancellationToken = default);

    /// <summary>Legt einen Eintrag an oder ersetzt ihn (Schlüssel: OriginKey + TargetCalendarId).</summary>
    Task UpsertAsync(LedgerEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Entfernt einen Eintrag.</summary>
    Task RemoveAsync(string originKey, string targetCalendarId, CancellationToken cancellationToken = default);

    /// <summary>Speichert die Zusammenfassung eines Sync-Laufs.</summary>
    Task AddRunAsync(SyncRunRecord run, CancellationToken cancellationToken = default);

    /// <summary>Liefert die letzten Sync-Läufe, neueste zuerst.</summary>
    Task<IReadOnlyList<SyncRunRecord>> GetRecentRunsAsync(int count, CancellationToken cancellationToken = default);
}
