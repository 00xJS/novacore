// Research tech tree. Ported from `src/data/research.ts`. Techs live in 5
// categories; each level has a per-level effect that folds into the sim via
// ResearchSystem accessors. Every tech is capped by the Research Lab level.
// Military research counts in every battle you fight (attack or defense); the
// Defense branch only counts when your home colony is the one being hit.
//
// Per-tech CostGrowth + TimeGrowth (v1 varies them; e.g. 1.5 vs 1.6) and the
// Requires prerequisite carries a required LEVEL, not just a tech id.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum TechId
    {
        // Economy
        YieldOptimization,
        DeepCoreDrilling,
        // Logistics
        IonThrusters,
        CargoHolds,
        FuelInjection,
        // Military
        WeaponsCalibration,
        ArmorPlating,
        // Industry
        PrefabAssembly,
        // Economy (content expansion, 2026-07-05)
        ExtractionAlgorithms,
        DeepVaultProtocols,
        // Military per-hull lines (content expansion)
        FighterDoctrine,
        BomberPayloads,
        CruiserBroadsides,
        FighterPlating,
        BomberHulls,
        CruiserBulkheads,
        RapidFabrication,
        // Tier-2 "doubled tree" lines (user request 2026-07-05: two full research
        // skills per stat — attack/defense/health/gathering/research/building/ship).
        AntimatterWarheads,
        DeflectorArray,
        ReinforcedHulls,
        NaniteRepairSwarms,
        SwarmFabricators,
        QuantumExtractors,
        QuantumComputing,
        SingularityCores,
        OrbitalAssembly,
        // Defense branch (user request 2026-09-27: "build out the defense research")
        BastionHangars,
        PointDefenseGrid,
        OrbitalBatteries,
        PlanetaryDeflectors,
    }

    public enum TechCategory { Economy, Logistics, Military, Industry, Defense }

    /// <summary>Kinds of effects a tech level can contribute.</summary>
    public enum TechEffectKind
    {
        ProdMultiplier,    // +% to resource production
        MarchSpeedMult,    // +% march travel speed
        CargoMult,         // +% cargo capacity
        HeliumReduce,         // % helium cost reduced
        AtkMult,           // +% attacker damage dealt
        HpMult,            // +% durability
        BuildTimeReduce,   // −% building/research/ship time (floor ResearchReduceFloor)
        GatherRateMult,    // +% gather speed at map nodes
        ShieldCapMult,     // +% Warehouse raid-shielded capacity
        ShipTimeReduce,    // −% ship build time only (stacks with BuildTimeReduce, same floor)
        ResearchTimeReduce,// −% research time (its own line, split from BuildTimeReduce)
        ShieldMult,        // +% ship shields (the regenerating pre-HP absorb layer)
        DefAtkMult,        // +% damage dealt by ships defending the home colony
        DefHpMult,         // +% durability of ships defending the home colony
        DefShieldMult,     // +% shields of ships defending the home colony
        OrbitalBattery,    // planetary guns: Balance.BatteryDamagePerLevel per level, every round
    }

    public readonly struct TechRequirement
    {
        public readonly TechId Tech;
        public readonly int Level;
        public TechRequirement(TechId tech, int level) { Tech = tech; Level = level; }
    }

    public sealed class TechDef
    {
        public string Name = "";
        public TechCategory Category;
        public TechEffectKind Effect;
        /// <summary>Fractional per-level contribution (e.g. 0.04 = +4% per level).</summary>
        public float PerLevel;
        public int MaxLevel;
        public ResourceBag BaseCost = new();
        public float CostGrowth;
        public int BaseTimeSec;
        public float TimeGrowth;
        /// <summary>Research Lab level required to start (and to keep levelling) this tech.</summary>
        public int LabLevelReq;
        /// <summary>Optional prerequisite tech + level.</summary>
        public TechRequirement? Requires;
        /// <summary>When set, an AtkMult/HpMult effect applies only to this hull.</summary>
        public HullId? HullScope;
    }

    public static class Techs
    {
        /// <summary>Lowest build-time / helium cost multiplier research can ever reach.</summary>
        public const float ResearchReduceFloor = 0.5f;

        public static readonly IReadOnlyList<TechId> All = new[]
        {
            TechId.YieldOptimization, TechId.DeepCoreDrilling,
            TechId.ExtractionAlgorithms, TechId.DeepVaultProtocols,
            TechId.IonThrusters, TechId.CargoHolds, TechId.FuelInjection,
            TechId.WeaponsCalibration, TechId.ArmorPlating,
            TechId.FighterDoctrine, TechId.BomberPayloads, TechId.CruiserBroadsides,
            TechId.FighterPlating, TechId.BomberHulls, TechId.CruiserBulkheads,
            TechId.RapidFabrication,
            TechId.PrefabAssembly,
            TechId.AntimatterWarheads, TechId.DeflectorArray,
            TechId.ReinforcedHulls, TechId.NaniteRepairSwarms,
            TechId.SwarmFabricators, TechId.QuantumExtractors,
            TechId.QuantumComputing, TechId.SingularityCores,
            TechId.OrbitalAssembly,
            TechId.BastionHangars, TechId.PointDefenseGrid,
            TechId.OrbitalBatteries, TechId.PlanetaryDeflectors,
        };

        public static readonly IReadOnlyList<TechCategory> Categories = new[]
        {
            TechCategory.Economy, TechCategory.Logistics, TechCategory.Military, TechCategory.Industry,
            TechCategory.Defense,
        };

        public static readonly IReadOnlyDictionary<TechId, TechDef> Defs = new Dictionary<TechId, TechDef>
        {
            [TechId.YieldOptimization] = new TechDef
            {
                Name = "Yield Optimization",
                Category = TechCategory.Economy,
                Effect = TechEffectKind.ProdMultiplier,
                PerLevel = 0.04f,
                MaxLevel = 25,
                BaseCost = new ResourceBag(300, 200, 40),
                CostGrowth = 1.5f,
                BaseTimeSec = 120,
                TimeGrowth = 1.45f,
                LabLevelReq = 1,
            },
            [TechId.DeepCoreDrilling] = new TechDef
            {
                Name = "Deep-Core Drilling",
                Category = TechCategory.Economy,
                Effect = TechEffectKind.ProdMultiplier,
                PerLevel = 0.06f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(1200, 900, 300),
                CostGrowth = 1.6f,
                BaseTimeSec = 900,
                TimeGrowth = 1.5f,
                LabLevelReq = 8,
                Requires = new TechRequirement(TechId.YieldOptimization, 8),
            },
            [TechId.IonThrusters] = new TechDef
            {
                Name = "Ion Thrusters",
                Category = TechCategory.Logistics,
                Effect = TechEffectKind.MarchSpeedMult,
                PerLevel = 0.05f,
                MaxLevel = 20,
                BaseCost = new ResourceBag(250, 250, 80),
                CostGrowth = 1.5f,
                BaseTimeSec = 150,
                TimeGrowth = 1.45f,
                LabLevelReq = 1,
            },
            [TechId.CargoHolds] = new TechDef
            {
                Name = "Expanded Cargo Holds",
                Category = TechCategory.Logistics,
                Effect = TechEffectKind.CargoMult,
                PerLevel = 0.06f,
                MaxLevel = 20,
                BaseCost = new ResourceBag(300, 150, 60),
                CostGrowth = 1.5f,
                BaseTimeSec = 150,
                TimeGrowth = 1.45f,
                LabLevelReq = 3,
            },
            [TechId.FuelInjection] = new TechDef
            {
                Name = "Fuel Injection",
                Category = TechCategory.Logistics,
                Effect = TechEffectKind.HeliumReduce,
                PerLevel = 0.03f,
                MaxLevel = 15,
                BaseCost = new ResourceBag(200, 200, 150),
                CostGrowth = 1.55f,
                BaseTimeSec = 240,
                TimeGrowth = 1.45f,
                LabLevelReq = 5,
                Requires = new TechRequirement(TechId.IonThrusters, 5),
            },
            [TechId.WeaponsCalibration] = new TechDef
            {
                Name = "Weapons Calibration",
                Category = TechCategory.Military,
                Effect = TechEffectKind.AtkMult,
                PerLevel = 0.05f,
                MaxLevel = 20,
                BaseCost = new ResourceBag(350, 250, 100),
                CostGrowth = 1.55f,
                BaseTimeSec = 180,
                TimeGrowth = 1.45f,
                LabLevelReq = 2,
            },
            [TechId.ArmorPlating] = new TechDef
            {
                Name = "Armor Plating",
                Category = TechCategory.Military,
                Effect = TechEffectKind.HpMult,
                PerLevel = 0.05f,
                MaxLevel = 20,
                BaseCost = new ResourceBag(400, 200, 80),
                CostGrowth = 1.55f,
                BaseTimeSec = 180,
                TimeGrowth = 1.45f,
                LabLevelReq = 2,
            },
            [TechId.ExtractionAlgorithms] = new TechDef
            {
                Name = "Extraction Algorithms",
                Category = TechCategory.Economy,
                Effect = TechEffectKind.GatherRateMult,
                PerLevel = 0.04f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(400, 300, 100),
                CostGrowth = 1.5f,
                BaseTimeSec = 240,
                TimeGrowth = 1.45f,
                LabLevelReq = 3,
            },
            [TechId.DeepVaultProtocols] = new TechDef
            {
                Name = "Deep Vault Protocols",
                Category = TechCategory.Economy,
                Effect = TechEffectKind.ShieldCapMult,
                PerLevel = 0.08f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(800, 400, 100),
                CostGrowth = 1.55f,
                BaseTimeSec = 300,
                TimeGrowth = 1.45f,
                LabLevelReq = 4,
            },
            [TechId.FighterDoctrine] = new TechDef
            {
                Name = "Fighter Doctrine",
                Category = TechCategory.Military,
                Effect = TechEffectKind.AtkMult,
                HullScope = HullId.Fighter,
                PerLevel = 0.02f,
                MaxLevel = 5,
                BaseCost = new ResourceBag(500, 300, 120),
                CostGrowth = 1.6f,
                BaseTimeSec = 300,
                TimeGrowth = 1.5f,
                LabLevelReq = 3,
                Requires = new TechRequirement(TechId.WeaponsCalibration, 3),
            },
            [TechId.BomberPayloads] = new TechDef
            {
                Name = "Bomber Payloads",
                Category = TechCategory.Military,
                Effect = TechEffectKind.AtkMult,
                HullScope = HullId.Bomber,
                PerLevel = 0.02f,
                MaxLevel = 5,
                BaseCost = new ResourceBag(650, 400, 180),
                CostGrowth = 1.6f,
                BaseTimeSec = 420,
                TimeGrowth = 1.5f,
                LabLevelReq = 5,
                Requires = new TechRequirement(TechId.WeaponsCalibration, 5),
            },
            [TechId.CruiserBroadsides] = new TechDef
            {
                Name = "Cruiser Broadsides",
                Category = TechCategory.Military,
                Effect = TechEffectKind.AtkMult,
                HullScope = HullId.Cruiser,
                PerLevel = 0.02f,
                MaxLevel = 5,
                BaseCost = new ResourceBag(900, 550, 250),
                CostGrowth = 1.6f,
                BaseTimeSec = 600,
                TimeGrowth = 1.5f,
                LabLevelReq = 7,
                Requires = new TechRequirement(TechId.WeaponsCalibration, 7),
            },
            [TechId.FighterPlating] = new TechDef
            {
                Name = "Fighter Plating",
                Category = TechCategory.Military,
                Effect = TechEffectKind.HpMult,
                HullScope = HullId.Fighter,
                PerLevel = 0.02f,
                MaxLevel = 5,
                BaseCost = new ResourceBag(550, 250, 120),
                CostGrowth = 1.6f,
                BaseTimeSec = 300,
                TimeGrowth = 1.5f,
                LabLevelReq = 3,
                Requires = new TechRequirement(TechId.ArmorPlating, 3),
            },
            [TechId.BomberHulls] = new TechDef
            {
                Name = "Reinforced Bomber Hulls",
                Category = TechCategory.Military,
                Effect = TechEffectKind.HpMult,
                HullScope = HullId.Bomber,
                PerLevel = 0.02f,
                MaxLevel = 5,
                BaseCost = new ResourceBag(700, 350, 180),
                CostGrowth = 1.6f,
                BaseTimeSec = 420,
                TimeGrowth = 1.5f,
                LabLevelReq = 5,
                Requires = new TechRequirement(TechId.ArmorPlating, 5),
            },
            [TechId.CruiserBulkheads] = new TechDef
            {
                Name = "Cruiser Bulkheads",
                Category = TechCategory.Military,
                Effect = TechEffectKind.HpMult,
                HullScope = HullId.Cruiser,
                PerLevel = 0.02f,
                MaxLevel = 5,
                BaseCost = new ResourceBag(950, 500, 250),
                CostGrowth = 1.6f,
                BaseTimeSec = 600,
                TimeGrowth = 1.5f,
                LabLevelReq = 7,
                Requires = new TechRequirement(TechId.ArmorPlating, 7),
            },
            [TechId.RapidFabrication] = new TechDef
            {
                Name = "Rapid Fabrication",
                Category = TechCategory.Military,
                Effect = TechEffectKind.ShipTimeReduce,
                PerLevel = 0.02f,
                MaxLevel = 5,
                BaseCost = new ResourceBag(700, 400, 200),
                CostGrowth = 1.6f,
                BaseTimeSec = 360,
                TimeGrowth = 1.5f,
                LabLevelReq = 6,
            },
            [TechId.PrefabAssembly] = new TechDef
            {
                Name = "Prefab Assembly",
                Category = TechCategory.Industry,
                Effect = TechEffectKind.BuildTimeReduce,
                PerLevel = 0.03f,
                MaxLevel = 15,
                BaseCost = new ResourceBag(500, 350, 120),
                CostGrowth = 1.6f,
                BaseTimeSec = 300,
                TimeGrowth = 1.5f,
                LabLevelReq = 4,
            },
            // ---- tier-2 "doubled tree" lines ----
            [TechId.AntimatterWarheads] = new TechDef
            {
                Name = "Antimatter Warheads",
                Category = TechCategory.Military,
                Effect = TechEffectKind.AtkMult,
                PerLevel = 0.04f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(1500, 900, 400),
                CostGrowth = 1.6f,
                BaseTimeSec = 900,
                TimeGrowth = 1.5f,
                LabLevelReq = 8,
                Requires = new TechRequirement(TechId.WeaponsCalibration, 10),
            },
            // Repurposed to the shield line when the Shield stat landed (2026-07-05,
            // user-approved): armor stays with Armor Plating / Reinforced Hulls.
            [TechId.DeflectorArray] = new TechDef
            {
                Name = "Deflector Array",
                Category = TechCategory.Military,
                Effect = TechEffectKind.ShieldMult,
                PerLevel = 0.04f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(1400, 800, 400),
                CostGrowth = 1.6f,
                BaseTimeSec = 900,
                TimeGrowth = 1.5f,
                LabLevelReq = 8,
                Requires = new TechRequirement(TechId.ArmorPlating, 10),
            },
            [TechId.ReinforcedHulls] = new TechDef
            {
                Name = "Reinforced Hulls",
                Category = TechCategory.Military,
                Effect = TechEffectKind.HpMult,
                PerLevel = 0.03f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(900, 400, 150),
                CostGrowth = 1.55f,
                BaseTimeSec = 420,
                TimeGrowth = 1.5f,
                LabLevelReq = 5,
            },
            [TechId.NaniteRepairSwarms] = new TechDef
            {
                Name = "Nanite Repair Swarms",
                Category = TechCategory.Military,
                Effect = TechEffectKind.HpMult,
                PerLevel = 0.03f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(1800, 1000, 500),
                CostGrowth = 1.6f,
                BaseTimeSec = 1200,
                TimeGrowth = 1.5f,
                LabLevelReq = 9,
                Requires = new TechRequirement(TechId.ReinforcedHulls, 5),
            },
            [TechId.SwarmFabricators] = new TechDef
            {
                Name = "Swarm Fabricators",
                Category = TechCategory.Military,
                Effect = TechEffectKind.ShipTimeReduce,
                PerLevel = 0.02f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(1600, 900, 450),
                CostGrowth = 1.6f,
                BaseTimeSec = 1000,
                TimeGrowth = 1.5f,
                LabLevelReq = 9,
                Requires = new TechRequirement(TechId.RapidFabrication, 5),
            },
            [TechId.QuantumExtractors] = new TechDef
            {
                Name = "Quantum Extractors",
                Category = TechCategory.Economy,
                Effect = TechEffectKind.GatherRateMult,
                PerLevel = 0.05f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(1600, 1200, 400),
                CostGrowth = 1.6f,
                BaseTimeSec = 900,
                TimeGrowth = 1.5f,
                LabLevelReq = 8,
                Requires = new TechRequirement(TechId.ExtractionAlgorithms, 5),
            },
            [TechId.QuantumComputing] = new TechDef
            {
                Name = "Quantum Computing",
                Category = TechCategory.Industry,
                Effect = TechEffectKind.ResearchTimeReduce,
                PerLevel = 0.03f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(600, 800, 200),
                CostGrowth = 1.55f,
                BaseTimeSec = 360,
                TimeGrowth = 1.5f,
                LabLevelReq = 5,
            },
            [TechId.SingularityCores] = new TechDef
            {
                Name = "Singularity Cores",
                Category = TechCategory.Industry,
                Effect = TechEffectKind.ResearchTimeReduce,
                PerLevel = 0.03f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(2000, 2500, 800),
                CostGrowth = 1.6f,
                BaseTimeSec = 1500,
                TimeGrowth = 1.5f,
                LabLevelReq = 10,
                Requires = new TechRequirement(TechId.QuantumComputing, 5),
            },
            [TechId.OrbitalAssembly] = new TechDef
            {
                Name = "Orbital Assembly Cranes",
                Category = TechCategory.Industry,
                Effect = TechEffectKind.BuildTimeReduce,
                PerLevel = 0.03f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(1800, 1200, 500),
                CostGrowth = 1.6f,
                BaseTimeSec = 1200,
                TimeGrowth = 1.5f,
                LabLevelReq = 9,
                Requires = new TechRequirement(TechId.PrefabAssembly, 8),
            },
            // ---- defense branch: home-colony battles only ----
            [TechId.BastionHangars] = new TechDef
            {
                Name = "Bastion Hangars",
                Category = TechCategory.Defense,
                Effect = TechEffectKind.DefHpMult,
                PerLevel = 0.06f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(350, 200, 60),
                CostGrowth = 1.5f,
                BaseTimeSec = 150,
                TimeGrowth = 1.45f,
                LabLevelReq = 1,
            },
            [TechId.PointDefenseGrid] = new TechDef
            {
                Name = "Point-Defense Grid",
                Category = TechCategory.Defense,
                Effect = TechEffectKind.DefAtkMult,
                PerLevel = 0.06f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(350, 250, 100),
                CostGrowth = 1.55f,
                BaseTimeSec = 180,
                TimeGrowth = 1.45f,
                LabLevelReq = 2,
            },
            [TechId.OrbitalBatteries] = new TechDef
            {
                Name = "Orbital Batteries",
                Category = TechCategory.Defense,
                Effect = TechEffectKind.OrbitalBattery,
                PerLevel = 1f, // battery level; damage = level × Balance.BatteryDamagePerLevel
                MaxLevel = 10,
                BaseCost = new ResourceBag(600, 400, 150),
                CostGrowth = 1.6f,
                BaseTimeSec = 300,
                TimeGrowth = 1.5f,
                LabLevelReq = 3,
            },
            [TechId.PlanetaryDeflectors] = new TechDef
            {
                Name = "Planetary Deflectors",
                Category = TechCategory.Defense,
                Effect = TechEffectKind.DefShieldMult,
                PerLevel = 0.08f,
                MaxLevel = 10,
                BaseCost = new ResourceBag(900, 500, 200),
                CostGrowth = 1.6f,
                BaseTimeSec = 600,
                TimeGrowth = 1.5f,
                LabLevelReq = 5,
                Requires = new TechRequirement(TechId.BastionHangars, 5),
            },
        };
    }
}
