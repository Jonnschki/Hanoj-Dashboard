using System.Globalization;
using System.Text.Json.Nodes;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Auth;
using static LifeDashboard.Infrastructure.Common.RestCalendarHelpers;

namespace LifeDashboard.Infrastructure.GoogleCalendar;

/// <summary>
/// Google Calendar API v3 per REST (ohne SDK).
/// - Eigene Kalender: accessRole = owner → <see cref="CalendarOwnership.Own"/> (Kategorie: privat), alle anderen read-only.
/// - Serien werden vom Server aufgelöst (singleEvents=true).
/// - Herkunftsmarker in extendedProperties.private.
/// Ist kein Google-Konto verbunden, liefert der Provider einfach keine Kalender.
/// </summary>
public sealed class GoogleCalendarProvider(HttpClient http, IAccessTokenSource tokens) : ICalendarProvider
{
    private const string Api = "https://www.googleapis.com/calendar/v3/";
    private const string KeyProvider = OriginMarker.PropertyPrefix + "OriginProvider";
    private const string KeyOrigin = OriginMarker.PropertyPrefix + "OriginId";
    private const string KeyHash = OriginMarker.PropertyPrefix + "SyncHash";

    public ProviderKind Kind => ProviderKind.Google;

    private HttpClient Http => http;

    private IAccessTokenSource Tokens => tokens;

    public CalendarAccount Account => tokens.Account ?? new CalendarAccount("google", ProviderKind.Google, "Google (nicht verbunden)", null);

