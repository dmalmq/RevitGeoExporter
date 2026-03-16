<#
.SYNOPSIS
    Uninstalls the RevitGeoExporter add-in for Revit 2024.
.DESCRIPTION
    Removes the add-in DLLs and manifest from the system-wide Revit add-ins folder.
    Must be run as Administrator.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- Check for Administrator privileges ---
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as administrator', then re-run this script." -ForegroundColor Yellow
    exit 1
}

$addinsRoot = "C:\ProgramData\Autodesk\Revit\Addins\2024"
$installDir = Join-Path $addinsRoot "RevitGeoExporter"
$addinFile  = Join-Path $addinsRoot "RevitGeoExporter.addin"

$removed = $false

# --- Remove add-in folder ---
if (Test-Path $installDir) {
    Remove-Item -Recurse -Force $installDir
    Write-Host "Removed: $installDir" -ForegroundColor Cyan
    $removed = $true
} else {
    Write-Host "Add-in folder not found: $installDir" -ForegroundColor Yellow
}

# --- Remove .addin manifest ---
if (Test-Path $addinFile) {
    Remove-Item -Force $addinFile
    Write-Host "Removed: $addinFile" -ForegroundColor Cyan
    $removed = $true
} else {
    Write-Host ".addin manifest not found: $addinFile" -ForegroundColor Yellow
}

# --- Done ---
Write-Host ""
if ($removed) {
    Write-Host "Uninstall complete." -ForegroundColor Green
    Write-Host "Please restart Revit 2024 to fully unload the add-in." -ForegroundColor Yellow
} else {
    Write-Host "Nothing to uninstall. RevitGeoExporter does not appear to be installed." -ForegroundColor Yellow
}
