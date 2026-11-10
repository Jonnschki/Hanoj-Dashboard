using System.Net;
using System.Text;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.InMemory;
using LifeDashboard.Infrastructure.Ics;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeDashboard.Infrastructure.Tests;

public sealed class IcsParserTests
{
    private static readonly DateRange March2026 = new(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

    private static string Ics(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Test//DE\r\n" + string.Join("\r\n", events) + "\r\nEND:VCALENDAR\r\n";

    private static IReadOnlyList<CalendarEvent> ParseOk(string ics, DateRange? range = null)
    {
        var result = IcsParser.Parse(ics, "Ics:ics:test", range ?? March2026);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        return result.Value;
    }

    [Fact]
    public void Utc_event_inside_window_is_returned_and_outside_is_ignored()
    {
        var events = ParseOk(Ics(
            "BEGIN:VEVENT\r\nUID:a\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:20260310T090000Z\r\nDTEND:20260310T100000Z\r\nSUMMARY:Abstimmung\r\nLOCATION:Teams\r\nEND:VEVENT",
            "BEGIN:VEVENT\r\nUID:b\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:20260510T090000Z\r\nDTEND:20260510T100000Z\r\nSUMMARY:Mai\r\nEND:VEVENT"));

        var e = events.Should().ContainSingle().Subject;
        e.Title.Should().Be("Abstimmung");
        e.Location.Should().Be("Teams");
        e.CalendarId.Should().Be("Ics:ics:test");
        var t = e.Schedule.Should().BeOfType<TimedSchedule>().Subject;
        t.StartUtc.Should().Be(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
        t.Duration.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Outlook_windows_time_zone_is_converted_to_utc_and_iana()
    {
        // Outlook veröffentlicht TZID mit Windows-Namen und eigenem VTIMEZONE-Block.
        var ics = Ics(
            "BEGIN:VTIMEZONE\r\nTZID:W. Europe Standard Time\r\nBEGIN:STANDARD\r\nDTSTART:16010101T030000\r\nTZOFFSETFROM:+0200\r\nTZOFFSETTO:+0100\r\nRRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=10\r\nEND:STANDARD\r\nBEGIN:DAYLIGHT\r\nDTSTART:16010101T020000\r\nTZOFFSETFROM:+0100\r\nTZOFFSETTO:+0200\r\nRRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=3\r\nEND:DAYLIGHT\r\nEND:VTIMEZONE",
            "BEGIN:VEVENT\r\nUID:w1\r\nDTSTAMP:20260101T000000Z\r\nDTSTART;TZID=W. Europe Standard Time:20260310T090000\r\nDTEND;TZID=W. Europe Standard Time:20260310T093000\r\nSUMMARY:Winterzeit\r\nEND:VEVENT",
            "BEGIN:VEVENT\r\nUID:w2\r\nDTSTAMP:20260101T000000Z\r\nDTSTART;TZID=W. Europe Standard Time:20260330T090000\r\nDTEND;TZID=W. Europe Standard Time:20260330T093000\r\nSUMMARY:Sommerzeit\r\nEND:VEVENT");

        var events = ParseOk(ics);

        var winter = (TimedSchedule)events.Single(e => e.Title == "Winterzeit").Schedule;
        var summer = (TimedSchedule)events.Single(e => e.Title == "Sommerzeit").Schedule;
        winter.StartUtc.Should().Be(new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero), "09:00 MEZ = 08:00 UTC");
        summer.StartUtc.Should().Be(new DateTimeOffset(2026, 3, 30, 7, 0, 0, TimeSpan.Zero), "09:00 MESZ = 07:00 UTC");
        winter.TimeZoneId.Should().Be("Europe/Berlin");
    }

    [Fact]
    public void All_day_event_becomes_all_day_schedule()
    {
        var events = ParseOk(Ics(
            "BEGIN:VEVENT\r\nUID:d\r\nDTSTAMP:20260101T000000Z\r\nDTSTART;VALUE=DATE:20260316\r\nDTEND;VALUE=DATE:20260318\r\nSUMMARY:Messe\r\nEND:VEVENT"));

        var schedule = events.Should().ContainSingle().Subject.Schedule.Should().BeOfType<AllDaySchedule>().Subject;
        schedule.StartDate.Should().Be(new DateOnly(2026, 3, 16));
        schedule.EndDateExclusive.Should().Be(new DateOnly(2026, 3, 18));
    }

    [Fact]
    public void Weekly_series_is_expanded_respecting_exdate_and_moved_instance()
    {
        var events = ParseOk(Ics(
            "BEGIN:VEVENT\r\nUID:serie\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:20260302T080000Z\r\nDTEND:20260302T083000Z\r\nRRULE:FREQ=WEEKLY;COUNT=4\r\nEXDATE:20260309T080000Z\r\nSUMMARY:Jour fixe\r\nEND:VEVENT",
            "BEGIN:VEVENT\r\nUID:serie\r\nDTSTAMP:20260101T000000Z\r\nRECURRENCE-ID:20260316T080000Z\r\nDTSTART:20260317T120000Z\r\nDTEND:20260317T123000Z\r\nSUMMARY:Jour fixe (verschoben)\r\nEND:VEVENT"));

        events.Select(e => e.Schedule.SortKeyUtc.Day).Should().BeEquivalentTo([2, 17, 23], "9. ist ausgenommen, 16. auf den 17. verschoben");
        events.Single(e => e.Schedule.SortKeyUtc.Day == 17).Title.Should().Be("Jour fixe (verschoben)");
        events.Select(e => e.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Status_class_and_outlook_busy_status_are_mapped()
    {
        var events = ParseOk(Ics(
            "BEGIN:VEVENT\r\nUID:c\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:20260305T090000Z\r\nDTEND:20260305T100000Z\r\nSTATUS:CANCELLED\r\nSUMMARY:Abgesagt\r\nEND:VEVENT",
            "BEGIN:VEVENT\r\nUID:p\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:20260306T090000Z\r\nDTEND:20260306T100000Z\r\nCLASS:PRIVATE\r\nSUMMARY:Privat\r\nEND:VEVENT",
            "BEGIN:VEVENT\r\nUID:f\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:20260307T090000Z\r\nDTEND:20260307T100000Z\r\nTRANSP:TRANSPARENT\r\nSUMMARY:Frei\r\nEND:VEVENT",
            "BEGIN:VEVENT\r\nUID:o\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:20260308T090000Z\r\nDTEND:20260308T100000Z\r\nX-MICROSOFT-CDO-BUSYSTATUS:OOF\r\nSUMMARY:Urlaub\r\nEND:VEVENT"));

        events.Should().NotContain(e => e.Title == "Abgesagt");
        events.Single(e => e.Title == "Privat").Visibility.Should().Be(EventVisibility.Private);
        events.Single(e => e.Title == "Frei").ShowAs.Should().Be(ShowAs.Free);
        events.Single(e => e.Title == "Urlaub").ShowAs.Should().Be(ShowAs.OutOfOffice);
    }

    [Theory]
    [InlineData("<html>Login</html>")]
    [InlineData("")]
    [InlineData("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:2026")]
    public void Invalid_content_is_a_failure_not_an_exception(string content)
    {
        var result = IcsParser.Parse(content, "x", March2026);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ics.invalid");
    }
}

public sealed class IcsCalendarProviderTests
{
    private const string SecretUrl = "https://outlook.office365.com/owa/calendar/SECRET-TOKEN/reachcalendar.ics";

    private static readonly DateRange March2026 = new(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/calendar") });
        }
    }

    private static async Task<(IcsCalendarProvider Provider, StubHandler Handler)> CreateAsync(HttpStatusCode status = HttpStatusCode.OK, bool storeUrl = true)
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:x\r\nBEGIN:VEVENT\r\nUID:k1\r\nDTSTAMP:20260101T000000Z\r\nDTSTART:20260312T130000Z\r\nDTEND:20260312T140000Z\r\nSUMMARY:Kundentermin\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var settings = new InMemoryUserSettingsStore();
        await settings.SaveAsync(new UserSettings { IcsSubscriptions = [new IcsSubscription("kunde1", "Kunde Müller", CalendarCategory.Work)] });
        var secrets = new InMemorySecureTokenStore();
        if (storeUrl)
        {
            await secrets.SetAsync(IcsSubscription.SecretKeyFor("kunde1"), SecretUrl);
        }

        var handler = new StubHandler(status, ics);
        return (new IcsCalendarProvider(new HttpClient(handler), settings, secrets, NullLogger<IcsCalendarProvider>.Instance), handler);
    }

    [Fact]
    public async Task Subscriptions_appear_as_read_only_calendars()
    {
        var (provider, _) = await CreateAsync();

        var calendar = (await provider.GetCalendarsAsync()).Value.Should().ContainSingle().Subject;

        calendar.Name.Should().Be("Kunde Müller");
        calendar.Ownership.Should().Be(CalendarOwnership.SharedReadOnly);
        calendar.IsWritable.Should().BeFalse();
        calendar.Id.Should().Be(IcsCalendarProvider.CalendarIdFor("kunde1"));
    }

    [Fact]
    public async Task Events_are_loaded_from_the_secret_url()
    {
        var (provider, handler) = await CreateAsync();
        var calendar = (await provider.GetCalendarsAsync()).Value.Single();

        var events = await provider.GetEventsAsync(calendar, March2026);

        events.Value.Should().ContainSingle().Which.Title.Should().Be("Kundentermin");
        handler.Requested.Should().ContainSingle().Which.ToString().Should().Be(SecretUrl);
    }

    [Fact]
    public async Task Writer_is_never_available()
    {
        var (provider, _) = await CreateAsync();
        var calendar = (await provider.GetCalendarsAsync()).Value.Single();

        var act = () => provider.GetWriter(calendar);

        act.Should().Throw<ReadOnlyCalendarException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "ics.not_found")]
    [InlineData(HttpStatusCode.Forbidden, "ics.forbidden")]
    [InlineData(HttpStatusCode.InternalServerError, "ics.http")]
    public async Task Http_errors_become_failures_without_leaking_the_url(HttpStatusCode status, string code)
    {
        var (provider, _) = await CreateAsync(status);
        var calendar = (await provider.GetCalendarsAsync()).Value.Single();

        var result = await provider.GetEventsAsync(calendar, March2026);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(code);
        result.Error.Message.Should().Contain("Kunde Müller").And.NotContain("SECRET-TOKEN");
    }

    [Fact]
    public async Task Missing_url_in_secure_store_is_a_failure()
    {
        var (provider, handler) = await CreateAsync(storeUrl: false);
        var calendar = (await provider.GetCalendarsAsync()).Value.Single();

        var result = await provider.GetEventsAsync(calendar, March2026);

        result.Error.Code.Should().Be("ics.no_url");
        handler.Requested.Should().BeEmpty();
    }
}
