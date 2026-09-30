// Ship modules and blueprints (late-game content, user-approved 2026-09-30).
// Fleets used to grow only in numbers. Now each hull class takes one refit
// module, a trade between firepower and staying power, fitted at the
// shipyard (MORE › REFITS). Modules are learned from blueprints: Pirate
// Lords carry them (their lairs), and rivals find some off-screen. A second
// and third copy of a blueprint raise the module to Mk II and Mk III.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum ModuleKind
    {
        BalancedRefit, OverchargedCannons, AblativePlating, TwinLinkedGuns, BulwarkFrames, PlasmaLances,
    }

    public sealed class ModuleDef
    {
        public ModuleKind Kind;
        public string Name = "";
        public string Blurb = "";
        /// <summary>Mk I's attack and hull change (Mk II ×1.5, Mk III ×2).</summary>
        public float Atk, Hp;
    }

    public static class Modules
    {
        public const int MaxMark = 3;

        /// <summary>The hull classes a module is fitted to (Ships' Class names, Support and Recon aside).</summary>
        public static readonly IReadOnlyList<string> Classes = new[]
        {
            "Strike Craft", "Interceptors", "Battleships", "Corvettes", "Dreadnoughts", "Destroyers",
        };

        public static readonly IReadOnlyList<ModuleDef> All = new[]
        {
            new ModuleDef { Kind = ModuleKind.BalancedRefit, Name = "Balanced Refit",
                Blurb = "A little more of everything.", Atk = 0.05f, Hp = 0.05f },
            new ModuleDef { Kind = ModuleKind.OverchargedCannons, Name = "Overcharged Cannons",
                Blurb = "Guns run hot; the hull pays for it.", Atk = 0.15f, Hp = -0.05f },
            new ModuleDef { Kind = ModuleKind.AblativePlating, Name = "Ablative Plating",
                Blurb = "Armour that burns away instead of the ship.", Atk = -0.05f, Hp = 0.15f },
            new ModuleDef { Kind = ModuleKind.TwinLinkedGuns, Name = "Twin-Linked Guns",
                Blurb = "Paired barrels, twice the volleys.", Atk = 0.10f, Hp = 0f },
            new ModuleDef { Kind = ModuleKind.BulwarkFrames, Name = "Bulwark Frames",
                Blurb = "A stiffer spine that shrugs off hits.", Atk = 0f, Hp = 0.10f },
            new ModuleDef { Kind = ModuleKind.PlasmaLances, Name = "Plasma Lances",
                Blurb = "Devastating, and devastatingly fragile.", Atk = 0.20f, Hp = -0.10f },
        };

        public static ModuleDef Def(ModuleKind kind) => All[(int)kind];

        /// <summary>Mk I, II, III scale the module's effect.</summary>
        public static float MarkScale(int mark) => mark <= 1 ? 1f : mark == 2 ? 1.5f : 2f;

        public static string MarkName(int mark) => mark switch { 2 => "Mk II", >= 3 => "Mk III", _ => "Mk I" };
    }
}
