using System;
using System.Collections.Generic;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// A mod's <c>mod.json</c>, as shipped next to its assembly.
    /// </summary>
    /// <example>
    /// <code>
    /// {
    ///   "id": "dox.betterfarming",
    ///   "name": "Better Farming",
    ///   "version": "1.2.0",
    ///   "author": "Dox",
    ///   "description": "Crops grow in stages and can be fertilised.",
    ///   "assembly": "BetterFarming.dll",
    ///   "entry": "BetterFarming.ModEntry",
    ///   "loadAfter": [ "someone.corelib" ],
    ///   "requires": { "someone.corelib": "1.0.0" },
    ///   "minLoaderVersion": "0.1.0",
    ///   "assets": "assets"
    /// }
    /// </code>
    /// </example>
    public sealed class ModManifest
    {
        /// <summary>
        /// Unique, stable, lowercase identifier. Convention is <c>author.modname</c>.
        /// Changing this after release breaks anything that depends on the mod.
        /// </summary>
        public string Id { get; set; }

        /// <summary>Human-readable name shown in logs and (eventually) in-game UI.</summary>
        public string Name { get; set; }

        /// <summary>Semantic version of this mod, e.g. <c>1.2.0</c>.</summary>
        public string Version { get; set; }

        /// <summary>Author or team name.</summary>
        public string Author { get; set; }

        /// <summary>One or two sentences describing the mod.</summary>
        public string Description { get; set; }

        /// <summary>
        /// File name of the mod assembly, relative to the mod folder.
        /// Optional: a content-only mod (assets only, no code) may omit it.
        /// </summary>
        public string Assembly { get; set; }

        /// <summary>
        /// Optional fully-qualified name of the <see cref="IMod"/> implementation. If omitted,
        /// the loader scans the assembly for the single non-abstract implementation.
        /// Set it explicitly to avoid the scan, or when an assembly contains more than one.
        /// </summary>
        public string Entry { get; set; }

        /// <summary>
        /// Soft ordering: these mod ids load first <em>if present</em>. Missing ids are ignored.
        /// </summary>
        public List<string> LoadAfter { get; } = new List<string>();

        /// <summary>
        /// Soft ordering in the other direction: this mod loads before these ids.
        /// </summary>
        public List<string> LoadBefore { get; } = new List<string>();

        /// <summary>
        /// Hard dependencies, mapping mod id to minimum version. A missing or too-old
        /// dependency disables this mod with an explanatory log line rather than crashing.
        /// </summary>
        public Dictionary<string, string> Requires { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Minimum loader version this mod needs. Older loaders refuse to load it.
        /// </summary>
        public string MinLoaderVersion { get; set; }

        /// <summary>
        /// Optional folder (relative to the mod folder) holding loose asset overrides that the
        /// loader registers automatically. Defaults to <c>assets</c> when that folder exists.
        /// </summary>
        public string Assets { get; set; }

        /// <summary>Absolute path to the folder this manifest was read from. Set by the loader.</summary>
        public string Directory { get; set; }

        /// <summary>Parsed <see cref="Version"/>, or 0.0.0 when unset/unparseable.</summary>
        public Version ParsedVersion => ModVersion.Parse(Version);

        public override string ToString() => $"{Name ?? Id} {Version} ({Id})";
    }
}
