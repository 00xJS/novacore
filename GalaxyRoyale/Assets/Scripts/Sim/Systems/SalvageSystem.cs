// Salvage Yard (Frontier, 2026-09-28): after each battle the player fights,
// the yard's crews recover part of the cost of the ships the player lost and,
// when the fight was over the player's own colony, of every raider shot down
// there. Salvage piles up in the yard (up to its capacity) until the player
// collects it. Runs off the battle reports as they're filed, so every kind of
// fight counts: raids, camps, strikes, intercepts, the core, the dreadnought.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Combat;

namespace GalaxyRoyale.Sim.Systems
{
    public static class SalvageSystem
    {
        public static int Level(GameState state) =>
            state.Buildings.TryGetValue(BuildingId.SalvageYard, out var b) ? b.Level : 0;

        /// <summary>Share of a wreck's cost that comes back.</summary>
        public static float Rate(GameState state) => Balance.SalvageRate(Level(state));

        /// <summary>How much the yard holds before it has to be collected (milli, per resource).</summary>
        public static long CapacityMilli(GameState state) => Balance.SalvageCapacity(Level(state)) * 1000L;

        /// <summary>Resource cost of a set of ships (milli).</summary>
        public static ResourceBag ValueOf(Dictionary<HullId, int> ships)
        {
            var bag = new ResourceBag();
            foreach (var kv in ships)
            {
                if (kv.Value <= 0) continue;
                var cost = Ships.Defs[kv.Key].Cost;
                bag.Gold += cost.Gold * 1000L * kv.Value;
                bag.Quartz += cost.Quartz * 1000L * kv.Value;
                bag.Helium += cost.Helium * 1000L * kv.Value;
            }
            return bag;
        }

        static Dictionary<HullId, int> Lost(Dictionary<HullId, int> before, Dictionary<HullId, int> after)
        {
            var lost = new Dictionary<HullId, int>();
            foreach (var kv in before)
            {
                int left = after.TryGetValue(kv.Key, out var a) ? a : 0;
                if (kv.Value - left > 0) lost[kv.Key] = kv.Value - left;
            }
            return lost;
        }

        /// <summary>The player's own losses in a report: their side's losses, less the
        /// share of any clanmates' ships fighting beside them.</summary>
        static Dictionary<HullId, int> OwnLosses(BattleMailReport mail)
        {
            var r = mail.Report;
            var before = mail.Defending ? r.Defender : r.Attacker;
            var after = mail.Defending ? r.DefenderSurvivors : r.AttackerSurvivors;
            var lost = Lost(before, after);
            if (mail.AllyShips == null || mail.AllyShips.Count == 0) return lost;
            var own = new Dictionary<HullId, int>();
            foreach (var kv in lost)
            {
                int total = before.TryGetValue(kv.Key, out var t) ? t : 0;
                int allies = mail.AllyShips.TryGetValue(kv.Key, out var a) ? a : 0;
                int mine = Math.Max(0, total - allies);
                if (total <= 0 || mine <= 0) continue;
                int share = (int)Math.Round(kv.Value * (double)mine / total);
                if (share > 0) own[kv.Key] = share;
            }
            return own;
        }

        /// <summary>What a battle report puts in the yard (milli), before the capacity limit.</summary>
        public static ResourceBag FromReport(GameState state, BattleMailReport mail)
        {
            float rate = Rate(state);
            if (rate <= 0f) return new ResourceBag();
            var value = ValueOf(OwnLosses(mail));
            // A raid on your own colony: the raiders' wrecks fall on your planet.
            if (mail.Defending && mail.GuardedBotId == 0)
            {
                var wrecks = ValueOf(Lost(mail.Report.Attacker, mail.Report.AttackerSurvivors));
                value.Gold += wrecks.Gold;
                value.Quartz += wrecks.Quartz;
                value.Helium += wrecks.Helium;
            }
            return value.Scaled(rate);
        }

        /// <summary>A dreadnought strike: the ships it cost you.</summary>
        public static ResourceBag FromBoss(GameState state, BossReport report)
        {
            float rate = Rate(state);
            if (rate <= 0f || report.Kind != BossReportKind.Strike) return new ResourceBag();
            return ValueOf(Lost(report.Fleet, report.Survivors)).Scaled(rate);
        }

        /// <summary>File a report's salvage in the yard and note it on the report;
        /// returns what was actually added (milli).</summary>
        public static ResourceBag OnMail(GameState state, MailItem item)
        {
            var gain = item switch
            {
                BattleMailReport b => FromReport(state, b),
                BossReport boss => FromBoss(state, boss),
                _ => new ResourceBag(),
            };
            var added = Store(state, gain);
            if (added.Total > 0) item.Salvaged = added;
            return added;
        }

        /// <summary>Add salvage up to the yard's capacity; returns what fitted (milli).</summary>
        public static ResourceBag Store(GameState state, ResourceBag gain)
        {
            long cap = CapacityMilli(state);
            var s = state.SalvageStored;
            var added = new ResourceBag(
                Math.Max(0, Math.Min(gain.Gold, cap - s.Gold)),
                Math.Max(0, Math.Min(gain.Quartz, cap - s.Quartz)),
                Math.Max(0, Math.Min(gain.Helium, cap - s.Helium)));
            s.Gold += added.Gold;
            s.Quartz += added.Quartz;
            s.Helium += added.Helium;
            return added;
        }

        /// <summary>The yard is full of at least one resource: new salvage of it is lost.</summary>
        public static bool Full(GameState state)
        {
            long cap = CapacityMilli(state);
            var s = state.SalvageStored;
            return cap > 0 && (s.Gold >= cap || s.Quartz >= cap || s.Helium >= cap);
        }

        /// <summary>Move everything in the yard into the treasury.</summary>
        public static SimResult Collect(GameState state, out ResourceBag collected)
        {
            collected = state.SalvageStored.Clone();
            if (collected.Total <= 0) return SimResult.Fail("Nothing to collect yet");
            ResourceSystem.Add(state, collected);
            state.SalvageStored = new ResourceBag();
            return SimResult.Success;
        }
    }
}
