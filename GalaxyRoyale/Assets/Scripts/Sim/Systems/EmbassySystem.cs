// Clan Embassy (Frontier, 2026-09-29): your clan's hall on the colony. Every 10
// levels one more clanmate wing can fly with your joint strikes (3 → 6) and one
// more supply run arrives a day (1 → 4); your share of the core's clan tribute
// grows from 4% to 9% across its 30 levels.
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class EmbassySystem
    {
        public static int Level(GameState state) =>
            state.Buildings.TryGetValue(BuildingId.ClanEmbassy, out var b) ? b.Level : 0;

        public static int StrikeWings(GameState state) => StrikeSystem.MaxStrikeWings + System.Math.Min(Level(state), 30) / 10;

        public static int SupplyRunsPerDay(GameState state) => 1 + System.Math.Min(Level(state), 30) / 10;

        public static int SupplyIntervalSec(GameState state) => ClanSystem.SupplyIntervalSec / SupplyRunsPerDay(state);

        public static double ClanTributeShare(GameState state) =>
            CoreSystem.ClanTributeShare + 0.05 * System.Math.Min(Level(state), 30) / 30.0;
    }
}
