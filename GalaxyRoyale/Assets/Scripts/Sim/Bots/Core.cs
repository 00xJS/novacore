// The Galactic Core (endgame, 2026-09-28): an ancient station at the heart of
// the galaxy. Whoever holds it — the Core Guardians, a simulated commander, or
// the player — defends it with the fleet they left there, and the holder earns
// tribute every hour. Rules live in Sim/Systems/CoreSystem.cs.
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Bots
{
    public sealed class CoreState
    {
        /// <summary>-1 = the Core Guardians (nobody holds it), 0 = the player, &gt;0 = a bot.</summary>
        public int HolderId = -1;
        public int HeldSinceTick;
        /// <summary>A bot holder's garrison (the player's garrison is their Core march).</summary>
        public Dictionary<HullId, int> Garrison = new();
        /// <summary>The guardians' fleet while nobody holds the core — worn down by
        /// failed assaults — and when it is next rebuilt to full strength.</summary>
        public Dictionary<HullId, int> Guardians = new();
        public int GuardiansRebuildTick;
        /// <summary>Next time a simulated commander considers an assault (galaxy time).</summary>
        public int NextRollTick;
        /// <summary>Next hourly tribute to the holder.</summary>
        public int NextTributeTick;
        public int TimesSeized;
    }
}
