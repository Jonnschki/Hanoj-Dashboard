using System.Collections.Concurrent;
using LifeDashboard.Core.Abstractions;

namespace LifeDashboard.Core.InMemory;

/// <summary>Sicherer Speicher im Arbeitsspeicher (nur für Tests).</summary>
public sealed class InMemorySecureTokenStore : ISecureTokenStore
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key) => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _values.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
