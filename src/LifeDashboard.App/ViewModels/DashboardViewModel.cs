using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LifeDashboard.App.Services;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Digest;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.App.ViewModels;

/// <summary>Startseite mit Kacheln. Nur Orchestrierung – Daten kommen aus Services.</summary>
public sealed partial class DashboardViewModel(
    SyncCoordinator sync,
    DailyDigestBuilder digest,
    IActivityProvider activities,
    IMarketDataProvider markets,
    NewsCoordinator news,
    IClock clock) : ObservableObject
{
    public ObservableCollection<StoryItem> Headlines { get; } = [];

    public ObservableCollection<EventItem> Today { get; } = [];

    public ObservableCollection<EventItem> Upcoming { get; } = [];

    public ObservableCollection<QuoteItem> Quotes { get; } = [];

    [ObservableProperty]
    public partial string Greeting { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SportSummary { get; set; } = "–";

    [ObservableProperty]
    public partial string SportDetails { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MarketSource { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SyncStatus { get; set; } = "Noch kein Sync";

    [ObservableProperty]
    public partial string SyncDetails { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Schlagzeilen parallel laden (Netzwerk), Rest der Kacheln wartet nicht darauf.
            var headlinesTask = LoadHeadlinesAsync();

            var local = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone);
            Greeting = $"{(local.Hour < 11 ? "Guten Morgen" : local.Hour < 18 ? "Hallo" : "Guten Abend")} · {local.ToString("dddd, d. MMMM", CultureInfo.GetCultureInfo("de-AT"))}";

            var todayRange = digest.TodayRange();
            var events = await sync.GetEventsAsync(new DateRange(todayRange.StartUtc, todayRange.StartUtc.AddDays(7)));
            Replace(Today, events.Where(x => x.Event.Schedule.SortKeyUtc < todayRange.EndUtc).Select(x => EventItem.From(x.Calendar, x.Event, includeDate: false)));
            Replace(Upcoming, events.Where(x => x.Event.Schedule.SortKeyUtc >= todayRange.EndUtc).Take(5).Select(x => EventItem.From(x.Calendar, x.Event, includeDate: true)));

            var week = await activities.GetActivitiesAsync(clock.UtcNow.AddDays(-7));
            if (week.IsSuccess)
            {
                var km = week.Value.Sum(a => a.DistanceMeters) / 1000d;
                var time = TimeSpan.FromSeconds(week.Value.Sum(a => a.MovingTime.TotalSeconds));
                SportSummary = $"{km:0.0} km";
                SportDetails = $"{week.Value.Count} Aktivitäten · {time:h\\:mm} h";
            }

            var quotes = await markets.GetQuotesAsync(await sync.GetWatchlistAsync());
            if (quotes.IsSuccess)
            {
                Replace(Quotes, quotes.Value.Select(QuoteItem.From));
            }

            MarketSource = quotes.IsSuccess ? $"Quelle: {markets.Name}" : $"Kurse nicht verfügbar: {quotes.Error.Message}";
            await UpdateSyncStatusAsync();
            await headlinesTask;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHeadlinesAsync()
    {
        try
        {
            var stories = await news.GetStoriesAsync();
            Replace(Headlines, stories.Take(6).Select(s => StoryItem.From(s, news)));
        }
        catch (HttpRequestException)
        {
            Headlines.Clear();
        }
    }

    [RelayCommand]
    private static Task OpenStoryAsync(StoryItem item) => Launcher.Default.OpenAsync(item.Link);

    [RelayCommand]
    private static Task OpenNewsAsync() => Shell.Current.GoToAsync("//news");

    [RelayCommand]
    private static Task OpenSportAsync() => DeviceInfo.Idiom == DeviceIdiom.Desktop
        ? Shell.Current.GoToAsync("//sport")
        : Shell.Current.GoToAsync("sportdetail");

    [RelayCommand]
    private static Task RefreshAsync() => Shell.Current.GoToAsync("//sync?autostart=true");

    [RelayCommand]
    private static Task OpenSettingsAsync() => Shell.Current.GoToAsync("settings");

    private async Task UpdateSyncStatusAsync()
    {
        var last = (await sync.GetRecentRunsAsync(1)).FirstOrDefault();
        if (last is null)
        {
            return;
        }

        SyncStatus = $"{(last.DryRun ? "Dry-Run" : "Sync")} {last.StartedUtc.ToLocalTime():dd.MM. HH:mm} · {last.Status}";
        SyncDetails = $"{last.Created} neu · {last.Updated} aktualisiert · {last.Deleted} gelöscht · {last.Conflicts} Konflikte";
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
