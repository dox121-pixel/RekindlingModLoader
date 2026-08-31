# Changelog

All notable changes to the mod loader. Versions follow [semantic versioning](https://semver.org/);
while the major version is 0, the mod API may still change between minor releases.

## [0.4.0] - 2026-08-31

### Added
- **Mod settings.** Mods declare options in `OnLoad` and the loader persists and restores them.
  Six kinds: `Toggle`, `Choice`, `Slider`, `Point` (a screen position stored as a fraction of the
  screen, so it survives a resolution change), `Text` (usually hidden, for state a mod keeps but
  presents its own way) and `Action` (a button, for anything a value control cannot express).
  `ModOption.Hidden` tells a settings UI to skip an option.
- Settings live in `ModConfig/<mod id>.json` in the game folder, **not** the mod's folder, so they
  survive updating or reinstalling a mod. Writes are deferred a moment after the last change,
  because dragging a control fires a change every frame.
- `ModOptionsRegistry` exposes every mod's options, so one mod can provide the settings UI for all
  of them. A mod does not have to draw anything to be configurable.

### Fixed
- A malformed options file falls back to defaults with a warning instead of failing the load, and
  a saved value that is no longer valid (a choice since removed) is ignored the same way.

## [0.3.0] - 2026-08-31

### Added
- **`WorldEvents`**, hooking the tile simulation where most of the game's logic actually runs:
  `TileUpdate` for every tile the world sweeps, `TickStarted` / `TickEnded` around each world
  tick, and `TileChanged` when a tile is replaced outright. Suggested by the game's developer as
  the injection point worth having.
- `TileUpdate` is on a hot path — measured at roughly twenty thousand calls a second at 3x game
  speed — so it costs a field read and a branch when unsubscribed, allocates nothing per call, and
  disables a handler that throws rather than letting it flood the log.

### Fixed
- `WorldTickContext.SpeedStep` (previously `UpdateFrame`) is documented for what it is: the step
  index within the current frame, counting up to the game speed. It is not a frame counter and
  does not grow over time.

## [0.2.0] - 2026-08-30

### Added
- **Co-op is disabled by default while mods are loaded.** The game synchronises simulation state
  between clients, so any mod touching it desyncs players not running an identical mod set. Join
  Game is greyed out and unclickable, the Solo/Co-op toggle is dimmed and inert, and Steam invites
  are ignored. Override with `--allow-multiplayer`.
- `ModRegistry` / `IModInfo`, so mods can enumerate what is installed — including mods that failed,
  and why.
- `poster` field in `mod.json`, for an image representing the mod in an in-game list.
- The loader can be built without owning the game: with no install present it compiles against the
  MonoGame NuGet package instead, which is what makes CI and outside contributions possible.
- Continuous integration, issue and pull request templates, and contribution guidelines.

### Changed
- `deploy.ps1` refuses to run while the game is open. A running game holds its mod assemblies
  locked, so the copy silently did nothing and you ended up testing the previous build.
- Sample mods moved to a separate repository. This one contains the loader only.

## [0.1.0] - 2026-08-30

Initial release.

### Added
- Loads mods from `Mods/` without modifying a single game file, so Steam's file validation stays
  clean and game updates cannot break the install.
- `IMod` / `ModBase` entry points with `OnLoad`, `OnGameReady` and `OnUnload`.
- Lifecycle events: `GameReady`, `UpdateStarted`, `UpdateEnded`, `DrawEnded`, `GameStateChanged`,
  `ShuttingDown`. Handler exceptions are caught and logged rather than crashing the game.
- Asset replacement by hooking `ContentManager.OpenStream`, the single choke point every content
  load passes through. Loose `.png`, `.jpg` and `.bmp` files are converted to XNB in memory, so
  mod authors never need the XNA content pipeline.
- Manifest parsing with `//` comments, trailing commas, and errors that report a line and column.
- Dependency resolution: hard requirements with version floors, soft `loadAfter` / `loadBefore`
  ordering, duplicate-id handling, cycle detection and transitive disabling.
- Per-mod Harmony instances, automatically unpatched if a mod throws while loading.
- Per-mod logging to `Logs/modloader.log`, with the previous run kept alongside.
