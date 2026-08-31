using System;
using System.IO;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Convenience base class for mods. Handles the boilerplate of <see cref="IMod"/> so a mod
    /// only overrides what it actually needs.
    /// </summary>
    public abstract class ModBase : IMod
    {
        private IModContext _context;

        /// <summary>Everything the loader knows about this mod.</summary>
        protected IModContext Context
        {
            get
            {
                if (_context == null)
                    throw new InvalidOperationException(
                        "Context is not available until OnLoad has been called. " +
                        "Do not touch it from your constructor.");
                return _context;
            }
        }

        /// <summary>Writes to both the console and <c>Logs/modloader.log</c>, tagged with this mod's id.</summary>
        protected IModLogger Log => Context.Log;

        /// <summary>This mod's parsed <c>mod.json</c>.</summary>
        protected ModManifest Manifest => Context.Manifest;

        /// <summary>Absolute path to the folder containing this mod's <c>mod.json</c>.</summary>
        protected string ModDirectory => Context.ModDirectory;

        /// <summary>This mod's settings. Declare them in <c>OnLoad</c>.</summary>
        protected IModOptions Options => Context.Options;

        void IMod.OnLoad(IModContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            OnLoad();
        }

        /// <inheritdoc cref="IMod.OnLoad"/>
        protected virtual void OnLoad() { }

        /// <inheritdoc cref="IMod.OnGameReady"/>
        public virtual void OnGameReady() { }

        /// <inheritdoc cref="IMod.OnUnload"/>
        public virtual void OnUnload() { }

        /// <summary>
        /// Resolves a path relative to this mod's own folder.
        /// </summary>
        protected string PathIn(params string[] relativeParts)
        {
            string path = ModDirectory;
            foreach (string part in relativeParts)
                path = Path.Combine(path, part);
            return path;
        }
    }
}
