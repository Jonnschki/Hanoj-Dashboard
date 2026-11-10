# Setup

## 1. Windows (Entwicklung und Start)

**Voraussetzungen**
- .NET SDK **10.0.4xx** (siehe `global.json`)
- MAUI-Workload für Windows: `dotnet workload install maui-windows`
  (bei installiertem Visual Studio 2026 mit der Workload „.NET Multi-platform App UI“ bereits vorhanden)
- Windows 10 1809+ / Windows 11

**Bauen und starten**
```bash
dotnet build LifeDashboard.slnx
dotnet test LifeDashboard.slnx
dotnet build src/LifeDashboard.App -f net10.0-windows10.0.19041.0 -t:Run
```
Die App läuft *unpackaged* (ohne MSIX) und startet im **Demo-Modus** mit Beispielkalendern. Konten sind dafür nicht nötig.

> **Pfadlänge:** Der Windows App SDK erzeugt sehr lange Pfade unter `obj/`. Liegt das Repo tief verschachtelt
> (z. B. in einem Git-Worktree), kann der Release-Build mit `MSB3030 … konnte nicht kopiert werden` abbrechen,
> weil 260 Zeichen überschritten werden. Abhilfe: Repo in einen kurzen Pfad legen (z. B. `C:\src\Hanoj-Dashboard`)
> oder in Windows lange Pfade aktivieren (Gruppenrichtlinie „Win32-Pfade mit langer Länge aktivieren“, Admin nötig).

## 2. Geheimnisse und Konfiguration

| Datei | Eingecheckt? | Inhalt |
|---|---|---|
| `src/LifeDashboard.App/appsettings.json` | ja | Defaults **ohne** Secrets, `UseDemoData: true` |
| `src/LifeDashboard.App/appsettings.Example.json` | ja | Vorlage mit Platzhaltern |
| `src/LifeDashboard.App/appsettings.Local.json` | **nein** (`.gitignore`) | deine echten Client-IDs/Keys |

```bash
copy src\LifeDashboard.App\appsettings.Example.json src\LifeDashboard.App\appsettings.Local.json
```
Werte eintragen und neu bauen. Die Datei wird beim Build als Ressource eingebettet.

**Wichtig:**
- Eine Mobile-/Desktop-App kann keine echten Geheimnisse schützen: alles, was eingebettet ist, lässt sich aus dem
  Binary auslesen. Deshalb nutzen Microsoft und Google **öffentliche Clients mit PKCE** (kein Secret nötig).
  Strava verlangt zwar ein Client-Secret, für eine rein private App ist das vertretbar. Die App bitte nicht öffentlich verteilen.
- **Tokens** landen ausschließlich in `SecureStorage` (iOS Keychain / Windows Credential Locker), nie in Dateien,
  Preferences oder Logs.

## 3. OAuth-App-Registrierungen

### 3.1 Microsoft (Outlook / Microsoft 365)
1. https://entra.microsoft.com → **App-Registrierungen** → **Neue Registrierung**.
   Ohne eigenen Entra-Tenant lässt sich einer mit einem kostenlosen Azure-Konto anlegen.
2. Kontotypen: **„Konten in einem beliebigen Organisationsverzeichnis und persönliche Microsoft-Konten“**.
3. **Authentifizierung** → Plattform **„Mobile- und Desktopanwendungen“**:
   - Windows: `http://localhost`
   - iOS: `msauth.at.hanoj.lifedashboard://auth`
   - **„Öffentliche Clientflows zulassen“** = Ja
4. **API-Berechtigungen** (Microsoft Graph, *delegiert*): `User.Read`, `Calendars.ReadWrite`, `Calendars.Read.Shared`, `offline_access`.
5. Die **Anwendungs-ID (Client-ID)** kommt in `appsettings.Local.json` → `Microsoft:ClientId`.

