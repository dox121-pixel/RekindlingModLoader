<#
.SYNOPSIS
    Builds the mod loader and copies it into a Rekindling install.

.DESCRIPTION
    Only ever ADDS files to the game folder. No game file is modified, renamed or deleted, so
    Steam's "verify integrity of game files" stays happy and a game update cannot break the
    install. Remove the loader by deleting the files listed at the end of a run.

.PARAMETER GameDir
    The Rekindling install folder.

.PARAMETER Configuration
    Debug or Release. Defaults to Release.

.EXAMPLE
    .\deploy.ps1
    .\deploy.ps1 -GameDir "D:\Games\Rekindling" -Configuration Debug
#>
[CmdletBinding()]
param(
    [string]$GameDir = "C:\SteamLibrary\steamapps\common\Rekindling",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot

if (-not (Test-Path (Join-Path $GameDir "Rekindling.exe"))) {
    throw "Rekindling.exe was not found in '$GameDir'. Pass -GameDir with your install path."
}

# A running game holds its assemblies open, so copies over them fail and you end up testing
# the previous build without realising it. Fail loudly instead.
$running = @(Get-Process -Name "Rekindling*" -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $ids = ($running | ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }) -join ", "
    throw "Rekindling is still running: $ids. Close it before deploying."
}

Write-Host "Building ($Configuration)..." -ForegroundColor Cyan

& dotnet build (Join-Path $repo "src\Rekindling.ModLoader\Rekindling.ModLoader.csproj") `
    -c $Configuration -v quiet --nologo -p:RekindlingDir="$GameDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$loaderOut = Join-Path $repo "src\Rekindling.ModLoader\bin\$Configuration"

# The loader's own files. Deliberately does NOT include MonoGame.Framework.dll or anything
# else the game already ships - we always bind to the game's copies.
$payload = @(
    "Rekindling.ModLoader.exe",
    "Rekindling.ModLoader.exe.config",
    "Rekindling.ModLoader.pdb",
    "Rekindling.ModLoader.API.dll",
    "Rekindling.ModLoader.API.pdb",
    "Rekindling.ModLoader.API.xml",
    "0Harmony.dll"
)

Write-Host "Deploying to $GameDir" -ForegroundColor Cyan
$copied = @()

foreach ($file in $payload) {
    $source = Join-Path $loaderOut $file
    if (-not (Test-Path $source)) {
        Write-Warning "  missing from build output: $file"
        continue
    }

    Copy-Item $source (Join-Path $GameDir $file) -Force
    $copied += $file
    Write-Host "  + $file"
}

# Mods/ is where users drop mod folders.
$modsDir = Join-Path $GameDir "Mods"
if (-not (Test-Path $modsDir)) {
    New-Item -ItemType Directory -Path $modsDir | Out-Null
    Write-Host "  + Mods\  (created)"
}

Write-Host ""
Write-Host "Done. Launch Rekindling.ModLoader.exe instead of Rekindling.exe." -ForegroundColor Green
Write-Host "To run it from Steam, set the game's launch options to:" -ForegroundColor Green
Write-Host '    cmd /c start "" "Rekindling.ModLoader.exe"' -ForegroundColor Gray
Write-Host ""
Write-Host "Logs are written to Logs\modloader.log." -ForegroundColor Gray
Write-Host "Uninstall by deleting these from the game folder:" -ForegroundColor Gray
Write-Host ("    " + ($copied -join ", ")) -ForegroundColor DarkGray
