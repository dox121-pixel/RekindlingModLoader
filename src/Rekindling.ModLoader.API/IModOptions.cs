using System;
using System.Collections.Generic;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// A mod's settings. Declare them in <c>OnLoad</c>; the loader restores saved values as each
    /// one is declared, and persists them when they change.
    /// </summary>
    /// <example>
    /// <code>
    /// private ChoiceOption _position;
    ///
    /// protected override void OnLoad()
    /// {
    ///     _position = Options.Choice("buttons", "Button position",
    ///         new[] { "Middle left", "Centre", "Middle right" }, "Middle left");
    /// }
    /// </code>
    /// Read <c>_position.Value</c> whenever you need it, or subscribe to its
    /// <see cref="ModOption.Changed"/> event to react immediately.
    /// </example>
    public interface IModOptions
    {
        /// <summary>The mod these options belong to.</summary>
        string ModId { get; }

        /// <summary>Every option this mod has declared, in declaration order.</summary>
        IReadOnlyList<ModOption> All { get; }

        /// <summary>Declares an on/off setting.</summary>
        ToggleOption Toggle(string key, string label, bool defaultValue, string description = null);

        /// <summary>Declares a pick-one-from-a-list setting.</summary>
        ChoiceOption Choice(string key, string label, IEnumerable<string> choices, string defaultValue, string description = null);

        /// <summary>Declares a number within a range.</summary>
        SliderOption Slider(string key, string label, float minimum, float maximum, float defaultValue,
            float step = 1f, string description = null);

        /// <summary>
        /// Declares a screen position, stored as a fraction of the screen so it survives a
        /// resolution change. Intended to be set by dragging.
        /// </summary>
        PointOption Point(string key, string label, float defaultX, float defaultY, string description = null);

        /// <summary>Looks up a declared option by key, or <c>null</c>.</summary>
        ModOption Find(string key);

        /// <summary>
        /// Writes the current values to disk. The loader also saves automatically shortly after a
        /// change, so calling this is only needed if you want it written right now.
        /// </summary>
        void Save();

        /// <summary>Restores every option to its default.</summary>
        void ResetAll();

        /// <summary>Raised when any option belonging to this mod changes.</summary>
        event Action<ModOption> Changed;
    }

    /// <summary>
    /// Every mod's options, for whichever mod is providing the settings UI.
    /// </summary>
    /// <remarks>
    /// A mod does not have to draw anything to be configurable: it declares options, and any mod
    /// that wants to can render them. Enumerate this to build a settings screen covering
    /// everything installed.
    /// </remarks>
    public static class ModOptionsRegistry
    {
        private static readonly List<IModOptions> Registered = new List<IModOptions>();
        private static readonly object Gate = new object();

        /// <summary>Options for every mod that declared any, in load order.</summary>
        public static IReadOnlyList<IModOptions> All
        {
            get
            {
                lock (Gate)
                    return Registered.ToArray();
            }
        }

        /// <summary>Options for one mod, or <c>null</c> when it declared none.</summary>
        public static IModOptions ForMod(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId))
                return null;

            lock (Gate)
            {
                foreach (IModOptions options in Registered)
                {
                    if (string.Equals(options.ModId, modId, StringComparison.OrdinalIgnoreCase))
                        return options;
                }
            }

            return null;
        }

        /// <summary>Raised when any option of any mod changes, so a UI can refresh itself.</summary>
        public static event Action<IModOptions, ModOption> AnyChanged;

        internal static void Register(IModOptions options)
        {
            if (options == null)
                return;

            lock (Gate)
            {
                if (!Registered.Contains(options))
                    Registered.Add(options);
            }

            options.Changed += option => AnyChanged?.Invoke(options, option);
        }

        internal static void Clear()
        {
            lock (Gate)
                Registered.Clear();
        }
    }
}
