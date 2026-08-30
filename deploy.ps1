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

.PARAMETER IncludeSample
    Also deploy the bundled ExampleMod into Mods/.

.EXAMPLE
    .\deploy.ps1
    .\deploy.ps1 -GameDir "D:\Games\Rekindling" -IncludeSample
#>
[CmdletBinding()]
param(
    [string]$GameDir = "C:\SteamLibrary\steamapps\common\Rekindling",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$IncludeSample
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot

if (-not (Test-Path (Join-Path $GameDir "Rekindling.exe"))) {
    throw "Rekindling.exe was not found in '$GameDir'. Pass -GameDir with your install path."
}

Write-Host "Building ($Configuration)..." -ForegroundColor Cyan

$projects = @(
    "src\Rekindling.ModLoader\Rekindling.ModLoader.csproj"
)
if ($IncludeSample) {
    $projects += "samples\ExampleMod\ExampleMod.csproj"
}

foreach ($project in $projects) {
    & dotnet build (Join-Path $repo $project) -c $Configuration -v quiet --nologo -p:RekindlingDir="$GameDir"
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $project" }
}

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

if ($IncludeSample) {
    $sampleOut = Join-Path $repo "samples\ExampleMod\bin\$Configuration"
    $target = Join-Path $modsDir "ExampleMod"

    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($file in @("ExampleMod.dll", "ExampleMod.pdb", "mod.json")) {
        $source = Join-Path $sampleOut $file
        if (Test-Path $source) {
            Copy-Item $source (Join-Path $target $file) -Force
            Write-Host "  + Mods\ExampleMod\$file"
        }
    }
}

Write-Host ""
Write-Host "Done. Launch Rekindling.ModLoader.exe instead of Rekindling.exe." -ForegroundColor Green
Write-Host "To run it from Steam, set the game's launch options to:" -ForegroundColor Green
Write-Host '    cmd /c start "" "Rekindling.ModLoader.exe"' -ForegroundColor Gray
Write-Host ""
Write-Host "Logs are written to Logs\modloader.log." -ForegroundColor Gray
Write-Host "Uninstall by deleting these from the game folder:" -ForegroundColor Gray
Write-Host ("    " + ($copied -join ", ")) -ForegroundColor DarkGray
