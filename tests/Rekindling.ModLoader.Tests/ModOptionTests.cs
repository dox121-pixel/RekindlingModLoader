using System;
using System.IO;

namespace Rekindling.ModLoader.Tests
{
    internal static class ModOptionTests
    {
        public static void Run(Action<string> section, Action<bool, string> isTrue, Action<object, object, string> areEqual)
        {
            section("Mod options");

            var toggle = new ToggleOption("t", "Toggle", defaultValue: true);
            areEqual(true, toggle.Value, "toggle starts at its default");
            toggle.Toggle();
            areEqual(false, toggle.Value, "toggle flips");
            isTrue(!toggle.IsDefault, "and knows it is no longer default");
            toggle.Reset();
            isTrue(toggle.IsDefault, "reset restores the default");

            var choice = new ChoiceOption("c", "Choice", new[] { "a", "b", "c" }, "b");
            areEqual("b", choice.Value, "choice honours its default");
            areEqual(1, choice.SelectedIndex, "selected index");
            choice.Next();
            areEqual("c", choice.Value, "next advances");
            choice.Next();
            areEqual("a", choice.Value, "next wraps");
            choice.Previous();
            areEqual("c", choice.Value, "previous wraps back");

            // A stale config must not be able to put a mod into a state it never anticipated.
            choice.Value = "not-a-choice";
            areEqual("c", choice.Value, "a value outside the list is refused");

            var unknownDefault = new ChoiceOption("c2", "Choice", new[] { "x", "y" }, "missing");
            areEqual("x", unknownDefault.Value, "an unknown default falls back to the first choice");

            var slider = new SliderOption("s", "Slider", 0f, 10f, 5f);
            areEqual(0.5f, slider.Normalized, "normalised position");
            slider.Value = 999f;
            areEqual(10f, slider.Value, "slider clamps to maximum");
            slider.Value = -5f;
            areEqual(0f, slider.Value, "slider clamps to minimum");

            var point = new PointOption("p", "Point", 0.1f, 0.2f);
            point.Set(1.5f, -0.5f);
            areEqual(1f, point.X, "point clamps x into 0..1");
            areEqual(0f, point.Y, "point clamps y into 0..1");

            // Change notification is what lets a mod react without polling.
            int changes = 0;
            var watched = new ToggleOption("w", "Watched", false);
            watched.Changed += _ => changes++;
            watched.Value = true;
            watched.Value = true; // no-op, same value
            areEqual(1, changes, "changing to the same value raises nothing");

            // ------------------------------------------------------------ persistence

            section("Mod option persistence");

            string directory = Path.Combine(Path.GetTempPath(), "rml-options-" + Guid.NewGuid().ToString("N"));

            try
            {
                var store = new ModOptionsStore("test.mod", directory, null);
                ToggleOption enabled = store.Toggle("enabled", "Enabled", true);
                ChoiceOption position = store.Choice("position", "Position", new[] { "left", "right" }, "left");
                SliderOption speed = store.Slider("speed", "Speed", 0f, 100f, 50f);
                PointOption spot = store.Point("spot", "Spot", 0.5f, 0.5f);

                enabled.Value = false;
                position.Value = "right";
                speed.Value = 75f;
                spot.Set(0.25f, 0.75f);
                store.Save();

                isTrue(File.Exists(Path.Combine(directory, "test.mod.json")), "options file was written");

                // A second store over the same folder is what happens on the next launch.
                var reloaded = new ModOptionsStore("test.mod", directory, null);
                ToggleOption enabled2 = reloaded.Toggle("enabled", "Enabled", true);
                ChoiceOption position2 = reloaded.Choice("position", "Position", new[] { "left", "right" }, "left");
                SliderOption speed2 = reloaded.Slider("speed", "Speed", 0f, 100f, 50f);
                PointOption spot2 = reloaded.Point("spot", "Spot", 0.5f, 0.5f);

                areEqual(false, enabled2.Value, "toggle survived a reload");
                areEqual("right", position2.Value, "choice survived a reload");
                areEqual(75f, speed2.Value, "slider survived a reload");
                areEqual(0.25f, spot2.X, "point x survived a reload");
                areEqual(0.75f, spot2.Y, "point y survived a reload");

                // Declaring the same key twice is a mod bug worth failing loudly on.
                bool threw = false;
                try { reloaded.Toggle("enabled", "Enabled again", true); }
                catch (InvalidOperationException) { threw = true; }
                isTrue(threw, "declaring a duplicate option key throws");

                // A corrupt file must not stop a mod loading.
                File.WriteAllText(Path.Combine(directory, "broken.mod.json"), "{ this is not json");
                var broken = new ModOptionsStore("broken.mod", directory, null);
                ToggleOption fallback = broken.Toggle("enabled", "Enabled", true);
                areEqual(true, fallback.Value, "a malformed options file falls back to defaults");
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch { }
            }
        }
    }
}
