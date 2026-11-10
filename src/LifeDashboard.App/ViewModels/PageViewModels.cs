using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LifeDashboard.App.Services;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Digest;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.Sync;
using LifeDashboard.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LifeDashboard.App.ViewModels;

/// <summary>Gruppe von Terminen pro Tag.</summary>
public sealed class DayGroup(string title, IEnumerable<EventItem> items) : List<EventItem>(items)
{
    public string Title { get; } = title;
}

/// <summary>Kalenderansicht: nächste 14 Tage aus allen Kalendern (inkl. read-only).</summary>
public sealed partial class CalendarViewModel(SyncCoordinator sync, IClock clock) : ObservableObject
{
    public ObservableCollection<DayGroup> Days { get; } = [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        var events = await sync.GetEventsAsync(DateRange.Around(clock.UtcNow, TimeSpan.FromDays(1), TimeSpan.FromDays(14)));
        Days.Clear();
        foreach (var group in events.GroupBy(e => e.Event.Schedule.SortKeyUtc.ToLocalTime().Date))
        {
            Days.Add(new DayGroup(group.Key.ToString("dddd, d. MMMM", CultureInfo.GetCultureInfo("de-AT")), group.Select(x => EventItem.From(x.Calendar, x.Event, includeDate: false))));
        }
    }
}

/// <summary>Sportseite: Aktivitäten der letzten 30 Tage.</summary>
public sealed partial class SportViewModel(IActivityProvider activities, IClock clock) : ObservableObject
{
    public ObservableCollection<ActivityItem> Items { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var result = await activities.GetActivitiesAsync(clock.UtcNow.AddDays(-30));
        Items.Clear();
        if (result.IsFailure)
        {
            Summary = result.Error.Message;
            return;
        }

        foreach (var a in result.Value.OrderByDescending(a => a.StartUtc))
        {
            Items.Add(ActivityItem.From(a));
        }

        Summary = $"{result.Value.Count} Aktivitäten · {result.Value.Sum(a => a.DistanceMeters) / 1000d:0.0} km in 30 Tagen";
    }
}

