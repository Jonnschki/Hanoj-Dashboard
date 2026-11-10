namespace LifeDashboard.Core.Domain;

/// <summary>Datenquelle bzw. Anbieter.</summary>
public enum ProviderKind
{
    Microsoft,
    Google,
    Strava,
    Ics,
    InMemory,
}

/// <summary>Wem ein Kalender gehört. Nur <see cref="Own"/> darf beschrieben werden.</summary>
public enum CalendarOwnership
{
    /// <summary>Eigener Kalender: lesen und schreiben erlaubt.</summary>
    Own,

    /// <summary>Kalender von Kollegen/Vorgesetzten: strikt read-only.</summary>
    SharedReadOnly,
}

/// <summary>Lebensbereich eines Kalenders, Grundlage der Datenschutzregeln.</summary>
public enum CalendarCategory
{
    Private,
    Work,
}

/// <summary>Sichtbarkeit eines Termins.</summary>
public enum EventVisibility
{
    Normal,
    Private,
}

/// <summary>Anzeige als (Graph: showAs, Google: transparency).</summary>
public enum ShowAs
{
    Busy,
    Free,
    Tentative,
    OutOfOffice,
}
