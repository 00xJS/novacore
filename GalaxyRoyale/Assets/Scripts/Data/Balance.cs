// Every global formula and constant in one tunable module. Direct C# port of
// iGalaxy v1's `src/data/balance.ts`. Amounts here are WHOLE units unless a
// name says Milli; the sim converts once at system boundaries.
using System;
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public static class Balance
    {
        public const int TickSeconds = 1;
        public const int OfflineCapHours = 8;
        public const int AutosaveIntervalTicks = 10;

        // Building curves (L = target level, base = level-1 value)
        public const float CostGrowth = 1.6f;   // cost(L) = ceil(base * 1.6^(L-1))
        public const float TimeGrowth = 1.5f;   // time(L) = ceil(baseTime * 1.5^(L-1)) seconds

        public static int ProdPerHour(int baseVal, int L) =>
            L < 1 ? 0 : (int)Math.Floor(baseVal * (double)L * Math.Pow(1.1, L - 1));

        public static int StorageBonus(int baseVal, int L) =>
            L < 1 ? 0 : (int)Math.Floor(baseVal * Math.Pow(1.8, L - 1));

        public static int EnergyOut(int L) =>
            L < 1 ? 0 : (int)Math.Floor(30.0 * L * Math.Pow(1.1, L - 1));

        public static int EnergyUse(int baseVal, int L) => L < 1 ? 0 : baseVal * L;

        /// <summary>Storage floor every planet has even with no warehouse (whole units, per resource).</summary>
        public const int BaseStorage = 2000;

        // Marches
        public static int TravelSeconds(double distTiles, int slowestTilesPerMin) =>
            Math.Max(5, (int)Math.Ceiling(distTiles / slowestTilesPerMin * 60.0));

        /// <summary>Global flight-fuel scalar (user spec 2026-07-07: drastically lower
        /// helium so low gas never grounds a fleet — flying should feel free). One
        /// tunable knob instead of editing every ship's FuelPerTile.</summary>
        public const float FlightFuelMult = 0.2f;

        /// <summary>Round-trip helium, charged up front at launch (milli-helium).</summary>
        public static int HeliumCostMilli(double distTiles, int fleetFuelPerTile) =>
            (int)Math.Ceiling(2.0 * distTiles * fleetFuelPerTile * 1000 * FlightFuelMult);

        /// <summary>Fraction of the helium attributable to un-flown distance refunded on
        /// recall. Bumped 0.5 → 0.85 (user spec: returning a flight refunds gas for
        /// the distance it never had to fly).</summary>
        public const float RecallRefundRate = 0.85f;

        // Combat
        public const float CounterMultiplier = 2.0f;
        public const float HaulerVulnMultiplier = 1.25f;
        public const int MaxCombatRounds = 10;

        // Radar Station (content expansion, 2026-07-05)
        /// <summary>Warning lead per radar level: L1 warns 30s before arrival, L10 = 5 min.</summary>
        public const int RadarLeadPerLevelSec = 30;
        /// <summary>Own spy probes fly this much faster per radar level.</summary>
        public const float RadarProbeSpeedPerLevel = 0.05f;

        // Battle aftermath + planetary shield (single-player pivot batch 3)
        /// <summary>A planet that LOSES a defense burns on the map for this long.</summary>
        public const int BurnDurationSec = 4 * 3600;
        /// <summary>Aegis Shield item durations (usable any time; breaks if the owner raids).</summary>
        public const int ShieldShortSec = 8 * 3600;
        public const int ShieldLongSec = 24 * 3600;
        /// <summary>The galaxy news feed keeps this many battle reports.</summary>
        public const int NewsCap = 100;

        // Defense research: Orbital Batteries (planetary guns). Damage per round
        // per level, split across the raiders by HP share, through ship shields.
        public const int BatteryDamagePerLevel = 250;
        /// <summary>With no defending fleet docked, the batteries still get this
        /// many rounds of fire before the raiders land.</summary>
        public const int BatteryOnlyRounds = 3;

        // Prototype/testing conveniences. Testing economy per user request
        // (2026-07-07): 500K of each resource + 1M Dark Matter + a stack of
        // every speed-up token so nothing gates a playtest. ALL of this must be
        // OFF at release.
        public const bool TestMode = true;
        public const int TestModeDarkMatter = 1_000_000;
        public const int TestModeResources = 500_000;
        public const int TestModeSpeedupCount = 10;

        /// <summary>Simulated commanders open with this much of each resource
        /// (user spec 2026-07-07: 250K "to make it interesting" — bots rocket out
        /// of the gate and the early leaderboard has teeth).</summary>
        public const int BotStartResources = 250_000;

        // Timed production buff (shop item)
        public const float ProdBoostFactor = 1.25f;
        public const int ProdBoostDurationSec = 3600;

        // Timed energy buff (shop item)
        public const float EnergyBoostFactor = 1.5f;
        public const int EnergyBoostDurationSec = 86400; // 24h

        // Concurrent queues — standardized 2026-07-08 (user spec: one queue paced
        // the whole game too slow). EVERYONE (player + bots) runs TWO parallel
        // build, research, and ship queues; the shop buffs now unlock a THIRD
        // build/research slot for a day (player-exclusive).
        public const int BaseBuildSlots = 2;
        public const int MaxBuildSlots = 3;
        public const int ExtraBuildSlotDurationSec = 86400;

        public const int BaseResearchSlots = 2;
        public const int MaxResearchSlots = 3;
        public const int ExtraResearchSlotDurationSec = 86400;

        /// <summary>Parallel ship production lines (no shop item — 2 for everyone).</summary>
        public const int ShipQueueSlots = 2;

        // Map & risk tiering — the galactic core (sector center) is the endgame zone.
        // 2500×2500 tiles gives room for thousands of players + tens of thousands
        // of resource nodes + camps. Rendering uses camera culling + zoom LOD.
        public const int SectorSize = 2500;
        /// <summary>Half-size of the 100×100 forbidden square around the supernova core —
        /// no relocation, holds, raids, or spawns inside (future kingdom stronghold zone).</summary>
        public const int CoreExclusionHalf = 50;
        public static readonly TileXY HomeTile = new TileXY(500, 1250); // outer rim, tier 0
        /// <summary>Cell size for jittered-grid node placement — one node per NxN cell.</summary>
        public const int NodeCellSize = 20;

        /// <summary>Tier 0 (safe outer rim) … 4 (rich, deadly core).</summary>
        public static int TierOf(int distFromCore) => Math.Max(0, 4 - distFromCore / 300);

        public static readonly float[] TierRichnessMult = { 1f, 1.6f, 2.5f, 4f, 6f };
        public static readonly int[] TierCampLevel = { 1, 2, 3, 4, 5 };

        // Multiple production buildings (gold/quartz/helium)
        public const int MaxMinesPerType = 5;
        /// <summary>CC level required to unlock the Nth instance of a mine type.</summary>
        public static readonly int[] MineSlotUnlocks = { 0, 3, 6, 10, 15 };

        // Dynamic map: depleted tiles / cleared camps come back after this long
        public const int NodeRespawnSec = 21600; // 6 hours

        // New game (whole units, per resource)
        public static ResourceBag StartResources() => new(500, 300, 100);
    }

    /// <summary>Integer tile coordinate on the sector grid.</summary>
    public readonly struct TileXY : IEquatable<TileXY>
    {
        public readonly int X;
        public readonly int Y;
        public TileXY(int x, int y) { X = x; Y = y; }

        public bool Equals(TileXY o) => X == o.X && Y == o.Y;
        public override bool Equals(object? o) => o is TileXY t && Equals(t);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X},{Y})";

        /// <summary>Euclidean tile distance — matches v1's tileDist in `src/sim/types.ts`.</summary>
        public static double Distance(TileXY a, TileXY b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public string Key() => $"{X},{Y}";
    }

    public enum ResourceId { Gold, Quartz, Helium }

    public static class Resources
    {
        public static readonly IReadOnlyList<ResourceId> All =
            new[] { ResourceId.Gold, ResourceId.Quartz, ResourceId.Helium };
    }

    /// <summary>
    /// Resource triple. Mutable — the sim mutates state.Resources directly. Definitions
    /// (BuildingDef.BaseCost, ShipDef.Cost, etc.) hold instances that should be treated
    /// as read-only by convention; callers Clone() before mutating.
    /// Fields are long: milli-unit totals exceed int range (10M-unit shop packs alone
    /// are 10¹⁰ milli). v1 rode on JS float64; long matches its integer range safely.
    /// </summary>
    public sealed class ResourceBag
    {
        public long Gold;
        public long Quartz;
        public long Helium;

        public ResourceBag() { }
        public ResourceBag(long gold, long quartz, long helium) { Gold = gold; Quartz = quartz; Helium = helium; }

        public ResourceBag Clone() => new(Gold, Quartz, Helium);
        public long Total => Gold + Quartz + Helium;

        /// <summary>Return a new bag with amounts converted from whole units → milli-units.</summary>
        public ResourceBag Milli() => new(Gold * 1000, Quartz * 1000, Helium * 1000);

        public long Get(ResourceId id) => id switch
        {
            ResourceId.Gold   => Gold,
            ResourceId.Quartz => Quartz,
            ResourceId.Helium     => Helium,
            _ => 0,
        };

        public void Set(ResourceId id, long v)
        {
            switch (id)
            {
                case ResourceId.Gold:   Gold = v; break;
                case ResourceId.Quartz: Quartz = v; break;
                case ResourceId.Helium:     Helium = v; break;
            }
        }

        public void Add(ResourceBag other)
        {
            Gold   += other.Gold;
            Quartz += other.Quartz;
            Helium     += other.Helium;
        }

        public ResourceBag Scaled(float f) => new(
            (long)Math.Ceiling(Gold   * (double)f),
            (long)Math.Ceiling(Quartz * (double)f),
            (long)Math.Ceiling(Helium     * (double)f));
    }
}
