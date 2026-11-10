using System.Net;
using System.Text.Json.Nodes;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.InMemory;
using LifeDashboard.Infrastructure.Auth;
using LifeDashboard.Infrastructure.GoogleCalendar;
using LifeDashboard.Infrastructure.MicrosoftGraph;
using LifeDashboard.Infrastructure.Strava;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeDashboard.Infrastructure.Tests;

internal sealed class FakeTokens(bool connected = true, string user = "me@iioe.at", ProviderKind kind = ProviderKind.Microsoft) : IAccessTokenSource
{
    public CalendarAccount? Account => connected ? new CalendarAccount(kind.ToString().ToLowerInvariant(), kind, user, user) : null;

    public Task<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(connected ? Result.Success("TOKEN") : Result.Failure<string>(Errors.NotConnected(kind.ToString())));
}

public sealed class GraphCalendarProviderTests
{
    private static readonly DateRange March = new(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Not_connected_means_no_calendars_and_no_request()
    {
        var handler = new RoutingHandler();
        var provider = new GraphCalendarProvider(new HttpClient(handler), new FakeTokens(connected: false));

        var calendars = await provider.GetCalendarsAsync();

        calendars.IsSuccess.Should().BeTrue();
        calendars.Value.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Own_vs_shared_calendars_are_classified()
    {
        var handler = new RoutingHandler().On("me/calendars?", HttpStatusCode.OK, """
            {"value":[
              {"id":"AAA=","name":"Kalender","canEdit":true,"owner":{"address":"me@iioe.at"}},
              {"id":"BBB=","name":"Kollege","canEdit":false,"owner":{"address":"kollege@iioe.at"}},
              {"id":"CCC=","name":"Assistenz (darf ich bearbeiten)","canEdit":true,"owner":{"address":"chef@iioe.at"}}]}
            """);
        var provider = new GraphCalendarProvider(new HttpClient(handler), new FakeTokens());

        var calendars = (await provider.GetCalendarsAsync()).Value;

        calendars.Single(c => c.Name == "Kalender").Ownership.Should().Be(CalendarOwnership.Own);
        calendars.Single(c => c.Name == "Kollege").Ownership.Should().Be(CalendarOwnership.SharedReadOnly);
        calendars.Single(c => c.Name.StartsWith("Assistenz", StringComparison.Ordinal)).Ownership.Should().Be(CalendarOwnership.SharedReadOnly, "fremder Kalender bleibt read-only, auch mit Schreibrecht");
        calendars.Should().OnlyContain(c => c.Category == CalendarCategory.Work);
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be("TOKEN");
    }

    [Fact]
    public async Task Calendar_view_is_paged_and_mapped_with_marker()
    {
        var page1 = $$"""
            {"value":[{"id":"e1","subject":"Kunde","bodyPreview":"Agenda","isAllDay":false,
              "start":{"dateTime":"2026-03-10T08:00:00.0000000","timeZone":"UTC"},"end":{"dateTime":"2026-03-10T09:00:00.0000000","timeZone":"UTC"},
              "location":{"displayName":"Wien"},"showAs":"busy","sensitivity":"normal","lastModifiedDateTime":"2026-03-01T10:00:00Z",
              "originalStartTimeZone":"W. Europe Standard Time","webLink":"https://outlook/e1",
              "singleValueExtendedProperties":[{"id":"{{GraphCalendarProvider.OriginPropertyId}}","value":"{\"p\":\"Google\",\"o\":\"evt|g|1\",\"h\":\"ABC\"}"}]}],
             "@odata.nextLink":"https://graph.microsoft.com/v1.0/next-page"}
            """;
        const string page2 = """
            {"value":[{"id":"e2","subject":"Urlaub","isAllDay":true,"start":{"dateTime":"2026-03-16T00:00:00.0000000"},"end":{"dateTime":"2026-03-18T00:00:00.0000000"},"showAs":"oof","sensitivity":"private","lastModifiedDateTime":"2026-03-01T10:00:00Z"},
                      {"id":"e3","subject":"Abgesagt","isCancelled":true,"start":{"dateTime":"2026-03-11T08:00:00"},"end":{"dateTime":"2026-03-11T09:00:00"}}]}
            """;
        var handler = new RoutingHandler().On("calendarView", HttpStatusCode.OK, page1).On("next-page", HttpStatusCode.OK, page2);
        var provider = new GraphCalendarProvider(new HttpClient(handler), new FakeTokens());
        var calendar = new CalendarInfo("Microsoft:microsoft:AAA=", "microsoft", ProviderKind.Microsoft, "Kalender", CalendarOwnership.Own, CalendarCategory.Work);

        var events = (await provider.GetEventsAsync(calendar, March)).Value;

        events.Should().HaveCount(2);
        var timed = events.Single(e => e.Id == "e1");
        ((TimedSchedule)timed.Schedule).StartUtc.Should().Be(new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero));
        ((TimedSchedule)timed.Schedule).TimeZoneId.Should().Be("Europe/Berlin");
        timed.Origin.Should().Be(new OriginMarker(ProviderKind.Google, "evt|g|1", "ABC"));
        var allDay = events.Single(e => e.Id == "e2");
        allDay.Schedule.Should().Be(new AllDaySchedule(new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 18)));
        allDay.ShowAs.Should().Be(ShowAs.OutOfOffice);
        allDay.Visibility.Should().Be(EventVisibility.Private);
        handler.Requests[0].Headers.GetValues("Prefer").Should().Contain(v => v.Contains("UTC", StringComparison.Ordinal));
        handler.Requests[0].RequestUri!.AbsoluteUri.Should().Contain("AAA%3D");
    }

