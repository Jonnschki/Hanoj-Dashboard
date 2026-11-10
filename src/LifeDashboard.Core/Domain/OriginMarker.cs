namespace LifeDashboard.Core.Domain;

/// <summary>
/// Herkunftsmarker an jedem von der App erzeugten Termin.
/// Google: Extended Property (private). Microsoft Graph: Single-Value Extended Property.
/// Termine MIT Marker werden nie erneut gespiegelt (Schleifenschutz).
/// </summary>
/// <param name="OriginProvider">Anbieter des Originals.</param>
/// <param name="OriginId">Ledger-Schlüssel des Originals (siehe <see cref="OriginKey"/>).</param>
/// <param name="SyncHash">Hash des gespiegelten Inhalts zum Zeitpunkt des Schreibens.</param>
public sealed record OriginMarker(ProviderKind OriginProvider, string OriginId, string SyncHash)
{
    /// <summary>Präfix der Property-Namen in den Fremdsystemen.</summary>
    public const string PropertyPrefix = "LifeDashboard.";
}

/// <summary>Erzeugt stabile Ledger-Schlüssel.</summary>
public static class OriginKey
{
    /// <summary>Schlüssel für einen Kalendertermin als Original.</summary>
    public static string ForEvent(string sourceCalendarId, string eventId) => $"evt|{sourceCalendarId}|{eventId}";

    /// <summary>Schlüssel für eine Sportaktivität als Original (z. B. Strava-Activity-ID).</summary>
    public static string ForActivity(ProviderKind provider, string activityId) => $"act|{provider}|{activityId}";
}
