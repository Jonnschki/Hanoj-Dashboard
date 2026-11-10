namespace LifeDashboard.Infrastructure.Persistence;

/// <summary>
/// Schema-Migrationen der SQLite-Datenbank.
/// Regeln: Migrationen laufen nur vorwärts. Bestehende Einträge NIE ändern, neue nur hinten anhängen.
/// Index + 1 = Zielversion. Jede Migration ist eine Liste einzelner SQL-Anweisungen
/// (sqlite-net führt pro Aufruf nur eine Anweisung aus).
/// Zeitpunkte werden als UTC-Ticks (INTEGER) gespeichert.
/// </summary>
public static class DatabaseSchema
{
    public static readonly IReadOnlyList<string[]> Migrations =
    [
        // Version 1: Ledger, Sync-Historie, Einstellungen
        [
            """
            CREATE TABLE Ledger (
                OriginKey             TEXT    NOT NULL,
                TargetCalendarId      TEXT    NOT NULL,
                OriginProvider        TEXT    NOT NULL,
                SourceCalendarId      TEXT    NULL,
                OriginNativeId        TEXT    NOT NULL,
                OriginStartUtcTicks   INTEGER NOT NULL,
                TargetEventId         TEXT    NOT NULL,
                SyncHash              TEXT    NOT NULL,
                TargetLastModifiedUtcTicks INTEGER NOT NULL,
                LastSyncedUtcTicks    INTEGER NOT NULL,
                Status                TEXT    NOT NULL,
                PRIMARY KEY (OriginKey, TargetCalendarId)
            )
            """,
            "CREATE INDEX IX_Ledger_Target ON Ledger (TargetCalendarId, TargetEventId)",
            "CREATE INDEX IX_Ledger_Source ON Ledger (SourceCalendarId)",
            """
            CREATE TABLE SyncRun (
                Id                 TEXT    PRIMARY KEY,
                StartedUtcTicks    INTEGER NOT NULL,
                FinishedUtcTicks   INTEGER NULL,
                DryRun             INTEGER NOT NULL,
                Status             TEXT    NOT NULL,
                Created            INTEGER NOT NULL,
                Updated            INTEGER NOT NULL,
                Deleted            INTEGER NOT NULL,
                Conflicts          INTEGER NOT NULL,
                Errors             INTEGER NOT NULL
            )
            """,
            "CREATE INDEX IX_SyncRun_Started ON SyncRun (StartedUtcTicks DESC)",
            """
            CREATE TABLE Setting (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            )
            """,
        ],
    ];

    /// <summary>Aktuelle Zielversion.</summary>
    public static int CurrentVersion => Migrations.Count;
}
