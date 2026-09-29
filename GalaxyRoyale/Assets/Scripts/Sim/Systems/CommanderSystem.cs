// Commander progression. XP comes from the empire's record: every building and
// research level (worth more the higher it goes), ships, battles, plunder,
// quests, events, achievements, the Galactic Core and the Pirate Dreadnought. Reading it off the
// record means offline catch-up needs nothing special and older saves start
// at the level their history earned. Only a new high on the record earns XP,
// at the difficulty's rate when it lands.
//
// Every level past the first pays Dark Matter (plus an item every fifth) and
// a skill point; points buy ranks in Data/CommanderSkills, whose effects join
// the research totals (ResearchSystem.EffectTotal).
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class CommanderSystem
    {
        public const int MaxLevel = 40;
        /// <summary>XP to reach a level is XpPerLevelSquared × (level − 1)².</summary>
        public const long XpPerLevelSquared = 25;
        /// <summary>Dark Matter per level reached, × the level.</summary>
        public const int LevelDarkMatter = 10;
        /// <summary>Resetting the skills costs this much Dark Matter after the first (free) reset.</summary>
        public const int RespecDarkMatter = 200;

        /// <summary>The item each fifth level adds (level → shop item id).</summary>
        static readonly Dictionary<int, string> MilestoneItems = new()
        {
            [5] = "speed-1h", [10] = "finish-any", [15] = "shield-8h", [20] = "buff-buildslot",
            [25] = "speed-6h", [30] = "finish-any", [35] = "shield-24h", [40] = "buff-researchslot",
        };

        // ---------- XP and levels ----------

        public static long XpForLevel(int level)
        {
            long l = Math.Max(0, Math.Min(level, MaxLevel) - 1);
            return XpPerLevelSquared * l * l;
        }

        public static int LevelFor(long xp)
        {
            int level = 1;
            while (level < MaxLevel && xp >= XpForLevel(level + 1)) level++;
            return level;
        }

        /// <summary>(XP into the current level, XP the level spans) — for the progress bar;
        /// (0, 0) at the level cap.</summary>
        public static (long into, long span) LevelProgress(GameState s)
        {
            var c = s.Commander;
            if (c.Level >= MaxLevel) return (0, 0);
            long floor = XpForLevel(c.Level);
            return (Math.Max(0, c.Xp - floor), XpForLevel(c.Level + 1) - floor);
        }

        static long Tri(int level) => (long)level * (level + 1) / 2;

        /// <summary>The empire's record in XP. Levels count 1 + 2 + … + L (buildings ×5,
        /// research ×4), so each new level is worth more than the last.</summary>
        public static long Score(GameState s)
        {
            long xp = 0;
            foreach (var kv in s.Buildings) xp += Tri(kv.Value.Level) * 5;
            foreach (var mine in s.ExtraMines) xp += Tri(mine.Level) * 5;
            foreach (var kv in s.Research) xp += Tri(kv.Value) * 4;
            var st = s.Stats;
            xp += st.ShipsBuilt;
            xp += st.BattlesWon * 5L + st.CampsCleared * 10L + st.RaidsWon * 40L + st.DefensesWon * 40L;
            xp += st.LootMilli / 1_000_000; // 1 per 1,000 plundered
            xp += s.QuestStep * 50L + s.Achievements.Count * 25L;
            xp += st.EventsCompleted * 100L + st.ClanWarsWon * 150L;
            xp += st.CoresSeized * 300L + st.CoreHoursHeld * 10L;
            xp += st.BossDamage / 2_000 + st.BossFinalBlows * 200L; // damage, not strikes: nothing to farm
            return xp;
        }

        /// <summary>A rival's level, read straight off their record (rivals have no skills).</summary>
        public static int RivalLevel(GameState rival) => LevelFor(Score(rival));

        /// <summary>What reaching a level pays.</summary>
        public static (int darkMatter, string? item) LevelReward(int level) =>
            (LevelDarkMatter * level, MilestoneItems.TryGetValue(level, out var id) ? id : null);

        /// <summary>Credit new XP and pay every level it reaches. Runs each sim tick
        /// (ProgressionSystem) — cheap: a few dozen additions.</summary>
        public static void Tick(GameState s, SimEventBus events)
        {
            var c = s.Commander;
            long score = Score(s);
            if (score > c.ScoreSeen)
            {
                // ×1.5 on Brutal, in halves so nothing is lost to rounding.
                int halves = (int)Math.Round(Difficulties.XpMult(s.Difficulty) * 2);
                long scaled = (score - c.ScoreSeen) * halves + c.XpCarry;
                c.Xp += scaled / 2;
                c.XpCarry = (int)(scaled % 2);
                c.ScoreSeen = score;
            }
            int level = LevelFor(c.Xp);
            if (level <= c.Level) return;
            int from = c.Level, darkMatter = 0;
            var items = new List<string>();
            while (c.Level < level)
            {
                c.Level++;
                var (dm, item) = LevelReward(c.Level);
                s.Premium.DarkMatter += dm;
                darkMatter += dm;
                if (item != null) { GrantItem(s, item); items.Add(item); }
            }
            // One event however many levels landed (a big win can be worth several).
            events.Emit(new CommanderLevelUp(c.Level, c.Level - from, darkMatter, items));
        }

        static void GrantItem(GameState s, string itemId)
        {
            foreach (var slot in s.Inventory)
                if (slot.ItemId == itemId) { slot.Count++; return; }
            s.Inventory.Add(new InventoryEntry { ItemId = itemId, Count = 1 });
        }

        // ---------- skill points ----------

        public static int Rank(GameState s, string skillId) =>
            s.Commander.Skills.TryGetValue(skillId, out var r) ? r : 0;

        public static int PointsEarned(GameState s) => Math.Max(0, s.Commander.Level - 1);

        public static int PointsSpent(GameState s)
        {
            int n = 0;
            foreach (var kv in s.Commander.Skills) n += kv.Value;
            return n;
        }

        public static int PointsFree(GameState s) => Math.Max(0, PointsEarned(s) - PointsSpent(s));

        public static int BranchPoints(GameState s, SkillBranch branch)
        {
            int n = 0;
            foreach (var kv in s.Commander.Skills)
                if (CommanderSkills.ById(kv.Key) is { } def && def.Branch == branch) n += kv.Value;
            return n;
        }

        public static bool TierOpen(GameState s, SkillBranch branch, int tier) =>
            BranchPoints(s, branch) >= CommanderSkills.TierPoints[Math.Clamp(tier, 1, 4) - 1];

        public static SimResult CheckLearn(GameState s, string skillId)
        {
            if (CommanderSkills.ById(skillId) is not { } def) return SimResult.Fail("Unknown skill");
            if (Rank(s, skillId) >= def.MaxRank) return SimResult.Fail($"{def.Name} is at its highest rank");
            if (!TierOpen(s, def.Branch, def.Tier))
                return SimResult.Fail($"Spend {CommanderSkills.TierPoints[def.Tier - 1]} points in " +
                    $"{CommanderSkills.BranchName(def.Branch)} to open tier {def.Tier}");
            if (PointsFree(s) < 1) return SimResult.Fail("No skill points left — level up to earn more");
            return SimResult.Success;
        }

        public static SimResult Learn(GameState s, string skillId)
        {
            var check = CheckLearn(s, skillId);
            if (!check.Ok) return check;
            s.Commander.Skills[skillId] = Rank(s, skillId) + 1;
            return SimResult.Success;
        }

        public static int RespecCost(GameState s) => s.Commander.Respecs == 0 ? 0 : RespecDarkMatter;

        /// <summary>Hand back every point (the first reset is free).</summary>
        public static SimResult Respec(GameState s)
        {
            if (s.Commander.Skills.Count == 0) return SimResult.Fail("No skills to reset");
            int cost = RespecCost(s);
            if (s.Premium.DarkMatter < cost) return SimResult.Fail("Not enough Dark Matter");
            s.Premium.DarkMatter -= cost;
            s.Commander.Skills.Clear();
            s.Commander.Respecs++;
            return SimResult.Success;
        }

        /// <summary>Summed magnitude of every learned skill effect of one kind
        /// (joins ResearchSystem.EffectTotal; 0 for rivals, who have no skills).</summary>
        public static float EffectTotal(GameState s, TechEffectKind kind)
        {
            var skills = s.Commander.Skills;
            if (skills.Count == 0) return 0f;
            float sum = 0f;
            foreach (var kv in skills)
            {
                if (kv.Value <= 0 || CommanderSkills.ById(kv.Key) is not { } def) continue;
                foreach (var e in def.Effects)
                    if (e.Kind == kind) sum += e.PerRank * kv.Value;
            }
            return sum;
        }
    }
}
