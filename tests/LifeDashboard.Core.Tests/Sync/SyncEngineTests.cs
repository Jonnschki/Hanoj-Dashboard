using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.Sync;
using LifeDashboard.Core.Tests.TestDoubles;
using NSubstitute;

namespace LifeDashboard.Core.Tests.Sync;

public sealed class SyncEngineTests
{
    // ------------------------------------------------------------ Idempotenz

    [Fact]
    public async Task Syncing_twice_creates_no_duplicates()
    {
        var bed = new SyncTestBed();
        bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Zahnarzt"));

        var first = await bed.Engine.RunAsync(bed.Request());
        var writesAfterFirst = bed.Google.WriteCount + bed.Microsoft.WriteCount;
        var second = await bed.Engine.RunAsync(bed.Request());

        first.Created.Should().Be(2, "PrivateB und Work erhalten je eine Spiegelung");
        second.Created.Should().Be(0);
        second.Updated.Should().Be(0);
        second.Unchanged.Should().Be(2);
        (bed.Google.WriteCount + bed.Microsoft.WriteCount).Should().Be(writesAfterFirst);
        bed.Google.EventsOf(bed.PrivateB).Should().ContainSingle();
        bed.Microsoft.EventsOf(bed.Work).Should().ContainSingle();
    }

    [Fact]
    public async Task Changed_source_updates_existing_mirror_instead_of_creating_new_one()
    {
        var bed = new SyncTestBed();
        var original = bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Training"));
        await bed.Engine.RunAsync(bed.Request());

        bed.Clock.Advance(TimeSpan.FromHours(1));
        bed.Google.EditByUser(bed.PrivateA, original.Id, e => e with { Title = "Training (verschoben)" });
        var report = await bed.Engine.RunAsync(bed.Request());

        report.Updated.Should().Be(1, "nur PrivateB zeigt den Titel; Work sieht nur 'Beschäftigt'");
        report.Created.Should().Be(0);
        bed.Google.EventsOf(bed.PrivateB).Should().ContainSingle().Which.Title.Should().Be("Training (verschoben)");
    }

    // ------------------------------------------------------------ Schleifenschutz

    [Fact]
    public async Task Mirrors_carry_origin_marker_and_are_never_mirrored_back()
    {
        var bed = new SyncTestBed();
        var original = bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Konzert"));

        await bed.Engine.RunAsync(bed.Request());
        await bed.Engine.RunAsync(bed.Request());
        await bed.Engine.RunAsync(bed.Request());

        var mirror = bed.Google.EventsOf(bed.PrivateB).Should().ContainSingle().Subject;
        mirror.Origin.Should().NotBeNull();
        mirror.Origin!.OriginId.Should().Be(OriginKey.ForEvent(bed.PrivateA.Id, original.Id));
        mirror.Origin.SyncHash.Should().NotBeNullOrEmpty();

        bed.Google.EventsOf(bed.PrivateA).Should().ContainSingle("die Spiegelung in B darf nicht nach A zurückgespiegelt werden");
        bed.Microsoft.EventsOf(bed.Work).Should().ContainSingle();
    }

    [Fact]
    public async Task Mirror_that_lost_its_marker_is_still_recognised_via_ledger()
    {
        var bed = new SyncTestBed();
        bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Kino"));
        await bed.Engine.RunAsync(bed.Request());

        // Ein Anbieter "verliert" den Marker (z. B. weil der Termin in einer Fremd-App bearbeitet wurde).
        var mirror = bed.Google.EventsOf(bed.PrivateB).Single();
        bed.Google.EditByUser(bed.PrivateB, mirror.Id, e => e with { Origin = null });

        await bed.Engine.RunAsync(bed.Request());

        bed.Google.EventsOf(bed.PrivateA).Should().ContainSingle();
        bed.Microsoft.EventsOf(bed.Work).Should().ContainSingle();
    }

    // ------------------------------------------------------------ Read-only-Schutz

    [Fact]
    public async Task Shared_calendar_is_never_written_even_if_requested_for_mirroring()
    {
        var bed = new SyncTestBed();
        var bossMeeting = bed.Microsoft.Seed(bed.Boss, SyncTestBed.NewEvent("Vorstandsrunde"));
        bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Arzt"));

        var report = await bed.Engine.RunAsync(bed.Request(dryRun: false, confirmDeletes: true, bed.PrivateA, bed.Boss, bed.Work));

        bed.Microsoft.EventsOf(bed.Boss).Should().ContainSingle().Which.Should().Be(bossMeeting);
        report.Actions.Should().NotContain(a => a.Target.Id == bed.Boss.Id);
        report.Log.Should().Contain(l => l.Level == "Warning" && l.Message.Contains("read-only"));
        bed.Microsoft.EventsOf(bed.Work).Should().ContainSingle("nur der eigene Termin aus PrivateA, nicht der Chef-Termin");
    }

