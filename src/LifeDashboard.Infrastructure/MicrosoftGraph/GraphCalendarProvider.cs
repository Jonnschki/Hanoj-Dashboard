using System.Globalization;
using System.Text.Json.Nodes;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using static LifeDashboard.Infrastructure.Common.RestCalendarHelpers;

namespace LifeDashboard.Infrastructure.MicrosoftGraph;

/// <summary>
/// Outlook/Microsoft 365 über Microsoft Graph per REST (ohne Graph-SDK: schlanker, voll testbar).
/// - Eigene Kalender: canEdit und Eigentümer = angemeldetes Konto → <see cref="CalendarOwnership.Own"/> (Kategorie Arbeit);
///   geteilte Kalender (z. B. Kollegen) sind read-only.
/// - Termine über calendarView (Serien aufgelöst) mit Zeiten in UTC (Header Prefer: outlook.timezone="UTC").
/// - Herkunftsmarker: Single-Value Extended Property <see cref="OriginPropertyId"/>.
/// Ist kein Microsoft-Konto verbunden, liefert der Provider einfach keine Kalender.
/// </summary>
public sealed class GraphCalendarProvider(HttpClient http, IAccessTokenSource tokens) : ICalendarProvider
{
    /// <summary>GUID des Property-Sets für die Extended Property (fest, nie ändern!).</summary>
    public const string ExtendedPropertySetId = "6f8b2a9e-3c1d-4e57-9a0b-2d4c6e8f1a3b";

    /// <summary>Vollständige Extended-Property-ID für Graph.</summary>
    public const string OriginPropertyId = $"String {{{ExtendedPropertySetId}}} Name {OriginMarker.PropertyPrefix}Origin";

    private const string Api = "https://graph.microsoft.com/v1.0/";
    private const string EventFields = "id,subject,bodyPreview,start,end,isAllDay,location,showAs,sensitivity,lastModifiedDateTime,isCancelled,webLink,originalStartTimeZone";

    public ProviderKind Kind => ProviderKind.Microsoft;

    public CalendarAccount Account => tokens.Account ?? new CalendarAccount("microsoft", ProviderKind.Microsoft, "Microsoft (nicht verbunden)", null);

    private HttpClient Http => http;

    private IAccessTokenSource Tokens => tokens;

    private static void PreferUtc(HttpRequestMessage request) => request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");

