# Contributing

Thanks for looking. This is an unofficial project built with the developer's permission, and
one that may end up being adopted upstream, so it is written to be handed over: no game files
modified, no compile-time coupling to the game assembly, and everything documented in place.

## Building

You need the .NET SDK. Visual Studio is not required - the .NET Framework 4.7.2 targeting pack
comes from NuGet.

```bash
git clone https://github.com/dox121-pixel/RekindlingModLoader
cd RekindlingModLoader
dotnet build -c Release
```

**You do not need to own the game to build this.** When no install is found, the build falls
back to the MonoGame NuGet package for compile-time references. The output still binds to the
game's own copy at runtime.

If your install is somewhere other than the default:

```bash
dotnet build -c Release -p:RekindlingDir="D:\Games\Rekindling"
```

## Testing

```bash
dotnet build tests/Rekindling.ModLoader.Tests/Rekindling.ModLoader.Tests.csproj
.\tests\Rekindling.ModLoader.Tests\bin\Debug\Rekindling.ModLoader.Tests.exe
```

The runner is a plain console app rather than a test framework - the loader ships as two
assemblies plus Harmony, and a framework would be more dependency than these checks are worth.

Some checks compare generated XNB output byte-for-byte against the game's own content files.
Those skip themselves when no install is present, so a passing run on a machine without the
game is not proof the encoder is correct. Run them against a real install before touching
`XnbEncoder`.

Anything that cannot be checked without launching the game - drawing, input, hooks firing -
has to be verified by hand. Say so in the PR rather than implying otherwise.

## House rules

These come from things that have already gone wrong here:

- **Every hook is applied individually and defensively.** If a game update renames a method,
  that one hook logs a warning and everything else keeps working. Never let a missing target
  take down the loader and every mod with it.
- **A broken mod must never take the game down.** Load failures, event-handler exceptions and
  asset errors are caught, logged, and rolled back where possible. The game still starts.
- **No compile-time reference to the game assembly from the loader.** `ZTD` types are resolved
  at runtime through `AccessTools`, so a game update does not require recompiling. Mods opt
  into that coupling themselves; the loader does not.
- **Never modify a game file.** Everything is additive - the loader's own files, `Mods/` and
  `Logs/`. That is what keeps Steam's file validation clean and survives game updates.
- **The loader is x86.** `Rekindling.exe` carries the `32BIT_REQUIRED` flag, so an AnyCPU
  build starts 64-bit and dies with `BadImageFormatException`.
- **Very small methods may not be patchable.** The JIT inlines trivial method bodies in Release
  builds, and an inlined method never runs the patched version. If a hook silently does nothing
  in Release but works in Debug, this is usually why.
- **Comment the surprising parts, not the obvious ones.** Most of the awkward code here exists
  because of something specific about the game; write down which thing, so the next person
  does not "simplify" it back into a bug.

## Third-party content

Do not commit game assets, or artwork you do not have the right to redistribute. The loader
repository contains no game-derived files and should stay that way.

## Reporting bugs

Include `Logs/modloader.log`, ideally from a run with `--debug`. Check first whether the
problem still happens with no mods installed - that single step usually identifies whether it
belongs here or with a mod author.
