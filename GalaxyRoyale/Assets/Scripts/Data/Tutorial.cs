// The new-commander training (user request 2026-09-30: "if a player does not
// understand how everything works they will be lost"). A guided run through the
// real game, step by step: each step says what to do and why, the coach
// spotlights where to tap, and it moves on by itself once the player has done
// it. It runs alongside the Commander's Path (Data/Quests): the training
// teaches the controls, the path keeps paying for the next step.
// Sim/Systems/TutorialSystem.cs runs it; Game/UI/TutorialCoach.cs shows it.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum TutorialGoal
    {
        /// <summary>Read it and tap NEXT.</summary>
        Next,
        /// <summary>The building is at Target or on its way there (queued counts).</summary>
        Building,
        /// <summary>The building has reached Target.</summary>
        BuildingBuilt,
        /// <summary>The Commander's Path has been claimed up to Target quests.</summary>
        QuestsClaimed,
        /// <summary>The player opened a screen (TutorialSystem.Notice with Seen).</summary>
        Seen,
        /// <summary>A pirate camp's garrison has been scanned.</summary>
        CampScouted,
        /// <summary>The player's own attack has been fought and reported.</summary>
        AttackReported,
        /// <summary>A technology is being researched or has been.</summary>
        ResearchStarted,
    }

    public sealed class TutorialStepDef
    {
        public string Id = "";
        public string Chapter = "";
        public string Title = "";
        public string Text = "";
        public TutorialGoal Goal;
        public BuildingId Building;
        public int Target;
        /// <summary>For Seen: the screen's key (TutorialSystem.Seen*).</summary>
        public string Seen = "";
    }

    public static class Tutorial
    {
        /// <summary>Handed over when the speed-up step begins (once per game).</summary>
        public const string SpeedupItem = "speed-5m";
        public const int SpeedupCount = 2;
        /// <summary>Paid once, when the training is finished (not when skipped).</summary>
        public static readonly ResourceBag Reward = new(1500, 1000, 600);
        public const int RewardDarkMatter = 50;

        public static readonly IReadOnlyList<TutorialStepDef> Steps = new[]
        {
            // ---- your colony ----
            new TutorialStepDef
            {
                Id = "welcome", Chapter = "YOUR COLONY", Title = "WELCOME, COMMANDER",
                Text = "You run a new colony on the rim of a galaxy with 249 rival commanders. This training walks you " +
                       "through the controls, one step at a time. It takes about ten minutes, and you can leave it " +
                       "from Settings at any time.",
                Goal = TutorialGoal.Next,
            },
            new TutorialStepDef
            {
                Id = "resources", Chapter = "YOUR COLONY", Title = "YOUR RESOURCES",
                Text = "Gold, quartz and helium pay for everything, and your mines make more every hour. Energy powers " +
                       "the mines: below 100% they slow down. Dark Matter is rare and buys speed-ups and items.",
                Goal = TutorialGoal.Next,
            },
            new TutorialStepDef
            {
                Id = "build-quartz", Chapter = "YOUR COLONY", Title = "BUILD A QUARTZ EXTRACTOR",
                Text = "Tap the Quartz Extractor's pad, then BUILD. Quartz goes into almost every upgrade.",
                Goal = TutorialGoal.Building, Building = BuildingId.QuartzExtractor, Target = 1,
            },
            new TutorialStepDef
            {
                Id = "queues", Chapter = "YOUR COLONY", Title = "YOUR BUILD QUEUE",
                Text = "Building takes time. The ☰ button lists everything under way. You have two build slots, and " +
                       "a number on the button means one is free. Tap it now.",
                Goal = TutorialGoal.Seen, Seen = TutorialSeen.Queues,
            },
            new TutorialStepDef
            {
                Id = "claim", Chapter = "YOUR COLONY", Title = "CLAIM YOUR REWARD",
                Text = "The Commander's Path, top left, always shows your next goal. When it says CLAIM, tap it " +
                       "and collect the reward.",
                Goal = TutorialGoal.QuestsClaimed, Target = 1,
            },
            new TutorialStepDef
            {
                Id = "command-center", Chapter = "YOUR COLONY", Title = "RAISE THE COMMAND CENTER",
                Text = "No building can be a higher level than your Command Center. Tap it and UPGRADE.",
                Goal = TutorialGoal.Building, Building = BuildingId.CommandCenter, Target = 2,
            },
            new TutorialStepDef
            {
                Id = "speed-up", Chapter = "YOUR COLONY", Title = "SPEED IT UP",
                Text = "Here are two free 5-minute Speed-Ups. Tap the Command Center, then SPEED UP, and USE one.",
                Goal = TutorialGoal.BuildingBuilt, Building = BuildingId.CommandCenter, Target = 2,
            },
            new TutorialStepDef
            {
                Id = "path", Chapter = "YOUR COLONY", Title = "FOLLOW THE PATH",
                Text = "Keep following the Commander's Path up to the Shipyard. Each reward pays for the next step, " +
                       "and the Shipyard's reward is your first squadron.",
                Goal = TutorialGoal.QuestsClaimed, Target = 5,
            },

            // ---- your fleet ----
            new TutorialStepDef
            {
                Id = "fleet", Chapter = "YOUR FLEET", Title = "MEET YOUR FLEET",
                Text = "You have 10 Fighters and 2 spy Probes. Tap FLEET to see them.",
                Goal = TutorialGoal.Seen, Seen = TutorialSeen.Fleet,
            },
            new TutorialStepDef
            {
                Id = "hulls", Chapter = "YOUR FLEET", Title = "BUILDING SHIPS",
                Text = "This is where you build ships. Fighters are fast and cheap, Bombers hit hard and Cruisers take " +
                       "a beating. Probes scout, and Haulers carry cargo. Heavier hulls unlock as the Shipyard levels up.",
                Goal = TutorialGoal.Next,
            },

            // ---- the galaxy ----
            new TutorialStepDef
            {
                Id = "map", Chapter = "THE GALAXY", Title = "OPEN THE MAP",
                Text = "Tap MAP. The galaxy is full of resource worlds, pirate camps and rival colonies.",
                Goal = TutorialGoal.Seen, Seen = TutorialSeen.Map,
            },
            new TutorialStepDef
            {
                Id = "find-camp", Chapter = "THE GALAXY", Title = "FIND A PIRATE CAMP",
                Text = "Pirate camps are red volcanic worlds. Tap FIND, choose Pirate Camp, then FIND again to fly " +
                       "to the nearest one.",
                Goal = TutorialGoal.Seen, Seen = TutorialSeen.Camp,
            },
            new TutorialStepDef
            {
                Id = "spy", Chapter = "THE GALAXY", Title = "SPY FIRST",
                Text = "Tap SPY to send a Probe. It reads the camp's garrison, so you know what you'd be fighting.",
                Goal = TutorialGoal.CampScouted,
            },
            new TutorialStepDef
            {
                Id = "intel", Chapter = "THE GALAXY", Title = "READ THE INTEL",
                Text = "The scan is in your Mail. Open MAIL and tap the report.",
                Goal = TutorialGoal.Seen, Seen = TutorialSeen.SpyReport,
            },
            new TutorialStepDef
            {
                Id = "attack", Chapter = "THE GALAXY", Title = "ATTACK THE CAMP",
                Text = "Back on the MAP, tap the camp, then ATTACK. Before you launch, the forecast shows your odds " +
                       "against the garrison you scanned.",
                Goal = TutorialGoal.AttackReported,
            },
            new TutorialStepDef
            {
                Id = "report", Chapter = "THE GALAXY", Title = "THE BATTLE REPORT",
                Text = "Every battle files a report: your losses, the plunder and a replay you can watch. Tap REPORT " +
                       "to read it. Later you'll find every report in MAIL.",
                Goal = TutorialGoal.Seen, Seen = TutorialSeen.BattleReport,
            },

            new TutorialStepDef
            {
                Id = "spoils", Chapter = "THE GALAXY", Title = "COLLECT YOUR SPOILS",
                Text = "Scouting the camp finished a Commander's Path quest, and winning a battle finishes the next. " +
                       "Tap CLAIM on the Path, top left, for each one that's ready.",
                // Both rewards: Scout the Frontier (6) and First Blood (7).
                Goal = TutorialGoal.QuestsClaimed, Target = 7,
            },

            // ---- growing stronger ----
            new TutorialStepDef
            {
                Id = "research", Chapter = "GROWING STRONGER", Title = "START RESEARCHING",
                Text = "Research gives permanent bonuses. Build a Research Lab, tap it, then OPEN RESEARCH. Pick a " +
                       "technology and tap RESEARCH. Claim any Path reward that's ready: it pays for this.",
                Goal = TutorialGoal.ResearchStarted,
            },
            new TutorialStepDef
            {
                Id = "districts", Chapter = "GROWING STRONGER", Title = "THE REST OF YOUR PLANET",
                Text = "These tabs turn the globe. MINES holds your extra mines, FRONTIER your advanced buildings and " +
                       "PORT your docked fleet. The WILDS, in the south, hold deposits to survey and harvest.",
                Goal = TutorialGoal.Next,
            },
            new TutorialStepDef
            {
                Id = "more", Chapter = "GROWING STRONGER", Title = "EVERYTHING ELSE",
                Text = "The ••• button holds the rest: daily objectives, galaxy events, your clan, the Galactic Core, " +
                       "the Pirate Dreadnought, the market and the rankings. Tap it to look, then tap it again to close it.",
                Goal = TutorialGoal.Seen, Seen = TutorialSeen.More,
            },
            new TutorialStepDef
            {
                Id = "defend", Chapter = "GROWING STRONGER", Title = "GUARD YOUR COLONY",
                Text = "Rivals will raid you. The Warehouse keeps part of your stockpile out of reach, the Radar Station " +
                       "warns you before raiders land, and DEFENSE research makes them pay.",
                Goal = TutorialGoal.Next,
            },
            new TutorialStepDef
            {
                Id = "done", Chapter = "TRAINING COMPLETE", Title = "YOU'RE READY",
                Text = "Keep following the Commander's Path. The Commander's Handbook, in Settings under Help, explains " +
                       "every system. Here's a parting gift: 1,500 gold, 1,000 quartz, 600 helium and 50 Dark Matter.",
                Goal = TutorialGoal.Next,
            },
        };
    }

    /// <summary>The screens a Seen step waits for.</summary>
    public static class TutorialSeen
    {
        public const string Queues = "queues";
        public const string Fleet = "fleet";
        public const string Map = "map";
        public const string Camp = "camp";
        public const string SpyReport = "spy-report";
        public const string BattleReport = "battle-report";
        public const string More = "more";
    }
}
