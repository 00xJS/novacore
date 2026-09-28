// Clans (user request 2026-09-28: "max limit 15 per alliance group"): groups
// of up to 15 commanders — the simulated ones and, if they join or found one,
// the player. Rules live in Sim/Systems/ClanSystem.cs.
using System.Collections.Generic;

namespace GalaxyRoyale.Sim.Bots
{
    public sealed class Clan
    {
        public int Id;
        public string Name = "";
        /// <summary>2–4 capitals shown before members' names: [VOID].</summary>
        public string Tag = "";
        /// <summary>Leader: 0 = the player, &gt;0 = a bot id.</summary>
        public int LeaderId;
        public int FoundedTick;
        /// <summary>The clan it's at war with (0 = at peace), when that war ends,
        /// and this side's victories in it.</summary>
        public int WarWithClanId;
        public int WarEndsTick;
        public int WarScore;
        /// <summary>No new war before this tick (a breather after the last one).</summary>
        public int WarCooldownUntilTick;
    }

    /// <summary>Deterministic names for the simulated clans. ASCII only (the
    /// runtime font tofu-boxes anything fancier).</summary>
    public static class ClanNames
    {
        public static readonly string[] First =
        {
            "Void", "Nova", "Iron", "Crimson", "Solar", "Quantum", "Obsidian", "Stellar",
            "Nebula", "Eclipse", "Astral", "Photon", "Onyx", "Cobalt", "Zenith", "Radiant",
            "Silent", "Ember", "Frost", "Shadow", "Titan", "Orbit", "Pulsar", "Comet",
            "Aurora", "Vortex", "Helix", "Warp", "Binary", "Parsec",
        };

        public static readonly string[] Second =
        {
            "Syndicate", "Pact", "Legion", "Armada", "Dominion", "Covenant", "Order", "Guild",
            "Accord", "Vanguard", "Collective", "Consortium", "Directorate", "Horde", "League",
            "Compact", "Union", "Fleet", "Hegemony", "Circle",
        };

        /// <summary>"VOID" from "Void", "CRIM" from "Crimson".</summary>
        public static string TagOf(string firstWord) =>
            (firstWord.Length > 4 ? firstWord.Substring(0, 4) : firstWord).ToUpperInvariant();
    }
}
