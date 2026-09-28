// Battle forecast: the launch screens run the deterministic resolver on what
// the player knows. For a pirate camp that is the exact garrison, so the
// forecast must match the arrival battle — research bonuses included.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class BattleForecastTests
    {
        static MapNode AddCamp(GameState state, int level)
        {
            var tile = new TileXY(state.HomeTile.X + 6, state.HomeTile.Y);
            if (MapLookup.NodeAt(state, tile) != null) tile = new TileXY(state.HomeTile.X + 7, state.HomeTile.Y + 1);
            var node = new MapNode
            {
                Id = "dyn-forecast", Kind = NodeKind.Camp, Tile = tile,
                Tier = 3, Amount = 0, RatePerSec = 0, Resource = null, CampLevel = level,
            };
            state.Map.DynamicNodes.Add(node);
            return node;
        }

        [Test]
        public void CampForecast_MatchesTheArrivalBattle_WithResearch()
        {
            var state = GameState.CreateNewGame(42);
            state.Resources.Helium = 100_000_000;
            state.Research[TechId.WeaponsCalibration] = 4; // +20% global attack
            state.Research[TechId.FighterDoctrine] = 5;    // +10% Fighter attack
            state.Research[TechId.CruiserBulkheads] = 5;   // +10% Cruiser durability
            var fleet = new Dictionary<HullId, int>
            {
                [HullId.Fighter] = 30, [HullId.Bomber] = 10, [HullId.Cruiser] = 6,
            };
            foreach (var kv in fleet) state.Ships[kv.Key] = kv.Value;
            var camp = AddCamp(state, 3);

            var forecast = BattleForecast.Predict(fleet, MarchSystem.CampGarrison(camp),
                ResearchSystem.CombatMods(state));

            var events = new SimEventBus();
            var engine = new TickEngine(state, events);
            Assert.IsTrue(MarchSystem.SendMarch(state, fleet, camp.Tile, MarchMission.Attack, out _).Ok);
            SaveManager.ApplyOfflineProgress(state, engine, events, 0, Balance.OfflineCapHours * 3600 * 1000L);

            var battle = state.Mailbox.OfType<BattleMailReport>().Single();
            Assert.AreEqual(battle.Report.Winner, forecast.Winner);
            Assert.AreEqual(battle.Report.Rounds.Count, forecast.Rounds);
            foreach (var hull in fleet.Keys)
            {
                int survived = battle.Report.AttackerSurvivors.TryGetValue(hull, out var s) ? s : 0;
                int predictedLost = forecast.YourLossesByHull.TryGetValue(hull, out var l) ? l : 0;
                Assert.AreEqual(fleet[hull] - survived, predictedLost, $"{hull} losses");
            }
            Assert.Greater(forecast.YourLosses, 0, "a real fight — the check isn't vacuous");
        }

        [Test]
        public void Forecast_ReadsUnopposedRaidsAndWipeouts()
        {
            var fleet = new Dictionary<HullId, int> { [HullId.Fighter] = 5 };

            var empty = BattleForecast.Predict(fleet, new Dictionary<HullId, int>(), FleetMods.None);
            Assert.IsTrue(empty.Unopposed);
            Assert.AreEqual(BattleWinner.Attacker, empty.Winner);
            Assert.AreEqual(0, empty.YourLosses);

            var wall = new Dictionary<HullId, int>
            {
                [HullId.Fighter] = 80, [HullId.Bomber] = 40, [HullId.Cruiser] = 20,
            };
            var wipe = BattleForecast.Predict(fleet, wall, FleetMods.None);
            Assert.AreEqual(BattleWinner.Defender, wipe.Winner);
            Assert.IsTrue(wipe.Wiped);
            Assert.AreEqual(5, wipe.YourLossesByHull[HullId.Fighter]);
        }
    }
}
