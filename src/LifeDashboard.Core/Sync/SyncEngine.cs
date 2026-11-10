using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using Microsoft.Extensions.Logging;

namespace LifeDashboard.Core.Sync;

/// <summary>
/// Spiegelt Termine zwischen den EIGENEN Kalendern und importiert Sportaktivitäten.
/// Ablauf: Kalender ermitteln → Termine laden → Plan erstellen → (falls kein Dry-Run) ausführen → Lauf protokollieren.
/// Siehe docs/SYNC_DESIGN.md.
/// </summary>
public sealed class SyncEngine
{
    private readonly IReadOnlyList<ICalendarProvider> _calendarProviders;
    private readonly IReadOnlyList<IActivityProvider> _activityProviders;
    private readonly ISyncLedger _ledger;
    private readonly IClock _clock;
    private readonly SyncRules _rules;
    private readonly ConflictResolver _conflicts;
    private readonly ILogger<SyncEngine> _logger;

    public SyncEngine(
        IEnumerable<ICalendarProvider> calendarProviders,
        IEnumerable<IActivityProvider> activityProviders,
        ISyncLedger ledger,
        IClock clock,
        SyncRules rules,
        ConflictResolver conflicts,
        ILogger<SyncEngine> logger)
    {
        _calendarProviders = calendarProviders.ToList();
        _activityProviders = activityProviders.ToList();
        _ledger = ledger;
        _clock = clock;
        _rules = rules;
        _conflicts = conflicts;
        _logger = logger;
    }

    /// <summary>Führt einen Sync-Lauf aus (oder nur die Planung im Dry-Run).</summary>
    public async Task<DryRunReport> RunAsync(SyncRequest request, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var run = new RunContext(Guid.NewGuid(), _clock.UtcNow, request, progress, _logger, _clock);

        run.Report(0.0, request.DryRun ? "Dry-Run gestartet" : "Sync gestartet");

        await DiscoverCalendarsAsync(run, cancellationToken).ConfigureAwait(false);
        run.Report(0.15, $"{run.MirrorCalendars.Count} Kalender für die Spiegelung");

        await LoadEventsAsync(run, cancellationToken).ConfigureAwait(false);
        run.Report(0.35, "Termine geladen");

        var actions = new List<SyncAction>();
        await PlanMirrorsAsync(run, actions, cancellationToken).ConfigureAwait(false);
        await PlanActivitiesAsync(run, actions, cancellationToken).ConfigureAwait(false);
        var plan = new SyncPlan(actions);
        run.Report(0.5, $"Plan: {plan.Writes.Count()} Änderungen");

        var executed = request.DryRun
            ? plan.Actions
            : await ExecuteAsync(run, plan, cancellationToken).ConfigureAwait(false);

        var report = new DryRunReport(run.Id, request.DryRun, run.StartedUtc, _clock.UtcNow, executed, run.Log);
        await _ledger.AddRunAsync(ToRecord(report), cancellationToken).ConfigureAwait(false);

        run.Report(1.0, $"Fertig: {report.Created} neu, {report.Updated} aktualisiert, {report.Deleted} gelöscht, {report.Conflicts} Konflikte, {report.Errors} Fehler");
        return report;
    }

    // ---------------------------------------------------------------- Discovery

    private async Task DiscoverCalendarsAsync(RunContext run, CancellationToken ct)
    {
        foreach (var provider in _calendarProviders)
        {
            var result = await provider.GetCalendarsAsync(ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                run.Warn($"Kalender von {provider.Account.DisplayName} nicht abrufbar: {result.Error}");
                continue;
            }

            foreach (var calendar in result.Value)
            {
                run.AllCalendars[calendar.Id] = (provider, calendar);
            }
        }

        foreach (var id in run.Request.MirrorCalendarIds.Distinct())
        {
            if (!run.AllCalendars.TryGetValue(id, out var entry))
            {
                run.Warn($"Kalender '{id}' nicht gefunden, wird übersprungen.");
                continue;
            }

            if (!entry.Calendar.IsWritable)
            {
                // Fremde Kalender nehmen weder als Quelle noch als Ziel an der Spiegelung teil.
                run.Warn($"Kalender '{entry.Calendar.Name}' ist read-only und wird nicht gespiegelt.");
                continue;
            }

            run.MirrorCalendars.Add(entry.Calendar);
        }
    }

