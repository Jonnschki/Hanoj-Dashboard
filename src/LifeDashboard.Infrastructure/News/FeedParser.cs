using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Infrastructure.News;

/// <summary>
/// Liest RSS 2.0, RSS 1.0/RDF (z. B. ORF) und Atom. Bewusst ohne Zusatzpaket: Feeds sind einfaches XML.
/// Nur Titel, Kurztext und Link werden übernommen (keine Volltexte).
/// </summary>
public static partial class FeedParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Rss1 = "http://purl.org/rss/1.0/";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    public static IReadOnlyList<NewsItem> Parse(string xml, NewsFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        XDocument doc;
        try
        {
            // DTDs verbieten (Schutz gegen XML-Angriffe aus fremden Quellen).
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return [];
        }

        var entries = doc.Descendants().Where(e =>
            e.Name == "item" || e.Name == Rss1 + "item" || e.Name == Atom + "entry");

        var result = new List<NewsItem>();
        foreach (var e in entries)
        {
            var title = Clean(Child(e, "title"));
            var link = ReadLink(e);
            var published = ReadDate(e);
            if (string.IsNullOrWhiteSpace(title) || link is null || published is null)
            {
                continue;
            }

            var summary = Clean(Child(e, "description") ?? Child(e, "summary"));
            result.Add(new NewsItem(title, string.IsNullOrWhiteSpace(summary) ? null : Truncate(summary, 280), link, feed.Source, feed.Region, feed.Category, published.Value));
        }

        return result;
    }

    private static string? Child(XElement e, string localName)
        => e.Elements().FirstOrDefault(c => c.Name.LocalName == localName)?.Value;

    private static Uri? ReadLink(XElement e)
    {
        // RSS: <link>url</link>; Atom: <link href="url" rel="alternate"/>
        var atomLink = e.Elements(Atom + "link").FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate");
        var text = (string?)atomLink?.Attribute("href") ?? Child(e, "link") ?? Child(e, "guid");
        return Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;
    }

    private static DateTimeOffset? ReadDate(XElement e)
    {
        var text = Child(e, "pubDate") ?? e.Element(Dc + "date")?.Value ?? Child(e, "updated") ?? Child(e, "published");
        return ParseDate(text);
    }

    /// <summary>RFC 822 (RSS) und ISO 8601 (Atom, dc:date), inkl. Varianten wie "Z", "GMT", "+0100", "-0000".</summary>
    internal static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var s = text.Trim();
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var iso) && s.Contains('T', StringComparison.Ordinal))
        {
            return iso.ToUniversalTime();
        }

        // RFC 822: "Tue, 10 Nov 2026 19:12:00 +0100" → Zeitzone in "+01:00"-Form bringen.
        s = TimeZoneSuffix().Replace(s, m => m.Groups[1].Value switch
        {
            "Z" or "GMT" or "UT" or "UTC" => "+00:00",
            "CET" => "+01:00",
            "CEST" => "+02:00",
            "EST" => "-05:00",
            "EDT" => "-04:00",
            "PST" => "-08:00",
            "PDT" => "-07:00",
            var offset => $"{offset[..3]}:{offset[3..]}",
        });

        string[] formats = ["ddd, d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm:ss zzz", "ddd, d MMM yyyy HH:mm zzz"];
        return DateTimeOffset.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var rfc)
            ? rfc.ToUniversalTime()
            : DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var any) ? any.ToUniversalTime() : null;
    }

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var noTags = HtmlTags().Replace(text, " ");
        return Whitespace().Replace(WebUtility.HtmlDecode(noTags), " ").Trim();
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    [GeneratedRegex(@"\s(Z|GMT|UTC|UT|CET|CEST|EST|EDT|PST|PDT|[+-]\d{4})$")]
    private static partial Regex TimeZoneSuffix();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
