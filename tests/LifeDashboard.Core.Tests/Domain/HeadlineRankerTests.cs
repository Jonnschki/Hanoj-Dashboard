using LifeDashboard.Core.Domain;
using LifeDashboard.Core.News;

namespace LifeDashboard.Core.Tests.Domain;

public sealed class HeadlineRankerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 20, 0, 0, TimeSpan.Zero);

    private static NewsItem Item(string title, string source, double hoursAgo = 1, NewsCategory category = NewsCategory.World, string? link = null) => new(
        title, null, new Uri(link ?? $"https://example.com/{source}/{Guid.NewGuid():N}"), source, NewsRegion.Germany, category, Now.AddHours(-hoursAgo));

    [Fact]
    public void Story_covered_by_several_papers_ranks_above_single_fresher_story()
    {
        var items = new[]
        {
            Item("EU einigt sich auf neue Zölle gegen China", "FAZ", 2),
            Item("Neue EU-Zölle gegen China beschlossen", "Der Standard", 2.5),
            Item("China: EU beschließt Zölle", "Spiegel", 3),
            Item("Unwetter in Kärnten", "ORF", 0.1),
        };

        var stories = new HeadlineRanker().Rank(items, Now);

        stories[0].Headline.Title.Should().Contain("Zölle");
        stories[0].SourceCount.Should().Be(3);
        stories.Should().HaveCount(2);
    }

    [Fact]
    public void Old_items_and_duplicate_links_are_ignored()
    {
        var items = new[]
        {
            Item("Alte Meldung", "FAZ", 30),
            Item("Doppelt", "SZ", 1, link: "https://sz.de/a"),
            Item("Doppelt", "SZ", 1, link: "https://sz.de/a"),
        };

        var stories = new HeadlineRanker().Rank(items, Now);

        stories.Should().ContainSingle().Which.Headline.Title.Should().Be("Doppelt");
    }

    [Fact]
    public void Same_words_in_different_categories_are_not_merged()
    {
        var items = new[]
        {
            Item("Regierung beschließt Budget", "Presse", 1, NewsCategory.Politics),
            Item("Regierung beschließt Budget", "Handelsblatt", 1, NewsCategory.Economy),
        };

        new HeadlineRanker().Rank(items, Now).Should().HaveCount(2);
    }

    [Fact]
    public void Newest_item_of_a_story_is_the_headline()
    {
        var items = new[]
        {
            Item("Waffenruhe in Nahost verlängert", "NZZ", 5),
            Item("Nahost: Waffenruhe wird verlängert", "Tagesschau", 1),
        };

        var story = new HeadlineRanker().Rank(items, Now).Single();

        story.Headline.Source.Should().Be("Tagesschau");
        story.Coverage.Should().HaveCount(2);
    }

    [Fact]
    public void Tokenizer_ignores_stop_words_case_and_umlauts()
        => HeadlineRanker.Tokenize("Die Zölle der EU gegen CHINA").Should().BeEquivalentTo(HeadlineRanker.Tokenize("zoelle gegen china"));
}
