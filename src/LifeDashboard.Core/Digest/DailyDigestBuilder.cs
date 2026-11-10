using System.Globalization;
using System.Text;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Digest;

/// <summary>Inhalt des morgendlichen Digests.</summary>
public sealed record DailyDigest(
    DateOnly Date,
    IReadOnlyList<CalendarEvent> TodaysEvents,
    IReadOnlyList<MarketQuote> Quotes,
    string? MarketSource)
{
    /// <summary>Kurztext für die Benachrichtigung.</summary>
    public string ToNotificationText()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{TodaysEvents.Count} Termine heute");
        foreach (var q in Quotes.Take(3))
        {
            sb.Append(CultureInfo.InvariantCulture, $" · {q.Symbol} {q.ChangePercent:+0.0;-0.0}%");
        }

        return sb.ToString();
    }
}

/// <summary>Stellt den Daily Digest aus Kalendern und Marktdaten zusammen.</summary>
public sealed class DailyDigestBuilder(IClock clock)
{
    /// <summary>Lokales "Heute" als UTC-Zeitfenster (berücksichtigt Sommerzeit).</summary>
    public DateRange TodayRange()
    {
        var tz = clock.LocalTimeZone;
        var localNow = TimeZoneInfo.ConvertTime(clock.UtcNow, tz);
        var localMidnight = localNow.Date;
        var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), tz);
        var end = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight.AddDays(1), DateTimeKind.Unspecified), tz);
        return new DateRange(new DateTimeOffset(start, TimeSpan.Zero), new DateTimeOffset(end, TimeSpan.Zero));
    }

    /// <summary>Baut den Digest. Termine werden nach Beginn sortiert, Ganztagstermine zuerst.</summary>
    public DailyDigest Build(IEnumerable<CalendarEvent> events, IEnumerable<MarketQuote> quotes, string? marketSource)
    {
        var range = TodayRange();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, clock.LocalTimeZone).Date);

        var todays = events
            .Where(e => e.Schedule switch
            {
                AllDaySchedule a => a.StartDate <= today && a.EndDateExclusive > today,
                TimedSchedule t => range.Overlaps(t.StartUtc, t.EndUtc),
                _ => false,
            })
            .OrderBy(e => e.Schedule is AllDaySchedule ? 0 : 1)
            .ThenBy(e => e.Schedule.SortKeyUtc)
            .ToList();

        return new DailyDigest(today, todays, quotes.ToList(), marketSource);
    }
}
