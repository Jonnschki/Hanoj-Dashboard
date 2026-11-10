using LifeDashboard.Core.Digest;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.Sync;
using LifeDashboard.Core.Tests.TestDoubles;

namespace LifeDashboard.Core.Tests.Domain;

public sealed class DomainTests
{
    [Fact]
    public void Timed_schedule_rejects_non_utc_timestamps()
    {
        var local = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.FromHours(1));

        var act = () => new TimedSchedule(local, local.AddHours(1), "Europe/Vienna");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void All_day_schedule_requires_at_least_one_day()
    {
        var day = new DateOnly(2026, 3, 1);

        var act = () => new AllDaySchedule(day, day);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Hash_ignores_ids_and_timestamps_but_detects_content_changes()
    {
        var a = SyncTestBed.NewEvent("A");
        var sameContent = a with { Id = "x", CalendarId = "y", LastModifiedUtc = a.LastModifiedUtc.AddDays(3) };
        var changed = a with { Title = "B" };

        EventMapping.ComputeHash(sameContent).Should().Be(EventMapping.ComputeHash(a));
        EventMapping.ComputeHash(changed).Should().NotBe(EventMapping.ComputeHash(a));
    }

    [Fact]
    public void Private_event_is_busy_only_even_between_private_calendars()
    {
        var rules = new SyncRules();
        var bed = new SyncTestBed();
        var privateEvent = SyncTestBed.NewEvent("Geheim") with { Visibility = EventVisibility.Private };

        rules.Resolve(bed.PrivateA, bed.PrivateB, privateEvent).Should().Be(MirrorDetailLevel.BusyOnly);
    }

    [Fact]
    public void Digest_today_range_respects_daylight_saving_time()
    {
        // 29.03.2026: Umstellung auf Sommerzeit in Wien → der Tag hat nur 23 Stunden.
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 29, 9, 0, 0, TimeSpan.Zero))
        {
            LocalTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna"),
        };

        var range = new DailyDigestBuilder(clock).TodayRange();

        (range.EndUtc - range.StartUtc).Should().Be(TimeSpan.FromHours(23));
        range.StartUtc.Should().Be(new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero));
    }
}
