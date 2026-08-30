## What this changes

<!-- One or two sentences. -->

## Why

<!-- The problem being solved. Link an issue if there is one. -->

## How it was tested

<!--
Say what you actually ran. "Builds" is not testing.
- [ ] `dotnet build -c Release`
- [ ] `tests\Rekindling.ModLoader.Tests\bin\Release\Rekindling.ModLoader.Tests.exe`
- [ ] Launched the game and confirmed the behaviour in-game
-->

## Checklist

- [ ] Hooks into game code degrade gracefully if the target method changes (log and carry on, never throw)
- [ ] No game files are read from or written to outside `Mods/`, `Logs/` and the loader's own files
- [ ] Public API changes are documented in the README
- [ ] `CHANGELOG.md` updated for user-visible changes
