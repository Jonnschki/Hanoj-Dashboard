using LifeDashboard.Core.Abstractions;

namespace LifeDashboard.Core.Sync;

/// <summary>
/// Ergebnis eines Sync-Laufs. Im Dry-Run beschreibt es, was geändert WÜRDE.
/// </summary>
public sealed record DryRunReport(
    Guid RunId,
    bool DryRun,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    IReadOnlyList<SyncAction> Actions,
    IReadOnlyList<SyncLogEntry> Log)
{
    public int Created => Count(SyncActionKind.Create);

    public int Updated => Count(SyncActionKind.Update) + Actions.Count(a => a.Kind == SyncActionKind.Conflict && a.ConflictOverwrites);

    public int Deleted => Count(SyncActionKind.Delete);

    public int Unchanged => Count(SyncActionKind.Unchanged);

    public int Conflicts => Count(SyncActionKind.Conflict);

    public int PendingDeletes => Count(SyncActionKind.PendingDeleteConfirmation);

    public int Errors => Actions.Count(a => a.Outcome == SyncActionOutcome.Failed);

    private int Count(SyncActionKind kind) => Actions.Count(a => a.Kind == kind);
}
