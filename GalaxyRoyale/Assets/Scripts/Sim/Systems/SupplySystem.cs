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
            int every = (int)(Balance.SupplyDropEverySec * TwistSystem.SupplyIntervalMult(s)); // Supply Surge
            if (s.NextSupplyDropTick == 0) { s.NextSupplyDropTick = s.Tick + every; return; }
            if (s.Tick < s.NextSupplyDropTick) return;
            s.NextSupplyDropTick = s.Tick + every;
            if (s.SupplyCrates >= Balance.SupplyDropMaxStored) return;
            s.SupplyCrates++;
            events.Emit(new SupplyDropLanded(s.SupplyCrates));
        }

        /// <summary>What one crate holds right now (milli-units).</summary>
        public static ResourceBag CrateContents(GameState s)
        {
            var hour = ResourceSystem.MineOutputPerHour(s);
            double h = Balance.SupplyDropHoursOfOutput;
            long floor = Balance.SupplyDropFloor * 1000L;
            return new ResourceBag(Math.Max(floor, (long)(hour.Gold * h)), Math.Max(floor, (long)(hour.Quartz * h)),
                Math.Max(floor / 2, (long)(hour.Helium * h)));
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
