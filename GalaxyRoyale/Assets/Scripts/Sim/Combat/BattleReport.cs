// Battle report shape. Direct port of v1's `src/sim/combat/CombatResolver.ts`
// interfaces. FleetComp is just a Dictionary<HullId, int> throughout — matches
// v1's `Partial<Record<HullId, number>>` semantics (absent hull = 0).
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Combat
{
    public enum BattleWinner { Attacker, Defender, Draw }

    public sealed class RoundLog
    {
        public int Round;
        public Dictionary<HullId, int> AttackerLosses = new();
        public Dictionary<HullId, int> DefenderLosses = new();
    }

    public sealed class BattleReport
    {
        public Dictionary<HullId, int> Attacker = new();
        public Dictionary<HullId, int> Defender = new();
        public BattleWinner Winner;
        public List<RoundLog> Rounds = new();
        public Dictionary<HullId, int> AttackerSurvivors = new();
        public Dictionary<HullId, int> DefenderSurvivors = new();
        /// <summary>Filled in by MarchSystem when the fight was over something lootable.</summary>
        public ResourceBag? Loot;
        public TileXY? Location;
        public string? DefenderName;
        /// <summary>Defender's Orbital Batteries level (0 = none) — the planetary
        /// guns that fired on the attackers every round.</summary>
        public int DefenderBattery;
    }

    /// <summary>One side's research bonuses in a battle — ResearchSystem.CombatMods
    /// when attacking, ResearchSystem.DefenseMods when defending the home colony
    /// (adds the Defense branch and the Orbital Batteries). Defaults = no effect.</summary>
    public readonly struct FleetMods
    {
        /// <summary>Multiplies the side's outgoing damage (weapons research).</summary>
        public readonly float AtkMult;
        /// <summary>Divides the damage the side takes (armor research).</summary>
        public readonly float HpMult;
        /// <summary>Optional per-hull attack multipliers (global + hull-scoped techs). Missing hull → AtkMult.</summary>
        public readonly IReadOnlyDictionary<HullId, float>? AtkByHull;
        /// <summary>Optional per-hull durability multipliers. Missing hull → HpMult.</summary>
        public readonly IReadOnlyDictionary<HullId, float>? HpByHull;
        /// <summary>Multiplies the side's regenerating shields (Deflector Array research).</summary>
        public readonly float ShieldMult;
        /// <summary>Orbital Batteries level — defenders only (0 = none).</summary>
        public readonly int BatteryLevel;

        public FleetMods(float atkMult = 1f, float hpMult = 1f,
            IReadOnlyDictionary<HullId, float>? atkByHull = null,
            IReadOnlyDictionary<HullId, float>? hpByHull = null,
            float shieldMult = 1f, int batteryLevel = 0)
        {
            AtkMult = atkMult;
            HpMult = hpMult;
            AtkByHull = atkByHull;
            HpByHull = hpByHull;
            ShieldMult = shieldMult;
            BatteryLevel = batteryLevel;
        }

        /// <summary>Shield multiplier with the default-struct guard (0 → 1).</summary>
        public float ShieldFor() => ShieldMult > 0 ? ShieldMult : 1f;

        /// <summary>Outgoing-damage multiplier for one hull.</summary>
        public float AtkFor(HullId hull) =>
            AtkByHull != null && AtkByHull.TryGetValue(hull, out var v) ? v : (AtkMult > 0 ? AtkMult : 1f);

        /// <summary>Durability multiplier for one hull taking damage.</summary>
        public float HpFor(HullId hull) =>
            HpByHull != null && HpByHull.TryGetValue(hull, out var v) ? v : (HpMult > 0 ? HpMult : 1f);

        public static readonly FleetMods None = new(1f, 1f);
    }
}
