using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace Rekindling.ModLoader.Tests
{
    /// <summary>
    /// Plain console test runner. Deliberately dependency-free: the loader ships as two
    /// assemblies plus Harmony, and pulling a test framework into the repo for a handful of
    /// pure-logic checks would be more setup than the checks are worth.
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static readonly List<string> Failures = new List<string>();

        private static int Main(string[] args)
        {
            string gameDirectory = args.Length > 0
                ? args[0]
                : @"C:\SteamLibrary\steamapps\common\Rekindling";

            Console.WriteLine("Rekindling Mod Loader - test run");
            Console.WriteLine(new string('-', 60));

            RunNormalizationTests();
            RunJsonTests();
            RunVersionTests();
            RunDependencyTests();
            RunXnbTests(gameDirectory);

            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"{_passed} passed, {Failures.Count} failed.");

            foreach (string failure in Failures)
                Console.WriteLine("  FAILED: " + failure);

            return Failures.Count == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------- asset names

        private static void RunNormalizationTests()
        {
            Section("Asset name normalisation");

            // The two content managers must land on the same key: Game1.Content has
            // RootDirectory "Content", while GlobalContent has "" and prepends "Content/".
            AreEqual("hud/rekinicon", AssetOverrideRegistry.Normalize("Content/Hud//RekinIcon"),
                "GlobalContent style path");
            AreEqual("hud/rekinicon", AssetOverrideRegistry.Normalize("Content/Hud/RekinIcon"),
                "Game1.Content style path");
            AreEqual("hud/rekinicon", AssetOverrideRegistry.Normalize(@"Content\Hud\RekinIcon"),
                "backslash separators");
            AreEqual("hud/rekinicon", AssetOverrideRegistry.Normalize("hud/rekinicon.xnb"),
                "explicit .xnb suffix");
            AreEqual("world objects/unknown", AssetOverrideRegistry.Normalize("World Objects//Unknown"),
                "spaces in folder names");
            AreEqual("sound/music/newday", AssetOverrideRegistry.Normalize("./Sound/Music/NewDay"),
                "leading ./");
            AreEqual(string.Empty, AssetOverrideRegistry.Normalize(null), "null is empty");
            AreEqual(string.Empty, AssetOverrideRegistry.Normalize("   "), "whitespace is empty");

            // A folder genuinely called "Content" inside Content must survive one strip only.
            AreEqual("content/thing", AssetOverrideRegistry.Normalize("Content/Content/Thing"),
                "only the first Content prefix is stripped");
        }

        // -------------------------------------------------------------------- json

        private static void RunJsonTests()
        {
            Section("Manifest parsing");

            JsonValue json = JsonValue.Parse(@"{
                // a comment authors will write anyway
                ""id"": ""dox.example"",
                ""version"": ""1.2.0"",
                ""loadAfter"": [ ""a.mod"", ""b.mod"" ],
                ""requires"": { ""core.lib"": ""2.0"" },
                ""nested"": { ""value"": 42, ""flag"": true, ""nothing"": null },
                ""escaped"": ""line\nbreak \u0041"",
                ""trailing"": [ 1, 2, 3, ],
            }");

            AreEqual("dox.example", json["id"].AsString(), "string field");
            AreEqual("dox.example", json["ID"].AsString(), "field lookup is case-insensitive");
            AreEqual(2, json["loadAfter"].AsStringList().Count, "array field");
            AreEqual("2.0", json["requires"]["core.lib"].AsString(), "nested object");
            AreEqual(42d, json["nested"]["value"].AsNumber(), "number");
            IsTrue(json["nested"]["flag"].AsBool(), "boolean");
            IsTrue(json["nested"]["nothing"].IsNull, "null");
            AreEqual("line\nbreak A", json["escaped"].AsString(), "escape sequences");
            AreEqual(3, json["trailing"].AsArray.Count, "trailing comma tolerated");
            IsTrue(json["missing"].IsNull, "missing field is null, not an exception");

            // A bare string where a list is expected is a common mistake worth tolerating.
            AreEqual(1, JsonValue.Parse(@"{""loadAfter"":""solo.mod""}")["loadAfter"].AsStringList().Count,
                "bare string coerced to list");

            ThrowsJson("{ \"a\": }", "missing value");
            ThrowsJson("{ \"a\" 1 }", "missing colon");
            ThrowsJson("{ \"unterminated: 1 }", "unterminated string");
            ThrowsJson("", "empty document");

            // Errors must point at the right line, or authors cannot find the problem.
            try
            {
                JsonValue.Parse("{\n  \"a\": 1,\n  \"b\": @\n}");
                Fail("expected a parse error for an invalid character");
            }
            catch (JsonException ex)
            {
                AreEqual(3, ex.Line, "error reports the correct line number");
            }
        }

        private static void RunVersionTests()
        {
            Section("Version handling");

            AreEqual(new Version(1, 2, 0), ModVersion.Parse("1.2.0"), "plain version");
            AreEqual(new Version(1, 2, 0), ModVersion.Parse("v1.2.0"), "leading v");
            AreEqual(new Version(1, 2, 0), ModVersion.Parse("1.2.0-beta3"), "pre-release suffix");
            AreEqual(new Version(1, 2), ModVersion.Parse("1.2"), "two components");
            AreEqual(new Version(0, 0, 0), ModVersion.Parse("garbage"), "unparseable falls back to 0.0.0");
            AreEqual(new Version(0, 0, 0), ModVersion.Parse(null), "null falls back to 0.0.0");

            IsTrue(ModVersion.Satisfies("1.2.0", "1.0.0"), "newer satisfies older requirement");
            IsTrue(ModVersion.Satisfies("1.0.0", "1.0.0"), "equal satisfies");
            IsFalse(ModVersion.Satisfies("0.9.0", "1.0.0"), "older does not satisfy");
        }

        // ------------------------------------------------------------- dependencies

        private static void RunDependencyTests()
        {
            Section("Dependency resolution");

            // b depends on a, so a must load first regardless of discovery order.
            var mods = new List<LoadedMod>
            {
                Mod("b.mod", requires: ("a.mod", "1.0.0")),
                Mod("a.mod", version: "1.0.0")
            };

            List<LoadedMod> order = DependencyResolver.Resolve(mods, new Version(1, 0, 0));
            AreEqual("a.mod, b.mod", string.Join(", ", order.Select(m => m.Id)), "dependency loads first");

            // A missing requirement disables the dependent, not the whole run.
            mods = new List<LoadedMod> { Mod("needs.missing", requires: ("absent.mod", "1.0.0")) };
            order = DependencyResolver.Resolve(mods, new Version(1, 0, 0));
            AreEqual(0, order.Count, "mod with a missing requirement is skipped");
            IsTrue(mods[0].Failed, "and is marked as failed");

            // A too-old dependency is refused.
            mods = new List<LoadedMod>
            {
                Mod("old.dep", version: "0.5.0"),
                Mod("wants.new", requires: ("old.dep", "1.0.0"))
            };
            order = DependencyResolver.Resolve(mods, new Version(1, 0, 0));
            AreEqual("old.dep", string.Join(", ", order.Select(m => m.Id)), "version requirement enforced");

            // Cycles disable every participant rather than picking arbitrarily.
            mods = new List<LoadedMod>
            {
                Mod("cycle.a", requires: ("cycle.b", "0.0.0")),
                Mod("cycle.b", requires: ("cycle.a", "0.0.0"))
            };
            order = DependencyResolver.Resolve(mods, new Version(1, 0, 0));
            AreEqual(0, order.Count, "circular dependency disables both mods");

            // Transitive orphaning: c needs b, b needs a missing mod.
            mods = new List<LoadedMod>
            {
                Mod("chain.c", requires: ("chain.b", "0.0.0")),
                Mod("chain.b", requires: ("chain.absent", "0.0.0"))
            };
            order = DependencyResolver.Resolve(mods, new Version(1, 0, 0));
            AreEqual(0, order.Count, "orphaned dependents are disabled transitively");

            // loadAfter is a soft hint: a missing target must not disable anything.
            mods = new List<LoadedMod> { Mod("soft.mod", loadAfter: "not.installed") };
            order = DependencyResolver.Resolve(mods, new Version(1, 0, 0));
            AreEqual(1, order.Count, "loadAfter on an absent mod is ignored");

            // loadBefore inverts correctly.
            mods = new List<LoadedMod>
            {
                Mod("second.mod"),
                Mod("first.mod", loadBefore: "second.mod")
            };
            order = DependencyResolver.Resolve(mods, new Version(1, 0, 0));
            AreEqual("first.mod, second.mod", string.Join(", ", order.Select(m => m.Id)), "loadBefore honoured");

            // A mod needing a newer loader is refused.
            mods = new List<LoadedMod> { Mod("future.mod", minLoader: "9.0.0") };
            order = DependencyResolver.Resolve(mods, new Version(1, 0, 0));
            AreEqual(0, order.Count, "minLoaderVersion enforced");
        }

        // --------------------------------------------------------------------- xnb

        private static void RunXnbTests(string gameDirectory)
        {
            Section("XNB encoding");

            // 1Part.xnb is a 1x1 opaque white texture: small enough to compare byte for byte.
            string reference = Path.Combine(gameDirectory, "Content", "Utility", "1Part.xnb");
            if (!File.Exists(reference))
            {
                Console.WriteLine($"  SKIPPED: reference asset not found at {reference}");
                return;
            }

            byte[] expected = File.ReadAllBytes(reference);

            using (var bitmap = new Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, Color.FromArgb(255, 255, 255, 255));
                byte[] actual = XnbEncoder.FromBitmap(bitmap);

                AreEqual(expected.Length, actual.Length, "encoded length matches the game's own XNB");
                IsTrue(expected.SequenceEqual(actual),
                    "1x1 white PNG encodes byte-for-byte identically to Content/Utility/1Part.xnb");

                if (!expected.SequenceEqual(actual))
                {
                    Console.WriteLine("    expected: " + Hex(expected));
                    Console.WriteLine("    actual:   " + Hex(actual));
                }
            }

            // Header shape must match a larger real asset too (64x64 with alpha).
            string icon = Path.Combine(gameDirectory, "Content", "Hud", "RekinIcon.xnb");
            if (File.Exists(icon))
            {
                byte[] real = File.ReadAllBytes(icon);

                using (var bitmap = new Bitmap(64, 64))
                {
                    byte[] actual = XnbEncoder.FromBitmap(bitmap);

                    AreEqual(real.Length, actual.Length, "64x64 encoding has the same total size as the real asset");
                    IsTrue(real.Take(0x55).SequenceEqual(actual.Take(0x55)),
                        "64x64 header is byte-identical to the game's own RekinIcon.xnb");
                }
            }

            // Alpha must be premultiplied, or every transparent edge gets a bright halo.
            using (var bitmap = new Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
                byte[] encoded = XnbEncoder.FromBitmap(bitmap);
                byte[] pixel = encoded.Skip(encoded.Length - 4).ToArray();

                // (255 * 128 + 127) / 255 == 128
                AreEqual(128, (int)pixel[0], "red channel premultiplied");
                AreEqual(0, (int)pixel[1], "green channel");
                AreEqual(0, (int)pixel[2], "blue channel");
                AreEqual(128, (int)pixel[3], "alpha preserved");
            }

            // Fully transparent pixels must be zeroed so filtering cannot bleed colour.
            using (var bitmap = new Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, Color.FromArgb(0, 255, 255, 255));
                byte[] encoded = XnbEncoder.FromBitmap(bitmap);
                byte[] pixel = encoded.Skip(encoded.Length - 4).ToArray();

                IsTrue(pixel.All(b => b == 0), "fully transparent pixel is zeroed");
            }

            // Channel order: GDI+ gives BGRA, XNA wants RGBA.
            using (var bitmap = new Bitmap(1, 1))
            {
                bitmap.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
                byte[] encoded = XnbEncoder.FromBitmap(bitmap);
                byte[] pixel = encoded.Skip(encoded.Length - 4).ToArray();

                AreEqual(10, (int)pixel[0], "red is first");
                AreEqual(20, (int)pixel[1], "green is second");
                AreEqual(30, (int)pixel[2], "blue is third");
            }
        }

        // ----------------------------------------------------------------- helpers

        private static LoadedMod Mod(
            string id,
            string version = "1.0.0",
            (string id, string version)? requires = null,
            string loadAfter = null,
            string loadBefore = null,
            string minLoader = null)
        {
            var manifest = new ModManifest
            {
                Id = id,
                Name = id,
                Version = version,
                Directory = @"C:\fake\" + id,
                MinLoaderVersion = minLoader
            };

            if (requires.HasValue)
                manifest.Requires[requires.Value.id] = requires.Value.version;

            if (loadAfter != null)
                manifest.LoadAfter.Add(loadAfter);

            if (loadBefore != null)
                manifest.LoadBefore.Add(loadBefore);

            return new LoadedMod(manifest);
        }

        private static string Hex(byte[] bytes)
            => string.Join(" ", bytes.Take(32).Select(b => b.ToString("x2")));

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine(name);
        }

        private static void AreEqual<T>(T expected, T actual, string what)
        {
            if (EqualityComparer<T>.Default.Equals(expected, actual))
                Pass(what);
            else
                Fail($"{what} (expected '{expected}', got '{actual}')");
        }

        private static void IsTrue(bool condition, string what)
        {
            if (condition) Pass(what); else Fail(what);
        }

        private static void IsFalse(bool condition, string what)
        {
            if (!condition) Pass(what); else Fail(what);
        }

        private static void ThrowsJson(string text, string what)
        {
            try
            {
                JsonValue.Parse(text);
                Fail($"{what} should have thrown");
            }
            catch (JsonException)
            {
                Pass($"{what} rejected");
            }
        }

        private static void Pass(string what)
        {
            _passed++;
            Console.WriteLine("  ok   " + what);
        }

        private static void Fail(string what)
        {
            Failures.Add(what);
            Console.WriteLine("  FAIL " + what);
        }
    }
}
