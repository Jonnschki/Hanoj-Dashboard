using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Digest;
using LifeDashboard.Core.Domain;
using LifeDashboard.Core.InMemory;
using LifeDashboard.Core.Sync;
using LifeDashboard.Infrastructure.Configuration;
using LifeDashboard.Infrastructure.Demo;
using LifeDashboard.Infrastructure.Export;
using LifeDashboard.Infrastructure.Markets;
using LifeDashboard.Infrastructure.Strava;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace LifeDashboard.Infrastructure;

/// <summary>DI-Registrierung, eine Extension-Methode pro Modul. Aufruf zentral in MauiProgram.cs.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Bindet alle Options-Klassen an die Konfiguration.</summary>
    public static IServiceCollection AddLifeDashboardOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppOptions>(configuration.GetSection(AppOptions.Section));
        services.Configure<MicrosoftOptions>(configuration.GetSection(MicrosoftOptions.Section));
        services.Configure<GoogleOptions>(configuration.GetSection(GoogleOptions.Section));
        services.Configure<StravaOptions>(configuration.GetSection(StravaOptions.Section));
        services.Configure<MarketDataOptions>(configuration.GetSection(MarketDataOptions.Section));
        services.Configure<SyncSettings>(configuration.GetSection(SyncSettings.Section));
        return services;
    }

    /// <summary>Kern: Uhr, Regeln, SyncEngine, Digest.</summary>
    public static IServiceCollection AddLifeDashboardCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<SyncRules>();
        services.AddSingleton(sp => new ConflictResolver(sp.GetRequiredService<IOptions<SyncSettings>>().Value.ConflictStrategy));
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<DailyDigestBuilder>();
        return services;
    }

    /// <summary>
    /// SQLite-Datenbank, Einstellungen und Ledger.
    /// Im Demo-Modus bleibt das Ledger im Speicher: Die Demo-Kalender entstehen bei jedem Start neu (mit denselben IDs),
    /// ein dauerhaftes Ledger würde sonst auf fremde Demo-Termine zeigen. Einstellungen werden immer gespeichert.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration, string databasePath)
    {
        services.AddSingleton(_ => new Persistence.SqliteDatabase(databasePath));
        services.AddSingleton<IUserSettingsStore, Persistence.SqliteUserSettingsStore>();

        var app = configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
        if (app.UseDemoData)
        {
            services.AddSingleton<ISyncLedger, InMemorySyncLedger>();
        }
        else
        {
            services.AddSingleton<ISyncLedger, Persistence.SqliteSyncLedger>();
        }

        return services;
    }

    /// <summary>
    /// Kalender-Provider und Login-Connectoren. Jeder Provider wird einzeln als <see cref="ICalendarProvider"/>
    /// registriert; die SyncEngine bekommt alle über <c>IEnumerable&lt;ICalendarProvider&gt;</c>.
    /// </summary>
    public static IServiceCollection AddCalendars(this IServiceCollection services, IConfiguration configuration)
    {
        var app = configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
        if (app.UseDemoData)
        {
            services.AddSingleton(sp => new DemoCalendarSet(DemoData.CreateCalendarProviders(sp.GetRequiredService<IClock>())));
            for (var i = 0; i < DemoData.ProviderCount; i++)
            {
                var index = i;
                services.AddSingleton<ICalendarProvider>(sp => sp.GetRequiredService<DemoCalendarSet>().Providers[index]);
            }
        }

        AddAccounts(services, configuration);

        if (!app.UseDemoData)
        {
            // Kalender-APIs: Wiederholungen nur für sichere Methoden (GET/DELETE …), sonst könnte ein
            // wiederholtes POST einen Termin doppelt anlegen.
            services.AddHttpClient("graph").AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
            services.AddHttpClient("google").AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
            services.AddSingleton<ICalendarProvider>(sp => new MicrosoftGraph.GraphCalendarProvider(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("graph"), sp.GetRequiredService<MicrosoftGraph.MicrosoftSession>()));
            services.AddSingleton<ICalendarProvider>(sp => new GoogleCalendar.GoogleCalendarProvider(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("google"), sp.GetRequiredKeyedService<Auth.OAuthSession>(ProviderKind.Google)));
        }

        // ICS-Abos fremder Kalender (immer read-only), auch im Demo-Modus.
        // RemoveAllLoggers: Der HttpClient würde sonst die geheime ICS-URL ins Log schreiben.
        services.AddHttpClient<Ics.IcsCalendarProvider>(client =>
            {
                client.MaxResponseContentBufferSize = 10 * 1024 * 1024; // Schutz gegen riesige Antworten
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LifeDashboard/0.1");
            })
            .RemoveAllLoggers()
            .AddStandardResilienceHandler();
        services.AddSingleton<ICalendarProvider>(sp => sp.GetRequiredService<Ics.IcsCalendarProvider>());
        return services;
    }

    /// <summary>
    /// Anmeldungen: Microsoft (MSAL), Google und Strava (eigener OAuth-Ablauf über <see cref="Auth.LoopbackBrowser"/>).
    /// Die App muss <see cref="IUrlLauncher"/> und <see cref="ISecureTokenStore"/> bereitstellen.
    /// </summary>
    private static void AddAccounts(IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(Auth.LoopbackBrowser)))
        {
            return;
        }

        // Token-Endpunkte bewusst OHNE automatische Wiederholung (Anmeldecodes sind nur einmal gültig).
        services.AddHttpClient("oauth", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<Auth.LoopbackBrowser>();
        services.AddSingleton<MicrosoftGraph.MicrosoftSession>();

        var google = configuration.GetSection(GoogleOptions.Section).Get<GoogleOptions>() ?? new GoogleOptions();
        services.AddKeyedSingleton(ProviderKind.Google, (sp, _) =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new Auth.OAuthSession(
                GoogleCalendar.GoogleOAuth.Create(google.ClientId, google.ClientSecret),
                factory.CreateClient("oauth"),
                sp.GetRequiredService<Auth.LoopbackBrowser>(),
                sp.GetRequiredService<ISecureTokenStore>(),
                sp.GetRequiredService<IClock>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Auth.OAuthSession>>())
            {
                ResolveAccountName = (token, ct) => GoogleCalendar.GoogleOAuth.ResolveEmailAsync(factory.CreateClient("oauth"), token, ct),
            };
        });

        var strava = configuration.GetSection(StravaOptions.Section).Get<StravaOptions>() ?? new StravaOptions();
        services.AddKeyedSingleton(ProviderKind.Strava, (sp, _) => new Auth.OAuthSession(
            StravaOAuth.Create(strava.ClientId, strava.ClientSecret),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("oauth"),
            sp.GetRequiredService<Auth.LoopbackBrowser>(),
            sp.GetRequiredService<ISecureTokenStore>(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Auth.OAuthSession>>())
        {
            AccountNameFromTokenResponse = StravaOAuth.AthleteName,
        });

        services.AddSingleton<IAccountConnector, MicrosoftGraph.MsalAccountConnector>();
        services.AddSingleton<IAccountConnector>(sp => new Auth.OAuthAccountConnector(sp.GetRequiredKeyedService<Auth.OAuthSession>(ProviderKind.Google)));
        services.AddSingleton<IAccountConnector>(sp => new Auth.OAuthAccountConnector(sp.GetRequiredKeyedService<Auth.OAuthSession>(ProviderKind.Strava)));
    }

    /// <summary>Strava (bzw. Demo-Aktivitäten) inkl. HttpClient mit Retry/Backoff/Timeout.</summary>
    public static IServiceCollection AddActivities(this IServiceCollection services, IConfiguration configuration)
    {
        var app = configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
        if (app.UseDemoData)
        {
            services.AddSingleton<IActivityProvider, DemoActivityProvider>();
            return services;
        }

        AddAccounts(services, configuration);
        services.AddHttpClient("strava", (sp, client) => client.BaseAddress = sp.GetRequiredService<IOptions<StravaOptions>>().Value.BaseAddress)
            .AddStandardResilienceHandler(); // Polly v8: Retry (exp. Backoff + Jitter, beachtet Retry-After), Circuit Breaker, Timeouts
        services.AddSingleton<IActivityProvider>(sp => new StravaActivityProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("strava"),
            sp.GetRequiredKeyedService<Auth.OAuthSession>(ProviderKind.Strava)));
        return services;
    }

    /// <summary>Marktdaten: Finnhub, falls konfiguriert, sonst Demo.</summary>
    public static IServiceCollection AddMarketData(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MarketDataOptions.Section).Get<MarketDataOptions>() ?? new MarketDataOptions();
        var hasFinnhubKey = !string.IsNullOrWhiteSpace(options.ApiKey);

        if (string.Equals(options.Provider, "Demo", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMarketDataProvider, DemoMarketDataProvider>();
            return services;
        }

        if (hasFinnhubKey)
        {
            services.AddHttpClient<FinnhubMarketDataProvider>((sp, client) =>
                    client.BaseAddress = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value.BaseAddress)
                .AddStandardResilienceHandler();
        }

        if (string.Equals(options.Provider, "Finnhub", StringComparison.OrdinalIgnoreCase) && hasFinnhubKey)
        {
            services.AddSingleton<IMarketDataProvider>(sp => sp.GetRequiredService<FinnhubMarketDataProvider>());
            return services;
        }

        // Standard: Yahoo, bei fehlenden Symbolen/Ausfall Finnhub (falls API-Key vorhanden).
        services.AddHttpClient<YahooFinanceMarketDataProvider>((sp, client) =>
            {
                client.BaseAddress = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value.YahooBaseAddress;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LifeDashboard/0.1"); // ohne User-Agent antwortet Yahoo mit 429
            })
            .AddStandardResilienceHandler();
        services.AddSingleton<IMarketDataProvider>(sp => new FallbackMarketDataProvider(
            sp.GetRequiredService<YahooFinanceMarketDataProvider>(),
            hasFinnhubKey ? sp.GetRequiredService<FinnhubMarketDataProvider>() : null,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FallbackMarketDataProvider>>()));
        return services;
    }

    /// <summary>Schlagzeilen aus offiziellen RSS-Feeds (Singleton, damit der HTTP-Cache erhalten bleibt).</summary>
    public static IServiceCollection AddNews(this IServiceCollection services)
    {
        services.AddHttpClient<News.RssNewsProvider>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("LifeDashboard/0.1 (private RSS reader)");
                client.MaxResponseContentBufferSize = 5 * 1024 * 1024;
            })
            .AddStandardResilienceHandler();
        services.AddSingleton<INewsProvider>(sp => sp.GetRequiredService<News.RssNewsProvider>());
        services.AddSingleton<Core.News.HeadlineRanker>();
        return services;
    }

    /// <summary>Excel- und CSV-Export.</summary>
    public static IServiceCollection AddExport(this IServiceCollection services)
    {
        services.AddSingleton<IExportService, ExcelExportService>();
        services.AddSingleton<IExportService, CsvExportService>();
        return services;
    }

    /// <summary>Komplettregistrierung aller Module (ohne plattformspezifische Dienste wie SecureStorage/Notifications).</summary>
    /// <param name="services">DI-Container.</param>
    /// <param name="configuration">Konfiguration (appsettings).</param>
    /// <param name="databasePath">Pfad der SQLite-Datei (App: FileSystem.AppDataDirectory).</param>
    public static IServiceCollection AddLifeDashboard(this IServiceCollection services, IConfiguration configuration, string databasePath) => services
        .AddLifeDashboardOptions(configuration)
        .AddLifeDashboardCore()
        .AddPersistence(configuration, databasePath)
        .AddCalendars(configuration)
        .AddActivities(configuration)
        .AddMarketData(configuration)
        .AddNews()
        .AddExport();
}
