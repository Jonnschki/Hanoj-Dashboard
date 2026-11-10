using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;
using SQLite;

namespace LifeDashboard.Infrastructure.Persistence;

/// <summary>Sync-Ledger und Lauf-Historie in SQLite. Schema: <see cref="DatabaseSchema"/>.</summary>
public sealed class SqliteSyncLedger(SqliteDatabase database) : ISyncLedger
{
    private const string LedgerColumns =
        "OriginKey, TargetCalendarId, OriginProvider, SourceCalendarId, OriginNativeId, OriginStartUtcTicks, TargetEventId, SyncHash, TargetLastModifiedUtcTicks, LastSyncedUtcTicks, Status";

    public async Task<LedgerEntry?> FindAsync(string originKey, string targetCalendarId, CancellationToken cancellationToken = default)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var rows = await db.QueryAsync<LedgerRow>(
            $"SELECT {LedgerColumns} FROM Ledger WHERE OriginKey = ? AND TargetCalendarId = ?", originKey, targetCalendarId).ConfigureAwait(false);
        return rows.Select(ToEntry).FirstOrDefault();
    }

    public async Task<LedgerEntry?> FindByTargetEventAsync(string targetCalendarId, string targetEventId, CancellationToken cancellationToken = default)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var rows = await db.QueryAsync<LedgerRow>(
            $"SELECT {LedgerColumns} FROM Ledger WHERE TargetCalendarId = ? AND TargetEventId = ? LIMIT 1", targetCalendarId, targetEventId).ConfigureAwait(false);
        return rows.Select(ToEntry).FirstOrDefault();
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetBySourceCalendarAsync(string sourceCalendarId, CancellationToken cancellationToken = default)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var rows = await db.QueryAsync<LedgerRow>(
            $"SELECT {LedgerColumns} FROM Ledger WHERE SourceCalendarId = ?", sourceCalendarId).ConfigureAwait(false);
        return rows.Select(ToEntry).ToList();
    }

    public async Task UpsertAsync(LedgerEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.ExecuteAsync(
            $"INSERT OR REPLACE INTO Ledger ({LedgerColumns}) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            entry.OriginKey,
            entry.TargetCalendarId,
            entry.OriginProvider.ToString(),
            entry.SourceCalendarId,
            entry.OriginNativeId,
            entry.OriginStartUtc.UtcTicks,
            entry.TargetEventId,
            entry.SyncHash,
            entry.TargetLastModifiedUtc.UtcTicks,
            entry.LastSyncedUtc.UtcTicks,
            entry.Status.ToString()).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string originKey, string targetCalendarId, CancellationToken cancellationToken = default)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.ExecuteAsync("DELETE FROM Ledger WHERE OriginKey = ? AND TargetCalendarId = ?", originKey, targetCalendarId).ConfigureAwait(false);
    }

    public async Task AddRunAsync(SyncRunRecord run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        await db.ExecuteAsync(
            "INSERT OR REPLACE INTO SyncRun (Id, StartedUtcTicks, FinishedUtcTicks, DryRun, Status, Created, Updated, Deleted, Conflicts, Errors) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            run.Id.ToString(),
            run.StartedUtc.UtcTicks,
            run.FinishedUtc?.UtcTicks,
            run.DryRun ? 1 : 0,
            run.Status.ToString(),
            run.Created,
            run.Updated,
            run.Deleted,
            run.Conflicts,
            run.Errors).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SyncRunRecord>> GetRecentRunsAsync(int count, CancellationToken cancellationToken = default)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var rows = await db.QueryAsync<SyncRunRow>(
            "SELECT Id, StartedUtcTicks, FinishedUtcTicks, DryRun, Status, Created, Updated, Deleted, Conflicts, Errors FROM SyncRun ORDER BY StartedUtcTicks DESC LIMIT ?", count).ConfigureAwait(false);
        return rows.Select(r => new SyncRunRecord(
            Guid.Parse(r.Id),
            FromTicks(r.StartedUtcTicks),
            r.FinishedUtcTicks is { } f ? FromTicks(f) : null,
            r.DryRun != 0,
            Enum.Parse<SyncRunStatus>(r.Status),
            r.Created,
            r.Updated,
            r.Deleted,
            r.Conflicts,
            r.Errors)).ToList();
    }

    private static LedgerEntry ToEntry(LedgerRow r) => new(
        r.OriginKey,
        Enum.Parse<ProviderKind>(r.OriginProvider),
        r.SourceCalendarId,
        r.OriginNativeId,
        FromTicks(r.OriginStartUtcTicks),
        r.TargetCalendarId,
        r.TargetEventId,
        r.SyncHash,
        FromTicks(r.TargetLastModifiedUtcTicks),
        FromTicks(r.LastSyncedUtcTicks),
        Enum.Parse<LedgerStatus>(r.Status));

    private static DateTimeOffset FromTicks(long ticks) => new(ticks, TimeSpan.Zero);

    // Zeilenklassen für sqlite-net (Spaltennamen = Property-Namen). [Preserve] schützt sie vor dem iOS-Linker.
    [Preserve(AllMembers = true)]
    internal sealed class LedgerRow
    {
        public string OriginKey { get; set; } = string.Empty;

        public string TargetCalendarId { get; set; } = string.Empty;

        public string OriginProvider { get; set; } = string.Empty;

        public string? SourceCalendarId { get; set; }

        public string OriginNativeId { get; set; } = string.Empty;

        public long OriginStartUtcTicks { get; set; }

        public string TargetEventId { get; set; } = string.Empty;

        public string SyncHash { get; set; } = string.Empty;

        public long TargetLastModifiedUtcTicks { get; set; }

        public long LastSyncedUtcTicks { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    [Preserve(AllMembers = true)]
    internal sealed class SyncRunRow
    {
        public string Id { get; set; } = string.Empty;

        public long StartedUtcTicks { get; set; }

        public long? FinishedUtcTicks { get; set; }

        public int DryRun { get; set; }

        public string Status { get; set; } = string.Empty;

        public int Created { get; set; }

        public int Updated { get; set; }

        public int Deleted { get; set; }

        public int Conflicts { get; set; }

        public int Errors { get; set; }
    }
}
