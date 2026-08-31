using System;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// What a tile passive update is handed. Passed by reference as a struct, so subscribing to
    /// <see cref="WorldEvents.TileUpdate"/> costs no allocation per tile.
    /// </summary>
    /// <remarks>
    /// The members are typed as <see cref="object"/> because this assembly deliberately does not
    /// reference the game. Cast them in your handler:
    /// <code>
    /// var tile = (ZTD.Tile)context.Tile;
    /// var all  = (ZTD.Tile[,])context.AllTiles;
    /// </code>
    /// </remarks>
    public readonly struct TileUpdateContext
    {
        public TileUpdateContext(object tile, object allTiles, object survivorManager, object creatureManager)
        {
            Tile = tile;
            AllTiles = allTiles;
            SurvivorManager = survivorManager;
            CreatureManager = creatureManager;
        }

        /// <summary>The tile being updated. Cast to <c>ZTD.Tile</c>.</summary>
        public object Tile { get; }

        /// <summary>The whole tile grid. Cast to <c>ZTD.Tile[,]</c>.</summary>
        public object AllTiles { get; }

        /// <summary>Cast to <c>ZTD.SurviorManager</c> (the game's spelling).</summary>
        public object SurvivorManager { get; }

        /// <summary>Cast to <c>ZTD.CreatureManager</c>.</summary>
        public object CreatureManager { get; }
    }

    /// <summary>Context for the start and end of a world tick.</summary>
    public readonly struct WorldTickContext
    {
        public WorldTickContext(object world, int speedStep, object survivorManager, object creatureManager)
        {
            World = world;
            SpeedStep = speedStep;
            SurvivorManager = survivorManager;
            CreatureManager = creatureManager;
        }

        /// <summary>Cast to <c>ZTD.World</c>.</summary>
        public object World { get; }

        /// <summary>
        /// Which step of the current frame this tick is, counting from zero.
        /// </summary>
        /// <remarks>
        /// The game runs the world <c>speed</c> times per frame, so at 3x game speed a single
        /// frame produces ticks 0, 1 and 2. It is <b>not</b> a frame counter and it does not
        /// increase over time - do not try to rate-limit with it. Keep your own counter instead.
        /// It is useful for telling how fast the game is currently running, or for doing
        /// something only once per frame regardless of speed (<c>SpeedStep == 0</c>).
        /// </remarks>
        public int SpeedStep { get; }

        /// <summary>Cast to <c>ZTD.SurviorManager</c>.</summary>
        public object SurvivorManager { get; }

        /// <summary>Cast to <c>ZTD.CreatureManager</c>.</summary>
        public object CreatureManager { get; }
    }

    /// <summary>Context for a single tile being replaced wholesale.</summary>
    public readonly struct TileChangedContext
    {
        public TileChangedContext(object world, object tileInfo, int x, int y)
        {
            World = world;
            TileInfo = tileInfo;
            X = x;
            Y = y;
        }

        /// <summary>Cast to <c>ZTD.World</c>.</summary>
        public object World { get; }

        /// <summary>The incoming tile data. Cast to <c>ZTD.TileInfo</c>.</summary>
        public object TileInfo { get; }

        public int X { get; }
        public int Y { get; }
    }

    public delegate void TileUpdateHandler(in TileUpdateContext context);

    public delegate void WorldTickHandler(in WorldTickContext context);

    public delegate void TileChangedHandler(in TileChangedContext context);

    /// <summary>
    /// Hooks into the game's world simulation, where most gameplay logic actually happens.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The game does not update every tile every frame. It sweeps a 20x20 window of tiles roughly
    /// every third frame, advancing the window across the map - which is how it keeps the tile
    /// simulation affordable on a large world. <see cref="TileUpdate"/> rides that sweep, so it
    /// is the right place for anything that needs to act on the world over time: a new kind of
    /// active object, a custom growth or decay rule, periodic checks against nearby tiles.
    /// </para>
    /// <para>
    /// <b>This is a hot path.</b> Measured at around twenty thousand calls a second at 3x game
    /// speed - the world runs once per speed step, so the rate scales with how fast the player
    /// has the game running. Handlers are invoked from a cached array with no per-call
    /// allocation, and the whole thing costs nothing at all when nobody is subscribed - but what
    /// your handler does is on you. Keep it cheap, and do real work on your own schedule using
    /// <see cref="TickStarted"/> and <see cref="TickEnded"/> to batch it.
    /// </para>
    /// <para>
    /// Because <see cref="TileUpdate"/> runs so often, a handler that throws is logged in full
    /// and then <b>unsubscribed</b>. One that faults on the first tile would otherwise fault on
    /// every tile and write gigabytes of log within a minute. The coarser events log and carry on.
    /// </para>
    /// <para>
    /// <see cref="TickStarted"/> and <see cref="TickEnded"/> also fire per speed step rather than
    /// per frame, so they run several times a frame at higher game speeds.
    /// </para>
    /// <para>
    /// Not to be confused with <see cref="ModEvents.UpdateStarted"/>, which fires once per
    /// rendered frame. These fire on the simulation's own cadence.
    /// </para>
    /// </remarks>
    public static class WorldEvents
    {
        private static readonly TileUpdateHandler[] NoTileHandlers = new TileUpdateHandler[0];
        private static readonly WorldTickHandler[] NoTickHandlers = new WorldTickHandler[0];
        private static readonly TileChangedHandler[] NoChangeHandlers = new TileChangedHandler[0];

        private static readonly object Gate = new object();

        private static TileUpdateHandler _tileUpdate;
        private static WorldTickHandler _tickStarted;
        private static WorldTickHandler _tickEnded;
        private static TileChangedHandler _tileChanged;

        // Invocation lists are cached rather than rebuilt per raise. GetInvocationList allocates,
        // and at this call rate that alone would be a measurable amount of garbage.
        private static TileUpdateHandler[] _tileUpdateList = NoTileHandlers;
        private static WorldTickHandler[] _tickStartedList = NoTickHandlers;
        private static WorldTickHandler[] _tickEndedList = NoTickHandlers;
        private static TileChangedHandler[] _tileChangedList = NoChangeHandlers;

        /// <summary>
        /// Raised for every tile the game passively updates, as it sweeps the map. The main
        /// injection point for gameplay logic. See the remarks on <see cref="WorldEvents"/>
        /// before subscribing - this runs thousands of times a second.
        /// </summary>
        public static event TileUpdateHandler TileUpdate
        {
            add { lock (Gate) { _tileUpdate += value; _tileUpdateList = Snapshot(_tileUpdate, NoTileHandlers); } }
            remove { lock (Gate) { _tileUpdate -= value; _tileUpdateList = Snapshot(_tileUpdate, NoTileHandlers); } }
        }

        /// <summary>Raised before the world's update, once per world tick.</summary>
        public static event WorldTickHandler TickStarted
        {
            add { lock (Gate) { _tickStarted += value; _tickStartedList = Snapshot(_tickStarted, NoTickHandlers); } }
            remove { lock (Gate) { _tickStarted -= value; _tickStartedList = Snapshot(_tickStarted, NoTickHandlers); } }
        }

        /// <summary>
        /// Raised after the world's update. Together with <see cref="TickStarted"/> this is the
        /// natural place to rate-limit: accumulate cheaply during <see cref="TileUpdate"/>, then
        /// do the expensive part here, or only every Nth tick.
        /// </summary>
        public static event WorldTickHandler TickEnded
        {
            add { lock (Gate) { _tickEnded += value; _tickEndedList = Snapshot(_tickEnded, NoTickHandlers); } }
            remove { lock (Gate) { _tickEnded -= value; _tickEndedList = Snapshot(_tickEnded, NoTickHandlers); } }
        }

        /// <summary>
        /// Raised when a single tile is replaced outright, which is how the game applies tile
        /// changes received over the network. Much rarer than <see cref="TileUpdate"/>.
        /// </summary>
        public static event TileChangedHandler TileChanged
        {
            add { lock (Gate) { _tileChanged += value; _tileChangedList = Snapshot(_tileChanged, NoChangeHandlers); } }
            remove { lock (Gate) { _tileChanged -= value; _tileChangedList = Snapshot(_tileChanged, NoChangeHandlers); } }
        }

        /// <summary>True when anything is listening. The loader's hooks check this first.</summary>
        public static bool HasTileUpdateSubscribers => _tileUpdateList.Length > 0;

        public static bool HasTickSubscribers => _tickStartedList.Length > 0 || _tickEndedList.Length > 0;

        public static bool HasTileChangedSubscribers => _tileChangedList.Length > 0;

        private static T[] Snapshot<T>(Delegate source, T[] empty) where T : class
        {
            if (source == null)
                return empty;

            Delegate[] list = source.GetInvocationList();
            var typed = new T[list.Length];
            for (int i = 0; i < list.Length; i++)
                typed[i] = list[i] as T;

            return typed;
        }

        /// <summary>Reports a handler fault, and which assembly it came from.</summary>
        internal static Action<string, Exception> HandlerFailed;

        private static string Describe(Delegate handler)
            => handler?.Method?.DeclaringType?.Assembly?.GetName()?.Name ?? "<unknown assembly>";

        // ------------------------------------------------------------------- raising

        internal static void RaiseTileUpdate(in TileUpdateContext context)
        {
            TileUpdateHandler[] handlers = _tileUpdateList;

            for (int i = 0; i < handlers.Length; i++)
            {
                TileUpdateHandler handler = handlers[i];

                try
                {
                    handler(in context);
                }
                catch (Exception ex)
                {
                    // Unsubscribed rather than merely logged: at this call rate a fault repeats
                    // thousands of times a second, and the log would be useless within seconds.
                    TileUpdate -= handler;

                    HandlerFailed?.Invoke(
                        $"TileUpdate handler in {Describe(handler)} threw and has been disabled " +
                        "for the rest of this session. Fix it and restart the game.", ex);
                }
            }
        }

        internal static void RaiseTickStarted(in WorldTickContext context)
            => RaiseTick(_tickStartedList, in context, nameof(TickStarted));

        internal static void RaiseTickEnded(in WorldTickContext context)
            => RaiseTick(_tickEndedList, in context, nameof(TickEnded));

        private static void RaiseTick(WorldTickHandler[] handlers, in WorldTickContext context, string name)
        {
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i](in context);
                }
                catch (Exception ex)
                {
                    HandlerFailed?.Invoke($"{name} handler in {Describe(handlers[i])}", ex);
                }
            }
        }

        internal static void RaiseTileChanged(in TileChangedContext context)
        {
            TileChangedHandler[] handlers = _tileChangedList;

            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i](in context);
                }
                catch (Exception ex)
                {
                    HandlerFailed?.Invoke($"TileChanged handler in {Describe(handlers[i])}", ex);
                }
            }
        }
    }
}
