using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Infrastructure.Persistence;

/// <summary>
/// SQLite-Schema des Ledgers (Phase 1, sqlite-net-pcl).
/// Migrationsstrategie: Tabelle SchemaVersion + nummerierte, nur vorwärts laufende Migrationsschritte.
/// Siehe docs/SYNC_DESIGN.md.
/// </summary>
public static class LedgerSchema
{
    public const int CurrentVersion = 1;

    /// <summary>Migrationen in Reihenfolge. Index + 1 = Zielversion. Bestehende Einträge NIE ändern, nur anhängen.</summary>
    public static readonly IReadOnlyList<string> Migrations =
    [
        """
        CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS Ledger (
            OriginKey             TEXT    NOT NULL,
            TargetCalendarId      TEXT    NOT NULL,
            OriginProvider        TEXT    NOT NULL,
            SourceCalendarId      TEXT    NULL,
            OriginNativeId        TEXT    NOT NULL,
            OriginStartUtc        INTEGER NOT NULL,
            TargetEventId         TEXT    NOT NULL,
            SyncHash              TEXT    NOT NULL,
            TargetLastModifiedUtc INTEGER NOT NULL,
            LastSyncedUtc         INTEGER NOT NULL,
            Status                TEXT    NOT NULL,
            PRIMARY KEY (OriginKey, TargetCalendarId)
        );
        CREATE INDEX IF NOT EXISTS IX_Ledger_Target ON Ledger (TargetCalendarId, TargetEventId);
        CREATE INDEX IF NOT EXISTS IX_Ledger_Source ON Ledger (SourceCalendarId);
        CREATE TABLE IF NOT EXISTS SyncRun (
            Id TEXT PRIMARY KEY, StartedUtc INTEGER NOT NULL, FinishedUtc INTEGER NULL, DryRun INTEGER NOT NULL,
            Status TEXT NOT NULL, Created INTEGER NOT NULL, Updated INTEGER NOT NULL, Deleted INTEGER NOT NULL,
            Conflicts INTEGER NOT NULL, Errors INTEGER NOT NULL
        );
        """,
    ];
}

/// <summary>SQLite-Ledger. STUB – Umsetzung in Phase 1. Bis dahin nutzt die App <c>InMemorySyncLedger</c>.</summary>
public sealed class SqliteSyncLedger(string databasePath) : ISyncLedger
{
    private const string Phase = "SQLite-Ledger wird in Phase 1 implementiert.";

    public string DatabasePath { get; } = databasePath;

    public Task<LedgerEntry?> FindAsync(string originKey, string targetCalendarId, CancellationToken cancellationToken = default) => throw new NotImplementedException(Phase);

    public Task<LedgerEntry?> FindByTargetEventAsync(string targetCalendarId, string targetEventId, CancellationToken cancellationToken = default) => throw new NotImplementedException(Phase);

    public Task<IReadOnlyList<LedgerEntry>> GetBySourceCalendarAsync(string sourceCalendarId, CancellationToken cancellationToken = default) => throw new NotImplementedException(Phase);

    public Task UpsertAsync(LedgerEntry entry, CancellationToken cancellationToken = default) => throw new NotImplementedException(Phase);

    public Task RemoveAsync(string originKey, string targetCalendarId, CancellationToken cancellationToken = default) => throw new NotImplementedException(Phase);

    public Task AddRunAsync(SyncRunRecord run, CancellationToken cancellationToken = default) => throw new NotImplementedException(Phase);

    public Task<IReadOnlyList<SyncRunRecord>> GetRecentRunsAsync(int count, CancellationToken cancellationToken = default) => throw new NotImplementedException(Phase);
}
