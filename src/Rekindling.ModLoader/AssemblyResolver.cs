using System;
using System.IO;
using System.Reflection;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Teaches the CLR where to find the game and loader assemblies when a mod references them.
    /// </summary>
    /// <remarks>
    /// Default probing only looks for <c>.dll</c> files, so a mod that references
    /// <c>Rekindling.exe</c> - which most code mods do - would otherwise fail with a
    /// <c>FileNotFoundException</c> the moment one of its types is touched.
    /// </remarks>
    internal static class AssemblyResolver
    {
        private const string GameAssemblyName = "Rekindling";
        private const string GameFileName = "Rekindling.exe";

        private static string _gameDirectory;
        private static bool _installed;

        public static void Install(string gameDirectory)
        {
            if (_installed)
                return;
            _installed = true;

            _gameDirectory = gameDirectory;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        /// <summary>
        /// Loads <c>Rekindling.exe</c> into the current AppDomain without running it, so Harmony
        /// can see the <c>ZTD</c> types.
        /// </summary>
        public static Assembly LoadGameAssembly(string gameDirectory)
        {
            string path = Path.Combine(gameDirectory, GameFileName);
            if (!File.Exists(path))
            {
                Log.Error("Loader", $"{GameFileName} was not found in {gameDirectory}.");
                return null;
            }

            try
            {
                Assembly assembly = Assembly.LoadFrom(path);
                Log.Debug("Loader", $"Loaded {assembly.GetName().Name} {assembly.GetName().Version}.");
                return assembly;
            }
            catch (Exception ex)
            {
                Log.Error("Loader", $"Could not load {GameFileName}.", ex);
                return null;
            }
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string requested;
            try
            {
                requested = new AssemblyName(args.Name).Name;
            }
            catch
            {
                return null;
            }

            // The game ships as an .exe, which the default probing logic never looks for.
            if (requested.Equals(GameAssemblyName, StringComparison.OrdinalIgnoreCase))
                return LoadIfPresent(Path.Combine(_gameDirectory, GameFileName));

            // Mods reference the API and Harmony; both sit next to the loader.
            string local = Path.Combine(_gameDirectory, requested + ".dll");
            if (File.Exists(local))
                return LoadIfPresent(local);

            string beside = Path.Combine(
                Path.GetDirectoryName(typeof(AssemblyResolver).Assembly.Location) ?? _gameDirectory,
                requested + ".dll");

            return File.Exists(beside) ? LoadIfPresent(beside) : null;
        }

        private static Assembly LoadIfPresent(string path)
        {
            try
            {
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            }
            catch (Exception ex)
            {
                Log.Warn("Loader", $"Could not load '{path}': {ex.Message}");
                return null;
            }
        }
    }
}
