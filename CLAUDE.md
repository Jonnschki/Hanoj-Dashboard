# CLAUDE.md – Regeln für dieses Repo

Life Dashboard: .NET 10 MAUI App (Windows + iOS). Nutzer ist kein Profi: wichtige Entscheidungen kurz erklären,
Code einfach halten, keine unnötige Komplexität. Kommunikation auf Deutsch.

## Build & Test
```bash
dotnet build LifeDashboard.slnx                    # Core, Infrastructure, Tests, App (nur Windows-Target)
dotnet test LifeDashboard.slnx                     # alle Tests
dotnet build src/LifeDashboard.App -f net10.0-windows10.0.19041.0 -t:Run   # App starten (Demo-Modus)
dotnet build src/LifeDashboard.App -p:EnableIos=true                       # iOS (nur mit gekoppeltem Mac)
```
- SDK ist über `global.json` fixiert (10.0.4xx). Paketversionen **nur** in `Directory.Packages.props` (Central Package Management) pflegen.
  In `.csproj`-Dateien `<PackageReference Include="…" />` **ohne** Version.
- Lange Pfade: In tief verschachtelten Ordnern (Worktrees) kann der Release-Build der Windows-App an MAX_PATH scheitern (MSB3030). Das ist kein Code-Fehler.

## Architekturregeln (strikt)
1. **`LifeDashboard.Core` hat keine Abhängigkeit** auf Infrastructure, App, MAUI oder Plattform-APIs. Einzige erlaubte Paketreferenz: `Microsoft.Extensions.Logging.Abstractions`.
2. Abhängigkeiten zeigen nach innen: App → Infrastructure → Core.
3. **ViewModels enthalten keine Business-Logik**, nur Orchestrierung und Bindings. Logik gehört in Core (testbar).
4. `Nullable` ist überall aktiv. `TreatWarningsAsErrors` gilt für Core und Infrastructure.
5. **Zeiten intern immer UTC** (`DateTimeOffset`, Offset 0, `Guard.Utc`), die Zeitzone wird separat gespeichert. Ganztagstermine nutzen `AllDaySchedule` (`DateOnly`, Ende exklusiv).
6. Erwartbare Fehler werden als `Result`/`Result<T>` zurückgegeben, Exceptions nur bei Programmierfehlern.
7. Zeitabhängige Logik nutzt `IClock`, nie `DateTime.Now` direkt (Ausnahme: reine UI-Formatierung).
8. Kein Namespace darf `Microsoft` oder `Google` als Segment unter `LifeDashboard.*` heißen (verdeckt `Microsoft.Extensions.*`). Deshalb heißen die Ordner `MicrosoftGraph`/`GoogleCalendar`.

## Sync-Regeln (nie aufweichen, siehe docs/SYNC_DESIGN.md)
- **Nur in eigene Kalender schreiben.** Schreibzugriff ausschließlich über `CalendarWriteGate.Open(...)`. Provider müssen in `GetWriter` für `SharedReadOnly` eine `ReadOnlyCalendarException` werfen.
- Jede Spiegelung trägt einen `OriginMarker`. Termine mit Marker sind nie Quelle (Schleifenschutz).
- Idempotenz über das Ledger (`OriginKey` + `TargetCalendarId`) und den Inhalts-Hash.
- Gelöscht wird nur, was das Ledger als app-erzeugt kennt, standardmäßig erst nach Bestätigung (`ConfirmDeletes`).
- Dry-Run ist der Default und schreibt nichts (auch nicht ins Ledger, außer der Lauf-Historie).
- Datenschutz-Default: privat ↔ Arbeit nur „Beschäftigt“ ohne Details.
- Jede Änderung an der SyncEngine braucht einen Test in `tests/LifeDashboard.Core.Tests/Sync/`.

## Persistenz
- Schemaänderungen **nur** als neue Migration hinten an `DatabaseSchema.Migrations` anhängen, bestehende nie ändern. Eine SQL-Anweisung pro Listeneintrag.
- Zeitpunkte als UTC-Ticks (`INTEGER`), Enums als Text speichern.
- Neue Felder in `UserSettings` brauchen immer einen Default (alte gespeicherte JSON-Stände müssen ladbar bleiben).
- Jede `ISyncLedger`-Implementierung muss `SyncLedgerContractTests` bestehen.

## Sicherheit
- **Keine Secrets im Repo.** Client-IDs und Keys gehören in `src/LifeDashboard.App/appsettings.Local.json` (gitignored). `appsettings.Example.json` ist die Vorlage.
- Tokens nur über `ISecureTokenStore` (SecureStorage), nie loggen, nie in SQLite oder Preferences.
- API-Keys in Headern senden, nicht in URLs (z. B. Finnhub `X-Finnhub-Token`).
- Kein Scraping, nur offizielle APIs.

## Konventionen
- **Conventional Commits**, ein Commit pro logischem Schritt: `feat(core): …`, `fix(app): …`, `test: …`, `docs: …`, `ci: …`, `chore: …`.
- C#: file-scoped namespaces, `sealed` als Default, Records für Domain-Daten, primäre Konstruktoren für DI.
- Tests: xUnit, FluentAssertions **7.x** (v8 ist kommerziell lizenziert, nicht upgraden!), NSubstitute. Testnamen beschreiben Verhalten (`Syncing_twice_creates_no_duplicates`).
- Lizenzen und Sicherheitslücken prüfen, bevor neue Pakete dazukommen (z. B. kein EPPlus; `dotnet list package --vulnerable --include-transitive`).
- Dateien nie mit Windows PowerShell 5 `Get-Content`/`Set-Content` umschreiben: Das zerstört Umlaute (UTF-8 ohne BOM wird als ANSI gelesen). Edit-Werkzeug oder `sed` verwenden.
- UI-Texte auf Deutsch. Styles nur über `Resources/Styles` (keine Farben inline), hell und dunkel per `AppThemeBinding`.
- Neue Seiten: View + ViewModel in `MauiProgram.AddPages` registrieren und in `AppShell.xaml` eintragen. iPhone verträgt maximal 5 Tabs.

## Wo was liegt
- Interfaces: `src/LifeDashboard.Core/Abstractions/`
- Sync-Logik: `src/LifeDashboard.Core/Sync/`
- In-Memory-Fakes (Tests/Demo): `src/LifeDashboard.Core/InMemory/`
- DI-Registrierung: `src/LifeDashboard.Infrastructure/ServiceCollectionExtensions.cs`, `src/LifeDashboard.App/MauiProgram.cs`
- Roadmap und Phasenstatus: `docs/ROADMAP.md`
