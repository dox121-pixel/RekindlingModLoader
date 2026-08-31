using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// The loader's own Harmony patches: the ones that turn the game's internals into the events
    /// and asset overrides exposed by the API.
    /// </summary>
    /// <remarks>
    /// Every patch is applied individually and defensively. If a game update renames or removes
    /// a method, that one hook logs a warning and the rest keep working, rather than the whole
    /// loader failing and taking every mod with it.
    /// </remarks>
    internal static class GameHooks
    {
        private const string HarmonyId = "rekindling.modloader.core";
        private const string GameTypeName = "ZTD.Game1";

        private static Harmony _harmony;
        private static AssetOverrideRegistry _assets;
        private static FieldInfo _gameStateField;

        /// <summary>Raised after <c>Game1.Initialize</c> so the host can notify mods.</summary>
        internal static Action GameReady;

        /// <summary>Raised during <c>Game1.UnloadContent</c>.</summary>
        internal static Action ShuttingDown;

        public static void Apply(AssetOverrideRegistry assets)
        {
            _assets = assets;
            _harmony = new Harmony(HarmonyId);

            PatchContentPipeline();
            PatchGameLifecycle();
        }

        public static void Remove()
        {
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
            }
            catch (Exception ex)
            {
                Log.Warn("Hooks", $"Could not remove loader patches: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- content

        private static void PatchContentPipeline()
        {
            MethodInfo target = AccessTools.Method(
                typeof(ContentManager), "OpenStream", new[] { typeof(string) });

            if (target == null)
            {
                Log.Error("Hooks",
                    "Could not find ContentManager.OpenStream. Asset replacement is disabled; " +
                    "mods that only change code will still work.");
                return;
            }

            Patch(target, prefix: nameof(OpenStreamPrefix), description: "asset replacement");
        }

        /// <summary>
        /// Substitutes a mod-supplied stream for a content load. Returning <c>false</c> skips the
        /// game's own file lookup.
        /// </summary>
        private static bool OpenStreamPrefix(ContentManager __instance, string assetName, ref Stream __result)
        {
            try
            {
                Stream replacement = _assets?.TryOpen(__instance?.RootDirectory, assetName);
                if (replacement == null)
                    return true; // No override; let the game load its own asset.

                __result = replacement;
                return false;
            }
            catch (Exception ex)
            {
                // Never let a loader bug break content loading.
                Log.Error("Hooks", $"Asset override check failed for '{assetName}'.", ex);
                return true;
            }
        }

        // --------------------------------------------------------------- lifecycle

        private static void PatchGameLifecycle()
        {
            Type game = AccessTools.TypeByName(GameTypeName);
            if (game == null)
            {
                Log.Error("Hooks",
                    $"Could not find {GameTypeName}. Lifecycle events (GameReady, UpdateStarted, " +
                    "DrawEnded, GameStateChanged) will not fire. The game itself is unaffected.");
                return;
            }

            Patch(AccessTools.Method(game, "Initialize"),
                postfix: nameof(InitializePostfix), description: "ModEvents.GameReady");

            Patch(AccessTools.Method(game, "Update", new[] { typeof(GameTime) }),
                prefix: nameof(UpdatePrefix), postfix: nameof(UpdatePostfix),
                description: "ModEvents.UpdateStarted / UpdateEnded");

            Patch(AccessTools.Method(game, "Draw", new[] { typeof(GameTime) }),
                postfix: nameof(DrawPostfix), description: "ModEvents.DrawEnded");

            Patch(AccessTools.Method(game, "UnloadContent"),
                prefix: nameof(UnloadContentPrefix), description: "ModEvents.ShuttingDown");

            // setupMainMenu lives on MainMenu, not Game1.
            Type mainMenu = AccessTools.TypeByName("ZTD.MainMenu");
            Patch(AccessTools.Method(mainMenu, "setupMainMenu"),
                postfix: nameof(SetupMainMenuPostfix), description: "co-op guard");

            PatchGameState(game);
            PatchWorldSimulation();
        }

        private static void PatchGameState(Type game)
        {
            // Read the backing field directly rather than declaring the enum-typed setter
            // parameter, which would need a compile-time reference to the game assembly.
            _gameStateField = AccessTools.Field(game, "_gameState");

            PropertyInfo property = AccessTools.Property(game, "cGameState");
            MethodInfo setter = property?.GetSetMethod(nonPublic: true);

            if (_gameStateField == null || setter == null)
            {
                Log.Warn("Hooks", "Could not hook Game1.cGameState; ModEvents.GameStateChanged will not fire.");
                return;
            }

            Patch(setter,
                prefix: nameof(GameStatePrefix), postfix: nameof(GameStatePostfix),
                description: "ModEvents.GameStateChanged");
        }

        // ------------------------------------------------------------------- world

        /// <summary>
        /// Hooks the tile simulation, which is where most of the game's logic actually runs.
        /// </summary>
        /// <remarks>
        /// Suggested by the game's developer as the place worth exposing: the world sweeps a
        /// 20x20 window of tiles every few frames rather than touching everything every frame,
        /// and that sweep is what drives world behaviour.
        /// </remarks>
        private static void PatchWorldSimulation()
        {
            Type tile = AccessTools.TypeByName("ZTD.Tile");
            Type world = AccessTools.TypeByName("ZTD.World");

            Patch(AccessTools.Method(tile, "passiveUpdates"),
                postfix: nameof(TilePassiveUpdatePostfix), description: "WorldEvents.TileUpdate");

            Patch(AccessTools.Method(world, "update"),
                prefix: nameof(WorldUpdatePrefix), postfix: nameof(WorldUpdatePostfix),
                description: "WorldEvents.TickStarted / TickEnded");

            Patch(AccessTools.Method(world, "UpdateSingleTile"),
                postfix: nameof(UpdateSingleTilePostfix), description: "WorldEvents.TileChanged");
        }

        /// <summary>
        /// Runs for every tile of every sweep - measured at roughly twenty thousand times a
        /// second at 3x game speed. The
        /// subscriber check comes first so that an unsubscribed event costs one field read and
        /// a branch, and the context struct is only built when somebody is actually listening.
        /// </summary>
        private static void TilePassiveUpdatePostfix(object __instance, object allTiles, object survman, object creatman)
        {
            if (!WorldEvents.HasTileUpdateSubscribers)
                return;

            WorldEvents.RaiseTileUpdate(new TileUpdateContext(__instance, allTiles, survman, creatman));
        }

        private static void WorldUpdatePrefix(object __instance, int updateFrame, object survman, object creatman)
        {
            if (!WorldEvents.HasTickSubscribers)
                return;

            WorldEvents.RaiseTickStarted(new WorldTickContext(__instance, updateFrame, survman, creatman));
        }

        private static void WorldUpdatePostfix(object __instance, int updateFrame, object survman, object creatman)
        {
            if (!WorldEvents.HasTickSubscribers)
                return;

            WorldEvents.RaiseTickEnded(new WorldTickContext(__instance, updateFrame, survman, creatman));
        }

        private static void UpdateSingleTilePostfix(object __instance, object tIO, int x, int y)
        {
            if (!WorldEvents.HasTileChangedSubscribers)
                return;

            WorldEvents.RaiseTileChanged(new TileChangedContext(__instance, tIO, x, y));
        }

        private static void InitializePostfix()
        {
            try
            {
                GameReady?.Invoke();
                ModEventBridge.RaiseGameReady();
            }
            catch (Exception ex)
            {
                Log.Error("Hooks", "Failed while raising GameReady.", ex);
            }
        }

        private static void UpdatePrefix(GameTime gameTime)
        {
            // Re-asserted every frame so nothing the game does later can re-enable co-op.
            MultiplayerGuard.Apply();

            // Persists option changes once they have settled, so dragging a control does not
            // write the file every frame.
            ModLoaderHost.FlushOptions();
            ModEventBridge.RaiseUpdateStarted(gameTime);
        }

        /// <summary>The Co-op icons only exist once the main menu has loaded its content.</summary>
        private static void SetupMainMenuPostfix() => MultiplayerGuard.DimCoopIcons();

        private static void UpdatePostfix(GameTime gameTime) => ModEventBridge.RaiseUpdateEnded(gameTime);

        private static void DrawPostfix(GameTime gameTime) => ModEventBridge.RaiseDrawEnded(gameTime);

        private static void UnloadContentPrefix()
        {
            try
            {
                ModEventBridge.RaiseShuttingDown();
                ShuttingDown?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("Hooks", "Failed while raising ShuttingDown.", ex);
            }
        }

        private static void GameStatePrefix(out string __state) => __state = ReadGameState();

        private static void GameStatePostfix(string __state)
        {
            string current = ReadGameState();
            if (!string.Equals(__state, current, StringComparison.Ordinal))
                ModEventBridge.RaiseGameStateChanged(__state, current);
        }

        private static string ReadGameState()
        {
            try
            {
                return _gameStateField?.GetValue(null)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ helper

        private static void Patch(MethodBase target, string prefix = null, string postfix = null, string description = null)
        {
            if (target == null)
            {
                Log.Warn("Hooks", $"Skipped a hook for {description}: the target method no longer exists.");
                return;
            }

            try
            {
                _harmony.Patch(
                    target,
                    prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(GameHooks), prefix)),
                    postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(GameHooks), postfix)));

                Log.Debug("Hooks", $"Hooked {target.DeclaringType?.Name}.{target.Name} for {description}.");
            }
            catch (Exception ex)
            {
                Log.Error("Hooks", $"Could not hook {target.DeclaringType?.Name}.{target.Name} ({description}).", ex);
            }
        }
    }
}
