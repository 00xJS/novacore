// Smoke-test bridge from the pure Sim to Unity's runtime. Instantiates a fresh
// GameState, drives the TickEngine off Unity's wall clock, and dumps state to an
// IMGUI overlay. Phase B.4 will replace this with a proper UI Toolkit panel.
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Game Context")]
    public sealed class GameContext : MonoBehaviour
    {
        [SerializeField] int seed = 42;
        // Default OFF: with the bot galaxy live, per-event logging (string
        // interpolation + Unity's log overhead) is real per-frame cost on device.
        // Flip on in the Inspector when debugging sim behavior.
        [SerializeField] bool logEvents = false;

        /// <summary>Sim seed — drives map/sector determinism. Reflects the LIVE
        /// state (adopted saves carry their own seed).</summary>
        public int Seed => _state?.Seed ?? seed;

        /// <summary>
        /// Seed for the planet's LOOK, decoupled from the sim seed: every
        /// shared-galaxy account shares one sim seed (one map for everyone), so
        /// mixing in the home tile — spawn tiles are unique per account — gives
        /// every commander a visually distinct planet. Guests are unique via
        /// their random sim seed. Side effect embraced as a feature: relocating
        /// your home re-rolls the planet's look (SceneBootstrap watches for it).
        /// </summary>
        public int VisualSeed
        {
            get
            {
                if (_state == null) return seed;
                return _state.Seed
                    ^ unchecked(_state.HomeTile.X * (int)0x9E3779B1)
                    ^ unchecked(_state.HomeTile.Y * (int)0x85EBCA6B)
                    // Planetary Resurfacing item: a saved offset rerolls the look
                    // without touching the sim seed or the home tile.
                    ^ unchecked(_state.VisualSeedOffset * (int)0x27D4EB2F);
            }
        }

        /// <summary>Live sim state. Null before Awake completes. Guard callers accordingly.</summary>
        public GameState? State => _state;

        /// <summary>
        /// Tick with the sub-second fraction from the wall clock — for VISUALS ONLY
        /// (smooth march movement between 1 Hz sim ticks). The sim itself never
        /// sees fractional time.
        /// </summary>
        public double PreciseTick
        {
            get
            {
                if (_state == null) return 0;
                // Read the fraction straight off the TickEngine's own ms accumulator
                // — the old wall-clock read was anchored separately from the engine,
                // so any drift/hitch pinned it against the clamp and march dots
                // moved in 1 Hz hops ("skipping") instead of gliding.
                double frac = _engine?.TickFraction() ?? 0.0;
                return _state.Tick + System.Math.Clamp(frac, 0.0, 0.999);
            }
        }

        /// <summary>Event bus for read-only subscribers (BuildingMarkers, HUD, etc.).</summary>
        public SimEventBus? Events => _events;

        /// <summary>The simulated galaxy — the 99 AI commanders. Null until a game is adopted/founded.</summary>
        public GalaxyRoyale.Sim.Bots.BotGalaxy? Bots => _bots;

        GameState? _state;
        GalaxyRoyale.Sim.Bots.BotGalaxy? _bots;
        TickEngine? _engine;
        SimEventBus? _events;
        double _startTime;

        void Awake() => EnsureInit();

        // Idempotent re-init. Unity nulls private non-serialized fields on domain reload
        // (e.g. asset refresh during Play mode) without calling Awake again — so any of
        // Update/OnGUI/action buttons must be safe to call before the sim is set up.
        void EnsureInit()
        {
            if (_state != null && _engine != null && _events != null) return;
            _state = GameState.CreateNewGame(seed);
            _events = new SimEventBus();
            _engine = new TickEngine(_state, _events);
            _startTime = Time.timeAsDouble;
            _events.Subscribe(OnSimEvent);
            // The always-on UI overlay (header/nav/panels) rides the same GameObject.
            if (GetComponent<UI.UIController>() == null) gameObject.AddComponent<UI.UIController>();
            // Local persistence (save/load/backup + fresh-galaxy founding).
            if (GetComponent<LocalBootstrap>() == null) gameObject.AddComponent<LocalBootstrap>();
            // Local notifications for timer completions (B.6 retention hook).
            if (GetComponent<MarchNotifications>() == null) gameObject.AddComponent<MarchNotifications>();
            // Daily objectives tracker (retention loop).
            if (GetComponent<DailyObjectives>() == null) gameObject.AddComponent<DailyObjectives>();
            // Radar Station incoming-threat warnings (content expansion).
            if (GetComponent<RadarService>() == null) gameObject.AddComponent<RadarService>();
            // PvP resolve-at-arrival (raids/spies fight when the fleet lands).
            if (GetComponent<RaidArrivals>() == null) gameObject.AddComponent<RaidArrivals>();
            Debug.Log($"[GalaxyRoyale] New game seeded {seed}. Sector generated.");
        }

        /// <summary>
        /// Replace the running sim with a loaded save (cloud or local): fast-forwards
        /// offline time through the normal advance() loop, rebases the wall clock to
        /// the loaded tick, and resyncs state-derived visuals. The event bus survives
        /// the swap, so existing subscribers (UI, markers) keep working.
        /// </summary>
        public OfflineSummary AdoptState(GameState state, GalaxyRoyale.Sim.Bots.BotGalaxy bots, long savedAtMs)
        {
            EnsureInit();
            _state = state;
            _bots = bots;
            _engine = new TickEngine(_state, _events!);
            var summary = SaveManager.ApplyOfflineProgress(
                _state, _engine, _events!,
                savedAtMs, System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            // Bots catch up to the fast-forwarded player clock in the same
            // suppressed window (no retroactive raid toasts from hours ago).
            _events!.Suppressed = true;
            GalaxyRoyale.Sim.Bots.BotSystem.Advance(_state, _bots, _events);
            _events.Suppressed = false;
            _events.DrainSuppressed();
            // AdvanceToWallClock's clock counts from scene start; the loaded save is
            // already at Tick N, so shift the origin back N seconds to line them up.
            _startTime = Time.timeAsDouble - _state.Tick;
            BuildingMarkers.RequestExtraMineSync();
            SceneBootstrap.RequestPlanetRefresh(); // adopted seed → new planet look
            Debug.Log($"[GalaxyRoyale] Adopted save at tick {_state.Tick} (+{summary.ElapsedSec}s offline)");
            return summary;
        }

        void Update()
        {
            EnsureInit();
            long nowMs = (long)((Time.timeAsDouble - _startTime) * 1000);
            _engine!.AdvanceToWallClock(nowMs);
            // The simulated galaxy rides the same clock, right behind the player sim.
            if (_bots != null && _state != null)
                GalaxyRoyale.Sim.Bots.BotSystem.Advance(_state, _bots, _events!);
        }

        void OnSimEvent(SimEvent e)
        {
            if (logEvents) Debug.Log($"[Sim] {e}");
        }
    }
}
