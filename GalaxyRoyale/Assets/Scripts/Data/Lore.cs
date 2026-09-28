// Flavor text for the ship and research detail cards — the "another layer of
// detail" pass (user request). Pure content: nothing here feeds the sim, so
// balance changes never have to touch it. Numbers (stats, counters, unlocks)
// are rendered live from ShipDef / TechDef, so these lines stay number-free.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public static class ShipLore
    {
        /// <summary>Battlefield role within the hull's class (the triangle corner
        /// for combat tiers; the job for support/recon hulls).</summary>
        public static readonly IReadOnlyDictionary<HullId, string> Role = new Dictionary<HullId, string>
        {
            [HullId.Fighter] = "Skirmisher", [HullId.Bomber] = "Striker", [HullId.Cruiser] = "Guardian",
            [HullId.Talon] = "Striker", [HullId.Sentinel] = "Guardian", [HullId.Harrier] = "Skirmisher",
            [HullId.Vanguard] = "Striker", [HullId.Rampart] = "Guardian", [HullId.Corsair] = "Skirmisher",
            [HullId.Lancer] = "Striker", [HullId.Bulwark] = "Guardian", [HullId.Javelin] = "Skirmisher",
            [HullId.Behemoth] = "Striker", [HullId.Leviathan] = "Guardian", [HullId.Nomad] = "Skirmisher",
            [HullId.Reaper] = "Striker", [HullId.Warden] = "Guardian", [HullId.Wraith] = "Skirmisher",
            [HullId.Hauler] = "Freighter", [HullId.Atlas] = "Freighter",
            [HullId.Aegis] = "Shield Frigate", [HullId.Scavenger] = "Salvage",
            [HullId.Probe] = "Recon",
        };

        public static readonly IReadOnlyDictionary<HullId, string> Desc = new Dictionary<HullId, string>
        {
            [HullId.Fighter] = "Cheap, nimble single-seat craft — the first hull every shipyard learns to build, and the backbone of an early fleet.",
            [HullId.Bomber] = "Heavy ordnance strapped to a slow frame. Bombers trade speed for a payload that punches well above their size.",
            [HullId.Cruiser] = "A thick-hulled line ship. Early commanders lean on Cruisers to soak fire while lighter craft do the damage.",
            [HullId.Talon] = "A knife-edged interceptor with forward-swept wings, built to pounce before an enemy can form up.",
            [HullId.Sentinel] = "A shield-heavy picket that screens the fleet — slower than its siblings, far harder to bring down.",
            [HullId.Harrier] = "The quickest combat hull in the yard: a hit-and-run skirmisher that is gone before the return fire lands.",
            [HullId.Vanguard] = "The classic ship of the line. Vanguard batteries lead every serious assault.",
            [HullId.Rampart] = "A fortress with engines. Rampart shield arrays shrug off punishment that would gut a lighter hull.",
            [HullId.Corsair] = "A raider's battleship — thinner armor, longer legs, and a habit of hitting where it is least expected.",
            [HullId.Lancer] = "A missile corvette that empties its racks in one devastating salvo. Glass cannon — very sharp glass.",
            [HullId.Bulwark] = "An armored escort corvette whose point-defense grid blunts whatever is thrown at the fleet.",
            [HullId.Javelin] = "A torpedo corvette that darts in fast, fires, and peels away before it can be pinned down.",
            [HullId.Behemoth] = "The heaviest gun platform afloat. Behemoths don't win fights so much as end them.",
            [HullId.Leviathan] = "A dreadnought-class citadel wrapped in layered shields. Nothing moves it quickly; very little moves it at all.",
            [HullId.Nomad] = "A roaming dreadnought that trades a little armor for range, stalking the lanes far from home.",
            [HullId.Reaper] = "A destroyer built for one job: break the enemy's heaviest line and keep going.",
            [HullId.Warden] = "The most heavily shielded hull in service — a destroyer that guards the fleet's flanks and refuses to die.",
            [HullId.Wraith] = "A stealth-hulled destroyer that strikes from the dark and slips away before anyone can answer.",
            [HullId.Hauler] = "A cargo tug with big holds and no teeth — the quiet backbone of every gathering run.",
            [HullId.Atlas] = "A star freighter with cavernous holds, big enough to carry a whole colony's plunder home in one trip.",
            [HullId.Aegis] = "A shield frigate that throws a protective field across the ships flying with it.",
            [HullId.Scavenger] = "A salvage corvette rigged with grapples and cutters for stripping wrecks and derelicts clean.",
            [HullId.Probe] = "A disposable sensor drone — unarmed, blindingly fast, and it sees everything it passes.",
        };

        public static string RoleOf(HullId hull) => Role.TryGetValue(hull, out var r) ? r : "";
        public static string DescOf(HullId hull) => Desc.TryGetValue(hull, out var d) ? d : "";
    }

    public static class TechLore
    {
        public static readonly IReadOnlyDictionary<TechId, string> Desc = new Dictionary<TechId, string>
        {
            [TechId.YieldOptimization] = "Smarter shift scheduling and sorter tuning squeeze more out of every mine.",
            [TechId.DeepCoreDrilling] = "Bores past the crust into the richer seams below.",
            [TechId.IonThrusters] = "Ion drives push every fleet across the void faster.",
            [TechId.CargoHolds] = "Reworked hull bays let every ship carry more home.",
            [TechId.FuelInjection] = "Precision injectors burn less helium for every tile flown.",
            [TechId.WeaponsCalibration] = "Fire-control tuning lands more of every volley on target.",
            [TechId.ArmorPlating] = "Layered alloy plating keeps hulls in the fight longer.",
            [TechId.ExtractionAlgorithms] = "Adaptive routines steer gatherers to the densest deposits in any field.",
            [TechId.DeepVaultProtocols] = "Hardened vaults keep more of your stockpile out of raiders' reach.",
            [TechId.FighterDoctrine] = "Wing tactics drilled into every Fighter pilot.",
            [TechId.BomberPayloads] = "Denser warheads for Bomber racks.",
            [TechId.CruiserBroadsides] = "Cruiser crews learn to bring every gun to bear at once.",
            [TechId.FighterPlating] = "Light composite armor for Fighter frames.",
            [TechId.BomberHulls] = "Reinforced spines so Bombers survive their own runs.",
            [TechId.CruiserBulkheads] = "Compartmentalized decks keep a damaged Cruiser flying.",
            [TechId.RapidFabrication] = "Streamlined assembly lines turn hulls out faster.",
            [TechId.PrefabAssembly] = "Pre-built modules snap into place, speeding every construction.",
            [TechId.AntimatterWarheads] = "Contained antimatter charges hit harder than any chemical round.",
            [TechId.DeflectorArray] = "Stronger field emitters for every ship's regenerating shields.",
            [TechId.ReinforcedHulls] = "Structural reinforcement across the entire fleet.",
            [TechId.NaniteRepairSwarms] = "Microscopic repair drones patch hulls in the middle of battle.",
            [TechId.SwarmFabricators] = "Self-replicating fabricators work the slipways around the clock.",
            [TechId.QuantumExtractors] = "Quantum-tunneling extractors pull resources out of a field at record speed.",
            [TechId.QuantumComputing] = "Qubit clusters run lab simulations in a fraction of the time.",
            [TechId.SingularityCores] = "Micro-singularity power cores keep every lab running flat out.",
            [TechId.OrbitalAssembly] = "Orbital cranes lift whole building sections into place at once.",
            [TechId.BastionHangars] = "Armored berths and damage-control crews keep your home defenders flying.",
            [TechId.PointDefenseGrid] = "Colony fire-control links every docked ship's guns when raiders arrive.",
            [TechId.OrbitalBatteries] = "Planetary guns that open fire on every raid — even with no fleet at home. Their shells punch straight through ship shields.",
            [TechId.PlanetaryDeflectors] = "Ground-based emitters wrap your home fleet in heavier shields.",
        };

        public static string DescOf(TechId tech) => Desc.TryGetValue(tech, out var d) ? d : "";
    }
}
