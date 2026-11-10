namespace LifeDashboard.Core.Domain;

/// <summary>Anbieterneutraler Kalendertermin.</summary>
/// <param name="Id">Native ID beim Anbieter (leer, solange noch nicht angelegt).</param>
/// <param name="CalendarId">ID des Kalenders (<see cref="CalendarInfo.Id"/>).</param>
/// <param name="Title">Titel.</param>
/// <param name="Description">Beschreibung/Notizen.</param>
/// <param name="Location">Ort.</param>
/// <param name="Schedule">Zeitliche Lage.</param>
/// <param name="Visibility">Sichtbarkeit.</param>
/// <param name="ShowAs">Verfügbarkeit.</param>
/// <param name="LastModifiedUtc">Letzte Änderung beim Anbieter (UTC).</param>
/// <param name="Origin">Herkunftsmarker, falls von der App erzeugt.</param>
/// <param name="Url">Optionaler Link (z. B. Strava-Aktivität).</param>
public sealed record CalendarEvent(
    string Id,
    string CalendarId,
    string Title,
    string? Description,
    string? Location,
    EventSchedule Schedule,
    EventVisibility Visibility,
    ShowAs ShowAs,
    DateTimeOffset LastModifiedUtc,
    OriginMarker? Origin = null,
    string? Url = null)
{
    /// <summary>True, wenn der Termin von dieser App erzeugt wurde.</summary>
    public bool IsAppGenerated => Origin is not null;
}
