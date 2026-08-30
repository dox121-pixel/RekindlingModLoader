using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Disables co-op while mods are loaded, and makes that visible in the UI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is on by default because modded multiplayer is not merely unsupported, it is actively
    /// broken: the game synchronises simulation state across clients, so any mod that changes
    /// that state desyncs every player who is not running an identical mod set. Silently letting
    /// someone host a modded lobby produces confusing corruption rather than a clean failure.
    /// </para>
    /// <para>
    /// Most of the work is done by the game itself. <c>Options.allowMultiplayer</c> already gates
    /// the Join Game action, the Solo/Co-op toggle's click handling, and adds Join Game to the
    /// menu's disabled list. What it does not do is grey the toggle out - that is drawn unguarded
    /// - so the icons are dimmed and the label recoloured here to match.
    /// </para>
    /// <para>
    /// Pass <c>--allow-multiplayer</c> to the loader to turn all of this off.
    /// </para>
    /// </remarks>
    internal static class MultiplayerGuard
    {
        private const string HarmonyId = "rekindling.modloader.multiplayerguard";

        private static bool _active;
        private static bool _iconsDimmed;

        private static FieldInfo _allowMultiplayer;
        private static FieldInfo _selectedType;
        private static FieldInfo _joiningGame;
        private static FieldInfo _coopIconSelected;
        private static FieldInfo _coopIconUnselected;

        private static object _soloGameType;

        /// <summary>Colour the Co-op label is forced to while co-op is disabled.</summary>
        private static readonly Color DisabledLabel = new Color(110, 110, 110);

        public static bool IsActive => _active;

        public static void Initialize(bool allowMultiplayer)
        {
            if (allowMultiplayer)
            {
                Log.Info("Multiplayer", "Co-op left enabled by --allow-multiplayer. Mods will desync online play.");
                return;
            }

            Type options = AccessTools.TypeByName("ZTD.Options");
            Type mainMenu = AccessTools.TypeByName("ZTD.MainMenu");
            Type steamHelper = AccessTools.TypeByName("FreakingSweet.SteamMultiplayerHelper");

            _allowMultiplayer = options == null ? null : AccessTools.Field(options, "allowMultiplayer");

            if (_allowMultiplayer == null)
            {
                Log.Warn("Multiplayer", "Could not find Options.allowMultiplayer; co-op has been left enabled.");
                return;
            }

            _selectedType = mainMenu == null ? null : AccessTools.Field(mainMenu, "selectedType");
            _joiningGame = steamHelper == null ? null : AccessTools.Field(steamHelper, "JoiningGame");
            _coopIconSelected = mainMenu == null ? null : AccessTools.Field(mainMenu, "multiIconSelected");
            _coopIconUnselected = mainMenu == null ? null : AccessTools.Field(mainMenu, "multiIconUnSelected");

            // Options.GameType.Solo, so a stale Co-op selection cannot survive into a new game.
            Type gameType = options.GetNestedType("GameType", BindingFlags.Public | BindingFlags.NonPublic);
            if (gameType != null && gameType.IsEnum)
                _soloGameType = Enum.Parse(gameType, "Solo");

            _active = true;
            Apply();
            PatchCoopLabel();

            Log.Info("Multiplayer", "Co-op disabled: mods desync online play. Pass --allow-multiplayer to override.");
        }

        /// <summary>
        /// Re-asserts the flags. Cheap enough to call every frame, which guarantees nothing the
        /// game does later can quietly turn co-op back on.
        /// </summary>
        public static void Apply()
        {
            if (!_active)
                return;

            try
            {
                _allowMultiplayer.SetValue(null, false);

                if (_selectedType != null && _soloGameType != null)
                    _selectedType.SetValue(null, _soloGameType);

                // A Steam invite would otherwise push the game straight into a join.
                _joiningGame?.SetValue(null, false);
            }
            catch (Exception ex)
            {
                _active = false;
                Log.Error("Multiplayer", "Failed to keep co-op disabled; giving up on the guard.", ex);
            }
        }

        /// <summary>
        /// Dims the Co-op icons once the main menu has loaded them. Called after
        /// <c>MainMenu.setupMainMenu</c>, which is where those textures come into existence.
        /// </summary>
        public static void DimCoopIcons()
        {
            if (!_active || _iconsDimmed)
                return;

            _iconsDimmed = true;

            try
            {
                Dim(_coopIconSelected);
                Dim(_coopIconUnselected);
            }
            catch (Exception ex)
            {
                // Purely cosmetic - co-op is already unusable either way.
                Log.Warn("Multiplayer", $"Could not dim the Co-op icons: {ex.Message}");
            }
        }

        /// <summary>
        /// Replaces a texture with a desaturated, darkened copy.
        /// </summary>
        /// <remarks>
        /// A copy is made rather than mutating the original, because the content manager caches
        /// textures and the same instance may be shared elsewhere.
        /// </remarks>
        private static void Dim(FieldInfo field)
        {
            if (!(field?.GetValue(null) is Texture2D source))
                return;

            var pixels = new Color[source.Width * source.Height];
            source.GetData(pixels);

            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                if (c.A == 0)
                    continue;

                // Luminance, then pull it well down. Values stay premultiplied-consistent
                // because alpha is untouched and the channels only ever get darker.
                byte grey = (byte)((c.R * 30 + c.G * 59 + c.B * 11) / 100 * 0.45f);
                pixels[i] = new Color(grey, grey, grey, c.A);
            }

            var dimmed = new Texture2D(source.GraphicsDevice, source.Width, source.Height);
            dimmed.SetData(pixels);
            field.SetValue(null, dimmed);
        }

        /// <summary>
        /// Greys the "Co-op" label, which the game draws unconditionally in bright green.
        /// </summary>
        private static void PatchCoopLabel()
        {
            Type betterFonts = AccessTools.TypeByName("FreakingSweet.BetterFonts");
            if (betterFonts == null)
                return;

            // Two overloads share the name; the wanted one takes a rotation float in slot 5,
            // where the other has the "center" bool.
            MethodInfo target = betterFonts
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                    m.Name == "drawWithShadow" &&
                    m.GetParameters().Length > 6 &&
                    m.GetParameters()[5].ParameterType == typeof(float));

            if (target == null)
            {
                Log.Debug("Multiplayer", "Could not find the label draw method; the Co-op text stays green.");
                return;
            }

            try
            {
                new Harmony(HarmonyId).Patch(
                    target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(MultiplayerGuard), nameof(CoopLabelPrefix))));
            }
            catch (Exception ex)
            {
                Log.Warn("Multiplayer", $"Could not recolour the Co-op label: {ex.Message}");
            }
        }

        /// <summary>Recolours just the one label, leaving every other string untouched.</summary>
        private static void CoopLabelPrefix(string txt, ref Color c)
        {
            if (_active && txt == "Co-op")
                c = DisabledLabel;
        }
    }
}
