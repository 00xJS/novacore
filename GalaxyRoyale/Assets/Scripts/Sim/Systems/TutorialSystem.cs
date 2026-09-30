// Runs the new-commander training (Data/Tutorial). Progress is read straight off
// the empire, like the Commander's Path, so a step the player has already done
// is passed the moment it comes up; the Seen steps wait for the screen they name
// to be opened (the UI calls Notice). Nothing here ever blocks the game: skip it
// and everything plays exactly the same.
using System;
using System.Linq;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class TutorialSystem
    {
        /// <summary>TutorialStep when the training is finished, skipped, or the save predates it.</summary>
        public const int Done = -1;

        const string FlagSpeedups = "speedups";
        const string FlagReward = "reward";

        public static bool Active(GameState state) => state.TutorialStep >= 0 && state.TutorialStep < Tutorial.Steps.Count;

        public static TutorialStepDef? Current(GameState state) => Active(state) ? Tutorial.Steps[state.TutorialStep] : null;

        public static bool IsMet(GameState state, TutorialStepDef step) => step.Goal switch
        {
            TutorialGoal.Building => Level(state, step.Building) >= step.Target
                || state.BuildQueue.Any(o => o.Building == step.Building && o.MineId == null && o.ToLevel >= step.Target),
            TutorialGoal.BuildingBuilt => Level(state, step.Building) >= step.Target,
            TutorialGoal.QuestsClaimed => state.QuestStep >= step.Target,
            TutorialGoal.CampScouted => CampScouted(state),
            TutorialGoal.AttackReported => AttackReported(state),
            TutorialGoal.ResearchStarted => state.ResearchQueue.Count > 0 || state.Research.Values.Any(l => l > 0),
            _ => false, // Next and Seen wait for the player
        };

        /// <summary>Pass every step that's already done. Call often (the coach does, every
        /// few frames); returns true when the step changed.</summary>
        public static bool Check(GameState state)
        {
            bool moved = false;
            for (int guard = 0; guard < Tutorial.Steps.Count && Current(state) is { } step && IsMet(state, step); guard++)
            {
                Advance(state);
                moved = true;
            }
            return moved;
        }

        /// <summary>NEXT on a read-and-continue step.</summary>
        public static SimResult Next(GameState state)
        {
            var step = Current(state);
            if (step == null) return SimResult.Fail("The training is finished");
            if (step.Goal != TutorialGoal.Next) return SimResult.Fail("Do this step to move on");
            Advance(state);
            return SimResult.Success;
        }

        /// <summary>A screen was opened: passes the step waiting for it.</summary>
        public static bool Notice(GameState state, string seen)
        {
            if (Current(state) is not { Goal: TutorialGoal.Seen } step || step.Seen != seen) return false;
            Advance(state);
            return true;
        }

        public static void Skip(GameState state) => state.TutorialStep = Done;

        /// <summary>Start over (Settings › Help). Gifts already given aren't given twice.</summary>
        public static void Restart(GameState state)
        {
            state.TutorialStep = 0;
            Check(state);
        }

        static void Advance(GameState state)
        {
            state.TutorialStep++;
            if (!Active(state))
            {
                state.TutorialStep = Done;
                if (state.TutorialFlags.Add(FlagReward))
                {
                    ResourceSystem.Add(state, Tutorial.Reward.Milli());
                    state.Premium.DarkMatter += Tutorial.RewardDarkMatter;
                }
                return;
            }
            if (Current(state)!.Id == "speed-up" && state.TutorialFlags.Add(FlagSpeedups))
            {
                var entry = state.Inventory.FirstOrDefault(i => i.ItemId == Tutorial.SpeedupItem);
                if (entry == null) state.Inventory.Add(entry = new InventoryEntry { ItemId = Tutorial.SpeedupItem });
                entry.Count += Tutorial.SpeedupCount;
            }
        }

        static int Level(GameState state, BuildingId id) =>
            state.Buildings.TryGetValue(id, out var slot) ? slot.Level : 0;

        static bool CampScouted(GameState state) =>
            state.Mailbox.Any(m => m is SpyReport r && r.Intel.Kind == NodeKind.Camp && r.Intel.Garrison != null);

        static bool AttackReported(GameState state) =>
            state.Mailbox.Any(m => m is BattleMailReport b && !b.Defending);

        /// <summary>The technology the research step suggests: the one the Commander's
        /// Path asks for when it asks for research (Bastion Hangars, on DEFENSE), else
        /// the first one that can be started on the ECONOMY page, in the panel's order.</summary>
        public static TechId? SuggestedTech(GameState state)
        {
            if (QuestSystem.Current(state) is { Goal: QuestGoal.ResearchLevel } quest
                && ResearchSystem.CheckResearch(state, quest.Tech).Ok)
                return quest.Tech;
            foreach (var id in Techs.All
                         .Where(t => Techs.Defs[t].Category != TechCategory.Military && Techs.Defs[t].Category != TechCategory.Defense)
                         .OrderBy(t => Techs.Defs[t].LabLevelReq).ThenBy(t => Techs.Defs[t].Name, StringComparer.Ordinal))
                if (ResearchSystem.CheckResearch(state, id).Ok) return id;
            return null;
        }

        /// <summary>"Step 3 of 21".</summary>
        public static (int number, int total) Position(GameState state) =>
            (Math.Max(0, state.TutorialStep) + 1, Tutorial.Steps.Count);
    }
}
