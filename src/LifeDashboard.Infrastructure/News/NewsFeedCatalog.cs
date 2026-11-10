using LifeDashboard.Core.Domain;

namespace LifeDashboard.Infrastructure.News;

/// <summary>
/// Offizielle RSS-Feeds von Qualitätsmedien (Stand 11/2026 geprüft: alle liefern Daten).
/// Angezeigt werden nur Schlagzeile, Kurztext und Link zum Originalartikel.
/// </summary>
public static class NewsFeedCatalog
{
    public static IReadOnlyList<NewsFeed> Default { get; } =
    [
        // Österreich
        Feed("standard-inland", "Der Standard", "https://www.derstandard.at/rss/inland", NewsRegion.Austria, NewsCategory.Politics),
        Feed("standard-international", "Der Standard", "https://www.derstandard.at/rss/international", NewsRegion.Austria, NewsCategory.World),
        Feed("standard-wirtschaft", "Der Standard", "https://www.derstandard.at/rss/wirtschaft", NewsRegion.Austria, NewsCategory.Economy),
        Feed("presse-politik", "Die Presse", "https://www.diepresse.com/rss/politik", NewsRegion.Austria, NewsCategory.Politics),
        Feed("presse-ausland", "Die Presse", "https://www.diepresse.com/rss/ausland", NewsRegion.Austria, NewsCategory.World),
        Feed("presse-wirtschaft", "Die Presse", "https://www.diepresse.com/rss/wirtschaft", NewsRegion.Austria, NewsCategory.Economy),
        Feed("orf", "ORF", "https://rss.orf.at/news.xml", NewsRegion.Austria, NewsCategory.Politics),

        // Deutschland
        Feed("tagesschau-inland", "Tagesschau", "https://www.tagesschau.de/inland/index~rss2.xml", NewsRegion.Germany, NewsCategory.Politics),
        Feed("tagesschau-ausland", "Tagesschau", "https://www.tagesschau.de/ausland/index~rss2.xml", NewsRegion.Germany, NewsCategory.World),
        Feed("tagesschau-wirtschaft", "Tagesschau", "https://www.tagesschau.de/wirtschaft/index~rss2.xml", NewsRegion.Germany, NewsCategory.Economy),
        Feed("faz-politik", "FAZ", "https://www.faz.net/rss/aktuell/politik/", NewsRegion.Germany, NewsCategory.Politics),
        Feed("faz-wirtschaft", "FAZ", "https://www.faz.net/rss/aktuell/wirtschaft/", NewsRegion.Germany, NewsCategory.Economy),
        Feed("sz-politik", "Süddeutsche", "https://rss.sueddeutsche.de/rss/Politik", NewsRegion.Germany, NewsCategory.Politics),
        Feed("sz-wirtschaft", "Süddeutsche", "https://rss.sueddeutsche.de/rss/Wirtschaft", NewsRegion.Germany, NewsCategory.Economy),
        Feed("spiegel-politik", "Spiegel", "https://www.spiegel.de/politik/index.rss", NewsRegion.Germany, NewsCategory.Politics),
        Feed("spiegel-ausland", "Spiegel", "https://www.spiegel.de/ausland/index.rss", NewsRegion.Germany, NewsCategory.World),
        Feed("spiegel-wirtschaft", "Spiegel", "https://www.spiegel.de/wirtschaft/index.rss", NewsRegion.Germany, NewsCategory.Economy),
        Feed("zeit-politik", "Zeit", "https://newsfeed.zeit.de/politik/index", NewsRegion.Germany, NewsCategory.Politics),
        Feed("zeit-wirtschaft", "Zeit", "https://newsfeed.zeit.de/wirtschaft/index", NewsRegion.Germany, NewsCategory.Economy),
        Feed("handelsblatt-politik", "Handelsblatt", "https://www.handelsblatt.com/contentexport/feed/politik", NewsRegion.Germany, NewsCategory.Politics),
        Feed("handelsblatt-finanzen", "Handelsblatt", "https://www.handelsblatt.com/contentexport/feed/finanzen", NewsRegion.Germany, NewsCategory.Economy),

        // Europa (inkl. Schweiz)
        Feed("nzz-international", "NZZ", "https://www.nzz.ch/international.rss", NewsRegion.Europe, NewsCategory.World),
        Feed("nzz-wirtschaft", "NZZ", "https://www.nzz.ch/wirtschaft.rss", NewsRegion.Europe, NewsCategory.Economy),
        Feed("politico-eu", "Politico Europe", "https://www.politico.eu/feed/", NewsRegion.Europe, NewsCategory.Politics),

        // International
        Feed("ft-world", "Financial Times", "https://www.ft.com/world?format=rss", NewsRegion.International, NewsCategory.World),
        Feed("guardian-world", "The Guardian", "https://www.theguardian.com/world/rss", NewsRegion.International, NewsCategory.World),
        Feed("bbc-world", "BBC", "https://feeds.bbci.co.uk/news/world/rss.xml", NewsRegion.International, NewsCategory.World),
    ];

    private static NewsFeed Feed(string id, string source, string url, NewsRegion region, NewsCategory category)
        => new(id, source, new Uri(url), region, category);
}
