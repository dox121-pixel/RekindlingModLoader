<#
.SYNOPSIS
    Builds a release archive of the mod loader, ready to attach to a GitHub release.

.DESCRIPTION
    Produces dist\RekindlingModLoader-v<version>.zip containing everything a player needs to
    drop into their Rekindling folder. Deliberately excludes MonoGame.Framework.dll and anything
    else the game already ships - the loader always binds to the game's own copies, and shipping
    duplicates would risk a version mismatch.

.PARAMETER Configuration
    Debug or Release. Defaults to Release.

.EXAMPLE
    .\package.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot

Write-Host "Building ($Configuration)..." -ForegroundColor Cyan

# No -p:RekindlingDir, so this uses whatever Directory.Build.props resolves: the game's own
# MonoGame assembly when an install is present, the NuGet package otherwise. Either produces a
# working artifact, because both carry the same MonoGame assembly identity.
& dotnet build (Join-Path $repo "src\Rekindling.ModLoader\Rekindling.ModLoader.csproj") `
    -c $Configuration -v quiet --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$out = Join-Path $repo "src\Rekindling.ModLoader\bin\$Configuration"

$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Join-Path $out "Rekindling.ModLoader.exe")).ProductVersion
if ($version -match '^(\d+\.\d+\.\d+)') { $version = $Matches[1] }

$dist = Join-Path $repo "dist"
$stage = Join-Path $dist "RekindlingModLoader-v$version"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# The .pdb files are included on purpose. They are small, and they turn the stack traces in
# modloader.log into ones with real line numbers, which makes early bug reports far more useful.
$payload = @(
    "Rekindling.ModLoader.exe",
    "Rekindling.ModLoader.exe.config",
    "Rekindling.ModLoader.pdb",
    "Rekindling.ModLoader.API.dll",
    "Rekindling.ModLoader.API.pdb",
    "Rekindling.ModLoader.API.xml",
    "0Harmony.dll"
)

foreach ($file in $payload) {
    $source = Join-Path $out $file
    if (-not (Test-Path $source)) { throw "Missing from build output: $file" }
    Copy-Item $source (Join-Path $stage $file) -Force
}

Copy-Item (Join-Path $repo "LICENSE") (Join-Path $stage "LICENSE") -Force

@"
Rekindling Mod Loader v$version
===============================

An unofficial mod loader for Rekindling, built with the developer's permission.
This is NOT affiliated with or supported by the developer - please report problems
to the issue tracker, not to them.


INSTALLING
----------
1. Copy every file from this archive into your Rekindling folder, next to
   Rekindling.exe. On Steam: right-click the game -> Manage -> Browse local files.
2. Launch Rekindling.ModLoader.exe instead of Rekindling.exe.

   To launch it from Steam instead, set the game's launch options to:
       cmd /c start "" "Rekindling.ModLoader.exe"

   Steam still sees the game as running, and Steam features work normally.

INSTALLING MODS
---------------
Each mod goes in its own folder under Mods\, which the loader creates on first run:

    Rekindling\
      Rekindling.ModLoader.exe
      Mods\
        SomeMod\
          mod.json
          SomeMod.dll

To disable a mod without deleting it, rename its folder to start with _ or .

NOTE ON CO-OP
-------------
Multiplayer is disabled while mods are loaded. This is deliberate: the game syncs
simulation state between clients, so any mod that changes that state desyncs players
who are not running an identical mod set. Run the loader with --allow-multiplayer to
override it, at your own risk.

UNINSTALLING
------------
Delete the files listed below from your game folder. No game file is ever modified,
so nothing else needs undoing and Steam's "verify integrity" stays clean.

$($payload -join "`r`n")

TROUBLESHOOTING
---------------
Logs are written to Logs\modloader.log. Run with --debug for more detail.
If something breaks, check whether it still happens with the Mods folder empty -
that tells you whether it is the loader or a specific mod.

Source, issues and documentation:
https://github.com/dox121-pixel/RekindlingModLoader
"@ | Set-Content (Join-Path $stage "README.txt") -Encoding UTF8

$zip = Join-Path $dist "RekindlingModLoader-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$size = [Math]::Round((Get-Item $zip).Length / 1KB, 1)

Write-Host ""
Write-Host "Packaged v$version" -ForegroundColor Green
Write-Host "  $zip"
Write-Host "  $size KB"
Write-Host "  SHA256: $hash"
Write-Host ""
Write-Host "Attach it to a release with:" -ForegroundColor Cyan
Write-Host "  gh release create v$version `"$zip`" --title `"v$version`" --notes-file dist\release-notes-v$version.md"
