// Mega-projects (late-game content, user-approved 2026-09-30). Four colony-wide
// works, each built in five stages over days, that open as the Command Center
// climbs past 15: long goals for the stretch where the content-cadence run
// found nothing new, and a sink for the resources a late colony piles up.
// Each stage costs a day's worth (and more) of the colony's mine output and
// adds a lasting bonus. MegaprojectSystem builds them, one stage at a time.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum MegaprojectKind { DysonSwarm, Stargate, ShieldArray, OrbitalFoundry }

    public sealed class MegaprojectDef
    {
        public MegaprojectKind Kind;
        public string Name = "";
        public string Blurb = "";
        public int UnlockCc;
        /// <summary>Per stage: effect kind → amount (research effect totals).</summary>
        public IReadOnlyDictionary<TechEffectKind, float> PerStage = new Dictionary<TechEffectKind, float>();
        /// <summary>Per stage: extra Power Plant output (Dyson Swarm only).</summary>
        public float EnergyPerStage;
        /// <summary>What each stage adds, for the card.</summary>
        public string StageBonus = "";
    }

    public static class Megaprojects
    {
        public const int Stages = 5;
        /// <summary>Stage N costs N × this many hours of the colony's mine output …</summary>
        public const int HoursPerStage = 20;
        /// <summary>… and takes N × this many hours to build.</summary>
        public const int BuildHoursPerStage = 10;

        public static readonly IReadOnlyList<MegaprojectDef> All = new[]
        {
            new MegaprojectDef
            {
                Kind = MegaprojectKind.DysonSwarm, Name = "Dyson Swarm", UnlockCc = 15,
                Blurb = "A cloud of collector mirrors around your star, beaming its light home.",
                PerStage = new Dictionary<TechEffectKind, float> { [TechEffectKind.ProdMultiplier] = 0.08f },
                EnergyPerStage = 0.10f,
                StageBonus = "+8% production, +10% power",
            },
            new MegaprojectDef
            {
                Kind = MegaprojectKind.Stargate, Name = "Stargate", UnlockCc = 17,
                Blurb = "A ring in high orbit that throws your fleets down folded lanes.",
                PerStage = new Dictionary<TechEffectKind, float>
                    { [TechEffectKind.MarchSpeedMult] = 0.06f, [TechEffectKind.CargoMult] = 0.08f },
                StageBonus = "+6% fleet speed, +8% cargo",
            },
            new MegaprojectDef
            {
                Kind = MegaprojectKind.ShieldArray, Name = "Planetary Shield Array", UnlockCc = 19,
                Blurb = "Shield emitters in a lattice around the planet: raiders meet a wall.",
                PerStage = new Dictionary<TechEffectKind, float>
                {
                    [TechEffectKind.DefHpMult] = 0.06f, [TechEffectKind.DefShieldMult] = 0.06f,
                    [TechEffectKind.ShieldCapMult] = 0.10f,
                },
                StageBonus = "+6% home defense hull and shields, +10% Warehouse shield",
            },
            new MegaprojectDef
            {
                Kind = MegaprojectKind.OrbitalFoundry, Name = "Orbital Foundry", UnlockCc = 21,
                Blurb = "A zero-gravity forge that casts hull plate and gun barrels no planet could.",
                PerStage = new Dictionary<TechEffectKind, float>
                    { [TechEffectKind.AtkMult] = 0.03f, [TechEffectKind.HpMult] = 0.03f },
                StageBonus = "+3% fleet attack and hull",
            },
        };

        public static MegaprojectDef Def(MegaprojectKind kind) => All[(int)kind];
    }
}
