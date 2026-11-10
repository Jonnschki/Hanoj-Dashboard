using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Digest;
using LifeDashboard.Core.InMemory;
using LifeDashboard.Core.Sync;
using LifeDashboard.Infrastructure.Configuration;
using LifeDashboard.Infrastructure.Demo;
using LifeDashboard.Infrastructure.Export;
using LifeDashboard.Infrastructure.Markets;
using LifeDashboard.Infrastructure.Strava;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Ledger. Phase 1 ersetzt InMemory durch <see cref="Persistence.SqliteSyncLedger"/>.</summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddSingleton<ISyncLedger, InMemorySyncLedger>();
        return services;
    }

    /// <summary>Kalender-Provider und Login-Connectoren.</summary>
    public static IServiceCollection AddCalendars(this IServiceCollection services, IConfiguration configuration)
    {
        var app = configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
        if (app.UseDemoData)
        {
            services.AddSingleton<IReadOnlyList<ICalendarProvider>>(sp => DemoData.CreateCalendarProviders(sp.GetRequiredService<IClock>()));
            services.AddSingleton<IEnumerable<ICalendarProvider>>(sp => sp.GetRequiredService<IReadOnlyList<ICalendarProvider>>());
        }
        else
        {
            // Phase 2/3: Provider werden pro verbundenem Konto Ã¼ber eine Account-Registry erzeugt.
            services.AddSingleton<IEnumerable<ICalendarProvider>>(_ => []);
        }

        services.AddSingleton<IAccountConnector, MicrosoftGraph.MsalAccountConnector>();
        services.AddSingleton<IAccountConnector, GoogleCalendar.GoogleAccountConnector>();
        services.AddSingleton<IAccountConnector, StravaAccountConnector>();
        return services;
    }

    /// <summary>Strava (bzw. Demo-AktivitÃ¤ten) inkl. HttpClient mit Retry/Backoff/Timeout.</summary>
    public static IServiceCollection AddActivities(this IServiceCollection services, IConfiguration configuration)
    {
        var app = configuration.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();
        if (app.UseDemoData)
        {
            services.AddSingleton<IActivityProvider, DemoActivityProvider>();
            return services;
        }

        services.AddHttpClient<IActivityProvider, StravaActivityProvider>((sp, client) =>
                client.BaseAddress = sp.GetRequiredService<IOptions<StravaOptions>>().Value.BaseAddress)
            .AddStandardResilienceHandler(); // Polly v8: Retry (exp. Backoff + Jitter, beachtet Retry-After), Circuit Breaker, Timeouts
        return services;
    }

    /// <summary>Marktdaten: Finnhub, falls konfiguriert, sonst Demo.</summary>
    public static IServiceCollection AddMarketData(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(MarketDataOptions.Section).Get<MarketDataOptions>() ?? new MarketDataOptions();
        if (string.Equals(options.Provider, "Finnhub", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(options.ApiKey))
        {
            services.AddHttpClient<IMarketDataProvider, FinnhubMarketDataProvider>((sp, client) =>
                    client.BaseAddress = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value.BaseAddress)
                .AddStandardResilienceHandler();
        }
        else
        {
            services.AddSingleton<IMarketDataProvider, DemoMarketDataProvider>();
        }

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
    public static IServiceCollection AddLifeDashboard(this IServiceCollection services, IConfiguration configuration) => services
        .AddLifeDashboardOptions(configuration)
        .AddLifeDashboardCore()
        .AddPersistence()
        .AddCalendars(configuration)
        .AddActivities(configuration)
        .AddMarketData(configuration)
        .AddExport();
}
