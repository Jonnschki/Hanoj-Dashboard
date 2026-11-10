using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Common;
using LifeDashboard.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LifeDashboard.Infrastructure.GoogleCalendar;

/// <summary>
/// Google Calendar API v3. STUB – Umsetzung in Phase 3.
/// Plan:
/// <list type="bullet">
/// <item>Kalender: calendarList.list (accessRole owner/writer → Own, reader/freeBusyReader → SharedReadOnly).</item>
/// <item>Termine: events.list mit singleEvents=true, timeMin/timeMax.</item>
/// <item>Herkunftsmarker: extendedProperties.private { LifeDashboard.OriginProvider, LifeDashboard.OriginId, LifeDashboard.SyncHash }.</item>
/// <item>Ganztagstermine: start.date / end.date (exklusiv) ↔ <see cref="AllDaySchedule"/>.</item>
/// </list>
/// </summary>
public sealed class GoogleCalendarProvider(CalendarAccount account) : NotImplementedCalendarProvider(account, "Google Calendar (Phase 3)");

/// <summary>Login per OAuth 2.0 Authorization Code + PKCE im System-Browser. STUB – Phase 3.</summary>
public sealed class GoogleAccountConnector(IOptions<GoogleOptions> options, ISecureTokenStore tokens)
    : NotImplementedAccountConnector(ProviderKind.Google, "Google-Login (Phase 3)")
{
    internal GoogleOptions Options { get; } = options.Value;

    internal ISecureTokenStore Tokens { get; } = tokens;
}
