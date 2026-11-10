# Roadmap

| Phase | Inhalt | Status |
|---|---|---|
| 0 | **Skelett**: Solution, Struktur, DI, Navigation, Platzhalter-UI, Tests-Gerüst, CI, Doku | ✅ erledigt |
| 1 | **Persistenz & Ledger**: `SqliteSyncLedger` (sqlite-net-pcl), Migrationen, Einstellungen speichern (Kalenderauswahl, Regeln, Watchlist, Digest-Uhrzeit), Sync-Run-Historie | ⏳ |
| 2 | **Microsoft-Auth + Outlook lesen**: MSAL (System-Browser, Token-Cache in SecureStorage), eigene Kalender via Graph; danach geteilte Kalender read-only (`Calendars.Read.Shared`), Fallbacks `getSchedule` (Frei/Gebucht) und ICS-Abo | ⏳ |
| 3 | **Google-Auth + Google Calendar** lesen/schreiben (OAuth Code + PKCE, `extendedProperties.private`) | ⏳ |
| 4 | **Sync-Engine produktiv**: Graph/Google-Writer mit Marker, Dry-Run-Bestätigungsdialog für Löschungen, Regeln-UI speichern, Einzelabfrage vor Löschung | ⏳ |
| 5 | **Strava-Import → Kalender**: OAuth, Paging, Token-Refresh, Rate-Limit-Handling | ⏳ |
| 6 | **Export**: UI für Zeitraum/Typ (Termine, Sport, Sync-Log). Die Services sind bereits fertig. | ⏳ |
| 7 | **Daily Digest**: lokale Notifications (iOS `UNUserNotificationCenter`, Windows App Notifications), Finnhub produktiv, optional Wetter | ⏳ |
| 8 | **Polishing**: Fehlerbehandlung, Performance, Release-Builds, Serilog-File-Sink, ggf. TestFlight | ⏳ |

## Offene Punkte aus Phase 0

- Die `LocalNotificationService` loggt nur (Phase 7).
- Die Einstellungsseite speichert noch nichts dauerhaft (Phase 1).
- Der iOS-Build ist konfiguriert, aber noch nicht auf einem Mac/Gerät verifiziert (siehe `SETUP.md`).
