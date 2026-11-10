using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.InMemory;

/// <summary>
/// Kalender-Provider im Speicher. Referenzimplementierung für Unit-Tests und den Demo-Modus der App.
/// Verhält sich wie ein echter Anbieter: vergibt IDs, setzt Server-Zeitstempel, verweigert Schreiben in fremde Kalender.
/// </summary>
public sealed class InMemoryCalendarProvider : ICalendarProvider
{
    private readonly Dictionary<string, CalendarInfo> _calendars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, CalendarEvent>> _events = new(StringComparer.Ordinal);
    private readonly IClock _clock;
    private int _nextId;

    public InMemoryCalendarProvider(CalendarAccount account, IClock clock)
    {
        Account = account;
        _clock = clock;
    }

    public ProviderKind Kind => Account.Provider;

    public CalendarAccount Account { get; }

    /// <summary>Anzahl ausgeführter Schreiboperationen (für Tests).</summary>
    public int WriteCount { get; private set; }

    /// <summary>Wie oft <see cref="GetWriter"/> aufgerufen wurde.</summary>
    public int WriterRequests { get; private set; }

    public CalendarInfo AddCalendar(string nativeId, string name, CalendarOwnership ownership, CalendarCategory category)
    {
        var calendar = new CalendarInfo($"{Kind}:{Account.Id}:{nativeId}", Account.Id, Kind, name, ownership, category);
        _calendars[calendar.Id] = calendar;
        _events[calendar.Id] = new Dictionary<string, CalendarEvent>(StringComparer.Ordinal);
        return calendar;
    }

    /// <summary>Simuliert einen vom Nutzer (nicht von der App) angelegten Termin.</summary>
    public CalendarEvent Seed(CalendarInfo calendar, CalendarEvent calendarEvent)
    {
        var stored = calendarEvent with { Id = NewId(), CalendarId = calendar.Id, LastModifiedUtc = _clock.UtcNow };
        _events[calendar.Id][stored.Id] = stored;
        return stored;
    }

    /// <summary>Simuliert eine manuelle Änderung durch den Nutzer.</summary>
    public CalendarEvent EditByUser(CalendarInfo calendar, string eventId, Func<CalendarEvent, CalendarEvent> change)
    {
        var updated = change(_events[calendar.Id][eventId]) with { LastModifiedUtc = _clock.UtcNow };
        _events[calendar.Id][eventId] = updated;
        return updated;
    }

    /// <summary>Simuliert eine manuelle Löschung durch den Nutzer.</summary>
    public void DeleteByUser(CalendarInfo calendar, string eventId) => _events[calendar.Id].Remove(eventId);

    /// <summary>Alle Termine eines Kalenders (für Assertions).</summary>
    public IReadOnlyList<CalendarEvent> EventsOf(CalendarInfo calendar) => _events[calendar.Id].Values.ToList();

    public Task<Result<IReadOnlyList<CalendarInfo>>> GetCalendarsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success<IReadOnlyList<CalendarInfo>>(_calendars.Values.ToList()));

    public Task<Result<IReadOnlyList<CalendarEvent>>> GetEventsAsync(CalendarInfo calendar, DateRange range, CancellationToken cancellationToken = default)
    {
        if (!_events.TryGetValue(calendar.Id, out var events))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<CalendarEvent>>(Errors.NotFound($"Kalender {calendar.Id}")));
        }

        IReadOnlyList<CalendarEvent> inRange = events.Values
            .Where(e => range.Overlaps(e.Schedule.SortKeyUtc, e.Schedule.EndKeyUtc))
            .ToList();
        return Task.FromResult(Result.Success(inRange));
    }

    public ICalendarWriter GetWriter(CalendarInfo calendar)
    {
        WriterRequests++;
        if (!_calendars.TryGetValue(calendar.Id, out var known) || !known.IsWritable)
        {
            throw new ReadOnlyCalendarException(calendar.Id);
        }

        return new Writer(this, known);
    }

    private string NewId() => $"e{Interlocked.Increment(ref _nextId)}";

    private sealed class Writer(InMemoryCalendarProvider owner, CalendarInfo calendar) : ICalendarWriter
    {
        public CalendarInfo Calendar { get; } = calendar;

        public Task<Result<WriteReceipt>> CreateAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
        {
            var stored = calendarEvent with { Id = owner.NewId(), CalendarId = Calendar.Id, LastModifiedUtc = owner._clock.UtcNow };
            owner._events[Calendar.Id][stored.Id] = stored;
            owner.WriteCount++;
            return Task.FromResult(Result.Success(new WriteReceipt(stored.Id, stored.LastModifiedUtc)));
        }

        public Task<Result<WriteReceipt>> UpdateAsync(string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
        {
            if (!owner._events[Calendar.Id].ContainsKey(eventId))
            {
                return Task.FromResult(Result.Failure<WriteReceipt>(Errors.NotFound($"Termin {eventId}")));
            }

            var stored = calendarEvent with { Id = eventId, CalendarId = Calendar.Id, LastModifiedUtc = owner._clock.UtcNow };
            owner._events[Calendar.Id][eventId] = stored;
            owner.WriteCount++;
            return Task.FromResult(Result.Success(new WriteReceipt(eventId, stored.LastModifiedUtc)));
        }

        public Task<Result> DeleteAsync(string eventId, CancellationToken cancellationToken = default)
        {
            owner.WriteCount++;
            return Task.FromResult(owner._events[Calendar.Id].Remove(eventId)
                ? Result.Success()
                : Result.Failure(Errors.NotFound($"Termin {eventId}")));
        }
    }
}
