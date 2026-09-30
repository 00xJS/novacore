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
        /// <summary>What happened at the core, newest first (capped at
        /// CoreSystem.MaxHistory). Names are snapshots, as tagged at the time.</summary>
        public List<CoreLogEntry> History = new();
        /// <summary>Core Tournament (rival events, 2026-09-30): the event instance of the
        /// tournament under way, -1 when none is.</summary>
        public int TournamentInstance = -1;
    }

    public enum CoreLogKind
    {
        /// <summary>Actor took the core from Other (who had held it HeldSec).</summary>
        Seized,
        /// <summary>Actor's assault broke on Other's defence; Actor lost ShipsLost.</summary>
        Repelled,
        /// <summary>Actor left the core after HeldSec; the guardians returned.</summary>
        Abandoned,
        /// <summary>The Core Guardians rebuilt to full strength.</summary>
        Rebuilt,
        /// <summary>A Core Tournament began: the holder (Other) was thrown out.</summary>
        TournamentOpened,
        /// <summary>Actor held the Core when the tournament ended.</summary>
        TournamentWon,
    }

    public sealed class CoreLogEntry
    {
        public int AtTick;
        public CoreLogKind Kind;
        /// <summary>0 = the player, &gt;0 = a bot, -1 = the Core Guardians.</summary>
        public int ActorId = -1;
        public string Actor = "";
        public string Other = "";
        public int HeldSec;
        public int ShipsLost;
    }
}
