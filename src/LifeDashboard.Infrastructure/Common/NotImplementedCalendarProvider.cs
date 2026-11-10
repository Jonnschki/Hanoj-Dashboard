using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Infrastructure.Common;

/// <summary>
/// Gemeinsame Basis für noch nicht implementierte Kalender-Provider.
/// Liefert saubere Fehler-Results statt Exceptions, damit die SyncEngine weiterläuft.
/// Der Read-only-Schutz ist bereits HIER aktiv.
/// </summary>
public abstract class NotImplementedCalendarProvider(CalendarAccount account, string feature) : ICalendarProvider
{
    public ProviderKind Kind => Account.Provider;

    public CalendarAccount Account { get; } = account;

    public virtual Task<Result<IReadOnlyList<CalendarInfo>>> GetCalendarsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<IReadOnlyList<CalendarInfo>>(Errors.NotImplementedYet(feature)));

    public virtual Task<Result<IReadOnlyList<CalendarEvent>>> GetEventsAsync(CalendarInfo calendar, DateRange range, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<IReadOnlyList<CalendarEvent>>(Errors.NotImplementedYet(feature)));

    public ICalendarWriter GetWriter(CalendarInfo calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (!calendar.IsWritable)
        {
            throw new ReadOnlyCalendarException(calendar.Id);
        }

        return CreateWriter(calendar);
    }

    /// <summary>Erzeugt den Writer für einen bereits als eigen geprüften Kalender.</summary>
    protected virtual ICalendarWriter CreateWriter(CalendarInfo calendar) => new NotImplementedWriter(calendar, feature);

    private sealed class NotImplementedWriter(CalendarInfo calendar, string feature) : ICalendarWriter
    {
        public CalendarInfo Calendar { get; } = calendar;

        public Task<Result<WriteReceipt>> CreateAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure<WriteReceipt>(Errors.NotImplementedYet(feature)));

        public Task<Result<WriteReceipt>> UpdateAsync(string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure<WriteReceipt>(Errors.NotImplementedYet(feature)));

        public Task<Result> DeleteAsync(string eventId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure(Errors.NotImplementedYet(feature)));
    }
}

/// <summary>Basis für noch nicht implementierte Login-Connectoren.</summary>
public abstract class NotImplementedAccountConnector(ProviderKind kind, string feature) : IAccountConnector
{
    public ProviderKind Kind { get; } = kind;

    public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<Result<CalendarAccount>> ConnectAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<CalendarAccount>(Errors.NotImplementedYet(feature)));

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
