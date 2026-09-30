// The Pirate Dreadnought (world boss, build-all plan 2026-09-28): where the
// current visit stands and how the last one ended. BossSystem runs it.
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Bots
{
    /// <summary>Dreadnought variants (rival events, 2026-09-30), in turn by visit.</summary>
    public enum BossVariant { Dreadnought, Carrier, Siege, Stealth }

    public sealed class BossState
    {
        /// <summary>This visit's variant (BossSystem.VariantFor).</summary>
        public BossVariant Variant;
        /// <summary>The Siege variant's next bombardment.</summary>
        public int NextSiegeTick;
        /// <summary>The current (or last) visit's number; 0 = none has come yet.</summary>
        public int Visit;
        public bool Active;
        public TileXY Tile;
        public long MaxHp;
        public long Hp;
        /// <summary>Its guns: damage per round against a striking fleet (more as its hull fails).</summary>
        public int Cannon;
        public int ArrivedTick;
        public int LeavesTick;
        /// <summary>When the next one drops in (0 = not scheduled yet).</summary>
        public int NextVisitTick;
        public int NextRollTick;
        /// <summary>Damage dealt this visit, by empire (0 = you, &gt;0 = a commander).</summary>
        public Dictionary<int, long> Damage = new();

        // The last visit's outcome (for the panel once it's gone).
        public bool LastKilled;
        public string? LastTopClan;
        public long LastYourDamage;
        public int LastYourRank;
        public int LastRewardDM;
    }
}
