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

        /// <summary>Nothing docked to fight and no batteries — the raid lands unopposed.</summary>
        public bool Unopposed => EnemyShips == 0 && Battery == 0;
        /// <summary>The defender's Orbital Batteries level (0 = none).</summary>
        public int Battery;
        /// <summary>Your whole fleet is destroyed.</summary>
        public bool Wiped => YourShips > 0 && YourLosses >= YourShips;

        /// <summary>
        /// Forecast `fleet` attacking `defenders` — `atkMods` your research,
        /// `defMods` theirs (a rival's home defenses; pirates have none). The same
        /// call the arrival battle makes (MarchSystem for camps, RaidArrivals for
        /// rival colonies).
        /// </summary>
        public static BattleForecast Predict(Dictionary<HullId, int> fleet,
            Dictionary<HullId, int> defenders, FleetMods atkMods, FleetMods defMods = default)
        {
            var report = CombatResolver.Resolve(fleet, defenders, atkMods, defMods);
            var forecast = new BattleForecast
            {
                Winner = report.Winner,
                Rounds = report.Rounds.Count,
                YourShips = CombatResolver.FleetCount(report.Attacker),
                EnemyShips = CombatResolver.FleetCount(report.Defender),
                Battery = report.DefenderBattery,
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
