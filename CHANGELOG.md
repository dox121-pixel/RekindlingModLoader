# Changelog

All notable changes to the mod loader. Versions follow [semantic versioning](https://semver.org/);
while the major version is 0, the mod API may still change between minor releases.

## [Unreleased]

### Added
- Co-op is disabled by default while mods are loaded, because the game synchronises simulation
  state between clients and any mod touching it desyncs players not running an identical mod
  set. Join Game is greyed out and unclickable, the Solo/Co-op toggle is dimmed and inert, and
  Steam invites are ignored. Override with `--allow-multiplayer`.
- `ModRegistry` / `IModInfo`, so mods can enumerate what is installed - including mods that
  failed, and why.
- `poster` field in `mod.json`, for an image representing the mod in an in-game list.
- The loader can now be built without owning the game: with no install present it compiles
  against the MonoGame NuGet package instead, which is what makes CI and outside contributions
  possible.
- Continuous integration, issue and pull request templates, and contribution guidelines.

### Changed
- `deploy.ps1` refuses to run while the game is open. A running game holds its mod assemblies
  locked, so the copy silently did nothing and you ended up testing the previous build.
- Sample mods moved to a separate repository. This one contains the loader only.

## [0.1.0]

Initial release.

### Added
- Loads mods from `Mods/` without modifying a single game file, so Steam's file validation
  stays clean and game updates cannot break the install.
- `IMod` / `ModBase` entry points with `OnLoad`, `OnGameReady` and `OnUnload`.
- Lifecycle events: `GameReady`, `UpdateStarted`, `UpdateEnded`, `DrawEnded`,
  `GameStateChanged`, `ShuttingDown`. Handler exceptions are caught and logged rather than
  crashing the game.
- Asset replacement by hooking `ContentManager.OpenStream`, the single choke point every
  content load passes through. Loose `.png`, `.jpg` and `.bmp` files are converted to XNB in
  memory, so mod authors never need the XNA content pipeline.
- Manifest parsing with `//` comments, trailing commas, and errors that report a line and
  column.
- Dependency resolution: hard requirements with version floors, soft `loadAfter` / `loadBefore`
  ordering, duplicate-id handling, cycle detection and transitive disabling.
- Per-mod Harmony instances, automatically unpatched if a mod throws while loading.
- Per-mod logging to `Logs/modloader.log`, with the previous run kept alongside.
