# Erste Schritte (Windows)

So bringst du Life Dashboard mit deinen echten Konten auf deinen Windows-PC. Dauer: ca. 30–45 Minuten, das meiste
davon für die einmaligen App-Registrierungen bei Microsoft, Google und Strava.

## 1. Repo aktuell holen
Im Repo-Ordner (z. B. `C:\Users\jonah\source\repos\Jonnschki\Hanoj-Dashboard`):
```bash
git checkout main
```
```bash
git pull origin main
```

## 2. Registrierungen anlegen (einmalig)
Ausführlich mit Screens-Beschreibung in [SETUP.md](SETUP.md), Abschnitt 3:

| Dienst | Wo | Was du am Ende brauchst |
|---|---|---|
| Microsoft 365 | entra.microsoft.com → App-Registrierungen | Anwendungs-ID + Verzeichnis-ID; Redirect `http://localhost`; Admin-Zustimmung erteilen |
| Google | console.cloud.google.com → Calendar API + OAuth-Client „Desktop-App“ | Client-ID + Clientschlüssel |
| Strava | strava.com/settings/api | Client ID + Client Secret; Callback-Domain `localhost` |
| Finnhub (optional) | finnhub.io | API-Key (nur als Fallback für Börsendaten) |

Börsendaten (Yahoo), Schlagzeilen und ICS-Abos brauchen **keine** Registrierung.

## 3. Zugangsdaten eintragen
```bash
copy src\LifeDashboard.App\appsettings.Example.json src\LifeDashboard.App\appsettings.Local.json
```
`appsettings.Local.json` im Editor öffnen und die Werte aus Schritt 2 eintragen. Die Datei wird **nie** eingecheckt (`.gitignore`).
`"UseDemoData": false` steht schon drin.

## 4. Installieren
```bash
powershell -ExecutionPolicy Bypass -File scripts\install-windows.ps1
```
Optional mit `-DesktopShortcut`. Das Skript baut eine eigenständige Version (≈ 310 MB, .NET muss danach nicht installiert
sein), installiert sie nach `%LOCALAPPDATA%\Programs\LifeDashboard` und legt „Life Dashboard“ im Startmenü an.
**Nach jeder Änderung an `appsettings.Local.json` oder einem Update einfach erneut ausführen**, deine Daten bleiben erhalten.

## 5. Einrichten in der App
1. **Einstellungen → Konten → Verbinden** für Microsoft, Google und Strava. Der Browser öffnet sich; nach der Anmeldung
   zurück zur App. Unter „Kalender für die Spiegelung“ erscheinen danach deine Kalender.
2. **Kalender auswählen**, die gegenseitig gespiegelt werden sollen (z. B. Outlook „Kalender“ ↔ Google „Jonah“).
   Fremde/geteilte Kalender sind gesperrt (read-only).
3. **Sport-Zielkalender** wählen (Tipp: in Google einen eigenen Kalender „Sport“ anlegen).
4. **Datenschutz-Regel** privat ↔ Arbeit prüfen (Standard: nur „Beschäftigt“ ohne Details).
5. Optional: **ICS-Abos** von Kunden hinzufügen und die **Watchlist** anpassen (Yahoo-Symbole, z. B. `^ATX, ^GDAXI, VOE.VI`).
6. **Einstellungen speichern**.

## 6. Erster Sync (wichtig: erst Vorschau!)
1. **Sync → „Dry-Run“ an → Sync starten.** Die App zeigt nur, was sie tun würde, und schreibt nichts.
2. Liste „Änderungen“ prüfen. Passt alles?
3. **„Dry-Run“ aus → Sync starten.** Jetzt wird geschrieben. Gelöscht wird nur, wenn „Löschungen ausführen“ an ist,
   und nur Termine, die die App selbst angelegt hat.

Ab dann: abends **„Aktualisieren“** im Dashboard, danach steht alles im Kalender.

## 7. Täglicher Digest
Unter **Einstellungen → Daily Digest** die Uhrzeit setzen. Läuft die App zu dieser Zeit (oder öffnest du sie danach),
kommt einmal pro Tag eine Windows-Benachrichtigung mit Terminen, Märkten und der wichtigsten Schlagzeile.

## Deinstallieren
```bash
powershell -ExecutionPolicy Bypass -File scripts\uninstall-windows.ps1
```
Mit `-RemoveData` werden auch Einstellungen, Ledger, Anmeldungen und ICS-Links gelöscht.

## Wenn etwas nicht klappt
| Meldung / Problem | Lösung |
|---|---|
| „Client-ID fehlt“ | `appsettings.Local.json` prüfen, dann Installationsskript erneut ausführen |
| Google: „Zugriff blockiert: App wurde nicht überprüft“ | Dich als Testnutzer eintragen bzw. „Erweitert → Weiter“ |
| Google trennt sich nach 7 Tagen | OAuth-Zustimmungsbildschirm von *Testing* auf *In Produktion* stellen |
| Microsoft: „Administratorgenehmigung erforderlich“ | In Entra bei API-Berechtigungen „Administratorzustimmung erteilen“ |
| Build bricht mit `MSB3030` ab | Repo liegt zu tief verschachtelt → in einen kurzen Pfad klonen (z. B. `C:\src\Hanoj-Dashboard`) |
| Keine Benachrichtigung | Windows-Einstellungen → System → Benachrichtigungen → „Life Dashboard“ erlauben |
| Börsenkurse fehlen | Yahoo ist inoffiziell und kann ausfallen; Finnhub-Key als Fallback eintragen |
