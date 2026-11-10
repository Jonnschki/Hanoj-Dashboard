using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Ics;
using LifeDashboard.Core.Sync;
using LifeDashboard.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LifeDashboard.App.Services;

/// <summary>
/// App-weiter Zustand rund um den Sync (Kalender, gespeicherte Nutzereinstellungen, letzter Report).
/// Wird von Dashboard, SyncPage und Settings gemeinsam genutzt. Keine Business-Logik – die steckt in der SyncEngine.
/// </summary>
public sealed class SyncCoordinator(
    SyncEngine engine,
    IEnumerable<ICalendarProvider> providers,
    ISyncLedger ledger,
    IUserSettingsStore settingsStore,
    ISecureTokenStore secrets,
    IClock clock,
    IOptions<SyncSettings> syncSettings,
    IOptions<MarketDataOptions> marketOptions)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<(ICalendarProvider Provider, CalendarInfo Calendar)>? _calendars;
    private UserSettings? _userSettings;

    /// <summary>Wird ausgelöst, wenn ein Lauf fertig ist.</summary>
    public event EventHandler<DryRunReport>? RunCompleted;

    public DryRunReport? LastReport { get; private set; }

    /// <summary>Technische Sync-Einstellungen aus appsettings.json.</summary>
    public SyncSettings Settings { get; } = syncSettings.Value;

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
        return list;
    }

    /// <summary>Kalenderliste neu laden lassen (z. B. nach dem Verbinden eines Kontos).</summary>
    public void InvalidateCalendars() => _calendars = null;

    /// <summary>Gespeicherte Nutzereinstellungen (gecacht).</summary>
    public async Task<UserSettings> GetUserSettingsAsync(CancellationToken ct = default)
        => _userSettings ??= await settingsStore.LoadAsync(ct);

    /// <summary>Speichert die Nutzereinstellungen.</summary>
    public async Task SaveUserSettingsAsync(UserSettings settings, CancellationToken ct = default)
    {
        await settingsStore.SaveAsync(settings, ct);
        _userSettings = settings;
        _calendars = null; // z. B. neue ICS-Abos → Kalenderliste neu laden
    }

    /// <summary>
    /// Fügt ein ICS-Abo hinzu (URL in den sicheren Speicher, Metadaten in die Einstellungen)
    /// und testet den Link sofort. Liefert die Anzahl der Termine der nächsten 30 Tage.
    /// Das Abo bleibt auch bei einem fehlgeschlagenen Test gespeichert (z. B. kurz offline) und kann entfernt werden.
    /// </summary>
    public async Task<Result<int>> AddIcsSubscriptionAsync(string? name, string? url, CalendarCategory category, CancellationToken ct = default)
    {
        var uri = IcsSubscription.NormalizeUrl(url);
        if (uri is null)
        {
            return Result.Failure<int>(new Error("ics.url", "Bitte einen gültigen https:// oder webcal:// Link eingeben."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<int>(new Error("ics.name", "Bitte einen Namen für den Kalender eingeben."));
        }

        var subscription = new IcsSubscription(Guid.NewGuid().ToString("N")[..12], name.Trim(), category);
        await secrets.SetAsync(subscription.SecretKey, uri.ToString());
        var settings = await GetUserSettingsAsync(ct);
        await SaveUserSettingsAsync(settings with { IcsSubscriptions = [.. settings.IcsSubscriptions, subscription] }, ct);

        var entry = (await GetCalendarsAsync(ct)).FirstOrDefault(x => x.Calendar.Id == IcsCalendarProvider.CalendarIdFor(subscription.Id));
        if (entry.Provider is null)
        {
            return Result.Failure<int>(new Error("ics.provider", "ICS-Abos sind in dieser Konfiguration nicht verfügbar."));
        }

        var events = await entry.Provider.GetEventsAsync(entry.Calendar, DateRange.Around(clock.UtcNow, TimeSpan.Zero, TimeSpan.FromDays(30)), ct);
        return events.IsSuccess ? Result.Success(events.Value.Count) : Result.Failure<int>(events.Error);
    }

    /// <summary>Entfernt ein ICS-Abo inklusive der gespeicherten URL.</summary>
    public async Task RemoveIcsSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        var settings = await GetUserSettingsAsync(ct);
        await SaveUserSettingsAsync(settings with { IcsSubscriptions = settings.IcsSubscriptions.Where(s => s.Id != subscriptionId).ToList() }, ct);
        await secrets.RemoveAsync(IcsSubscription.SecretKeyFor(subscriptionId));
    }

    /// <summary>Watchlist: eigene aus den Einstellungen, sonst die aus appsettings.json.</summary>
    public async Task<IReadOnlyList<string>> GetWatchlistAsync(CancellationToken ct = default)
        => (await GetUserSettingsAsync(ct)).EffectiveWatchlist(marketOptions.Value.Watchlist);

    /// <summary>
    /// Kalender, die gespiegelt werden: gespeicherte Auswahl, oder (solange nie gespeichert) alle eigenen.
    /// Nur eigene Kalender – fremde werden hier zusätzlich herausgefiltert.
    /// </summary>
    public async Task<IReadOnlySet<string>> GetSelectedCalendarIdsAsync(CancellationToken ct = default)
    {
        var settings = await GetUserSettingsAsync(ct);
        var own = (await GetCalendarsAsync(ct)).Where(x => x.Calendar.IsWritable).Select(x => x.Calendar.Id).ToHashSet();
        return settings.HasCalendarSelection ? settings.SelectedCalendarIds.Where(own.Contains).ToHashSet() : own;
    }

    /// <summary>Sport-Zielkalender: gespeicherte Wahl, sonst erster eigener Kalender mit "Sport" im Namen.</summary>
    public async Task<string?> GetActivityTargetCalendarIdAsync(CancellationToken ct = default)
    {
        var settings = await GetUserSettingsAsync(ct);
        var calendars = await GetCalendarsAsync(ct);
        if (settings.ActivityTargetCalendarId is { } id && calendars.Any(x => x.Calendar.Id == id && x.Calendar.IsWritable))
        {
            return id;
        }

        return calendars.Select(x => x.Calendar).FirstOrDefault(c => c.IsWritable && c.Name.Contains("Sport", StringComparison.OrdinalIgnoreCase))?.Id;
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

    public async Task<IReadOnlyList<SyncRunRecord>> GetRecentRunsAsync(int count, CancellationToken ct = default)
        => await ledger.GetRecentRunsAsync(count, ct);

    /// <summary>Startet einen Lauf mit den gespeicherten Einstellungen. Parallele Läufe werden verhindert.</summary>
    public async Task<DryRunReport> RunAsync(bool dryRun, bool confirmDeletes, IProgress<SyncProgress>? progress, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var settings = await GetUserSettingsAsync(ct);
            var selected = await GetSelectedCalendarIdsAsync(ct);
            var activityTarget = await GetActivityTargetCalendarIdAsync(ct);
            var now = clock.UtcNow;

            var request = new SyncRequest
            {
                // Der Sport-Zielkalender wird gefüllt, aber nicht in andere Kalender gespiegelt.
                MirrorCalendarIds = selected.Where(id => id != activityTarget).ToList(),
                Window = DateRange.Around(now, TimeSpan.FromDays(Settings.WindowPastDays), TimeSpan.FromDays(Settings.WindowFutureDays)),
                DryRun = dryRun,
                ConfirmDeletes = confirmDeletes,
                ActivityTargetCalendarId = activityTarget,
                ActivitiesSinceUtc = now.AddDays(-14),
                Rules = settings.ToRules(),
            };

            var report = await engine.RunAsync(request, progress, ct);
            LastReport = report;
            RunCompleted?.Invoke(this, report);
            return report;
        }
        finally
        {
            _gate.Release();
        }
    }
}
