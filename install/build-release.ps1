<#
.SYNOPSIS
    Builds RevitGeoExporter in Release configuration and copies output to install/dist/.
.DESCRIPTION
    Run this script from the repo root or from the install/ folder.
    The resulting dist/ folder contains everything needed for installation.
#>
param(
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolve paths relative to this script's location
$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot   = Split-Path -Parent $scriptDir
$solutionDir = Join-Path $repoRoot "src"
$projectDir  = Join-Path $solutionDir "RevitGeoExporter"
$projectFile = Join-Path $projectDir "RevitGeoExporter.csproj"
$distDir     = Join-Path $scriptDir "dist"

if (-not (Test-Path $projectFile)) {
    Write-Error "Cannot find project file at $projectFile. Run this script from the repo."
    exit 1
}

# Clean dist folder
if (Test-Path $distDir) {
    Remove-Item -Recurse -Force $distDir
}
New-Item -ItemType Directory -Path $distDir | Out-Null

Write-Host "Building $Configuration configuration..." -ForegroundColor Cyan
dotnet build $projectFile -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

$binDir = Join-Path $projectDir "bin\$Configuration\net48"

if (-not (Test-Path $binDir)) {
    Write-Error "Build output not found at $binDir"
    exit 1
}

# Files to copy (flat)
$files = @(
    "RevitGeoExporter.dll",
    "RevitGeoExporter.Core.dll",
    "Microsoft.Data.Sqlite.dll",
    "NetTopologySuite.dll",
    "Newtonsoft.Json.dll",
    "ProjNET.dll",
    "SQLitePCLRaw.batteries_v2.dll",
    "SQLitePCLRaw.core.dll",
    "SQLitePCLRaw.provider.dynamic_cdecl.dll",
    "System.Buffers.dll",
    "System.Memory.dll",
    "System.Numerics.Vectors.dll",
    "System.Runtime.CompilerServices.Unsafe.dll"
)

$missing = @()
foreach ($f in $files) {
    $src = Join-Path $binDir $f
    if (Test-Path $src) {
        Copy-Item $src $distDir
    } else {
        $missing += $f
    }
}

if ($missing.Count -gt 0) {
    Write-Warning "The following expected files were not found in build output:"
    $missing | ForEach-Object { Write-Warning "  - $_" }
}

# Copy runtimes folder (native SQLite binaries)
$runtimesSrc = Join-Path $binDir "runtimes"
if (Test-Path $runtimesSrc) {
    Copy-Item -Recurse $runtimesSrc (Join-Path $distDir "runtimes")
} else {
    Write-Warning "runtimes/ folder not found in build output. Native SQLite binaries will be missing."
}

$count = (Get-ChildItem $distDir -File).Count
Write-Host ""
Write-Host "Build complete. $count files copied to:" -ForegroundColor Green
Write-Host "  $distDir" -ForegroundColor Green
Write-Host ""
Write-Host "Next step options:" -ForegroundColor Yellow
Write-Host "  1) End-user installer EXE: run build-installer.ps1" -ForegroundColor Yellow
Write-Host "  2) Direct admin install:  run install.ps1" -ForegroundColor Yellow
