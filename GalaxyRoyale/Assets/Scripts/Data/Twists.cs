// Weekly galaxy twists (late-game content, user-approved 2026-09-30). From the
// second week on, every galaxy week bends one rule for everyone, rivals too:
// faster fleets, richer fields, quicker shipyards. Ten of them in a fixed
// order, so a new colony meets a new one each week for ten weeks, on top of
// the two-week event rotation. TwistSystem applies them.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum TwistKind
    {
        None, LowGravity, PirateUprising, RichVeins, ShipwrightsWeek, BuildersBoom,
        ScholarsWeek, SolarMaximum, SalvageStorm, HuntersMoon, SupplySurge,
    }

    public sealed class TwistDef
    {
        public TwistKind Kind;
        public string Name = "";
        /// <summary>The rule it bends, one line.</summary>
        public string Effect = "";
        /// <summary>HALCYON's word on it when the week begins.</summary>
        public string Flavor = "";
    }

    public static class Twists
    {
        public const int WeekSec = 7 * 24 * 3600;

        public static readonly TwistDef None = new()
        {
            Kind = TwistKind.None, Name = "Settling In", Effect = "No twist in a colony's first week",
        };

        public static readonly IReadOnlyList<TwistDef> Rotation = new[]
        {
            new TwistDef { Kind = TwistKind.LowGravity, Name = "Low Gravity",
                Effect = "Fleets fly 25% faster",
                Flavor = "A gravity lull is rolling across the galaxy. Engines run hot and light this week." },
            new TwistDef { Kind = TwistKind.PirateUprising, Name = "Pirate Uprising",
                Effect = "Pirate camps carry 50% more loot",
                Flavor = "The camps are flush with stolen cargo and spoiling for a fight." },
            new TwistDef { Kind = TwistKind.RichVeins, Name = "Rich Veins",
                Effect = "Fleets gather 50% faster",
                Flavor = "Fresh veins are surfacing in the fields. Send every Hauler you have." },
            new TwistDef { Kind = TwistKind.ShipwrightsWeek, Name = "Shipwright's Week",
                Effect = "Ships build 30% faster",
                Flavor = "The yards are running double shifts across the galaxy." },
            new TwistDef { Kind = TwistKind.BuildersBoom, Name = "Builder's Boom",
                Effect = "Construction runs 20% faster",
                Flavor = "Cheap alloys are flooding the market. Good week to build." },
            new TwistDef { Kind = TwistKind.ScholarsWeek, Name = "Scholars' Week",
                Effect = "Research runs 25% faster",
                Flavor = "Every lab in the galaxy is trading notes this week." },
            new TwistDef { Kind = TwistKind.SolarMaximum, Name = "Solar Maximum",
                Effect = "+20% resource production",
                Flavor = "The stars are at their brightest. Every mine is humming." },
            new TwistDef { Kind = TwistKind.SalvageStorm, Name = "Salvage Storm",
                Effect = "The Salvage Yard recovers twice as much",
                Flavor = "Debris fields are unusually rich. Every wreck is worth stripping." },
            new TwistDef { Kind = TwistKind.HuntersMoon, Name = "Hunter's Moon",
                Effect = "Pirate Lords pay 50% more, and a beaten lord rises at once",
                Flavor = "The lords are restless under the hunter's moon. So are their bounties." },
            new TwistDef { Kind = TwistKind.SupplySurge, Name = "Supply Surge",
                Effect = "Supply drops land twice as often",
                Flavor = "Command is emptying its depots. Keep an eye on the drop zone." },
        };

        public static TwistDef Def(TwistKind kind)
        {
            foreach (var t in Rotation) if (t.Kind == kind) return t;
            return None;
        }
    }
}
