// Load-cargo-at-launch + tier-2 "doubled tree" techs (2026-07-05):
// cargo rides marches from launch, occupies gather/loot space, and the new
// ResearchTimeReduce line shortens research only.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class CargoAndTier2Tests
    {
        static GameState HaulerGame()
        {
            var s = GameState.CreateNewGame(42);
            s.Ships[HullId.Hauler] = 10; // 10 × 800K milli = 8M milli cargo
            s.Resources.Helium = 100_000_000;
            s.Resources.Gold = 100_000_000;
            s.Resources.Quartz = 100_000_000;
            return s;
        }

        static TileXY EmptyTileNear(GameState s)
        {
            var tile = new TileXY(s.HomeTile.X + 6, s.HomeTile.Y);
            if (MapLookup.NodeAt(s, tile) != null)
                tile = new TileXY(s.HomeTile.X + 7, s.HomeTile.Y + 1);
            return tile;
        }

        /// <summary>SendMarch needs a real node — plant a fat asteroid to fly at.</summary>
        static TileXY PlantAsteroid(GameState s, string id = "dyn-cargo")
        {
            var tile = EmptyTileNear(s);
            s.Map.DynamicNodes.Add(new MapNode
            {
                Id = id, Kind = NodeKind.Asteroid, Resource = ResourceId.Gold,
                Tile = tile, Tier = 1, Amount = 50_000_000, RatePerSec = 1_000_000, CampLevel = 0,
            });
            return tile;
        }

        [Test]
        public void LoadCargo_AtLaunch_MovesResourcesOntoTheMarch()
        {
            var state = HaulerGame();
            var res = MarchSystem.SendMarch(state,
                new Dictionary<HullId, int> { [HullId.Hauler] = 10 },
                PlantAsteroid(state), MarchMission.Gather, out int id);
            Assert.IsTrue(res.Ok, res.Reason);

            long goldBefore = state.Resources.Gold;
            var load = new ResourceBag(2_000_000, 1_000_000, 0); // 3M milli, well under 8M cap
            Assert.IsTrue(MarchSystem.LoadCargo(state, id, load).Ok);

            var march = state.Marches.Find(m => m.Id == id)!;
            Assert.AreEqual(2_000_000, march.Cargo.Gold);
            Assert.AreEqual(1_000_000, march.Cargo.Quartz);
            Assert.AreEqual(goldBefore - 2_000_000, state.Resources.Gold, "stockpile paid the cargo");
        }

        [Test]
        public void LoadCargo_RejectsOverCapacity_AndLateLoads()
        {
            var state = HaulerGame();
            var engine = new TickEngine(state, new SimEventBus());
            MarchSystem.SendMarch(state,
                new Dictionary<HullId, int> { [HullId.Hauler] = 10 },
                PlantAsteroid(state), MarchMission.Gather, out int id);

            long cap = MarchSystem.EffCargoCap(state,
                new Dictionary<HullId, int> { [HullId.Hauler] = 10 });
            Assert.IsFalse(MarchSystem.LoadCargo(state, id, new ResourceBag(cap + 1000, 0, 0)).Ok,
                "over capacity must fail");

            engine.Advance(1); // no longer the launch tick
            Assert.IsFalse(MarchSystem.LoadCargo(state, id, new ResourceBag(1000, 0, 0)).Ok,
                "cargo can only be loaded at launch");
        }

        [Test]
        public void PreloadedCargo_ReducesGatherHaul()
        {
            var state = HaulerGame();
            var engine = new TickEngine(state, new SimEventBus());
            var tile = PlantAsteroid(state, "dyn-big");
            var comp = new Dictionary<HullId, int> { [HullId.Hauler] = 10 };
            long cap = MarchSystem.EffCargoCap(state, comp);
            MarchSystem.SendMarch(state, comp, tile, MarchMission.Gather, out int id);
            var half = new ResourceBag(cap / 2, 0, 0);
            Assert.IsTrue(MarchSystem.LoadCargo(state, id, half).Ok);

            var march = state.Marches.Find(m => m.Id == id)!;
            engine.Advance(march.ArrivesAtTick - state.Tick + 1); // arrive → gather
            engine.Advance(march.ArrivesAtTick - state.Tick + 1); // finish gathering
            Assert.LessOrEqual(march.Cargo.Total, cap, "haul must respect total capacity");
            Assert.GreaterOrEqual(march.Cargo.Gold, cap / 2, "the pre-load is still aboard");
            Assert.LessOrEqual(march.Cargo.Total - half.Total, cap - half.Total,
                "gathered amount fits the FREE space only");
        }

        [Test]
        public void ResearchTimeReduce_ShortensResearchOnly()
        {
            var state = GameState.CreateNewGame(42);
            int researchBefore = ResearchSystem.GetResearchTime(state, TechId.IonThrusters, 3);
            int buildBefore = BuildingSystem.GetBuildTime(state, BuildingId.Warehouse, 2);

            state.Research[TechId.QuantumComputing] = 10; // −30% research time
            Assert.AreEqual((int)System.Math.Ceiling(researchBefore * 0.7),
                ResearchSystem.GetResearchTime(state, TechId.IonThrusters, 3), 1.0);
            Assert.AreEqual(buildBefore, BuildingSystem.GetBuildTime(state, BuildingId.Warehouse, 2),
                "building time ignores the research line");
        }

        [Test]
        public void SaveCodec_RoundTripsTier2TechIds()
        {
            var state = GameState.CreateNewGame(42);
            state.Research[TechId.AntimatterWarheads] = 4;
            state.Research[TechId.SingularityCores] = 2;
            var back = SaveManager.Unwrap(
                SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000))))!.Value.state;
            Assert.AreEqual(4, back.Research[TechId.AntimatterWarheads]);
            Assert.AreEqual(2, back.Research[TechId.SingularityCores]);
        }
    }
}
