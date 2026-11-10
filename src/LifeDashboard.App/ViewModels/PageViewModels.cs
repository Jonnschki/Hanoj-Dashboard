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
    IOptions<MarketDataOptions> marketOptions) : ObservableObject
{
    private DailyDigest? _digest;

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
        var quotes = await markets.GetQuotesAsync(marketOptions.Value.Watchlist.ToList());
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

        Headline = _digest.ToNotificationText();
        Status = $"Märkte: {markets.Name}";
    }

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
    SyncRules rules,
    IOptions<MarketDataOptions> marketOptions,
    IClock clock) : ObservableObject
{
    public ObservableCollection<AccountItem> Accounts { get; } = [];

    public ObservableCollection<CalendarChoice> Calendars { get; } = [];

    public IReadOnlyList<MirrorDetailLevel> DetailLevels { get; } = Enum.GetValues<MirrorDetailLevel>();

    [ObservableProperty]
    public partial MirrorDetailLevel PrivateToWork { get; set; } = rules.Rules.First(r => r.From == CalendarCategory.Private && r.To == CalendarCategory.Work).Level;

    [ObservableProperty]
    public partial TimeSpan DigestTime { get; set; } = sync.Settings.DigestTime.ToTimeSpan();

    [ObservableProperty]
    public partial string Watchlist { get; set; } = string.Join(", ", marketOptions.Value.Watchlist);

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

        Calendars.Clear();
        foreach (var (_, calendar) in await sync.GetCalendarsAsync())
        {
            var choice = new CalendarChoice(calendar, sync.SelectedCalendarIds.Contains(calendar.Id));
            choice.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CalendarChoice.IsSelected) && calendar.IsWritable)
                {
                    if (choice.IsSelected)
                    {
                        sync.SelectedCalendarIds.Add(calendar.Id);
                    }
                    else
                    {
                        sync.SelectedCalendarIds.Remove(calendar.Id);
                    }
                }
            };
            Calendars.Add(choice);
        }
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
