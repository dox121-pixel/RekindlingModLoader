using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// A mod the loader knows about, in whatever state it reached.
    /// </summary>
    internal sealed class LoadedMod
    {
        public LoadedMod(ModManifest manifest) => Manifest = manifest;

        public ModManifest Manifest { get; }
        public string Id => Manifest.Id;

        public Assembly Assembly { get; set; }
        public IMod Instance { get; set; }
        public ModContext Context { get; set; }

        /// <summary>Set when the mod is skipped or fails; shown in the startup summary.</summary>
        public string FailureReason { get; private set; }

        public bool Failed => FailureReason != null;

        /// <summary>True once <see cref="IMod.OnLoad"/> has completed without throwing.</summary>
        public bool IsLoaded { get; set; }

        public void Fail(string reason)
        {
            // Keep the first failure: later ones are usually consequences of it.
            if (FailureReason == null)
                FailureReason = reason;
        }

        public override string ToString() => Manifest.ToString();
    }

    /// <summary>
    /// Finds mod folders and reads their manifests. A folder is a mod when it contains a
    /// <c>mod.json</c>; anything else is ignored, so users can drop readmes and zips in
    /// <c>Mods/</c> without breaking startup.
    /// </summary>
    internal static class ModDiscovery
    {
        public const string ManifestFileName = "mod.json";

        /// <summary>
        /// Scans <paramref name="modsDirectory"/> one level deep for mod folders.
        /// A folder whose name starts with <c>.</c> or is named <c>disabled</c> is skipped, which
        /// gives users a no-uninstall way to turn a mod off.
        /// </summary>
        public static List<LoadedMod> Discover(string modsDirectory)
        {
            var results = new List<LoadedMod>();

            if (!Directory.Exists(modsDirectory))
            {
                Directory.CreateDirectory(modsDirectory);
                Log.Info("Loader", $"Created the mods folder at {modsDirectory}");
                return results;
            }

            foreach (string directory in Directory.GetDirectories(modsDirectory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string folderName = Path.GetFileName(directory);

                if (folderName.StartsWith(".", StringComparison.Ordinal) ||
                    folderName.StartsWith("_", StringComparison.Ordinal) ||
                    folderName.Equals("disabled", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug("Loader", $"Skipping '{folderName}' (disabled by naming convention).");
                    continue;
                }

                string manifestPath = Path.Combine(directory, ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    Log.Debug("Loader", $"Skipping '{folderName}': no {ManifestFileName}.");
                    continue;
                }

                LoadedMod mod = ReadManifest(manifestPath, directory);
                if (mod != null)
                    results.Add(mod);
            }

            return results;
        }

        private static LoadedMod ReadManifest(string manifestPath, string directory)
        {
            string folderName = Path.GetFileName(directory);

            JsonValue json;
            try
            {
                json = JsonValue.Parse(File.ReadAllText(manifestPath));
            }
            catch (JsonException ex)
            {
                Log.Error("Loader", $"'{folderName}' has an invalid {ManifestFileName}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Loader", $"Could not read '{folderName}/{ManifestFileName}': {ex.Message}");
                return null;
            }

            if (json.Kind != JsonKind.Object)
            {
                Log.Error("Loader", $"'{folderName}/{ManifestFileName}' must contain a JSON object.");
                return null;
            }

            var manifest = new ModManifest
            {
                Id = json["id"].AsString()?.Trim(),
                Name = json["name"].AsString(),
                Version = json["version"].AsString("0.0.0"),
                Author = json["author"].AsString(),
                Description = json["description"].AsString(),
                Assembly = json["assembly"].AsString(),
                Entry = json["entry"].AsString(),
                MinLoaderVersion = json["minLoaderVersion"].AsString(),
                Assets = json["assets"].AsString(),
                Directory = directory
            };

            if (string.IsNullOrWhiteSpace(manifest.Id))
            {
                Log.Error("Loader", $"'{folderName}/{ManifestFileName}' is missing the required \"id\" field.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
                manifest.Name = manifest.Id;

            manifest.LoadAfter.AddRange(json["loadAfter"].AsStringList());
            manifest.LoadBefore.AddRange(json["loadBefore"].AsStringList());

            foreach (KeyValuePair<string, JsonValue> pair in json["requires"].AsObject)
                manifest.Requires[pair.Key] = pair.Value.AsString("0.0.0");

            // "requires" as a plain list means "any version".
            foreach (string id in json["requires"].AsStringList())
                manifest.Requires[id] = "0.0.0";

            // Default to an "assets" folder when one exists and nothing was configured.
            if (string.IsNullOrWhiteSpace(manifest.Assets) &&
                Directory.Exists(Path.Combine(directory, "assets")))
            {
                manifest.Assets = "assets";
            }

            return new LoadedMod(manifest);
        }
    }
}
