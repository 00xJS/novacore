// Supply drops (balance pass 2026-09-30): the pacing run found the mid-game's
// check-ins idle — nothing affordable, nothing to do. Fleet command now sends a
// crate every Balance.SupplyDropEverySec; up to Balance.SupplyDropMaxStored wait
// at the Command Center, so a player who looks in twice a day still finds some.
// A crate holds Balance.SupplyDropHoursOfOutput of the colony's current mine
// output (at full energy), so it keeps pace with the colony.
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class SupplySystem
    {
        public static void Tick(GameState s, SimEventBus events)
        {
            if (s.NextSupplyDropTick == 0) { s.NextSupplyDropTick = s.Tick + Balance.SupplyDropEverySec; return; }
            if (s.Tick < s.NextSupplyDropTick) return;
            s.NextSupplyDropTick = s.Tick + Balance.SupplyDropEverySec;
            if (s.SupplyCrates >= Balance.SupplyDropMaxStored) return;
            s.SupplyCrates++;
            events.Emit(new SupplyDropLanded(s.SupplyCrates));
        }

        /// <summary>What one crate holds right now (milli-units).</summary>
        public static ResourceBag CrateContents(GameState s)
        {
            var bag = new ResourceBag();
            foreach (var id in Buildings.All) AddOutput(bag, id, s.Buildings[id].Level);
            foreach (var mine in s.ExtraMines) AddOutput(bag, MineTypes.ToBuildingId(mine.Type), mine.Level);
            long floor = Balance.SupplyDropFloor * 1000L;
            return new ResourceBag(Math.Max(floor, bag.Gold), Math.Max(floor, bag.Quartz), Math.Max(floor / 2, bag.Helium));
        }

        static void AddOutput(ResourceBag bag, BuildingId id, int level)
        {
            var def = Buildings.Defs[id];
            if (def.Kind != BuildingKind.Producer || level < 1) return;
            long milli = (long)(Balance.ProdPerHour(def.BaseProdPerHour, level) * 1000.0 * Balance.SupplyDropHoursOfOutput);
            switch (def.Resource)
            {
                case "gold": bag.Gold += milli; break;
                case "quartz": bag.Quartz += milli; break;
                case "helium": bag.Helium += milli; break;
            }
        }

        /// <summary>Open every waiting crate. Returns what they held (milli), or null if none waited.</summary>
        public static ResourceBag? Collect(GameState s)
        {
            if (s.SupplyCrates <= 0) return null;
            var one = CrateContents(s);
            var all = new ResourceBag(one.Gold * s.SupplyCrates, one.Quartz * s.SupplyCrates, one.Helium * s.SupplyCrates);
            ResourceSystem.Add(s, all);
            s.Stats.SupplyDropsCollected += s.SupplyCrates;
            s.SupplyCrates = 0;
            return all;
        }
    }
}
