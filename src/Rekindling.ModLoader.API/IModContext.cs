using HarmonyLib;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Everything the loader hands a mod at load time.
    /// </summary>
    public interface IModContext
    {
        /// <summary>This mod's parsed <c>mod.json</c>.</summary>
        ModManifest Manifest { get; }

        /// <summary>Absolute path to the folder containing this mod's <c>mod.json</c>.</summary>
        string ModDirectory { get; }

        /// <summary>Logger tagged with this mod's id.</summary>
        IModLogger Log { get; }

        /// <summary>
        /// A Harmony instance whose id is this mod's id. Already created, so
        /// <c>Context.Harmony.PatchAll()</c> is enough to apply annotated patches in your
        /// assembly. The loader unpatches it automatically if the mod fails to load.
        /// </summary>
        Harmony Harmony { get; }

        /// <summary>Asset override registry, shared across all mods.</summary>
        IAssetOverrides Assets { get; }

        /// <summary>Absolute path to the Rekindling install folder.</summary>
        string GameDirectory { get; }

        /// <summary>Version of the running loader.</summary>
        string LoaderVersion { get; }

        /// <summary>
        /// Looks up another loaded mod's entry-point instance by id, for cross-mod integration.
        /// Returns <c>null</c> when that mod is absent or disabled — always null-check, so your
        /// mod degrades gracefully instead of crashing when an optional dependency is missing.
        /// </summary>
        IMod GetMod(string modId);

        /// <summary>True when a mod with this id is loaded and enabled.</summary>
        bool IsModLoaded(string modId);
    }
}
