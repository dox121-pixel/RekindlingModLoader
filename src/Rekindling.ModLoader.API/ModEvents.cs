using System;
using Microsoft.Xna.Framework;

namespace Rekindling.ModLoader
{
    public sealed class GameStateChangedEventArgs : EventArgs
    {
        public GameStateChangedEventArgs(string previous, string current)
        {
            Previous = previous;
            Current = current;
        }

        /// <summary>Name of the previous <c>ZTD.Game1.Gamestate</c> value, e.g. <c>menu</c>.</summary>
        public string Previous { get; }

        /// <summary>Name of the new <c>ZTD.Game1.Gamestate</c> value, e.g. <c>game</c>.</summary>
        public string Current { get; }

        public override string ToString() => $"{Previous} -> {Current}";
    }

    public sealed class GameTickEventArgs : EventArgs
    {
        public GameTickEventArgs(GameTime gameTime) => GameTime = gameTime;

        public GameTime GameTime { get; }
    }

    /// <summary>
    /// Game lifecycle events, so the common cases don't each need their own Harmony patch.
    /// Subscribe from <c>OnLoad</c>.
    /// </summary>
    /// <remarks>
    /// Handlers run on the game's main thread, inside the game loop. Exceptions thrown by a
    /// handler are caught and logged rather than propagated — one misbehaving mod must not take
    /// the game down — but a slow handler will still stall the frame, so keep them cheap.
    /// </remarks>
    public static class ModEvents
    {
        /// <summary>Raised after <c>Game1.Initialize</c>, once the graphics device exists.</summary>
        public static event EventHandler GameReady;

        /// <summary>Raised at the top of every <c>Game1.Update</c>, before the game updates.</summary>
        public static event EventHandler<GameTickEventArgs> UpdateStarted;

        /// <summary>Raised at the end of every <c>Game1.Update</c>, after the game has updated.</summary>
        public static event EventHandler<GameTickEventArgs> UpdateEnded;

        /// <summary>Raised at the end of every <c>Game1.Draw</c>, after the game has drawn its frame.</summary>
        public static event EventHandler<GameTickEventArgs> DrawEnded;

        /// <summary>Raised whenever <c>Game1.cGameState</c> changes (menu, game, science, pause...).</summary>
        public static event EventHandler<GameStateChangedEventArgs> GameStateChanged;

        /// <summary>Raised during <c>Game1.UnloadContent</c>, before mods are unloaded.</summary>
        public static event EventHandler ShuttingDown;

        // The loader owns raising these; mods only subscribe. Each raiser swallows and reports
        // handler exceptions via the callback the loader installs.
        internal static Action<string, Exception> HandlerFailed;

        private static void Raise(string name, Delegate handlers, object sender, EventArgs args)
        {
            if (handlers == null)
                return;

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    handler.DynamicInvoke(sender, args);
                }
                catch (Exception ex)
                {
                    // Unwrap the reflection wrapper so the log shows the real fault site.
                    Exception real = (ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex;
                    HandlerFailed?.Invoke($"{name} handler in {Describe(handler)}", real);
                }
            }
        }

        private static string Describe(Delegate handler)
            => handler.Method?.DeclaringType?.Assembly?.GetName()?.Name ?? "<unknown assembly>";

        internal static void RaiseGameReady() => Raise(nameof(GameReady), GameReady, null, EventArgs.Empty);

        internal static void RaiseUpdateStarted(GameTime time)
            => Raise(nameof(UpdateStarted), UpdateStarted, null, new GameTickEventArgs(time));

        internal static void RaiseUpdateEnded(GameTime time)
            => Raise(nameof(UpdateEnded), UpdateEnded, null, new GameTickEventArgs(time));

        internal static void RaiseDrawEnded(GameTime time)
            => Raise(nameof(DrawEnded), DrawEnded, null, new GameTickEventArgs(time));

        internal static void RaiseGameStateChanged(string previous, string current)
            => Raise(nameof(GameStateChanged), GameStateChanged, null, new GameStateChangedEventArgs(previous, current));

        internal static void RaiseShuttingDown() => Raise(nameof(ShuttingDown), ShuttingDown, null, EventArgs.Empty);
    }
}
