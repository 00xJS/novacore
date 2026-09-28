// Content-expansion tech slice (2026-07-05): gather-rate research, warehouse
// shield research, ship-only build-time research, and per-hull attack/plating
// lines that ride hull-scoped combat mods through the pure CombatResolver.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class ContentTechsTests
    {
        [Test]
        public void HullScopedTechs_DoNotLeakIntoGlobalAccessors()
        {
            var state = GameState.CreateNewGame(42);
            state.Research[TechId.FighterDoctrine] = 5;   // +10% Fighter attack
            state.Research[TechId.CruiserBulkheads] = 5;  // +10% Cruiser durability

            Assert.AreEqual(1f, ResearchSystem.AtkMult(state), 1e-5f, "global attack untouched");
            Assert.AreEqual(1f, ResearchSystem.HpMult(state), 1e-5f, "global durability untouched");
            Assert.AreEqual(1.1f, ResearchSystem.AtkMultFor(state, HullId.Fighter), 1e-5f);
            Assert.AreEqual(1f, ResearchSystem.AtkMultFor(state, HullId.Bomber), 1e-5f);
            Assert.AreEqual(1.1f, ResearchSystem.HpMultFor(state, HullId.Cruiser), 1e-5f);
        }

        [Test]
        public void GlobalAndScopedMods_Stack()
        {
            var state = GameState.CreateNewGame(42);
            state.Research[TechId.WeaponsCalibration] = 4; // +20% global
            state.Research[TechId.FighterDoctrine] = 5;    // +10% Fighter
            Assert.AreEqual(1.3f, ResearchSystem.AtkMultFor(state, HullId.Fighter), 1e-5f);
            Assert.AreEqual(1.2f, ResearchSystem.AtkMultFor(state, HullId.Cruiser), 1e-5f);
        }

        [Test]
        public void CombatResolver_PerHullAtkMods_TipASymmetricFight()
        {
            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 10 };
            var mods = new FleetMods(1f, 1f,
                atkByHull: new Dictionary<HullId, float> { [HullId.Cruiser] = 2f });

            var report = CombatResolver.Resolve(fleet, new Dictionary<HullId, int>(fleet), mods);
            Assert.AreEqual(BattleWinner.Attacker, report.Winner,
                "doubled per-hull attack must win the mirror match");

            // And without mods the mirror match cannot favor the attacker.
            var even = CombatResolver.Resolve(fleet, new Dictionary<HullId, int>(fleet));
            Assert.AreNotEqual(BattleWinner.Attacker, even.Winner);
        }

        [Test]
        public void CombatResolver_PerHullHpMods_ShieldOnlyThatHull()
        {
            var attacker = new Dictionary<HullId, int> { [HullId.Cruiser] = 10 };
            var defender = new Dictionary<HullId, int> { [HullId.Cruiser] = 12 };
            var unmodded = CombatResolver.Resolve(attacker, defender);
            Assert.AreEqual(BattleWinner.Defender, unmodded.Winner, "outnumbered attacker loses clean");

            var mods = new FleetMods(1f, 1f,
                hpByHull: new Dictionary<HullId, float> { [HullId.Cruiser] = 3f });
            var shielded = CombatResolver.Resolve(attacker, defender, mods);
            // Since regenerating shields landed, mirror attrition is slower — the
            // 10-round cap can end in a draw. The armor mod's job is to stop the
            // loss and keep more ships alive, not necessarily sweep the board.
            Assert.AreNotEqual(BattleWinner.Defender, shielded.Winner,
                "tripled cruiser durability must at least save the fight");
            int keptWithMods = shielded.AttackerSurvivors.TryGetValue(HullId.Cruiser, out var a) ? a : 0;
            int keptPlain = unmodded.AttackerSurvivors.TryGetValue(HullId.Cruiser, out var b) ? b : 0;
            Assert.Greater(keptWithMods, keptPlain, "armor research keeps more hulls alive");
        }

        [Test]
        public void ShipTimeReduce_CutsShipTime_NotBuildingTime()
        {
            var state = GameState.CreateNewGame(42);
            int shipBefore = FleetSystem.ShipBuildTime(state, HullId.Cruiser);
            int bldBefore = BuildingSystem.GetBuildTime(state, BuildingId.Warehouse, 2);

            state.Research[TechId.RapidFabrication] = 5; // −10% ships only
            Assert.AreEqual((int)System.Math.Ceiling(shipBefore * 0.9),
                FleetSystem.ShipBuildTime(state, HullId.Cruiser));
            Assert.AreEqual(bldBefore, BuildingSystem.GetBuildTime(state, BuildingId.Warehouse, 2),
                "building upgrades ignore ship-only research");
        }

        [Test]
        public void DeepVaultProtocols_ScalesRaidShieldedCapacity()
        {
            var state = GameState.CreateNewGame(42);
            var baseShield = ResourceSystem.GetProtected(state);
            state.Research[TechId.DeepVaultProtocols] = 10; // +80%
            var boosted = ResourceSystem.GetProtected(state);
            Assert.AreEqual((long)(baseShield.Gold * 1.8), boosted.Gold);
        }

        [Test]
        public void ExtractionAlgorithms_ShortensGatherDuration()
        {
            GameState Fresh()
            {
                var s = GameState.CreateNewGame(42);
                s.Ships[HullId.Hauler] = 20;
                s.Resources.Helium = 100_000_000;
                var tile = new TileXY(s.HomeTile.X + 6, s.HomeTile.Y);
                if (MapLookup.NodeAt(s, tile) != null)
                    tile = new TileXY(s.HomeTile.X + 7, s.HomeTile.Y + 1);
                s.Map.DynamicNodes.Add(new MapNode
                {
                    Id = "dyn-ast", Kind = NodeKind.Asteroid, Resource = ResourceId.Gold,
                    Tile = tile, Tier = 1, Amount = 3_600_000, RatePerSec = 1000, CampLevel = 0,
                });
                return s;
            }

            int GatherTicks(GameState s)
            {
                var engine = new TickEngine(s, new SimEventBus());
                var node = s.Map.DynamicNodes[0];
                var res = MarchSystem.SendMarch(s,
                    new Dictionary<HullId, int> { [HullId.Hauler] = 20 },
                    node.Tile, MarchMission.Gather, out int id);
                Assert.IsTrue(res.Ok, res.Reason);
                var march = s.Marches.Find(m => m.Id == id)!;
                engine.Advance(march.ArrivesAtTick - s.Tick + 1); // arrive → gathering
                Assert.AreEqual(MarchPhase.Gathering, march.Phase);
                return march.ArrivesAtTick - s.Tick;
            }

            var plain = Fresh();
            int slow = GatherTicks(plain);

            var researched = Fresh();
            researched.Research[TechId.ExtractionAlgorithms] = 10; // +40% gather speed
            int fast = GatherTicks(researched);

            Assert.Less(fast, slow, "gather research must shorten time-on-station");
            Assert.AreEqual((int)System.Math.Ceiling(slow / 1.4), fast, 1.0,
                "duration should shrink by ≈ the researched multiplier");
        }

        [Test]
        public void SaveCodec_RoundTripsNewTechIds()
        {
            var state = GameState.CreateNewGame(42);
            state.Research[TechId.ExtractionAlgorithms] = 3;
            state.Research[TechId.BomberPayloads] = 2;
            state.ResearchQueue.Add(new ResearchOrder
            {
                TechId = TechId.RapidFabrication, ToLevel = 1, EndsAtTick = 5_000,
            });

            var decoded = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000)));
            var back = SaveManager.Unwrap(decoded)!.Value.state;
            Assert.AreEqual(3, back.Research[TechId.ExtractionAlgorithms]);
            Assert.AreEqual(2, back.Research[TechId.BomberPayloads]);
            Assert.AreEqual(TechId.RapidFabrication, back.ResearchQueue[0].TechId);
        }
    }
}
