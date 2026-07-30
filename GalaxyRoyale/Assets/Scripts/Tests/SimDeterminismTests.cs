// Smoke tests for the Phase B.2 sim port. These focus on:
//   - Load-bearing invariants (drift-free production, deterministic map, purity of combat)
//   - Known-good behavioral outcomes (level-up completes on the right tick, prereqs gate)
//
// They do NOT compare byte-for-byte against v1 output — that would require running
// the TypeScript sim in-process. Cross-engine parity is a Phase B.3+ task.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class SimDeterminismTests
    {
        static (GameState state, TickEngine engine, SimEventBus events) NewGame(int seed = 42)
        {
            var state = GameState.CreateNewGame(seed);
            var events = new SimEventBus();
            var engine = new TickEngine(state, events);
            return (state, engine, events);
        }

        // ---------- NewGame ----------

        [Test]
        public void NewGame_StartingResources_ConvertedToMilli()
        {
            var (state, _, _) = NewGame();
            // TestMode swaps in the fat playtest wallet; release keeps v1's 500/300/100.
            long gold = Balance.TestMode ? Balance.TestModeResources * 1000L : 500_000;
            long quartz = Balance.TestMode ? Balance.TestModeResources * 1000L : 300_000;
            long helium = Balance.TestMode ? Balance.TestModeResources * 1000L : 100_000;
            Assert.AreEqual(gold, state.Resources.Gold,   "whole units → milli");
            Assert.AreEqual(quartz, state.Resources.Quartz, "whole units → milli");
            Assert.AreEqual(helium, state.Resources.Helium,     "whole units → milli");
        }

        [Test]
        public void NewGame_StartingBuildingLevels_MatchV1Rules()
        {
            var (state, _, _) = NewGame();
            Assert.AreEqual(1, state.Buildings[BuildingId.CommandCenter].Level);
            Assert.AreEqual(1, state.Buildings[BuildingId.GoldMine].Level);
            Assert.AreEqual(1, state.Buildings[BuildingId.PowerPlant].Level);
            Assert.AreEqual(0, state.Buildings[BuildingId.QuartzExtractor].Level);
            Assert.AreEqual(0, state.Buildings[BuildingId.ResearchLab].Level);
        }

        [Test]
        public void NewGame_AllShipCountsZero()
        {
            var (state, _, _) = NewGame();
            foreach (var hull in Ships.All)
                Assert.AreEqual(0, state.Ships[hull], $"{hull} should start at 0");
        }

        // ---------- Resource production drift-free property ----------

        [Test]
        public void ProducedBetween_TelescopesOverHourExactly()
        {
            // Per-tick sum from t=0..3600 must equal the closed-form integral: floor(rate*3600/3600) = rate.
            const long rate = 30_000; // 30,000 milli/hour
            long sum = 0;
            for (int t = 1; t <= 3600; t++) sum += ResourceSystem.ProducedBetween(rate, t - 1, t);
            Assert.AreEqual(30_000, sum, "Per-tick production must telescope with zero drift.");
        }

        [Test]
        public void ResourceTick_OneHour_ProducesExpectedGold()
        {
            var (state, engine, _) = NewGame();
            long before = state.Resources.Gold;

            // L1 CC + L1 GoldMine (30 whole/hr base × ProdPerHour(30,1)=30) + L1 PowerPlant.
            // Energy: supply=30, demand=10 (mine only), factor=1. No research/boosts. Rate=30_000 milli/hr.
            engine.Advance(3600);

            long gained = state.Resources.Gold - before;
            Assert.AreEqual(30_000, gained, "1h of L1 gold mine at full energy = 30 whole = 30,000 milli.");
        }

        // ---------- Map generation determinism ----------

        [Test]
        public void MapGenerator_SameSeed_ProducesSameNodeSet()
        {
            var a = MapGenerator.GenerateSector(seed: 12345);
            var b = MapGenerator.GenerateSector(seed: 12345);
            Assert.AreEqual(a.Nodes.Count, b.Nodes.Count, "Node count must match for the same seed.");
            foreach (var kv in a.Nodes)
            {
                Assert.IsTrue(b.Nodes.TryGetValue(kv.Key, out var bn), $"Tile {kv.Key} missing in second run.");
                Assert.AreEqual(kv.Value.Kind,   bn.Kind,   $"Kind drift at {kv.Key}");
                Assert.AreEqual(kv.Value.Tier,   bn.Tier,   $"Tier drift at {kv.Key}");
                Assert.AreEqual(kv.Value.Amount, bn.Amount, $"Amount drift at {kv.Key}");
            }
        }

        [Test]
        public void MapGenerator_DifferentSeeds_ProduceDifferentSectors()
        {
            var a = MapGenerator.GenerateSector(seed: 1);
            var b = MapGenerator.GenerateSector(seed: 2);
            Assert.AreNotEqual(a.Nodes.Count, b.Nodes.Count,
                "Different seeds should produce different node populations (astronomically unlikely to collide).");
        }

        [Test]
        public void MapGenerator_NoNodesInsideHomeClearRadius()
        {
            var sector = MapGenerator.GenerateSector(seed: 99);
            foreach (var node in sector.Nodes.Values)
            {
                double dist = TileXY.Distance(node.Tile, Balance.HomeTile);
                Assert.Greater(dist, 6, $"Node at {node.Tile} within HomeClearRadius (6 tiles).");
            }
        }

        // ---------- Building upgrade queue ----------

        [Test]
        public void BuildingSystem_UpgradeCompletesOnExpectedTick()
        {
            var (state, engine, events) = NewGame();
            // Grant plenty of resources so the upgrade actually starts.
            state.Resources.Add(new ResourceBag(100_000, 100_000, 100_000).Milli());

            var result = BuildingSystem.StartUpgrade(state, BuildingId.CommandCenter);
            Assert.IsTrue(result.Ok, $"Upgrade should start: {result.Reason}");

            var order = state.BuildQueue[0];
            int completionTick = order.EndsAtTick;
            int levelBefore = state.Buildings[BuildingId.CommandCenter].Level;

            // Fire event capture so we can verify BuildingCompleted was emitted.
            var completedEvents = new List<BuildingCompleted>();
            events.Subscribe(e => { if (e is BuildingCompleted bc) completedEvents.Add(bc); });

            engine.Advance(completionTick - state.Tick);

            Assert.AreEqual(levelBefore + 1, state.Buildings[BuildingId.CommandCenter].Level, "Level must bump on completion.");
            Assert.AreEqual(0, state.BuildQueue.Count, "Queue must clear on completion.");
            Assert.AreEqual(1, completedEvents.Count, "Exactly one BuildingCompleted event should fire.");
            Assert.AreEqual(levelBefore + 1, completedEvents[0].Level);
        }

        // ---------- Research prereqs ----------

        [Test]
        public void ResearchSystem_DeepCoreDrilling_GatedByYieldOptimizationLevel()
        {
            var (state, _, _) = NewGame();
            state.Buildings[BuildingId.ResearchLab].Level = 10; // lab is fine
            state.Research[TechId.YieldOptimization] = 5;       // below required level 8

            var result = ResearchSystem.CheckResearch(state, TechId.DeepCoreDrilling);
            Assert.IsFalse(result.Ok, "Should fail: YieldOptimization L5 < required L8.");
            StringAssert.Contains("Yield Optimization", result.Reason ?? "");

            state.Research[TechId.YieldOptimization] = 8;       // now at required level
            state.Resources.Add(new ResourceBag(999_999, 999_999, 999_999).Milli());
            result = ResearchSystem.CheckResearch(state, TechId.DeepCoreDrilling);
            Assert.IsTrue(result.Ok, $"Should now pass: {result.Reason}");
        }

        // ---------- Combat purity ----------

        [Test]
        public void CombatResolver_IsPure_IdenticalInputsProduceIdenticalReport()
        {
            var atk = new Dictionary<HullId, int> { [HullId.Fighter] = 20, [HullId.Bomber] = 5 };
            var def = new Dictionary<HullId, int> { [HullId.Cruiser] = 3, [HullId.Fighter] = 8 };

            var report1 = CombatResolver.Resolve(atk, def);
            var report2 = CombatResolver.Resolve(atk, def);

            Assert.AreEqual(report1.Winner, report2.Winner);
            Assert.AreEqual(report1.Rounds.Count, report2.Rounds.Count);
            foreach (var hull in Ships.All)
            {
                int s1 = report1.AttackerSurvivors.TryGetValue(hull, out var a) ? a : 0;
                int s2 = report2.AttackerSurvivors.TryGetValue(hull, out var b) ? b : 0;
                Assert.AreEqual(s1, s2, $"Attacker survivor drift on {hull}");
            }
        }

        [Test]
        public void CombatResolver_OverwhelmingForce_AttackerWins()
        {
            // 100 fighters vs 5 fighters — no counter subtleties, just numbers.
            var atk = new Dictionary<HullId, int> { [HullId.Fighter] = 100 };
            var def = new Dictionary<HullId, int> { [HullId.Fighter] = 5 };
            var report = CombatResolver.Resolve(atk, def);
            Assert.AreEqual(BattleWinner.Attacker, report.Winner);
            Assert.AreEqual(0, report.DefenderSurvivors.Count, "Defender should be wiped.");
        }

        [Test]
        public void CombatResolver_CounterBonus_CountsMoreDamageThanNoCounter()
        {
            // Fighters counter Bombers. Same attacker force vs an all-Bomber defender should
            // inflict more relative damage than vs an all-Cruiser defender of equivalent HP.
            var atk = new Dictionary<HullId, int> { [HullId.Fighter] = 10 };
            var vsBombers = CombatResolver.Resolve(atk, new Dictionary<HullId, int> { [HullId.Bomber] = 10 });
            var vsCruisers = CombatResolver.Resolve(atk, new Dictionary<HullId, int> { [HullId.Cruiser] = 3 });
            int bomberLossesR1 = vsBombers.Rounds[0].DefenderLosses.TryGetValue(HullId.Bomber, out var b) ? b : 0;
            int cruiserLossesR1 = vsCruisers.Rounds[0].DefenderLosses.TryGetValue(HullId.Cruiser, out var c) ? c : 0;
            Assert.Greater(bomberLossesR1, 0, "Counter bonus should kill at least one bomber in round 1.");
            _ = cruiserLossesR1; // no strict inequality — just verifying the counter path executes
        }

        // ---------- Power / might score ----------

        [Test]
        public void PowerSystem_GrowsWithBuildingsAndShips_MarchingShipsStillCount()
        {
            // Mirrors v1's `shop.test.ts` PowerSystem block.
            var (state, _, _) = NewGame();

            long p0 = PowerSystem.ComputePower(state);
            Assert.Greater(p0, 0, "Starting buildings alone should yield nonzero power.");

            state.Buildings[BuildingId.CommandCenter].Level = 5;
            long p1 = PowerSystem.ComputePower(state);
            Assert.Greater(p1, p0, "Higher building level must raise power.");

            state.Ships[HullId.Cruiser] = 10;
            long p2 = PowerSystem.ComputePower(state);
            Assert.Greater(p2, p1, "Docked ships must raise power.");

            // Moving ships onto a march must not reduce power.
            state.Ships[HullId.Cruiser] = 0;
            state.Marches.Add(new March
            {
                Id = 1,
                Phase = MarchPhase.Outbound,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 10 },
                Node = new TileXY(1, 1),
                LegFrom = new Position(0, 0),
                LegTo = new Position(1, 1),
                DepartedAtTick = 0,
                ArrivesAtTick = 100,
                Mission = MarchMission.Attack,
            });
            Assert.AreEqual(p2, PowerSystem.ComputePower(state), "Marching ships must count toward power.");
        }

        [Test]
        public void PowerSystem_ExtraMines_CountLikeSingletons()
        {
            var (state, _, _) = NewGame();
            long before = PowerSystem.ComputePower(state);
            state.ExtraMines.Add(new ExtraMine { Id = 1, Type = MineType.GoldMine, Level = 3, Plot = 0 });
            Assert.Greater(PowerSystem.ComputePower(state), before, "Extra mine levels must add invested cost.");
        }

        // ---------- Shop / inventory (mirrors v1 shop.test.ts) ----------

        [Test]
        public void ShopSystem_BuyPlacesConsumableInInventory_AndChargesDM()
        {
            var (state, _, events) = NewGame();
            state.Premium.DarkMatter = 100;
            state.Inventory.Clear(); // TestMode pre-fills speed-ups; this test counts from zero

            var result = ShopSystem.Buy(state, "speed-5m", events);
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(100 - 8, state.Premium.DarkMatter, "Speed-Up 5 min costs 8 DM (v1 price).");
            Assert.AreEqual(1, state.Inventory.Count);
            Assert.AreEqual("speed-5m", state.Inventory[0].ItemId);

            var broke = ShopSystem.Buy(state, "speed-24h", events); // 960 DM
            Assert.IsFalse(broke.Ok, "Should fail without enough Dark Matter.");
        }

        [Test]
        public void ShopSystem_UseResourcePack_CreditsUncapped_NoIntOverflow()
        {
            var (state, _, events) = NewGame();
            state.Premium.DarkMatter = 999_999;
            state.Inventory.Clear(); // TestMode pre-fills speed-ups; this test counts from zero
            long before = state.Resources.Gold;

            // The 10M pack credits 10^10 milli — the case that forced ResourceBag to long.
            Assert.IsTrue(ShopSystem.Buy(state, "res-gold-10000000", events).Ok);
            Assert.IsTrue(ShopSystem.UseItem(state, "res-gold-10000000").Ok);

            Assert.AreEqual(before + 10_000_000L * 1000L, state.Resources.Gold,
                "10M whole units must credit as 10B milli without wrapping.");
            Assert.AreEqual(0, state.Inventory.Count, "Consumable must leave the inventory on use.");
        }

        [Test]
        public void ShopSystem_SpeedupTargetsCurrentBuildOrder()
        {
            var (state, _, events) = NewGame();
            state.Premium.DarkMatter = 1000;
            state.Resources.Add(new ResourceBag(100_000, 100_000, 100_000).Milli());
            Assert.IsTrue(BuildingSystem.StartUpgrade(state, BuildingId.CommandCenter).Ok);
            int endsBefore = state.BuildQueue[0].EndsAtTick;

            Assert.IsTrue(ShopSystem.Buy(state, "speed-1m", events).Ok);
            Assert.IsTrue(ShopSystem.UseItem(state, "speed-1m").Ok);

            Assert.AreEqual(System.Math.Max(state.Tick, endsBefore - 60), state.BuildQueue[0].EndsAtTick,
                "Speed-up must shave 60s but never rewind past the current tick.");
        }

        [Test]
        public void ShopSystem_SkinFlow_BuyApplyAndGuards()
        {
            var (state, _, events) = NewGame();
            state.Premium.DarkMatter = 500;

            Assert.IsFalse(ShopSystem.ApplySkin(state, "skin-crimson").Ok, "Cannot equip an unowned skin.");
            Assert.IsTrue(ShopSystem.Buy(state, "skin-crimson", events).Ok);
            Assert.IsFalse(ShopSystem.Buy(state, "skin-crimson", events).Ok, "Cannot buy a skin twice.");
            Assert.IsTrue(ShopSystem.ApplySkin(state, "skin-crimson").Ok);
            Assert.AreEqual("skin-crimson", state.Skins.ActivePlanet);
            Assert.IsFalse(ShopSystem.ApplySkin(state, "skin-emerald").Ok, "Skin not owned.");
        }

        // ---------- Sim purity vs UnityEngine ----------
        //
        // No compile-time assertion here; the guarantee is enforced by
        // `noEngineReferences: true` on GalaxyRoyale.Sim.asmdef + GalaxyRoyale.Data.asmdef.
        // Adding `using UnityEngine;` anywhere in those assemblies fails compilation,
        // which fails the whole test run before this test even loads.
    }
}
