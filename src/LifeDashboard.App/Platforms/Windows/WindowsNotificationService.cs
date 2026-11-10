using LifeDashboard.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace LifeDashboard.App.Platforms.Windows;

/// <summary>
/// Windows-Benachrichtigungen (Info-Center) über die App Notifications des Windows App SDK.
/// Funktioniert auch für die unpackaged App; die Registrierung passiert beim ersten Aufruf.
/// Tägliche Benachrichtigungen plant der <see cref="Services.DigestScheduler"/> (solange die App läuft).
/// </summary>
public sealed class WindowsNotificationService(ILogger<WindowsNotificationService> logger) : INotificationService
{
    private static readonly object Gate = new();
    private static bool _registered;

    public Task<bool> EnsurePermissionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(EnsureRegistered() && AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled);

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        if (!EnsureRegistered())
        {
            return Task.CompletedTask;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(body)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            logger.LogWarning("Benachrichtigung nicht möglich: {Type}", ex.GetType().Name);
        }

        return Task.CompletedTask;
    }

    // Die Uhrzeit verwaltet der DigestScheduler; hier ist nichts dauerhaft zu planen.
    public Task ScheduleDailyAsync(TimeOnly localTime, string title, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task CancelDailyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private bool EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return true;
            }

            try
            {
                if (!AppNotificationManager.IsSupported())
                {
                    return false;
                }

                AppNotificationManager.Default.Register();
                _registered = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                logger.LogWarning("Benachrichtigungen nicht verfügbar: {Type}", ex.GetType().Name);
            }

            return _registered;
        }
    }
}
