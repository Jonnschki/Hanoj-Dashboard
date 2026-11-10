using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.InMemory;

/// <summary>Einstellungen im Speicher (für Tests).</summary>
public sealed class InMemoryUserSettingsStore : IUserSettingsStore
{
    private UserSettings _settings = new();

    public Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

    public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        return Task.CompletedTask;
    }
}
