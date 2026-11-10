using LifeDashboard.Core.Common;

namespace LifeDashboard.Core.Domain;

/// <summary>Halboffenes Zeitfenster [Start, End) in UTC.</summary>
public sealed record DateRange
{
    public DateRange(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        StartUtc = Guard.Utc(startUtc, nameof(startUtc));
        EndUtc = Guard.Utc(endUtc, nameof(endUtc));
        if (EndUtc < StartUtc)
        {
            throw new ArgumentException("End must not be before start.", nameof(endUtc));
        }
    }

    public DateTimeOffset StartUtc { get; }

    public DateTimeOffset EndUtc { get; }

    public bool Contains(DateTimeOffset utc) => utc >= StartUtc && utc < EndUtc;

    public bool Overlaps(DateTimeOffset startUtc, DateTimeOffset endUtc) => startUtc < EndUtc && endUtc > StartUtc;

    /// <summary>Fenster ab <paramref name="nowUtc"/> minus <paramref name="past"/> bis plus <paramref name="future"/>.</summary>
    public static DateRange Around(DateTimeOffset nowUtc, TimeSpan past, TimeSpan future) => new(nowUtc - past, nowUtc + future);
}
