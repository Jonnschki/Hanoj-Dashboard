using SQLite;

namespace LifeDashboard.Infrastructure.Persistence;

/// <summary>
/// Gemeinsame SQLite-Datenbank für Ledger, Sync-Historie und Einstellungen (sqlite-net-pcl).
/// Öffnet die Verbindung lazy und spielt beim ersten Zugriff fehlende Migrationen ein.
/// </summary>
public sealed class SqliteDatabase : IAsyncDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    public SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
    }

    public string DatabasePath { get; }

    /// <summary>Liefert die initialisierte Verbindung (Migrationen sind dann eingespielt).</summary>
    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                var directory = Path.GetDirectoryName(DatabasePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var connection = new SQLiteAsyncConnection(DatabasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
                await MigrateAsync(connection).ConfigureAwait(false);
                _connection = connection;
            }

            return _connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>Aktuelle Schemaversion der Datenbank (0 = leer).</summary>
    public async Task<int> GetSchemaVersionAsync()
    {
        var connection = await GetConnectionAsync().ConfigureAwait(false);
        return await ReadVersionAsync(connection).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            _connection = null;
        }

        _initLock.Dispose();
    }

    private static async Task MigrateAsync(SQLiteAsyncConnection connection)
    {
        await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER NOT NULL)").ConfigureAwait(false);
        var current = await ReadVersionAsync(connection).ConfigureAwait(false);

        for (var version = current + 1; version <= DatabaseSchema.Migrations.Count; version++)
        {
            var statements = DatabaseSchema.Migrations[version - 1];
            var target = version;

            // Jede Migration läuft komplett oder gar nicht (Transaktion).
            await connection.RunInTransactionAsync(db =>
            {
                foreach (var sql in statements)
                {
                    db.Execute(sql);
                }

                db.Execute("DELETE FROM SchemaVersion");
                db.Execute("INSERT INTO SchemaVersion (Version) VALUES (?)", target);
            }).ConfigureAwait(false);
        }
    }

    private static Task<int> ReadVersionAsync(SQLiteAsyncConnection connection)
        => connection.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion");
}
