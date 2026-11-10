using System.Net;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using Microsoft.Extensions.Logging;

namespace LifeDashboard.Infrastructure.Ics;

/// <summary>
/// Abonnierte ICS-Kalender fremder Personen/Firmen (z. B. über "Kalender veröffentlichen" in Outlook).
/// - Welche Abos es gibt, steht in den <see cref="UserSettings.IcsSubscriptions"/>.
/// - Die URL ist ein Geheimnis (enthält ein Zugriffstoken) und liegt nur im <see cref="ISecureTokenStore"/>.
///   Sie wird nie geloggt und nie in Fehlermeldungen übernommen.
/// - Alle Kalender sind <see cref="CalendarOwnership.SharedReadOnly"/>; <see cref="GetWriter"/> wirft IMMER.
/// </summary>
public sealed class IcsCalendarProvider(
    HttpClient http,
    IUserSettingsStore settings,
    ISecureTokenStore secrets,
    ILogger<IcsCalendarProvider> logger) : ICalendarProvider
{
    public const string AccountId = "ics";

    public ProviderKind Kind => ProviderKind.Ics;

    public CalendarAccount Account { get; } = new(AccountId, ProviderKind.Ics, "Abonnierte Kalender", null);

    /// <summary>Kalender-ID für ein Abo.</summary>
    public static string CalendarIdFor(string subscriptionId) => $"{ProviderKind.Ics}:{AccountId}:{subscriptionId}";

    public async Task<Result<IReadOnlyList<CalendarInfo>>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        var subscriptions = (await settings.LoadAsync(cancellationToken).ConfigureAwait(false)).IcsSubscriptions;
        IReadOnlyList<CalendarInfo> calendars = subscriptions
            .Select(s => new CalendarInfo(CalendarIdFor(s.Id), AccountId, ProviderKind.Ics, s.Name, CalendarOwnership.SharedReadOnly, s.Category))
            .ToList();
        return Result.Success(calendars);
    }

    public async Task<Result<IReadOnlyList<CalendarEvent>>> GetEventsAsync(CalendarInfo calendar, DateRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var subscription = (await settings.LoadAsync(cancellationToken).ConfigureAwait(false)).IcsSubscriptions
            .FirstOrDefault(s => CalendarIdFor(s.Id) == calendar.Id);
        if (subscription is null)
        {
            return Result.Failure<IReadOnlyList<CalendarEvent>>(Errors.NotFound($"ICS-Abo „{calendar.Name}“"));
        }

        var url = IcsSubscription.NormalizeUrl(await secrets.GetAsync(subscription.SecretKey).ConfigureAwait(false));
        if (url is null)
        {
            return Result.Failure<IReadOnlyList<CalendarEvent>>(new Error("ics.no_url", $"Für „{subscription.Name}“ ist kein gültiger ICS-Link gespeichert."));
        }

        string body;
        try
        {
            using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<IReadOnlyList<CalendarEvent>>(DescribeHttpError(subscription.Name, response.StatusCode));
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Bewusst ohne URL und ohne ex.Message (könnte die URL enthalten).
            logger.LogWarning("ICS-Abo {Name}: Netzwerkfehler ({Error})", subscription.Name, ex.HttpRequestError);
            return Result.Failure<IReadOnlyList<CalendarEvent>>(new Error("ics.network", $"„{subscription.Name}“ ist gerade nicht erreichbar."));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<IReadOnlyList<CalendarEvent>>(new Error("ics.timeout", $"„{subscription.Name}“ antwortet nicht (Zeitüberschreitung)."));
        }

        var parsed = IcsParser.Parse(body, calendar.Id, range);
        if (parsed.IsFailure)
        {
            logger.LogWarning("ICS-Abo {Name}: {Error}", subscription.Name, parsed.Error.Message);
        }

        return parsed;
    }

    /// <summary>ICS-Kalender sind fremde Kalender: Schreiben ist nie erlaubt.</summary>
    public ICalendarWriter GetWriter(CalendarInfo calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        throw new ReadOnlyCalendarException(calendar.Id);
    }

    private static Error DescribeHttpError(string name, HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound or HttpStatusCode.Gone =>
            new Error("ics.not_found", $"„{name}“: Link ungültig oder Kalender nicht mehr veröffentlicht (HTTP {(int)status})."),
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new Error("ics.forbidden", $"„{name}“: Zugriff verweigert (HTTP {(int)status}). Bitte neuen Link anfordern."),
        _ => new Error("ics.http", $"„{name}“: Server-Fehler (HTTP {(int)status})."),
    };
}
