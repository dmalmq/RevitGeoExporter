<#
.SYNOPSIS
    Installs the RevitGeoExporter add-in for the selected Revit year (all users).
.DESCRIPTION
    Copies the add-in DLLs and manifest to the system-wide Revit add-ins folder
    for the selected Revit year.
    Must be run as Administrator.

    The script looks for build output in this order:
      1. install/dist/ folder (from build-release.ps1)
      2. src/RevitGeoExporter/bin/Release/net48/ (direct build output)
#>
param(
    [string]$RevitYear = "2024"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-AddinManifestContents {
    param(
        [string]$TemplatePath,
        [string]$TargetRevitYear
    )

    if (-not (Test-Path $TemplatePath)) {
        throw "Cannot find .addin template at $TemplatePath."
    }

    return (Get-Content -Path $TemplatePath -Raw).Replace("__REVIT_YEAR__", $TargetRevitYear)
}

# --- Check for Administrator privileges ---
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator." -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as administrator', then re-run this script." -ForegroundColor Yellow
    exit 1
}

# --- Resolve paths ---
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir

$addinsRoot  = "C:\ProgramData\Autodesk\Revit\Addins\$RevitYear"
$installDir  = Join-Path $addinsRoot "RevitGeoExporter"
$addinTemplate = Join-Path $scriptDir "RevitGeoExporter.addin.template"
$addinFile = Join-Path $addinsRoot "RevitGeoExporter.addin"

# --- Find build output ---
$distDir = Join-Path $scriptDir "dist"
$binDir  = Join-Path $repoRoot "src\RevitGeoExporter\bin\Release\net48"

if (Test-Path (Join-Path $distDir "RevitGeoExporter.dll")) {
    $sourceDir = $distDir
    Write-Host "Using pre-built dist/ folder for Revit $RevitYear." -ForegroundColor Cyan
} elseif (Test-Path (Join-Path $binDir "RevitGeoExporter.dll")) {
    $sourceDir = $binDir
    Write-Host "Using build output from bin/Release/net48/ for Revit $RevitYear." -ForegroundColor Cyan
} else {
    Write-Host "ERROR: No build output found." -ForegroundColor Red
    Write-Host "Run build-release.ps1 -RevitYear $RevitYear first, or build the solution in Release configuration." -ForegroundColor Yellow
    exit 1
}

# --- Validate .addin template ---
if (-not (Test-Path $addinTemplate)) {
    Write-Error "Cannot find .addin template at $addinTemplate"
    exit 1
}

# --- Files to install ---
$files = @(
    "RevitGeoExporter.dll",
    "RevitGeoExporter.Core.dll",
    "RevitGeoExporter.pdb",
    "RevitGeoExporter.Core.pdb",
    "Microsoft.Data.Sqlite.dll",
    "NetTopologySuite.dll",
    "NetTopologySuite.Features.dll",
    "NetTopologySuite.IO.ShapeFile.dll",
    "Newtonsoft.Json.dll",
    "ProjNET.dll",
    "SQLitePCLRaw.batteries_v2.dll",
    "SQLitePCLRaw.core.dll",
    "SQLitePCLRaw.provider.dynamic_cdecl.dll",
    "System.Buffers.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll",
    "System.Text.Encoding.CodePages.dll"
)

# --- Create install directory ---
if (-not (Test-Path $addinsRoot)) {
    New-Item -ItemType Directory -Path $addinsRoot -Force | Out-Null
}
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# --- Copy DLLs ---
Write-Host "Copying files to $installDir ..." -ForegroundColor Cyan
$copied = 0
foreach ($f in $files) {
    $src = Join-Path $sourceDir $f
    if (Test-Path $src) {
        Copy-Item $src $installDir -Force
        $copied++
    } else {
        Write-Warning "File not found, skipping: $f"
    }
}

# --- Copy runtimes (native SQLite) ---
$runtimesSrc  = Join-Path $sourceDir "runtimes"
$runtimesDest = Join-Path $installDir "runtimes"
if (Test-Path $runtimesSrc) {
    if (Test-Path $runtimesDest) {
        Remove-Item -Recurse -Force $runtimesDest
    }
    Copy-Item -Recurse $runtimesSrc $runtimesDest
    Write-Host "Copied runtimes/ folder (native SQLite binaries)." -ForegroundColor Cyan
} else {
    Write-Warning "runtimes/ folder not found in source. Native SQLite binaries will be missing."
}

# --- Copy satellite resource folders (for localized UI strings) ---
$resourceFolders = Get-ChildItem -Path $sourceDir -Directory | Where-Object {
    $_.Name -ne 'runtimes' -and
    (Get-ChildItem -Path $_.FullName -Filter '*.resources.dll' -File -ErrorAction SilentlyContinue | Select-Object -First 1)
}
foreach ($folder in $resourceFolders) {
    $destination = Join-Path $installDir $folder.Name
    if (Test-Path $destination) {
        Remove-Item -Recurse -Force $destination
    }
    Copy-Item -Recurse $folder.FullName $destination
    Write-Host "Copied resource folder: $($folder.Name)" -ForegroundColor Cyan
}

# --- Generate .addin manifest ---
Set-Content -Path $addinFile -Value (Get-AddinManifestContents -TemplatePath $addinTemplate -TargetRevitYear $RevitYear) -Encoding UTF8
Write-Host "Generated .addin manifest for Revit $RevitYear at $addinFile" -ForegroundColor Cyan

# --- Done ---
Write-Host ""
Write-Host "Installation complete! ($copied files installed)" -ForegroundColor Green
Write-Host "  Add-in folder: $installDir" -ForegroundColor Green
Write-Host "  Manifest:      $addinFile" -ForegroundColor Green
Write-Host ""
Write-Host "Please restart Revit $RevitYear to load the add-in." -ForegroundColor Yellow
