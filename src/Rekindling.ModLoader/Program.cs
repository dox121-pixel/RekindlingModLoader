using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Standalone launcher. Sits in the Rekindling folder, brings up the mod loader, then hands
    /// control to the game's own entry point. No game file is modified, so Steam's file
    /// validation and game updates leave everything intact.
    /// </summary>
    internal static class Program
    {
        // Game1's constructor calls Control.FromHandle, and the game's own Main is [STAThread];
        // matching it here keeps Windows Forms interop happy.
        [STAThread]
        private static int Main(string[] args)
        {
            // Content paths in the game are relative, so we must run as if we were the game.
            string gameDirectory = AppDomain.CurrentDomain.BaseDirectory;
            Directory.SetCurrentDirectory(gameDirectory);

            if (IntPtr.Size != 4)
            {
                // Rekindling.exe is flagged 32BIT_REQUIRED, so a 64-bit host can never load it.
                // Catch it here rather than letting it surface as a bare BadImageFormatException.
                Fail("The mod loader was built for the wrong architecture.\n\n" +
                     "Rekindling is a 32-bit game, so the loader must be built with " +
                     "PlatformTarget set to x86. Rebuild it, or download an x86 release.");
                return 1;
            }

            try
            {
                ModLoaderHost.Initialize(gameDirectory, args);
            }
            catch (Exception ex)
            {
                // A broken loader must never stop someone playing the game. Report and continue
                // unmodded rather than leaving them with a dead launcher.
                ReportStartupFailure(gameDirectory, ex);
            }

            return LaunchGame(gameDirectory);
        }

        /// <summary>
        /// Invokes <c>ZTD.Program.Main</c> reflectively. Deliberately not a compile-time
        /// reference: the loader keeps working when the game updates its assembly.
        /// </summary>
        private static int LaunchGame(string gameDirectory)
        {
            string exePath = Path.Combine(gameDirectory, "Rekindling.exe");
            if (!File.Exists(exePath))
            {
                Fail($"Could not find Rekindling.exe next to the loader.\n\n" +
                     $"Expected it at:\n{exePath}\n\n" +
                     $"Copy the whole ModLoader folder into your Rekindling install directory.");
                return 2;
            }

            try
            {
                Assembly game = Assembly.LoadFrom(exePath);
                MethodInfo entry = game.EntryPoint;
                if (entry == null)
                {
                    Fail("Rekindling.exe has no entry point. The install may be corrupt; " +
                         "verify the game files through Steam.");
                    return 3;
                }

                Log.Info("Loader", "Handing off to the game.");
                Log.Flush();

                // The game's Main is parameterless; tolerate it gaining a string[] later.
                object[] parameters = entry.GetParameters().Length == 0
                    ? null
                    : new object[] { Array.Empty<string>() };

                object result = entry.Invoke(null, parameters);
                return result is int code ? code : 0;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                Log.Error("Loader", "The game threw an unhandled exception.", ex.InnerException);
                Log.Flush();
                throw ex.InnerException;
            }
            finally
            {
                ModLoaderHost.Shutdown();
                Log.Flush();
            }
        }

        private static void ReportStartupFailure(string gameDirectory, Exception ex)
        {
            try
            {
                Log.Error("Loader", "Mod loading failed; starting the game unmodded.", ex);
                Log.Flush();
            }
            catch
            {
                // Logging itself is broken - fall back to a file we write by hand.
                try
                {
                    File.AppendAllText(
                        Path.Combine(gameDirectory, "modloader-crash.txt"),
                        DateTime.Now + Environment.NewLine + ex + Environment.NewLine);
                }
                catch
                {
                    // Nothing left to do; still let the game start.
                }
            }
        }

        private static void Fail(string message)
        {
            Log.Error("Loader", message);
            Log.Flush();
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    message, "Rekindling Mod Loader",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch
            {
                // Headless or Forms unavailable - the log and console already have it.
            }
        }
    }
}