    public async Task<Result<IReadOnlyList<CalendarInfo>>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        if ((await tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false)).IsFailure)
        {
            return Result.Success<IReadOnlyList<CalendarInfo>>([]); // nicht verbunden: keine Kalender, kein Fehler
        }

        var result = await SendAsync(http, tokens, HttpMethod.Get, $"{Api}users/me/calendarList?minAccessRole=reader&maxResults=250", null, "Google", cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<CalendarInfo>>(result.Error);
        }

        var account = Account;
        IReadOnlyList<CalendarInfo> calendars = (result.Value?["items"]?.AsArray() ?? [])
            .Where(i => i is not null)
            .Select(i => new CalendarInfo(
                $"{ProviderKind.Google}:{account.Id}:{i!["id"]!.GetValue<string>()}",
                account.Id,
                ProviderKind.Google,
                i["summaryOverride"]?.GetValue<string>() ?? i["summary"]?.GetValue<string>() ?? "Google",
                i["accessRole"]?.GetValue<string>() == "owner" ? CalendarOwnership.Own : CalendarOwnership.SharedReadOnly,
                CalendarCategory.Private))
            .ToList();
        return Result.Success(calendars);
    }

    public async Task<Result<IReadOnlyList<CalendarEvent>>> GetEventsAsync(CalendarInfo calendar, DateRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(range);
        var events = new List<CalendarEvent>();
        string? pageToken = null;
        do
        {
            var url = $"{Api}calendars/{Uri.EscapeDataString(NativeId(calendar))}/events?singleEvents=true&showDeleted=false&maxResults=2500"
                + $"&timeMin={Uri.EscapeDataString(range.StartUtc.ToString("o", CultureInfo.InvariantCulture))}"
                + $"&timeMax={Uri.EscapeDataString(range.EndUtc.ToString("o", CultureInfo.InvariantCulture))}"
                + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            var page = await SendAsync(http, tokens, HttpMethod.Get, url, null, "Google", cancellationToken).ConfigureAwait(false);
            if (page.IsFailure)
            {
                return Result.Failure<IReadOnlyList<CalendarEvent>>(page.Error);
            }

            foreach (var item in page.Value?["items"]?.AsArray() ?? [])
            {
                if (item is not null && ToDomain(item, calendar.Id) is { } e)
                {
                    events.Add(e);
                }
            }

            pageToken = page.Value?["nextPageToken"]?.GetValue<string>();
        }
        while (pageToken is not null);

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
        if (item["status"]?.GetValue<string>() == "cancelled" || item["id"] is null)
        {
            return null;
        }

        var start = item["start"];
        var end = item["end"];
        EventSchedule schedule;
        if (start?["date"] is { } startDate)
        {
            var s = DateOnly.Parse(startDate.GetValue<string>(), CultureInfo.InvariantCulture);
            var e = end?["date"] is { } endDate ? DateOnly.Parse(endDate.GetValue<string>(), CultureInfo.InvariantCulture) : s.AddDays(1);
            schedule = new AllDaySchedule(s, e > s ? e : s.AddDays(1));
        }
        else
        {
            var s = ParseUtc(start?["dateTime"]?.GetValue<string>(), DateTimeOffset.UnixEpoch);
            var e = ParseUtc(end?["dateTime"]?.GetValue<string>(), s);
            schedule = new TimedSchedule(s, e < s ? s : e, start?["timeZone"]?.GetValue<string>() ?? "UTC");
        }

        var props = item["extendedProperties"]?["private"];
        OriginMarker? marker = null;
        if (props?[KeyOrigin]?.GetValue<string>() is { } origin && props[KeyHash]?.GetValue<string>() is { } hash)
        {
            marker = new OriginMarker(Enum.TryParse<ProviderKind>(props[KeyProvider]?.GetValue<string>(), out var p) ? p : ProviderKind.InMemory, origin, hash);
        }

        var visibility = item["visibility"]?.GetValue<string>();
        return new CalendarEvent(
            item["id"]!.GetValue<string>(),
            calendarId,
            item["summary"]?.GetValue<string>() ?? "(ohne Titel)",
            item["description"]?.GetValue<string>(),
            item["location"]?.GetValue<string>(),
            schedule,
            visibility is "private" or "confidential" ? EventVisibility.Private : EventVisibility.Normal,
            item["transparency"]?.GetValue<string>() == "transparent" ? ShowAs.Free : ShowAs.Busy,
            ParseUtc(item["updated"]?.GetValue<string>(), DateTimeOffset.UnixEpoch),
            marker,
            item["htmlLink"]?.GetValue<string>());
    }

    internal static JsonObject ToGoogle(CalendarEvent e)
    {
        var json = new JsonObject
        {
            ["summary"] = e.Title,
            ["description"] = e.Description ?? (e.Url is null ? null : e.Url),
            ["location"] = e.Location,
            ["transparency"] = e.ShowAs == ShowAs.Free ? "transparent" : "opaque",
            ["visibility"] = e.Visibility == EventVisibility.Private ? "private" : "default",
        };

        switch (e.Schedule)
        {
            case AllDaySchedule a:
                json["start"] = new JsonObject { ["date"] = a.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
                json["end"] = new JsonObject { ["date"] = a.EndDateExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
                break;
            case TimedSchedule t:
                json["start"] = new JsonObject { ["dateTime"] = t.StartUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), ["timeZone"] = t.TimeZoneId };
                json["end"] = new JsonObject { ["dateTime"] = t.EndUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), ["timeZone"] = t.TimeZoneId };
                break;
        }

        if (e.Origin is { } o)
        {
            json["extendedProperties"] = new JsonObject
            {
                ["private"] = new JsonObject { [KeyProvider] = o.OriginProvider.ToString(), [KeyOrigin] = o.OriginId, [KeyHash] = o.SyncHash },
            };
        }

        return json;
    }

    private sealed class Writer(GoogleCalendarProvider owner, CalendarInfo calendar) : ICalendarWriter
    {
        public CalendarInfo Calendar { get; } = calendar;

        private string EventsUrl => $"{Api}calendars/{Uri.EscapeDataString(NativeId(Calendar))}/events";

        public Task<Result<WriteReceipt>> CreateAsync(CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
            => WriteAsync(HttpMethod.Post, EventsUrl, calendarEvent, cancellationToken);

        public Task<Result<WriteReceipt>> UpdateAsync(string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken = default)
            => WriteAsync(HttpMethod.Patch, $"{EventsUrl}/{Uri.EscapeDataString(eventId)}", calendarEvent, cancellationToken);

        public async Task<Result> DeleteAsync(string eventId, CancellationToken cancellationToken = default)
        {
            var result = await SendAsync(owner.Http, owner.Tokens, HttpMethod.Delete, $"{EventsUrl}/{Uri.EscapeDataString(eventId)}", null, "Google", cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
        }

        private async Task<Result<WriteReceipt>> WriteAsync(HttpMethod method, string url, CalendarEvent e, CancellationToken ct)
        {
            var result = await SendAsync(owner.Http, owner.Tokens, method, url, ToGoogle(e), "Google", ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result.Failure<WriteReceipt>(result.Error);
            }

            var id = result.Value?["id"]?.GetValue<string>();
            return id is null
                ? Result.Failure<WriteReceipt>(Errors.ProviderFailure("Google", "Antwort ohne ID"))
                : Result.Success(new WriteReceipt(id, ParseUtc(result.Value?["updated"]?.GetValue<string>(), DateTimeOffset.UtcNow)));
        }
    }
}

/// <summary>Google-OAuth-Konfiguration (Desktop-Client, PKCE, Offline-Zugriff für Refresh-Token).</summary>
public static class GoogleOAuth
{
    public static OAuthSettings Create(string clientId, string? clientSecret) => new(
        ProviderKind.Google,
        new Uri("https://accounts.google.com/o/oauth2/v2/auth"),
        new Uri("https://oauth2.googleapis.com/token"),
        clientId,
        clientSecret,
        "openid email https://www.googleapis.com/auth/calendar",
        UsePkce: true,
        new Dictionary<string, string> { ["access_type"] = "offline", ["prompt"] = "consent" });

    /// <summary>Ermittelt nach dem Login die E-Mail-Adresse (Anzeige in den Einstellungen).</summary>
    public static async Task<string?> ResolveEmailAsync(HttpClient http, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))?["email"]?.GetValue<string>() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
