using System.Globalization;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.App.ViewModels;

/// <summary>Anzeigemodell für einen Termin.</summary>
public sealed record EventItem(string Title, string When, string CalendarName, bool IsReadOnly, bool IsMirror)
{
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-AT");

    public string Badge => IsReadOnly ? "read-only" : IsMirror ? "gespiegelt" : string.Empty;

    public static EventItem From(CalendarInfo calendar, CalendarEvent e, bool includeDate)
    {
        string when = e.Schedule switch
        {
            AllDaySchedule a => includeDate ? $"{a.StartDate.ToString("ddd dd.MM.", De)} · ganztägig" : "ganztägig",
            TimedSchedule t => Format(t, includeDate),
            _ => string.Empty,
        };
        return new EventItem(e.Title, when, calendar.Name, !calendar.IsWritable, e.IsAppGenerated);
    }

    private static string Format(TimedSchedule t, bool includeDate)
    {
        var start = t.StartUtc.ToLocalTime();
        var end = t.EndUtc.ToLocalTime();
        var time = $"{start:HH\\:mm}–{end:HH\\:mm}";
        return includeDate ? $"{start.ToString("ddd dd.MM.", De)} · {time}" : time;
    }
}

/// <summary>Anzeigemodell für einen Kurs.</summary>
public sealed record QuoteItem(string Symbol, string Price, string Change, bool IsUp)
{
    public static QuoteItem From(MarketQuote q) => new(
        q.Symbol,
        q.Price.ToString("N2", CultureInfo.GetCultureInfo("de-AT")),
        $"{q.ChangePercent:+0.00;-0.00}%",
        q.ChangePercent >= 0);
}

/// <summary>Anzeigemodell für eine Sportaktivität.</summary>
public sealed record ActivityItem(string Title, string Details, string When)
{
    public static ActivityItem From(SportActivity a) => new(
        $"{a.SportType}: {a.Name}",
        $"{a.DistanceMeters / 1000d:0.0} km · {a.MovingTime:h\\:mm} h",
        a.StartUtc.ToLocalTime().ToString("ddd dd.MM. HH:mm", CultureInfo.GetCultureInfo("de-AT")));
}

/// <summary>Auswahlmöglichkeit in einem Picker (z. B. Sport-Zielkalender).</summary>
public sealed record CalendarOption(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Abonnierter ICS-Kalender in den Einstellungen (ohne URL – die bleibt im sicheren Speicher).</summary>
public sealed record IcsItem(string Id, string Name, string Details)
{
    public static IcsItem From(IcsSubscription s) => new(
        s.Id,
        s.Name,
        $"ICS-Abo · read-only · {(s.Category == CalendarCategory.Work ? "Arbeit" : "Privat")}");
}

/// <summary>Eintrag der Sync-Historie.</summary>
public sealed record SyncRunItem(string When, string Kind, string Summary, string Status)
{
    public static SyncRunItem From(SyncRunRecord r) => new(
        r.StartedUtc.ToLocalTime().ToString("ddd dd.MM. HH:mm", CultureInfo.GetCultureInfo("de-AT")),
        r.DryRun ? "Dry-Run" : "Sync",
        $"{r.Created} neu · {r.Updated} aktualisiert · {r.Deleted} gelöscht · {r.Conflicts} Konflikte · {r.Errors} Fehler",
        r.Status switch
        {
            SyncRunStatus.Succeeded => "OK",
            SyncRunStatus.CompletedWithErrors => "mit Fehlern",
            SyncRunStatus.Failed => "fehlgeschlagen",
            SyncRunStatus.Cancelled => "abgebrochen",
            _ => r.Status.ToString(),
        });
}

/// <summary>Kalender in den Einstellungen (Auswahl für den Sync).</summary>
public sealed partial class CalendarChoice(CalendarInfo calendar, bool isSelected) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public CalendarInfo Calendar { get; } = calendar;

    public string Name => Calendar.Name;

    public string Role => Calendar.IsWritable
        ? $"Eigener Kalender · {(Calendar.Category == CalendarCategory.Work ? "Arbeit" : "Privat")} · {Calendar.Provider}"
        : $"Fremd (read-only) · {Calendar.Provider}";

    public bool CanSelect => Calendar.IsWritable;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}
