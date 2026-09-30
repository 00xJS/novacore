// The campaign, "The Long Night" (late-game content, user-approved 2026-09-30).
// Ten chapters starring the player's commander, told by HALCYON, the colony's
// ship-mind. Each opens once the colony is ready (a Command Center level) and
// time has passed (galaxy days), sets three objectives counted from the
// chapter's start, and ends with a Pirate Lord (Data/PirateLords) in their
// lair. The content-cadence run (Tests/ContentCadenceTests) found nothing new
// after day 18; the chapters are spread from day 2 to day 68 to fill that.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum CampaignGoal
    {
        DefeatLord,     // the chapter's lord, in their lair
        CampsCleared,   // since the chapter began
        BattlesWon,
        Gathered,       // whole units hauled home from gathering
        WildsSurveyed,
        Expeditions,
        BossStrikes,
        EventsCompleted,
        ShipsBuilt,
        ContractsDone,
        ResearchLevels,
        Relics,         // relics owned, all kinds (not since the start)
        TerraformStage, // the Terraformer's stage (not since the start)
    }

    public sealed class CampaignObjective
    {
        public CampaignGoal Goal;
        public long Target = 1;
        public string Text = "";
    }

    public sealed class ChapterDef
    {
        public int Number;
        public string Title = "";
        public int UnlockCc;
        /// <summary>Galaxy days since the colony was founded.</summary>
        public int UnlockDay;
        public int Lord;
        /// <summary>HALCYON's briefing when the chapter opens, and its word when it closes.</summary>
        public string Intro = "", Outro = "";
        public IReadOnlyList<CampaignObjective> Objectives = new CampaignObjective[0];
        /// <summary>Hours of the colony's mine output the chapter pays, and its Dark Matter.</summary>
        public int RewardHours;
        public int RewardDM;
    }

    public static class Campaign
    {
        public const string Name = "The Long Night";
        public const string Narrator = "HALCYON";

        static CampaignObjective O(CampaignGoal goal, long target, string text) =>
            new() { Goal = goal, Target = target, Text = text };

        static CampaignObjective Lord(int lord) =>
            O(CampaignGoal.DefeatLord, 1, $"Storm the lair of {PirateLords.Def(lord).FullName}");

        public static readonly IReadOnlyList<ChapterDef> Chapters = new[]
        {
            new ChapterDef
            {
                Number = 1, Title = "Smoke on the Rim", UnlockCc = 5, UnlockDay = 2, Lord = 0,
                Intro = "Commander, the rim camps have gone quiet, and pirates are never quiet. Someone is buying their loyalty. " +
                        "Our scouts traced the money to a scrapyard lair run by Grisha Vane, the Rust Queen. Thin out her camps, then pay her a visit.",
                Outro = "The Rust Queen's yard is burning. In her logs: payments from someone who signs only as \"the Court\". " +
                        "I don't like it, Commander. Pirates don't have courts.",
                Objectives = new[] { O(CampaignGoal.CampsCleared, 5, "Clear 5 pirate camps"), O(CampaignGoal.BattlesWon, 8, "Win 8 battles"), Lord(0) },
                RewardHours = 12, RewardDM = 150,
            },
            new ChapterDef
            {
                Number = 2, Title = "The Bone Collector", UnlockCc = 7, UnlockDay = 6, Lord = 1,
                Intro = "The Court's money moved on to Old Marrow, a salvager who strips colonies to the frame. " +
                        "He's hoarding hulls for someone. Starve him: out-gather his crews, then crack his armoured yard.",
                Outro = "Marrow's hoard was a shipyard order: hundreds of hulls, delivery to \"the Court, at the Long Night\". " +
                        "Whatever the Long Night is, it's being built with stolen ships.",
                Objectives = new[] { O(CampaignGoal.Gathered, 60_000, "Haul 60,000 resources home"), O(CampaignGoal.ShipsBuilt, 60, "Build 60 ships"), Lord(1) },
                RewardHours = 14, RewardDM = 175,
            },
            new ChapterDef
            {
                Number = 3, Title = "Nightjar", UnlockCc = 9, UnlockDay = 12, Lord = 2,
                Intro = "Someone has been reading our fleet orders before we send them. The leak is a raider called Kessa Nightjar, " +
                        "hiding among the rim's resource fields. Work those fields and clear her camps until she has nowhere left to hide.",
                Outro = "Nightjar's relay was listening to every colony on the rim, not just ours, and sending it all to the core. " +
                        "The Court has ears everywhere, Commander. Now it has one less.",
                // (Not Wilds surveys: a colony that has charted every sector can't survey more.)
                Objectives = new[] { O(CampaignGoal.Gathered, 120_000, "Haul 120,000 resources home"), O(CampaignGoal.CampsCleared, 8, "Clear 8 pirate camps"), Lord(2) },
                RewardHours = 16, RewardDM = 200,
            },
            new ChapterDef
            {
                Number = 4, Title = "Twin Stars", UnlockCc = 11, UnlockDay = 18, Lord = 3,
                Intro = "Two fleet admirals deserted the old navy with their battle groups: Oro and Ash. The Court hired both. " +
                        "Their fleets cover each other. Learn how big fleets fight: strike the dreadnought, send an expedition, then split the twins.",
                Outro = "Ash is gone and Oro surrendered. Oro talked: the Court is a council of pirate lords under one master, " +
                        "the Pale Sovereign. Nobody who met the Sovereign came back to describe them.",
                Objectives = new[] { O(CampaignGoal.BossStrikes, 1, "Strike a dreadnought"), O(CampaignGoal.Expeditions, 1, "Bring an expedition home"), Lord(3) },
                RewardHours = 18, RewardDM = 225,
            },
            new ChapterDef
            {
                Number = 5, Title = "Cinders", UnlockCc = 12, UnlockDay = 25, Lord = 4,
                Intro = "Brother Cinder preaches that the galaxy must burn before the Long Night. His bomber cults are torching trade lanes. " +
                        "Keep the lanes open: deliver contracts, win the galaxy's events, then silence the preacher.",
                Outro = "Cinder's sermons named the Long Night's date. It's not a date. It's a star: the Sovereign plans to ignite one " +
                        "and blind every radar in the galaxy. We have time. Not much.",
                Objectives = new[] { O(CampaignGoal.ContractsDone, 2, "Deliver 2 trade contracts"), O(CampaignGoal.EventsCompleted, 1, "Complete a galaxy event goal"), Lord(4) },
                RewardHours = 20, RewardDM = 250,
            },
            new ChapterDef
            {
                Number = 6, Title = "The Hollow Crown", UnlockCc = 14, UnlockDay = 32, Lord = 5,
                Intro = "The Hollow King rules a graveyard of dreadnoughts and calls it a kingdom. He guards the Court's shipyards. " +
                        "Match his steel: build a real battle fleet and reshape your world with the Terraformer.",
                Outro = "The King's throne was a dreadnought bridge, still warm. Its nav logs point inward, to the Court itself. " +
                        "Commander, you're the only one out here who has beaten four of them.",
                Objectives = new[] { O(CampaignGoal.ShipsBuilt, 250, "Build 250 ships"), O(CampaignGoal.TerraformStage, 2, "Reach terraform stage 2"), Lord(5) },
                RewardHours = 22, RewardDM = 275,
            },
            new ChapterDef
            {
                Number = 7, Title = "Deep Signal", UnlockCc = 15, UnlockDay = 40, Lord = 6,
                Intro = "A signal is waking old relics across the galaxy. Vesper Lyse, the Signal Witch, is broadcasting it. " +
                        "Find the relics before she does, and outthink her destroyer packs.",
                Outro = "Lyse's signal was a summons. Every lord still standing is gathering at the Court. " +
                        "They're afraid, Commander. Of you.",
                Objectives = new[] { O(CampaignGoal.Relics, 4, "Own 4 relics"), O(CampaignGoal.ResearchLevels, 6, "Complete 6 research levels"), Lord(6) },
                RewardHours = 24, RewardDM = 300,
            },
            new ChapterDef
            {
                Number = 8, Title = "Iron Tide", UnlockCc = 17, UnlockDay = 48, Lord = 7,
                Intro = "Warlord Dray commands the Court's army: every hull class, in numbers we've never faced. " +
                        "The tide is coming for the rim. Break its outriders, then break the tide.",
                Outro = "The Iron Tide broke on our guns. Dray's last order was to the Pale Herald: \"Tell the Sovereign the rim is lost.\"",
                Objectives = new[] { O(CampaignGoal.CampsCleared, 15, "Clear 15 pirate camps"), O(CampaignGoal.BattlesWon, 20, "Win 20 battles"), Lord(7) },
                RewardHours = 26, RewardDM = 325,
            },
            new ChapterDef
            {
                Number = 9, Title = "The Pale Court", UnlockCc = 19, UnlockDay = 58, Lord = 8,
                Intro = "The Pale Herald speaks for the Sovereign and commands the Court's elite. Beyond the Herald is the Sovereign alone. " +
                        "Prepare everything, Commander: research, ships, allies. This is the last door.",
                Outro = "The Herald fell with a message for you: \"The Sovereign will receive you personally.\" " +
                        "The Court's star is ready to burn. It's now or never.",
                Objectives = new[] { O(CampaignGoal.ResearchLevels, 10, "Complete 10 research levels"), O(CampaignGoal.EventsCompleted, 2, "Complete 2 galaxy event goals"), Lord(8) },
                RewardHours = 30, RewardDM = 400,
            },
            new ChapterDef
            {
                Number = 10, Title = "The Long Night Ends", UnlockCc = 21, UnlockDay = 68, Lord = 9,
                Intro = "The Pale Sovereign waits at the Court with everything the lords ever stole. If the star burns, the galaxy goes dark for a generation. " +
                        "Every commander who ever raided you is watching. Show them a morning.",
                Outro = "The star didn't burn. The Court is scattered, the lords are broken, and the galaxy is still yours to fight over. " +
                        "Thank you, Commander. It was an honour to be your ship-mind.",
                Objectives = new[] { O(CampaignGoal.BattlesWon, 25, "Win 25 battles"), O(CampaignGoal.ShipsBuilt, 400, "Build 400 ships"), Lord(9) },
                RewardHours = 36, RewardDM = 600,
            },
        };
    }
}
