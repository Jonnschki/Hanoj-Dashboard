using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.InMemory;
using LifeDashboard.Core.Sync;
using LifeDashboard.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeDashboard.Infrastructure.Tests;

/// <summary>Temporäre SQLite-Datei pro Test, wird danach gelöscht.</summary>
public sealed class TempDatabase : IAsyncDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ld-test-{Guid.NewGuid():N}.db3");

    public SqliteDatabase Open() => new(Path);

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // Datei noch gesperrt: bleibt im Temp-Ordner liegen, kein Testfehler.
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Vertragstests: Jede ISyncLedger-Implementierung muss sich gleich verhalten.
/// Laufen gegen InMemory (Referenz) und SQLite.
/// </summary>
public abstract class SyncLedgerContractTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    protected ISyncLedger Ledger { get; private set; } = null!;

    public async Task InitializeAsync() => Ledger = await CreateAsync();

    public virtual Task DisposeAsync() => Task.CompletedTask;

    protected abstract Task<ISyncLedger> CreateAsync();

    private static LedgerEntry Entry(string origin, string target, string targetEvent = "t1", string? source = "src") => new(
        origin, ProviderKind.Google, source, "native-1", T0, target, targetEvent, "HASH", T0.AddMinutes(1), T0.AddMinutes(2), LedgerStatus.Active);

    [Fact]
    public async Task Upsert_then_find_returns_identical_entry()
    {
        var entry = Entry("evt|a|1", "cal-b");

        await Ledger.UpsertAsync(entry);

        (await Ledger.FindAsync("evt|a|1", "cal-b")).Should().Be(entry);
        (await Ledger.FindAsync("evt|a|1", "cal-x")).Should().BeNull();
    }

    [Fact]
    public async Task Upsert_replaces_existing_entry_with_same_key()
    {
        await Ledger.UpsertAsync(Entry("evt|a|1", "cal-b"));
        var updated = Entry("evt|a|1", "cal-b") with { SyncHash = "NEW", TargetEventId = "t2" };

        await Ledger.UpsertAsync(updated);

        (await Ledger.FindAsync("evt|a|1", "cal-b")).Should().Be(updated);
        (await Ledger.GetBySourceCalendarAsync("src")).Should().ContainSingle();
    }

    [Fact]
    public async Task Find_by_target_event_and_by_source_calendar()
    {
        await Ledger.UpsertAsync(Entry("evt|a|1", "cal-b", "t1"));
        await Ledger.UpsertAsync(Entry("evt|a|2", "cal-b", "t2"));
        await Ledger.UpsertAsync(Entry("act|Strava|9", "cal-s", "t3", source: null));

        (await Ledger.FindByTargetEventAsync("cal-b", "t2"))!.OriginKey.Should().Be("evt|a|2");
        (await Ledger.FindByTargetEventAsync("cal-b", "nope")).Should().BeNull();
        (await Ledger.GetBySourceCalendarAsync("src")).Should().HaveCount(2);
    }

    [Fact]
    public async Task Remove_deletes_only_the_given_key()
    {
        await Ledger.UpsertAsync(Entry("evt|a|1", "cal-b"));
        await Ledger.UpsertAsync(Entry("evt|a|1", "cal-c"));

        await Ledger.RemoveAsync("evt|a|1", "cal-b");

        (await Ledger.FindAsync("evt|a|1", "cal-b")).Should().BeNull();
        (await Ledger.FindAsync("evt|a|1", "cal-c")).Should().NotBeNull();
    }

    [Fact]
    public async Task Recent_runs_are_returned_newest_first_and_limited()
    {
        for (var i = 0; i < 5; i++)
        {
            await Ledger.AddRunAsync(new SyncRunRecord(Guid.NewGuid(), T0.AddHours(i), T0.AddHours(i).AddMinutes(1), i % 2 == 0, SyncRunStatus.Succeeded, i, 0, 0, 0, 0));
        }

        var runs = await Ledger.GetRecentRunsAsync(3);

        runs.Should().HaveCount(3);
        runs.Select(r => r.Created).Should().Equal(4, 3, 2);
        runs[0].DryRun.Should().BeTrue();
        runs[0].FinishedUtc.Should().Be(T0.AddHours(4).AddMinutes(1));
    }
}

public sealed class InMemorySyncLedgerContractTests : SyncLedgerContractTests
{
    protected override Task<ISyncLedger> CreateAsync() => Task.FromResult<ISyncLedger>(new InMemorySyncLedger());
}

public sealed class SqliteSyncLedgerContractTests : SyncLedgerContractTests
{
    private readonly TempDatabase _temp = new();
    private SqliteDatabase? _db;

    protected override Task<ISyncLedger> CreateAsync()
    {
        _db = _temp.Open();
        return Task.FromResult<ISyncLedger>(new SqliteSyncLedger(_db));
    }

    public override async Task DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        await _temp.DisposeAsync();
    }
}

