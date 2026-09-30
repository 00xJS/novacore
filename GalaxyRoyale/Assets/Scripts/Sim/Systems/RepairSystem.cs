// Repair Dock (Frontier, 2026-09-29): when raiders shoot your ships down over
// your own colony, tugs tow a share of the wrecks home as damaged hulls. Repair
// them at the dock for a fraction of their cost and build time — far cheaper
// than building anew — before they're scrapped (72 h). One repair at a time.
// Runs off the battle reports as they're filed, like the Salvage Yard.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class RepairSystem
    {
        /// <summary>Damaged hulls are scrapped this long after the battle.</summary>
        public const int KeepSec = 72 * 3600;
        /// <summary>Repair costs this share of the hulls' build cost and time.</summary>
        public const double CostShare = 0.3;

        public static int Level(GameState state) =>
            state.Buildings.TryGetValue(BuildingId.RepairDock, out var b) ? b.Level : 0;

        /// <summary>Share of the ships lost defending home that come back damaged: 1.7% a level, 50% at level 30.</summary>
        public static double TowShare(int level) => level < 1 ? 0 : 0.5 * Math.Min(level, 30) / 30.0;

        /// <summary>File a report's towed hulls (defences of your own colony only).</summary>
        public static void OnMail(GameState state, MailItem item)
        {
            if (item is not BattleMailReport mail || !mail.Defending || mail.GuardedBotId != 0) return;
            double share = TowShare(Level(state));
            if (share <= 0) return;
            var towed = new Dictionary<HullId, int>();
            foreach (var kv in SalvageSystem.OwnLosses(mail))
            {
                int n = (int)Math.Floor(kv.Value * share);
                if (n > 0) towed[kv.Key] = n;
            }
            if (towed.Count == 0) return;
            state.DamagedHulls.Add(new DamagedBatch { Ships = towed, ExpiresAtTick = state.Tick + KeepSec });
            mail.Towed = towed;
        }

        /// <summary>Every damaged hull still waiting (expired batches dropped).</summary>
        public static Dictionary<HullId, int> Waiting(GameState state)
        {
            state.DamagedHulls.RemoveAll(b => b.ExpiresAtTick <= state.Tick);
            var all = new Dictionary<HullId, int>();
            foreach (var b in state.DamagedHulls)
                foreach (var kv in b.Ships)
                    all[kv.Key] = (all.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            return all;
        }

        /// <summary>When the oldest waiting batch is scrapped (0 = nothing waiting).</summary>
        public static int NextScrapTick(GameState state)
        {
            int soonest = 0;
            foreach (var b in state.DamagedHulls)
                if (b.ExpiresAtTick > state.Tick && (soonest == 0 || b.ExpiresAtTick < soonest)) soonest = b.ExpiresAtTick;
            return soonest;
        }

        /// <summary>What repairing a set of hulls costs (milli).</summary>
        public static ResourceBag Cost(Dictionary<HullId, int> ships) => SalvageSystem.ValueOf(ships).Scaled((float)CostShare);

        public static int Seconds(Dictionary<HullId, int> ships)
        {
            long sec = 0;
            foreach (var kv in ships) sec += (long)Ships.Defs[kv.Key].BuildTimeSec * kv.Value;
            return (int)Math.Max(10, Math.Ceiling(sec * CostShare));
        }

        /// <summary>Repair everything waiting: pay, and the hulls rejoin the dock when it's done.</summary>
        public static SimResult StartRepair(GameState state)
        {
            if (Level(state) < 1) return SimResult.Fail("Build the Repair Dock first");
            if (state.Repair != null) return SimResult.Fail("A repair is already under way");
            var ships = Waiting(state);
            if (ships.Count == 0) return SimResult.Fail("No damaged hulls to repair");
            var pay = ResourceSystem.Spend(state, Cost(ships));
            if (!pay.Ok) return pay;
            state.Repair = new RepairJob { Ships = ships, EndsAtTick = state.Tick + Seconds(ships) };
            state.DamagedHulls.Clear();
            return SimResult.Success;
        }

        /// <summary>A finished repair puts the ships back in your dock.</summary>
        public static void Tick(GameState state, SimEventBus events)
        {
            var job = state.Repair;
            if (job == null || state.Tick < job.EndsAtTick) return;
            foreach (var kv in job.Ships)
                state.Ships[kv.Key] = (state.Ships.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            state.Repair = null;
        }
    }
}
