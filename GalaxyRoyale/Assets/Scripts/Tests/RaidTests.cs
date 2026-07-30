// Phase C.4 sim-side raid tests: SendRaidMarch flies a round trip to a
// node-less tile (a "player colony" — those exist only server-side), and the
// caller-written outcome (survivors + plunder) is credited home through the
// normal march return path — including under offline catch-up.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class RaidTests
    {
        static (GameState state, TickEngine engine, SimEventBus events) NewGame(int seed = 42)
        {
            var state = GameState.CreateNewGame(seed);
            var events = new SimEventBus();
            var engine = new TickEngine(state, events);
            return (state, engine, events);
        }

        static TileXY BlankTileNear(GameState state, int dist)
        {
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = 0; dx <= 6; dx++)
                {
                    var t = new TileXY(state.HomeTile.X + dist + dx, state.HomeTile.Y + dy);
                    if (!t.Equals(state.HomeTile) && MapLookup.NodeAt(state, t) == null)
                        return t;
                }
            Assert.Fail("no blank tile found near home");
            return default;
        }

        [Test]
        public void RaidMarch_RoundTrip_CarriesPlunderHome()
        {
            var (state, engine, _) = NewGame();
            state.Ships[HullId.Fighter] = 10;
            state.Resources.Helium = 1_000_000;
            // Kill gold production so the plunder credit can be asserted exactly
            // (the mine would otherwise produce during the round trip).
            state.Buildings[BuildingId.GoldMine].Level = 0;
            var target = BlankTileNear(state, 8);

            var res = MarchSystem.SendRaidMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 10 }, target, out int marchId);
            Assert.IsTrue(res.Ok, res.Reason);
            Assert.AreEqual(0, state.Ships[HullId.Fighter], "the whole fleet flew out");

            // The game layer writes the pre-resolved outcome onto the march.
            var march = state.Marches.Find(m => m.Id == marchId)!;
            Assert.AreEqual(MarchMission.Attack, march.Mission);
            march.Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 8 }; // 2 lost
            march.Cargo = new ResourceBag { Gold = 5_000, Quartz = 2_000, Helium = 0 };

            long goldBefore = state.Resources.Gold;

            // Arrive at the node-less tile → automatic turnaround.
            engine.Advance(march.ArrivesAtTick - state.Tick + 1);
            Assert.AreEqual(MarchPhase.Returning, march.Phase, "empty tile turns the raid around");

            // Home again → survivors dock, plunder credits.
            engine.Advance(march.ArrivesAtTick - state.Tick + 1);
            Assert.AreEqual(0, state.Marches.Count, "march completed");
            Assert.AreEqual(8, state.Ships[HullId.Fighter], "survivors docked");
            Assert.AreEqual(goldBefore + 5_000, state.Resources.Gold, "plunder credited");
        }

        [Test]
        public void RecalledRaid_FlagsRecalled_RefundsGas_AndFiresHook()
        {
            var (state, engine, _) = NewGame();
            state.Ships[HullId.Fighter] = 10;
            state.Resources.Helium = 1_000_000;
            var target = BlankTileNear(state, 40); // far, so a partial recall refund is meaningful

            var res = MarchSystem.SendRaidMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 10 }, target, out int marchId);
            Assert.IsTrue(res.Ok, res.Reason);
            long heliumAfterLaunch = state.Resources.Helium;

            int recalledHookId = -1;
            MarchSystem.OnMarchRecalled = id => recalledHookId = id;
            try
            {
                engine.Advance(5); // fly a little way out, then abort
                var recall = MarchSystem.RecallMarch(state, marchId);
                Assert.IsTrue(recall.Ok, recall.Reason);
            }
            finally { MarchSystem.OnMarchRecalled = null; }

            var march = state.Marches.Find(m => m.Id == marchId)!;
            Assert.IsTrue(march.Recalled, "a recalled march is flagged so it can't file a phantom victory");
            Assert.AreEqual(MarchPhase.Returning, march.Phase);
            Assert.AreEqual(marchId, recalledHookId, "the recall hook fired (drops the pending PvP resolution)");
            Assert.Greater(state.Resources.Helium, heliumAfterLaunch, "unflown-distance gas was refunded");

            // The recalled flag survives a save round-trip (offline-safe abort).
            string json = GalaxyRoyale.Sim.Save.SaveCodec.Encode(SaveManager.Wrap(state, 0, null));
            var back = SaveManager.Unwrap(GalaxyRoyale.Sim.Save.SaveCodec.Decode(json))!.Value.state;
            Assert.IsTrue(back.Marches.Find(m => m.Id == marchId)!.Recalled, "Recalled persists");
        }

        [Test]
        public void RaidMarch_RejectsOwnColonyAndValidatesFleet()
        {
            var (state, _, _) = NewGame();
            state.Ships[HullId.Fighter] = 5;
            state.Resources.Helium = 1_000_000;

            var self = MarchSystem.SendRaidMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 5 }, state.HomeTile, out _);
            Assert.IsFalse(self.Ok, "cannot raid yourself");

            var tooMany = MarchSystem.SendRaidMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 6 },
                BlankTileNear(state, 8), out _);
            Assert.IsFalse(tooMany.Ok, "fleet validation still applies");
        }
    }
}
