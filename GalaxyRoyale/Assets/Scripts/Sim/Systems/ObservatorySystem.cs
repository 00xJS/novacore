// Deep Space Observatory (Frontier, 2026-09-29): long-range telescopes. Wilds
// surveys finish 1.5% sooner a level (45% at 30), the radar hears raiders 3%
// earlier a level, and from level 10 it forecasts the next Pirate Dreadnought:
// where it will drop, and when.
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Sim.Systems
{
    public static class ObservatorySystem
    {
        public const int ForecastLevel = 10;

        public static int Level(GameState state) =>
            state.Buildings.TryGetValue(BuildingId.Observatory, out var b) ? b.Level : 0;

        public static double SurveyMult(GameState state) => 1.0 - 0.015 * System.Math.Min(Level(state), 30);

        public static double RadarLeadMult(GameState state) => 1.0 + 0.03 * System.Math.Min(Level(state), 30);

        /// <summary>The next dreadnought's drop point and arrival tick, once the
        /// observatory can see it coming (null while one is here, or below level 10).</summary>
        public static (TileXY tile, int atTick)? Forecast(GameState state, BotGalaxy galaxy)
        {
            var boss = galaxy.Boss;
            if (Level(state) < ForecastLevel || boss.Active || boss.NextVisitTick <= 0) return null;
            return (BossSystem.DropPointFor(state.Seed, boss.Visit + 1), boss.NextVisitTick);
        }
    }
}
