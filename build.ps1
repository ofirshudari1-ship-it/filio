#!/usr/bin/env pwsh
# build.ps1 — Filio one-command build
# Usage: .\build.ps1            (builds installer)
#        .\build.ps1 -SkipTests (skip test suite)
#        .\build.ps1 -PublishOnly (dotnet publish only, no installer)

param(
    [switch]$SkipTests,
    [switch]$PublishOnly
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

# Read version from version.json
$version = (Get-Content "$Root\version.json" | ConvertFrom-Json).version
Write-Host "Building Filio v$version" -ForegroundColor Cyan

# 1. Tests
if (-not $SkipTests) {
    Write-Host "`n[1/4] Running tests..." -ForegroundColor Yellow
    & dotnet test "$Root\app\Filio.sln" --configuration Release --no-build 2>&1
    if ($LASTEXITCODE -ne 0) {
        # Build first, then test
        & dotnet build "$Root\app\Filio.sln" --configuration Release
        & dotnet test "$Root\app\Filio.sln" --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "Tests failed!" }
    }
}

# 2. Publish
Write-Host "`n[2/4] Publishing..." -ForegroundColor Yellow
& dotnet publish "$Root\app\Filio.App\Filio.App.csproj" `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$version `
    -p:FileVersion=$version `
    -p:AssemblyVersion=$version `
    --output "$Root\build\publish"
if ($LASTEXITCODE -ne 0) { throw "Publish failed!" }

if ($PublishOnly) {
    Write-Host "`nDone (publish only). Output: $Root\build\publish" -ForegroundColor Green
    exit 0
}

# 3. Build installer — custom WinForms wizard (build\installer\Setup.csproj), not Inno Setup.
# See CHANGELOG.md 2.9.0 for why the installer engine changed: Inno Setup's wizard pages always
# render with native Windows button/control chrome no matter how the bitmaps are branded, which
# no longer meets the bar for this portfolio. Setup.csproj's own EnsurePublishOutput/
# BuildPayloadZip targets zip $Root\build\publish into payload.zip and embed it, so this dotnet
# build call is the entire installer build - no external tool (Inno Setup / ISCC.exe) required.
Write-Host "`n[3/4] Building installer..." -ForegroundColor Yellow
& dotnet build "$Root\build\installer\Setup.csproj" `
    --configuration Release `
    -p:Version=$version `
    -p:FileVersion=$version `
    -p:AssemblyVersion=$version
if ($LASTEXITCODE -ne 0) { throw "Installer build failed!" }

$builtSetupExe = "$Root\build\installer\bin\Release\net48\Filio-Setup.exe"
if (-not (Test-Path $builtSetupExe)) { throw "Installer build reported success but $builtSetupExe is missing!" }

# 4. Copy to root, and remove any older-versioned installer left behind in root from a
# previous build so there is never more than one installer file at the project root
# (STANDARDS.md: one installer file in root, no stale duplicates).
Write-Host "`n[4/4] Copying installer to root..." -ForegroundColor Yellow
$installerName = "Filio-Setup-$version.exe"
Get-ChildItem "$Root\Filio-Setup-*.exe" -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item $builtSetupExe "$Root\$installerName" -Force

Write-Host "`nBuild complete! $installerName is ready." -ForegroundColor Green
Write-Host "Smoke-testing the installer (headless, no admin/UAC needed): $installerName --selftest" -ForegroundColor Yellow
& "$Root\$installerName" --selftest
if ($LASTEXITCODE -ne 0) { throw "Installer self-test failed!" }
