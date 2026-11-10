namespace LifeDashboard.Core.Domain;

/// <summary>Ein verbundenes Konto (z. B. Microsoft-Arbeitskonto, privates Google-Konto).</summary>
public sealed record CalendarAccount(string Id, ProviderKind Provider, string DisplayName, string? UserPrincipal);

/// <summary>Ein Kalender innerhalb eines Kontos.</summary>
/// <param name="Id">Global eindeutige ID (Konvention: "{Provider}:{AccountId}:{NativeId}").</param>
/// <param name="AccountId">ID des Kontos.</param>
/// <param name="Provider">Anbieter.</param>
/// <param name="Name">Anzeigename.</param>
/// <param name="Ownership">Eigener oder fremder (read-only) Kalender.</param>
/// <param name="Category">Privat oder beruflich (für Spiegelungsregeln).</param>
public sealed record CalendarInfo(
    string Id,
    string AccountId,
    ProviderKind Provider,
    string Name,
    CalendarOwnership Ownership,
    CalendarCategory Category)
{
    /// <summary>Nur eigene Kalender sind beschreibbar.</summary>
    public bool IsWritable => Ownership == CalendarOwnership.Own;
}
