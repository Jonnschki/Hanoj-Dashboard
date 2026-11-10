<#
.SYNOPSIS
    Entfernt Life Dashboard (Programmordner und Verknuepfungen).

.DESCRIPTION
    Deine Daten (Einstellungen, Ledger, Anmeldungen) bleiben standardmaessig erhalten.
    Mit -RemoveData werden auch sie geloescht (inkl. gespeicherter Anmeldungen und ICS-Links).
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\LifeDashboard'),
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
Get-Process -Name 'LifeDashboard.App' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

foreach ($path in @(
        $InstallDir,
        (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Life Dashboard.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Life Dashboard.lnk'))) {
    if (Test-Path $path) {
        Remove-Item -Recurse -Force $path
        Write-Host "Entfernt: $path"
    }
}

if ($RemoveData) {
    $data = Join-Path $env:LOCALAPPDATA 'Hanoj\at.hanoj.lifedashboard'
    if (Test-Path $data) {
        Remove-Item -Recurse -Force $data
        Write-Host "Daten entfernt: $data"
    }
}

Write-Host 'Life Dashboard wurde entfernt.' -ForegroundColor Green
