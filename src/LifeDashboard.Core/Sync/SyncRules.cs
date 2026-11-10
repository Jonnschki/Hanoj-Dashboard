using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Sync;

/// <summary>Wie viel von einem Termin in den Zielkalender übernommen wird.</summary>
public enum MirrorDetailLevel
{
    /// <summary>Gar nicht spiegeln.</summary>
    None,

    /// <summary>Nur als Block "Beschäftigt" (ohne Titel, Ort, Beschreibung).</summary>
    BusyOnly,

    /// <summary>Alle Details übernehmen.</summary>
    Full,
}

/// <summary>Konfliktstrategie, wenn eine Spiegelung im Zielkalender manuell geändert wurde.</summary>
public enum ConflictStrategy
{
    /// <summary>Der zuletzt geänderte Stand gewinnt (Default). Konflikte werden protokolliert.</summary>
    LastModifiedWins,

    /// <summary>Das Original überschreibt immer die Spiegelung.</summary>
    SourceWins,

    /// <summary>Manuelle Änderungen an der Spiegelung bleiben erhalten.</summary>
    TargetWins,
}

/// <summary>Spiegelungsregel zwischen zwei Kalenderkategorien.</summary>
public sealed record MirrorRule(CalendarCategory From, CalendarCategory To, MirrorDetailLevel Level);

/// <summary>
/// Konfigurierbare Datenschutz- und Spiegelungsregeln.
/// Default: gleiche Kategorie = volle Details, privat ↔ beruflich = nur "Beschäftigt".
/// </summary>
public sealed class SyncRules
{
    /// <summary>Titel für Termine ohne Details.</summary>
    public string BusyTitle { get; init; } = "Beschäftigt";

    /// <summary>Regeln; nicht abgedeckte Kombinationen fallen auf <see cref="MirrorDetailLevel.BusyOnly"/> zurück.</summary>
    public IReadOnlyList<MirrorRule> Rules { get; init; } =
    [
        new(CalendarCategory.Private, CalendarCategory.Private, MirrorDetailLevel.Full),
        new(CalendarCategory.Work, CalendarCategory.Work, MirrorDetailLevel.Full),
        new(CalendarCategory.Private, CalendarCategory.Work, MirrorDetailLevel.BusyOnly),
        new(CalendarCategory.Work, CalendarCategory.Private, MirrorDetailLevel.BusyOnly),
    ];

    /// <summary>Als privat markierte Termine nie mit Details spiegeln.</summary>
    public bool PrivateEventsBusyOnly { get; init; } = true;

    /// <summary>Als "frei" markierte Termine nicht spiegeln (blockieren keine Zeit).</summary>
    public bool SkipFreeEvents { get; init; } = true;

    /// <summary>Ermittelt die Detailstufe für einen Termin von Quelle nach Ziel.</summary>
    public MirrorDetailLevel Resolve(CalendarInfo source, CalendarInfo target, CalendarEvent calendarEvent)
    {
        if (SkipFreeEvents && calendarEvent.ShowAs == ShowAs.Free)
        {
            return MirrorDetailLevel.None;
        }

        var rule = Rules.FirstOrDefault(r => r.From == source.Category && r.To == target.Category);
        var level = rule?.Level ?? MirrorDetailLevel.BusyOnly;

        if (PrivateEventsBusyOnly && calendarEvent.Visibility == EventVisibility.Private && level == MirrorDetailLevel.Full)
        {
            level = MirrorDetailLevel.BusyOnly;
        }

        return level;
    }
}
