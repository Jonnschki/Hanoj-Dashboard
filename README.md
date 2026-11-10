# Hanoj-Dashboard · Life Dashboard

.NET MAUI App von Jonah für **Windows** und **iPhone**: Ein Knopfdruck auf **„Aktualisieren“** zieht Termine aus
Outlook und Google, spiegelt sie zwischen den eigenen Kalendern, holt Strava-Aktivitäten in den Kalender und
zeigt einen Tages-Digest mit Märkten. Danach muss man nur noch in den Kalender schauen.

> **Status: Phase 1 (Persistenz) abgeschlossen.** Die App läuft im Demo-Modus mit Beispieldaten. Sync-Engine, SQLite-Ledger
> und gespeicherte Einstellungen sind fertig und getestet; die Anbindungen an Microsoft, Google und Strava folgen laut
> [Roadmap](docs/ROADMAP.md).

## Schnellstart (Windows)
```bash
dotnet build LifeDashboard.slnx
dotnet test LifeDashboard.slnx
dotnet build src/LifeDashboard.App -f net10.0-windows10.0.19041.0 -t:Run
```
Voraussetzungen und iOS-Setup: [docs/SETUP.md](docs/SETUP.md).

## Features (Ziel)
| # | Feature | Stand |
|---|---|---|
| F1 | Kalender-Sync Outlook ↔ Google (Spiegelung zwischen eigenen Kalendern) | Engine ✅, Anbieter ⏳ |
| F2 | Kalender von Kollegen/Vorgesetzten **read-only** einblenden | Schutz ✅, Graph ⏳ |
| F3 | Strava → Kalender | Engine + Mapper ✅, API ⏳ |
| F4 | On-Demand-Refresh mit Fortschritt und Protokoll | ✅ (Demo) |
| F5 | Export Excel/CSV | Services ✅, UI teilweise |
| F6 | Daily Digest (Termine + Märkte, Benachrichtigung) | Builder ✅, Notification ⏳ |
| F7 | Dashboard mit Kacheln | ✅ (Demo) |

## Dokumentation
- [Architektur](docs/ARCHITECTURE.md)
- [Setup (Windows, iOS/Mac, OAuth-Registrierungen)](docs/SETUP.md)
- [Sync-Design (Regeln, Ledger, Ablauf)](docs/SYNC_DESIGN.md)
- [Roadmap](docs/ROADMAP.md)
- [CLAUDE.md](CLAUDE.md): Regeln für die Arbeit mit Claude Code

## Sicherheit
Es gibt keine Secrets im Repo. Eigene Werte gehören in `src/LifeDashboard.App/appsettings.Local.json` (gitignored),
Tokens nur in SecureStorage. Fremde Kalender sind technisch read-only.
