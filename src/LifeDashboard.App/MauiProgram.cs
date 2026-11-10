using System.Reflection;
using CommunityToolkit.Maui;
using LifeDashboard.App.Services;
using LifeDashboard.App.ViewModels;
using LifeDashboard.App.Views;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeDashboard.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        AddConfiguration(builder.Configuration);

        // Alle Module aus Core/Infrastructure (siehe ServiceCollectionExtensions).
        // SQLite-Datei im App-Datenordner (Windows: %LOCALAPPDATA%\…, iOS: Library-Ordner der App, wird gesichert).
        builder.Services.AddLifeDashboard(builder.Configuration, Path.Combine(FileSystem.AppDataDirectory, "lifedashboard.db3"));

        // Plattformspezifische Dienste.
        builder.Services.AddSingleton<ISecureTokenStore, MauiSecureTokenStore>();
        builder.Services.AddSingleton<INotificationService, LocalNotificationService>();
        builder.Services.AddSingleton<SyncCoordinator>();

        AddPages(builder.Services);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>Lädt appsettings.json und (falls vorhanden) appsettings.Local.json aus den eingebetteten Ressourcen.</summary>
    private static void AddConfiguration(ConfigurationManager configuration)
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in new[] { "appsettings.json", "appsettings.Local.json" })
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            // JSON-Provider liest den Stream sofort ein, daher kann er danach freigegeben werden.
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            configuration.AddJsonStream(copy);
        }
    }

    private static void AddPages(IServiceCollection services)
    {
        services.AddTransient<DashboardViewModel>().AddTransient<DashboardPage>();
        services.AddTransient<SyncViewModel>().AddTransient<SyncPage>();
        services.AddTransient<CalendarViewModel>().AddTransient<CalendarPage>();
        services.AddTransient<SportViewModel>().AddTransient<SportPage>();
        services.AddTransient<DigestViewModel>().AddTransient<DigestPage>();
        services.AddTransient<SettingsViewModel>().AddTransient<SettingsPage>();
    }
}
