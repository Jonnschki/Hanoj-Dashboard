namespace LifeDashboard.Core.Common;

/// <summary>Zentrale Fehlercodes.</summary>
public static class Errors
{
    public static Error NotConnected(string provider) => new("auth.not_connected", $"{provider} ist nicht verbunden.");

    public static Error NotFound(string what) => new("not_found", $"{what} wurde nicht gefunden.");

    public static Error ProviderFailure(string provider, string message) => new("provider.failure", $"{provider}: {message}");

    public static Error NotImplementedYet(string feature) => new("not_implemented", $"{feature} ist noch nicht implementiert.");
}

/// <summary>
/// Wird geworfen, wenn versucht wird, in einen Kalender zu schreiben, der nicht dem Nutzer gehört.
/// Das ist ein Programmierfehler und wird daher bewusst als Exception (nicht als Result) gemeldet.
/// </summary>
public sealed class ReadOnlyCalendarException(string calendarId)
    : InvalidOperationException($"Calendar '{calendarId}' is read-only. Writing to foreign calendars is forbidden.")
{
    public string CalendarId { get; } = calendarId;
}
