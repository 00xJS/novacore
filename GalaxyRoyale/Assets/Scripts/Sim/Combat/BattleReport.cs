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
    }

    /// <summary>Attacker-side research bonuses. Defaults = no effect.</summary>
    public readonly struct AttackerMods
    {
        /// <summary>Multiplies the attacker's outgoing damage (weapons research).</summary>
        public readonly float AtkMult;
        /// <summary>Divides the damage the attacker takes (armor research).</summary>
        public readonly float HpMult;
        /// <summary>Optional per-hull attack multipliers (global + hull-scoped techs). Missing hull → AtkMult.</summary>
        public readonly IReadOnlyDictionary<HullId, float>? AtkByHull;
        /// <summary>Optional per-hull durability multipliers. Missing hull → HpMult.</summary>
        public readonly IReadOnlyDictionary<HullId, float>? HpByHull;
        /// <summary>Multiplies the attacker's regenerating shields (Deflector Array research).</summary>
        public readonly float ShieldMult;

        public AttackerMods(float atkMult = 1f, float hpMult = 1f,
            IReadOnlyDictionary<HullId, float>? atkByHull = null,
            IReadOnlyDictionary<HullId, float>? hpByHull = null,
            float shieldMult = 1f)
        {
            AtkMult = atkMult;
            HpMult = hpMult;
            AtkByHull = atkByHull;
            HpByHull = hpByHull;
            ShieldMult = shieldMult;
        }

        /// <summary>Shield multiplier with the default-struct guard (0 → 1).</summary>
        public float ShieldFor() => ShieldMult > 0 ? ShieldMult : 1f;

        /// <summary>Outgoing-damage multiplier for one attacking hull.</summary>
        public float AtkFor(HullId hull) =>
            AtkByHull != null && AtkByHull.TryGetValue(hull, out var v) ? v : (AtkMult > 0 ? AtkMult : 1f);

        /// <summary>Durability multiplier for one attacker hull taking damage.</summary>
        public float HpFor(HullId hull) =>
            HpByHull != null && HpByHull.TryGetValue(hull, out var v) ? v : (HpMult > 0 ? HpMult : 1f);

        public static readonly AttackerMods None = new(1f, 1f);
    }
}
