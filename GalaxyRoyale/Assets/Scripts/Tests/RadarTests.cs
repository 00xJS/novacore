// Radar Station slice (content expansion, 2026-07-05): warning lead / detail
// tiers, probe-only speed bonus, and the v14 codec additions (radarStation
// building key + "radar" mailbox kind, with v13-shape backward compat).
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class RadarTests
    {
        [Test]
        public void WarnLead_ScalesThirtySecondsPerLevel()
        {
            Assert.AreEqual(0, RadarSystem.WarnLeadSeconds(0), "no radar, no warning");
            Assert.AreEqual(30, RadarSystem.WarnLeadSeconds(1));
            Assert.AreEqual(300, RadarSystem.WarnLeadSeconds(10));
        }

        [Test]
        public void DetailTier_Boundaries()
        {
            Assert.AreEqual(0, RadarSystem.DetailTier(0));
            Assert.AreEqual(1, RadarSystem.DetailTier(1));
            Assert.AreEqual(1, RadarSystem.DetailTier(4));
            Assert.AreEqual(2, RadarSystem.DetailTier(5));
            Assert.AreEqual(3, RadarSystem.DetailTier(10));
            Assert.AreEqual(4, RadarSystem.DetailTier(15));
        }

        [Test]
        public void ProbeSpeedBonus_AppliesOnlyToProbeOnlyFleets()
        {
            var state = GameState.CreateNewGame(42);
            var probeOnly = new Dictionary<HullId, int> { [HullId.Probe] = 1 };
            var mixed = new Dictionary<HullId, int> { [HullId.Probe] = 1, [HullId.Fighter] = 1 };

            double probeBase = MarchSystem.EffSpeed(state, probeOnly);
            double mixedBase = MarchSystem.EffSpeed(state, mixed);

            state.Buildings[BuildingId.RadarStation].Level = 10; // +50%
            Assert.AreEqual(probeBase * 1.5, MarchSystem.EffSpeed(state, probeOnly), 1e-6,
                "probe-only fleet rides the radar bonus");
            Assert.AreEqual(mixedBase, MarchSystem.EffSpeed(state, mixed), 1e-6,
                "escorted probes fly at fleet speed — no bonus");
        }

        [Test]
        public void SaveCodec_RoundTripsRadarBuildingAndWarningMail()
        {
            var state = GameState.CreateNewGame(42);
            state.Buildings[BuildingId.RadarStation].Level = 7;
            state.Mailbox.Insert(0, new RadarWarning
            {
                Id = 900, AtTick = state.Tick, Target = state.HomeTile,
                Subject = "⚠ RADAR — war fleet inbound",
                ArrivesAtTick = state.Tick + 120,
                IsFleet = true,
                AttackerName = "Zarkon",
                FleetCount = 12,
                FleetComp = new Dictionary<HullId, int> { [HullId.Cruiser] = 12 },
            });
            state.Mailbox.Insert(0, new RadarWarning // low-tier: optional fields empty
            {
                Id = 901, AtTick = state.Tick, Target = state.HomeTile,
                Subject = "⚠ RADAR — unknown contact inbound",
                ArrivesAtTick = state.Tick + 30,
            });

            var back = SaveManager.Unwrap(SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000))))!.Value.state;
            Assert.AreEqual(7, back.Buildings[BuildingId.RadarStation].Level);

            var full = (RadarWarning)back.Mailbox.Find(m => m.Id == 900)!;
            Assert.AreEqual(true, full.IsFleet);
            Assert.AreEqual("Zarkon", full.AttackerName);
            Assert.AreEqual(12, full.FleetCount);
            Assert.AreEqual(12, full.FleetComp![HullId.Cruiser]);

            var dim = (RadarWarning)back.Mailbox.Find(m => m.Id == 901)!;
            Assert.IsNull(dim.IsFleet);
            Assert.IsNull(dim.AttackerName);
            Assert.IsNull(dim.FleetCount);
            Assert.IsNull(dim.FleetComp);
            Assert.AreEqual(state.Tick + 30, dim.ArrivesAtTick);
        }

        [Test]
        public void SaveCodec_V13ShapeWithoutRadarStation_DecodesToLevelZero()
        {
            var encoded = SaveCodec.EncodeState(GameState.CreateNewGame(42));
            var buildings = (Dictionary<string, object?>)encoded["buildings"]!;
            buildings.Remove("radarStation"); // simulate a pre-v14 save
            var back = SaveCodec.DecodeState(encoded);
            Assert.AreEqual(0, back.Buildings[BuildingId.RadarStation].Level);
        }
    }
}
