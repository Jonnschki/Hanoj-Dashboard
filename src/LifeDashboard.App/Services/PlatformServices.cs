using LifeDashboard.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace LifeDashboard.App.Services;

/// <summary>
/// Tokens über MAUI SecureStorage: iOS → Keychain, Windows → Credential Locker/DPAPI.
/// Niemals Tokens in Preferences, Dateien oder Logs ablegen.
/// </summary>
public sealed class MauiSecureTokenStore : ISecureTokenStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public Task RemoveAsync(string key)
    {
        SecureStorage.Default.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Lokale Benachrichtigungen. STUB – Umsetzung in Phase 7:
/// iOS: UNUserNotificationCenter (UNCalendarNotificationTrigger für die tägliche Uhrzeit).
/// Windows: Microsoft.Windows.AppNotifications (AppNotificationManager; unpackaged braucht Registrierung beim Start).
/// Bis dahin wird nur geloggt.
/// </summary>
public sealed class LocalNotificationService(ILogger<LocalNotificationService> logger) : INotificationService
{
    public Task<bool> EnsurePermissionAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task ShowAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Notification] {Title}: {Body}", title, body);
        return Task.CompletedTask;
    }

    public Task ScheduleDailyAsync(TimeOnly localTime, string title, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[Notification] täglich {Time} geplant (Stub)", localTime);
        return Task.CompletedTask;
    }

    public Task CancelDailyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
