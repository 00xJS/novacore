// Timed galaxy events (user request 2026-09-28, "timed events and seasons"): a
// weekly rotation on galaxy time, after a quiet lead-in. Each event bends one
// rule for EVERY empire — the rivals feel it too — and carries a goal with a
// reward. Rewards are whole units plus Dark Matter.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum GalaxyEventKind { None, GoldRush, ResearchSurge, PirateArmada, WarGames }

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
    }

    public static class GalaxyEvents
    {
        public const float GoldRushProduction = 1.5f;
        public const float ResearchSurgeTime = 0.7f;
        public const float PirateArmadaLoot = 2f;
        public const float WarGamesLoot = 1.5f;

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

        /// <summary>The weekly rotation (after the lead-in), in order; durations sum to one week.</summary>
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
    }
}
