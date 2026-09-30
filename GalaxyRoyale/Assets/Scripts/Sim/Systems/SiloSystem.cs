// The Missile Silo (the Citadel, 2026-09-30): an active defence. Once your
// radar has seen a raid coming, FIRE: a salvo destroys a share of the raiding
// fleet in flight (those ships are gone for good). Then the silo reloads.
// Only raids the radar has picked up can be targeted, so the Radar Station and
// the Observatory's longer warnings matter; an Ion Storm blinds both.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Sim.Systems
{
    public static class SiloSystem
    {
        public static int Level(GameState s) => s.Buildings[BuildingId.MissileSilo].Level;

        /// <summary>Share of each hull type a salvo destroys: 10%, +1% a level, up to 40%.</summary>
        public static double Share(int level) => level < 1 ? 0 : Math.Min(0.40, 0.10 + 0.01 * level);

        /// <summary>Reload: 8 hours, 12 minutes less a level, never under 2 hours.</summary>
        public static int ReloadSec(int level) => Math.Max(2 * 3600, 8 * 3600 - 12 * 60 * level);

        public static int ReloadLeft(GameState s) => Math.Max(0, s.SiloReadyTick - s.Tick);

        /// <summary>Raids inbound to you that the radar has picked up (war fleets, not probes).</summary>
        public static List<BotAttack> Targets(GameState s, BotGalaxy galaxy)
        {
            var list = new List<BotAttack>();
            int lead = RadarSystem.WarnLeadSeconds(s);
            foreach (var atk in galaxy.Inbound)
            {
                int left = atk.ArrivesAtTick - s.Tick;
                if (atk.IsFleet && left > 0 && left <= lead && MarchFleetCount(atk.Ships) > 0) list.Add(atk);
            }
            list.Sort((a, b) => a.ArrivesAtTick.CompareTo(b.ArrivesAtTick));
            return list;
        }

        public static SimResult CanFire(GameState s, BotGalaxy galaxy, int attackId)
        {
            if (Level(s) < 1) return SimResult.Fail("Build the Missile Silo first");
            if (ReloadLeft(s) > 0) return SimResult.Fail("The silo is reloading");
            if (!Targets(s, galaxy).Exists(a => a.Id == attackId)) return SimResult.Fail("Your radar hasn't picked that raid up");
            return SimResult.Success;
        }

        /// <summary>Fire a salvo at an inbound raid. Returns the ships destroyed.</summary>
        public static SimResult Fire(GameState s, BotGalaxy galaxy, int attackId, SimEventBus? events, out int destroyed)
        {
            destroyed = 0;
            var can = CanFire(s, galaxy, attackId);
            if (!can.Ok) return can;
            var atk = galaxy.Inbound.Find(a => a.Id == attackId)!;
            double share = Share(Level(s));
            HullId? biggest = null;
            foreach (var hull in Ships.All)
            {
                if (!atk.Ships.TryGetValue(hull, out var n) || n <= 0) continue;
                int kill = (int)Math.Floor(n * share);
                atk.Ships[hull] = n - kill;
                destroyed += kill;
                if (biggest == null || Ships.Defs[hull].Cost.Total > Ships.Defs[biggest.Value].Cost.Total) biggest = hull;
            }
            // A salvo always takes at least one hull.
            if (destroyed == 0 && biggest is { } h && atk.Ships[h] > 0) { atk.Ships[h]--; destroyed = 1; }
            s.SiloReadyTick = s.Tick + ReloadSec(Level(s));
            s.Stats.MissileKills += destroyed;
            string name = galaxy.Find(atk.BotId)?.Name ?? "the raiders";
            events?.Emit(new SiloFired(destroyed, name));
            return SimResult.Success;
        }

        static int MarchFleetCount(Dictionary<HullId, int> ships)
        {
            int n = 0;
            foreach (var v in ships.Values) n += v;
            return n;
        }
    }
}
