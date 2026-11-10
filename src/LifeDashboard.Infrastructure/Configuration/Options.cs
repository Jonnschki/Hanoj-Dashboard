using LifeDashboard.Core.Sync;

namespace LifeDashboard.Infrastructure.Configuration;

// Options-Klassen werden aus appsettings.json + appsettings.Local.json (nicht eingecheckt) gebunden.
// Secrets (Client-Secrets, API-Keys) gehören AUSSCHLIESSLICH in appsettings.Local.json bzw. User-Secrets.

/// <summary>Allgemeine App-Einstellungen.</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>Demo-Modus: In-Memory-Kalender mit Beispieldaten statt echter Konten.</summary>
    public bool UseDemoData { get; set; } = true;
}

/// <summary>Microsoft Entra ID / Graph (MSAL, öffentlicher Client, kein Secret).</summary>
public sealed class MicrosoftOptions
{
    public const string Section = "Microsoft";

    public string ClientId { get; set; } = string.Empty;

    /// <summary>"common" (Arbeit + privat), "organizations" oder konkrete Tenant-ID.</summary>
    public string TenantId { get; set; } = "common";

    /// <summary>Redirect-URI, z. B. "msal{ClientId}://auth" (iOS) bzw. "http://localhost" (Windows).</summary>
    public string? RedirectUri { get; set; }

    public IReadOnlyList<string> Scopes { get; set; } = ["User.Read", "Calendars.ReadWrite", "Calendars.Read.Shared", "offline_access"];
}

/// <summary>Google OAuth 2.0 (Authorization Code + PKCE).</summary>
public sealed class GoogleOptions
{
    public const string Section = "Google";

    public string ClientId { get; set; } = string.Empty;

    /// <summary>Nur für den Desktop-Client-Typ nötig; Google behandelt es bei installierten Apps nicht als echtes Geheimnis.</summary>
    public string? ClientSecret { get; set; }

    public string? RedirectUri { get; set; }

    public IReadOnlyList<string> Scopes { get; set; } = ["https://www.googleapis.com/auth/calendar"];
}

/// <summary>Strava OAuth 2.0 + API.</summary>
public sealed class StravaOptions
{
    public const string Section = "Strava";

    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public string RedirectUri { get; set; } = "lifedashboard://strava-callback";

    public Uri BaseAddress { get; set; } = new("https://www.strava.com/api/v3/");

    public string Scope { get; set; } = "activity:read_all";
}

/// <summary>Marktdaten (Finnhub als Standard).</summary>
public sealed class MarketDataOptions
{
    public const string Section = "MarketData";

    /// <summary>"Finnhub" oder "Demo".</summary>
    public string Provider { get; set; } = "Demo";

    public string? ApiKey { get; set; }

    public Uri BaseAddress { get; set; } = new("https://finnhub.io/api/v1/");

    /// <summary>
    /// Watchlist-Symbole, z. B. "SPY", "QQQ", "AAPL". Bewusst ohne Default-Werte:
    /// der Configuration-Binder HÄNGT Listen an bestehende Werte an, statt sie zu ersetzen.
    /// </summary>
    public IReadOnlyList<string> Watchlist { get; set; } = [];
}

/// <summary>Sync-Einstellungen.</summary>
public sealed class SyncSettings
{
    public const string Section = "Sync";

    public bool DryRunByDefault { get; set; } = true;

    public ConflictStrategy ConflictStrategy { get; set; } = ConflictStrategy.LastModifiedWins;

    public int WindowPastDays { get; set; } = 7;

    public int WindowFutureDays { get; set; } = 60;

    /// <summary>Uhrzeit des Daily Digests (lokal).</summary>
    public TimeOnly DigestTime { get; set; } = new(6, 30);
}
