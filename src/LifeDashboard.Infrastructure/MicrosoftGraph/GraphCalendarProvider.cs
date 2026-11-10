using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Common;
using LifeDashboard.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace LifeDashboard.Infrastructure.MicrosoftGraph;

/// <summary>
/// Outlook/Microsoft 365 über Microsoft Graph. STUB – Umsetzung in Phase 2.
/// Plan:
/// <list type="bullet">
/// <item>Eigene Kalender: GET /me/calendars, /me/calendars/{id}/calendarView (Serien expandiert).</item>
/// <item>Geteilte Kalender: erscheinen nach Annahme der Freigabe unter /me/calendars (canEdit=false → SharedReadOnly),
///   alternativ /users/{upn}/calendar/calendarView mit Calendars.Read.Shared.</item>
/// <item>Fallback Frei/Gebucht: POST /me/calendar/getSchedule (keine Einzelfreigabe nötig).</item>
/// <item>Herkunftsmarker: Single-Value Extended Property
///   "String {GUID} Name LifeDashboard.Origin" mit JSON {OriginProvider, OriginId, SyncHash}.</item>
/// </list>
/// </summary>
public sealed class GraphCalendarProvider(CalendarAccount account) : NotImplementedCalendarProvider(account, "Microsoft Graph (Phase 2)")
{
    /// <summary>GUID des Property-Sets für die Extended Property (fest, nie ändern!).</summary>
    public const string ExtendedPropertySetId = "6f8b2a9e-3c1d-4e57-9a0b-2d4c6e8f1a3b";

    /// <summary>Vollständige Extended-Property-ID für Graph.</summary>
    public const string OriginPropertyId = $"String {{{ExtendedPropertySetId}}} Name {OriginMarker.PropertyPrefix}Origin";
}

/// <summary>Login per MSAL (System-Browser, Token-Cache in SecureStorage). STUB – Phase 2.</summary>
public sealed class MsalAccountConnector(IOptions<MicrosoftOptions> options, ISecureTokenStore tokens)
    : NotImplementedAccountConnector(ProviderKind.Microsoft, "Microsoft-Login (Phase 2)")
{
    internal MicrosoftOptions Options { get; } = options.Value;

    internal ISecureTokenStore Tokens { get; } = tokens;
}
