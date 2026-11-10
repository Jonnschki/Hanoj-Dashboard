using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Common;

namespace LifeDashboard.Infrastructure.Ics;

/// <summary>
/// Fallback für Kollegen-Kalender, falls der Arbeitgeber-Tenant Graph-Zugriff blockiert:
/// abonniert einen veröffentlichten ICS-Link (Outlook im Web → Kalender veröffentlichen).
/// IMMER read-only: alle Kalender werden als <see cref="CalendarOwnership.SharedReadOnly"/> gemeldet,
/// <see cref="NotImplementedCalendarProvider.GetWriter"/> wirft daher immer. STUB – optional in Phase 2.
/// </summary>
public sealed class IcsCalendarProvider(CalendarAccount account, Uri icsUrl) : NotImplementedCalendarProvider(account, "ICS-Abo")
{
    public Uri IcsUrl { get; } = icsUrl;
}