    private async Task LoadEventsAsync(RunContext run, CancellationToken ct)
    {
        var toLoad = new Dictionary<string, DateRange>();
        foreach (var calendar in run.MirrorCalendars)
        {
            toLoad[calendar.Id] = run.Request.Window;
        }

        if (run.Request.ActivityTargetCalendarId is { } activityTarget && run.AllCalendars.ContainsKey(activityTarget))
        {
            var since = run.Request.ActivitiesSinceUtc ?? run.Request.Window.StartUtc;
            var start = since < run.Request.Window.StartUtc ? since : run.Request.Window.StartUtc;
            toLoad[activityTarget] = new DateRange(start, run.Request.Window.EndUtc);
        }

        foreach (var (calendarId, range) in toLoad)
        {
            var (provider, calendar) = run.AllCalendars[calendarId];
            var result = await provider.GetEventsAsync(calendar, range, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                // Wichtig: Ein Kalender, der nicht geladen werden konnte, darf NICHT als "leer" gelten,
                // sonst würden alle Spiegelungen gelöscht.
                run.Error($"Termine aus '{calendar.Name}' nicht abrufbar: {result.Error}");
                continue;
            }

            run.Loaded[calendarId] = new LoadedCalendar(calendar, range, result.Value.ToDictionary(e => e.Id));
        }
    }

    // ---------------------------------------------------------------- Planning

    private async Task PlanMirrorsAsync(RunContext run, List<SyncAction> actions, CancellationToken ct)
    {
        var loadedMirrors = run.MirrorCalendars.Where(c => run.Loaded.ContainsKey(c.Id)).ToList();

        foreach (var source in loadedMirrors)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var sourceEvent in run.Loaded[source.Id].Events.Values)
            {
                // Schleifenschutz 1: von der App erzeugte Termine (mit Marker) werden nie gespiegelt.
                if (sourceEvent.IsAppGenerated)
                {
                    continue;
                }

                // Schleifenschutz 2: Ledger kennt den Termin als Spiegelung (falls ein Anbieter den Marker verliert).
                if (await _ledger.FindByTargetEventAsync(source.Id, sourceEvent.Id, ct).ConfigureAwait(false) is not null)
                {
                    continue;
                }

                var originKey = OriginKey.ForEvent(source.Id, sourceEvent.Id);
                seen.Add(originKey);

                foreach (var target in loadedMirrors.Where(t => t.Id != source.Id))
                {
                    var entry = await _ledger.FindAsync(originKey, target.Id, ct).ConfigureAwait(false);
                    var level = _rules.Resolve(source, target, sourceEvent);

                    if (level == MirrorDetailLevel.None)
                    {
                        if (entry is not null)
                        {
                            actions.Add(PlanDelete(run, entry, target, "Regel schließt Termin aus"));
                        }

                        continue;
                    }

                    var mirror = EventMapping.ToMirror(sourceEvent, target, level, _rules);
                    var origin = new Origin(originKey, source.Provider, source.Id, sourceEvent.Id, sourceEvent.Schedule.SortKeyUtc, sourceEvent.LastModifiedUtc);
                    actions.Add(PlanUpsert(run, origin, target, mirror, entry));
                }
            }

