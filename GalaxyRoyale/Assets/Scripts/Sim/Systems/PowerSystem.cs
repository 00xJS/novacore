// Might score. Direct port of v1's `src/sim/systems/PowerSystem.ts`.
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class PowerSystem
    {
        /// <summary>
        /// Might score: total resources ever invested in buildings and ships,
        /// in whole units / 10. Marching ships count — power shouldn't dip when you attack.
        /// Accumulates in long: milli-invested exceeds int range at high levels (v1 used float64).
        ///
        /// Hot path: every bot think step, the header, rankings, and the map label
        /// call this — the per-level cost is computed inline (identical math to
        /// BuildingSystem.GetUpgradeCost) instead of allocating two ResourceBags
        /// per building level.
        /// </summary>
        public static long ComputePower(GameState state)
        {
            long milliInvested = 0;

            foreach (var id in Buildings.All)
            {
                for (int level = 1; level <= state.Buildings[id].Level; level++)
                    milliInvested += UpgradeCostTotalMilli(id, level);
            }
            foreach (var mine in state.ExtraMines)
            {
                var buildingId = MineTypes.ToBuildingId(mine.Type);
                for (int level = 1; level <= mine.Level; level++)
                    milliInvested += UpgradeCostTotalMilli(buildingId, level);
            }

            var fleetCounts = new System.Collections.Generic.Dictionary<HullId, int>(state.Ships);
            foreach (var march in state.Marches)
            {
                foreach (var kv in march.Ships)
                    fleetCounts[kv.Key] = (fleetCounts.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            }
            foreach (var hull in Ships.All)
            {
                if (fleetCounts.TryGetValue(hull, out var count))
                    milliInvested += (long)count * Ships.Defs[hull].Cost.Total * 1000;
            }

            return milliInvested / 10_000;
        }

        /// <summary>Allocation-free twin of BuildingSystem.GetUpgradeCost(id, level).Total —
        /// the float factor + per-component Ceiling must stay bit-identical to
        /// ResourceBag.Scaled(f).Milli() or might values drift.</summary>
        static long UpgradeCostTotalMilli(BuildingId id, int toLevel)
        {
            var baseCost = Buildings.Defs[id].BaseCost;
            float f = (float)System.Math.Pow(Balance.CostGrowth, toLevel - 1);
            return ((long)System.Math.Ceiling(baseCost.Gold * (double)f)
                  + (long)System.Math.Ceiling(baseCost.Quartz * (double)f)
                  + (long)System.Math.Ceiling(baseCost.Helium * (double)f)) * 1000;
        }
    }
}
