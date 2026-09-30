// The Frontier completed (2026-09-29): the Repair Dock, the Jump Gate, the Clan
// Embassy and the Deep Space Observatory filled the reserved pads.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class FrontierCompleteTests
    {
        static GameState Colony()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.Buildings[BuildingId.CommandCenter].Level = 30;
            s.Resources = new ResourceBag(10_000_000, 10_000_000, 10_000_000).Milli();
            s.Tick = 1000;
            return s;
        }

        static BattleMailReport HomeDefence(int cruisersLost) => new()
        {
            Id = 1,
            Defending = true,
            GuardedBotId = 0,
            Report = new BattleReport
            {
                Attacker = new Dictionary<HullId, int> { [HullId.Fighter] = 10 },
                AttackerSurvivors = new Dictionary<HullId, int> { [HullId.Fighter] = 4 },
                Defender = new Dictionary<HullId, int> { [HullId.Cruiser] = 40 },
                DefenderSurvivors = new Dictionary<HullId, int> { [HullId.Cruiser] = 40 - cruisersLost },
                Winner = BattleWinner.Defender,
            },
        };

        [Test]
        public void EveryFrontierPad_HasABuilding_OpeningAtItsCommandCenterLevel()
        {
            var s = Colony();
            foreach (var (id, cc) in new[] { (BuildingId.JumpGate, 7), (BuildingId.ClanEmbassy, 7),
                         (BuildingId.RepairDock, 9), (BuildingId.Observatory, 11) })
            {
                s.Buildings[BuildingId.CommandCenter].Level = cc - 1;
                Assert.AreEqual($"Unlocks at Command Center {cc}", BuildingSystem.CheckUpgrade(s, id).Reason, id.ToString());
                s.Buildings[BuildingId.CommandCenter].Level = cc;
                Assert.IsTrue(BuildingSystem.CheckUpgrade(s, id).Ok, id.ToString());
                Assert.IsFalse(Buildings.Defs[id].BotsBuild, "the simulated commanders don't build it");
            }
            Assert.IsFalse(BaseLayout.Pads.Any(p => p.Kind == PadKind.Reserved), "nothing reserved or coming soon");
        }

        // ---------- Repair Dock ----------

        [Test]
        public void TheRepairDock_TowsHomeAShareOfTheShipsLostDefendingHome()
        {
            var s = Colony();
            var mail = HomeDefence(30);
            RepairSystem.OnMail(s, mail);
            Assert.IsEmpty(s.DamagedHulls, "no dock, no tugs");

            s.Buildings[BuildingId.RepairDock].Level = 30;
            RepairSystem.OnMail(s, mail);
            Assert.AreEqual(15, RepairSystem.Waiting(s)[HullId.Cruiser], "half of 30 at level 30");
            Assert.AreEqual(15, mail.Towed![HullId.Cruiser], "the report says so");

            var away = HomeDefence(30);
            away.GuardedBotId = 12; // guarding a clanmate, not home
            RepairSystem.OnMail(s, away);
            Assert.AreEqual(15, RepairSystem.Waiting(s)[HullId.Cruiser], "only defences of your own colony");
        }

        [Test]
        public void Repairs_CostAFractionOfTheShips_AndPutThemBackInTheDock()
        {
            var s = Colony();
            s.Buildings[BuildingId.RepairDock].Level = 30;
            s.Ships[HullId.Cruiser] = 0;
            RepairSystem.OnMail(s, HomeDefence(20));
            var waiting = RepairSystem.Waiting(s);
            var full = SalvageSystem.ValueOf(waiting);
            var gold = s.Resources.Gold;

            Assert.IsTrue(RepairSystem.StartRepair(s).Ok);
            Assert.AreEqual(gold - (long)(full.Gold * RepairSystem.CostShare), s.Resources.Gold, 1000);
            Assert.IsEmpty(s.DamagedHulls);
            Assert.IsFalse(RepairSystem.StartRepair(s).Ok, "one repair at a time");

            s.Tick = s.Repair!.EndsAtTick;
            RepairSystem.Tick(s, new SimEventBus());
            Assert.AreEqual(10, s.Ships[HullId.Cruiser]);
            Assert.IsNull(s.Repair);
        }

        [Test]
        public void DamagedHulls_AreScrappedAfter72Hours()
        {
            var s = Colony();
            s.Buildings[BuildingId.RepairDock].Level = 30;
            RepairSystem.OnMail(s, HomeDefence(20));
            s.Tick += RepairSystem.KeepSec;
            Assert.IsEmpty(RepairSystem.Waiting(s));
            Assert.IsFalse(RepairSystem.StartRepair(s).Ok);
        }

        // ---------- Jump Gate ----------

        [Test]
        public void TheJumpGate_SpeedsFlightsAndCutsTheirHelium()
        {
            var s = Colony();
            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 10 };
            s.Ships[HullId.Cruiser] = 10;
            var target = new TileXY(s.HomeTile.X + 200, s.HomeTile.Y);
            double speed = MarchSystem.EffSpeed(s, fleet);
            int helium = MarchSystem.PreviewMarch(s, fleet, target).HeliumCost;

            s.Buildings[BuildingId.JumpGate].Level = 20;
            Assert.AreEqual(speed * 1.2, MarchSystem.EffSpeed(s, fleet), 1e-9);
            Assert.AreEqual(helium * 0.8, MarchSystem.PreviewMarch(s, fleet, target).HeliumCost, 2);
        }

        [Test]
        public void TheFreeJump_ReloadsEvery24Hours_AndGoesAnywhereFromLevel10()
        {
            var s = Colony();
            Assert.IsFalse(JumpGateSystem.JumpRandom(s).Ok, "no gate");
            s.Buildings[BuildingId.JumpGate].Level = 1;
            var home = s.HomeTile;
            Assert.IsTrue(JumpGateSystem.JumpRandom(s).Ok);
            Assert.AreNotEqual(home, s.HomeTile);
            Assert.AreEqual(24 * 3600, JumpGateSystem.ReloadLeft(s));
            Assert.IsFalse(JumpGateSystem.JumpRandom(s).Ok, "still reloading");

            s.Tick += JumpGateSystem.ReloadSec;
            var spot = new TileXY(300, 1250);
            Assert.IsFalse(JumpGateSystem.JumpTo(s, spot).Ok, "precision jumps wait for level 10");
            s.Buildings[BuildingId.JumpGate].Level = 10;
            Assert.IsTrue(JumpGateSystem.JumpTo(s, spot).Ok);
            Assert.AreEqual(spot, s.HomeTile);
            Assert.IsFalse(JumpGateSystem.Ready(s));
        }

        // ---------- Clan Embassy ----------

        [Test]
        public void TheClanEmbassy_AddsWingsSupplyRunsAndTribute()
        {
            var s = Colony();
            Assert.AreEqual(3, EmbassySystem.StrikeWings(s));
            Assert.AreEqual(24 * 3600, EmbassySystem.SupplyIntervalSec(s));
            Assert.AreEqual(0.04, EmbassySystem.ClanTributeShare(s), 1e-9);

            s.Buildings[BuildingId.ClanEmbassy].Level = 30;
            Assert.AreEqual(6, EmbassySystem.StrikeWings(s));
            Assert.AreEqual(4, EmbassySystem.SupplyRunsPerDay(s));
            Assert.AreEqual(6 * 3600, EmbassySystem.SupplyIntervalSec(s));
            Assert.AreEqual(0.09, EmbassySystem.ClanTributeShare(s), 1e-9);
        }

        // ---------- Deep Space Observatory ----------

        [Test]
        public void TheObservatory_SpeedsSurveysAndStretchesTheRadar()
        {
            var s = Colony();
            int survey = WildsSystem.SurveySeconds(s, 0);
            s.Buildings[BuildingId.RadarStation].Level = 5;
            int lead = RadarSystem.WarnLeadSeconds(s);
            s.Buildings[BuildingId.Observatory].Level = 20;
            Assert.AreEqual((int)System.Math.Round(survey * 0.7), WildsSystem.SurveySeconds(s, 0));
            Assert.AreEqual((int)System.Math.Round(lead * 1.6), RadarSystem.WarnLeadSeconds(s));
        }

        [Test]
        public void TheObservatory_ForecastsWhereTheNextDreadnoughtDrops()
        {
            var s = Colony();
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 20);
            var bus = new SimEventBus();
            BossSystem.Tick(s, galaxy, bus); // schedules the first visit
            s.Buildings[BuildingId.Observatory].Level = 9;
            Assert.IsNull(ObservatorySystem.Forecast(s, galaxy), "from level 10");
            s.Buildings[BuildingId.Observatory].Level = 10;
            var forecast = ObservatorySystem.Forecast(s, galaxy)!.Value;
            Assert.AreEqual(galaxy.Boss.NextVisitTick, forecast.atTick);

            s.Tick = forecast.atTick;
            BossSystem.Tick(s, galaxy, bus);
            Assert.IsTrue(galaxy.Boss.Active);
            Assert.AreEqual(forecast.tile, galaxy.Boss.Tile, "it dropped exactly where the observatory said");
            Assert.IsNull(ObservatorySystem.Forecast(s, galaxy), "no forecast while it's here");
        }

        // ---------- saves ----------

        [Test]
        public void TheNewBuildings_AndTheirState_SurviveASave()
        {
            var s = Colony();
            s.Buildings[BuildingId.RepairDock].Level = 12;
            s.Buildings[BuildingId.JumpGate].Level = 11;
            s.Buildings[BuildingId.ClanEmbassy].Level = 7;
            s.Buildings[BuildingId.Observatory].Level = 4;
            s.DamagedHulls.Add(new DamagedBatch { Ships = new() { [HullId.Cruiser] = 5 }, ExpiresAtTick = 9999 });
            s.Repair = new RepairJob { Ships = new() { [HullId.Fighter] = 3 }, EndsAtTick = 4444 };
            s.JumpGateReadyTick = 7777;
            var mail = HomeDefence(4);
            mail.Towed = new Dictionary<HullId, int> { [HullId.Cruiser] = 2 };
            s.Mailbox.Add(mail);

            var back = SaveCodec.DecodeState(SaveCodec.EncodeState(s));
            Assert.AreEqual(12, back.Buildings[BuildingId.RepairDock].Level);
            Assert.AreEqual(11, back.Buildings[BuildingId.JumpGate].Level);
            Assert.AreEqual(7, back.Buildings[BuildingId.ClanEmbassy].Level);
            Assert.AreEqual(4, back.Buildings[BuildingId.Observatory].Level);
            Assert.AreEqual(5, back.DamagedHulls.Single().Ships[HullId.Cruiser]);
            Assert.AreEqual(9999, back.DamagedHulls[0].ExpiresAtTick);
            Assert.AreEqual(3, back.Repair!.Ships[HullId.Fighter]);
            Assert.AreEqual(4444, back.Repair.EndsAtTick);
            Assert.AreEqual(7777, back.JumpGateReadyTick);
            Assert.AreEqual(2, ((BattleMailReport)back.Mailbox[0]).Towed![HullId.Cruiser]);
        }
    }
}
