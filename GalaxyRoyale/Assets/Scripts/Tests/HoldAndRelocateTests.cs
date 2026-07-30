// Fly-to holds, relocation guards, and the supernova exclusion zone
// (third playtest batch, 2026-07-05).
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class HoldAndRelocateTests
    {
        static (GameState state, TickEngine engine) NewGame(int seed = 42)
        {
            var state = GameState.CreateNewGame(seed);
            var engine = new TickEngine(state, new SimEventBus());
            return (state, engine);
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
            Assert.Fail("no blank tile found");
            return default;
        }

        [Test]
        public void FlyTo_HoldsAtEmptySpace_UntilRecalled()
        {
            var (state, engine) = NewGame();
            state.Ships[HullId.Fighter] = 5;
            state.Resources.Helium = 1_000_000;
            var target = BlankTileNear(state, 8);

            var res = MarchSystem.SendRaidMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 5 }, target, out int id,
                MarchMission.Gather);
            Assert.IsTrue(res.Ok, res.Reason);
            var march = state.Marches.Find(m => m.Id == id)!;

            engine.Advance(march.ArrivesAtTick - state.Tick + 1);
            Assert.AreEqual(MarchPhase.Gathering, march.Phase, "fly-to holds as 'gathering' at the tile");
            Assert.AreEqual(int.MaxValue, march.ArrivesAtTick, "hold never expires on its own");

            engine.Advance(5_000);
            Assert.AreEqual(1, state.Marches.Count, "still holding hours later");

            Assert.IsTrue(MarchSystem.RecallMarch(state, id).Ok);
            engine.Advance(march.ArrivesAtTick - state.Tick + 1);
            Assert.AreEqual(0, state.Marches.Count, "recalled hold flies home");
            Assert.AreEqual(5, state.Ships[HullId.Fighter], "fleet docked again");
        }

        [Test]
        public void Relocation_BlockedWhileFleetsAreOut()
        {
            var (state, engine) = NewGame();
            state.Ships[HullId.Fighter] = 5;
            state.Resources.Helium = 1_000_000;
            var target = BlankTileNear(state, 8);
            var newHome = BlankTileNear(state, 14);

            Assert.IsTrue(MarchSystem.SendRaidMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 5 }, target, out int id).Ok);

            var blocked = MarchSystem.RelocateHome(state, newHome);
            Assert.IsFalse(blocked.Ok, "cannot jump with a fleet in flight");
            StringAssert.Contains("Recall", blocked.Reason);

            // Bring the fleet home, then the jump goes through.
            var march = state.Marches.Find(m => m.Id == id)!;
            engine.Advance(march.ArrivesAtTick - state.Tick + 1); // arrive → auto-return
            engine.Advance(march.ArrivesAtTick - state.Tick + 1); // home
            Assert.AreEqual(0, state.Marches.Count);
            Assert.IsTrue(MarchSystem.RelocateHome(state, newHome).Ok);
            Assert.AreEqual(newHome, state.HomeTile);
        }

        [Test]
        public void CoreExclusion_ForbidsPortsAndMarches()
        {
            var (state, _) = NewGame();
            state.Ships[HullId.Fighter] = 5;
            state.Resources.Helium = 1_000_000;
            var core = new TileXY(Balance.SectorSize / 2, Balance.SectorSize / 2);

            Assert.IsTrue(MarchSystem.InCoreExclusion(core));
            Assert.IsTrue(MarchSystem.InCoreExclusion(
                new TileXY(core.X + Balance.CoreExclusionHalf, core.Y - Balance.CoreExclusionHalf)));
            Assert.IsFalse(MarchSystem.InCoreExclusion(
                new TileXY(core.X + Balance.CoreExclusionHalf + 1, core.Y)));

            Assert.IsFalse(MarchSystem.RelocateHome(state, core).Ok, "no porting into the core");
            Assert.IsFalse(MarchSystem.SendRaidMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 5 }, core, out _).Ok,
                "no marches into the core");
        }
    }
}
