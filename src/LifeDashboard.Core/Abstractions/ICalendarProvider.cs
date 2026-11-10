using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Abstractions;

/// <summary>
/// Zugriff auf die Kalender EINES verbundenen Kontos (Microsoft, Google, ICS, ...).
/// Lesen ist für alle Kalender erlaubt, Schreiben ausschließlich über <see cref="GetWriter"/>.
/// </summary>
public interface ICalendarProvider
{
    /// <summary>Anbieter dieses Providers.</summary>
    ProviderKind Kind { get; }

    /// <summary>Konto, zu dem dieser Provider gehört.</summary>
    CalendarAccount Account { get; }

    /// <summary>Listet alle sichtbaren Kalender des Kontos (eigene und geteilte).</summary>
    /// <param name="cancellationToken">Abbruch.</param>
    Task<Result<IReadOnlyList<CalendarInfo>>> GetCalendarsAsync(CancellationToken cancellationToken = default);

    /// <summary>Liest Termine eines Kalenders im Zeitfenster. Wiederkehrende Serien werden als Einzeltermine geliefert.</summary>
    /// <param name="calendar">Kalender (eigener oder geteilter).</param>
    /// <param name="range">Zeitfenster (UTC).</param>
    /// <param name="cancellationToken">Abbruch.</param>
    Task<Result<IReadOnlyList<CalendarEvent>>> GetEventsAsync(CalendarInfo calendar, DateRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Liefert einen Schreibzugriff für einen EIGENEN Kalender.
    /// Implementierungen MÜSSEN für <see cref="CalendarOwnership.SharedReadOnly"/> eine
    /// <see cref="ReadOnlyCalendarException"/> werfen. Die SyncEngine prüft das zusätzlich selbst.
    /// </summary>
    /// <param name="calendar">Zielkalender.</param>
    /// <exception cref="ReadOnlyCalendarException">Kalender ist nicht beschreibbar.</exception>
    ICalendarWriter GetWriter(CalendarInfo calendar);
}

/// <summary>
/// Quittung eines Schreibvorgangs. <see cref="LastModifiedUtc"/> stammt vom SERVER (nicht von der lokalen Uhr)
/// und dient später zur Erkennung manueller Änderungen an der Spiegelung.
/// </summary>
public sealed record WriteReceipt(string EventId, DateTimeOffset LastModifiedUtc);

/// <summary>Schreibzugriff auf genau einen eigenen Kalender.</summary>
public interface ICalendarWriter
{
    /// <summary>Kalender, in den geschrieben wird.</summary>
    CalendarInfo Calendar { get; }

    /// <summary>Legt einen Termin an. Der Herkunftsmarker (<see cref="CalendarEvent.Origin"/>) muss mitgespeichert werden.</summary>
    /// <returns>ID und serverseitiger Änderungszeitpunkt des neuen Termins.</returns>
    Task<Result<WriteReceipt>> CreateAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default);

    /// <summary>Aktualisiert einen bestehenden Termin inklusive Herkunftsmarker.</summary>
    /// <returns>ID und serverseitiger Änderungszeitpunkt nach dem Update.</returns>
    Task<Result<WriteReceipt>> UpdateAsync(string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default);

    /// <summary>Löscht einen Termin.</summary>
    Task<Result> DeleteAsync(string eventId, CancellationToken cancellationToken = default);
}
