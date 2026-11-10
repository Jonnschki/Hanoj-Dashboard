using System.Text.Json;
using System.Text.Json.Serialization;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;
using Microsoft.Extensions.Logging;

namespace LifeDashboard.Infrastructure.Persistence;

/// <summary>
/// Speichert <see cref="UserSettings"/> als JSON in der Tabelle "Setting" (Schlüssel "user").
/// JSON statt einzelner Spalten: neue Einstellungen brauchen so keine Migration, fehlende Felder bekommen ihre Defaults.
/// </summary>
public sealed class SqliteUserSettingsStore(SqliteDatabase database, ILogger<SqliteUserSettingsStore> logger) : IUserSettingsStore
{
    internal const string Key = "user";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var json = await db.ExecuteScalarAsync<string?>("SELECT Value FROM Setting WHERE Key = ?", Key).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new UserSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<UserSettings>(json, Json) ?? new UserSettings();
        }
        catch (JsonException ex)
        {
            // Beschädigte Einstellungen dürfen die App nicht blockieren: Defaults verwenden, Fehler protokollieren.
            logger.LogWarning(ex, "Gespeicherte Einstellungen sind ungültig, Defaults werden verwendet.");
            return new UserSettings();
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var db = await database.GetConnectionAsync().ConfigureAwait(false);
        var json = JsonSerializer.Serialize(settings, Json);
        await db.ExecuteAsync("INSERT OR REPLACE INTO Setting (Key, Value) VALUES (?, ?)", Key, json).ConfigureAwait(false);
    }
}
