using System.Net;
using System.Text;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Markets;
using LifeDashboard.Infrastructure.News;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LifeDashboard.Infrastructure.Tests;

/// <summary>HTTP-Stub, der pro URL-Teilstring eine Antwort liefert.</summary>
internal sealed class RoutingHandler : HttpMessageHandler
{
    private readonly List<(string Contains, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public RoutingHandler On(string contains, HttpStatusCode status, string body, string mediaType = "application/json", Action<HttpResponseMessage>? configure = null)
    {
        _routes.Add((contains, _ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
            configure?.Invoke(response);
            return response;
        }));
        return this;
    }

    public RoutingHandler On(string contains, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((contains, respond));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var route = _routes.FirstOrDefault(r => request.RequestUri!.AbsoluteUri.Contains(r.Contains, StringComparison.Ordinal));
        return Task.FromResult(route.Respond?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public sealed class YahooFinanceTests
{
    private const string AtxJson = """
        {"chart":{"result":[{"meta":{"currency":"EUR","symbol":"^ATX","regularMarketTime":1794329401,"regularMarketPrice":6650.55,"chartPreviousClose":6562.47,"regularMarketChangePercent":1.342}}],"error":null}}
        """;

    private static YahooFinanceMarketDataProvider Create(RoutingHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://query1.finance.yahoo.com/") },
        new FixedClock(DateTimeOffset.UnixEpoch),
        NullLogger<YahooFinanceMarketDataProvider>.Instance);

    [Fact]
    public async Task Parses_chart_meta_and_skips_unknown_symbols()
    {
        var handler = new RoutingHandler()
            .On("%5EATX", HttpStatusCode.OK, AtxJson)
            .On("NOPE", HttpStatusCode.NotFound, """{"chart":{"result":null,"error":{"code":"Not Found"}}}""");

        var result = await Create(handler).GetQuotesAsync(["^ATX", "NOPE"]);

        var quote = result.Value.Should().ContainSingle().Subject;
        quote.Name.Should().Be("ATX");
        quote.Price.Should().Be(6650.55m);
        quote.Change.Should().Be(88.08m);
        quote.ChangePercent.Should().Be(1.34m);
        quote.Currency.Should().Be("EUR");
        quote.Source.Should().Be("Yahoo Finance");
        handler.Requests.Should().Contain(r => r.RequestUri!.AbsoluteUri.Contains("v8/finance/chart/%5EATX", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Network_failure_for_all_symbols_is_a_failure()
    {
        var handler = new RoutingHandler().On("chart", _ => throw new HttpRequestException("offline"));

        var result = await Create(handler).GetQuotesAsync(["^ATX"]);

        result.IsFailure.Should().BeTrue();
    }
}

public sealed class FallbackMarketDataTests
{
    private static MarketQuote Q(string symbol, string source) => new(symbol, null, 1, 0, 0, "USD", DateTimeOffset.UnixEpoch) { Source = source };

    [Fact]
    public async Task Missing_symbols_are_asked_from_fallback_and_order_is_kept()
    {
        var yahoo = Substitute.For<IMarketDataProvider>();
        yahoo.GetQuotesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<MarketQuote>>([Q("^ATX", "Yahoo")]));
        var finnhub = Substitute.For<IMarketDataProvider>();
        finnhub.GetQuotesAsync(Arg.Is<IReadOnlyCollection<string>>(s => s.SequenceEqual(new[] { "AAPL" })), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<MarketQuote>>([Q("AAPL", "Finnhub")]));

        var result = await new FallbackMarketDataProvider(yahoo, finnhub, NullLogger<FallbackMarketDataProvider>.Instance).GetQuotesAsync(["AAPL", "^ATX"]);

        result.Value.Select(q => $"{q.Symbol}/{q.Source}").Should().Equal("AAPL/Finnhub", "^ATX/Yahoo");
    }

    [Fact]
    public async Task Primary_outage_falls_back_completely()
    {
        var yahoo = Substitute.For<IMarketDataProvider>();
        yahoo.GetQuotesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<MarketQuote>>(Errors.ProviderFailure("Yahoo", "down")));
        var finnhub = Substitute.For<IMarketDataProvider>();
        finnhub.GetQuotesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<MarketQuote>>([Q("AAPL", "Finnhub")]));

        var result = await new FallbackMarketDataProvider(yahoo, finnhub, NullLogger<FallbackMarketDataProvider>.Instance).GetQuotesAsync(["AAPL"]);

        result.Value.Should().ContainSingle().Which.Source.Should().Be("Finnhub");
    }

    [Fact]
    public async Task Without_fallback_primary_failure_is_returned()
    {
        var yahoo = Substitute.For<IMarketDataProvider>();
        yahoo.GetQuotesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<MarketQuote>>(Errors.ProviderFailure("Yahoo", "down")));

        var result = await new FallbackMarketDataProvider(yahoo, null, NullLogger<FallbackMarketDataProvider>.Instance).GetQuotesAsync(["AAPL"]);

        result.IsFailure.Should().BeTrue();
    }
}

public sealed class FeedParserTests
{
    private static readonly NewsFeed Feed = new("t", "Testzeitung", new Uri("https://example.com/rss"), NewsRegion.Austria, NewsCategory.Politics);

    [Fact]
    public void Parses_rss2_with_entities_html_and_rfc822_dates()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0"><channel><title>T</title>
              <item><title>Regierung &amp; Opposition einigen sich</title><link>https://example.com/a</link>
                <description>&lt;p&gt;Ein &lt;b&gt;Kompromiss&lt;/b&gt; zum Budget.&lt;/p&gt;</description><pubDate>Tue, 10 Nov 2026 19:12:00 +0100</pubDate></item>
              <item><title>Zweite</title><link>https://example.com/b</link><pubDate>Tue, 10 Nov 2026 14:30:37 Z</pubDate></item>
              <item><title>Ohne Datum</title><link>https://example.com/c</link></item>
            </channel></rss>
            """;

        var items = FeedParser.Parse(xml, Feed);

        items.Should().HaveCount(2);
        items[0].Title.Should().Be("Regierung & Opposition einigen sich");
        items[0].Summary.Should().Be("Ein Kompromiss zum Budget.");
        items[0].PublishedUtc.Should().Be(new DateTimeOffset(2026, 11, 10, 18, 12, 0, TimeSpan.Zero));
        items[1].PublishedUtc.Should().Be(new DateTimeOffset(2026, 11, 10, 14, 30, 37, TimeSpan.Zero));
        items[0].Source.Should().Be("Testzeitung");
    }

    [Fact]
    public void Parses_rdf_with_dc_date_like_orf()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/" xmlns:dc="http://purl.org/dc/elements/1.1/">
              <channel><title>news.ORF.at</title></channel>
              <item rdf:about="https://orf.at/stories/1/"><title>Nationalrat beschließt Gesetz</title><link>https://orf.at/stories/1/</link><dc:date>2026-11-10T21:00:48+01:00</dc:date></item>
            </rdf:RDF>
            """;

        var item = FeedParser.Parse(xml, Feed).Should().ContainSingle().Subject;

        item.Title.Should().Be("Nationalrat beschließt Gesetz");
        item.PublishedUtc.Should().Be(new DateTimeOffset(2026, 11, 10, 20, 0, 48, TimeSpan.Zero));
    }

    [Fact]
    public void Parses_atom()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom"><title>A</title>
              <entry><title>Atom-Meldung</title><link rel="alternate" href="https://example.com/atom"/><updated>2026-11-10T20:04:49Z</updated><summary>Kurz</summary></entry>
            </feed>
            """;

        var item = FeedParser.Parse(xml, Feed).Should().ContainSingle().Subject;

        item.Link.Should().Be(new Uri("https://example.com/atom"));
        item.Summary.Should().Be("Kurz");
    }

    [Theory]
    [InlineData("Tue, 10 Nov 2026 16:08:39 GMT", "2026-11-10T16:08:39Z")]
    [InlineData("Tue, 10 Nov 2026 19:50:11 -0000", "2026-11-10T19:50:11Z")]
    [InlineData("Tue, 10 Nov 2026 19:31:46 +0000", "2026-11-10T19:31:46Z")]
    [InlineData("2026-11-06T08:13:15Z", "2026-11-06T08:13:15Z")]
    public void Date_variants(string input, string expectedUtc)
        => FeedParser.ParseDate(input).Should().Be(DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Invalid_xml_and_doctype_return_no_items()
    {
        FeedParser.Parse("<html>kaputt", Feed).Should().BeEmpty();
        FeedParser.Parse("<!DOCTYPE x [<!ENTITY a \"b\">]><rss><channel><item><title>&a;</title></item></channel></rss>", Feed).Should().BeEmpty();
    }
}

public sealed class RssNewsProviderTests
{
    private static string Rss(string title) =>
        $"<rss><channel><item><title>{title}</title><link>https://example.com/{Uri.EscapeDataString(title)}</link><pubDate>Tue, 10 Nov 2026 19:00:00 GMT</pubDate></item></channel></rss>";

    private static readonly NewsFeed[] TwoFeeds =
    [
        new("a", "Zeitung A", new Uri("https://a.example/rss"), NewsRegion.Austria, NewsCategory.Politics),
        new("b", "Zeitung B", new Uri("https://b.example/rss"), NewsRegion.Germany, NewsCategory.Economy),
    ];

    [Fact]
    public async Task Failing_feed_is_reported_and_others_still_load()
    {
        var handler = new RoutingHandler()
            .On("a.example", HttpStatusCode.OK, Rss("Meldung A"), "application/rss+xml")
            .On("b.example", HttpStatusCode.InternalServerError, "kaputt");
        var provider = new RssNewsProvider(new HttpClient(handler), new FixedClock(DateTimeOffset.UnixEpoch), NullLogger<RssNewsProvider>.Instance) { Feeds = TwoFeeds };

        var snapshot = await provider.GetLatestAsync();

        snapshot.Items.Should().ContainSingle().Which.Source.Should().Be("Zeitung A");
        snapshot.FailedSources.Should().Equal("Zeitung B");
    }

    [Fact]
    public async Task Uses_etag_and_keeps_items_on_304_and_respects_min_interval()
    {
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var calls = 0;
        var handler = new RoutingHandler().On("a.example", req =>
        {
            calls++;
            if (req.Headers.IfNoneMatch.Any(e => e.Tag == "\"v1\""))
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Rss("Meldung A")) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
            return response;
        });
        var provider = new RssNewsProvider(new HttpClient(handler), clock, NullLogger<RssNewsProvider>.Instance) { Feeds = [TwoFeeds[0]] };

        await provider.GetLatestAsync();
        await provider.GetLatestAsync(); // innerhalb des Mindestabstands: kein Request
        calls.Should().Be(1);

        clock.UtcNow += TimeSpan.FromMinutes(5);
        var third = await provider.GetLatestAsync(); // 304 → Cache

        calls.Should().Be(2);
        third.Items.Should().ContainSingle().Which.Title.Should().Be("Meldung A");
    }

    [Fact]
    public void Default_catalog_contains_austrian_german_and_european_sources()
    {
        NewsFeedCatalog.Default.Select(f => f.Region).Distinct().Should().Contain([NewsRegion.Austria, NewsRegion.Germany, NewsRegion.Europe, NewsRegion.International]);
        NewsFeedCatalog.Default.Should().OnlyContain(f => f.Url.Scheme == Uri.UriSchemeHttps);
        NewsFeedCatalog.Default.Select(f => f.Id).Should().OnlyHaveUniqueItems();
    }
}