    public async Task<Result<IReadOnlyList<CalendarInfo>>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        if ((await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false)).IsFailure)
        {
            return Result.Success<IReadOnlyList<CalendarInfo>>([]); // nicht verbunden: keine Kalender, kein Fehler
        }

        var result = await SendAsync(http, tokens, HttpMethod.Get, $"{Api}me/calendars?$select=id,name,canEdit,owner,isDefaultCalendar&$top=100", null, "Microsoft", cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<CalendarInfo>>(result.Error);
        }

        var account = Account;
        IReadOnlyList<CalendarInfo> calendars = (result.Value?["value"]?.AsArray() ?? [])
            .Where(c => c is not null)
            .Select(c =>
            {
                var owner = c!["owner"]?["address"]?.GetValue<string>();
                var mine = c["canEdit"]?.GetValue<bool>() == true
                    && (owner is null || account.UserPrincipal is null || string.Equals(owner, account.UserPrincipal, StringComparison.OrdinalIgnoreCase));
                return new CalendarInfo(
                    $"{ProviderKind.Microsoft}:{account.Id}:{c["id"]!.GetValue<string>()}",
                    account.Id,
                    ProviderKind.Microsoft,
                    c["name"]?.GetValue<string>() ?? "Outlook",
                    mine ? CalendarOwnership.Own : CalendarOwnership.SharedReadOnly,
                    CalendarCategory.Work);
            })
            .ToList();
        return Result.Success(calendars);
    }

    public async Task<Result<IReadOnlyList<CalendarEvent>>> GetEventsAsync(CalendarInfo calendar, DateRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(range);
        var events = new List<CalendarEvent>();
        string? url = $"{Api}me/calendars/{Uri.EscapeDataString(NativeId(calendar))}/calendarView"
            + $"?startDateTime={Uri.EscapeDataString(range.StartUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}"
            + $"&endDateTime={Uri.EscapeDataString(range.EndUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}"
            + $"&$top=250&$select={EventFields}"
            + $"&$expand=singleValueExtendedProperties($filter=id eq '{OriginPropertyId}')";

        while (url is not null)
        {
            var page = await SendAsync(http, tokens, HttpMethod.Get, url, null, "Microsoft", cancellationToken, PreferUtc).ConfigureAwait(false);
            if (page.IsFailure)
            {
                return Result.Failure<IReadOnlyList<CalendarEvent>>(page.Error);
            }

            foreach (var item in page.Value?["value"]?.AsArray() ?? [])
            {
                if (item is not null && ToDomain(item, calendar.Id) is { } e)
                {
                    events.Add(e);
                }
            }

            url = page.Value?["@odata.nextLink"]?.GetValue<string>();
        }

        return Result.Success<IReadOnlyList<CalendarEvent>>(events);
    }

    public ICalendarWriter GetWriter(CalendarInfo calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (!calendar.IsWritable)
        {
            throw new ReadOnlyCalendarException(calendar.Id);
        }

        return new Writer(this, calendar);
    }

    internal static CalendarEvent? ToDomain(JsonNode item, string calendarId)
    {
        if (item["isCancelled"]?.GetValue<bool>() == true || item["id"] is null)
        {
            return null;
        }

        var startText = item["start"]?["dateTime"]?.GetValue<string>();
        var endText = item["end"]?["dateTime"]?.GetValue<string>();
        EventSchedule schedule;
        if (item["isAllDay"]?.GetValue<bool>() == true && startText is { Length: >= 10 })
        {
            var s = DateOnly.Parse(startText[..10], CultureInfo.InvariantCulture);
            var e = endText is { Length: >= 10 } ? DateOnly.Parse(endText[..10], CultureInfo.InvariantCulture) : s.AddDays(1);
            schedule = new AllDaySchedule(s, e > s ? e : s.AddDays(1));
        }
        else
        {
            // Mit "Prefer: outlook.timezone=UTC" kommen die Zeiten als UTC ohne Offset.
            var s = ParseUtc(startText, DateTimeOffset.UnixEpoch);
            var e = ParseUtc(endText, s);
            schedule = new TimedSchedule(s, e < s ? s : e, ToIana(item["originalStartTimeZone"]?.GetValue<string>()));
        }

        var markerValue = (item["singleValueExtendedProperties"]?.AsArray() ?? [])
            .FirstOrDefault(p => string.Equals(p?["id"]?.GetValue<string>(), OriginPropertyId, StringComparison.OrdinalIgnoreCase))?["value"]?.GetValue<string>();

        var sensitivity = item["sensitivity"]?.GetValue<string>();
        return new CalendarEvent(
            item["id"]!.GetValue<string>(),
            calendarId,
            item["subject"]?.GetValue<string>() is { Length: > 0 } subject ? subject : "(ohne Titel)",
            item["bodyPreview"]?.GetValue<string>() is { Length: > 0 } body ? body : null,
            item["location"]?["displayName"]?.GetValue<string>() is { Length: > 0 } loc ? loc : null,
            schedule,
            sensitivity is "private" or "confidential" or "personal" ? EventVisibility.Private : EventVisibility.Normal,
            item["showAs"]?.GetValue<string>() switch
            {
                "free" => ShowAs.Free,
                "tentative" => ShowAs.Tentative,
                "oof" => ShowAs.OutOfOffice,
                _ => ShowAs.Busy,
            },
            ParseUtc(item["lastModifiedDateTime"]?.GetValue<string>(), DateTimeOffset.UnixEpoch),
            ParseMarkerJson(markerValue),
            item["webLink"]?.GetValue<string>());
    }

    internal static JsonObject ToGraph(CalendarEvent e)
    {
        var json = new JsonObject
        {
            ["subject"] = e.Title,
            ["body"] = new JsonObject { ["contentType"] = "text", ["content"] = e.Description ?? e.Url ?? string.Empty },
            ["location"] = new JsonObject { ["displayName"] = e.Location ?? string.Empty },
            ["showAs"] = e.ShowAs switch { ShowAs.Free => "free", ShowAs.Tentative => "tentative", ShowAs.OutOfOffice => "oof", _ => "busy" },
            ["sensitivity"] = e.Visibility == EventVisibility.Private ? "private" : "normal",
            ["isReminderOn"] = false, // Spiegelungen sollen nicht doppelt erinnern
        };

        switch (e.Schedule)
        {
            case AllDaySchedule a:
                json["isAllDay"] = true;
                json["start"] = new JsonObject { ["dateTime"] = a.StartDate.ToString("yyyy-MM-dd'T'00:00:00", CultureInfo.InvariantCulture), ["timeZone"] = "UTC" };
                json["end"] = new JsonObject { ["dateTime"] = a.EndDateExclusive.ToString("yyyy-MM-dd'T'00:00:00", CultureInfo.InvariantCulture), ["timeZone"] = "UTC" };
                break;
            case TimedSchedule t:
                json["isAllDay"] = false;
                json["start"] = new JsonObject { ["dateTime"] = t.StartUtc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), ["timeZone"] = "UTC" };
                json["end"] = new JsonObject { ["dateTime"] = t.EndUtc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), ["timeZone"] = "UTC" };
                break;
        }

        if (e.Origin is { } o)
        {
            json["singleValueExtendedProperties"] = new JsonArray(new JsonObject { ["id"] = OriginPropertyId, ["value"] = MarkerJson(o) });
        }

        return json;
    }

    private sealed class Writer(GraphCalendarProvider owner, CalendarInfo calendar) : ICalendarWriter
    {
        public CalendarInfo Calendar { get; } = calendar;

        public Task<Result<WriteReceipt>> CreateAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
            => WriteAsync(HttpMethod.Post, $"{Api}me/calendars/{Uri.EscapeDataString(NativeId(Calendar))}/events", calendarEvent, cancellationToken);

        public Task<Result<WriteReceipt>> UpdateAsync(string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
            => WriteAsync(HttpMethod.Patch, $"{Api}me/events/{Uri.EscapeDataString(eventId)}", calendarEvent, cancellationToken);

        public async Task<Result> DeleteAsync(string eventId, CancellationToken cancellationToken = default)
        {
            var result = await SendAsync(owner.Http, owner.Tokens, HttpMethod.Delete, $"{Api}me/events/{Uri.EscapeDataString(eventId)}", null, "Microsoft", cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
        }

        private async Task<Result<WriteReceipt>> WriteAsync(HttpMethod method, string url, CalendarEvent e, CancellationToken ct)
        {
            var result = await SendAsync(owner.Http, owner.Tokens, method, url, ToGraph(e), "Microsoft", ct, PreferUtc).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result.Failure<WriteReceipt>(result.Error);
            }

            var id = result.Value?["id"]?.GetValue<string>();
            return id is null
                ? Result.Failure<WriteReceipt>(Errors.ProviderFailure("Microsoft", "Antwort ohne ID"))
                : Result.Success(new WriteReceipt(id, ParseUtc(result.Value?["lastModifiedDateTime"]?.GetValue<string>(), DateTimeOffset.UtcNow)));
        }
    }
}
