// Map node definitions — resource tiles, helium clouds, derelict hulks, pirate camps.
// Ported from `src/data/nodes.ts`. Tier weights control which node kinds appear
// at each risk tier (0 outer rim → 4 core).
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
        public const float CampLootFactor = 0.5f;

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
