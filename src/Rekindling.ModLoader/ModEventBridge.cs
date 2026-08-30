using System;
using Microsoft.Xna.Framework;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// The one place allowed to raise <see cref="ModEvents"/>. Keeping the raisers internal to
    /// the API assembly and funnelling them through here means a mod can subscribe to events but
    /// cannot fire them at other mods.
    /// </summary>
    internal static class ModEventBridge
    {
        /// <summary>Routes handler exceptions into the log instead of the game loop.</summary>
        public static void Install()
        {
            ModEvents.HandlerFailed = (source, exception) =>
                Log.Error("Events", $"Unhandled exception in {source}.", exception);
        }

        public static void RaiseGameReady() => ModEvents.RaiseGameReady();

        public static void RaiseUpdateStarted(GameTime time) => ModEvents.RaiseUpdateStarted(time);

        public static void RaiseUpdateEnded(GameTime time) => ModEvents.RaiseUpdateEnded(time);

        public static void RaiseDrawEnded(GameTime time) => ModEvents.RaiseDrawEnded(time);

        public static void RaiseGameStateChanged(string previous, string current)
        {
            Log.Debug("Events", $"Game state: {previous ?? "?"} -> {current ?? "?"}");
            ModEvents.RaiseGameStateChanged(previous, current);
        }

        public static void RaiseShuttingDown() => ModEvents.RaiseShuttingDown();
    }
}
