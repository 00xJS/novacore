// The Commander's Path quest chain (Data/Quests). Progress is read straight
// off the empire — building levels, research, battles won, the mailbox — so a
// step the player already did shows as complete the moment it becomes current.
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class QuestSystem
    {
        /// <summary>The active quest, or null once the chain is complete.</summary>
        public static QuestDef? Current(GameState state) =>
            state.QuestStep >= 0 && state.QuestStep < Quests.Chain.Count ? Quests.Chain[state.QuestStep] : null;

        /// <summary>Progress toward the quest's goal, capped at the target.</summary>
        public static (int have, int need) Progress(GameState state, QuestDef quest)
        {
            int have = quest.Goal switch
            {
                QuestGoal.BuildingLevel => state.Buildings.TryGetValue(quest.Building, out var slot) ? slot.Level : 0,
                QuestGoal.ResearchLevel => ResearchSystem.TechLevel(state, quest.Tech),
                QuestGoal.BattlesWon => state.Stats.BattlesWon,
                QuestGoal.CampScouted => CampScouted(state) ? 1 : 0,
                _ => 0,
            };
            return (Math.Min(have, quest.Target), quest.Target);
        }

        public static bool IsComplete(GameState state, QuestDef quest)
        {
            var (have, need) = Progress(state, quest);
            return have >= need;
        }

        /// <summary>Collect the current quest's reward and move on to the next.</summary>
        public static SimResult Claim(GameState state)
        {
            var quest = Current(state);
            if (quest == null) return SimResult.Fail("Every quest is complete");
            if (!IsComplete(state, quest)) return SimResult.Fail("Quest not finished yet");
            var milli = quest.Reward.Milli();
            state.Resources.Gold += milli.Gold;
            state.Resources.Quartz += milli.Quartz;
            state.Resources.Helium += milli.Helium;
            if (quest.RewardShips != null)
                foreach (var kv in quest.RewardShips)
                    state.Ships[kv.Key] = (state.Ships.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            state.QuestStep++;
            return SimResult.Success;
        }

        /// <summary>A pirate camp's garrison scanned: camp recon files a Camp-kind
        /// report WITHOUT the base / wallet sections a rival-colony scan carries.</summary>
        static bool CampScouted(GameState state)
        {
            foreach (var m in state.Mailbox)
                if (m is SpyReport r && r.Intel.Kind == NodeKind.Camp && r.Intel.Garrison != null
                    && r.Intel.Buildings == null && r.Intel.LootableMilli == null)
                    return true;
            return false;
        }
    }
}
