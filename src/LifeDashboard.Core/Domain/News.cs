namespace LifeDashboard.Core.Domain;

/// <summary>Region einer Nachrichtenquelle.</summary>
public enum NewsRegion
{
    Austria,
    Germany,
    Europe,
    International,
}

/// <summary>Themenbereich.</summary>
public enum NewsCategory
{
    /// <summary>Innenpolitik.</summary>
    Politics,

    /// <summary>Außenpolitik, Geopolitik, internationale Lage.</summary>
    World,

    /// <summary>Wirtschaft und Finanzen.</summary>
    Economy,
}

/// <summary>Ein Nachrichten-Feed (RSS/Atom) einer Qualitätszeitung.</summary>
public sealed record NewsFeed(string Id, string Source, Uri Url, NewsRegion Region, NewsCategory Category);

/// <summary>Eine einzelne Schlagzeile aus einem Feed.</summary>
public sealed record NewsItem(
    string Title,
    string? Summary,
    Uri Link,
    string Source,
    NewsRegion Region,
    NewsCategory Category,
    DateTimeOffset PublishedUtc);

/// <summary>
/// Eine Geschichte: gleiche Meldung aus mehreren Quellen zusammengefasst.
/// <see cref="Headline"/> ist die neueste Meldung, <see cref="Coverage"/> alle Meldungen (inkl. Headline).
/// </summary>
public sealed record NewsStory(NewsItem Headline, IReadOnlyList<NewsItem> Coverage, double Score)
{
    /// <summary>Anzahl unterschiedlicher Quellen, die berichten.</summary>
    public int SourceCount => Coverage.Select(c => c.Source).Distinct(StringComparer.Ordinal).Count();
}
