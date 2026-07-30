// Ship (hull) definitions and IDs. Original five ported from `src/data/ships.ts`;
// the fleet expansion (2026-07-05) added the combat ladder + support line + the
// Shield stat; the triangle expansion (2026-07-05, user-approved) turns each
// combat tier into a rock-paper-scissors triangle of three variants.
//
// Every combat TIER (Strike Craft, Interceptors, Battleships, Corvettes,
// Dreadnoughts, Destroyers) is an internal RPS ring of three roles:
//   Striker (glass cannon) > Guardian (tank) > Skirmisher (fast) > Striker.
// `Counters` points each hull at the sibling it deals 2× to. Counters are
// WITHIN a tier only — vertical progression is stat scale + tech gates, so a
// higher tier always out-muscles a lower one; the triangle is the horizontal
// choice inside a tier.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum HullId
    {
        Fighter,
        Bomber,
        Cruiser,
        Hauler,
        Probe,
        // Combat ladder anchors (each is one corner of its tier's triangle)
        Talon,      // Interceptor / Striker
        Vanguard,   // Battleship / Striker
        Lancer,     // Corvette / Striker
        Leviathan,  // Dreadnought / Guardian
        Reaper,     // Destroyer / Striker
        // Support line
        Atlas,      // star freighter
        Aegis,      // shield frigate
        Scavenger,  // salvage corvette
        // Triangle siblings (2 per combat tier)
        Sentinel,   // Interceptor / Guardian
        Harrier,    // Interceptor / Skirmisher
        Rampart,    // Battleship / Guardian
        Corsair,    // Battleship / Skirmisher
        Bulwark,    // Corvette / Guardian
        Javelin,    // Corvette / Skirmisher
        Behemoth,   // Dreadnought / Striker
        Nomad,      // Dreadnought / Skirmisher
        Warden,     // Destroyer / Guardian
        Wraith,     // Destroyer / Skirmisher
    }

    public sealed class ShipDef
    {
        public string Name = "";
        /// <summary>Tier/role group for UI headers (e.g. "Interceptors", "Support").</summary>
        public string Class = "";
        public int Atk;
        /// <summary>Regenerating shield points per ship — absorbed FIRST each combat
        /// round, restored every round. Hull damage (Hp) is permanent.</summary>
        public int Shield;
        public int Hp;
        /// <summary>Tiles per minute at 100% efficiency. The whole table was
        /// doubled 2026-07-07 (user spec: all fleets 2× faster).</summary>
        public int Speed;
        /// <summary>Cargo capacity in milli-units.</summary>
        public int Cargo;
        /// <summary>Helium consumed per tile (milli).</summary>
        public int FuelPerTile;
        public ResourceBag Cost = new();
        public int BuildTimeSec;
        /// <summary>Hull this ship gets a 2× damage bonus against (its tier sibling).</summary>
        public HullId? Counters;
        /// <summary>Shipyard level required to unlock this hull.</summary>
        public int ShipyardLevelReq;
        /// <summary>Research required to unlock this hull (all must be met).</summary>
        public TechRequirement[] TechReqs = System.Array.Empty<TechRequirement>();
    }

    public static class Ships
    {
        // Ordered by tier so UI group headers (ShipDef.Class) stay contiguous.
        public static readonly IReadOnlyList<HullId> All = new[]
        {
            // Strike Craft (light triangle)
            HullId.Fighter, HullId.Bomber, HullId.Cruiser,
            // Interceptors
            HullId.Talon, HullId.Sentinel, HullId.Harrier,
            // Battleships
            HullId.Vanguard, HullId.Rampart, HullId.Corsair,
            // Corvettes
            HullId.Lancer, HullId.Bulwark, HullId.Javelin,
            // Dreadnoughts
            HullId.Behemoth, HullId.Leviathan, HullId.Nomad,
            // Destroyers
            HullId.Reaper, HullId.Warden, HullId.Wraith,
            // Support
            HullId.Hauler, HullId.Atlas, HullId.Aegis, HullId.Scavenger,
            // Recon
            HullId.Probe,
        };

        public static readonly IReadOnlyDictionary<HullId, ShipDef> Defs =
            new Dictionary<HullId, ShipDef>
        {
            // ---- Strike Craft (light triangle: Fighter > Bomber > Cruiser > Fighter) ----
            [HullId.Fighter] = new ShipDef
            {
                Name = "Fighter", Class = "Strike Craft",
                Atk = 30, Shield = 10, Hp = 60, Speed = 60, Cargo = 40 * 1000, FuelPerTile = 3,
                Cost = new ResourceBag(30, 10, 0), BuildTimeSec = 5,
                Counters = HullId.Bomber, ShipyardLevelReq = 1,
            },
            [HullId.Bomber] = new ShipDef
            {
                Name = "Bomber", Class = "Strike Craft",
                Atk = 50, Shield = 20, Hp = 90, Speed = 40, Cargo = 30 * 1000, FuelPerTile = 6,
                Cost = new ResourceBag(70, 30, 10), BuildTimeSec = 15,
                Counters = HullId.Cruiser, ShipyardLevelReq = 2,
            },
            [HullId.Cruiser] = new ShipDef
            {
                Name = "Cruiser", Class = "Strike Craft",
                Atk = 70, Shield = 40, Hp = 300, Speed = 30, Cargo = 100 * 1000, FuelPerTile = 12,
                Cost = new ResourceBag(200, 80, 40), BuildTimeSec = 45,
                Counters = HullId.Fighter, ShipyardLevelReq = 3,
            },

            // ---- Interceptors (Talon > Sentinel > Harrier > Talon) ----
            [HullId.Talon] = new ShipDef
            {
                Name = "Talon", Class = "Interceptors",
                Atk = 45, Shield = 15, Hp = 80, Speed = 68, Cargo = 20 * 1000, FuelPerTile = 4,
                Cost = new ResourceBag(60, 25, 5), BuildTimeSec = 8,
                Counters = HullId.Sentinel, ShipyardLevelReq = 4,
                TechReqs = new[] { new TechRequirement(TechId.ArmorPlating, 2) },
            },
            [HullId.Sentinel] = new ShipDef
            {
                Name = "Sentinel", Class = "Interceptors",
                Atk = 30, Shield = 40, Hp = 110, Speed = 56, Cargo = 20 * 1000, FuelPerTile = 4,
                Cost = new ResourceBag(60, 30, 5), BuildTimeSec = 8,
                Counters = HullId.Harrier, ShipyardLevelReq = 4,
                TechReqs = new[] { new TechRequirement(TechId.ArmorPlating, 2) },
            },
            [HullId.Harrier] = new ShipDef
            {
                Name = "Harrier", Class = "Interceptors",
                Atk = 40, Shield = 12, Hp = 70, Speed = 80, Cargo = 20 * 1000, FuelPerTile = 3,
                Cost = new ResourceBag(55, 22, 5), BuildTimeSec = 7,
                Counters = HullId.Talon, ShipyardLevelReq = 4,
                TechReqs = new[] { new TechRequirement(TechId.ArmorPlating, 2) },
            },

            // ---- Battleships (Vanguard > Rampart > Corsair > Vanguard) ----
            [HullId.Vanguard] = new ShipDef
            {
                Name = "Vanguard", Class = "Battleships",
                Atk = 120, Shield = 60, Hp = 520, Speed = 24, Cargo = 150 * 1000, FuelPerTile = 18,
                Cost = new ResourceBag(450, 180, 80), BuildTimeSec = 90,
                Counters = HullId.Rampart, ShipyardLevelReq = 7,
                TechReqs = new[] { new TechRequirement(TechId.WeaponsCalibration, 5) },
            },
            [HullId.Rampart] = new ShipDef
            {
                Name = "Rampart", Class = "Battleships",
                Atk = 90, Shield = 100, Hp = 680, Speed = 20, Cargo = 150 * 1000, FuelPerTile = 18,
                Cost = new ResourceBag(480, 210, 90), BuildTimeSec = 96,
                Counters = HullId.Corsair, ShipyardLevelReq = 7,
                TechReqs = new[] { new TechRequirement(TechId.WeaponsCalibration, 5) },
            },
            [HullId.Corsair] = new ShipDef
            {
                Name = "Corsair", Class = "Battleships",
                Atk = 115, Shield = 45, Hp = 440, Speed = 32, Cargo = 150 * 1000, FuelPerTile = 16,
                Cost = new ResourceBag(430, 170, 75), BuildTimeSec = 84,
                Counters = HullId.Vanguard, ShipyardLevelReq = 7,
                TechReqs = new[] { new TechRequirement(TechId.WeaponsCalibration, 5) },
            },

            // ---- Corvettes (Lancer > Bulwark > Javelin > Lancer) ----
            [HullId.Lancer] = new ShipDef
            {
                Name = "Lancer", Class = "Corvettes",
                Atk = 160, Shield = 10, Hp = 120, Speed = 48, Cargo = 30 * 1000, FuelPerTile = 10,
                Cost = new ResourceBag(320, 220, 120), BuildTimeSec = 60,
                Counters = HullId.Bulwark, ShipyardLevelReq = 8,
                TechReqs = new[] { new TechRequirement(TechId.WeaponsCalibration, 8) },
            },
            [HullId.Bulwark] = new ShipDef
            {
                Name = "Bulwark", Class = "Corvettes",
                Atk = 110, Shield = 70, Hp = 260, Speed = 40, Cargo = 30 * 1000, FuelPerTile = 10,
                Cost = new ResourceBag(340, 240, 130), BuildTimeSec = 64,
                Counters = HullId.Javelin, ShipyardLevelReq = 8,
                TechReqs = new[] { new TechRequirement(TechId.WeaponsCalibration, 8) },
            },
            [HullId.Javelin] = new ShipDef
            {
                Name = "Javelin", Class = "Corvettes",
                Atk = 150, Shield = 12, Hp = 110, Speed = 60, Cargo = 30 * 1000, FuelPerTile = 9,
                Cost = new ResourceBag(300, 200, 110), BuildTimeSec = 56,
                Counters = HullId.Lancer, ShipyardLevelReq = 8,
                TechReqs = new[] { new TechRequirement(TechId.WeaponsCalibration, 8) },
            },

            // ---- Dreadnoughts (Behemoth > Leviathan > Nomad > Behemoth) ----
            [HullId.Behemoth] = new ShipDef
            {
                Name = "Behemoth", Class = "Dreadnoughts",
                Atk = 300, Shield = 90, Hp = 720, Speed = 16, Cargo = 250 * 1000, FuelPerTile = 32,
                Cost = new ResourceBag(940, 470, 230), BuildTimeSec = 190,
                Counters = HullId.Leviathan, ShipyardLevelReq = 9,
                TechReqs = new[]
                {
                    new TechRequirement(TechId.AntimatterWarheads, 3),
                    new TechRequirement(TechId.DeflectorArray, 3),
                },
            },
            [HullId.Leviathan] = new ShipDef
            {
                Name = "Leviathan", Class = "Dreadnoughts",
                Atk = 220, Shield = 150, Hp = 900, Speed = 16, Cargo = 250 * 1000, FuelPerTile = 30,
                Cost = new ResourceBag(900, 450, 220), BuildTimeSec = 180,
                Counters = HullId.Nomad, ShipyardLevelReq = 9,
                TechReqs = new[]
                {
                    new TechRequirement(TechId.AntimatterWarheads, 3),
                    new TechRequirement(TechId.DeflectorArray, 3),
                },
            },
            [HullId.Nomad] = new ShipDef
            {
                Name = "Nomad", Class = "Dreadnoughts",
                Atk = 240, Shield = 100, Hp = 760, Speed = 24, Cargo = 250 * 1000, FuelPerTile = 28,
                Cost = new ResourceBag(880, 440, 210), BuildTimeSec = 174,
                Counters = HullId.Behemoth, ShipyardLevelReq = 9,
                TechReqs = new[]
                {
                    new TechRequirement(TechId.AntimatterWarheads, 3),
                    new TechRequirement(TechId.DeflectorArray, 3),
                },
            },

            // ---- Destroyers (Reaper > Warden > Wraith > Reaper) ----
            [HullId.Reaper] = new ShipDef
            {
                Name = "Reaper", Class = "Destroyers",
                Atk = 350, Shield = 200, Hp = 1400, Speed = 20, Cargo = 300 * 1000, FuelPerTile = 40,
                Cost = new ResourceBag(1500, 900, 400), BuildTimeSec = 300,
                Counters = HullId.Warden, ShipyardLevelReq = 10,
                TechReqs = new[]
                {
                    new TechRequirement(TechId.AntimatterWarheads, 6),
                    new TechRequirement(TechId.NaniteRepairSwarms, 5),
                },
            },
            [HullId.Warden] = new ShipDef
            {
                Name = "Warden", Class = "Destroyers",
                Atk = 260, Shield = 320, Hp = 1800, Speed = 16, Cargo = 300 * 1000, FuelPerTile = 40,
                Cost = new ResourceBag(1550, 950, 420), BuildTimeSec = 315,
                Counters = HullId.Wraith, ShipyardLevelReq = 10,
                TechReqs = new[]
                {
                    new TechRequirement(TechId.AntimatterWarheads, 6),
                    new TechRequirement(TechId.NaniteRepairSwarms, 5),
                },
            },
            [HullId.Wraith] = new ShipDef
            {
                Name = "Wraith", Class = "Destroyers",
                Atk = 330, Shield = 180, Hp = 1200, Speed = 26, Cargo = 300 * 1000, FuelPerTile = 36,
                Cost = new ResourceBag(1450, 870, 380), BuildTimeSec = 288,
                Counters = HullId.Reaper, ShipyardLevelReq = 10,
                TechReqs = new[]
                {
                    new TechRequirement(TechId.AntimatterWarheads, 6),
                    new TechRequirement(TechId.NaniteRepairSwarms, 5),
                },
            },

            // ---- Support line ----
            [HullId.Hauler] = new ShipDef
            {
                Name = "Hauler", Class = "Support",
                Atk = 5, Shield = 5, Hp = 60, Speed = 24, Cargo = 800 * 1000, FuelPerTile = 3,
                Cost = new ResourceBag(80, 40, 20), BuildTimeSec = 12,
                Counters = null, ShipyardLevelReq = 1,
            },
            [HullId.Atlas] = new ShipDef
            {
                Name = "Atlas", Class = "Support",
                Atk = 8, Shield = 30, Hp = 180, Speed = 20, Cargo = 2400 * 1000, FuelPerTile = 6,
                Cost = new ResourceBag(240, 120, 60), BuildTimeSec = 36,
                Counters = null, ShipyardLevelReq = 5,
                TechReqs = new[] { new TechRequirement(TechId.CargoHolds, 4) },
            },
            [HullId.Aegis] = new ShipDef
            {
                Name = "Aegis", Class = "Support",
                Atk = 25, Shield = 120, Hp = 250, Speed = 36, Cargo = 60 * 1000, FuelPerTile = 8,
                Cost = new ResourceBag(180, 160, 50), BuildTimeSec = 45,
                Counters = null, ShipyardLevelReq = 5,
                TechReqs = new[] { new TechRequirement(TechId.ArmorPlating, 5) },
            },
            [HullId.Scavenger] = new ShipDef
            {
                Name = "Scavenger", Class = "Support",
                Atk = 10, Shield = 20, Hp = 150, Speed = 40, Cargo = 300 * 1000, FuelPerTile = 5,
                Cost = new ResourceBag(150, 90, 30), BuildTimeSec = 30,
                Counters = null, ShipyardLevelReq = 4,
            },

            // ---- Recon ----
            [HullId.Probe] = new ShipDef
            {
                Name = "Spy Probe", Class = "Recon",
                // Speed 3×'d at the pivot, then the whole fleet table 2×'d
                // (2026-07-07, user: still felt slow) — and the Radar Station
                // stacks +5%/level on top of this.
                Atk = 0, Shield = 0, Hp = 10, Speed = 180, Cargo = 0, FuelPerTile = 1,
                Cost = new ResourceBag(10, 20, 5), BuildTimeSec = 4,
                Counters = null, ShipyardLevelReq = 1,
            },
        };
    }
}
