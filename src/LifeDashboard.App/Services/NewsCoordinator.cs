using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.News;

namespace LifeDashboard.App.Services;

/// <summary>
/// Gemeinsamer Zugriff auf Schlagzeilen für Dashboard, News-Seite und Digest.
/// Der Provider cacht pro Feed (HTTP-Caching + Mindestabstand), wiederholte Aufrufe sind daher günstig.
/// </summary>
public sealed class NewsCoordinator(INewsProvider provider, HeadlineRanker ranker, IClock clock)
{
    public NewsSnapshot? Last { get; private set; }

    /// <summary>Lädt aktuelle Meldungen und liefert die gerankten Geschichten (optional gefiltert).</summary>
    public async Task<IReadOnlyList<NewsStory>> GetStoriesAsync(NewsCategory? category = null, NewsRegion? region = null, CancellationToken ct = default)
    {
        Last = await provider.GetLatestAsync(ct);
        var items = Last.Items
            .Where(i => category is null || i.Category == category)
            .Where(i => region is null || i.Region == region);
        return ranker.Rank(items, clock.UtcNow);
    }

    /// <summary>"vor 5 Min." / "vor 2 Std." für die Anzeige.</summary>
    public string Ago(DateTimeOffset publishedUtc)
    {
        var age = clock.UtcNow - publishedUtc;
        return age.TotalMinutes switch
        {
            < 1 => "gerade eben",
            < 60 => $"vor {(int)age.TotalMinutes} Min.",
            < 60 * 24 => $"vor {(int)age.TotalHours} Std.",
            _ => publishedUtc.ToLocalTime().ToString("dd.MM. HH:mm", System.Globalization.CultureInfo.GetCultureInfo("de-AT")),
        };
    }
}