public sealed class SqliteDatabaseTests
{
    [Fact]
    public async Task Fresh_database_is_migrated_to_current_version_and_reopening_is_idempotent()
    {
        await using var temp = new TempDatabase();

        await using (var db = temp.Open())
        {
            (await db.GetSchemaVersionAsync()).Should().Be(DatabaseSchema.CurrentVersion);
        }

        await using (var reopened = temp.Open())
        {
            (await reopened.GetSchemaVersionAsync()).Should().Be(DatabaseSchema.CurrentVersion);
        }
    }

    [Fact]
    public async Task Ledger_data_survives_app_restart()
    {
        await using var temp = new TempDatabase();
        var entry = new LedgerEntry("evt|a|1", ProviderKind.Microsoft, "a", "n1", DateTimeOffset.UnixEpoch, "b", "t1", "H", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, LedgerStatus.Active);

        await using (var db = temp.Open())
        {
            await new SqliteSyncLedger(db).UpsertAsync(entry);
        }

        await using (var db = temp.Open())
        {
            (await new SqliteSyncLedger(db).FindAsync("evt|a|1", "b")).Should().Be(entry);
        }
    }

    [Fact]
    public async Task Sync_with_sqlite_ledger_stays_idempotent_across_restarts()
    {
        await using var temp = new TempDatabase();
        var clock = new SimpleClock(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));
        var google = new InMemoryCalendarProvider(new CalendarAccount("g", ProviderKind.Google, "G", null), clock);
        var a = google.AddCalendar("a", "A", CalendarOwnership.Own, CalendarCategory.Private);
        var b = google.AddCalendar("b", "B", CalendarOwnership.Own, CalendarCategory.Private);
        google.Seed(a, new CalendarEvent(string.Empty, string.Empty, "Termin", null, null,
            new TimedSchedule(clock.UtcNow.AddDays(1), clock.UtcNow.AddDays(1).AddHours(1), "Europe/Vienna"), EventVisibility.Normal, ShowAs.Busy, clock.UtcNow));
        var request = new SyncRequest { MirrorCalendarIds = [a.Id, b.Id], Window = DateRange.Around(clock.UtcNow, TimeSpan.FromDays(1), TimeSpan.FromDays(10)), DryRun = false };

        await using (var db = temp.Open())
        {
            await Engine(google, new SqliteSyncLedger(db), clock).RunAsync(request);
        }

        // "Neustart": neue Verbindung, neues Ledger-Objekt, dieselben Kalender.
        DryRunReport second;
        await using (var db = temp.Open())
        {
            second = await Engine(google, new SqliteSyncLedger(db), clock).RunAsync(request);
            (await new SqliteSyncLedger(db).GetRecentRunsAsync(10)).Should().HaveCount(2);
        }

        second.Created.Should().Be(0);
        second.Unchanged.Should().Be(1);
        google.EventsOf(b).Should().ContainSingle();
    }

    private static SyncEngine Engine(ICalendarProvider provider, ISyncLedger ledger, IClock clock)
        => new([provider], [], ledger, clock, new SyncRules(), new ConflictResolver(ConflictStrategy.LastModifiedWins), NullLogger<SyncEngine>.Instance);

    private sealed class SimpleClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;

        public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

public sealed class SqliteUserSettingsStoreTests
{
    [Fact]
    public async Task Returns_defaults_when_nothing_was_saved()
    {
        await using var temp = new TempDatabase();
        await using var db = temp.Open();

        var settings = await new SqliteUserSettingsStore(db, NullLogger<SqliteUserSettingsStore>.Instance).LoadAsync();

        settings.Should().BeEquivalentTo(new UserSettings());
    }

    [Fact]
    public async Task Saved_settings_round_trip_across_restart()
    {
        await using var temp = new TempDatabase();
        var saved = new UserSettings
        {
            HasCalendarSelection = true,
            SelectedCalendarIds = ["Google:g:a", "Microsoft:m:w"],
            ActivityTargetCalendarId = "Google:g:sport",
            PrivateWorkDetailLevel = MirrorDetailLevel.None,
            DigestTime = new TimeOnly(7, 15),
            Watchlist = ["SPY", "AAPL"],
        };

        await using (var db = temp.Open())
        {
            await new SqliteUserSettingsStore(db, NullLogger<SqliteUserSettingsStore>.Instance).SaveAsync(saved);
        }

        await using (var db = temp.Open())
        {
            var loaded = await new SqliteUserSettingsStore(db, NullLogger<SqliteUserSettingsStore>.Instance).LoadAsync();
            loaded.Should().BeEquivalentTo(saved);
        }
    }

    [Fact]
    public async Task Corrupt_settings_fall_back_to_defaults()
    {
        await using var temp = new TempDatabase();
        await using var db = temp.Open();
        var connection = await db.GetConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO Setting (Key, Value) VALUES ('user', '{kaputt')");

        var settings = await new SqliteUserSettingsStore(db, NullLogger<SqliteUserSettingsStore>.Instance).LoadAsync();

        settings.Should().BeEquivalentTo(new UserSettings());
    }
}
