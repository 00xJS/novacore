// Jump Gate (Frontier, 2026-09-29): folds space for your fleets — every level
// makes flights 1% faster and 1% cheaper in helium (30% at level 30) — and
// charges one free jump of your colony every 24 hours: a Blind Jump to a random
// empty tile, or from level 10 a Precision Warp to a tile you pick.
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class JumpGateSystem
    {
        public const int ReloadSec = 24 * 3600;
        /// <summary>From this level the free jump goes wherever you choose.</summary>
        public const int PrecisionLevel = 10;

        public static int Level(GameState state) =>
            state.Buildings.TryGetValue(BuildingId.JumpGate, out var b) ? b.Level : 0;

        public static double SpeedMult(GameState state) => 1.0 + 0.01 * System.Math.Min(Level(state), 30);
        public static double HeliumMult(GameState state) => 1.0 - 0.01 * System.Math.Min(Level(state), 30);

        public static bool Precise(GameState state) => Level(state) >= PrecisionLevel;
        public static bool Ready(GameState state) => Level(state) >= 1 && state.Tick >= state.JumpGateReadyTick;
        public static int ReloadLeft(GameState state) => System.Math.Max(0, state.JumpGateReadyTick - state.Tick);

        static SimResult CanJump(GameState state)
        {
            if (Level(state) < 1) return SimResult.Fail("Build the Jump Gate first");
            if (!Ready(state)) return SimResult.Fail("The Jump Gate is still recharging");
            return SimResult.Success;
        }

        /// <summary>The free Blind Jump: a random empty tile.</summary>
        public static SimResult JumpRandom(GameState state)
        {
            var ok = CanJump(state);
            if (!ok.Ok) return ok;
            var res = MarchSystem.RelocateHomeRandom(state);
            if (res.Ok) state.JumpGateReadyTick = state.Tick + ReloadSec;
            return res;
        }

        /// <summary>The free Precision Warp (level 10+): a tile you pick.</summary>
        public static SimResult JumpTo(GameState state, TileXY tile)
        {
            var ok = CanJump(state);
            if (!ok.Ok) return ok;
            if (!Precise(state)) return SimResult.Fail($"Precision jumps open at Jump Gate level {PrecisionLevel}");
            var res = MarchSystem.RelocateHome(state, tile);
            if (res.Ok) state.JumpGateReadyTick = state.Tick + ReloadSec;
            return res;
        }
    }
}