#### Kalender von Kollegen und Vorgesetzten: Strategie und Fallbacks
Dein Arbeitgeber-Tenant entscheidet, ob du einer fremden App zustimmen darfst. Seit 2020 erlauben viele Tenants
Benutzer-Zustimmung nur noch für Apps **verifizierter Herausgeber**, oder sie blockieren Drittanbieter-Apps ganz.

| Option | Was du brauchst | Liefert | Bewertung |
|---|---|---|---|
| **A. `Calendars.Read.Shared`** (Standard) | Benutzer-Zustimmung im Tenant erlaubt; Kollegen haben dir ihren Kalender **freigegeben** (Outlook → Freigeben) | Termine (je nach Freigabe-Stufe: Details oder nur Frei/Gebucht) | am besten, wenn erlaubt |
| B. Delegate-Zugriff | Kollege macht dich zum Stellvertreter | wie A, mehr Rechte als nötig | **nicht** empfohlen |
| **C. `getSchedule` (Frei/Gebucht)** | nur `Calendars.ReadWrite`/`Calendars.Read`; keine Einzelfreigabe | Verfügbarkeitsblöcke laut Organisations-Standard | guter Fallback |
| **D. ICS-Abo** | Kollege veröffentlicht den Kalender (Outlook im Web → Einstellungen → Freigegebene Kalender → Kalender veröffentlichen); Tenant muss das erlauben | Termine oder Frei/Gebucht, read-only | Fallback ohne jede App-Zustimmung (`IcsCalendarProvider`) |
| E. Admin-Zustimmung | IT erteilt Zustimmung im Tenant (ggf. eigene Registrierung im Firmen-Tenant) | wie A | sauberste Lösung, falls die IT mitspielt |

Erkennst du beim ersten Login die Meldung *„Administratorgenehmigung erforderlich“*, bleiben **C** (nur mit
Zustimmung) bzw. **D** oder **E**. Alle fremden Kalender sind in der App unabhängig von der Variante technisch read-only.

### 3.2 Google Calendar
1. https://console.cloud.google.com → neues Projekt → **APIs & Dienste** → **Google Calendar API** aktivieren.
2. **OAuth-Zustimmungsbildschirm**: Typ *Extern*, dich selbst als **Testnutzer** eintragen, Scope
   `https://www.googleapis.com/auth/calendar`.
   - Achtung: Im Status *Testing* laufen Refresh-Tokens **nach 7 Tagen** ab. Für den Dauerbetrieb den Status auf
     *In Produktion* setzen. Ohne Verifizierung zeigt Google dann einen Warnhinweis, für die private Nutzung ist das ok.
3. **Anmeldedaten** → OAuth-Client-ID:
   - **iOS**: Bundle-ID `at.hanoj.lifedashboard`
   - **Desktop-App** (für Windows, Loopback-Redirect `http://127.0.0.1:<port>`)
4. Client-ID(s) kommen in `appsettings.Local.json` → `Google`.

### 3.3 Strava
1. https://www.strava.com/settings/api → App anlegen.
2. **Authorization Callback Domain**: `localhost`. Redirect der App: `lifedashboard://localhost/strava`.
3. `Client ID` und `Client Secret` kommen in `appsettings.Local.json` → `Strava`. Scope: `activity:read_all`.
4. Rate-Limits (Standard): 100 Requests / 15 min, 1000 / Tag. Der Resilience-Handler wartet bei HTTP 429.

### 3.4 Finnhub (Marktdaten)
1. https://finnhub.io → kostenlos registrieren → API-Key kopieren.
2. In `appsettings.Local.json`: `MarketData:Provider = "Finnhub"`, `MarketData:ApiKey = "…"`.
3. Free-Tier: ca. 60 Requests/Minute, vor allem US-Werte. Indizes daher über ETFs (`SPY` ≈ S&P 500, `QQQ` ≈ Nasdaq 100).
   Der Key wird im Header `X-Finnhub-Token` gesendet, nicht in der URL.

## 4. iOS (iPhone 15) – was du mit dem Mac einrichten musst

