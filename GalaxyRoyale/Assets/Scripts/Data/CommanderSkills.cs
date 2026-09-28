// Commander progression (build-all plan, 2026-09-28): everything the commander
// does earns XP (CommanderSystem), every level past the first earns a skill
// point, and points buy ranks in three branches. A skill's effects are the
// same effect kinds research uses, so every system that reads research
// bonuses picks them up without knowing skills exist.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum SkillBranch { Industry, Admiralty, Bastion }

    public readonly struct SkillEffect
    {
        public readonly TechEffectKind Kind;
        /// <summary>Per rank, in the research units (0.04 = +4%, or −4% for a Reduce kind;
        /// OrbitalBattery counts whole battery levels).</summary>
        public readonly float PerRank;
        public SkillEffect(TechEffectKind kind, float perRank) { Kind = kind; PerRank = perRank; }
    }

    public sealed class SkillDef
    {
        public string Id = "";
        public string Name = "";
        public SkillBranch Branch;
        /// <summary>1–4. A tier opens once the branch has CommanderSkills.TierPoints[tier − 1] points in it.</summary>
        public int Tier;
        public int MaxRank;
        public SkillEffect[] Effects = System.Array.Empty<SkillEffect>();
    }

    public static class CommanderSkills
    {
        /// <summary>Points a branch needs before each tier opens.</summary>
        public static readonly int[] TierPoints = { 0, 3, 6, 12 };

        public static readonly IReadOnlyList<SkillBranch> Branches =
            new[] { SkillBranch.Industry, SkillBranch.Admiralty, SkillBranch.Bastion };

        static SkillDef S(string id, string name, SkillBranch branch, int tier, int maxRank,
            params (TechEffectKind kind, float perRank)[] effects)
        {
            var list = new SkillEffect[effects.Length];
            for (int i = 0; i < effects.Length; i++) list[i] = new SkillEffect(effects[i].kind, effects[i].perRank);
            return new SkillDef { Id = id, Name = name, Branch = branch, Tier = tier, MaxRank = maxRank, Effects = list };
        }

        const SkillBranch I = SkillBranch.Industry, A = SkillBranch.Admiralty, B = SkillBranch.Bastion;
        const TechEffectKind Prod = TechEffectKind.ProdMultiplier, Gather = TechEffectKind.GatherRateMult,
            Build = TechEffectKind.BuildTimeReduce, Helium = TechEffectKind.HeliumReduce,
            Research = TechEffectKind.ResearchTimeReduce, Cargo = TechEffectKind.CargoMult,
            Atk = TechEffectKind.AtkMult, Hp = TechEffectKind.HpMult, Speed = TechEffectKind.MarchSpeedMult,
            ShipTime = TechEffectKind.ShipTimeReduce, Shield = TechEffectKind.ShieldMult,
            DefAtk = TechEffectKind.DefAtkMult, DefHp = TechEffectKind.DefHpMult,
            DefShield = TechEffectKind.DefShieldMult, Vault = TechEffectKind.ShieldCapMult,
            Battery = TechEffectKind.OrbitalBattery;

        public static readonly IReadOnlyList<SkillDef> All = new[]
        {
            // ---- Industry: the economy ----
            S("prospector", "Prospector", I, 1, 3, (Prod, 0.04f)),
            S("salvage-crews", "Salvage Crews", I, 1, 3, (Gather, 0.06f)),
            S("site-foremen", "Site Foremen", I, 2, 3, (Build, 0.04f)),
            S("quartermasters", "Quartermasters", I, 2, 3, (Helium, 0.06f)),
            S("lab-directors", "Lab Directors", I, 3, 3, (Research, 0.05f)),
            S("deep-holds", "Deep Holds", I, 3, 3, (Cargo, 0.08f)),
            S("golden-age", "Golden Age", I, 4, 1, (Prod, 0.10f), (Gather, 0.10f)),

            // ---- Admiralty: the fleet in the field ----
            S("gunnery-drills", "Gunnery Drills", A, 1, 3, (Atk, 0.03f)),
            S("hull-riveters", "Hull Riveters", A, 1, 3, (Hp, 0.03f)),
            S("afterburners", "Afterburners", A, 2, 3, (Speed, 0.05f)),
            S("dockyard-rhythm", "Dockyard Rhythm", A, 2, 3, (ShipTime, 0.05f)),
            S("shield-harmonics", "Shield Harmonics", A, 3, 3, (Shield, 0.05f)),
            S("veteran-crews", "Veteran Crews", A, 3, 3, (Atk, 0.02f), (Hp, 0.02f)),
            S("fleet-admiral", "Fleet Admiral", A, 4, 1, (Atk, 0.06f), (Hp, 0.06f), (Speed, 0.10f)),

            // ---- Bastion: the home colony ----
            S("home-guard", "Home Guard", B, 1, 3, (DefAtk, 0.04f)),
            S("bulwark", "Bulwark", B, 1, 3, (DefHp, 0.04f)),
            S("deflector-crews", "Deflector Crews", B, 2, 3, (DefShield, 0.06f)),
            S("vault-wardens", "Vault Wardens", B, 2, 3, (Vault, 0.12f)),
            S("orbital-gunners", "Orbital Gunners", B, 3, 2, (Battery, 1f)),
            S("militia", "Militia", B, 3, 3, (DefAtk, 0.03f), (DefHp, 0.03f)),
            S("fortress-world", "Fortress World", B, 4, 1, (DefAtk, 0.10f), (DefHp, 0.10f), (DefShield, 0.10f)),
        };

        static readonly Dictionary<string, SkillDef> s_byId = Index();

        static Dictionary<string, SkillDef> Index()
        {
            var d = new Dictionary<string, SkillDef>();
            foreach (var s in All) d[s.Id] = s;
            return d;
        }

        public static SkillDef? ById(string id) => s_byId.TryGetValue(id, out var s) ? s : null;

        public static string BranchName(SkillBranch b) => b switch
        {
            SkillBranch.Industry => "INDUSTRY",
            SkillBranch.Admiralty => "ADMIRALTY",
            _ => "BASTION",
        };

        public static string BranchPitch(SkillBranch b) => b switch
        {
            SkillBranch.Industry => "Production, gathering, construction and research.",
            SkillBranch.Admiralty => "Your fleets in the field: damage, hulls, shields and speed.",
            _ => "Your home colony's defenders, guns and vaults.",
        };

        /// <summary>"+4% resource production" — one effect at a total magnitude.</summary>
        public static string Describe(SkillEffect e, float total)
        {
            if (e.Kind == TechEffectKind.OrbitalBattery)
                return $"+{total:0} Orbital Battery level{(total == 1f ? "" : "s")}";
            string pct = $"{total * 100f:0.#}%";
            return e.Kind switch
            {
                TechEffectKind.ProdMultiplier => $"+{pct} resource production",
                TechEffectKind.GatherRateMult => $"+{pct} gathering speed",
                TechEffectKind.BuildTimeReduce => $"−{pct} construction and shipbuilding time",
                TechEffectKind.HeliumReduce => $"−{pct} helium burned in flight",
                TechEffectKind.ResearchTimeReduce => $"−{pct} research time",
                TechEffectKind.CargoMult => $"+{pct} cargo space",
                TechEffectKind.AtkMult => $"+{pct} fleet damage",
                TechEffectKind.HpMult => $"+{pct} fleet durability",
                TechEffectKind.MarchSpeedMult => $"+{pct} flight speed",
                TechEffectKind.ShipTimeReduce => $"−{pct} ship build time",
                TechEffectKind.ShieldMult => $"+{pct} ship shields",
                TechEffectKind.DefAtkMult => $"+{pct} damage defending your colony",
                TechEffectKind.DefHpMult => $"+{pct} durability defending your colony",
                TechEffectKind.DefShieldMult => $"+{pct} shields defending your colony",
                TechEffectKind.ShieldCapMult => $"+{pct} raid-protected storage",
                _ => $"+{pct}",
            };
        }

        /// <summary>Every effect of a skill at a rank, joined ("+3% fleet damage · +3% fleet durability").</summary>
        public static string Describe(SkillDef s, int rank)
        {
            var parts = new List<string>();
            foreach (var e in s.Effects) parts.Add(Describe(e, e.PerRank * rank));
            return string.Join(" · ", parts);
        }
    }
}
