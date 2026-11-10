using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.Sync;
using LifeDashboard.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LifeDashboard.App.Services;

/// <summary>
/// App-weiter Zustand rund um den Sync (Kalenderauswahl, letzter Report).
/// Wird von Dashboard, SyncPage und Settings gemeinsam genutzt. Keine Business-Logik – die steckt in der SyncEngine.
/// </summary>
public sealed class SyncCoordinator(
    SyncEngine engine,
    IEnumerable<ICalendarProvider> providers,
    ISyncLedger ledger,
    IClock clock,
    IOptions<SyncSettings> settings)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<(ICalendarProvider Provider, CalendarInfo Calendar)>? _calendars;

    /// <summary>Wird ausgelöst, wenn ein Lauf fertig ist.</summary>
    public event EventHandler<DryRunReport>? RunCompleted;

    public DryRunReport? LastReport { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>IDs der eigenen Kalender, die gespiegelt werden (Default: alle eigenen).</summary>
    public HashSet<string> SelectedCalendarIds { get; } = [];

    /// <summary>Zielkalender für Sport (Default: erster eigener Kalender mit "Sport" im Namen).</summary>
    public string? ActivityTargetCalendarId { get; set; }

    public SyncSettings Settings { get; } = settings.Value;

    /// <summary>Alle bekannten Kalender (eigene und fremde), gecacht.</summary>
    public async Task<IReadOnlyList<(ICalendarProvider Provider, CalendarInfo Calendar)>> GetCalendarsAsync(CancellationToken ct = default)
    {
        if (_calendars is not null)
        {
            return _calendars;
        }

        var list = new List<(ICalendarProvider, CalendarInfo)>();
        foreach (var provider in providers)
        {
            var result = await provider.GetCalendarsAsync(ct);
            if (result.IsSuccess)
            {
                list.AddRange(result.Value.Select(c => (provider, c)));
            }
        }

        _calendars = list;
        if (SelectedCalendarIds.Count == 0)
        {
            foreach (var (_, c) in list.Where(x => x.Item2.IsWritable))
            {
                SelectedCalendarIds.Add(c.Id);
            }
        }

        ActivityTargetCalendarId ??= list.Select(x => x.Item2).FirstOrDefault(c => c.IsWritable && c.Name.Contains("Sport", StringComparison.OrdinalIgnoreCase))?.Id;
        return list;
    }

    /// <summary>Termine aller Kalender im Fenster (für Dashboard/Kalenderansicht).</summary>
    public async Task<IReadOnlyList<(CalendarInfo Calendar, CalendarEvent Event)>> GetEventsAsync(DateRange range, CancellationToken ct = default)
    {
        var result = new List<(CalendarInfo, CalendarEvent)>();
        foreach (var (provider, calendar) in await GetCalendarsAsync(ct))
        {
            var events = await provider.GetEventsAsync(calendar, range, ct);
            if (events.IsSuccess)
            {
                result.AddRange(events.Value.Select(e => (calendar, e)));
            }
        }

        return result.OrderBy(x => x.Item2.Schedule.SortKeyUtc).ToList();
    }

    public async Task<SyncRunRecord?> GetLastRunAsync(CancellationToken ct = default)
        => (await ledger.GetRecentRunsAsync(1, ct)).FirstOrDefault();

    /// <summary>Startet einen Lauf. Parallele Läufe werden verhindert.</summary>
    public async Task<DryRunReport> RunAsync(bool dryRun, bool confirmDeletes, IProgress<SyncProgress>? progress, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            IsRunning = true;
            await GetCalendarsAsync(ct);
            var now = clock.UtcNow;
            var request = new SyncRequest
            {
                MirrorCalendarIds = SelectedCalendarIds.ToList(),
                Window = DateRange.Around(now, TimeSpan.FromDays(Settings.WindowPastDays), TimeSpan.FromDays(Settings.WindowFutureDays)),
                DryRun = dryRun,
                ConfirmDeletes = confirmDeletes,
                ActivityTargetCalendarId = ActivityTargetCalendarId,
                ActivitiesSinceUtc = now.AddDays(-14),
            };

            // Der Sport-Zielkalender wird gefüllt, aber nicht in andere Kalender gespiegelt.
            if (ActivityTargetCalendarId is not null)
            {
                request = request with { MirrorCalendarIds = request.MirrorCalendarIds.Where(id => id != ActivityTargetCalendarId).ToList() };
            }

            var report = await engine.RunAsync(request, progress, ct);
            LastReport = report;
            RunCompleted?.Invoke(this, report);
            return report;
        }
        finally
        {
            IsRunning = false;
            _gate.Release();
        }
    }
}
