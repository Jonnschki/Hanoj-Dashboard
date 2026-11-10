using LifeDashboard.Core.Abstractions;

namespace LifeDashboard.Core.Common;

/// <summary>Echte Systemuhr.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}
