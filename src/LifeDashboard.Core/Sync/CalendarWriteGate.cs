using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Sync;

/// <summary>
/// Einziger Weg, über den die SyncEngine einen Schreibzugriff erhält.
/// Prüft die Eigentümerschaft VOR dem Provider-Aufruf und erneut bei jeder Schreiboperation.
/// So bleiben fremde Kalender read-only, selbst wenn ein Provider fehlerhaft implementiert ist.
/// </summary>
public static class CalendarWriteGate
{
    /// <exception cref="ReadOnlyCalendarException">Kalender ist nicht eigener Kalender.</exception>
    public static ICalendarWriter Open(ICalendarProvider provider, CalendarInfo calendar)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(calendar);

        if (!calendar.IsWritable)
        {
            throw new ReadOnlyCalendarException(calendar.Id);
        }

        return new GuardedWriter(provider.GetWriter(calendar), calendar);
    }

    private sealed class GuardedWriter(ICalendarWriter inner, CalendarInfo expected) : ICalendarWriter
    {
        public CalendarInfo Calendar => inner.Calendar;

        public Task<Result<WriteReceipt>> CreateAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
        {
            EnsureWritable();
            return inner.CreateAsync(calendarEvent, cancellationToken);
        }

        public Task<Result<WriteReceipt>> UpdateAsync(string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
        {
            EnsureWritable();
            return inner.UpdateAsync(eventId, calendarEvent, cancellationToken);
        }

        public Task<Result> DeleteAsync(string eventId, CancellationToken cancellationToken = default)
        {
            EnsureWritable();
            return inner.DeleteAsync(eventId, cancellationToken);
        }

        private void EnsureWritable()
        {
            // Der Provider könnte einen Writer für einen ANDEREN Kalender liefern – beides prüfen.
            if (!inner.Calendar.IsWritable || inner.Calendar.Id != expected.Id)
            {
                throw new ReadOnlyCalendarException(inner.Calendar.Id);
            }
        }
    }
}
