// The Academy (the Citadel, 2026-09-30): your commander's training ground.
//   - More commander XP: +3% a level.
//   - Cheaper skill resets: −2% a level, down to 40% of the price.
//   - Your commander leads a fleet (user: "no captains — use our main character
//     as the captain"): one fleet at a time flies with them, fighting harder by
//     CaptainBonus. If that fleet is wiped out, the commander is wounded and
//     can't lead again for WoundSec.
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class AcademySystem
    {
        public const float XpPerLevel = 0.03f;
        public const float RespecCutPerLevel = 0.02f, RespecFloor = 0.4f;
        public const int WoundSec = 6 * 3600;

        public static int Level(GameState s) => s.Buildings[BuildingId.Academy].Level;

        public static float XpBonus(GameState s) => XpPerLevel * Math.Min(30, Level(s));

        public static float RespecMult(GameState s) => Math.Max(RespecFloor, 1f - RespecCutPerLevel * Level(s));

        /// <summary>A fleet led by your commander fights this much harder (attack and
        /// durability): 2%, +0.4% a commander level and an Academy level, up to 30%.</summary>
        public static float CaptainBonus(GameState s) =>
            Math.Min(0.30f, 0.02f + 0.004f * s.Commander.Level + 0.004f * Level(s));

        public static bool Wounded(GameState s) => s.CaptainWoundedUntilTick > s.Tick;

        public static SimResult CanLead(GameState s)
        {
            if (Level(s) < 1) return SimResult.Fail("Build the Academy to let your commander lead");
            if (s.CaptainMarchId != 0) return SimResult.Fail("Your commander is already leading a fleet");
            if (Wounded(s)) return SimResult.Fail("Your commander is recovering from their wounds");
            return SimResult.Success;
        }

        /// <summary>Put the commander at the head of a fleet you just launched.</summary>
        public static SimResult Lead(GameState s, int marchId)
        {
            var can = CanLead(s);
            if (!can.Ok) return can;
            if (!s.Marches.Exists(m => m.Id == marchId)) return SimResult.Fail("No such fleet");
            s.CaptainMarchId = marchId;
            return SimResult.Success;
        }

        public static bool Leads(GameState s, int marchId) => marchId != 0 && s.CaptainMarchId == marchId;

        /// <summary>The fleet came home: the commander is free again (MarchSystem calls this).</summary>
        public static void OnReturned(GameState s, int marchId)
        {
            if (s.CaptainMarchId == marchId) s.CaptainMarchId = 0;
        }

        /// <summary>Per tick, after the marches: a led fleet that no longer exists was destroyed.</summary>
        public static void Tick(GameState s, SimEventBus events)
        {
            if (s.CaptainMarchId == 0 || s.Marches.Exists(m => m.Id == s.CaptainMarchId)) return;
            s.CaptainMarchId = 0;
            s.CaptainWoundedUntilTick = s.Tick + WoundSec;
            events.Emit(new CommanderWounded(s.CaptainWoundedUntilTick));
        }
    }
}
