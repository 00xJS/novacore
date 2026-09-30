// Achievements and commander titles (user request 2026-09-28). Each pays a
// little Dark Matter when it unlocks; many also unlock a TITLE the commander
// can wear on their profile and in the rankings.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum AchievementGoal
    {
        BattlesWon, CampsCleared, RaidsWon, DefensesWon, ShipsBuilt, LootWhole,
        CommandCenter, ResearchLevels, DefenseResearchLevels, QuestsDone,
        ClanJoined, ClanSize, ClanWarsWon, SeasonTop, EventsCompleted, Might,
        CoresSeized, CoreHoursHeld, BossDamage, BossFinalBlows, MarketTrades,
        NemesesDefeated, // 2026-09-30
        LordsDefeated, ChaptersDone, // the campaign (2026-09-30)
        MegaprojectsDone, // mega-projects (2026-09-30)
    }

    public sealed class AchievementDef
    {
        public string Id = "";
        public string Name = "";
        public string Detail = "";
        public AchievementGoal Goal;
        /// <summary>Count to reach — or, for SeasonTop, the rank to finish at or above.</summary>
        public long Target;
        public int RewardDM;
        /// <summary>Title the achievement unlocks (null = none).</summary>
        public string? Title;
    }

    public static class Achievements
    {
        static AchievementDef A(string id, string name, string detail, AchievementGoal goal, long target,
            int dm, string? title = null) =>
            new() { Id = id, Name = name, Detail = detail, Goal = goal, Target = target, RewardDM = dm, Title = title };

        public static readonly IReadOnlyList<AchievementDef> All = new[]
        {
            A("first-blood", "First Blood", "Win your first battle", AchievementGoal.BattlesWon, 1, 25),
            A("veteran", "Veteran", "Win 25 battles", AchievementGoal.BattlesWon, 25, 100, "Veteran"),
            A("warlord", "Warlord", "Win 100 battles", AchievementGoal.BattlesWon, 100, 300, "Warlord"),
            A("pirate-hunter", "Pirate Hunter", "Clear 10 pirate camps", AchievementGoal.CampsCleared, 10, 100, "Pirate Hunter"),
            A("scourge", "Scourge of the Void", "Clear 50 pirate camps", AchievementGoal.CampsCleared, 50, 250, "Scourge of the Void"),
            A("raider", "Raider", "Win 5 raids on rival colonies", AchievementGoal.RaidsWon, 5, 100, "Raider"),
            A("conqueror", "Conqueror", "Win 25 raids on rival colonies", AchievementGoal.RaidsWon, 25, 300, "Conqueror"),
            A("iron-wall", "Iron Wall", "Repel 5 raids on your colony", AchievementGoal.DefensesWon, 5, 100, "Iron Wall"),
            A("unbreakable", "Unbreakable", "Repel 25 raids on your colony", AchievementGoal.DefensesWon, 25, 300, "The Unbreakable"),
            A("shipwright", "Shipwright", "Build 100 ships", AchievementGoal.ShipsBuilt, 100, 50),
            A("admiral", "Admiral", "Build 1,000 ships", AchievementGoal.ShipsBuilt, 1000, 200, "Admiral"),
            A("plunderer", "Plunderer", "Loot 100,000 resources", AchievementGoal.LootWhole, 100_000, 150, "Plunderer"),
            A("architect", "Architect", "Raise the Command Center to Lv 10", AchievementGoal.CommandCenter, 10, 200, "Architect"),
            A("scholar", "Scholar", "Complete 25 research levels", AchievementGoal.ResearchLevels, 25, 150, "Scholar"),
            A("fortress", "Fortress", "Complete 10 defense research levels", AchievementGoal.DefenseResearchLevels, 10, 150, "Fortress Keeper"),
            A("pathfinder", "Pathfinder", "Finish the first act of the Commander's Path", AchievementGoal.QuestsDone, 11, 150, "Pathfinder"),
            // Act II of the Path (balance pass 2026-09-30).
            A("trailblazer", "Trailblazer", "Finish both acts of the Commander's Path", AchievementGoal.QuestsDone, 36, 300, "Trailblazer"),
            A("diplomat", "Diplomat", "Join or found a clan", AchievementGoal.ClanJoined, 1, 50, "Diplomat"),
            A("full-ranks", "Full Ranks", "Be in a clan of 15 commanders", AchievementGoal.ClanSize, 15, 200, "Clan Captain"),
            A("warmaster", "Warmaster", "Win 3 clan wars", AchievementGoal.ClanWarsWon, 3, 300, "Warmaster"),
            A("event-hunter", "Event Hunter", "Complete 5 galaxy event goals", AchievementGoal.EventsCompleted, 5, 150, "Event Hunter"),
            A("contender", "Contender", "Finish a season in the top 10", AchievementGoal.SeasonTop, 10, 200, "Contender"),
            A("champion", "Champion", "Win a season", AchievementGoal.SeasonTop, 1, 500, "Champion"),
            A("rising-power", "Rising Power", "Reach 50,000 might", AchievementGoal.Might, 50_000, 200, "Rising Power"),
            A("core-breacher", "Core Breacher", "Seize the Galactic Core", AchievementGoal.CoresSeized, 1, 250, "Core Breacher"),
            A("warden", "Warden of the Core", "Collect 24 hours of Core tribute", AchievementGoal.CoreHoursHeld, 24, 500, "Warden of the Core"),
            A("dreadnought-hunter", "Dreadnought Hunter", "Deal 500,000 damage to Pirate Dreadnoughts", AchievementGoal.BossDamage, 500_000, 200, "Dreadnought Hunter"),
            A("final-blow", "Leviathan Slayer", "Land the final blow on a Pirate Dreadnought", AchievementGoal.BossFinalBlows, 1, 300, "Leviathan Slayer"),
            A("trader", "Merchant Prince", "Make 25 trades on the galactic market", AchievementGoal.MarketTrades, 25, 100, "Merchant Prince"),
            A("nemesis-slayer", "Nemesis Slayer", "Break a nemesis", AchievementGoal.NemesesDefeated, 1, 200, "Nemesis Slayer"),
            // The campaign, "The Long Night" (2026-09-30).
            A("lord-breaker", "Lord-Breaker", "Defeat 3 Pirate Lords", AchievementGoal.LordsDefeated, 3, 200, "Lord-Breaker"),
            A("court-breaker", "Court-Breaker", "Defeat every Pirate Lord", AchievementGoal.LordsDefeated, 10, 500, "Scourge of the Court"),
            A("nightwalker", "Nightwalker", "Finish 5 chapters of The Long Night", AchievementGoal.ChaptersDone, 5, 250, "Nightwalker"),
            A("dawnbringer", "Dawnbringer", "Finish The Long Night", AchievementGoal.ChaptersDone, 10, 600, "Dawnbringer"),
            // Mega-projects (2026-09-30).
            A("wonder-builder", "Wonder-Builder", "Complete a mega-project", AchievementGoal.MegaprojectsDone, 1, 300, "Wonder-Builder"),
            A("architect-of-worlds", "Architect of Worlds", "Complete all four mega-projects", AchievementGoal.MegaprojectsDone, 4, 600, "Architect of Worlds"),
        };

        public static AchievementDef? ById(string id)
        {
            foreach (var a in All) if (a.Id == id) return a;
            return null;
        }
    }
}
