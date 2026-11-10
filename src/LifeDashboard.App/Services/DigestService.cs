using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Digest;
using LifeDashboard.Core.Domain;
using Microsoft.Extensions.Logging;

namespace LifeDashboard.App.Services;

/// <summary>Inhalt des Daily Digest inkl. Schlagzeilen.</summary>
public sealed record DigestContent(
    DailyDigest Digest,
    IReadOnlyList<(CalendarInfo Calendar, CalendarEvent Event)> Events,
    IReadOnlyList<NewsStory> Headlines,
    string MarketSource)
{
    /// <summary>Text für die Benachrichtigung.</summary>
    public string NotificationText => Digest.ToNotificationText() + (Headlines.Count > 0 ? $" · Top: {Headlines[0].Headline.Title}" : string.Empty);
}

/// <summary>Stellt den Digest zusammen (für Digest-Seite und Zeitplaner). Netzwerkfehler führen zu einem Digest ohne den jeweiligen Teil.</summary>
public sealed class DigestService(SyncCoordinator sync, DailyDigestBuilder builder, IMarketDataProvider markets, NewsCoordinator news, ILogger<DigestService> logger)
{
    public async Task<DigestContent> BuildAsync(CancellationToken ct = default)
    {
        var events = await sync.GetEventsAsync(builder.TodayRange(), ct);
        var quotes = await markets.GetQuotesAsync(await sync.GetWatchlistAsync(ct), ct);

        IReadOnlyList<NewsStory> headlines = [];
        try
        {
            headlines = (await news.GetStoriesAsync(ct: ct)).Take(5).ToList();
        }
        catch (HttpRequestException ex)
        {
            logger.LogInformation("Digest ohne Schlagzeilen: {Error}", ex.HttpRequestError);
        }

        var digest = builder.Build(events.Select(e => e.Event), quotes.IsSuccess ? quotes.Value : [], markets.Name);
        var todays = events.Where(e => digest.TodaysEvents.Contains(e.Event)).ToList();
        return new DigestContent(digest, todays, headlines, quotes.IsSuccess ? markets.Name : "Kurse nicht verfügbar");
    }
}

/// <summary>
/// Zeigt den Digest einmal täglich zur eingestellten Uhrzeit als Benachrichtigung – solange die App läuft.
/// Wird die App erst nach der Uhrzeit geöffnet, kommt der Digest beim Start (einmal pro Tag).
/// Bewusst kein Hintergrunddienst (Vorgabe: App arbeitet auf Knopfdruck).
/// </summary>
public sealed class DigestScheduler(DigestService digest, SyncCoordinator sync, INotificationService notifications, IClock clock, ILogger<DigestScheduler> logger)
{
    private const string LastShownKey = "digest.lastShownDate";
    private IDispatcherTimer? _timer;

    public void Start(IDispatcher dispatcher)
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        try
        {
            var settings = await sync.GetUserSettingsAsync();
            var local = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone);
            var today = DateOnly.FromDateTime(local.DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (TimeOnly.FromDateTime(local.DateTime) < settings.DigestTime || Preferences.Default.Get(LastShownKey, string.Empty) == today)
            {
                return;
            }

            Preferences.Default.Set(LastShownKey, today);
            var content = await digest.BuildAsync();
            await notifications.ShowAsync("Dein Tag", content.NotificationText);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            logger.LogWarning("Digest-Benachrichtigung fehlgeschlagen: {Type}", ex.GetType().Name);
        }
    }
}
