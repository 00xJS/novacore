// Timed galaxy events (user request 2026-09-28, "timed events and seasons"): a
// weekly rotation on galaxy time, after a quiet lead-in. Each event bends one
// rule for EVERY empire — the rivals feel it too — and carries a goal with a
// reward. Rewards are whole units plus Dark Matter.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum GalaxyEventKind
    {
        None, GoldRush, ResearchSurge, PirateArmada, WarGames,
        // Map events (2026-09-30): each puts something on the map near you.
        CometPass, TradeCaravan, IonStorm, Supernova,
        // Rival events (2026-09-30): the galaxy's commanders at the centre of it.
        BountyBoard, CoreTournament,
    }

    public sealed class GalaxyEventDef
    {
        public GalaxyEventKind Kind;
        public string Name = "";
        /// <summary>The rule it bends, one line.</summary>
        public string Effect = "";
        /// <summary>The goal, phrased for the progress line ("Clear 3 pirate camps").</summary>
        public string Goal = "";
        public int Target;
        public int DurationSec;
        public ResourceBag Reward = new();
        public int RewardDM;
        /// <summary>On top of Reward: this many hours of the colony's mine output, split in
        /// Reward's proportions (user 2026-09-30: fixed rewards were far too small for a
        /// grown colony). 0 = the default for its length (EventSystem.RewardHours).</summary>
        public double RewardHours;
    }

    public static class GalaxyEvents
    {
        public const float GoldRushProduction = 1.5f;
        public const float ResearchSurgeTime = 0.7f;
        public const float PirateArmadaLoot = 2f;
        public const float WarGamesLoot = 1.5f;

        // ---- map events (2026-09-30), placed by Sim/Systems/EventSites ----
        /// <summary>Comet Pass: a comet parks this far from your colony (tiles).</summary>
        public const int CometMinDist = 50, CometMaxDist = 110;
        /// <summary>Its stock: this many hours of your colony's mine output (at least 20,000).</summary>
        public const double CometHoursOfOutput = 10;
        /// <summary>Rival commanders mine it too: this share of its stock goes each hour.</summary>
        public const double CometRivalDrainPerHour = 0.03;
        /// <summary>A haul from the comet splits 40% gold, 35% quartz, 25% helium, plus
        /// 1 Dark Matter per this many units.</summary>
        public const int CometUnitsPerDarkMatter = 400;

        /// <summary>Trade Caravan: it docks at this many waystations, this long each.</summary>
        public const int CaravanStops = 4, CaravanStopSec = 6 * 3600;
        public const int CaravanMinDist = 40, CaravanMaxDist = 100;

        /// <summary>Ion Storm: a disc this wide (tiles) whose centre sits this far from your colony.</summary>
        public const int StormRadius = 220, StormOffset = 110;
        /// <summary>Fleets flying into or out of the storm fly at this share of their speed.</summary>
        public const float StormSpeed = 0.7f;
        /// <summary>Camps inside the storm carry this much more loot.</summary>
        public const float StormCampLoot = 1.5f;

        /// <summary>Supernova: the doomed sector's radius, and how far its centre sits from your colony.</summary>
        public const int NovaRadius = 70, NovaMinDist = 110, NovaMaxDist = 170;
        /// <summary>Gathering in the doomed sector runs this much faster…</summary>
        public const float NovaGatherRate = 3f;
        /// <summary>…and each haul from it pays this much extra when it lands home.</summary>
        public const float NovaHaulBonus = 0.5f;

        // ---- rival events (2026-09-30) ----
        /// <summary>Bounty Board: the most-wanted raider is picked from commanders this close (tiles).</summary>
        public const int BountyRange = 450;
        /// <summary>…and whose might is within this factor of yours, either way.</summary>
        public const double BountyMightBand = 2.0;
        /// <summary>The bounty: this many camp stockpiles (at your Command Center) plus Dark Matter.</summary>
        public const int BountyStockpiles = 4, BountyDarkMatter = 150;
        /// <summary>Rivals hunt the marked commander this many times as keenly.</summary>
        public const double BountyHuntWeight = 3.0;

        /// <summary>Core Tournament: the guardians reset to this share of full strength.</summary>
        public const double TournamentGuardians = 0.5;
        /// <summary>Holding the Core when it ends: this many hours of your production, plus Dark Matter.</summary>
        public const double TournamentPrizeHours = 12;
        public const int TournamentPrizeDarkMatter = 400;
        /// <summary>A clanmate holding it pays you this much Dark Matter.</summary>
        public const int TournamentClanDarkMatter = 100;

        /// <summary>A new galaxy opens with this long of quiet skies before the
        /// first event. Besides giving a new commander the plain economy first,
        /// it keeps the rivals' pre-simulated head start (up to
        /// BotSystem.MaxPreSimTicks) free of event boosts — a Gold Rush there
        /// would hand every rival 50% more opening production.</summary>
        public const int LeadInSec = 48 * 3600;

        /// <summary>What's "live" during the lead-in: no rule bent, no goal.</summary>
        public static readonly GalaxyEventDef Quiet = new()
        {
            Kind = GalaxyEventKind.None, Name = "Quiet Skies",
            Effect = "No galaxy event yet — the first one begins soon",
            DurationSec = LeadInSec,
        };

        /// <summary>The original weekly four (Rotation interleaves the map events between them).</summary>
        public static readonly IReadOnlyList<GalaxyEventDef> Cycle = new[]
        {
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.GoldRush, Name = "Gold Rush",
                Effect = "+50% resource production for every empire",
                Goal = "Complete building upgrades", Target = 3, DurationSec = 48 * 3600,
                Reward = new ResourceBag(2000, 1000, 0), RewardDM = 25,
            },
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.ResearchSurge, Name = "Research Surge",
                Effect = "Research started now takes 30% less time",
                Goal = "Complete research levels", Target = 2, DurationSec = 24 * 3600,
                Reward = new ResourceBag(0, 1500, 600), RewardDM = 25,
            },
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.PirateArmada, Name = "Pirate Armada",
                Effect = "Pirate camps carry double loot",
                Goal = "Clear pirate camps", Target = 3, DurationSec = 48 * 3600,
                Reward = new ResourceBag(2000, 0, 800), RewardDM = 40,
            },
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.WarGames, Name = "War Games",
                Effect = "Raiding fleets haul 50% more loot — yours and theirs",
                Goal = "Win battles", Target = 3, DurationSec = 48 * 3600,
                Reward = new ResourceBag(1500, 1500, 1000), RewardDM = 50,
            },
        };

        /// <summary>The two-week rotation since the map events (2026-09-30), in order.</summary>
        public static readonly IReadOnlyList<GalaxyEventDef> Rotation = new[]
        {
            Cycle[0], // Gold Rush
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.CometPass, Name = "Comet Pass",
                Effect = "A comet rich in every resource and Dark Matter parks near your colony. Rivals are mining it too",
                Goal = "Haul from the comet", Target = 5000, DurationSec = 24 * 3600,
                Reward = new ResourceBag(2000, 1500, 800), RewardDM = 40,
            },
            Cycle[1], // Research Surge
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.TradeCaravan, Name = "Trade Caravan",
                Effect = "A rich convoy docks at four waystations near you, six hours each. ATTACK it, or ESCORT it for a fee",
                Goal = "Intercept or escort it", Target = 1, DurationSec = 24 * 3600,
                Reward = new ResourceBag(2500, 1500, 1000), RewardDM = 40,
            },
            Cycle[2], // Pirate Armada
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.IonStorm, Name = "Ion Storm",
                Effect = "A storm darkens your region: fleets fly 30% slower through it, radar can't see raids coming, and its camps carry 50% more loot",
                Goal = "Clear camps in the storm", Target = 3, DurationSec = 24 * 3600,
                Reward = new ResourceBag(2000, 2000, 1000), RewardDM = 45,
            },
            Cycle[3], // War Games
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.Supernova, Name = "Supernova Warning",
                Effect = "A star near you will explode when the event ends. Its worlds gather 3x faster and pay 50% more. Any fleet still there is lost",
                Goal = "Haul from the doomed sector", Target = 8000, DurationSec = 24 * 3600,
                Reward = new ResourceBag(2500, 2000, 1200), RewardDM = 50,
            },
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.BountyBoard, Name = "Bounty Board",
                Effect = "The rim's most-wanted raider has a price on their head. Win a raid on them to collect, before the rivals do",
                Goal = "Claim the bounty", Target = 1, DurationSec = 24 * 3600,
                Reward = new ResourceBag(2500, 2000, 1500), RewardDM = 50,
            },
            new GalaxyEventDef
            {
                Kind = GalaxyEventKind.CoreTournament, Name = "Core Tournament",
                Effect = "The Core's holder is thrown out and its guardians fall to half strength. Whoever holds it when the tournament ends wins a prize",
                Goal = "Seize the Galactic Core", Target = 1, DurationSec = 48 * 3600,
                Reward = new ResourceBag(4000, 3000, 2000), RewardDM = 100,
            },
        };
    }
}
