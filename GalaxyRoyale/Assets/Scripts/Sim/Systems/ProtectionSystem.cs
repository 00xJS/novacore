// Beginner protection (balance pass 2026-09-30): a new colony starts off every
// rival's target list, like an Aegis Shield that can't be bought. It ends after
// Balance.BeginnerProtectionSec, when the Command Center reaches
// Balance.BeginnerProtectionEndsAtCc, or the moment the player raids another
// commander (BotSystem.BreakShieldForAggression). Raiding pirate camps is fine.
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class ProtectionSystem
    {
        public static bool Active(GameState s, int atTick) => s.Buffs.ProtectionUntilTick > atTick;
        public static bool Active(GameState s) => Active(s, s.Tick);

        /// <summary>Nobody may raid or scan this colony at that tick: the Aegis or protection.</summary>
        public static bool Untargetable(GameState s, int atTick) =>
            s.Buffs.ShieldUntilTick > atTick || Active(s, atTick);

        /// <summary>Seconds of protection left (0 when none).</summary>
        public static int SecondsLeft(GameState s) => Active(s) ? s.Buffs.ProtectionUntilTick - s.Tick : 0;

        public static void Tick(GameState s, SimEventBus events)
        {
            if (s.Buffs.ProtectionUntilTick == 0) return;
            if (s.Tick >= s.Buffs.ProtectionUntilTick) End(s, events, "time");
            else if (s.Buildings[BuildingId.CommandCenter].Level >= Balance.BeginnerProtectionEndsAtCc)
                End(s, events, "cc");
        }

        /// <summary>Drop it now (raiding a commander). True if it was up.</summary>
        public static bool Break(GameState s, SimEventBus? events)
        {
            if (!Active(s)) return false;
            End(s, events, "raid");
            return true;
        }

        static void End(GameState s, SimEventBus? events, string reason)
        {
            s.Buffs.ProtectionUntilTick = 0;
            events?.Emit(new ProtectionEnded(reason));
        }
    }
}
