// The Frontier's buildings (2026-09-28): the Command Bastion's railguns and
// armoured docks, and the Salvage Yard's recovered wrecks.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class FrontierBuildingTests
    {
        static GameState Colony(int commandCenter, int bastion = 0, int salvage = 0)
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.Buildings[BuildingId.CommandCenter].Level = commandCenter;
            s.Buildings[BuildingId.CommandBastion].Level = bastion;
            s.Buildings[BuildingId.SalvageYard].Level = salvage;
            s.Resources = new ResourceBag(10_000_000, 10_000_000, 10_000_000).Milli();
            return s;
        }

        [Test]
        public void FrontierBuildings_OpenAtTheirCommandCenterLevel()
        {
            var s = Colony(5);
            var locked = BuildingSystem.CheckUpgrade(s, BuildingId.CommandBastion);
            Assert.IsFalse(locked.Ok);
            Assert.AreEqual("Unlocks at Command Center 6", locked.Reason);
            s.Buildings[BuildingId.CommandCenter].Level = 6;
            Assert.IsTrue(BuildingSystem.CheckUpgrade(s, BuildingId.CommandBastion).Ok);
            Assert.AreEqual("Unlocks at Command Center 8", BuildingSystem.CheckUpgrade(s, BuildingId.SalvageYard).Reason);
            s.Buildings[BuildingId.CommandCenter].Level = 8;
            Assert.IsTrue(BuildingSystem.CheckUpgrade(s, BuildingId.SalvageYard).Ok);
        }

        [Test]
        public void TheNextBestUpgrade_SticksToTheCoreNine()
        {
            var s = Colony(30);
            foreach (var id in Buildings.Core) s.Buildings[id].Level = 20;
            Assert.IsFalse(Buildings.IsFrontier(BuildingSystem.NextBestUpgrade(s)));
        }

        [Test]
        public void BastionRailguns_HitTheHeaviestHullFirst_AndHoldAnEmptyColony()
        {
            var home = Colony(10, bastion: 10);
            int railguns = Balance.BastionDamage(10);
            Assert.AreEqual(railguns, ResearchSystem.DefenseMods(home).TurretDamage);
            var raid = new Dictionary<HullId, int> { [HullId.Fighter] = 20, [HullId.Cruiser] = 2 };
            var report = CombatResolver.Resolve(raid, new Dictionary<HullId, int>(), default, ResearchSystem.DefenseMods(home));

            var first = report.Rounds[0].AttackerLosses;
            Assert.AreEqual(2, first.TryGetValue(HullId.Cruiser, out var c) ? c : 0, "round 1: the cruisers go first");
            Assert.IsFalse(first.ContainsKey(HullId.Fighter), "the fighters wait their turn");
            Assert.AreEqual(BattleWinner.Defender, report.Winner, "no fleet home, and the raid is still beaten off");
            Assert.AreEqual(railguns, report.DefenderTurret);
            Assert.AreEqual(0, CombatResolver.FleetCount(report.AttackerSurvivors));
        }

        [Test]
        public void WithoutABastion_AnEmptyColonyFalls()
        {
            var raid = new Dictionary<HullId, int> { [HullId.Fighter] = 20 };
            var report = CombatResolver.Resolve(raid, new Dictionary<HullId, int>(), default,
                ResearchSystem.DefenseMods(Colony(10)));
            Assert.AreEqual(BattleWinner.Attacker, report.Winner);
            Assert.AreEqual(0, report.DefenderTurret);
        }

        [Test]
        public void ArmouredDocks_ToughenDefendersAtHome_Only()
        {
            var plain = ResearchSystem.DefenseMods(Colony(10));
            var docks = ResearchSystem.DefenseMods(Colony(10, bastion: 10));
            Assert.AreEqual(plain.HpMult + 0.10f, docks.HpMult, 1e-4);
            Assert.AreEqual(plain.HpFor(HullId.Cruiser) + 0.10f, docks.HpFor(HullId.Cruiser), 1e-4);
            Assert.AreEqual(ResearchSystem.CombatMods(Colony(10)).HpMult,
                ResearchSystem.CombatMods(Colony(10, bastion: 10)).HpMult, 1e-4, "no bonus away from home");
        }

        [Test]
        public void Raiders_CountTheBastion_WhenSizingUpATarget()
        {
            long bare = BotSystem.EstimateDefensePower(Colony(10));
            long guarded = BotSystem.EstimateDefensePower(Colony(10, bastion: 10));
            Assert.AreEqual((long)Balance.BastionDamage(10) * Balance.BatteryOnlyRounds, guarded - bare);
        }

        static BattleMailReport Defense(int raidersLost, int cruisersLost)
        {
            var report = new BattleReport
            {
                Attacker = new Dictionary<HullId, int> { [HullId.Fighter] = 10 },
                AttackerSurvivors = new Dictionary<HullId, int> { [HullId.Fighter] = 10 - raidersLost },
                Defender = new Dictionary<HullId, int> { [HullId.Cruiser] = 2 },
                DefenderSurvivors = new Dictionary<HullId, int> { [HullId.Cruiser] = 2 - cruisersLost },
                Winner = BattleWinner.Defender,
            };
            return new BattleMailReport { Id = 1, Report = report, Defending = true };
        }

        [Test]
        public void SalvageYard_RecoversOwnLosses_AndRaidersDownedOverTheColony()
        {
            var s = Colony(8, salvage: 5);
            float rate = Balance.SalvageRate(5);
            BotSystem.InsertMail(s, Defense(raidersLost: 10, cruisersLost: 1));
            // 1 cruiser (200/80/40) of your own + 10 raiding fighters (30/10/0 each).
            var value = new ResourceBag(200 + 300, 80 + 100, 40).Milli().Scaled(rate);
            Assert.AreEqual(value.Gold, s.SalvageStored.Gold);
            Assert.AreEqual(value.Quartz, s.SalvageStored.Quartz);
            Assert.AreEqual(value.Helium, s.SalvageStored.Helium);
        }

        [Test]
        public void SalvageYard_OnlyCountsYourShare_OfAJointFight()
        {
            var s = Colony(8, salvage: 5);
            var report = new BattleReport
            {
                Attacker = new Dictionary<HullId, int> { [HullId.Cruiser] = 4 },
                AttackerSurvivors = new Dictionary<HullId, int>(),
                Defender = new Dictionary<HullId, int> { [HullId.Fighter] = 50 },
                DefenderSurvivors = new Dictionary<HullId, int> { [HullId.Fighter] = 50 },
                Winner = BattleWinner.Defender,
            };
            var mail = new BattleMailReport { Report = report, AllyShips = new() { [HullId.Cruiser] = 2 } };
            var gain = SalvageSystem.FromReport(s, mail);
            Assert.AreEqual(new ResourceBag(400, 160, 80).Milli().Scaled(Balance.SalvageRate(5)).Gold, gain.Gold,
                "two of the four cruisers were yours");
        }

        [Test]
        public void SalvageYard_FillsToItsCapacity_ThenCollectEmptiesIt()
        {
            var s = Colony(8, salvage: 1);
            long cap = SalvageSystem.CapacityMilli(s);
            var added = SalvageSystem.Store(s, new ResourceBag(cap * 2, 5_000, 0));
            Assert.AreEqual(cap, added.Gold);
            Assert.AreEqual(cap, s.SalvageStored.Gold);
            Assert.IsTrue(SalvageSystem.Full(s));

            long before = s.Resources.Gold;
            Assert.IsTrue(SalvageSystem.Collect(s, out var got).Ok);
            Assert.AreEqual(cap, got.Gold);
            Assert.AreEqual(before + cap, s.Resources.Gold);
            Assert.AreEqual(0, s.SalvageStored.Total);
            Assert.IsFalse(SalvageSystem.Collect(s, out _).Ok, "nothing left to collect");
        }

        [Test]
        public void WithoutASalvageYard_WrecksAreLost()
        {
            var s = Colony(8);
            var mail = Defense(raidersLost: 10, cruisersLost: 2);
            BotSystem.InsertMail(s, mail);
            Assert.AreEqual(0, s.SalvageStored.Total);
            Assert.IsNull(mail.Salvaged, "the report has no salvage line");
        }

        [Test]
        public void TheReport_RemembersWhatTheYardRecovered()
        {
            var s = Colony(8, salvage: 5);
            var mail = Defense(raidersLost: 10, cruisersLost: 1);
            BotSystem.InsertMail(s, mail);
            Assert.IsNotNull(mail.Salvaged);
            Assert.AreEqual(s.SalvageStored.Gold, mail.Salvaged!.Gold);
            Assert.AreEqual(s.SalvageStored.Helium, mail.Salvaged.Helium);

            var back = (BattleMailReport)SaveCodec.DecodeState(SaveCodec.EncodeState(s)).Mailbox[0];
            Assert.AreEqual(mail.Salvaged.Gold, back.Salvaged!.Gold);
            Assert.AreEqual(mail.Salvaged.Quartz, back.Salvaged.Quartz);
        }

        [Test]
        public void AFullYard_NotesOnlyWhatFitted()
        {
            var s = Colony(8, salvage: 1);
            long cap = SalvageSystem.CapacityMilli(s);
            s.SalvageStored = new ResourceBag(cap, cap, cap);
            var mail = Defense(raidersLost: 10, cruisersLost: 1);
            BotSystem.InsertMail(s, mail);
            Assert.IsNull(mail.Salvaged, "nothing fitted, so nothing to report");
        }

        [Test]
        public void TheNewBuildings_AndTheYardsContents_SurviveASave()
        {
            var s = Colony(8, bastion: 4, salvage: 3);
            s.SalvageStored = new ResourceBag(1_234_000, 567_000, 89_000);
            s.Mailbox.Add(new BattleMailReport { Id = 7, Report = new BattleReport { DefenderTurret = 812 } });
            var back = SaveCodec.DecodeState(SaveCodec.EncodeState(s));
            Assert.AreEqual(4, back.Buildings[BuildingId.CommandBastion].Level);
            Assert.AreEqual(3, back.Buildings[BuildingId.SalvageYard].Level);
            Assert.AreEqual(1_234_000, back.SalvageStored.Gold);
            Assert.AreEqual(89_000, back.SalvageStored.Helium);
            Assert.AreEqual(812, ((BattleMailReport)back.Mailbox[0]).Report.DefenderTurret);
        }

        [Test]
        public void AnOldSave_LoadsWithTheFrontierUnbuilt()
        {
            var s = Colony(8, bastion: 4, salvage: 3);
            var json = SaveCodec.EncodeState(s);
            var buildings = (Dictionary<string, object?>)json["buildings"]!;
            buildings.Remove("commandBastion");
            buildings.Remove("salvageYard");
            var back = SaveCodec.DecodeState(json);
            Assert.AreEqual(0, back.Buildings[BuildingId.CommandBastion].Level);
            Assert.AreEqual(0, back.Buildings[BuildingId.SalvageYard].Level);
        }
    }
}
