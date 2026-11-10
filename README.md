# Hanoj-Dashboard · Life Dashboard

.NET MAUI App von Jonah für **Windows** und **iPhone**: Ein Knopfdruck auf **„Aktualisieren“** zieht Termine aus
Outlook und Google, spiegelt sie zwischen den eigenen Kalendern, holt Strava-Aktivitäten in den Kalender und
zeigt einen Tages-Digest mit Märkten. Danach muss man nur noch in den Kalender schauen.

> **Status: Windows-Version einsatzbereit.** Outlook, Google, Strava, ICS-Abos, Börse, Schlagzeilen, Export und
> Windows-Benachrichtigungen sind fertig und getestet. Für echte Konten brauchst du einmalig die App-Registrierungen
> ([Erste Schritte](docs/ERSTE_SCHRITTE.md)); ohne sie läuft die App im Demo-Modus. Die iPhone-Version folgt (braucht Mac/TestFlight).

## Installieren (Windows)
👉 **[Erste Schritte](docs/ERSTE_SCHRITTE.md)**: Registrierungen, Zugangsdaten, Installation per Skript, erster Sync.
```bash
powershell -ExecutionPolicy Bypass -File scripts\install-windows.ps1
```

## Entwickeln (Windows)
```bash
dotnet build LifeDashboard.slnx
dotnet test LifeDashboard.slnx
dotnet build src/LifeDashboard.App -f net10.0-windows10.0.19041.0 -t:Run
```
Voraussetzungen und iOS-Setup: [docs/SETUP.md](docs/SETUP.md).

## Features (Ziel)
| # | Feature | Stand |
|---|---|---|
| F1 | Kalender-Sync Outlook ↔ Google (Spiegelung zwischen eigenen Kalendern) | ✅ (Login über Einstellungen → Konten) |
| F2 | Fremde Kalender (Kunden, Partner) **read-only** einblenden | ICS-Abo ✅ |
| F3 | Strava → Kalender | ✅ (Login über Einstellungen → Konten) |
| F4 | On-Demand-Refresh mit Fortschritt, Protokoll und Verlauf | ✅ |
| F5 | Export Excel/CSV (Termine, Sport, Sync-Protokoll) | ✅ |
| F6 | Daily Digest (Termine, Märkte, Schlagzeilen, Windows-Benachrichtigung) | ✅ Windows · iPhone ⏳ |
| F7 | Dashboard mit Kacheln | ✅ |
| F8 | Börse live (ATX, DAX, Euro Stoxx, US, Devisen; Yahoo + Finnhub-Fallback) | ✅ |
| F9 | Schlagzeilen aus Qualitätsmedien AT/DE/EU, nach Relevanz, alle 5 Min. aktualisiert | ✅ |

## Dokumentation
- [Architektur](docs/ARCHITECTURE.md)
- [Setup (Windows, iOS/Mac, OAuth-Registrierungen)](docs/SETUP.md)
- [Sync-Design (Regeln, Ledger, Ablauf)](docs/SYNC_DESIGN.md)
- [Roadmap](docs/ROADMAP.md)
- [CLAUDE.md](CLAUDE.md): Regeln für die Arbeit mit Claude Code

## Sicherheit
Es gibt keine Secrets im Repo. Eigene Werte gehören in `src/LifeDashboard.App/appsettings.Local.json` (gitignored),
Tokens nur in SecureStorage. Fremde Kalender sind technisch read-only.
