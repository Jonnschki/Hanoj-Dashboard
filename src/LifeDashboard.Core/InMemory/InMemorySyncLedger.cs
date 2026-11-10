using System.Collections.Concurrent;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.InMemory;

/// <summary>Ledger im Speicher. Für Tests und den Demo-Modus; die App nutzt ab Phase 1 SQLite.</summary>
public sealed class InMemorySyncLedger : ISyncLedger
{
    private readonly ConcurrentDictionary<(string OriginKey, string Target), LedgerEntry> _entries = new();
    private readonly ConcurrentBag<SyncRunRecord> _runs = [];

    /// <summary>Alle Einträge (für Assertions).</summary>
    public IReadOnlyCollection<LedgerEntry> Entries => _entries.Values.ToList();

    public Task<LedgerEntry?> FindAsync(string originKey, string targetCalendarId, CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.GetValueOrDefault((originKey, targetCalendarId)));

    public Task<LedgerEntry?> FindByTargetEventAsync(string targetCalendarId, string targetEventId, CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.Values.FirstOrDefault(e => e.TargetCalendarId == targetCalendarId && e.TargetEventId == targetEventId));

    public Task<IReadOnlyList<LedgerEntry>> GetBySourceCalendarAsync(string sourceCalendarId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<LedgerEntry>>(_entries.Values.Where(e => e.SourceCalendarId == sourceCalendarId).ToList());

    public Task UpsertAsync(LedgerEntry entry, CancellationToken cancellationToken = default)
    {
        _entries[(entry.OriginKey, entry.TargetCalendarId)] = entry;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string originKey, string targetCalendarId, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove((originKey, targetCalendarId), out _);
        return Task.CompletedTask;
    }

    public Task AddRunAsync(SyncRunRecord run, CancellationToken cancellationToken = default)
    {
        _runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SyncRunRecord>> GetRecentRunsAsync(int count, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SyncRunRecord>>(_runs.OrderByDescending(r => r.StartedUtc).Take(count).ToList());
}
