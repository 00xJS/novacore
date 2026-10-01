// Radar Station effects (content expansion, 2026-07-05). Pure helpers — the
// polling/warning flow lives in the Game layer; everything numeric is here so
// it stays testable and portable to the future authoritative server.
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class RadarSystem
    {
        public static int Level(GameState state) =>
            state.Buildings.TryGetValue(BuildingId.RadarStation, out var slot) ? slot.Level : 0;

        /// <summary>Seconds before arrival at which an inbound threat is announced. 0 = no radar, no warning.</summary>
        public static int WarnLeadSeconds(int radarLevel) =>
            radarLevel < 1 ? 0 : radarLevel * Balance.RadarLeadPerLevelSec;

        /// <summary>
        /// How much an incoming-threat warning reveals:
        /// 0 = nothing (no radar) · 1 = unknown contact + ETA · 2 = contact type (spy vs fleet)
        /// · 3 = + fleet size and attacker name · 4 = + full hull composition.
        /// </summary>
        /// <summary>This colony's warning lead: the Radar Station's, stretched by the
        /// Deep Space Observatory. The Ion Storm blinds it while the colony sits
        /// inside (map events, 2026-09-30).</summary>
        public static int WarnLeadSeconds(GameState state) =>
            EventSites.RadarBlind(state) ? 0
            : (int)System.Math.Round(WarnLeadSeconds(Level(state)) * ObservatorySystem.RadarLeadMult(state)
                * TwistSystem.RadarLeadMult(state)); // Clear Skies

        public static int DetailTier(int radarLevel) =>
            radarLevel < 1 ? 0
            : radarLevel < 5 ? 1
            : radarLevel < 10 ? 2
            : radarLevel < 15 ? 3
            : 4;

        /// <summary>Own-probe speed multiplier (a probe-only fleet flies faster per radar level).</summary>
        public static float ProbeSpeedMult(GameState state) =>
            1f + Level(state) * Balance.RadarProbeSpeedPerLevel;

        /// <summary>Radar speed bonus for a fleet: applies only when every ship in it is a spy probe.</summary>
        public static float FleetSpeedMult(GameState state, Dictionary<HullId, int> ships)
        {
            bool anyProbe = false;
            foreach (var kv in ships)
            {
                if (kv.Value <= 0) continue;
                if (kv.Key != HullId.Probe) return 1f;
                anyProbe = true;
            }
            return anyProbe ? ProbeSpeedMult(state) : 1f;
        }
    }
}
