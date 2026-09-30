// Expeditions (2026-09-30): fleets sent beyond the charted galaxy for a few
// hours. Halfway there, something happens and you choose: a bold option (more
// reward, and a chance it goes wrong) or a careful one. The result comes back as
// a short story. Six destinations; the stories are written here.
using System.Collections.Generic;

namespace GalaxyRoyale.Data
{
    public enum ExpeditionKind { DerelictSignal, UnchartedNebula, PirateGraveyard, AncientBeacon, RogueMoon, IceFields }

    public sealed class ExpeditionDef
    {
        public ExpeditionKind Kind;
        public string Name = "";
        /// <summary>The destination, one line.</summary>
        public string Blurb = "";
        /// <summary>What happens halfway, and the two choices.</summary>
        public string Moment = "";
        public string BoldChoice = "", SafeChoice = "";
        /// <summary>How each ending reads ("{fleet}" is the fleet's name).</summary>
        public string BoldWin = "", BoldLoss = "", Safe = "";
        /// <summary>The relic a bold win is most likely to turn up.</summary>
        public RelicKind Relic;
    }

    public static class Expeditions
    {
        public static readonly IReadOnlyList<ExpeditionDef> All = new[]
        {
            new ExpeditionDef
            {
                Kind = ExpeditionKind.DerelictSignal, Name = "Derelict Signal", Relic = RelicKind.HullShard,
                Blurb = "A distress beacon pulses from a hulk drifting past the rim.",
                Moment = "Your crews reach the hulk. Its reactor is failing — the holds are full, but it could blow at any moment.",
                BoldChoice = "BOARD IT", SafeChoice = "STRIP THE HULL",
                BoldWin = "The boarding party cleared the holds with minutes to spare and flew home heavy with salvage.",
                BoldLoss = "The reactor went critical with the boarding party still aboard. The survivors limped home.",
                Safe = "Your crews cut plating from the outer hull and left the reactor to its fate.",
            },
            new ExpeditionDef
            {
                Kind = ExpeditionKind.UnchartedNebula, Name = "Uncharted Nebula", Relic = RelicKind.StarChart,
                Blurb = "A nebula no chart shows. Sensors can't see past its edge.",
                Moment = "Deep in the fog, something vast is moving. Its wake glitters with ore.",
                BoldChoice = "HUNT IT", SafeChoice = "CHART AND LEAVE",
                BoldWin = "Your fleet ran the creature down and mined the glittering wake it left behind.",
                BoldLoss = "The thing turned on your fleet. It was far bigger than the sensors said.",
                Safe = "Your navigators mapped the nebula's currents and slipped away before it noticed them.",
            },
            new ExpeditionDef
            {
                Kind = ExpeditionKind.PirateGraveyard, Name = "Pirate Graveyard", Relic = RelicKind.WarIdol,
                Blurb = "Where pirate fleets go to die — and their hoards with them.",
                Moment = "A pirate salvage crew is already picking over the wrecks, and they've seen you.",
                BoldChoice = "DRIVE THEM OFF", SafeChoice = "SHARE THE SPOILS",
                BoldWin = "The salvagers broke and ran, leaving the graveyard's best hoards to your fleet.",
                BoldLoss = "The salvagers had friends hiding in the wrecks. Your fleet fought its way out.",
                Safe = "You split the graveyard with the salvagers and both crews went home richer.",
            },
            new ExpeditionDef
            {
                Kind = ExpeditionKind.AncientBeacon, Name = "Ancient Beacon", Relic = RelicKind.VaultSeal,
                Blurb = "A signal older than any colony in the galaxy.",
                Moment = "The beacon opens as your fleet arrives, offering a sealed casket. Something else is waking up too.",
                BoldChoice = "TAKE THE CASKET", SafeChoice = "RECORD AND GO",
                BoldWin = "Your fleet was gone with the casket before the guardians fully woke.",
                BoldLoss = "The beacon's guardians woke. Your fleet escaped, but not all of it.",
                Safe = "Your science officers recorded every signal the beacon sent — scholars will pay for that.",
            },
            new ExpeditionDef
            {
                Kind = ExpeditionKind.RogueMoon, Name = "Rogue Moon", Relic = RelicKind.AncientDrill,
                Blurb = "A wandering moon rich in ore, passing close to the rim.",
                Moment = "The moon's orbit is decaying faster than expected. Every hour of mining is worth a fortune — and a risk.",
                BoldChoice = "MINE TO THE END", SafeChoice = "PULL OUT EARLY",
                BoldWin = "Your crews mined until the moon broke apart, and flew home through the debris with full holds.",
                BoldLoss = "The moon broke apart early. Some of your ships didn't clear the debris in time.",
                Safe = "Your crews took what they could safely carry and left the moon to its fall.",
            },
            new ExpeditionDef
            {
                Kind = ExpeditionKind.IceFields, Name = "Ice Fields", Relic = RelicKind.VoidLens,
                Blurb = "A field of frozen comets, thick with helium ice.",
                Moment = "A cometary storm is closing in on the field.",
                BoldChoice = "PUSH THROUGH", SafeChoice = "SHELTER AND WAIT",
                BoldWin = "Your fleet rode the storm through the richest part of the field.",
                BoldLoss = "The storm tore through your formation before it could find shelter.",
                Safe = "Your fleet sheltered behind a comet until the storm passed, then harvested the edges.",
            },
        };

        public static ExpeditionDef Def(ExpeditionKind kind) => All[(int)kind];
    }
}
