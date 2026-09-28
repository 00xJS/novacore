// Difficulty (build-all plan, 2026-09-28): how hard the galaxy leans on YOU.
// Chosen at NEW GAME, changeable from the profile. It only changes how rival
// commanders treat the player (and what the player's vaults protect, and the
// XP they earn); rivals fight each other the same way on every setting.
namespace GalaxyRoyale.Data
{
    public enum Difficulty { Standard, Easy, Brutal }

    public static class Difficulties
    {
        public static readonly Difficulty[] All = { Difficulty.Easy, Difficulty.Standard, Difficulty.Brutal };

        public static string Name(Difficulty d) => d switch
        {
            Difficulty.Easy => "EASY",
            Difficulty.Brutal => "BRUTAL",
            _ => "STANDARD",
        };

        public static string Pitch(Difficulty d) => d switch
        {
            Difficulty.Easy => "Rivals raid you less often, only when they clearly outgun you, and your vaults hold more.",
            Difficulty.Brutal => "Rivals raid you more often, pick you first and send more of their fleet. +50% commander XP.",
            _ => "The galaxy as designed: rivals raid whoever looks richest and weakest.",
        };

        /// <summary>Least time between rival raids aimed at you (BotSystem.InboundCooldownTicks on Standard).</summary>
        public static int InboundCooldownTicks(Difficulty d) => d switch
        {
            Difficulty.Easy => 3600,
            Difficulty.Brutal => 1200,
            _ => 2100,
        };

        /// <summary>How far a raider's fleet must out-power your defence before it attacks you
        /// (BotSystem.BeatabilityEdge on Standard).</summary>
        public static double BeatabilityEdge(Difficulty d) => d switch
        {
            Difficulty.Easy => 1.4,
            Difficulty.Brutal => 1.0,
            _ => 1.15,
        };

        /// <summary>How rich you look next to other targets when a raider picks one.</summary>
        public static double LootWeight(Difficulty d) => d switch
        {
            Difficulty.Easy => 0.6,
            Difficulty.Brutal => 1.6,
            _ => 1.0,
        };

        /// <summary>Share of their docked warships a raider sends against you (BotSystem.RaidCommitFraction on Standard).</summary>
        public static double RaidCommit(Difficulty d) => d switch
        {
            Difficulty.Brutal => 0.85,
            _ => 0.7,
        };

        /// <summary>Rivals leave you alone below this might (BotSystem.PlayerShieldMight on Standard).</summary>
        public static long ShieldMight(Difficulty d) => d switch
        {
            Difficulty.Easy => 1500,
            _ => 500,
        };

        /// <summary>Multiplier on what your warehouse keeps safe from plunder.</summary>
        public static double VaultMult(Difficulty d) => d == Difficulty.Easy ? 1.5 : 1.0;

        /// <summary>Multiplier on commander XP.</summary>
        public static double XpMult(Difficulty d) => d == Difficulty.Brutal ? 1.5 : 1.0;
    }
}
