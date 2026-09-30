// Map node definitions — resource tiles, helium clouds, derelict hulks, pirate camps.
// Ported from `src/data/nodes.ts`. Tier weights control which node kinds appear
// at each risk tier (0 outer rim → 4 core).
using System;
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum NodeKind
    {
        Asteroid,   // gold
        Nebula,     // quartz
        HeliumCloud,   // helium
        Derelict,   // mixed salvage
        Camp,       // hostile — attack for loot
        DMField,    // rare Dark Matter field — the F2P premium-currency path
        // Galaxy events (2026-09-30), placed and removed by EventSites — never
        // generated, so the map's seed layout is untouched:
        Comet,      // Comet Pass: gather a mix of all three resources + Dark Matter
        Caravan,    // Trade Caravan: attack it (intercept) or gather at it (escort)
    }

    public sealed class NodeDef
    {
        public string Name = "";
        /// <summary>What gathering yields. Null for Derelict (fixed 50/30/20 salvage) and Camp (hostile).</summary>
        public ResourceId? Resource;
        public int BaseAmount;        // gatherable stock at tier 0 (whole units)
        public int BaseRatePerSec;    // gather rate at tier 0 (whole units/s)
    }

    public static class Nodes
    {
        public const float NodeCellOccupancy = 0.55f;
        /// <summary>Camp loot = garrison build cost × this. 0.5 → 2 in the
        /// 2026-09-28 balance pass: a rim camp paid ~100 resources (half a
        /// minute of gathering) — less than the fighters it cost to take.</summary>
        public const float CampLootFactor = 2f;

        /// <summary>A camp's stockpile on top of its garrison's worth (whole units,
        /// by camp level; balance pass 2026-09-30): raids earned 800-2,400 a day in
        /// the pacing run, not worth the fleet. A Lv 1 camp now holds about two
        /// hours of an early colony's mines, and each level ~3× the last; the
        /// pirates grow fatter as the colonies around them do (× the raider's
        /// CampStockpileGrowth). Split 45% gold, 35% quartz, 20% helium.</summary>
        public static long CampStockpile(int campLevel, int raiderCc) =>
            (long)(CampStockpile(campLevel) * CampStockpileGrowth(raiderCc));

        /// <summary>+40% per Command Center level past the first.</summary>
        public static double CampStockpileGrowth(int raiderCc) => 1.0 + 0.4 * Math.Max(0, raiderCc - 1);

        public static long CampStockpile(int campLevel) => campLevel switch
        {
            <= 1 => 2500,
            2 => 8000,
            3 => 24000,
            4 => 70000,
            _ => 180000,
        };

        public static readonly IReadOnlyList<NodeKind> All = new[]
        {
            NodeKind.Asteroid, NodeKind.Nebula, NodeKind.HeliumCloud,
            NodeKind.Derelict, NodeKind.Camp, NodeKind.DMField,
        };

        public static readonly IReadOnlyDictionary<NodeKind, NodeDef> Defs =
            new Dictionary<NodeKind, NodeDef>
        {
            // DMField spawns via a dedicated empty-cell roll in MapGenerator
            // (NOT TierKindWeights — adding it there would reshuffle the
            // existing shared galaxy). Amount/rate are set there too.
            [NodeKind.DMField] = new NodeDef
            {
                Name = "Dark Matter Field",
                Resource = null,
                BaseAmount = 250,
                BaseRatePerSec = 0,
            },
            [NodeKind.Asteroid] = new NodeDef
            {
                Name = "Asteroid Field",
                Resource = ResourceId.Gold,
                BaseAmount = 2000,
                BaseRatePerSec = 3,
            },
            [NodeKind.Nebula] = new NodeDef
            {
                Name = "Quartz Nebula",
                Resource = ResourceId.Quartz,
                BaseAmount = 1500,
                BaseRatePerSec = 2,
            },
            [NodeKind.HeliumCloud] = new NodeDef
            {
                Name = "Helium Cloud",
                Resource = ResourceId.Helium,
                BaseAmount = 1000,
                BaseRatePerSec = 1,
            },
            [NodeKind.Derelict] = new NodeDef
            {
                Name = "Derelict Hulk",
                Resource = null,
                BaseAmount = 1200,
                BaseRatePerSec = 0, // salvaged all at once, not gathered over time
            },
            [NodeKind.Camp] = new NodeDef
            {
                Name = "Pirate Camp",
                Resource = null,
                BaseAmount = 0,
                BaseRatePerSec = 0,
            },
            // Event sites: amounts and rates are set by EventSites.
            [NodeKind.Comet] = new NodeDef { Name = "Passing Comet", Resource = null },
            [NodeKind.Caravan] = new NodeDef { Name = "Trade Caravan", Resource = null },
        };

        /// <summary>Per-tier probability weights. Indexed by tier (0..4).</summary>
        public static readonly IReadOnlyList<IReadOnlyDictionary<NodeKind, float>> TierKindWeights = new[]
        {
            // Tier 0 (safe rim): mostly asteroids + a few helium clouds
            new Dictionary<NodeKind, float>
            {
                [NodeKind.Asteroid] = 6, [NodeKind.Nebula] = 1, [NodeKind.HeliumCloud] = 2,
                [NodeKind.Derelict] = 1, [NodeKind.Camp] = 1,
            },
            // Tier 1
            new Dictionary<NodeKind, float>
            {
                [NodeKind.Asteroid] = 4, [NodeKind.Nebula] = 2, [NodeKind.HeliumCloud] = 2,
                [NodeKind.Derelict] = 1, [NodeKind.Camp] = 2,
            },
            // Tier 2
            new Dictionary<NodeKind, float>
            {
                [NodeKind.Asteroid] = 3, [NodeKind.Nebula] = 3, [NodeKind.HeliumCloud] = 2,
                [NodeKind.Derelict] = 1, [NodeKind.Camp] = 3,
            },
            // Tier 3
            new Dictionary<NodeKind, float>
            {
                [NodeKind.Asteroid] = 2, [NodeKind.Nebula] = 3, [NodeKind.HeliumCloud] = 3,
                [NodeKind.Derelict] = 1, [NodeKind.Camp] = 4,
            },
            // Tier 4 (core): dense, deadly. Derelict Hulks stay a RARE salvage
            // find (≤ any single resource weight), not more common than gold.
            new Dictionary<NodeKind, float>
            {
                [NodeKind.Asteroid] = 2, [NodeKind.Nebula] = 3, [NodeKind.HeliumCloud] = 3,
                [NodeKind.Derelict] = 1, [NodeKind.Camp] = 6,
            },
        };

        /// <summary>Pirate garrison composition per camp level (1..5).</summary>
        public static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<HullId, int>> CampTemplates =
            new Dictionary<int, IReadOnlyDictionary<HullId, int>>
        {
            [1] = new Dictionary<HullId, int> { [HullId.Fighter] = 5 },
            [2] = new Dictionary<HullId, int> { [HullId.Fighter] = 10, [HullId.Bomber] = 3 },
            [3] = new Dictionary<HullId, int> { [HullId.Fighter] = 20, [HullId.Bomber] = 8, [HullId.Cruiser] = 2 },
            [4] = new Dictionary<HullId, int> { [HullId.Fighter] = 40, [HullId.Bomber] = 20, [HullId.Cruiser] = 8 },
            [5] = new Dictionary<HullId, int> { [HullId.Fighter] = 80, [HullId.Bomber] = 40, [HullId.Cruiser] = 20 },
        };
    }
}