Ein **Mac ist zwingend**, weil Apple-Builds und die Signierung nur dort laufen.

### 4.1 Mac vorbereiten
1. Aktuelles macOS, **Xcode** aus dem App Store (die Version, die der .NET-10-iOS-Workload verlangt; aktuell Xcode 26.x).
   Xcode einmal starten und die zusätzlichen Komponenten installieren.
2. `xcode-select --install` (Command Line Tools).
3. .NET 10 SDK installieren, dann `sudo dotnet workload install ios maui-ios`.
4. Xcode → *Settings* → *Accounts*: deine **Apple-ID** hinzufügen.

### 4.2 Apple-Konto und Provisioning
| Variante | Kosten | Grenzen |
|---|---|---|
| Kostenlose Apple-ID („Personal Team“) | 0 € | Profil läuft nach **7 Tagen** ab (neu installieren), keine TestFlight-Verteilung, einige Entitlements fehlen |
| **Apple Developer Program** | 99 $/Jahr | 1 Jahr gültig, TestFlight, alle Entitlements (für Keychain-Sharing bei MSAL empfehlenswert) |

Automatisches Provisioning: In Xcode ein leeres iOS-Projekt mit der Bundle-ID `at.hanoj.lifedashboard` anlegen und
einmal auf dem iPhone starten. Xcode erzeugt dabei das Provisioning-Profil, das .NET anschließend mitbenutzt.

### 4.3 iPhone koppeln
1. iPhone per USB-C am Mac anschließen und **„Diesem Computer vertrauen“** bestätigen.
2. iPhone: *Einstellungen → Datenschutz & Sicherheit → **Entwicklermodus*** aktivieren (Neustart).
3. Xcode → *Window → Devices and Simulators*: Das Gerät muss als „Connected“ erscheinen.
4. Beim ersten Start einer App mit kostenloser Apple-ID: *Einstellungen → Allgemein → VPN & Geräteverwaltung* → dem Entwickler vertrauen.

### 4.4 Bauen
**Direkt auf dem Mac (empfohlen):** VS Code mit *C# Dev Kit* und *.NET MAUI*-Extension, oder die Kommandozeile:
```bash
dotnet build src/LifeDashboard.App -f net10.0-ios -t:Run -p:RuntimeIdentifier=ios-arm64
```
Auf dem Mac ist iOS automatisch aktiv.

**Von Windows aus (Pair-to-Mac in Visual Studio):** Mac und PC im selben Netz, auf dem Mac *Entfernte Anmeldung* (SSH)
aktivieren, in Visual Studio *Tools → iOS → Pair to Mac*. iOS-Target unter Windows einschalten:
```bash
dotnet build src/LifeDashboard.App -p:EnableIos=true
```
oder dauerhaft die Umgebungsvariable `LIFEDASHBOARD_ENABLE_IOS=true` setzen.
Ohne diesen Schalter baut Windows **nur** das Windows-Target, so funktioniert alles auch ohne Mac.

### 4.5 Für später (Phase 2/3), iOS-spezifisch
- MSAL: URL-Schema `msauth.at.hanoj.lifedashboard` in `Info.plist` (`CFBundleURLTypes`),
  `LSApplicationQueriesSchemes` = `msauthv2`, `msauthv3`, Keychain-Gruppe `com.microsoft.adalcache` in `Entitlements.plist`.
- Google/Strava: URL-Schema `lifedashboard` registrieren (Callback über `WebAuthenticator`).
- Safe Areas: Shell und `ContentPage` berücksichtigen Notch/Dynamic Island automatisch. Für randlose Layouts
  `SafeAreaEdges` gezielt setzen.

## 5. CI

`.github/workflows/ci.yml` läuft auf einem Windows-Runner: Restore, Build (inkl. App für Windows) und Tests.
Ein iOS-Build (Simulator, unsigniert) lässt sich manuell per *Run workflow → ios = true* starten (macOS-Runner).
