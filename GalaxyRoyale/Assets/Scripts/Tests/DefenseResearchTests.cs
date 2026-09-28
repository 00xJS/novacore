// Defense research (user request 2026-09-27): military research now counts for
// defenders too, the Defense branch counts only at home, and Orbital Batteries
// fire on raiders every round — even with no fleet docked.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class DefenseResearchTests
    {
        static Dictionary<HullId, int> Fleet() => new()
        {
            [HullId.Fighter] = 60, [HullId.Bomber] = 25, [HullId.Cruiser] = 12,
        };

        static int Count(Dictionary<HullId, int> fleet) => CombatResolver.FleetCount(fleet);

        [Test]
        public void MilitaryResearch_NowHelpsTheDefender()
        {
            var home = GameState.CreateNewGame(42);
            home.Research[TechId.WeaponsCalibration] = 10; // +50% attack
            home.Research[TechId.ArmorPlating] = 10;       // +50% durability
            var plain = CombatResolver.Resolve(Fleet(), Fleet());
            var researched = CombatResolver.Resolve(Fleet(), Fleet(), default, ResearchSystem.DefenseMods(home));
            Assert.Greater(Count(researched.DefenderSurvivors), Count(plain.DefenderSurvivors));
            Assert.LessOrEqual(Count(researched.AttackerSurvivors), Count(plain.AttackerSurvivors));
        }

        [Test]
        public void DefenseBranch_CountsAtHomeOnly()
        {
            var state = GameState.CreateNewGame(42);
            state.Research[TechId.BastionHangars] = 10;
            state.Research[TechId.PointDefenseGrid] = 10;
            state.Research[TechId.PlanetaryDeflectors] = 10;
            state.Research[TechId.OrbitalBatteries] = 4;

            var attack = ResearchSystem.CombatMods(state);
            Assert.AreEqual(1f, attack.AtkFor(HullId.Fighter), 1e-4, "no home bonus when attacking");
            Assert.AreEqual(1f, attack.HpFor(HullId.Cruiser), 1e-4);
            Assert.AreEqual(0, attack.BatteryLevel, "the guns stay on the planet");

            var defense = ResearchSystem.DefenseMods(state);
            Assert.AreEqual(1.6f, defense.AtkFor(HullId.Fighter), 1e-4);
            Assert.AreEqual(1.6f, defense.HpFor(HullId.Cruiser), 1e-4);
            Assert.AreEqual(1.8f, defense.ShieldFor(), 1e-4);
            Assert.AreEqual(4, defense.BatteryLevel);
        }

        [Test]
        public void OrbitalBatteries_HitRaiders_WithNoFleetHome()
        {
            var raid = new Dictionary<HullId, int> { [HullId.Fighter] = 30 };
            // Level 2 = 500 per round, straight through shields: 30 × 60 HP = 1800 → 300 left.
            var report = CombatResolver.Resolve(raid, new Dictionary<HullId, int>(), default,
                new FleetMods(batteryLevel: 2));
            Assert.AreEqual(Balance.BatteryOnlyRounds, report.Rounds.Count, "the guns get their rounds");
            Assert.AreEqual(5, Count(report.AttackerSurvivors));
            Assert.AreEqual(BattleWinner.Attacker, report.Winner, "a raid that outlasts the barrage still lands");
            Assert.AreEqual(2, report.DefenderBattery);
        }

        [Test]
        public void OrbitalBatteries_CanRepelASmallRaidAlone()
        {
            var raid = new Dictionary<HullId, int> { [HullId.Fighter] = 8 };
            var report = CombatResolver.Resolve(raid, new Dictionary<HullId, int>(), default,
                new FleetMods(batteryLevel: 3));
            Assert.AreEqual(0, Count(report.AttackerSurvivors));
            Assert.AreEqual(BattleWinner.Defender, report.Winner, "wiped by the guns = repelled");
        }

        [Test]
        public void NoResearch_BattlesAreUnchanged()
        {
            var before = CombatResolver.Resolve(Fleet(), Fleet());
            var after = CombatResolver.Resolve(Fleet(), Fleet(), FleetMods.None, FleetMods.None);
            Assert.AreEqual(before.Winner, after.Winner);
            Assert.AreEqual(before.Rounds.Count, after.Rounds.Count);
            Assert.AreEqual(Count(before.AttackerSurvivors), Count(after.AttackerSurvivors));
            Assert.AreEqual(Count(before.DefenderSurvivors), Count(after.DefenderSurvivors));
            Assert.AreEqual(0, after.DefenderBattery);
        }

        [Test]
        public void Forecast_SeesTheDefendersResearch()
        {
            var rival = GameState.CreateNewGame(7);
            rival.Research[TechId.PointDefenseGrid] = 6;
            rival.Research[TechId.OrbitalBatteries] = 2;
            var defMods = ResearchSystem.DefenseMods(rival);
            var forecast = BattleForecast.Predict(Fleet(), Fleet(), FleetMods.None, defMods);
            var real = CombatResolver.Resolve(Fleet(), Fleet(), FleetMods.None, defMods);
            Assert.AreEqual(real.Winner, forecast.Winner);
            Assert.AreEqual(real.Rounds.Count, forecast.Rounds);
            Assert.AreEqual(Count(Fleet()) - Count(real.AttackerSurvivors), forecast.YourLosses);
            Assert.AreEqual(2, forecast.Battery);
        }

        [Test]
        public void BatteryLevel_SurvivesTheSave()
        {
            var state = GameState.CreateNewGame(42);
            state.Mailbox.Clear();
            state.Mailbox.Add(new BattleMailReport
            {
                Id = 1,
                Subject = "Raid repelled — Moon Moon",
                Defending = true,
                Report = new BattleReport { Winner = BattleWinner.Defender, DefenderBattery = 3 },
            });
            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000))).State;
            Assert.AreEqual(3, ((BattleMailReport)back.Mailbox[0]).Report.DefenderBattery);
        }

        [Test]
        public void EveryTech_SurvivesTheSave()
        {
            // The codec maps each tech to a saved name — a tech missing from the
            // table crashed the save the first time a bot researched it.
            var state = GameState.CreateNewGame(42);
            foreach (var id in Techs.All) state.Research[id] = 1;
            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000))).State;
            foreach (var id in Techs.All) Assert.AreEqual(1, back.Research[id], id.ToString());
        }

        [Test]
        public void Bots_CountDefensesBeforeRaiding()
        {
            var colony = GameState.CreateNewGame(42);
            colony.Ships[HullId.Fighter] = 20;
            long bare = BotSystem.EstimateDefensePower(colony);
            Assert.AreEqual(BotSystem.EstimateFleetPower(colony.Ships), bare, "no research = the old estimate");
            colony.Research[TechId.OrbitalBatteries] = 2;
            colony.Research[TechId.BastionHangars] = 5;
            Assert.Greater(BotSystem.EstimateDefensePower(colony), bare, "bots see the guns and the hangars");
        }
    }
}
