using System;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Implemented by the entry-point class of every mod. The loader looks for exactly one
    /// non-abstract implementation per mod assembly and instantiates it with a parameterless
    /// constructor.
    /// </summary>
    /// <remarks>
    /// Prefer deriving from <see cref="ModBase"/>, which implements this and gives you
    /// <c>Logger</c>, <c>Manifest</c> and <c>ModDirectory</c> for free.
    /// </remarks>
    public interface IMod
    {
        /// <summary>
        /// Called once, before the game's entry point runs. The graphics device, content
        /// manager and all <c>ZTD</c> static state do <b>not</b> exist yet, so do not touch
        /// them here. Use this for Harmony patches, config loading and event subscription.
        /// </summary>
        void OnLoad(IModContext context);

        /// <summary>
        /// Called once the game has finished <c>Game1.Initialize</c>, i.e. the graphics device
        /// and content pipeline are live. Safe place to load textures or read game statics.
        /// </summary>
        void OnGameReady();

        /// <summary>
        /// Called when the process is shutting down cleanly. Flush anything you own here.
        /// Not guaranteed to run if the game crashes hard.
        /// </summary>
        void OnUnload();
    }
}
