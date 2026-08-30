# Rekindling Mod Loader

[![CI](../../actions/workflows/ci.yml/badge.svg)](../../actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

An **unofficial** mod loader for [Rekindling](https://store.steampowered.com/app/661500/), built
with the developer's permission.

It loads mods into the game without modifying a single game file. Nothing is renamed, patched on
disk, or replaced, so Steam's *Verify integrity of game files* stays clean and a game update
cannot break your install.

> This is not affiliated with or supported by the developer. Report problems here, not to them.
> If you are having trouble with a specific mod, report it to that mod's author.

---

## Status

Working and tested against Rekindling 1.0.0.0 (MonoGame, .NET Framework 4.7.2).

Verified end to end on a real launch: mods discovered and loaded, Harmony patches applied,
lifecycle events firing, PNG asset replacement converted and accepted by MonoGame at runtime, and
a clean shutdown. See [What has been verified](#what-has-been-verified).

---

## Installing

1. Build (see [Building](#building)) or download a release.
2. Copy these files into your Rekindling folder, next to `Rekindling.exe`:

   ```
   Rekindling.ModLoader.exe
   Rekindling.ModLoader.exe.config
   Rekindling.ModLoader.API.dll
   0Harmony.dll
   ```

3. Launch **`Rekindling.ModLoader.exe`** instead of `Rekindling.exe`.

To launch it from Steam, set the game's launch options to:

```
cmd /c start "" "Rekindling.ModLoader.exe"
```

Steam still sees the game as running, and Steamworks initialises normally because the game ships a
`steam_appid.txt`.

**Uninstalling:** delete the four files above and the `Mods` folder. Nothing else changed.

---

## Installing mods

Drop each mod into its own folder under `Mods/`:

```
Rekindling/
├─ Rekindling.exe
├─ Rekindling.ModLoader.exe
└─ Mods/
   ├─ BetterFarming/
   │  ├─ mod.json
   │  ├─ BetterFarming.dll
   │  └─ assets/
   └─ NewTextures/
      ├─ mod.json
      └─ assets/
```

To disable a mod without deleting it, rename its folder to start with `_` or `.`.

### Co-op is disabled while mods are loaded

The loader turns multiplayer off by default. This is not caution for its own sake: the game
synchronises simulation state between clients, so any mod that touches that state desyncs every
player not running an identical mod set. A silently-broken lobby is a worse outcome than a
clearly-disabled button.

Join Game is greyed out and unclickable, the Solo/Co-op toggle is dimmed and inert, and Steam
invites are ignored. Pass `--allow-multiplayer` to the loader to override it.

Logs go to `Logs/modloader.log`, with the previous run kept as `Logs/modloader.previous.log`.

| Flag | Effect |
| --- | --- |
| `--debug` / `--trace` | More detailed logging. |
| `--allow-multiplayer` | Re-enables co-op. Mods will desync online play. |

---

## Writing a mod

A mod is a .NET Framework 4.7.2 class library plus a `mod.json`.

### mod.json

```json
{
  "id": "yourname.yourmod",
  "name": "Your Mod",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "What it does.",

  "assembly": "YourMod.dll",
  "entry": "YourMod.ModEntry",

  "assets": "assets",

  "requires":   { "someone.corelib": "1.2.0" },
  "loadAfter":  [ "another.mod" ],
  "loadBefore": [ "yet.another.mod" ],

  "minLoaderVersion": "0.1.0"
}
```

Only `id` is required. `//` comments and trailing commas are tolerated, and a malformed manifest
reports the offending line and column instead of failing silently.

| Field | Meaning |
| --- | --- |
| `id` | Unique, stable, lowercase. Convention is `author.modname`. Changing it breaks dependents. |
| `assembly` | Your DLL. Omit it for a content-only mod that just replaces assets. |
| `entry` | Optional. Omit and the loader finds the single `IMod` implementation itself. |
| `assets` | Folder of loose asset overrides, registered automatically. Defaults to `assets` if present. |
| `poster` | Image shown next to the mod in the in-game mod list. Defaults to `poster.png` if present. |
| `requires` | Hard dependency. A missing or too-old one disables the mod with an explanation. |
| `loadAfter` / `loadBefore` | Soft ordering hints. Missing targets are ignored. |
| `minLoaderVersion` | Refuses to load on an older loader. |

### Entry point

```csharp
using Rekindling.ModLoader;

public sealed class ModEntry : ModBase
{
    protected override void OnLoad()
    {
        Log.Info("Hello from my mod");

        ModEvents.GameStateChanged += (s, e) => Log.Info($"State: {e}");

        Context.Harmony.PatchAll(typeof(ModEntry).Assembly);
    }

    public override void OnGameReady()
    {
        // Graphics device and content pipeline exist from here on.
    }
}
```

`ModBase` gives you `Log`, `Manifest`, `ModDirectory`, `Context` and `PathIn(...)`.

### Lifecycle

| Stage | What exists |
| --- | --- |
| `OnLoad` | Nothing game-side yet. Patch, read config, subscribe to events here. |
| `OnGameReady` | After `Game1.Initialize`. Graphics device and content are live. |
| `OnUnload` | During shutdown. Flush anything you own. |

### Events

```csharp
ModEvents.GameReady        += ...;  // after Game1.Initialize
ModEvents.UpdateStarted    += ...;  // top of every Game1.Update
ModEvents.UpdateEnded      += ...;  // end of every Game1.Update
ModEvents.DrawEnded        += ...;  // end of every Game1.Draw
ModEvents.GameStateChanged += ...;  // menu / game / science / pause / ...
ModEvents.ShuttingDown     += ...;  // during Game1.UnloadContent
```

Handlers run on the game's main thread. An exception in one is caught and logged rather than
crashing the game, but a slow handler still stalls the frame — keep per-frame work cheap.

### Replacing art and sound

Put files under your `assets` folder, mirroring the game's `Content` layout minus the extension:

```
assets/
├─ Hud/RekinIcon.png
└─ Awards/cheers.png
```

That replaces `Content/Hud/RekinIcon.xnb` and `Content/Awards/cheers.xnb`.

**Loose `.png` works — you do not need the XNA content pipeline.** The loader converts PNG (and
JPG/BMP) to the XNB format MonoGame expects, in memory, with alpha premultiplied the same way the
official pipeline does. `.xnb` files are passed through untouched. Conversions are cached, so a
texture that streams in repeatedly is only converted once.

Asset names are matched case-insensitively, and `/`, `//` and `\` are all equivalent — the game
itself mixes all three.

You can also register overrides in code, which is what you want when the replacement is chosen at
runtime:

```csharp
Context.Assets.Override("Hud/RekinIcon", PathIn("art", "my-icon.png"));
```

If two mods claim the same asset, the later one in load order wins and the loader logs a warning
naming both.

### Patching game code

Reference `Rekindling.exe` from your csproj to get at the `ZTD` namespace, then use
[Harmony](https://harmony.pardeike.net/):

```csharp
[HarmonyPatch(typeof(ZTD.Game1), "Initialize")]
internal static class MyPatch
{
    private static void Postfix() { /* ... */ }
}
```

`Context.Harmony` is pre-created with your mod id, so `Context.Harmony.PatchAll(...)` is enough —
and if your mod throws while loading, the loader removes whatever it already applied rather than
leaving orphaned patches live.

### Cross-mod integration

```csharp
if (Context.IsModLoaded("someone.corelib"))
{
    var other = Context.GetMod("someone.corelib");
}
```

`GetMod` returns `null` when the mod is absent, so optional integrations degrade gracefully.

### Reading the mod list

`ModRegistry` exposes everything the loader discovered, for mods that want to display or reason
about the load order:

```csharp
foreach (IModInfo mod in ModRegistry.All)
    Log.Info($"{mod.Name} {mod.Version} - {(mod.IsLoaded ? "ok" : mod.FailureReason)}");
```

`ModRegistry.All` deliberately includes mods that **failed**, along with the reason. Anyone opening
a mod list is usually trying to work out why something is not working, and a list that hides the
broken entries answers the wrong question. Use `ModRegistry.Loaded` when you only want the
working ones.

It is populated once loading finishes, so read it from `OnGameReady` onwards rather than `OnLoad`.

---

## Building

Needs the .NET SDK. The .NET Framework 4.7.2 targeting pack comes from NuGet, so Visual Studio is
not required - and **you do not need to own the game to build the loader**. With no install
present it compiles against the MonoGame NuGet package instead; the output still binds to the
game's own copy at runtime.

```bash
git clone https://github.com/dox121-pixel/RekindlingModLoader
cd RekindlingModLoader
dotnet build -c Release
```

If Rekindling is not at `C:\SteamLibrary\steamapps\common\Rekindling`:

```bash
dotnet build -c Release -p:RekindlingDir="D:\Games\Rekindling"
```

Deploy straight into the game folder:

```powershell
.\deploy.ps1 -GameDir "D:\Games\Rekindling"
```

Deploying fails fast if the game is running: a running game holds its assemblies open, so the
copy would silently leave you testing the previous build.

Run the tests:

```bash
dotnet build tests/Rekindling.ModLoader.Tests/Rekindling.ModLoader.Tests.csproj
./tests/Rekindling.ModLoader.Tests/bin/Debug/Rekindling.ModLoader.Tests.exe
```

### Mods must be x86 or AnyCPU

`Rekindling.exe` is an IL-only assembly with the `32BIT_REQUIRED` flag set, and so is
`Steamworks.NET.dll`. The game therefore only runs in a 32-bit process. The loader is built `x86`
to match. A mod targeting `x64` will not load.

---

## How it works

```
Rekindling.ModLoader.exe  (x86, [STAThread])
  │
  ├─ set working directory to the game folder   (content paths are relative)
  ├─ install an AssemblyResolve handler         (default probing never looks for .exe files)
  ├─ Assembly.LoadFrom("Rekindling.exe")        (loads the ZTD types; does not run Main)
  ├─ apply the loader's own Harmony patches
  ├─ discover Mods/, resolve dependencies, load each mod
  └─ invoke ZTD.Program.Main() reflectively
```

Design decisions worth knowing:

- **Launcher, not a patcher.** Nothing on disk is modified, so Steam validation and game updates
  are both non-events.
- **No compile-time reference to the game.** The loader finds `ZTD` types through
  `AccessTools.TypeByName` at runtime, so a game update does not require recompiling it. Mods opt
  into that coupling themselves by referencing `Rekindling.exe`.
- **Every hook is applied individually and defensively.** If a game update renames one method,
  that hook logs a warning and everything else keeps working.
- **A broken mod never takes the game down.** Load failures, event-handler exceptions and asset
  errors are all caught, logged, and rolled back where possible; the game still starts.
- **Assets hook `ContentManager.OpenStream`,** not `Load<T>`. That is the single choke point every
  content load passes through, and it is non-generic, so one patch covers everything. It also
  handles the game using two content managers with different root directories.

### Adopting this upstream

If the loader is ever made official, no launcher is needed. One call in the game's own `Main` does
the whole job:

```csharp
[STAThread]
static void Main()
{
    Rekindling.ModLoader.ModLoaderHost.Initialize(AppDomain.CurrentDomain.BaseDirectory);

    using Game1 game = new Game1();
    game.Run();
}
```

`ModLoaderHost.Initialize` is idempotent and never throws — worst case it logs and the game starts
unmodded.

---

## What has been verified

Automated (55 checks, `tests/`):

- Asset-name normalisation across both of the game's content managers and all three separator styles.
- Manifest parsing, including comments, trailing commas, escapes, and line-accurate error reporting.
- Version parsing and comparison, including pre-release suffixes and unparseable input.
- Dependency resolution: ordering, missing and too-old requirements, cycles, transitive orphaning,
  `loadBefore` inversion, and `minLoaderVersion`.
- XNB encoding, including **byte-for-byte identity with the game's own `Content/Utility/1Part.xnb`**
  and a byte-identical header against `Content/Hud/RekinIcon.xnb`, plus alpha premultiplication and
  RGBA channel order.

On a real game launch:

- Three mods discovered and loaded together, and their Harmony patches applied.
- All six lifecycle hooks attached.
- `GameStateChanged` fired correctly through `menu → loading → menu`.
- 15,134 frames rendered across the menu, the Options screen and the Mods screen, with no errors.
- A loose PNG converted and accepted by MonoGame's `Texture2DReader` without error.
- `MenuOverhaul` verified by screenshot: logo repositioned, button column rebuilt, background
  crossfade caught mid-transition, and the Mods screen listing all three mods with posters.
- Discord and Patreon relocated to the bottom-left above the version number, with their URLs read
  out of the game's own IL rather than copied, and both confirmed working by hand.
- Clean shutdown with `OnUnload` called.

Not yet verified:

- Multiplayer. The game has a substantial netcode layer (`NetworkManager`, `BaseNetMessage` and
  friends); mods that change simulation state will desync unless every player runs the same set.
  The loader does nothing about this yet.
- Save compatibility. Saves use `BinaryFormatter` with `ISerializable`, so mod-added data needs a
  side-car file rather than new fields on game types. There is no API for this yet.
- Long sessions and heavy mod counts.

---

## Roadmap

- Side-car save data API, so mods can persist state without touching `BinaryFormatter` types.
- An in-game mod list showing what loaded and what failed.
- Steam Workshop distribution for code mods. The game's existing Workshop support uses the legacy
  `SteamRemoteStorage` API and only handles scenarios (`.ztdsc`).
- A mod-compatibility handshake for multiplayer.

---

## Licence

MIT. Rekindling itself is not covered by this licence — only the loader in this repository.
