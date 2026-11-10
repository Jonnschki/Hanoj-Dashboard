using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.InMemory;

namespace LifeDashboard.Infrastructure.Demo;

/// <summary>
/// Beispieldaten für den Demo-Modus (App:UseDemoData = true): zwei Konten, vier Kalender, ein Chef-Kalender (read-only).
/// Damit lässt sich die SyncEngine in der App ohne echte Konten ausprobieren.
/// </summary>
public static class DemoData
{
    public static IReadOnlyList<ICalendarProvider> CreateCalendarProviders(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var work = new InMemoryCalendarProvider(new CalendarAccount("demo-ms", ProviderKind.Microsoft, "Arbeit (Demo)", "jonah@firma.example"), clock);
        var personal = new InMemoryCalendarProvider(new CalendarAccount("demo-g", ProviderKind.Google, "Privat (Demo)", "jonah@gmail.example"), clock);

        var workCal = work.AddCalendar("calendar", "Outlook – Arbeit", CalendarOwnership.Own, CalendarCategory.Work);
        var bossCal = work.AddCalendar("boss", "Chef (geteilt)", CalendarOwnership.SharedReadOnly, CalendarCategory.Work);
        var privateCal = personal.AddCalendar("primary", "Google – Privat", CalendarOwnership.Own, CalendarCategory.Private);
        personal.AddCalendar("sport", "Google – Sport", CalendarOwnership.Own, CalendarCategory.Private);

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        work.Seed(workCal, Timed("Team-Standup", today, 8, 0, 15));
        work.Seed(workCal, Timed("Projekt-Review", today.AddDays(1), 13, 0, 60));
        work.Seed(bossCal, Timed("Vorstandsrunde", today, 10, 0, 90));
        personal.Seed(privateCal, Timed("Zahnarzt", today.AddDays(2), 16, 30, 45));
        personal.Seed(privateCal, new CalendarEvent(string.Empty, string.Empty, "Urlaub", null, "Kärnten", new AllDaySchedule(today.AddDays(10), today.AddDays(14)), EventVisibility.Normal, ShowAs.OutOfOffice, clock.UtcNow));

        return [work, personal];
    }

    private static CalendarEvent Timed(string title, DateOnly day, int hour, int minute, int durationMinutes)
    {
        var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);
        return new CalendarEvent(string.Empty, string.Empty, title, "Demo-Termin", null, new TimedSchedule(start, start.AddMinutes(durationMinutes), "Europe/Vienna"), EventVisibility.Normal, ShowAs.Busy, start);
    }
}

/// <summary>Demo-Aktivitäten statt Strava.</summary>
public sealed class DemoActivityProvider(IClock clock) : IActivityProvider
{
    public ProviderKind Kind => ProviderKind.Strava;

    public Task<Result<IReadOnlyList<SportActivity>>> GetActivitiesAsync(DateTimeOffset afterUtc, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        IReadOnlyList<SportActivity> list =
        [
            new("1001", ProviderKind.Strava, "Morgenlauf", "Run", now.AddDays(-1).AddHours(-3), "Europe/Vienna", TimeSpan.FromMinutes(46), TimeSpan.FromMinutes(44), 8400, "https://www.strava.com/activities/1001"),
            new("1002", ProviderKind.Strava, "Feierabendrunde", "Ride", now.AddDays(-3), "Europe/Vienna", TimeSpan.FromMinutes(95), TimeSpan.FromMinutes(88), 41200, "https://www.strava.com/activities/1002"),
        ];
        return Task.FromResult(Result.Success<IReadOnlyList<SportActivity>>(list.Where(a => a.StartUtc >= afterUtc).ToList()));
    }
}
