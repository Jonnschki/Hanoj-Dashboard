using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Sync;

/// <summary>Erzeugt Spiegelungen und berechnet den Inhalts-Hash.</summary>
public static class EventMapping
{
    /// <summary>Erzeugt die Spiegelung eines Termins für den Zielkalender gemäß Detailstufe (ohne Marker/ID).</summary>
    public static CalendarEvent ToMirror(CalendarEvent source, CalendarInfo target, MirrorDetailLevel level, SyncRules rules)
    {
        if (level == MirrorDetailLevel.None)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Level 'None' produces no mirror.");
        }

        var full = level == MirrorDetailLevel.Full;
        return new CalendarEvent(
            Id: string.Empty,
            CalendarId: target.Id,
            Title: full ? source.Title : rules.BusyTitle,
            Description: full ? source.Description : null,
            Location: full ? source.Location : null,
            Schedule: source.Schedule,
            Visibility: full ? source.Visibility : EventVisibility.Private,
            ShowAs: source.ShowAs,
            LastModifiedUtc: source.LastModifiedUtc,
            Origin: null,
            Url: full ? source.Url : null);
    }

    /// <summary>Wandelt eine Sportaktivität in einen Kalendertermin um.</summary>
    public static CalendarEvent FromActivity(SportActivity activity, CalendarInfo target, DateTimeOffset nowUtc)
    {
        var km = activity.DistanceMeters / 1000d;
        var description = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Sportart: {activity.SportType}")
            .AppendLine(CultureInfo.InvariantCulture, $"Distanz: {km:0.00} km")
            .AppendLine(CultureInfo.InvariantCulture, $"Bewegungszeit: {activity.MovingTime:hh\\:mm\\:ss}")
            .AppendLine(CultureInfo.InvariantCulture, $"Gesamtzeit: {activity.ElapsedTime:hh\\:mm\\:ss}");
        if (activity.Url is not null)
        {
            description.AppendLine(activity.Url);
        }

        return new CalendarEvent(
            Id: string.Empty,
            CalendarId: target.Id,
            Title: $"{activity.SportType}: {activity.Name}",
            Description: description.ToString().TrimEnd(),
            Location: null,
            Schedule: new TimedSchedule(activity.StartUtc, activity.StartUtc + activity.ElapsedTime, activity.TimeZoneId),
            Visibility: EventVisibility.Normal,
            ShowAs: ShowAs.Busy,
            LastModifiedUtc: nowUtc,
            Origin: null,
            Url: activity.Url);
    }

    /// <summary>Stabiler Hash über alle gespiegelten Inhalte (unabhängig von IDs und Zeitstempeln).</summary>
    public static string ComputeHash(CalendarEvent mirror)
    {
        var schedule = mirror.Schedule switch
        {
            TimedSchedule t => string.Create(CultureInfo.InvariantCulture, $"T|{t.StartUtc.UtcTicks}|{t.EndUtc.UtcTicks}|{t.TimeZoneId}"),
            AllDaySchedule a => string.Create(CultureInfo.InvariantCulture, $"A|{a.StartDate:yyyy-MM-dd}|{a.EndDateExclusive:yyyy-MM-dd}"),
            _ => throw new NotSupportedException(mirror.Schedule.GetType().Name),
        };

        var canonical = string.Join(
            '\u001f',
            mirror.Title,
            mirror.Description ?? string.Empty,
            mirror.Location ?? string.Empty,
            schedule,
            mirror.Visibility.ToString(),
            mirror.ShowAs.ToString(),
            mirror.Url ?? string.Empty);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes, 0, 12);
    }
}
