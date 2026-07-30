// Dark Matter mining (v13): DM Fields spawn only in otherwise-empty cells
// (existing layout untouched), gather marches fill cargoDm, and the haul
// credits Premium.DarkMatter on return.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class DmFieldTests
    {
        [Test]
        public void Generator_SpawnsDmFields_WithSpecStats()
        {
            var sector = MapGenerator.GenerateSector(42);
            int dmCount = 0;
            foreach (var node in sector.Nodes.Values)
            {
                if (node.Kind != NodeKind.DMField) continue;
                dmCount++;
                Assert.AreEqual(250_000, node.Amount, "250 DM per field (milli)");
                Assert.AreEqual(6, node.RatePerSec, "~21.6 DM/h");
                Assert.IsNull(node.Resource);
            }
            Assert.Greater(dmCount, 0, "the galaxy must contain DM fields");
        }

        [Test]
        public void GatherMarch_AtDmField_CreditsDarkMatterOnReturn()
        {
            var state = GameState.CreateNewGame(42);
            var engine = new TickEngine(state, new SimEventBus());
            state.Ships[HullId.Hauler] = 20; // plenty of cargo
            state.Resources.Helium = 100_000_000;

            // Plant a dynamic DM field near home (deterministic, no map scan needed).
            var tile = new TileXY(state.HomeTile.X + 6, state.HomeTile.Y);
            if (MapLookup.NodeAt(state, tile) != null)
                tile = new TileXY(state.HomeTile.X + 7, state.HomeTile.Y + 1);
            state.Map.DynamicNodes.Add(new MapNode
            {
                Id = "dyn-dm", Kind = NodeKind.DMField, Tile = tile,
                Tier = 1, Amount = 250_000, RatePerSec = 6, CampLevel = 0,
            });

            int dmBefore = state.Premium.DarkMatter;
            var res = MarchSystem.SendMarch(state,
                new Dictionary<HullId, int> { [HullId.Hauler] = 20 },
                tile, MarchMission.Gather, out int id);
            Assert.IsTrue(res.Ok, res.Reason);

            // Outbound → gather (full 250 DM) → home. Advance generously.
            var march = state.Marches.Find(m => m.Id == id)!;
            engine.Advance(march.ArrivesAtTick - state.Tick + 1); // arrive, start gathering
            Assert.AreEqual(MarchPhase.Gathering, march.Phase);
            engine.Advance(march.ArrivesAtTick - state.Tick + 1); // finish gathering
            engine.Advance(march.ArrivesAtTick - state.Tick + 1); // fly home

            Assert.AreEqual(0, state.Marches.Count, "march completed");
            Assert.AreEqual(dmBefore + 250, state.Premium.DarkMatter,
                "the full 250 DM field credits as whole Dark Matter");
        }
    }
}