    [Fact]
    public void Write_gate_rejects_shared_calendar_before_asking_the_provider()
    {
        var bed = new SyncTestBed();

        var act = () => CalendarWriteGate.Open(bed.Microsoft, bed.Boss);

        act.Should().Throw<ReadOnlyCalendarException>().Which.CalendarId.Should().Be(bed.Boss.Id);
        bed.Microsoft.WriterRequests.Should().Be(0);
    }

    [Fact]
    public void In_memory_provider_itself_refuses_writer_for_shared_calendar()
    {
        var bed = new SyncTestBed();

        var act = () => bed.Microsoft.GetWriter(bed.Boss);

        act.Should().Throw<ReadOnlyCalendarException>();
    }

    [Fact]
    public async Task Write_gate_blocks_buggy_provider_that_hands_out_writer_for_foreign_calendar()
    {
        var bed = new SyncTestBed();
        var buggyWriter = Substitute.For<ICalendarWriter>();
        buggyWriter.Calendar.Returns(bed.Boss);
        var buggyProvider = Substitute.For<ICalendarProvider>();
        buggyProvider.GetWriter(Arg.Any<CalendarInfo>()).Returns(buggyWriter);

        var writer = CalendarWriteGate.Open(buggyProvider, bed.Work);
        var act = () => writer.CreateAsync(SyncTestBed.NewEvent("x"));

        await act.Should().ThrowAsync<ReadOnlyCalendarException>();
        await buggyWriter.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    // ------------------------------------------------------------ Dry-Run

    [Fact]
    public async Task Dry_run_plans_changes_but_writes_nothing()
    {
        var bed = new SyncTestBed();
        bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Geburtstag"));

        var report = await bed.Engine.RunAsync(bed.Request(dryRun: true));

        report.DryRun.Should().BeTrue();
        report.Created.Should().Be(2);
        report.Actions.Should().OnlyContain(a => a.Outcome == SyncActionOutcome.Planned);
        bed.Google.EventsOf(bed.PrivateB).Should().BeEmpty();
        bed.Microsoft.EventsOf(bed.Work).Should().BeEmpty();
        bed.Ledger.Entries.Should().BeEmpty();
        (bed.Google.WriteCount + bed.Microsoft.WriteCount).Should().Be(0);
        bed.Google.WriterRequests.Should().Be(0);
    }

    [Fact]
    public async Task Dry_run_after_real_sync_reports_pending_delete_without_deleting()
    {
        var bed = new SyncTestBed();
        var original = bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Absage"));
        await bed.Engine.RunAsync(bed.Request());
        bed.Google.DeleteByUser(bed.PrivateA, original.Id);

        var report = await bed.Engine.RunAsync(bed.Request(dryRun: true, confirmDeletes: true));

        report.Deleted.Should().Be(2);
        bed.Google.EventsOf(bed.PrivateB).Should().ContainSingle();
        bed.Ledger.Entries.Should().HaveCount(2);
    }

    // ------------------------------------------------------------ Löschungen

    [Fact]
    public async Task Deleted_original_only_marks_mirror_for_confirmation_by_default()
    {
        var bed = new SyncTestBed();
        var original = bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Termin"));
        await bed.Engine.RunAsync(bed.Request());
        bed.Google.DeleteByUser(bed.PrivateA, original.Id);

        var report = await bed.Engine.RunAsync(bed.Request(confirmDeletes: false));

        report.PendingDeletes.Should().Be(2);
        report.Deleted.Should().Be(0);
        bed.Google.EventsOf(bed.PrivateB).Should().ContainSingle();
    }

    [Fact]
    public async Task Deleted_original_removes_mirror_when_confirmed()
    {
        var bed = new SyncTestBed();
        var original = bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Termin"));
        await bed.Engine.RunAsync(bed.Request());
        bed.Google.DeleteByUser(bed.PrivateA, original.Id);

        var report = await bed.Engine.RunAsync(bed.Request(confirmDeletes: true));

        report.Deleted.Should().Be(2);
        bed.Google.EventsOf(bed.PrivateB).Should().BeEmpty();
        bed.Microsoft.EventsOf(bed.Work).Should().BeEmpty();
        bed.Ledger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Events_not_created_by_the_app_are_never_deleted()
    {
        var bed = new SyncTestBed();
        var mine = bed.Google.Seed(bed.PrivateB, SyncTestBed.NewEvent("Eigener Termin in B"));

        await bed.Engine.RunAsync(bed.Request(confirmDeletes: true));
        await bed.Engine.RunAsync(bed.Request(confirmDeletes: true));

        bed.Google.EventsOf(bed.PrivateB).Should().ContainSingle(e => e.Id == mine.Id);
    }

    // ------------------------------------------------------------ Datenschutz

    [Fact]
    public async Task Private_to_work_mirror_contains_no_details_by_default()
    {
        var bed = new SyncTestBed();
        bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Therapie", description: "sehr privat"));

        await bed.Engine.RunAsync(bed.Request());

        var workMirror = bed.Microsoft.EventsOf(bed.Work).Should().ContainSingle().Subject;
        workMirror.Title.Should().Be(bed.Rules.BusyTitle);
        workMirror.Description.Should().BeNull();
        workMirror.Location.Should().BeNull();
        workMirror.ShowAs.Should().Be(ShowAs.Busy);

        bed.Google.EventsOf(bed.PrivateB).Single().Title.Should().Be("Therapie", "privat → privat übernimmt Details");
    }

    // ------------------------------------------------------------ Konflikte

    [Fact]
    public async Task Conflict_last_modified_wins_source_newer_overwrites_mirror()
    {
        var bed = new SyncTestBed();
        var original = bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Original"));
        await bed.Engine.RunAsync(bed.Request());
        var mirror = bed.Google.EventsOf(bed.PrivateB).Single();

        bed.Clock.Advance(TimeSpan.FromMinutes(5));
        bed.Google.EditByUser(bed.PrivateB, mirror.Id, e => e with { Title = "Manuell in B" });
        bed.Clock.Advance(TimeSpan.FromMinutes(5));
        bed.Google.EditByUser(bed.PrivateA, original.Id, e => e with { Title = "Neu in A" });

        var report = await bed.Engine.RunAsync(bed.Request());

        report.Conflicts.Should().Be(1);
        bed.Google.EventsOf(bed.PrivateB).Single().Title.Should().Be("Neu in A");
        report.Log.Should().Contain(l => l.Message.Contains("Konflikt"));
    }

    [Fact]
    public async Task Conflict_last_modified_wins_target_newer_keeps_manual_change()
    {
        var bed = new SyncTestBed();
        var original = bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Original"));
        await bed.Engine.RunAsync(bed.Request());
        var mirror = bed.Google.EventsOf(bed.PrivateB).Single();

        bed.Clock.Advance(TimeSpan.FromMinutes(5));
        bed.Google.EditByUser(bed.PrivateA, original.Id, e => e with { Title = "Neu in A" });
        bed.Clock.Advance(TimeSpan.FromMinutes(5));
        bed.Google.EditByUser(bed.PrivateB, mirror.Id, e => e with { Title = "Manuell in B" });

        var report = await bed.Engine.RunAsync(bed.Request());

        report.Conflicts.Should().Be(1);
        bed.Google.EventsOf(bed.PrivateB).Single().Title.Should().Be("Manuell in B");
    }

    // ------------------------------------------------------------ Strava

    [Fact]
    public async Task Activities_are_imported_once_and_updated_on_change()
    {
        var bed = new SyncTestBed();
        var run = new SportActivity("123", ProviderKind.Strava, "Morgenlauf", "Run", SyncTestBed.Start.AddHours(-2), "Europe/Vienna", TimeSpan.FromMinutes(42), TimeSpan.FromMinutes(40), 8050, "https://www.strava.com/activities/123");
        bed.Activities.Activities.Add(run);
        var request = bed.Request() with { ActivityTargetCalendarId = bed.PrivateB.Id, ActivitiesSinceUtc = SyncTestBed.Start.AddDays(-1) };

        await bed.Engine.RunAsync(request);
        await bed.Engine.RunAsync(request);
        bed.Activities.Activities[0] = run with { Name = "Morgenlauf im Prater" };
        var third = await bed.Engine.RunAsync(request);

        var imported = bed.Google.EventsOf(bed.PrivateB).Where(e => e.Origin?.OriginId == OriginKey.ForActivity(ProviderKind.Strava, "123")).ToList();
        imported.Should().ContainSingle();
        imported[0].Title.Should().Be("Run: Morgenlauf im Prater");
        imported[0].Description.Should().Contain("8.05 km");
        third.Updated.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Failed_calendar_load_does_not_delete_mirrors()
    {
        var bed = new SyncTestBed();
        bed.Google.Seed(bed.PrivateA, SyncTestBed.NewEvent("Bleibt"));
        await bed.Engine.RunAsync(bed.Request());

        var flaky = Substitute.For<ICalendarProvider>();
        flaky.Account.Returns(bed.Google.Account);
        flaky.GetCalendarsAsync(Arg.Any<CancellationToken>()).Returns(bed.Google.GetCalendarsAsync());
        flaky.GetEventsAsync(Arg.Any<CalendarInfo>(), Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<CalendarEvent>>(Errors.ProviderFailure("Google", "timeout")));
        var engine = new SyncEngine([flaky, bed.Microsoft], [], bed.Ledger, bed.Clock, bed.Rules, new ConflictResolver(ConflictStrategy.LastModifiedWins), Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncEngine>.Instance);

        var report = await engine.RunAsync(bed.Request(confirmDeletes: true));

        report.Deleted.Should().Be(0);
        bed.Microsoft.EventsOf(bed.Work).Should().ContainSingle();
        bed.Ledger.Entries.Should().HaveCount(2);
    }
}
