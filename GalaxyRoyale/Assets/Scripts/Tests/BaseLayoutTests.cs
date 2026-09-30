// The globe base (2026-09-28): every building has a fixed pad, pads never
// overlap, the Mining Belt has a pad for every extra mine the Command Center
// can unlock, and mines take their type's pads in the order they were built.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class BaseLayoutTests
    {
        static GameState NewState(int commandCenter)
        {
            var state = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            state.Buildings[BuildingId.CommandCenter].Level = commandCenter;
            return state;
        }

        static ExtraMine AddMine(GameState state, MineType type, int level = 1)
        {
            var mine = new ExtraMine { Id = state.NextMineId++, Type = type, Level = level, Plot = 0 };
            state.ExtraMines.Add(mine);
            return mine;
        }

        /// <summary>Great-circle distance in degrees.</summary>
        static double Apart(BasePad a, BasePad b)
        {
            double p1 = a.Lat * Math.PI / 180, p2 = b.Lat * Math.PI / 180, dl = (b.Lon - a.Lon) * Math.PI / 180;
            double c = Math.Sin(p1) * Math.Sin(p2) + Math.Cos(p1) * Math.Cos(p2) * Math.Cos(dl);
            return Math.Acos(Math.Max(-1, Math.Min(1, c))) * 180 / Math.PI;
        }

        [Test]
        public void EveryBuilding_HasOneFixedPad_CoreInCommand_FrontierInTheFrontier()
        {
            Assert.AreEqual(Buildings.All.Count, BaseLayout.Pads.Count(p => p.Kind == PadKind.Building));
            foreach (var id in Buildings.All)
            {
                var pad = BaseLayout.BuildingPad(id);
                Assert.AreEqual(Buildings.IsCitadel(id) ? BaseDistrict.Citadel
                    : Buildings.IsFrontier(id) ? BaseDistrict.Frontier : BaseDistrict.Command, pad.District, id.ToString());
                Assert.AreEqual(Buildings.Defs[id].UnlockCc, pad.UnlockCc, id.ToString());
            }
            Assert.AreEqual(9, BaseLayout.Pads.Count(p => p.District == BaseDistrict.Command));
            Assert.AreEqual(0, BaseLayout.BuildingPad(BuildingId.CommandCenter).Lon, "the Command Center is the middle of the base");
        }

        [Test]
        public void TheBelt_HasAPadForEveryExtraMine_OpeningWithTheCommandCenter()
        {
            int extras = Balance.MaxMinesPerType - 1;
            Assert.AreEqual(extras, BaseLayout.BeltLon.Length);
            Assert.AreEqual(extras * MineTypes.All.Count, BaseLayout.Pads.Count(p => p.Kind == PadKind.Mine));
            foreach (var type in MineTypes.All)
            {
                // Each resource's row lines up with its first mine in the Command district.
                Assert.AreEqual(BaseLayout.BuildingPad(MineTypes.ToBuildingId(type)).Lat, BaseLayout.MineRow(type), type.ToString());
                for (int tier = 0; tier < extras; tier++)
                {
                    var pad = BaseLayout.MinePad(type, tier);
                    Assert.AreEqual(BaseDistrict.MiningBelt, pad.District);
                    Assert.AreEqual(Balance.MineSlotUnlocks[tier + 1], pad.UnlockCc, $"{type} #{tier + 2}");
                    Assert.AreEqual(BaseLayout.MineRow(type), pad.Lat);
                }
            }
        }

        [Test]
        public void Pads_NeverOverlap_AndHaveUniqueKeys()
        {
            var pads = BaseLayout.Pads;
            Assert.AreEqual(pads.Count, pads.Select(p => p.Key).Distinct().Count());
            for (int i = 0; i < pads.Count; i++)
                for (int j = i + 1; j < pads.Count; j++)
                    Assert.Greater(Apart(pads[i], pads[j]), 9.5, $"{pads[i].Key} and {pads[j].Key} are too close"); // the belt's gold row is the tightest
            foreach (var p in pads)
                Assert.That(p.Lat, p.District == BaseDistrict.Citadel ? Is.InRange(60.0, 90.0) : Is.InRange(0.0, 60.0),
                    $"{p.Key}: the band keeps below 60°, the Citadel crowns the pole");
        }

        [Test]
        public void Mines_TakeTheirTypesPads_InTheOrderTheyWereBuilt()
        {
            var state = NewState(15);
            var gold1 = AddMine(state, MineType.GoldMine);
            var quartz1 = AddMine(state, MineType.QuartzExtractor);
            var gold2 = AddMine(state, MineType.GoldMine);
            Assert.AreEqual(0, BaseLayout.MineTier(state, gold1));
            Assert.AreEqual(1, BaseLayout.MineTier(state, gold2));
            Assert.AreEqual(0, BaseLayout.MineTier(state, quartz1));
            Assert.AreSame(gold2, BaseLayout.MineOn(state, BaseLayout.MinePad(MineType.GoldMine, 1)));
            Assert.IsNull(BaseLayout.MineOn(state, BaseLayout.MinePad(MineType.HeliumRefinery, 0)));

            // A cancelled placement frees its pad, and the next mine of that type moves up.
            state.ExtraMines.Remove(gold1);
            Assert.AreEqual(0, BaseLayout.MineTier(state, gold2));
        }

        [Test]
        public void AllTwelveExtraMines_GetTheirOwnPad()
        {
            // The old base had 9 expansion plots, so the 10th to 12th extra mine had nowhere to go.
            var state = NewState(15);
            foreach (var type in MineTypes.All)
                for (int i = 0; i < Balance.MaxMinesPerType - 1; i++) AddMine(state, type);
            var pads = new HashSet<string>();
            foreach (var mine in state.ExtraMines)
                Assert.IsTrue(pads.Add(BaseLayout.MinePad(mine.Type, BaseLayout.MineTier(state, mine)).Key));
            Assert.AreEqual(12, pads.Count);
            Assert.AreEqual(0, BaseLayout.OpenPads(state, BaseDistrict.MiningBelt));
        }

        [Test]
        public void OpenPadsAndCounts_FollowTheCommandCenter()
        {
            var state = NewState(2);
            Assert.AreEqual(0, BaseLayout.OpenPads(state, BaseDistrict.MiningBelt));
            state.Buildings[BuildingId.CommandCenter].Level = 3;
            Assert.AreEqual(3, BaseLayout.OpenPads(state, BaseDistrict.MiningBelt), "one pad per resource opens at CC 3");
            AddMine(state, MineType.GoldMine);
            Assert.AreEqual(2, BaseLayout.OpenPads(state, BaseDistrict.MiningBelt));
            Assert.AreEqual((1, 12), BaseLayout.Count(state, BaseDistrict.MiningBelt));
            state.Buildings[BuildingId.CommandCenter].Level = 6;
            Assert.AreEqual(5, BaseLayout.OpenPads(state, BaseDistrict.MiningBelt));

            var mine = AddMine(state, MineType.QuartzExtractor, level: 0); // placed, still building
            Assert.AreEqual(4, BaseLayout.OpenPads(state, BaseDistrict.MiningBelt), "a mine under construction takes its pad");
            Assert.AreEqual((1, 12), BaseLayout.Count(state, BaseDistrict.MiningBelt), "but isn't built yet");
            Assert.IsNotNull(mine);
        }

        [Test]
        public void TheFrontier_FillsInAsTheCommandCenterRises()
        {
            var state = NewState(4);
            Assert.IsFalse(BaseLayout.PlannedOnline(state, PlannedBuilding.ExchangeTerminal));
            Assert.AreEqual((0, 8), BaseLayout.Count(state, BaseDistrict.Frontier));
            state.Buildings[BuildingId.CommandCenter].Level = 5;
            Assert.IsTrue(BaseLayout.PlannedOnline(state, PlannedBuilding.ExchangeTerminal));
            Assert.AreEqual((1, 8), BaseLayout.Count(state, BaseDistrict.Frontier));

            var bastion = BaseLayout.BuildingPad(BuildingId.CommandBastion);
            Assert.IsFalse(BaseLayout.Unlocked(state, bastion), "the Bastion opens at CC 6");
            state.Buildings[BuildingId.CommandCenter].Level = 6;
            Assert.IsTrue(BaseLayout.Unlocked(state, bastion));
            state.Buildings[BuildingId.CommandBastion].Level = 1;
            Assert.AreEqual((2, 8), BaseLayout.Count(state, BaseDistrict.Frontier));

            var drones = BaseLayout.BuildingPad(BuildingId.DroneFactory);
            Assert.AreEqual(BaseDistrict.Frontier, drones.District);
            Assert.IsFalse(BaseLayout.Unlocked(state, drones), "the Drone Factory opens at CC 10");
            state.Buildings[BuildingId.CommandCenter].Level = 10;
            Assert.IsTrue(BaseLayout.Unlocked(state, drones));
            state.Buildings[BuildingId.DroneFactory].Level = 1;
            Assert.AreEqual((3, 8), BaseLayout.Count(state, BaseDistrict.Frontier));

            // The Frontier completed (2026-09-29): no reserved or "coming soon" pads left.
            Assert.IsFalse(BaseLayout.Pads.Any(p => p.Kind == PadKind.Reserved));
            state.Buildings[BuildingId.CommandCenter].Level = 30;
            foreach (var id in new[] { BuildingId.RepairDock, BuildingId.JumpGate, BuildingId.ClanEmbassy, BuildingId.Observatory })
            {
                var pad = BaseLayout.BuildingPad(id);
                Assert.AreEqual(BaseDistrict.Frontier, pad.District, id.ToString());
                Assert.IsTrue(BaseLayout.Unlocked(state, pad), id.ToString());
                state.Buildings[id].Level = 1;
            }
            Assert.AreEqual((7, 8), BaseLayout.Count(state, BaseDistrict.Frontier));
        }
    
        [Test]
        public void AFinishedUpgrade_SaysWhichMineItWas_SoTheGlobeLightsTheRightPad()
        {
            var state = NewState(10);
            var mine = AddMine(state, MineType.QuartzExtractor, level: 1);
            state.BuildQueue.Add(new BuildOrder { Building = BuildingId.QuartzExtractor, ToLevel = 2, EndsAtTick = state.Tick, MineId = mine.Id });
            state.BuildQueue.Add(new BuildOrder { Building = BuildingId.RadarStation, ToLevel = 3, EndsAtTick = state.Tick });
            var bus = new SimEventBus();
            var done = new List<BuildingCompleted>();
            bus.Subscribe(e => { if (e is BuildingCompleted b) done.Add(b); });
            BuildingSystem.Tick(state, bus);
            Assert.AreEqual(2, done.Count);
            var quartz = done.Find(d => d.Building == BuildingId.QuartzExtractor);
            Assert.AreEqual(mine.Id, quartz!.MineId);
            Assert.AreEqual(2, mine.Level);
            Assert.IsNull(done.Find(d => d.Building == BuildingId.RadarStation)!.MineId, "the core buildings carry no mine");
        }
}
}
