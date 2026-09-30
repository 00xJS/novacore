// Achievements (Data/Achievements): progress reads straight off the empire's
// counters and levels; CheckNew unlocks whatever just became true, pays its
// Dark Matter and reports it so the UI can celebrate.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class AchievementSystem
    {
        public static bool IsUnlocked(GameState state, AchievementDef a) => state.Achievements.Contains(a.Id);

        /// <summary>(have, need). SeasonTop reports 1/1 once a finish is good enough.</summary>
        public static (long have, long need) Progress(GameState state, AchievementDef a)
        {
            if (a.Goal == AchievementGoal.SeasonTop)
            {
                int best = state.Stats.BestSeasonRank;
                return (best > 0 && best <= a.Target ? 1 : 0, 1);
            }
            long have = a.Goal switch
            {
                AchievementGoal.BattlesWon => state.Stats.BattlesWon,
                AchievementGoal.CampsCleared => state.Stats.CampsCleared,
                AchievementGoal.RaidsWon => state.Stats.RaidsWon,
                AchievementGoal.DefensesWon => state.Stats.DefensesWon,
                AchievementGoal.ShipsBuilt => state.Stats.ShipsBuilt,
                AchievementGoal.LootWhole => state.Stats.LootMilli / 1000,
                AchievementGoal.CommandCenter => state.Buildings[BuildingId.CommandCenter].Level,
                AchievementGoal.ResearchLevels => ResearchLevels(state, defenseOnly: false),
                AchievementGoal.DefenseResearchLevels => ResearchLevels(state, defenseOnly: true),
                AchievementGoal.QuestsDone => state.QuestStep,
                AchievementGoal.ClanJoined => state.ClanId != 0 ? 1 : 0,
                AchievementGoal.ClanSize => state.Stats.BestClanSize,
                AchievementGoal.ClanWarsWon => state.Stats.ClanWarsWon,
                AchievementGoal.EventsCompleted => state.Stats.EventsCompleted,
                AchievementGoal.Might => PowerSystem.ComputePower(state),
                AchievementGoal.CoresSeized => state.Stats.CoresSeized,
                AchievementGoal.CoreHoursHeld => state.Stats.CoreHoursHeld,
                AchievementGoal.BossDamage => state.Stats.BossDamage,
                AchievementGoal.BossFinalBlows => state.Stats.BossFinalBlows,
                AchievementGoal.MarketTrades => state.Stats.MarketTrades,
                AchievementGoal.NemesesDefeated => state.Stats.NemesesDefeated,
                AchievementGoal.LordsDefeated => LairSystem.LordsBeaten(state),
                AchievementGoal.ChaptersDone => CampaignSystem.ChaptersDone(state),
                AchievementGoal.MegaprojectsDone => MegaprojectSystem.Completed(state),
                _ => 0,
            };
            return (Math.Min(have, a.Target), a.Target);
        }

        /// <summary>Unlock (and pay for) every achievement that just became true.</summary>
        public static List<AchievementDef> CheckNew(GameState state)
        {
            var unlocked = new List<AchievementDef>();
            foreach (var a in Achievements.All)
            {
                if (IsUnlocked(state, a)) continue;
                var (have, need) = Progress(state, a);
                if (have < need) continue;
                state.Achievements.Add(a.Id);
                state.Premium.DarkMatter += a.RewardDM;
                unlocked.Add(a);
            }
            return unlocked;
        }

        /// <summary>Wear an unlocked achievement's title (null takes it off).</summary>
        public static SimResult EquipTitle(GameState state, string? achievementId)
        {
            if (achievementId == null) { state.Title = null; return SimResult.Success; }
            var a = Achievements.ById(achievementId);
            if (a?.Title == null) return SimResult.Fail("That achievement has no title");
            if (!IsUnlocked(state, a)) return SimResult.Fail("Unlock it first");
            state.Title = achievementId;
            return SimResult.Success;
        }

        /// <summary>The worn title's text, or null.</summary>
        public static string? TitleText(GameState state) =>
            state.Title != null ? Achievements.ById(state.Title)?.Title : null;

        static long ResearchLevels(GameState state, bool defenseOnly)
        {
            long sum = 0;
            foreach (var kv in state.Research)
                if (!defenseOnly || Techs.Defs[kv.Key].Category == TechCategory.Defense)
                    sum += kv.Value;
            return sum;
        }
    }
}
