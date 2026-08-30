using System;
using System.Collections.Generic;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Read-only view of a mod the loader discovered, whether or not it loaded successfully.
    /// </summary>
    /// <remarks>
    /// Deliberately covers failed mods too. A mod list that silently omits everything that broke
    /// is the opposite of useful when someone is trying to work out why their game looks wrong.
    /// </remarks>
    public interface IModInfo
    {
        string Id { get; }
        string Name { get; }
        string Version { get; }
        string Author { get; }
        string Description { get; }

        /// <summary>Absolute path to the mod's folder.</summary>
        string Directory { get; }

        /// <summary>
        /// Absolute path to the mod's poster image, or <c>null</c> when it ships none.
        /// Declared as <c>"poster"</c> in <c>mod.json</c>, relative to the mod folder.
        /// </summary>
        string PosterPath { get; }

        /// <summary>True when the mod loaded and is running.</summary>
        bool IsLoaded { get; }

        /// <summary>True when the mod has no code and only replaces assets.</summary>
        bool IsContentOnly { get; }

        /// <summary>Why the mod was skipped, or <c>null</c> when it loaded fine.</summary>
        string FailureReason { get; }
    }

    /// <summary>
    /// The set of mods in this session, for any mod that wants to display or reason about them.
    /// </summary>
    /// <remarks>
    /// Populated by the loader once loading finishes, so it is safe to read from
    /// <see cref="IMod.OnGameReady"/> onwards. Reading it during <c>OnLoad</c> gives whatever has
    /// been loaded up to that point, which is rarely what you want.
    /// </remarks>
    public static class ModRegistry
    {
        private static IReadOnlyList<IModInfo> _all = new IModInfo[0];

        /// <summary>Every discovered mod, in load order, including ones that failed.</summary>
        public static IReadOnlyList<IModInfo> All => _all;

        /// <summary>Mods that loaded successfully, in load order.</summary>
        public static IEnumerable<IModInfo> Loaded
        {
            get
            {
                foreach (IModInfo mod in _all)
                {
                    if (mod.IsLoaded)
                        yield return mod;
                }
            }
        }

        /// <summary>Looks up one mod by id, or <c>null</c> when it was never discovered.</summary>
        public static IModInfo Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            foreach (IModInfo mod in _all)
            {
                if (string.Equals(mod.Id, id, StringComparison.OrdinalIgnoreCase))
                    return mod;
            }

            return null;
        }

        internal static void Populate(IReadOnlyList<IModInfo> mods)
            => _all = mods ?? new IModInfo[0];
    }
}
