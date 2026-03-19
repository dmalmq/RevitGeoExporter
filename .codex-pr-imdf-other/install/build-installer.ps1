<#
.SYNOPSIS
    Builds a distributable installer EXE for RevitGeoExporter.
.DESCRIPTION
    1) Builds Release output into install/dist (via build-release.ps1)
    2) Compiles install/RevitGeoExporter.iss with Inno Setup (ISCC.exe)

    Requires Inno Setup 6:
      https://jrsoftware.org/isinfo.php
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$IsccPath = "",
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-IsccPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path $ExplicitPath) {
            return (Resolve-Path $ExplicitPath).Path
        }
        throw "ISCC.exe not found at explicit path: $ExplicitPath"
    }

    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($fromPath -ne $null) {
        return $fromPath.Path
    }

    $candidates = @()
    if ($env:ProgramFiles -and $env:ProgramFiles.Trim().Length -gt 0) {
        $candidates += (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    }
    if (${env:ProgramFiles(x86)} -and ${env:ProgramFiles(x86)}.Trim().Length -gt 0) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    }
    if ($env:LOCALAPPDATA -and $env:LOCALAPPDATA.Trim().Length -gt 0) {
        $candidates += (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    }

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Resolve-VersionFromAssembly {
    param([string]$AssemblyPath)

    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $AssemblyPath))
    if (-not [string]::IsNullOrWhiteSpace($info.FileVersion)) {
        return $info.FileVersion.Trim()
    }

    return "1.0.0.0"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$distDir = Join-Path $scriptDir "dist"
$outputDir = Join-Path $scriptDir "output"
$issFile = Join-Path $scriptDir "RevitGeoExporter.iss"
$buildReleaseScript = Join-Path $scriptDir "build-release.ps1"

if (-not (Test-Path $issFile)) {
    throw "Installer script not found: $issFile"
}

if (-not $SkipBuild) {
    if (-not (Test-Path $buildReleaseScript)) {
        throw "build-release.ps1 not found at $buildReleaseScript"
    }

    Write-Host "Building add-in payload into install/dist ..." -ForegroundColor Cyan
    & $buildReleaseScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "build-release.ps1 failed with exit code $LASTEXITCODE."
    }
}

$payloadDll = Join-Path $distDir "RevitGeoExporter.dll"
if (-not (Test-Path $payloadDll)) {
    throw "Payload missing: $payloadDll. Run build-release.ps1 first or remove -SkipBuild."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Resolve-VersionFromAssembly -AssemblyPath $payloadDll
}

$safeVersion = $Version.Trim()
if ([string]::IsNullOrWhiteSpace($safeVersion)) {
    $safeVersion = "1.0.0.0"
}

$versionMarkerPath = Join-Path $distDir "version.txt"
Set-Content -Path $versionMarkerPath -Value $safeVersion -NoNewline -Encoding UTF8

$iscc = Resolve-IsccPath -ExplicitPath $IsccPath
if ($iscc -eq $null) {
    throw @"
ISCC.exe (Inno Setup Compiler) was not found.
Install Inno Setup 6, or pass -IsccPath "C:\Path\To\ISCC.exe".
"@
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Cyan
Write-Host "  Version: $safeVersion" -ForegroundColor DarkGray
Write-Host "  Marker:  $versionMarkerPath" -ForegroundColor DarkGray
Write-Host "  ISCC:    $iscc" -ForegroundColor DarkGray

& $iscc `
    "/DMyAppVersion=$safeVersion" `
    "/DDistDir=$distDir" `
    "/DOutputDir=$outputDir" `
    $issFile

if ($LASTEXITCODE -ne 0) {
    throw "Installer compilation failed with exit code $LASTEXITCODE."
}

$expectedPrefix = "RevitGeoExporter-Setup-$safeVersion"
$installer = Get-ChildItem -Path $outputDir -File |
    Where-Object { $_.BaseName -eq $expectedPrefix } |
    Select-Object -First 1

Write-Host ""
if ($installer -ne $null) {
    Write-Host "Installer created:" -ForegroundColor Green
    Write-Host "  $($installer.FullName)" -ForegroundColor Green
}
else {
    Write-Host "Installer build completed. Check output folder:" -ForegroundColor Yellow
    Write-Host "  $outputDir" -ForegroundColor Yellow
}
