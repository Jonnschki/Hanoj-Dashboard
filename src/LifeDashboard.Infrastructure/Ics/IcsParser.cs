using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using IcalCalendar = Ical.Net.Calendar;
using IcalEvent = Ical.Net.CalendarComponents.CalendarEvent;
using IcalDateTime = Ical.Net.DataTypes.CalDateTime;

namespace LifeDashboard.Infrastructure.Ics;

/// <summary>
/// Wandelt eine iCalendar-Datei (RFC 5545) in Domain-Termine eines Zeitfensters um (Ical.Net).
/// - Wiederkehrende Serien werden in Einzeltermine aufgelöst (inkl. EXDATE und geänderter Einzeltermine/RECURRENCE-ID).
/// - Zeiten werden nach UTC umgerechnet; Windows-Zeitzonennamen aus Outlook werden in IANA-Namen übersetzt.
/// - Ganztagstermine (DTSTART;VALUE=DATE) werden zu <see cref="AllDaySchedule"/>.
/// - Abgesagte Termine (STATUS:CANCELLED) werden ausgelassen.
/// </summary>
public static class IcsParser
{
    /// <summary>Wie weit vor dem Fenster nach Serienbeginnen gesucht wird, damit lange laufende Termine nicht fehlen.</summary>
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(35);

    /// <summary>Sicherheitsgrenze gegen endlose oder riesige Serien.</summary>
    private const int MaxOccurrences = 5000;

    public static Result<IReadOnlyList<CalendarEvent>> Parse(string icsText, string calendarId, DateRange range)
    {
        ArgumentNullException.ThrowIfNull(range);
        if (string.IsNullOrWhiteSpace(icsText) || !icsText.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<IReadOnlyList<CalendarEvent>>(new Error("ics.invalid", "Die Antwort ist keine gültige ICS-Kalenderdatei."));
        }

        IcalCalendar? calendar;
        try
        {
            calendar = IcalCalendar.Load(icsText);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or System.Runtime.Serialization.SerializationException)
        {
            return Result.Failure<IReadOnlyList<CalendarEvent>>(new Error("ics.invalid", $"ICS-Datei konnte nicht gelesen werden: {ex.Message}"));
        }

        if (calendar is null)
        {
            return Result.Failure<IReadOnlyList<CalendarEvent>>(new Error("ics.invalid", "ICS-Datei ist leer."));
        }

        try
        {
            return Result.Success(Expand(calendar, calendarId, range));
        }
        catch (Exception ex) when (ex is Ical.Net.Evaluation.EvaluationException or FormatException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            // Defekte Serien/Zeitangaben in fremden Dateien dürfen den Sync nicht abbrechen.
            return Result.Failure<IReadOnlyList<CalendarEvent>>(new Error("ics.invalid", $"ICS-Termine konnten nicht ausgewertet werden: {ex.Message}"));
        }
    }

    private static IReadOnlyList<CalendarEvent> Expand(IcalCalendar calendar, string calendarId, DateRange range)
    {
        var searchFrom = new IcalDateTime((range.StartUtc - Lookback).UtcDateTime, "UTC");
        var result = new List<CalendarEvent>();

        foreach (var occurrence in calendar.GetOccurrences<IcalEvent>(searchFrom).Take(MaxOccurrences))
        {
            var start = occurrence.Period.StartTime;
            if (ToUtc(start) >= range.EndUtc)
            {
                break; // Vorkommen sind nach Beginn sortiert.
            }

            if (occurrence.Source is not IcalEvent source
                || string.Equals(source.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end = occurrence.Period.EffectiveEndTime ?? start;
            var schedule = ToSchedule(start, end);
            if (!range.Overlaps(schedule.SortKeyUtc, schedule.EndKeyUtc))
            {
                continue;
            }

            result.Add(new CalendarEvent(
                Id: $"{source.Uid}|{schedule.SortKeyUtc.UtcTicks}",
                CalendarId: calendarId,
                Title: string.IsNullOrWhiteSpace(source.Summary) ? "(ohne Titel)" : source.Summary.Trim(),
                Description: source.Description,
                Location: source.Location,
                Schedule: schedule,
                Visibility: IsPrivate(source.Class) ? EventVisibility.Private : EventVisibility.Normal,
                ShowAs: ToShowAs(source),
                LastModifiedUtc: ToLastModified(source),
                Origin: null,
                Url: source.Url?.ToString()));
        }

        return result;
    }

    private static EventSchedule ToSchedule(IcalDateTime start, IcalDateTime end)
    {
        if (!start.HasTime)
        {
            var endDate = end.Date > start.Date ? end.Date : start.Date.AddDays(1);
            return new AllDaySchedule(start.Date, endDate);
        }

        var startUtc = ToUtc(start);
        var endUtc = ToUtc(end);
        return new TimedSchedule(startUtc, endUtc < startUtc ? startUtc : endUtc, ToIanaZone(start.TzId));
    }

    private static DateTimeOffset ToUtc(IcalDateTime value)
        => new(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc), TimeSpan.Zero);

    /// <summary>Outlook liefert Windows-Namen ("W. Europe Standard Time"); intern verwenden wir IANA ("Europe/Berlin").</summary>
    internal static string ToIanaZone(string? tzId)
    {
        if (string.IsNullOrWhiteSpace(tzId))
        {
            return "UTC";
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(tzId, out var iana))
        {
            return iana;
        }

        return tzId;
    }

    private static bool IsPrivate(string? @class)
        => string.Equals(@class, "PRIVATE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(@class, "CONFIDENTIAL", StringComparison.OrdinalIgnoreCase);

    private static ShowAs ToShowAs(IcalEvent source)
    {
        // Outlook: eigene Eigenschaft mit genauerem Status.
        var busy = source.Properties.Get<string>("X-MICROSOFT-CDO-BUSYSTATUS");
        switch (busy?.ToUpperInvariant())
        {
            case "FREE": return ShowAs.Free;
            case "TENTATIVE": return ShowAs.Tentative;
            case "OOF": return ShowAs.OutOfOffice;
            case "BUSY": return ShowAs.Busy;
        }

        if (string.Equals(source.Status, "TENTATIVE", StringComparison.OrdinalIgnoreCase))
        {
            return ShowAs.Tentative;
        }

        return string.Equals(source.Transparency, "TRANSPARENT", StringComparison.OrdinalIgnoreCase) ? ShowAs.Free : ShowAs.Busy;
    }

    private static DateTimeOffset ToLastModified(IcalEvent source)
    {
        var stamp = source.LastModified ?? source.DtStamp;
        return stamp is null ? DateTimeOffset.UnixEpoch : ToUtc(stamp);
    }
}
