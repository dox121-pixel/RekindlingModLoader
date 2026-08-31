using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Rekindling.ModLoader.Tests
{
    // Stand-ins shaped like the game's own types. The real ZTD.Tile is not available here, and
    // the point of these tests is the plumbing, which does not care what the objects actually are.
    internal sealed class FakeSurvivorManager { }

    internal sealed class FakeCreatureManager { }

    internal sealed class FakeTile
    {
        public string Name;

        // Same shape as ZTD.Tile.passiveUpdates(Tile[,], SurviorManager, CreatureManager).
        //
        // NoInlining is load-bearing. This body is empty, so in a Release build the JIT inlines
        // it into the caller and the Harmony patch never runs - the test would report that
        // injection is broken when it is fine. The real passiveUpdates is far too large to be
        // inlined, which is why the game hooks work. Worth knowing before hooking any very small
        // game method.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void passiveUpdates(FakeTile[,] allTiles, FakeSurvivorManager survman, FakeCreatureManager creatman)
        {
            Name = Name ?? string.Empty;
        }
    }

    /// <summary>
    /// Captures what Harmony hands a postfix whose parameters are declared as <see cref="object"/>.
    /// </summary>
    internal static class InjectionProbe
    {
        public static object Instance;
        public static object AllTiles;
        public static object SurvivorManager;
        public static object CreatureManager;
        public static int Calls;

        public static void Reset()
        {
            Instance = AllTiles = SurvivorManager = CreatureManager = null;
            Calls = 0;
        }

        // Deliberately mirrors GameHooks.TilePassiveUpdatePostfix: every game type is declared
        // as object, because the loader does not reference the game assembly.
        public static void Postfix(object __instance, object allTiles, object survman, object creatman)
        {
            Instance = __instance;
            AllTiles = allTiles;
            SurvivorManager = survman;
            CreatureManager = creatman;
            Calls++;
        }
    }

    internal static class WorldEventTests
    {
        public static void Run(Action<string> section, Action<bool, string> isTrue, Action<object, object, string> areEqual)
        {
            section("Harmony object-parameter injection");

            // The whole world-event design rests on this: the loader has no compile-time
            // reference to the game, so its hooks declare every game type as object. If Harmony
            // did not bind those, the hooks would silently never fire.
            var harmony = new Harmony("rekindling.modloader.tests.injection");
            InjectionProbe.Reset();

            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(FakeTile), nameof(FakeTile.passiveUpdates)),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(InjectionProbe), nameof(InjectionProbe.Postfix))));

                var tile = new FakeTile { Name = "target" };
                var grid = new FakeTile[2, 2];
                var survivors = new FakeSurvivorManager();
                var creatures = new FakeCreatureManager();

                tile.passiveUpdates(grid, survivors, creatures);

                areEqual(1, InjectionProbe.Calls, "postfix ran");
                isTrue(ReferenceEquals(InjectionProbe.Instance, tile), "__instance bound to an object parameter");
                isTrue(ReferenceEquals(InjectionProbe.AllTiles, grid), "array argument bound to an object parameter");
                isTrue(ReferenceEquals(InjectionProbe.SurvivorManager, survivors), "survman bound by name");
                isTrue(ReferenceEquals(InjectionProbe.CreatureManager, creatures), "creatman bound by name");
            }
            finally
            {
                harmony.UnpatchAll("rekindling.modloader.tests.injection");
            }

            // ---------------------------------------------------------------- raising

            section("World event dispatch");

            isTrue(!WorldEvents.HasTileUpdateSubscribers, "no subscribers by default");

            var seen = new List<string>();

            void Observer(in TileUpdateContext context)
                => seen.Add(((FakeTile)context.Tile).Name);

            WorldEvents.TileUpdate += Observer;
            try
            {
                isTrue(WorldEvents.HasTileUpdateSubscribers, "subscribing is visible to the hook's fast path");

                WorldEvents.RaiseTileUpdate(new TileUpdateContext(
                    new FakeTile { Name = "a" }, null, null, null));

                areEqual(1, seen.Count, "handler received the event");
                areEqual("a", seen[0], "context carried the tile through");
            }
            finally
            {
                WorldEvents.TileUpdate -= Observer;
            }

            isTrue(!WorldEvents.HasTileUpdateSubscribers, "unsubscribing is visible again");

            // ------------------------------------------------------- fault isolation

            section("World event fault handling");

            var failures = new List<string>();
            WorldEvents.HandlerFailed = (source, ex) => failures.Add(source);

            int goodCalls = 0;
            int badCalls = 0;

            void Throws(in TileUpdateContext context)
            {
                badCalls++;
                throw new InvalidOperationException("deliberate");
            }

            void Fine(in TileUpdateContext context) => goodCalls++;

            WorldEvents.TileUpdate += Throws;
            WorldEvents.TileUpdate += Fine;

            try
            {
                var context = new TileUpdateContext(new FakeTile { Name = "x" }, null, null, null);

                WorldEvents.RaiseTileUpdate(context);
                areEqual(1, badCalls, "faulting handler ran once");
                areEqual(1, goodCalls, "a sibling handler still ran after one threw");
                areEqual(1, failures.Count, "the fault was reported");

                // TileUpdate fires thousands of times a second, so a handler that throws is
                // removed rather than allowed to flood the log for the rest of the session.
                WorldEvents.RaiseTileUpdate(context);
                areEqual(1, badCalls, "faulting handler was unsubscribed after its first throw");
                areEqual(2, goodCalls, "the healthy handler kept receiving events");
            }
            finally
            {
                WorldEvents.TileUpdate -= Throws;
                WorldEvents.TileUpdate -= Fine;
                WorldEvents.HandlerFailed = null;
            }

            // The coarser events fire rarely, so a fault there is logged but not disabled.
            failures.Clear();
            WorldEvents.HandlerFailed = (source, ex) => failures.Add(source);

            int tickThrows = 0;

            void ThrowingTick(in WorldTickContext context)
            {
                tickThrows++;
                throw new InvalidOperationException("deliberate");
            }

            WorldEvents.TickEnded += ThrowingTick;
            try
            {
                var context = new WorldTickContext(null, 1, null, null);

                WorldEvents.RaiseTickEnded(context);
                WorldEvents.RaiseTickEnded(context);

                areEqual(2, tickThrows, "a faulting tick handler stays subscribed");
                areEqual(2, failures.Count, "and each fault is reported");
            }
            finally
            {
                WorldEvents.TickEnded -= ThrowingTick;
                WorldEvents.HandlerFailed = null;
            }
        }
    }
}
