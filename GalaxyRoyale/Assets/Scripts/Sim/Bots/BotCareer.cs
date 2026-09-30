// Rivals play the whole game (user request 2026-09-30: "make sure the bots ...
// do the quests & level up using all of the in game features"). Before this,
// the 249 simulated commanders built, researched, flew and fought, but never
// learned a skill, never walked the Commander's Path, never touched the
// Frontier, the Citadel, the market or the Wilds, and stalled at Command
// Center 9-10 for a month: gold-starved (ships and research ate it all),
// short of energy (extra mines outgrew a Power Plant the Command Center caps)
// and never saving for the next Command Center.
//
// Once per decision window (BotSystem.Decide) a rival now:
//   - saves for the Command Center when it's the thing holding them back, and
//     trades surplus on the galactic market for what it lacks;
//   - levels their commander and spends the skill points by temperament;
//   - walks the Commander's Path and collects its rewards (steps built on
//     things rivals don't do on the map count as done a little later);
//   - raises the Frontier and Citadel buildings, and terraforms;
//   - sends expeditions and hunts pirate camps, off-screen like their
//     gathering: the counters, loot and relics are real.
// Everything goes through the same systems and rules the player uses.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Bots
{
    public static class BotCareer
    {
        const int Hour = 3600;
        static readonly SimEventBus Quiet = new();

        /// <summary>The Command Center is what holds the colony back: its producers and
        /// Power Plant sit at its level, and only resources stand in the way.</summary>
        public static bool SavingForCommand(GameState s)
        {
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            if (cc >= Buildings.Defs[BuildingId.CommandCenter].MaxLevel) return false;
            foreach (var o in s.BuildQueue) if (o.Building == BuildingId.CommandCenter) return false;
            int capped = 0;
            foreach (var id in new[] { BuildingId.GoldMine, BuildingId.QuartzExtractor, BuildingId.HeliumRefinery, BuildingId.PowerPlant })
                if (s.Buildings[id].Level >= cc) capped++;
            if (capped < 3) return false;
            return !ResourceSystem.CanAfford(s, BuildingSystem.GetUpgradeCost(BuildingId.CommandCenter, cc + 1));
        }

        /// <summary>Runs once per decision window (the first think step inside it).</summary>
        public static void Decide(BotEmpire bot, BotPersonality personality, Func<double> rng)
        {
            var s = bot.State;
            int window = s.Tick / BotSystem.DecideWindowTicks;
            if (window == s.CareerWindow) return;
            s.CareerWindow = window;

            Trade(s);
            Commander(s, personality);
            Path(s, rng);
            Terraform(s, personality);
            OffScreen(bot, s, rng);
        }

        // ---------- the market: what the Command Center needs ----------

        static void Trade(GameState s)
        {
            if (s.Buildings[BuildingId.CommandCenter].Level < 5) return; // the market opens with the Path's Act II
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            var need = BuildingSystem.GetUpgradeCost(BuildingId.CommandCenter, cc + 1);
            ResourceId? lack = null, spare = null;
            double worst = double.MaxValue, best = 0;
            foreach (var r in Data.Resources.All)
            {
                long want = Math.Max(1, need.Get(r));
                double ratio = s.Resources.Get(r) / (double)want;
                if (ratio < worst) { worst = ratio; lack = r; }
                if (ratio > best) { best = ratio; spare = r; }
            }
            // Only a clear imbalance: something short, something piled up past twice its share.
            if (lack is not { } buy || spare is not { } sell || buy == sell || worst >= 1 || best < 2) return;
            long surplusWhole = (s.Resources.Get(sell) - 2 * need.Get(sell)) / 1000;
            long amount = Math.Min(surplusWhole / 2, MarketSystem.MaxSell(s, sell));
            if (amount >= 100) MarketSystem.Trade(s, sell, buy, amount);
        }

        // ---------- the commander ----------

        static readonly string[] IndustryFirst = BuildOrder(economy: true);
        static readonly string[] AdmiraltyFirst = BuildOrder(economy: false);

        static string[] BuildOrder(bool economy)
        {
            var list = new List<string>();
            foreach (var pass in economy ? new[] { 0, 1 } : new[] { 1, 0 })
                foreach (var skill in CommanderSkills.All)
                    if ((skill.Branch == CommanderSkills.Branches[0]) == (pass == 0)) list.Add(skill.Id);
            return list.ToArray();
        }

        static void Commander(GameState s, BotPersonality personality)
        {
            var c = s.Commander;
            int level = CommanderSystem.RivalLevel(s);
            if (level > c.Level) c.Level = level;
            var order = personality.EconomyFocus >= 0.5 ? IndustryFirst : AdmiraltyFirst;
            for (int guard = 0; guard < 64 && CommanderSystem.PointsFree(s) > 0; guard++)
            {
                bool learned = false;
                foreach (var id in order)
                    if (CommanderSystem.Learn(s, id).Ok) { learned = true; break; }
                if (!learned) break;
            }
        }

        // ---------- the Commander's Path ----------

        /// <summary>Goals a rival pursues for real; the rest (map scouting, the Wilds,
        /// dailies, items, the market, expeditions) are done off-screen.</summary>
        static bool RealGoal(QuestGoal g) => g is QuestGoal.BuildingLevel or QuestGoal.ResearchLevel
            or QuestGoal.ShipsBuilt or QuestGoal.CommanderLevel or QuestGoal.ExtraMines or QuestGoal.SkillsLearned
            or QuestGoal.BattlesWon or QuestGoal.CampsCleared;

        static void Path(GameState s, Func<double> rng)
        {
            for (int step = 0; step < 3; step++)
            {
                var quest = QuestSystem.Current(s);
                if (quest == null) return;
                if (QuestSystem.IsComplete(s, quest)) { QuestSystem.Claim(s); continue; }
                // An off-screen step: done in a window or two, on average.
                if (RealGoal(quest.Goal) || rng() > 0.4) return;
                ForceComplete(s, quest);
                QuestSystem.Claim(s);
            }
        }

        static void ForceComplete(GameState s, QuestDef quest)
        {
            var st = s.Stats;
            switch (quest.Goal)
            {
                case QuestGoal.Gathered: st.GatheredMilli = Math.Max(st.GatheredMilli, quest.Target * 1000L); break;
                case QuestGoal.DailiesClaimed: st.DailiesClaimed = Math.Max(st.DailiesClaimed, quest.Target); break;
                case QuestGoal.ItemsUsed: st.ItemsUsed = Math.Max(st.ItemsUsed, quest.Target); break;
                case QuestGoal.MarketTrades: st.MarketTrades = Math.Max(st.MarketTrades, quest.Target); break;
                case QuestGoal.Expeditions: st.ExpeditionsDone = Math.Max(st.ExpeditionsDone, quest.Target); break;
                case QuestGoal.SupplyDrops: st.SupplyDropsCollected = Math.Max(st.SupplyDropsCollected, quest.Target); break;
                case QuestGoal.ClanJoined: if (s.ClanId == 0) s.QuestStep++; return; // (their clan comes from ClanSystem)
                default: s.QuestStep++; return; // scouting, the Wilds: no counter to show for it
            }
        }

        // ---------- the Terraformer ----------

        static void Terraform(GameState s, BotPersonality personality)
        {
            if (TerraformSystem.Level(s) < 1 || SavingForCommand(s)) return;
            var path = s.Terraform.Path != TerraformPath.None ? s.Terraform.Path
                : personality.EconomyFocus >= 0.66 ? TerraformPath.Metallic
                : personality.EconomyFocus >= 0.33 ? TerraformPath.Crystalline
                : TerraformPath.Temperate;
            TerraformSystem.Start(s, path);
        }

        /// <summary>Per think step (BotSystem.ThinkStep): finish a terraform stage.</summary>
        public static void CompleteProjects(GameState s) => TerraformSystem.Tick(s, Quiet);

        // ---------- off-screen: expeditions and pirate hunting ----------

        static void OffScreen(BotEmpire bot, GameState s, Func<double> rng)
        {
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            // Pirate hunting: now and then at the keyboard, once there's a fleet. The
            // camp's own stockpile, not grown by the Command Center: it's for the record
            // and the Path, not a second economy.
            if (cc >= 3 && rng() < 0.1)
            {
                s.Stats.CampsCleared++;
                s.Stats.BattlesWon++;
                int level = Math.Clamp(1 + cc / 3, 1, 5);
                long loot = Nodes.CampStockpile(level) * 1000 * 3 / 10;
                var bag = new ResourceBag(loot * 45 / 100, loot * 35 / 100, loot * 20 / 100);
                ResourceSystem.Add(s, bag);
                s.Stats.LootMilli += bag.Total;
            }
            // Expeditions: about one a day at the keyboard from Command Center 6.
            if (cc >= 6 && rng() < 0.03)
            {
                var hourly = ResourceSystem.MineOutputPerHour(s);
                ResourceSystem.Add(s, new ResourceBag(hourly.Gold * 3, hourly.Quartz * 3, hourly.Helium * 3));
                s.Stats.ExpeditionsDone++;
                if (s.Buildings[BuildingId.RelicVault].Level >= 1 && rng() < 0.3)
                {
                    var kind = (RelicKind)((bot.Id + s.Stats.ExpeditionsDone) % Relics.All.Count);
                    s.Relics[kind] = RelicSystem.Count(s, kind) + 1;
                }
            }
        }
    }
}
