using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.Abstractions;

/// <summary>Quelle für Sportaktivitäten (z. B. Strava).</summary>
public interface IActivityProvider
{
    /// <summary>Anbieter.</summary>
    ProviderKind Kind { get; }

    /// <summary>Liest Aktivitäten, die nach <paramref name="afterUtc"/> gestartet sind. Implementierungen beachten Rate-Limits.</summary>
    Task<Result<IReadOnlyList<SportActivity>>> GetActivitiesAsync(DateTimeOffset afterUtc, CancellationToken cancellationToken = default);
}

/// <summary>Quelle für Marktdaten (Indizes, Watchlist). Kein Scraping, nur offizielle APIs.</summary>
public interface IMarketDataProvider
{
    /// <summary>Name des Anbieters (für Quellenangabe im UI).</summary>
    string Name { get; }

    /// <summary>Liefert aktuelle Kurse zu den Symbolen. Unbekannte Symbole werden ausgelassen.</summary>
    Task<Result<IReadOnlyList<MarketQuote>>> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken = default);
}

/// <summary>Ergebnis eines News-Abrufs: Meldungen plus Hinweise zu Feeds, die gerade nicht erreichbar waren.</summary>
public sealed record NewsSnapshot(IReadOnlyList<NewsItem> Items, IReadOnlyList<string> FailedSources, DateTimeOffset FetchedUtc);

/// <summary>Quelle für Schlagzeilen (offizielle RSS-/Atom-Feeds der Zeitungen, kein Scraping).</summary>
public interface INewsProvider
{
    /// <summary>Konfigurierte Feeds.</summary>
    IReadOnlyList<NewsFeed> Feeds { get; }

    /// <summary>Lädt alle Feeds (mit HTTP-Caching). Fehlerhafte Feeds werden übersprungen und gemeldet.</summary>
    Task<NewsSnapshot> GetLatestAsync(CancellationToken cancellationToken = default);
}

/// <summary>Exportformat.</summary>
public enum ExportFormat
{
    Xlsx,
    Csv,
}

/// <summary>Eine Zeile des Sync-Protokolls (für Export und UI).</summary>
public sealed record SyncLogEntry(DateTimeOffset TimestampUtc, string Level, string Message);

/// <summary>Export von Terminen, Sportdaten und Sync-Log.</summary>
public interface IExportService
{
    /// <summary>Format dieses Exporters.</summary>
    ExportFormat Format { get; }

    /// <summary>Dateiendung inklusive Punkt, z. B. ".xlsx".</summary>
    string FileExtension { get; }

    /// <summary>Exportiert Termine in den Stream.</summary>
    Task ExportEventsAsync(IEnumerable<CalendarEvent> events, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>Exportiert Sportaktivitäten in den Stream.</summary>
    Task ExportActivitiesAsync(IEnumerable<SportActivity> activities, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>Exportiert das Sync-Protokoll in den Stream.</summary>
    Task ExportSyncLogAsync(IEnumerable<SyncLogEntry> entries, Stream destination, CancellationToken cancellationToken = default);
}

/// <summary>Lokale Benachrichtigungen (iOS: UNUserNotificationCenter, Windows: Toast).</summary>
public interface INotificationService
{
    /// <summary>Fragt (falls nötig) die Berechtigung an. Liefert true, wenn Benachrichtigungen erlaubt sind.</summary>
    Task<bool> EnsurePermissionAsync(CancellationToken cancellationToken = default);

    /// <summary>Zeigt sofort eine Benachrichtigung.</summary>
    Task ShowAsync(string title, string body, CancellationToken cancellationToken = default);

    /// <summary>Plant eine tägliche Benachrichtigung zur lokalen Uhrzeit (ersetzt eine bestehende).</summary>
    Task ScheduleDailyAsync(TimeOnly localTime, string title, string body, CancellationToken cancellationToken = default);

    /// <summary>Entfernt die geplante tägliche Benachrichtigung.</summary>
    Task CancelDailyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Sicherer Speicher für Tokens (iOS Keychain / Windows Credential Store über MAUI SecureStorage).
/// Tokens dürfen NIE an anderer Stelle (Dateien, Logs, SQLite) im Klartext landen.
/// </summary>
public interface ISecureTokenStore
{
    /// <summary>Liest einen Wert oder null.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Speichert einen Wert.</summary>
    Task SetAsync(string key, string value);

    /// <summary>Entfernt einen Wert.</summary>
    Task RemoveAsync(string key);
}

/// <summary>Uhr-Abstraktion, damit Zeitlogik testbar ist.</summary>
public interface IClock
{
    /// <summary>Aktuelle Zeit in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Lokale Zeitzone des Geräts.</summary>
    TimeZoneInfo LocalTimeZone { get; }
}

/// <summary>Öffnet eine URL im System-Browser (für OAuth-Logins). Plattformspezifisch, wird von der App bereitgestellt.</summary>
public interface IUrlLauncher
{
    Task OpenAsync(Uri url, CancellationToken cancellationToken = default);
}

/// <summary>Liefert ein gültiges Zugriffstoken für eine API (erneuert es bei Bedarf still im Hintergrund).</summary>
public interface IAccessTokenSource
{
    /// <summary>Verbundenes Konto oder null.</summary>
    CalendarAccount? Account { get; }

    /// <summary>Token oder Fehler <c>auth.not_connected</c>, wenn (erneut) eine Anmeldung nötig ist.</summary>
    Task<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Verbinden/Trennen eines Kontos (OAuth-Login), für die Einstellungsseite.</summary>
public interface IAccountConnector
{
    /// <summary>Anbieter.</summary>
    ProviderKind Kind { get; }

    /// <summary>True, wenn ein gültiges (oder erneuerbares) Token vorliegt.</summary>
    Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default);

    /// <summary>Startet den interaktiven Login (System-Browser, PKCE).</summary>
    Task<Result<CalendarAccount>> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Meldet ab und löscht gespeicherte Tokens.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
