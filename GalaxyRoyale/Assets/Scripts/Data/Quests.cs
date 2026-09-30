// The Commander's Path (user request 2026-09-27): a guided quest chain that
// runs in both modes. In a STANDARD game it carries the honest start — 500
// gold / 300 quartz / 100 helium can't even afford Command Center 2, and the
// Shipyard eats every drop of starting helium — so each reward pays for the
// next step, and the Shipyard quest hands over a starter squadron. In a
// TESTING game it's a tour of the systems. Rewards are whole units.
//
// Act II (balance pass 2026-09-30): Act I ended three hours in, and from then on
// nothing said what to do next. Act II carries the Path through the first week,
// from Command Center 4 to 9, with the Wilds, supply drops, a real fleet, pirate
// hunting and the first Frontier buildings. Saves that finished Act I pick it up.
// Together with the training (Data/Tutorial) it teaches every basic feature once:
// gathering, extra mines, dailies, items, skills, clans and the market too.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum QuestGoal
    {
        BuildingLevel, // Building at Target level or higher
        CampScouted,   // a pirate camp's garrison scanned (camp spy report in the mailbox)
        BattlesWon,    // Stats.BattlesWon >= Target (attack or defense)
        ResearchLevel, // Tech at Target level or higher
        // Act II (balance pass 2026-09-30):
        CampsCleared,   // Stats.CampsCleared >= Target
        ShipsBuilt,     // Stats.ShipsBuilt >= Target
        WildsSurveyed,  // surveys of the Wilds, all sectors together
        SupplyDrops,    // Stats.SupplyDropsCollected >= Target
        CommanderLevel, // Commander.Level >= Target
        // Teaching steps (user 2026-09-30: every basic feature gets taught):
        Gathered,       // whole units hauled home from gathering
        ExtraMines,     // extra mines built in the Mining Belt
        DailiesClaimed, // daily objectives claimed
        SkillsLearned,  // commander skill ranks learned
        ItemsUsed,      // items used from the inventory
        ClanJoined,     // in a clan (joined or founded)
        MarketTrades,   // trades on the galactic market
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
            // ---- Act II: the first week ----
            new QuestDef
            {
                Title = "Into the Wilds",
                Detail = "Tip your planet south and SURVEY a sector of the Wilds. Deposits there refill forever.",
                Goal = QuestGoal.WildsSurveyed, Target = 1,
                Reward = new ResourceBag(1200, 900, 400),
            },
            new QuestDef
            {
                Title = "Supply Lines",
                Detail = "Fleet command drops a supply crate every 4 hours. Collect one at the Command Center.",
                Goal = QuestGoal.SupplyDrops, Target = 1,
                Reward = new ResourceBag(1200, 900, 600),
            },
            new QuestDef
            {
                Title = "Gather Round",
                Detail = "Tap a resource world on the MAP and GATHER with Haulers. Bring 3,000 resources home.",
                Goal = QuestGoal.Gathered, Target = 3000,
                Reward = new ResourceBag(1400, 1000, 600),
            },
            new QuestDef
            {
                Title = "The Mining Belt",
                Detail = "Turn to the MINES district and build a second mine on an open pad. More pads open as the Command Center rises.",
                Goal = QuestGoal.ExtraMines, Target = 1,
                Reward = new ResourceBag(1500, 1100, 500),
            },
            new QuestDef
            {
                Title = "Daily Orders",
                Detail = "Open MORE › DAILY and claim a daily objective. They reset every day and pay Dark Matter.",
                Goal = QuestGoal.DailiesClaimed, Target = 1,
                Reward = new ResourceBag(1200, 900, 500),
            },
            new QuestDef
            {
                Title = "Home Guard",
                Detail = "Build 30 ships. Warships docked at home raise production, up to +15%.",
                Goal = QuestGoal.ShipsBuilt, Target = 30,
                Reward = new ResourceBag(1600, 1200, 800),
            },
            new QuestDef
            {
                Title = "Growing Colony",
                Detail = "Upgrade the Command Center to Lv 4.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.CommandCenter, Target = 4,
                Reward = new ResourceBag(2500, 1800, 900),
            },
            new QuestDef
            {
                Title = "The Citadel",
                Detail = "Tip your planet north (or tap CITADEL) and build the Academy. Your commander can lead a fleet from there.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.Academy, Target = 1,
                Reward = new ResourceBag(2200, 1600, 900),
            },
            new QuestDef
            {
                Title = "Sharpen Your Skills",
                Detail = "Open your profile (top left) and learn a commander skill. Every level earns a skill point.",
                Goal = QuestGoal.SkillsLearned, Target = 1,
                Reward = new ResourceBag(2000, 1500, 800),
            },
            new QuestDef
            {
                Title = "Pirate Hunter",
                Detail = "Clear 5 pirate camps. Every camp holds a stockpile, and Haulers flying along carry 50% more.",
                Goal = QuestGoal.CampsCleared, Target = 5,
                Reward = new ResourceBag(3000, 2200, 1200),
            },
            new QuestDef
            {
                Title = "Tools of the Trade",
                Detail = "Open ITEMS and use something from your inventory, like a Speed-Up on a building.",
                Goal = QuestGoal.ItemsUsed, Target = 1,
                Reward = new ResourceBag(2500, 1800, 1000),
            },
            new QuestDef
            {
                Title = "Deep Vaults",
                Detail = "Upgrade the Warehouse to Lv 4 — raiders can't touch what it protects.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.Warehouse, Target = 4,
                Reward = new ResourceBag(3500, 2500, 1200),
            },
            new QuestDef
            {
                Title = "Strength in Numbers",
                Detail = "Open MORE › CLAN and join a clan, or found your own. Clanmates fly with you and guard your colony.",
                Goal = QuestGoal.ClanJoined, Target = 1,
                Reward = new ResourceBag(3000, 2200, 1200),
            },
            new QuestDef
            {
                Title = "Standing Tall",
                Detail = "Upgrade the Command Center to Lv 5. Your beginner protection ends there, so have your defenses ready.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.CommandCenter, Target = 5,
                Reward = new ResourceBag(5000, 3500, 2000),
            },
            new QuestDef
            {
                Title = "Open for Business",
                Detail = "Trade one resource for another on the market (MORE › MARKET, or the Exchange Terminal).",
                Goal = QuestGoal.MarketTrades, Target = 1,
                Reward = new ResourceBag(4000, 3000, 1500),
            },
            new QuestDef
            {
                Title = "Scholar",
                Detail = "Upgrade the Research Lab to Lv 5 for the deeper technologies.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.ResearchLab, Target = 5,
                Reward = new ResourceBag(6000, 4500, 2000),
            },
            new QuestDef
            {
                Title = "Surveyor",
                Detail = "Survey the Wilds 8 times. The deeper rings hold richer deposits and relics.",
                Goal = QuestGoal.WildsSurveyed, Target = 8,
                Reward = new ResourceBag(7000, 5000, 2500),
            },
            new QuestDef
            {
                Title = "The Frontier",
                Detail = "Build the Command Bastion on the FRONTIER (Command Center 6) — railguns for every raid on your colony.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.CommandBastion, Target = 1,
                Reward = new ResourceBag(9000, 6500, 3500),
            },
            new QuestDef
            {
                Title = "Armada",
                Detail = "Build 120 ships in all. A bigger Home Guard, and a fleet that can take the tougher camps.",
                Goal = QuestGoal.ShipsBuilt, Target = 120,
                Reward = new ResourceBag(10000, 7500, 4000),
            },
            new QuestDef
            {
                Title = "Open the Gate",
                Detail = "Build the Jump Gate (Command Center 7): faster fleets, and a free jump for your colony every day.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.JumpGate, Target = 1,
                Reward = new ResourceBag(13000, 9500, 5000),
            },
            new QuestDef
            {
                Title = "Scourge of the Rim",
                Detail = "Clear 12 pirate camps. The deeper tiers toward the Core hold tougher camps with richer stockpiles.",
                Goal = QuestGoal.CampsCleared, Target = 12,
                Reward = new ResourceBag(15000, 11000, 6000),
            },
            new QuestDef
            {
                Title = "Seasoned Commander",
                Detail = "Reach commander level 15. Spend your skill points in your profile.",
                Goal = QuestGoal.CommanderLevel, Target = 15,
                Reward = new ResourceBag(18000, 13000, 7000),
            },
            new QuestDef
            {
                Title = "Nothing Wasted",
                Detail = "Build the Salvage Yard (Command Center 8) to recover resources from every battle's wrecks.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.SalvageYard, Target = 1,
                Reward = new ResourceBag(22000, 16000, 8000),
            },
            new QuestDef
            {
                Title = "Power of the Rim",
                Detail = "Upgrade the Command Center to Lv 9. The Core is waiting.",
                Goal = QuestGoal.BuildingLevel, Building = BuildingId.CommandCenter, Target = 9,
                Reward = new ResourceBag(30000, 22000, 12000),
            },
        };

        /// <summary>Act I's length: the Pathfinder achievement and the training count to here.</summary>
        public const int ActOneSteps = 11;
    }
}
