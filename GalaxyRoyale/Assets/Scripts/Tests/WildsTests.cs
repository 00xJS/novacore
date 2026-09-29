// The Wilds (2026-09-29): the sectors, surveys, the harvester drones, and the
// refills and fog that keep them from running out.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class WildsTests
    {
        static GameState Colony(int commandCenter = 5, int droneFactory = 0)
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.Buildings[BuildingId.CommandCenter].Level = commandCenter;
            s.Buildings[BuildingId.DroneFactory].Level = droneFactory;
            s.Resources = new ResourceBag(10_000_000, 10_000_000, 10_000_000).Milli();
            return s;
        }

        /// <summary>Only the Wilds, a second at a time (production and the rest stay out of the sums).</summary>
        static List<SimEvent> Run(GameState s, int seconds)
        {
            var bus = new SimEventBus();
            var seen = new List<SimEvent>();
            bus.Subscribe(seen.Add);
            for (int i = 0; i < seconds; i++)
            {
                s.Tick++;
                WildsSystem.Tick(s, bus);
            }
            return seen;
        }

        static WildsSector Deposit(GameState s, int index, ResourceId resource, long stockMilli)
        {
            var sector = new WildsSector
            {
                Index = index, Find = WildsFind.Deposit, Resource = resource, StockMilli = stockMilli, MaxMilli = stockMilli,
                Surveys = 1,
            };
            s.Wilds.Sectors[index] = sector;
            return sector;
        }

        [Test]
        public void FiftyEightSectors_InFiveRings_SouthToThePole()
        {
            Assert.AreEqual(58, WildsLayout.Total);
            Assert.AreEqual(new[] { 18, 16, 12, 8, 4 }, WildsLayout.Rings.Select(r => r.Count).ToArray());
            var (lat, lon, width) = WildsLayout.Place(0);
            Assert.AreEqual(-13, lat);
            Assert.AreEqual(-170, lon, 1e-9);
            Assert.AreEqual(20, width, 1e-9);
            Assert.AreEqual(4, WildsLayout.RingOf(57));
            Assert.AreEqual(-73, WildsLayout.Place(57).Lat);
            Assert.AreEqual("A-01", WildsLayout.Name(0));
            Assert.AreEqual("B-01", WildsLayout.Name(18));
            Assert.AreEqual("E-04", WildsLayout.Name(57));
        }

        [Test]
        public void EverySectorSouthOfTheFirstRing_TouchesTheRingAbove()
        {
            for (int i = 18; i < WildsLayout.Total; i++)
            {
                var up = WildsLayout.Neighbours(i);
                Assert.IsNotEmpty(up, WildsLayout.Name(i));
                Assert.IsTrue(up.All(j => WildsLayout.RingOf(j) == WildsLayout.RingOf(i) - 1));
            }
            Assert.IsEmpty(WildsLayout.Neighbours(0), "the first ring borders the colony itself");
        }

        [Test]
        public void Surveys_StartOnTheFirstRing_AndSpreadSouthFromWhatsCharted()
        {
            var s = Colony();
            int deep = WildsLayout.Index(1, 0);
            Assert.AreEqual("Chart a sector next to it first", WildsSystem.CheckSurvey(s, deep).Reason);
            int above = WildsLayout.Neighbours(deep)[0];
            Assert.IsTrue(WildsSystem.CheckSurvey(s, above).Ok);

            long gold = s.Resources.Gold;
            Assert.IsTrue(WildsSystem.StartSurvey(s, above).Ok);
            Assert.AreEqual(gold - Balance.WildsSurveyCost(0).Gold * 1000, s.Resources.Gold);
            Assert.AreEqual("A survey is already under way", WildsSystem.CheckSurvey(s, WildsLayout.Index(0, 5)).Reason);
            Assert.AreEqual(Balance.WildsSurveySec[0], WildsSystem.SurveyLeft(s));

            var events = Run(s, Balance.WildsSurveySec[0]);
            Assert.IsTrue(WildsSystem.Charted(s, above));
            Assert.AreEqual(-1, s.Wilds.Surveying);
            var landed = events.OfType<WildsSurveyed>().Single();
            Assert.AreEqual(above, landed.Sector);
            Assert.AreEqual(s.Wilds.Sectors[above].Find, landed.Find);
            Assert.IsTrue(WildsSystem.Reachable(s, deep), "charted land opens the ring below");
            Assert.AreEqual("Already charted", WildsSystem.CheckSurvey(s, above).Reason);
        }

        [Test]
        public void DeeperRings_CostMore_AndTakeLonger()
        {
            for (int ring = 1; ring < WildsLayout.Rings.Count; ring++)
            {
                Assert.Greater(Balance.WildsSurveyCost(ring).Total, Balance.WildsSurveyCost(ring - 1).Total);
                Assert.Greater(Balance.WildsSurveySec[ring], Balance.WildsSurveySec[ring - 1]);
                Assert.Greater(Balance.WildsDepositSize(ring), Balance.WildsDepositSize(ring - 1));
            }
        }

        [Test]
        public void WhatASurveyFinds_IsSeeded_AndMostlyDeposits()
        {
            var a = Colony();
            var b = Colony();
            int deposits = 0, caches = 0, relics = 0;
            for (int i = 0; i < WildsLayout.Total; i++)
            {
                var find = WildsSystem.Reveal(a, i);
                Assert.AreEqual(find, WildsSystem.Reveal(b, i), "same galaxy, same sector, same find");
                Assert.AreEqual(a.Wilds.Sectors[i].MaxMilli, b.Wilds.Sectors[i].MaxMilli);
                if (find == WildsFind.Deposit) deposits++;
                else if (find == WildsFind.Cache) caches++;
                else relics++;
                if (find == WildsFind.Deposit)
                {
                    long full = Balance.WildsDepositSize(WildsLayout.RingOf(i)) * 1000;
                    Assert.That(a.Wilds.Sectors[i].MaxMilli, Is.InRange(full * 7 / 10, full * 13 / 10));
                }
            }
            Assert.Greater(deposits, caches + relics, "deposits are the common find");
            Assert.Greater(caches + relics, 0);
            Assert.AreEqual(WildsLayout.Total, deposits + caches + relics);
        }

        [Test]
        public void TheColonysOwnDrone_HarvestsFromTheStart_TheFactoryAddsMore()
        {
            Assert.AreEqual(1, Balance.WildsDrones(0));
            Assert.AreEqual(2, Balance.WildsDrones(1));
            Assert.AreEqual(5, Balance.WildsDrones(10));
            Assert.Greater(WildsSystem.HaulPerHourMilli(Colony(droneFactory: 10)), 5 * WildsSystem.HaulPerHourMilli(Colony()));

            var s = Colony();
            var field = Deposit(s, 3, ResourceId.Quartz, 1_000_000_000);
            Run(s, 1); // the drones start their rounds
            long quartz = s.Resources.Quartz;
            Run(s, 3600);
            long hour = WildsSystem.HaulPerHourMilli(s);
            Assert.AreEqual(720_000, hour, "one drone, 120 a trip, six trips an hour");
            Assert.AreEqual(quartz + hour, s.Resources.Quartz);
            Assert.AreEqual(1_000_000_000 - hour, field.StockMilli);
            Assert.AreEqual(hour, s.Wilds.Harvested.Quartz);
        }

        [Test]
        public void TheHaul_IsSharedAcrossTheDeposits()
        {
            var s = Colony(commandCenter: 10, droneFactory: 4);
            var gold = Deposit(s, 1, ResourceId.Gold, 1_000_000_000);
            var helium = Deposit(s, 2, ResourceId.Helium, 1_000_000_000);
            Run(s, 1);
            Run(s, 3600);
            long hour = WildsSystem.HaulPerHourMilli(s);
            Assert.AreEqual(1_000_000_000 - hour / 2, gold.StockMilli, 60);
            Assert.AreEqual(gold.StockMilli, helium.StockMilli);
        }

        [Test]
        public void ADrainedDeposit_RefillsLater()
        {
            var s = Colony();
            var seam = Deposit(s, 4, ResourceId.Gold, 5_000);
            Run(s, 1);
            Run(s, 60);
            Assert.AreEqual(0, seam.StockMilli, "a small seam is emptied in one run");
            Assert.AreEqual(5_000, seam.HarvestedMilli);
            Assert.Greater(seam.RefillTick, s.Tick);
            Assert.AreEqual(0, WildsSystem.ActiveDeposits(s));
            Run(s, Balance.WildsRefillSec - 60);
            Assert.AreEqual(0, seam.StockMilli, "still refilling");
            Run(s, 60);
            Assert.AreEqual(5_000, seam.StockMilli, "full again after the wait");
            Assert.AreEqual(0, seam.RefillTick);
            Assert.AreEqual(1, WildsSystem.ActiveDeposits(s));
        }

        [Test]
        public void CachesAndRelics_AreClaimed_ThenTheFogReturns_ForSomethingNew()
        {
            var s = Colony();
            var cache = new WildsSector
            {
                Index = 6, Find = WildsFind.Cache, Reward = new ResourceBag(4_000_000, 3_000_000, 1_000_000), Surveys = 1,
            };
            s.Wilds.Sectors[6] = cache;
            var relic = new WildsSector { Index = 7, Find = WildsFind.Relic, RewardDM = 30, Surveys = 1 };
            s.Wilds.Sectors[7] = relic;

            long gold = s.Resources.Gold;
            int dm = s.Premium.DarkMatter;
            Assert.IsTrue(WildsSystem.Claim(s, 6).Ok);
            Assert.IsTrue(WildsSystem.Claim(s, 7).Ok);
            Assert.AreEqual(gold + 4_000_000, s.Resources.Gold);
            Assert.AreEqual(dm + 30, s.Premium.DarkMatter);
            Assert.AreEqual("Already claimed", WildsSystem.Claim(s, 6).Reason);
            Assert.AreEqual("Nothing to claim here", WildsSystem.Claim(s, 9).Reason);

            Run(s, 1);
            Run(s, Balance.WildsShiftSec + 60);
            Assert.IsFalse(WildsSystem.Charted(s, 6), "the Wilds shift: back under the fog");
            Assert.IsFalse(WildsSystem.Charted(s, 7));
            Assert.IsTrue(WildsSystem.CheckSurvey(s, 6).Ok, "ready to be surveyed again");
            WildsSystem.Reveal(s, 6);
            Assert.AreEqual(2, s.Wilds.Sectors[6].Surveys, "a second survey rolls something new");
        }

        [Test]
        public void SurveyNext_PicksTheNearestReachableSector()
        {
            var s = Colony();
            int first = WildsSystem.NextSurvey(s, -106);
            Assert.AreEqual(0, WildsLayout.RingOf(first));
            Assert.Less(System.Math.Abs(WildsLayout.DeltaLon(-106, WildsLayout.Place(first).Lon)), 10.01);
            for (int i = 0; i < 18; i++) WildsSystem.Reveal(s, i);
            int next = WildsSystem.NextSurvey(s, 0);
            Assert.AreEqual(1, WildsLayout.RingOf(next), "the first ring is all charted: on to the second");
        }

        [Test]
        public void SpeedUps_ShortenTheSurvey()
        {
            var s = Colony();
            Assert.IsTrue(WildsSystem.StartSurvey(s, 0).Ok);
            WildsSystem.SpeedUpSurvey(s, 200);
            Assert.AreEqual(Balance.WildsSurveySec[0] - 200, WildsSystem.SurveyLeft(s));
            WildsSystem.SpeedUpSurvey(s, int.MaxValue);
            Assert.AreEqual(0, WildsSystem.SurveyLeft(s));
            Run(s, 1);
            Assert.IsTrue(WildsSystem.Charted(s, 0));
        }

        [Test]
        public void TheDroneFactory_OpensAtCommandCenterTen()
        {
            var s = Colony(commandCenter: 9);
            Assert.AreEqual("Unlocks at Command Center 10", BuildingSystem.CheckUpgrade(s, BuildingId.DroneFactory).Reason);
            s.Buildings[BuildingId.CommandCenter].Level = 10;
            Assert.IsTrue(BuildingSystem.CheckUpgrade(s, BuildingId.DroneFactory).Ok);
        }

        [Test]
        public void TheWilds_SurviveASave()
        {
            var s = Colony(commandCenter: 10, droneFactory: 3);
            Deposit(s, 2, ResourceId.Helium, 7_500_000).RefillTick = 0;
            var drained = Deposit(s, 5, ResourceId.Gold, 0);
            drained.MaxMilli = 9_000_000;
            drained.RefillTick = 12_345;
            s.Wilds.Sectors[7] = new WildsSector { Index = 7, Find = WildsFind.Relic, RewardDM = 45, Claimed = true, FogTick = 99_999, Surveys = 2 };
            s.Wilds.Sectors[8] = new WildsSector { Index = 8, Find = WildsFind.None, Surveys = 3 };
            Assert.IsTrue(WildsSystem.StartSurvey(s, 0).Ok);
            s.Wilds.HarvestTick = 777;
            s.Wilds.Harvested = new ResourceBag(1_000, 2_000, 3_000);

            var back = SaveCodec.DecodeState(SaveCodec.EncodeState(s)).Wilds;
            Assert.AreEqual(0, back.Surveying);
            Assert.AreEqual(s.Wilds.SurveyDoneTick, back.SurveyDoneTick);
            Assert.AreEqual(777, back.HarvestTick);
            Assert.AreEqual(3_000, back.Harvested.Helium);
            Assert.AreEqual(ResourceId.Helium, back.Sectors[2].Resource);
            Assert.AreEqual(7_500_000, back.Sectors[2].StockMilli);
            Assert.AreEqual(12_345, back.Sectors[5].RefillTick);
            Assert.AreEqual(9_000_000, back.Sectors[5].MaxMilli);
            Assert.AreEqual(WildsFind.Relic, back.Sectors[7].Find);
            Assert.IsTrue(back.Sectors[7].Claimed);
            Assert.AreEqual(99_999, back.Sectors[7].FogTick);
            Assert.AreEqual(45, back.Sectors[7].RewardDM);
            Assert.AreEqual(WildsFind.None, back.Sectors[8].Find);
            Assert.AreEqual(3, back.Sectors[8].Surveys);
            Assert.AreEqual(3, SaveCodec.DecodeState(SaveCodec.EncodeState(s)).Buildings[BuildingId.DroneFactory].Level);
        }

        [Test]
        public void AnOldSave_LoadsWithTheWildsUncharted()
        {
            var s = Colony();
            var json = SaveCodec.EncodeState(s);
            json.Remove("wilds");
            ((Dictionary<string, object?>)json["buildings"]!).Remove("droneFactory");
            var back = SaveCodec.DecodeState(json);
            Assert.AreEqual(0, back.Wilds.Sectors.Count);
            Assert.AreEqual(-1, back.Wilds.Surveying);
            Assert.AreEqual(0, back.Buildings[BuildingId.DroneFactory].Level);
        }
    }
}
