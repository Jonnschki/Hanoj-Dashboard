namespace LifeDashboard.Core.Common;

/// <summary>Kleine Hilfen zur Argumentprüfung.</summary>
public static class Guard
{
    public static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty.", paramName);
        }

        return value;
    }

    /// <summary>Stellt sicher, dass ein Zeitpunkt in UTC (Offset 0) vorliegt.</summary>
    public static DateTimeOffset Utc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be UTC (offset 00:00).", paramName);
        }

        return value;
    }
}
