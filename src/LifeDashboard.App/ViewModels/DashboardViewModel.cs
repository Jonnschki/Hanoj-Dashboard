using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LifeDashboard.App.Services;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Digest;
using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LifeDashboard.App.ViewModels;

/// <summary>Startseite mit Kacheln. Nur Orchestrierung – Daten kommen aus Services.</summary>
public sealed partial class DashboardViewModel(
    SyncCoordinator sync,
    DailyDigestBuilder digest,
    IActivityProvider activities,
    IMarketDataProvider markets,
    IClock clock,
    IOptions<MarketDataOptions> marketOptions) : ObservableObject
{
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

            var quotes = await markets.GetQuotesAsync(marketOptions.Value.Watchlist.ToList());
            if (quotes.IsSuccess)
            {
                Replace(Quotes, quotes.Value.Select(QuoteItem.From));
            }

            MarketSource = $"Quelle: {markets.Name}";
            await UpdateSyncStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static Task RefreshAsync() => Shell.Current.GoToAsync("//sync?autostart=true");

    [RelayCommand]
    private static Task OpenSettingsAsync() => Shell.Current.GoToAsync("settings");

    private async Task UpdateSyncStatusAsync()
    {
        var last = await sync.GetLastRunAsync();
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
