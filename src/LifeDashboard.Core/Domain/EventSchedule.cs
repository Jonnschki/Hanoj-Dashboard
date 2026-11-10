using LifeDashboard.Core.Common;

namespace LifeDashboard.Core.Domain;

/// <summary>
/// Zeitliche Lage eines Termins. Zeitgebundene Termine und Ganztagstermine werden bewusst getrennt modelliert,
/// weil ein Ganztagstermin ein Kalenderdatum ist und kein Zeitpunkt (keine Verschiebung durch Zeitzonen).
/// </summary>
public abstract record EventSchedule
{
    /// <summary>Beginn als UTC-Zeitpunkt (bei Ganztagsterminen: Mitternacht UTC des Startdatums, nur zum Sortieren/Filtern).</summary>
    public abstract DateTimeOffset SortKeyUtc { get; }

    /// <summary>Ende als UTC-Zeitpunkt (zum Filtern).</summary>
    public abstract DateTimeOffset EndKeyUtc { get; }
}

/// <summary>Termin mit Uhrzeit. Intern UTC, die ursprüngliche Zeitzone wird separat gespeichert.</summary>
public sealed record TimedSchedule : EventSchedule
{
    public TimedSchedule(DateTimeOffset startUtc, DateTimeOffset endUtc, string timeZoneId)
    {
        StartUtc = Guard.Utc(startUtc, nameof(startUtc));
        EndUtc = Guard.Utc(endUtc, nameof(endUtc));
        TimeZoneId = Guard.NotNullOrWhiteSpace(timeZoneId, nameof(timeZoneId));
        if (EndUtc < StartUtc)
        {
            throw new ArgumentException("End must not be before start.", nameof(endUtc));
        }
    }

    public DateTimeOffset StartUtc { get; }

    public DateTimeOffset EndUtc { get; }

    /// <summary>Zeitzone des Termins (IANA, z. B. "Europe/Vienna").</summary>
    public string TimeZoneId { get; }

    public TimeSpan Duration => EndUtc - StartUtc;

    public override DateTimeOffset SortKeyUtc => StartUtc;

    public override DateTimeOffset EndKeyUtc => EndUtc;
}

/// <summary>Ganztagstermin über einen oder mehrere Tage. Das Ende ist exklusiv (wie bei Graph und Google).</summary>
public sealed record AllDaySchedule : EventSchedule
{
    public AllDaySchedule(DateOnly startDate, DateOnly endDateExclusive)
    {
        if (endDateExclusive <= startDate)
        {
            throw new ArgumentException("All-day events must span at least one day.", nameof(endDateExclusive));
        }

        StartDate = startDate;
        EndDateExclusive = endDateExclusive;
    }

    public DateOnly StartDate { get; }

    public DateOnly EndDateExclusive { get; }

    public override DateTimeOffset SortKeyUtc => new(StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public override DateTimeOffset EndKeyUtc => new(EndDateExclusive.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
