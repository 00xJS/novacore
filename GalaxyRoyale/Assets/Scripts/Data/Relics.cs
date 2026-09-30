// Relics (the Citadel, 2026-09-30): every relic found in the Wilds is one of six
// kinds, kept by the Relic Vault. Each copy of a kind adds its bonus, up to as
// many copies as the vault has levels (Sim/Systems/RelicSystem).
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum RelicKind { AncientDrill, StarChart, VoidLens, HullShard, WarIdol, VaultSeal }

    public sealed class RelicDef
    {
        public RelicKind Kind;
        public string Name = "";
        public string Desc = "";
        public TechEffectKind Effect;
        /// <summary>What each copy on display adds (0.01 = +1%).</summary>
        public float PerCopy;
    }

    public static class Relics
    {
        public static readonly IReadOnlyList<RelicDef> All = new[]
        {
            new RelicDef { Kind = RelicKind.AncientDrill, Name = "Ancient Drill", Effect = TechEffectKind.ProdMultiplier, PerCopy = 0.01f,
                Desc = "resource production" },
            new RelicDef { Kind = RelicKind.StarChart, Name = "Star Chart", Effect = TechEffectKind.MarchSpeedMult, PerCopy = 0.01f,
                Desc = "march speed" },
            new RelicDef { Kind = RelicKind.VoidLens, Name = "Void Lens", Effect = TechEffectKind.GatherRateMult, PerCopy = 0.02f,
                Desc = "gathering speed" },
            new RelicDef { Kind = RelicKind.HullShard, Name = "Hull Shard", Effect = TechEffectKind.HpMult, PerCopy = 0.01f,
                Desc = "ship durability" },
            new RelicDef { Kind = RelicKind.WarIdol, Name = "War Idol", Effect = TechEffectKind.AtkMult, PerCopy = 0.01f,
                Desc = "ship attack" },
            new RelicDef { Kind = RelicKind.VaultSeal, Name = "Vault Seal", Effect = TechEffectKind.ShieldCapMult, PerCopy = 0.02f,
                Desc = "raid-protected storage" },
        };

        public static RelicDef Def(RelicKind kind) => All[(int)kind];
    }
}
