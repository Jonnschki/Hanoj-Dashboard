using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.InMemory;
using LifeDashboard.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeDashboard.Core.Tests.TestDoubles;

/// <summary>Steuerbare Uhr: jeder Zugriff liefert dieselbe Zeit, bis <see cref="Advance"/> aufgerufen wird.</summary>
public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public TimeZoneInfo LocalTimeZone { get; set; } = TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Fake für Strava & Co.</summary>
public sealed class FakeActivityProvider : IActivityProvider
{
    public List<SportActivity> Activities { get; } = [];

    public ProviderKind Kind => ProviderKind.Strava;

    public Task<Result<IReadOnlyList<SportActivity>>> GetActivitiesAsync(DateTimeOffset afterUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success<IReadOnlyList<SportActivity>>(Activities.Where(a => a.StartUtc >= afterUtc).ToList()));
}

/// <summary>
/// Aufbau für Sync-Tests:
/// Google (privat): PrivateA, PrivateB (eigen).
/// Microsoft (Arbeit): Work (eigen), Boss (fremd, read-only).
/// </summary>
public sealed class SyncTestBed
{
    public static readonly DateTimeOffset Start = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    public SyncTestBed(ConflictStrategy strategy = ConflictStrategy.LastModifiedWins)
    {
        Clock = new FakeClock(Start);
        Google = new InMemoryCalendarProvider(new CalendarAccount("g1", ProviderKind.Google, "Privat", "me@gmail.com"), Clock);
        Microsoft = new InMemoryCalendarProvider(new CalendarAccount("m1", ProviderKind.Microsoft, "Arbeit", "me@work.com"), Clock);

        PrivateA = Google.AddCalendar("a", "Privat A", CalendarOwnership.Own, CalendarCategory.Private);
        PrivateB = Google.AddCalendar("b", "Privat B", CalendarOwnership.Own, CalendarCategory.Private);
        Work = Microsoft.AddCalendar("w", "Arbeit", CalendarOwnership.Own, CalendarCategory.Work);
        Boss = Microsoft.AddCalendar("boss", "Chef", CalendarOwnership.SharedReadOnly, CalendarCategory.Work);

        Engine = new SyncEngine(
            [Google, Microsoft],
            [Activities],
            Ledger,
            Clock,
            Rules,
            new ConflictResolver(strategy),
            NullLogger<SyncEngine>.Instance);
    }

    public FakeClock Clock { get; }

    public InMemoryCalendarProvider Google { get; }

    public InMemoryCalendarProvider Microsoft { get; }

    public FakeActivityProvider Activities { get; } = new();

    public InMemorySyncLedger Ledger { get; } = new();

    public SyncRules Rules { get; } = new();

    public SyncEngine Engine { get; }

    public CalendarInfo PrivateA { get; }

    public CalendarInfo PrivateB { get; }

    public CalendarInfo Work { get; }

    public CalendarInfo Boss { get; }

    public DateRange Window => new(Start.AddDays(-7), Start.AddDays(30));

    public SyncRequest Request(bool dryRun = false, bool confirmDeletes = false, params CalendarInfo[] calendars) => new()
    {
        MirrorCalendarIds = (calendars.Length == 0 ? [PrivateA, PrivateB, Work] : calendars).Select(c => c.Id).ToList(),
        Window = Window,
        DryRun = dryRun,
        ConfirmDeletes = confirmDeletes,
    };

    public static CalendarEvent NewEvent(string title, int dayOffset = 1, int hour = 10, string? description = "Details") => new(
        Id: string.Empty,
        CalendarId: string.Empty,
        Title: title,
        Description: description,
        Location: "Wien",
        Schedule: new TimedSchedule(Start.Date.AddDays(dayOffset).AddHours(hour).ToUniversalTimeOffset(), Start.Date.AddDays(dayOffset).AddHours(hour + 1).ToUniversalTimeOffset(), "Europe/Vienna"),
        Visibility: EventVisibility.Normal,
        ShowAs: ShowAs.Busy,
        LastModifiedUtc: Start);
}

internal static class DateTimeTestExtensions
{
    public static DateTimeOffset ToUniversalTimeOffset(this DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);
}
