<#
.SYNOPSIS
    Baut Life Dashboard fuer Windows und installiert es fuer den aktuellen Benutzer (ohne Admin-Rechte).

.DESCRIPTION
    - Erstellt eine eigenstaendige Version (self-contained: .NET und Windows App SDK sind enthalten).
    - Installiert nach %LOCALAPPDATA%\Programs\LifeDashboard (ersetzt eine vorhandene Installation).
    - Legt eine Verknuepfung im Startmenue an (optional auch auf dem Desktop).
    - Deine Daten (Einstellungen, Ledger, Anmeldungen) liegen getrennt unter %LOCALAPPDATA%\Hanoj
      und bleiben bei einer Neuinstallation erhalten.

    Vorher: src\LifeDashboard.App\appsettings.Local.json anlegen (siehe docs\SETUP.md), sonst startet die App im Demo-Modus.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\install-windows.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\install-windows.ps1 -DesktopShortcut
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\LifeDashboard'),
    [switch]$DesktopShortcut,
    [switch]$NoShortcuts
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\LifeDashboard.App\LifeDashboard.App.csproj'
$localSettings = Join-Path $repo 'src\LifeDashboard.App\appsettings.Local.json'

Write-Host '== Life Dashboard: Windows-Installation ==' -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Das .NET SDK wurde nicht gefunden. Bitte .NET 10 SDK installieren (siehe docs\SETUP.md).'
}

if (-not (Test-Path $localSettings)) {
    Write-Warning 'appsettings.Local.json fehlt: Die App startet im Demo-Modus (Beispielkalender, keine echten Konten).'
    Write-Warning 'Fuer echte Konten: appsettings.Example.json kopieren, Werte eintragen, Skript erneut ausfuehren.'
}

if ($repo.Length -gt 70) {
    Write-Warning "Der Repo-Pfad ist lang ($($repo.Length) Zeichen). Falls der Build mit MSB3030 abbricht: Repo in einen kurzen Pfad legen (z. B. C:\src\Hanoj-Dashboard)."
}

# Laufende Instanz beenden, sonst sind Dateien gesperrt.
Get-Process -Name 'LifeDashboard.App' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$publishDir = Join-Path ([System.IO.Path]::GetTempPath()) 'LifeDashboard-publish'
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

Write-Host 'Baue Release-Version (das dauert beim ersten Mal ein paar Minuten) ...'
& dotnet publish $project `
    -c Release `
    -f net10.0-windows10.0.19041.0 `
    -r win-x64 `
    --self-contained true `
    -p:WindowsAppSDKSelfContained=true `
    -p:WindowsPackageType=None `
    -o $publishDir `
    -nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    throw "Build fehlgeschlagen (Exit-Code $LASTEXITCODE). Details: dotnet publish ohne '-v quiet' erneut ausfuehren."
}

$exeName = 'LifeDashboard.App.exe'
if (-not (Test-Path (Join-Path $publishDir $exeName))) {
    throw "Build-Ergebnis unvollstaendig: $exeName fehlt."
}

Write-Host "Installiere nach $InstallDir ..."
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $publishDir '*') $InstallDir
Remove-Item -Recurse -Force $publishDir

$exe = Join-Path $InstallDir $exeName

function New-Shortcut([string]$Path) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $InstallDir
    $link.Description = 'Life Dashboard'
    $link.IconLocation = "$exe,0"
    $link.Save()
}

if (-not $NoShortcuts) {
    $startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Life Dashboard.lnk'
    New-Shortcut $startMenu
    Write-Host "Startmenue-Verknuepfung: $startMenu"
    if ($DesktopShortcut) {
        $desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Life Dashboard.lnk'
        New-Shortcut $desktop
        Write-Host "Desktop-Verknuepfung: $desktop"
    }
}

$sizeMb = [math]::Round(((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 0)
Write-Host ''
Write-Host "Fertig ($sizeMb MB). Starten: Startmenue -> 'Life Dashboard'" -ForegroundColor Green
Write-Host "Oder direkt: $exe"