/// <summary>Daily Digest: heutige Termine + Märkte.</summary>
public sealed partial class DigestViewModel(
    SyncCoordinator sync,
    DailyDigestBuilder builder,
    IMarketDataProvider markets,
    INotificationService notifications,
    NewsCoordinator news) : ObservableObject
{
    private DailyDigest? _digest;

    public ObservableCollection<StoryItem> Headlines { get; } = [];

    public ObservableCollection<EventItem> Events { get; } = [];

    public ObservableCollection<QuoteItem> Quotes { get; } = [];

    [ObservableProperty]
    public partial string Headline { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [RelayCommand]
    private async Task BuildAsync()
    {
        var events = await sync.GetEventsAsync(builder.TodayRange());
        var quotes = await markets.GetQuotesAsync(await sync.GetWatchlistAsync());
        _digest = builder.Build(events.Select(e => e.Event), quotes.IsSuccess ? quotes.Value : [], markets.Name);

        Events.Clear();
        foreach (var e in _digest.TodaysEvents)
        {
            Events.Add(EventItem.From(events.First(x => x.Event == e).Calendar, e, includeDate: false));
        }

        Quotes.Clear();
        foreach (var q in _digest.Quotes)
        {
            Quotes.Add(QuoteItem.From(q));
        }

        Headlines.Clear();
        try
        {
            foreach (var story in (await news.GetStoriesAsync()).Take(5))
            {
                Headlines.Add(StoryItem.From(story, news));
            }
        }
        catch (HttpRequestException)
        {
            // Ohne Netz bleibt der Digest ohne Schlagzeilen.
        }

        Headline = _digest.ToNotificationText() + (Headlines.Count > 0 ? $" · Top: {Headlines[0].Title}" : string.Empty);
        Status = $"Märkte: {markets.Name}";
    }

    [RelayCommand]
    private static Task OpenStoryAsync(StoryItem item) => Launcher.Default.OpenAsync(item.Link);

    [RelayCommand]
    private async Task NotifyAsync()
    {
        if (_digest is null)
        {
            await BuildAsync();
        }

        await notifications.ShowAsync("Dein Tag", _digest!.ToNotificationText());
        Status = "Benachrichtigung gesendet (Stub: siehe Debug-Log, echte Notification ab Phase 7).";
    }
}

/// <summary>Einstellungen: Konten, Kalenderauswahl, Regeln, Digest, Watchlist, Export.</summary>
public sealed partial class SettingsViewModel(
    SyncCoordinator sync,
    IEnumerable<IAccountConnector> connectors,
    IEnumerable<IExportService> exporters,
    IOptions<MarketDataOptions> marketOptions,
    IClock clock) : ObservableObject
{
    public ObservableCollection<AccountItem> Accounts { get; } = [];

    public ObservableCollection<CalendarChoice> Calendars { get; } = [];

    /// <summary>Eigene Kalender als mögliches Ziel für Sportaktivitäten.</summary>
    public ObservableCollection<CalendarOption> SportTargets { get; } = [];

    public IReadOnlyList<MirrorDetailLevel> DetailLevels { get; } = Enum.GetValues<MirrorDetailLevel>();

    /// <summary>Abonnierte ICS-Kalender fremder Firmen.</summary>
    public ObservableCollection<IcsItem> IcsSubscriptions { get; } = [];

    public IReadOnlyList<CalendarCategory> Categories { get; } = Enum.GetValues<CalendarCategory>();

    [ObservableProperty]
    public partial string NewIcsName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewIcsUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CalendarCategory NewIcsCategory { get; set; } = CalendarCategory.Work;

    [ObservableProperty]
    public partial string IcsStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddIcsCommand))]
    public partial bool IsAddingIcs { get; set; }

    /// <summary>Platzhalter im Watchlist-Feld: die Liste aus appsettings.json.</summary>
    public string WatchlistPlaceholder { get; } = string.Join(", ", marketOptions.Value.Watchlist);

    [ObservableProperty]
    public partial MirrorDetailLevel PrivateToWork { get; set; } = MirrorDetailLevel.BusyOnly;

    [ObservableProperty]
    public partial CalendarOption? SportTarget { get; set; }

    [ObservableProperty]
    public partial TimeSpan DigestTime { get; set; }

    [ObservableProperty]
    public partial string Watchlist { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        Accounts.Clear();
        foreach (var connector in connectors)
        {
            Accounts.Add(new AccountItem(connector, await connector.IsConnectedAsync()));
        }

        var settings = await sync.GetUserSettingsAsync();
        var selected = await sync.GetSelectedCalendarIdsAsync();
        var sportTargetId = await sync.GetActivityTargetCalendarIdAsync();

        Calendars.Clear();
        SportTargets.Clear();
        foreach (var (_, calendar) in await sync.GetCalendarsAsync())
        {
            Calendars.Add(new CalendarChoice(calendar, selected.Contains(calendar.Id)));
            if (calendar.IsWritable)
            {
                SportTargets.Add(new CalendarOption(calendar.Id, calendar.Name));
            }
        }

        IcsSubscriptions.Clear();
        foreach (var s in settings.IcsSubscriptions)
        {
            IcsSubscriptions.Add(IcsItem.From(s));
        }

        SportTarget = SportTargets.FirstOrDefault(o => o.Id == sportTargetId);
        PrivateToWork = settings.PrivateWorkDetailLevel;
        DigestTime = settings.DigestTime.ToTimeSpan();
        Watchlist = string.Join(", ", settings.Watchlist);
        Status = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var current = await sync.GetUserSettingsAsync();
        var updated = current with
        {
            HasCalendarSelection = true,
            SelectedCalendarIds = Calendars.Where(c => c.CanSelect && c.IsSelected).Select(c => c.Calendar.Id).ToList(),
            ActivityTargetCalendarId = SportTarget?.Id,
            PrivateWorkDetailLevel = PrivateToWork,
            DigestTime = TimeOnly.FromTimeSpan(DigestTime),
            Watchlist = UserSettings.ParseWatchlist(Watchlist),
        };

        await sync.SaveUserSettingsAsync(updated);
        Watchlist = string.Join(", ", updated.Watchlist);
        Status = $"Gespeichert ({DateTime.Now:HH:mm}).";
    }

    private bool CanAddIcs() => !IsAddingIcs;

    [RelayCommand(CanExecute = nameof(CanAddIcs))]
    private async Task AddIcsAsync()
    {
        IsAddingIcs = true;
        IcsStatus = "Link wird geprüft …";
        try
        {
            var name = NewIcsName;
            var result = await sync.AddIcsSubscriptionAsync(name, NewIcsUrl, NewIcsCategory);
            if (result.IsFailure && result.Error.Code is "ics.url" or "ics.name")
            {
                IcsStatus = result.Error.Message; // Eingabefehler: nichts gespeichert
                return;
            }

            NewIcsName = string.Empty;
            NewIcsUrl = string.Empty; // Geheimen Link nicht im Eingabefeld stehen lassen
            IcsStatus = result.IsSuccess
                ? $"„{name.Trim()}“ hinzugefügt: {result.Value} Termine in den nächsten 30 Tagen."
                : $"„{name.Trim()}“ gespeichert, aber der Abruf schlug fehl: {result.Error.Message}";
            await LoadAsync();
        }
        finally
        {
            IsAddingIcs = false;
        }
    }

    [RelayCommand]
    private async Task RemoveIcsAsync(IcsItem item)
    {
        await sync.RemoveIcsSubscriptionAsync(item.Id);
        IcsStatus = $"„{item.Name}“ entfernt.";
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleAccountAsync(AccountItem item)
    {
        if (item.IsConnected)
        {
            await item.Connector.DisconnectAsync();
            item.IsConnected = false;
            return;
        }

        var result = await item.Connector.ConnectAsync();
        item.IsConnected = result.IsSuccess;
        Status = result.IsSuccess ? $"{item.Name} verbunden." : result.Error.Message;
    }

    [RelayCommand]
    private Task ExportExcelAsync() => ExportAsync(ExportFormat.Xlsx);

    [RelayCommand]
    private Task ExportCsvAsync() => ExportAsync(ExportFormat.Csv);

    private async Task ExportAsync(ExportFormat format)
    {
        var exporter = exporters.First(e => e.Format == format);
        var events = await sync.GetEventsAsync(DateRange.Around(clock.UtcNow, TimeSpan.FromDays(30), TimeSpan.FromDays(60)));
        using var stream = new MemoryStream();
        await exporter.ExportEventsAsync(events.Select(e => e.Event), stream);
        stream.Position = 0;

        var result = await FileSaver.Default.SaveAsync($"Termine_{DateTime.Now:yyyyMMdd}{exporter.FileExtension}", stream);
        Status = result.IsSuccessful ? $"Gespeichert: {result.FilePath}" : $"Export abgebrochen: {result.Exception?.Message}";
    }
}

/// <summary>Konto in den Einstellungen.</summary>
public sealed partial class AccountItem(IAccountConnector connector, bool isConnected) : ObservableObject
{
    public IAccountConnector Connector { get; } = connector;

    public string Name => Connector.Kind switch
    {
        ProviderKind.Microsoft => "Microsoft 365 / Outlook",
        ProviderKind.Google => "Google Kalender",
        ProviderKind.Strava => "Strava",
        _ => Connector.Kind.ToString(),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsConnected { get; set; } = isConnected;

    public string ButtonText => IsConnected ? "Trennen" : "Verbinden";

    public string StatusText => IsConnected ? "Verbunden" : "Nicht verbunden";
}
