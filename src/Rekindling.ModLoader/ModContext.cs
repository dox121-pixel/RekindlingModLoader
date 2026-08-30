using System;
using HarmonyLib;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// What a mod receives in <c>OnLoad</c>.
    /// </summary>
    internal sealed class ModContext : IModContext
    {
        private readonly Func<string, IMod> _lookup;

        public ModContext(
            ModManifest manifest,
            IModLogger log,
            Harmony harmony,
            IAssetOverrides assets,
            string gameDirectory,
            string loaderVersion,
            Func<string, IMod> lookup)
        {
            Manifest = manifest;
            Log = log;
            Harmony = harmony;
            Assets = assets;
            GameDirectory = gameDirectory;
            LoaderVersion = loaderVersion;
            _lookup = lookup;
        }

        public ModManifest Manifest { get; }

        public string ModDirectory => Manifest.Directory;

        public IModLogger Log { get; }

        public Harmony Harmony { get; }

        public IAssetOverrides Assets { get; }

        public string GameDirectory { get; }

        public string LoaderVersion { get; }

        public IMod GetMod(string modId)
            => string.IsNullOrWhiteSpace(modId) ? null : _lookup(modId);

        public bool IsModLoaded(string modId) => GetMod(modId) != null;
    }
}
