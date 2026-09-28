// The Commander's Path (user request 2026-09-27): a guided quest chain that
// runs in both modes. In a STANDARD game it carries the honest start — 500
// gold / 300 quartz / 100 helium can't even afford Command Center 2, and the
// Shipyard eats every drop of starting helium — so each reward pays for the
// next step, and the Shipyard quest hands over a starter squadron. In a
// TESTING game it's a tour of the systems. Rewards are whole units.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum QuestGoal
    {
        BuildingLevel, // Building at Target level or higher
        CampScouted,   // a pirate camp's garrison scanned (camp spy report in the mailbox)
        BattlesWon,    // Stats.BattlesWon >= Target (attack or defense)
        ResearchLevel, // Tech at Target level or higher
    }

    public sealed class QuestDef
    {
        public string Title = "";
        /// <summary>What to do and why it matters — one sentence.</summary>
        public string Detail = "";
        public QuestGoal Goal;
        public BuildingId Building;
        public TechId Tech;
        public int Target = 1;
        public ResourceBag Reward = new();
        public IReadOnlyDictionary<HullId, int>? RewardShips;
    }

    public static class Quests
    {
        public static readonly IReadOnlyList<QuestDef> Chain = new[]
        {
            new QuestDef
            {
                Title = "Break Ground",
                Detail = "Build a Quartz Extractor — quartz pays for nearly every upgrade.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.QuartzExtractor, Target = 1,
                Reward = new ResourceBag(400, 200, 0),
            },
            new QuestDef
            {
                Title = "Raise the Capital",
                Detail = "Upgrade the Command Center to Lv 2 — no other building can outgrow it.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.CommandCenter, Target = 2,
                Reward = new ResourceBag(300, 300, 100),
            },
            new QuestDef
            {
                Title = "Keep the Lights On",
                Detail = "Upgrade the Power Plant to Lv 2 — starved mines produce less.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.PowerPlant, Target = 2,
                Reward = new ResourceBag(250, 150, 0),
            },
            new QuestDef
            {
                Title = "Fuel the Fleet",
                Detail = "Build a Helium Refinery — every fleet burns helium to fly.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.HeliumRefinery, Target = 1,
                Reward = new ResourceBag(100, 0, 300),
            },
            new QuestDef
            {
                Title = "Lay the Keel",
                Detail = "Build the Shipyard. Your starter squadron is waiting to launch.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.Shipyard, Target = 1,
                Reward = new ResourceBag(400, 200, 200),
                RewardShips = new Dictionary<HullId, int> { [HullId.Fighter] = 10, [HullId.Probe] = 2 },
            },
            new QuestDef
            {
                Title = "Scout the Frontier",
                Detail = "Tap a pirate camp on the MAP and send a SPY probe to read its garrison.",
                Goal = QuestGoal.CampScouted, Target = 1,
                // Helium enough to fly the next step's strike (balance pass 2026-09-28).
                Reward = new ResourceBag(400, 300, 600),
            },
            new QuestDef
            {
                Title = "First Blood",
                Detail = "Win a battle — the forecast tells you if your fleet can take the camp.",
                Goal = QuestGoal.BattlesWon, Target = 1,
                Reward = new ResourceBag(600, 400, 300),
            },
            new QuestDef
            {
                Title = "Knowledge Is Power",
                Detail = "Build a Research Lab to unlock the tech tree.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.ResearchLab, Target = 1,
                Reward = new ResourceBag(400, 300, 200),
            },
            new QuestDef
            {
                Title = "Hold the Line",
                Detail = "Research Bastion Hangars on the DEFENSE page — raiders will come.",
                Goal = QuestGoal.ResearchLevel, Tech = TechId.BastionHangars, Target = 1,
                Reward = new ResourceBag(500, 300, 200),
            },
            new QuestDef
            {
                Title = "Rising Power",
                Detail = "Upgrade the Command Center to Lv 3 to raise every building's cap.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.CommandCenter, Target = 3,
                Reward = new ResourceBag(800, 600, 400),
            },
            new QuestDef
            {
                Title = "Eyes on the Sky",
                Detail = "Build a Radar Station — see raids coming before they land.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.RadarStation, Target = 1,
                Reward = new ResourceBag(1000, 800, 500),
            },
        };
    }
}
