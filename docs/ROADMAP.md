# Roadmap

| Phase | Inhalt | Status |
|---|---|---|
| 0 | **Skelett**: Solution, Struktur, DI, Navigation, Platzhalter-UI, Tests-Gerüst, CI, Doku | ✅ erledigt |
| 1 | **Persistenz & Ledger**: `SqliteSyncLedger` (sqlite-net-pcl), Migrationen, Einstellungen speichern (Kalenderauswahl, Regeln, Sport-Zielkalender, Watchlist, Digest-Uhrzeit), Sync-Run-Historie | ✅ erledigt |
| 2a | **Fremde Kalender per ICS-Abo** (read-only): Parser (Serien, Zeitzonen, Ganztags), Provider, Verwaltung in den Einstellungen, Link nur im sicheren Speicher | ✅ erledigt |
| 2c | **Börse + News**: Yahoo Finance mit Finnhub-Fallback, Schlagzeilen aus offiziellen RSS-Feeds (AT/DE/EU/international) mit Relevanz-Ranking | ✅ erledigt |
| 2b | **Microsoft-Auth + Outlook** lesen/schreiben: MSAL (System-Browser, Token-Cache in SecureStorage), Graph per REST, Marker als Extended Property | ✅ Code + Tests; echter Login wartet auf deine App-Registrierung |
| 3 | **Google-Auth + Google Calendar** lesen/schreiben (OAuth Code + PKCE über 127.0.0.1, `extendedProperties.private`) | ✅ Code + Tests; echter Login wartet auf deine App-Registrierung |
| 4 | **Sync-Engine produktiv**: Graph/Google-Writer mit Marker ✅; offen: Bestätigungsdialog für Löschungen direkt aus dem Report, Einzelabfrage vor Löschung | 🟡 |
| 5 | **Strava-Import → Kalender**: OAuth, Paging, Token-Refresh, Rate-Limit-Handling | ✅ Code + Tests; echter Login wartet auf deine App-Registrierung |
| 6 | **Export**: Termine (Excel/CSV), Sport (12 Monate), Sync-Protokoll | ✅ erledigt |
| 7 | **Daily Digest**: Windows-Benachrichtigung zur eingestellten Uhrzeit (solange die App läuft bzw. beim ersten Start danach), mit Schlagzeilen | ✅ Windows · iOS offen |
| 8 | **Windows-Release**: eigenständiges Paket, Installations-/Deinstallationsskript, Startmenü | ✅ erledigt |
| 9 | **iPhone**: TestFlight-Pipeline (GitHub Actions auf macOS) oder lokaler Mac-Build; MSAL-/OAuth-Callbacks für iOS, `UNUserNotificationCenter` | ⏳ wartet auf Entscheidung Mac/Apple-Developer-Account |

## Offene Punkte

- **Erster echter Login** bei Microsoft/Google/Strava ist noch nicht gegen die echten Dienste geprüft (braucht deine App-Registrierungen). Alle Abläufe sind mit simulierten Servern getestet.
- Löschungen aus dem Dry-Run-Report einzeln bestätigen (aktuell: Schalter „Löschungen ausführen“ für den ganzen Lauf).
- Täglicher Digest ohne laufende App (z. B. per Windows-Aufgabenplanung) ist bewusst nicht umgesetzt (Vorgabe: kein Hintergrunddienst).
- Konfliktstrategie und Sync-Fenster stehen weiterhin in `appsettings.json` (bewusst nicht in der UI).
