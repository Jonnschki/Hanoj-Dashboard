using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Tests.Domain;

public sealed class IcsSubscriptionTests
{
    [Theory]
    [InlineData("webcal://outlook.office365.com/owa/calendar/abc/reachcalendar.ics", "https://outlook.office365.com/owa/calendar/abc/reachcalendar.ics")]
    [InlineData("  https://calendar.google.com/calendar/ical/x/private-y/basic.ics ", "https://calendar.google.com/calendar/ical/x/private-y/basic.ics")]
    [InlineData("WEBCAL://example.com/a.ics", "https://example.com/a.ics")]
    public void Valid_links_are_normalised_to_https(string input, string expected)
        => IcsSubscription.NormalizeUrl(input)!.ToString().Should().Be(expected);

    [Theory]
    [InlineData("http://example.com/a.ics")]
    [InlineData("ftp://example.com/a.ics")]
    [InlineData("kein link")]
    [InlineData("")]
    [InlineData(null)]
    public void Insecure_or_invalid_links_are_rejected(string? input)
        => IcsSubscription.NormalizeUrl(input).Should().BeNull();

    [Fact]
    public void Secret_key_is_derived_from_id()
        => new IcsSubscription("abc", "Kunde", CalendarCategory.Work).SecretKey.Should().Be("ics-url:abc");
}
