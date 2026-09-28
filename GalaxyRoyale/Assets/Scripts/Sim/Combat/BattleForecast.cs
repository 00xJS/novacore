// Battle forecast for the launch screens. The resolver is pure and RNG-free,
// so running it on what the player knows — a pirate camp's fixed garrison, or
// a scanned rival's docked fleet — predicts the fight exactly, provided nothing
// changes before the fleet arrives (rivals keep building; camps never do).
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Combat
{
    public sealed class BattleForecast
    {
        public BattleWinner Winner;
        public int Rounds;
        public int YourShips;
        public int YourLosses;
        public int EnemyShips;
        public int EnemyLosses;
        /// <summary>Your ships lost, per hull (absent = none lost).</summary>
        public Dictionary<HullId, int> YourLossesByHull = new();

        /// <summary>Nothing docked to fight — the raid lands unopposed.</summary>
        public bool Unopposed => EnemyShips == 0;
        /// <summary>Your whole fleet is destroyed.</summary>
        public bool Wiped => YourShips > 0 && YourLosses >= YourShips;

        /// <summary>
        /// Forecast `fleet` attacking `defenders` with the attacker's research
        /// `mods` — the same call the arrival battle makes (MarchSystem for camps,
        /// RaidArrivals for rival colonies).
        /// </summary>
        public static BattleForecast Predict(Dictionary<HullId, int> fleet,
            Dictionary<HullId, int> defenders, AttackerMods mods)
        {
            var report = CombatResolver.Resolve(fleet, defenders, mods);
            var forecast = new BattleForecast
            {
                Winner = report.Winner,
                Rounds = report.Rounds.Count,
                YourShips = CombatResolver.FleetCount(report.Attacker),
                EnemyShips = CombatResolver.FleetCount(report.Defender),
            };
            foreach (var hull in Ships.All)
            {
                int sent = report.Attacker.TryGetValue(hull, out var s) ? s : 0;
                int left = report.AttackerSurvivors.TryGetValue(hull, out var a) ? a : 0;
                if (sent - left > 0) forecast.YourLossesByHull[hull] = sent - left;
                forecast.YourLosses += System.Math.Max(0, sent - left);
            }
            forecast.EnemyLosses = forecast.EnemyShips - CombatResolver.FleetCount(report.DefenderSurvivors);
            return forecast;
        }
    }
}