            // Löschungen: Original verschwunden → Spiegelung löschen (nur, was das Ledger kennt).
            foreach (var entry in await _ledger.GetBySourceCalendarAsync(source.Id, ct).ConfigureAwait(false))
            {
                if (seen.Contains(entry.OriginKey) || !run.Request.Window.Contains(entry.OriginStartUtc))
                {
                    continue;
                }

                if (!run.Loaded.TryGetValue(entry.TargetCalendarId, out var target) || !target.Calendar.IsWritable)
                {
                    continue;
                }

                actions.Add(PlanDelete(run, entry, target.Calendar, "Original wurde gelöscht"));
            }
        }
    }

    private async Task PlanActivitiesAsync(RunContext run, List<SyncAction> actions, CancellationToken ct)
    {
        if (run.Request.ActivityTargetCalendarId is not { } targetId)
        {
            return;
        }

        if (!run.Loaded.TryGetValue(targetId, out var target))
        {
            run.Warn("Zielkalender für Sport nicht verfügbar, Import übersprungen.");
            return;
        }

        if (!target.Calendar.IsWritable)
        {
            run.Warn($"Sport-Zielkalender '{target.Calendar.Name}' ist read-only, Import übersprungen.");
            return;
        }

        var since = run.Request.ActivitiesSinceUtc ?? run.Request.Window.StartUtc;
        foreach (var provider in _activityProviders)
        {
            var result = await provider.GetActivitiesAsync(since, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                run.Error($"Aktivitäten von {provider.Kind} nicht abrufbar: {result.Error}");
                continue;
            }

            foreach (var activity in result.Value)
            {
                // Ledger-Schlüssel = Activity-ID → einmalig pro Aktivität, Änderungen werden aktualisiert.
                var originKey = OriginKey.ForActivity(activity.Provider, activity.Id);
                var entry = await _ledger.FindAsync(originKey, target.Calendar.Id, ct).ConfigureAwait(false);
                var mirror = EventMapping.FromActivity(activity, target.Calendar, run.StartedUtc);
                var origin = new Origin(originKey, activity.Provider, null, activity.Id, activity.StartUtc, run.StartedUtc);
                actions.Add(PlanUpsert(run, origin, target.Calendar, mirror, entry));
            }
        }
    }

    private SyncAction PlanUpsert(RunContext run, Origin origin, CalendarInfo target, CalendarEvent mirror, LedgerEntry? entry)
    {
        var hash = EventMapping.ComputeHash(mirror);
        var marked = mirror with { Origin = new OriginMarker(origin.Provider, origin.Key, hash) };

        SyncAction Action(SyncActionKind kind, string? existingId, string? reason = null) =>
            new(kind, origin.Key, origin.Provider, origin.SourceCalendarId, origin.NativeId, target, marked.Title, marked, existingId, reason)
            {
                OriginStartUtc = origin.StartUtc,
                SyncHash = hash,
            };

        if (entry is null)
        {
            return Action(SyncActionKind.Create, null);
        }

        var loaded = run.Loaded[target.Id];
        loaded.Events.TryGetValue(entry.TargetEventId, out var current);

        if (current is null && loaded.Range.Overlaps(mirror.Schedule.SortKeyUtc, mirror.Schedule.EndKeyUtc))
        {
            // Spiegelung wurde im Ziel manuell gelöscht → neu anlegen (Ledger-Eintrag wird ersetzt).
            return Action(SyncActionKind.Create, null, "Spiegelung fehlte im Zielkalender");
        }

        if (entry.SyncHash == hash)
        {
            return Action(SyncActionKind.Unchanged, entry.TargetEventId);
        }

        if (current is not null && ConflictResolver.TargetWasModified(entry, current))
        {
            var overwrite = _conflicts.SourceShouldWin(origin.ModifiedUtc, current.LastModifiedUtc);
            var reason = overwrite
                ? $"Spiegelung wurde manuell geändert; Original gewinnt ({_conflicts.Strategy})"
                : $"Spiegelung wurde manuell geändert; Änderung bleibt erhalten ({_conflicts.Strategy})";
            run.Warn($"Konflikt bei '{marked.Title}' in '{target.Name}': {reason}");
            return Action(SyncActionKind.Conflict, entry.TargetEventId, reason) with { ConflictOverwrites = overwrite };
        }

        return Action(SyncActionKind.Update, entry.TargetEventId);
    }

    private static SyncAction PlanDelete(RunContext run, LedgerEntry entry, CalendarInfo target, string reason)
    {
        var kind = run.Request.ConfirmDeletes ? SyncActionKind.Delete : SyncActionKind.PendingDeleteConfirmation;
        return new SyncAction(kind, entry.OriginKey, entry.OriginProvider, entry.SourceCalendarId, entry.OriginNativeId, target, entry.OriginNativeId, null, entry.TargetEventId, reason)
        {
            OriginStartUtc = entry.OriginStartUtc,
            SyncHash = entry.SyncHash,
        };
    }

    // ---------------------------------------------------------------- Execution

    private async Task<IReadOnlyList<SyncAction>> ExecuteAsync(RunContext run, SyncPlan plan, CancellationToken ct)
    {
        var results = new List<SyncAction>(plan.Actions.Count);
        var writes = plan.Writes.Count();
        var done = 0;

        foreach (var action in plan.Actions)
        {
            if (!action.IsWrite)
            {
                results.Add(action);
                continue;
            }

            ct.ThrowIfCancellationRequested();
            var (provider, calendar) = run.AllCalendars[action.Target.Id];

            // ReadOnlyCalendarException wird absichtlich NICHT gefangen: das wäre ein Programmierfehler.
            var writer = CalendarWriteGate.Open(provider, calendar);

            var executed = action.Kind switch
            {
                SyncActionKind.Create => await CreateAsync(writer, action, ct).ConfigureAwait(false),
                SyncActionKind.Update or SyncActionKind.Conflict => await UpdateAsync(writer, action, ct).ConfigureAwait(false),
                SyncActionKind.Delete => await DeleteAsync(writer, action, ct).ConfigureAwait(false),
                _ => action,
            };

            if (executed.Outcome == SyncActionOutcome.Failed)
            {
                run.Error($"{action.Kind} '{action.Title}' in '{action.Target.Name}' fehlgeschlagen: {executed.Error}");
            }

            results.Add(executed);
            run.Report(0.5 + (0.45 * ++done / Math.Max(writes, 1)), $"{action.Kind}: {action.Title}");
        }

        return results;
    }

    private async Task<SyncAction> CreateAsync(ICalendarWriter writer, SyncAction action, CancellationToken ct)
    {
        var result = await writer.CreateAsync(action.Mirror!, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return action with { Outcome = SyncActionOutcome.Failed, Error = result.Error.ToString() };
        }

        await _ledger.UpsertAsync(ToLedger(action, result.Value), ct).ConfigureAwait(false);
        return action with { Outcome = SyncActionOutcome.Succeeded };
    }

    private async Task<SyncAction> UpdateAsync(ICalendarWriter writer, SyncAction action, CancellationToken ct)
    {
        var result = await writer.UpdateAsync(action.ExistingTargetEventId!, action.Mirror!, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return action with { Outcome = SyncActionOutcome.Failed, Error = result.Error.ToString() };
        }

        await _ledger.UpsertAsync(ToLedger(action, result.Value), ct).ConfigureAwait(false);
        return action with { Outcome = SyncActionOutcome.Succeeded };
    }

    private async Task<SyncAction> DeleteAsync(ICalendarWriter writer, SyncAction action, CancellationToken ct)
    {
        var result = await writer.DeleteAsync(action.ExistingTargetEventId!, ct).ConfigureAwait(false);
        if (result.IsFailure && result.Error.Code != "not_found")
        {
            return action with { Outcome = SyncActionOutcome.Failed, Error = result.Error.ToString() };
        }

        await _ledger.RemoveAsync(action.OriginKey, action.Target.Id, ct).ConfigureAwait(false);
        return action with { Outcome = SyncActionOutcome.Succeeded };
    }

    private LedgerEntry ToLedger(SyncAction action, WriteReceipt receipt) => new(
        action.OriginKey,
        action.OriginProvider,
        action.SourceCalendarId,
        action.OriginNativeId,
        action.OriginStartUtc,
        action.Target.Id,
        receipt.EventId,
        action.SyncHash,
        receipt.LastModifiedUtc,
        _clock.UtcNow,
        LedgerStatus.Active);

    private static SyncRunRecord ToRecord(DryRunReport report)
    {
        var status = report.Errors > 0 ? SyncRunStatus.CompletedWithErrors : SyncRunStatus.Succeeded;
        if (report.Log.Any(l => l.Level == "Error") && report.Errors == 0)
        {
            status = SyncRunStatus.CompletedWithErrors;
        }

        return new SyncRunRecord(report.RunId, report.StartedUtc, report.FinishedUtc, report.DryRun, status, report.Created, report.Updated, report.Deleted, report.Conflicts, report.Errors);
    }

    // ---------------------------------------------------------------- Helper types

    private sealed record Origin(string Key, ProviderKind Provider, string? SourceCalendarId, string NativeId, DateTimeOffset StartUtc, DateTimeOffset ModifiedUtc);

    private sealed record LoadedCalendar(CalendarInfo Calendar, DateRange Range, IReadOnlyDictionary<string, CalendarEvent> Events);

    private sealed class RunContext(Guid id, DateTimeOffset startedUtc, SyncRequest request, IProgress<SyncProgress>? progress, ILogger logger, IClock clock)
    {
        public Guid Id { get; } = id;

        public DateTimeOffset StartedUtc { get; } = startedUtc;

        public SyncRequest Request { get; } = request;

        public Dictionary<string, (ICalendarProvider Provider, CalendarInfo Calendar)> AllCalendars { get; } = new(StringComparer.Ordinal);

        public List<CalendarInfo> MirrorCalendars { get; } = [];

        public Dictionary<string, LoadedCalendar> Loaded { get; } = new(StringComparer.Ordinal);

        public List<SyncLogEntry> Log { get; } = [];

        public void Report(double fraction, string message)
        {
            Add("Info", message);
            logger.LogInformation("{Message}", message);
            progress?.Report(new SyncProgress(fraction, message));
        }

        public void Warn(string message)
        {
            Add("Warning", message);
            logger.LogWarning("{Message}", message);
        }

        public void Error(string message)
        {
            Add("Error", message);
            logger.LogError("{Message}", message);
        }

        private void Add(string level, string message) => Log.Add(new SyncLogEntry(clock.UtcNow, level, message));
    }
}
