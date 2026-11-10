namespace LifeDashboard.Core.Domain;

/// <summary>
/// Ein abonnierter ICS-Kalender (z. B. von einem Kunden über "Kalender veröffentlichen" in Outlook).
/// ICS-Kalender sind immer <see cref="CalendarOwnership.SharedReadOnly"/>.
/// </summary>
/// <param name="Id">Stabile ID (wird Teil der Kalender-ID).</param>
/// <param name="Name">Anzeigename, z. B. "Kunde Müller GmbH".</param>
/// <param name="Category">Lebensbereich (für Anzeige/Regeln), meist <see cref="CalendarCategory.Work"/>.</param>
public sealed record IcsSubscription(string Id, string Name, CalendarCategory Category)
{
    /// <summary>Schlüssel der geheimen URL im sicheren Speicher.</summary>
    public string SecretKey => SecretKeyFor(Id);

    public static string SecretKeyFor(string id) => $"ics-url:{id}";

    /// <summary>Normalisiert einen eingegebenen Link (webcal:// → https://). Liefert null bei ungültigen Links.</summary>
    public static Uri? NormalizeUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        if (text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            text = "https://" + text["webcal://".Length..];
        }

        // Nur HTTPS: ein ICS-Link ist ein Geheimnis und darf nicht unverschlüsselt übertragen werden.
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
    }
}