    [Fact]
    public async Task Writer_creates_event_with_marker_and_returns_server_timestamp()
    {
        string? sentBody = null;
        var handler = new RoutingHandler().On("/events", req =>
        {
            sentBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"id":"new1","lastModifiedDateTime":"2026-03-02T12:00:00Z"}""") };
        });
        var provider = new GraphCalendarProvider(new HttpClient(handler), new FakeTokens());
        var calendar = new CalendarInfo("Microsoft:microsoft:AAA=", "microsoft", ProviderKind.Microsoft, "Kalender", CalendarOwnership.Own, CalendarCategory.Work);
        var e = new CalendarEvent(string.Empty, calendar.Id, "Beschäftigt", null, null,
            new TimedSchedule(new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), "Europe/Vienna"),
            EventVisibility.Private, ShowAs.Busy, DateTimeOffset.UnixEpoch, new OriginMarker(ProviderKind.Google, "evt|g|1", "H1"));

        var receipt = await provider.GetWriter(calendar).CreateAsync(e);

        receipt.Value.Should().Be(new WriteReceipt("new1", new DateTimeOffset(2026, 3, 2, 12, 0, 0, TimeSpan.Zero)));
        var json = JsonNode.Parse(sentBody!)!;
        json["start"]!["dateTime"]!.GetValue<string>().Should().Be("2026-03-10T08:00:00");
        json["start"]!["timeZone"]!.GetValue<string>().Should().Be("UTC");
        json["sensitivity"]!.GetValue<string>().Should().Be("private");
        json["isReminderOn"]!.GetValue<bool>().Should().BeFalse();
        json["singleValueExtendedProperties"]![0]!["id"]!.GetValue<string>().Should().Be(GraphCalendarProvider.OriginPropertyId);
    }

    [Fact]
    public void Writer_refuses_shared_calendar()
    {
        var provider = new GraphCalendarProvider(new HttpClient(new RoutingHandler()), new FakeTokens());
        var shared = new CalendarInfo("Microsoft:microsoft:BBB=", "microsoft", ProviderKind.Microsoft, "Kollege", CalendarOwnership.SharedReadOnly, CalendarCategory.Work);

        var act = () => provider.GetWriter(shared);

        act.Should().Throw<ReadOnlyCalendarException>();
    }
}

public sealed class GoogleCalendarProviderTests
{
    private static readonly DateRange March = new(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Only_owner_calendars_are_own_and_category_is_private()
    {
        var handler = new RoutingHandler().On("calendarList", HttpStatusCode.OK, """
            {"items":[{"id":"me@gmail.com","summary":"Jonah","accessRole":"owner","primary":true},
                      {"id":"de.austrian#holiday@group.v.calendar.google.com","summary":"Feiertage","accessRole":"reader"},
                      {"id":"familie@group.calendar.google.com","summary":"Familie","accessRole":"writer"}]}
            """);
        var provider = new GoogleCalendarProvider(new HttpClient(handler), new FakeTokens(kind: ProviderKind.Google, user: "me@gmail.com"));

        var calendars = (await provider.GetCalendarsAsync()).Value;

        calendars.Single(c => c.Name == "Jonah").IsWritable.Should().BeTrue();
        calendars.Where(c => c.Name != "Jonah").Should().OnlyContain(c => !c.IsWritable);
        calendars.Should().OnlyContain(c => c.Category == CalendarCategory.Private);
        calendars.Single(c => c.Name == "Feiertage").Id.Should().Be("Google:google:de.austrian#holiday@group.v.calendar.google.com");
    }

    [Fact]
    public async Task Events_are_paged_mapped_and_cancelled_ones_skipped()
    {
        const string page1 = """
            {"items":[{"id":"g1","status":"confirmed","summary":"Laufen","start":{"dateTime":"2026-03-10T07:00:00+01:00","timeZone":"Europe/Vienna"},"end":{"dateTime":"2026-03-10T08:00:00+01:00"},
                       "updated":"2026-03-01T10:00:00.000Z","transparency":"transparent","visibility":"private",
                       "extendedProperties":{"private":{"LifeDashboard.OriginProvider":"Strava","LifeDashboard.OriginId":"act|Strava|1","LifeDashboard.SyncHash":"H"}}}],
             "nextPageToken":"P2"}
            """;
        const string page2 = """
            {"items":[{"id":"g2","summary":"Urlaub","start":{"date":"2026-03-16"},"end":{"date":"2026-03-18"},"updated":"2026-03-01T10:00:00Z"},
                      {"id":"g3","status":"cancelled"}]}
            """;
        var handler = new RoutingHandler().On("pageToken=P2", HttpStatusCode.OK, page2).On("/events?", HttpStatusCode.OK, page1);
        var provider = new GoogleCalendarProvider(new HttpClient(handler), new FakeTokens(kind: ProviderKind.Google));
        var calendar = new CalendarInfo("Google:google:me@gmail.com", "google", ProviderKind.Google, "Jonah", CalendarOwnership.Own, CalendarCategory.Private);

        var events = (await provider.GetEventsAsync(calendar, March)).Value;

        events.Select(e => e.Id).Should().Equal("g1", "g2");
        var run = events[0];
        ((TimedSchedule)run.Schedule).StartUtc.Should().Be(new DateTimeOffset(2026, 3, 10, 6, 0, 0, TimeSpan.Zero));
        run.ShowAs.Should().Be(ShowAs.Free);
        run.Visibility.Should().Be(EventVisibility.Private);
        run.Origin!.OriginId.Should().Be("act|Strava|1");
        events[1].Schedule.Should().Be(new AllDaySchedule(new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 18)));
        handler.Requests[0].RequestUri!.AbsoluteUri.Should().Contain("singleEvents=true").And.Contain("me%40gmail.com");
    }

    [Fact]
    public void Mapping_round_trips_marker_and_all_day()
    {
        var e = new CalendarEvent(string.Empty, "c", "Messe", "Text", "Wien", new AllDaySchedule(new DateOnly(2026, 3, 16), new DateOnly(2026, 3, 18)),
            EventVisibility.Normal, ShowAs.Busy, DateTimeOffset.UnixEpoch, new OriginMarker(ProviderKind.Microsoft, "evt|m|1", "XYZ"));

        var json = GoogleCalendarProvider.ToGoogle(e);
        json["id"] = "x";
        json["updated"] = "2026-03-01T00:00:00Z";
        var back = GoogleCalendarProvider.ToDomain(json, "c")!;

        back.Schedule.Should().Be(e.Schedule);
        back.Origin.Should().Be(e.Origin);
        back.Title.Should().Be("Messe");
    }
}

public sealed class StravaActivityProviderTests
{
    private static string Activities(int count, int startId) => "[" + string.Join(',', Enumerable.Range(startId, count).Select(i =>
        $$"""{"id":{{i}},"name":"Lauf {{i}}","sport_type":"Run","start_date":"2026-03-10T06:00:00Z","timezone":"(GMT+01:00) Europe/Vienna","elapsed_time":3000,"moving_time":2900,"distance":10000}""")) + "]";

    [Fact]
    public async Task Pages_until_a_short_page_and_maps_activities()
    {
        var handler = new RoutingHandler()
            .On("&page=1", HttpStatusCode.OK, Activities(100, 1))
            .On("&page=2", HttpStatusCode.OK, Activities(3, 101));
        var provider = new StravaActivityProvider(new HttpClient(handler) { BaseAddress = new Uri("https://www.strava.com/api/v3/") }, new FakeTokens(kind: ProviderKind.Strava));

        var result = await provider.GetActivitiesAsync(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        result.Value.Should().HaveCount(103);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.Query.Should().Contain("after=1772323200");
    }

    [Fact]
    public async Task Not_connected_returns_empty_list()
    {
        var provider = new StravaActivityProvider(new HttpClient(new RoutingHandler()), new FakeTokens(connected: false, kind: ProviderKind.Strava));

        var result = await provider.GetActivitiesAsync(DateTimeOffset.UnixEpoch);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}

public sealed class OAuthSessionTests
{
    /// <summary>Simuliert den Browser: leitet sofort an die Redirect-URI weiter (wie nach erfolgreichem Login).</summary>
    private sealed class AutoApproveLauncher(Func<IReadOnlyDictionary<string, string>, string> buildRedirectQuery) : IUrlLauncher
    {
        public Uri? Opened { get; private set; }

        public Task OpenAsync(Uri url, CancellationToken cancellationToken = default)
        {
            Opened = url;
            var query = LoopbackBrowser.ParseQuery(url.Query.TrimStart('?'));
            var redirect = query["redirect_uri"];
            _ = Task.Run(async () =>
            {
                using var client = new HttpClient();
                await client.GetAsync($"{redirect}?{buildRedirectQuery(query)}", cancellationToken);
            }, cancellationToken);
            return Task.CompletedTask;
        }
    }

    private static OAuthSettings Settings(bool pkce = true) => new(
        ProviderKind.Google, new Uri("https://auth.example/authorize"), new Uri("https://auth.example/token"), "client-1", "secret-1", "calendar", pkce, new Dictionary<string, string> { ["access_type"] = "offline" });

    [Fact]
    public async Task Full_sign_in_flow_stores_tokens_securely()
    {
        string? tokenRequest = null;
        var tokenHandler = new RoutingHandler().On("auth.example/token", req =>
        {
            tokenRequest = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"AT1","refresh_token":"RT1","expires_in":3600}""") };
        });
        var launcher = new AutoApproveLauncher(q => $"code=CODE123&state={q["state"]}");
        var store = new InMemorySecureTokenStore();
        var session = new OAuthSession(Settings(), new HttpClient(tokenHandler), new LoopbackBrowser(launcher), store, new FixedClock(DateTimeOffset.UnixEpoch), NullLogger<OAuthSession>.Instance)
        {
            ResolveAccountName = (_, _) => Task.FromResult<string?>("me@gmail.com"),
        };

        var result = await session.SignInAsync();

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.DisplayName.Should().Be("me@gmail.com");
        launcher.Opened!.Query.Should().Contain("code_challenge_method=S256").And.Contain("access_type=offline").And.Contain("redirect_uri=http%3A%2F%2F127.0.0.1");
        tokenRequest.Should().Contain("code=CODE123").And.Contain("code_verifier=").And.Contain("client_secret=secret-1");
        (await store.GetAsync("oauth:Google")).Should().Contain("AT1");
        (await session.GetAccessTokenAsync()).Value.Should().Be("AT1");
    }

    [Fact]
    public async Task Wrong_state_is_rejected()
    {
        var launcher = new AutoApproveLauncher(_ => "code=EVIL&state=forged");
        var session = new OAuthSession(Settings(), new HttpClient(new RoutingHandler()), new LoopbackBrowser(launcher), new InMemorySecureTokenStore(), new FixedClock(DateTimeOffset.UnixEpoch), NullLogger<OAuthSession>.Instance);

        var result = await session.SignInAsync();

        result.Error.Code.Should().Be("auth.state");
    }

    [Fact]
    public async Task Expired_token_is_refreshed_and_refresh_token_kept()
    {
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var store = new InMemorySecureTokenStore();
        await store.SetAsync("oauth:Google", System.Text.Json.JsonSerializer.Serialize(new OAuthTokens("OLD", "RT1", clock.UtcNow.AddMinutes(1), "me@gmail.com")));
        string? form = null;
        var handler = new RoutingHandler().On("auth.example/token", req =>
        {
            form = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"NEW","expires_in":3600}""") };
        });
        var session = new OAuthSession(Settings(), new HttpClient(handler), new LoopbackBrowser(new AutoApproveLauncher(_ => string.Empty)), store, clock, NullLogger<OAuthSession>.Instance);

        var token = await session.GetAccessTokenAsync();

        token.Value.Should().Be("NEW");
        form.Should().Contain("grant_type=refresh_token").And.Contain("refresh_token=RT1");
        (await store.GetAsync("oauth:Google")).Should().Contain("RT1").And.Contain("me@gmail.com");
    }

    [Fact]
    public async Task Revoked_refresh_token_requires_new_login()
    {
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var store = new InMemorySecureTokenStore();
        await store.SetAsync("oauth:Google", System.Text.Json.JsonSerializer.Serialize(new OAuthTokens("OLD", "RT1", clock.UtcNow, null)));
        var handler = new RoutingHandler().On("auth.example/token", HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        var session = new OAuthSession(Settings(), new HttpClient(handler), new LoopbackBrowser(new AutoApproveLauncher(_ => string.Empty)), store, clock, NullLogger<OAuthSession>.Instance);

        var token = await session.GetAccessTokenAsync();

        token.Error.Code.Should().Be("auth.not_connected");
    }

    [Fact]
    public async Task Missing_client_id_is_a_config_error()
    {
        var settings = Settings() with { ClientId = string.Empty };
        var session = new OAuthSession(settings, new HttpClient(new RoutingHandler()), new LoopbackBrowser(new AutoApproveLauncher(_ => string.Empty)), new InMemorySecureTokenStore(), new FixedClock(DateTimeOffset.UnixEpoch), NullLogger<OAuthSession>.Instance);

        (await session.SignInAsync()).Error.Code.Should().Be("auth.config");
    }
}
