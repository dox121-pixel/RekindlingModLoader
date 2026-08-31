using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Owns the whole mod loading process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately usable two ways. The bundled launcher calls <see cref="Initialize"/> and then
    /// invokes the game's entry point, which needs no change to any game file. If the loader is
    /// ever adopted upstream, the same call at the top of the game's own <c>Main</c> is the only
    /// integration required:
    /// </para>
    /// <code>
    /// [STAThread]
    /// static void Main()
    /// {
    ///     Rekindling.ModLoader.ModLoaderHost.Initialize(AppDomain.CurrentDomain.BaseDirectory);
    ///     using Game1 game = new Game1();
    ///     game.Run();
    /// }
    /// </code>
    /// </remarks>
    public static class ModLoaderHost
    {
        private static readonly List<LoadedMod> All = new List<LoadedMod>();
        private static readonly AssetOverrideRegistry Assets = new AssetOverrideRegistry();

        private static Dictionary<string, LoadedMod> _byId =
            new Dictionary<string, LoadedMod>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<ModOptionsStore> OptionStores = new List<ModOptionsStore>();

        private static bool _initialized;
        private static bool _shutdown;
        private static string _gameDirectory;
        private static string _configDirectory;

        /// <summary>Version of the running loader.</summary>
        public static string Version { get; } =
            typeof(ModLoaderHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        /// <summary>The loaded game assembly, or <c>null</c> when running inside the game already.</summary>
        public static Assembly GameAssembly { get; private set; }

        /// <summary>Ids of every mod that loaded successfully, in load order.</summary>
        public static IReadOnlyList<string> LoadedModIds =>
            All.Where(m => m.IsLoaded).Select(m => m.Id).ToList();

        /// <summary>
        /// Brings up logging, patches the game, and loads every mod in <c>Mods/</c>.
        /// Safe to call twice; the second call is ignored.
        /// </summary>
        /// <param name="gameDirectory">The Rekindling install folder.</param>
        /// <param name="args">Command line arguments; <c>--debug</c> and <c>--trace</c> raise the log level.</param>
        public static void Initialize(string gameDirectory, string[] args = null)
        {
            if (_initialized)
                return;
            _initialized = true;

            _gameDirectory = gameDirectory;
            // Settings live outside the mod folders, so they survive updating or reinstalling a mod.
            _configDirectory = Path.Combine(gameDirectory, "ModConfig");
            LogLevel level = ParseLogLevel(args);

            Log.Initialize(gameDirectory, level);
            Log.Info("Loader", $"Rekindling Mod Loader {Version}");
            Log.Info("Loader", $"Game folder: {gameDirectory}");

            ModEventBridge.Install();
            AssemblyResolver.Install(gameDirectory);

            // The game assembly must be in memory before Harmony can find ZTD.Game1. Loading it
            // does not run Main, so this is safe to do ahead of patching.
            GameAssembly = AssemblyResolver.LoadGameAssembly(gameDirectory);

            // Before the hooks, so the flag is already false when setupMainMenu builds the
            // menu and decides which entries to disable.
            MultiplayerGuard.Initialize(HasFlag(args, "--allow-multiplayer"));

            GameHooks.GameReady = NotifyGameReady;
            GameHooks.ShuttingDown = Shutdown;
            GameHooks.Apply(Assets);

            string modsDirectory = Path.Combine(gameDirectory, "Mods");
            List<LoadedMod> discovered = ModDiscovery.Discover(modsDirectory);

            if (discovered.Count == 0)
            {
                Log.Info("Loader", $"No mods found in {modsDirectory}.");
                ModRegistry.Populate(new IModInfo[0]);
                return;
            }

            Log.Info("Loader", $"Found {discovered.Count} mod(s) in {modsDirectory}.");
            All.AddRange(discovered);

            List<LoadedMod> ordered = DependencyResolver.Resolve(All, ModVersion.Parse(Version));
            _byId = All.ToDictionary(m => m.Id, m => m, StringComparer.OrdinalIgnoreCase);

            foreach (LoadedMod mod in ordered)
                LoadMod(mod);

            // Publish the results so mods (and the in-game mod list) can read them.
            ModRegistry.Populate(All.Cast<IModInfo>().ToList());

            ReportSummary();
        }

        /// <summary>
        /// Calls <c>OnUnload</c> on every loaded mod and flushes the log. Idempotent.
        /// </summary>
        public static void Shutdown()
        {
            if (_shutdown || !_initialized)
                return;
            _shutdown = true;

            foreach (ModOptionsStore store in OptionStores)
            {
                try
                {
                    store.Save();
                }
                catch (Exception ex)
                {
                    Log.Error(store.ModId, "Failed to save options during shutdown.", ex);
                }
            }

            foreach (LoadedMod mod in All.Where(m => m.IsLoaded).Reverse())
            {
                try
                {
                    mod.Instance?.OnUnload();
                }
                catch (Exception ex)
                {
                    Log.Error(mod.Id, "Threw during OnUnload.", ex);
                }
            }

            Log.Info("Loader", "Shutdown complete.");
            Log.Flush();
        }

        // ------------------------------------------------------------------ loading

        private static void LoadMod(LoadedMod mod)
        {
            ModManifest manifest = mod.Manifest;

            try
            {
                Assets.CurrentModId = mod.Id;

                RegisterDeclaredAssets(mod);

                if (string.IsNullOrWhiteSpace(manifest.Assembly))
                {
                    // Content-only mod: assets are registered, there is no code to run.
                    mod.IsLoaded = true;
                    Log.Info("Loader", $"Loaded {manifest.Name} {manifest.Version} (content only).");
                    return;
                }

                string assemblyPath = Path.Combine(manifest.Directory, manifest.Assembly);
                if (!File.Exists(assemblyPath))
                {
                    mod.Fail($"Assembly '{manifest.Assembly}' was not found in the mod folder.");
                    return;
                }

                mod.Assembly = Assembly.LoadFrom(assemblyPath);

                Type entryType = FindEntryType(mod);
                if (entryType == null)
                    return; // FindEntryType already recorded the reason.

                if (!(Activator.CreateInstance(entryType) is IMod instance))
                {
                    mod.Fail($"'{entryType.FullName}' does not implement IMod.");
                    return;
                }

                var logger = new ModLogger(mod.Id);
                var options = new ModOptionsStore(mod.Id, _configDirectory, logger);

                mod.Instance = instance;
                mod.Context = new ModContext(
                    manifest,
                    logger,
                    new Harmony(mod.Id),
                    Assets,
                    options,
                    _gameDirectory,
                    Version,
                    LookupMod);

                instance.OnLoad(mod.Context);

                if (options.All.Count > 0)
                {
                    OptionStores.Add(options);
                    ModOptionsRegistry.Register(options);
                    Log.Debug(mod.Id, $"Declared {options.All.Count} option(s).");
                }

                mod.IsLoaded = true;
                Log.Info("Loader", $"Loaded {manifest.Name} {manifest.Version} by {manifest.Author ?? "unknown"}.");
            }
            catch (ReflectionTypeLoadException ex)
            {
                // The default message names no types at all, which makes this failure very hard
                // to diagnose. Surface the individual loader errors instead.
                IEnumerable<string> details = ex.LoaderExceptions
                    .Where(e => e != null)
                    .Select(e => e.Message)
                    .Distinct();

                mod.Fail("Its assembly could not be loaded; it may target a different game version.");
                Log.Error(mod.Id, "Assembly load failed.",
                    new ReflectionTypeLoadExceptionWrapper(ex.Message, ex, details));
                RollBack(mod);
            }
            catch (Exception ex)
            {
                mod.Fail($"Threw during loading: {ex.GetType().Name}: {ex.Message}");
                Log.Error(mod.Id, "Failed to load.", ex);
                RollBack(mod);
            }
            finally
            {
                Assets.CurrentModId = "loader";
            }
        }

        /// <summary>
        /// Undoes a half-applied mod. Without this, a mod that patches several methods and then
        /// throws would leave the earlier patches live with no owner to maintain them.
        /// </summary>
        private static void RollBack(LoadedMod mod)
        {
            mod.IsLoaded = false;

            try
            {
                mod.Context?.Harmony?.UnpatchAll(mod.Id);
            }
            catch (Exception ex)
            {
                Log.Warn(mod.Id, $"Could not remove its patches after the failure: {ex.Message}");
            }

            foreach (string asset in Assets.Registered
                         .Where(pair => string.Equals(pair.Value, mod.Id, StringComparison.OrdinalIgnoreCase))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                Assets.Remove(asset);
            }
        }

        private static void RegisterDeclaredAssets(LoadedMod mod)
        {
            string assets = mod.Manifest.Assets;
            if (string.IsNullOrWhiteSpace(assets))
                return;

            string directory = Path.Combine(mod.Manifest.Directory, assets);
            if (!Directory.Exists(directory))
            {
                Log.Warn(mod.Id, $"Declared an assets folder '{assets}' that does not exist.");
                return;
            }

            int count = Assets.OverrideDirectory(directory);
            if (count > 0)
                Log.Info(mod.Id, $"Registered {count} asset override(s).");
        }

        /// <summary>
        /// Resolves the mod's entry-point type, either from <c>entry</c> in the manifest or by
        /// scanning for the single <see cref="IMod"/> implementation.
        /// </summary>
        private static Type FindEntryType(LoadedMod mod)
        {
            string declared = mod.Manifest.Entry;

            if (!string.IsNullOrWhiteSpace(declared))
            {
                Type type = mod.Assembly.GetType(declared, throwOnError: false, ignoreCase: true);
                if (type == null)
                {
                    mod.Fail($"Entry type '{declared}' was not found in {mod.Manifest.Assembly}.");
                    return null;
                }
                return type;
            }

            List<Type> candidates = mod.Assembly.GetTypes()
                .Where(t => typeof(IMod).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .ToList();

            if (candidates.Count == 0)
            {
                mod.Fail(
                    $"No class in {mod.Manifest.Assembly} implements IMod. " +
                    "Derive your entry class from ModBase, or omit \"assembly\" for a content-only mod.");
                return null;
            }

            if (candidates.Count > 1)
            {
                mod.Fail(
                    $"{mod.Manifest.Assembly} contains {candidates.Count} IMod implementations " +
                    $"({string.Join(", ", candidates.Select(t => t.FullName))}). " +
                    "Name the one to use with \"entry\" in mod.json.");
                return null;
            }

            return candidates[0];
        }

        private static void NotifyGameReady()
        {
            foreach (LoadedMod mod in All.Where(m => m.IsLoaded))
            {
                try
                {
                    mod.Instance?.OnGameReady();
                }
                catch (Exception ex)
                {
                    Log.Error(mod.Id, "Threw during OnGameReady.", ex);
                }
            }

            Log.Flush();
        }

        private static IMod LookupMod(string modId)
            => _byId.TryGetValue(modId, out LoadedMod mod) && mod.IsLoaded ? mod.Instance : null;

        private static void ReportSummary()
        {
            int loaded = All.Count(m => m.IsLoaded);
            int failed = All.Count - loaded;

            Log.Info("Loader", $"{loaded} mod(s) loaded, {failed} skipped.");

            foreach (LoadedMod mod in All.Where(m => !m.IsLoaded))
            {
                Log.Warn("Loader",
                    $"Skipped {mod.Manifest.Name} ({mod.Id}): {mod.FailureReason ?? "unknown reason"}");
            }

            Log.Flush();
        }

        /// <summary>
        /// Writes any option changes that have settled. Called once per frame; the stores
        /// themselves decide whether enough time has passed to be worth a disk write.
        /// </summary>
        internal static void FlushOptions()
        {
            for (int i = 0; i < OptionStores.Count; i++)
                OptionStores[i].FlushIfDue();
        }

        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null)
                return false;

            foreach (string arg in args)
            {
                if (arg.Equals(flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static LogLevel ParseLogLevel(string[] args)
        {
            if (args == null)
                return LogLevel.Info;

            foreach (string arg in args)
            {
                if (arg.Equals("--trace", StringComparison.OrdinalIgnoreCase))
                    return LogLevel.Trace;
                if (arg.Equals("--debug", StringComparison.OrdinalIgnoreCase))
                    return LogLevel.Debug;
            }

            return LogLevel.Info;
        }
    }
}
