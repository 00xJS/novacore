// Fleet expansion (2026-07-05, approved ships.md proposal): regenerating
// shields in combat, the 8 rebranded hulls, tech-req unlock gating, v15 codec.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class FleetExpansionTests
    {
        [Test]
        public void Shields_AbsorbSwarmFire_Completely()
        {
            // 20 Fighters put out 600 raw vs 10 Aegis projecting 1,200 shield —
            // every round's damage dies on the shield wall; nobody scratches hull.
            var fighters = new Dictionary<HullId, int> { [HullId.Fighter] = 20 };
            var wall = new Dictionary<HullId, int> { [HullId.Aegis] = 10 };
            var report = CombatResolver.Resolve(fighters, wall);
            Assert.AreEqual(10, report.DefenderSurvivors[HullId.Aegis],
                "the shield wall regenerates faster than the swarm can burn it");
        }

        [Test]
        public void Shields_HeavyFire_PunchesThrough()
        {
            // 30 Cruisers: 2,100 raw − 1,200 shields = 900/round through to hull.
            var cruisers = new Dictionary<HullId, int> { [HullId.Cruiser] = 30 };
            var wall = new Dictionary<HullId, int> { [HullId.Aegis] = 10 };
            var report = CombatResolver.Resolve(cruisers, wall);
            int survivors = report.DefenderSurvivors.TryGetValue(HullId.Aegis, out var s) ? s : 0;
            Assert.Less(survivors, 10, "concentrated fire overflows the shields");
        }

        [Test]
        public void ShieldMultMods_FlipAMirrorMatch()
        {
            // Cruiser mirror: 700 raw − 400 shields = 300 through each way.
            // Tripled attacker shields (400→1200) absorb everything → attacker wins.
            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 10 };
            var mods = new FleetMods(1f, 1f, shieldMult: 3f);
            var report = CombatResolver.Resolve(fleet, new Dictionary<HullId, int>(fleet), mods);
            Assert.AreEqual(BattleWinner.Attacker, report.Winner);
        }

        [Test]
        public void WithinTierTriangle_IsARockPaperScissorsRing()
        {
            // Interceptor tier: Talon > Sentinel > Harrier > Talon. Each counter
            // (2× damage) must beat an equal count of the sibling it targets.
            (HullId a, HullId b)[] ring =
            {
                (HullId.Talon, HullId.Sentinel),
                (HullId.Sentinel, HullId.Harrier),
                (HullId.Harrier, HullId.Talon),
            };
            foreach (var (a, b) in ring)
            {
                Assert.AreEqual(b, Ships.Defs[a].Counters, $"{a} should counter {b}");
                var atkFleet = new Dictionary<HullId, int> { [a] = 12 };
                var defFleet = new Dictionary<HullId, int> { [b] = 12 };
                var report = CombatResolver.Resolve(atkFleet, defFleet);
                Assert.AreEqual(BattleWinner.Attacker, report.Winner,
                    $"{a} (counters {b}) must beat an equal stack of {b}");
            }
        }

        [Test]
        public void ShipQueue_RunsTwoParallelLines_AndBuffsUnlockThirdSlots()
        {
            // 2026-07-08 pacing standardization: two parallel queues everywhere;
            // the shop buffs unlock a THIRD build/research slot.
            var state = GameState.CreateNewGame(42);
            var events = new SimEventBus();
            var engine = new TickEngine(state, events);
            state.Resources = new ResourceBag(100_000, 100_000, 100_000).Milli();
            state.Buildings[BuildingId.Shipyard].Level = 1;

            Assert.IsTrue(FleetSystem.QueueShips(state, HullId.Fighter, 2).Ok);
            Assert.IsTrue(FleetSystem.QueueShips(state, HullId.Hauler, 1).Ok);
            // Fighter builds in 5 s, Hauler in 12 s — both lines run at once.
            engine.Advance(6);
            Assert.AreEqual(1, state.Ships[HullId.Fighter], "line 1 built its first fighter");
            engine.Advance(8); // hauler line (anchored t=1, 12 s) lands inside this window
            Assert.AreEqual(1, state.Ships[HullId.Hauler], "line 2 built the hauler IN PARALLEL");
            Assert.AreEqual(2, state.Ships[HullId.Fighter], "line 1 finished its batch meanwhile");

            Assert.AreEqual(Balance.BaseBuildSlots, BuildingSystem.BuildSlots(state));
            state.Buffs.ExtraBuildSlotUntilTick = state.Tick + 1000;
            Assert.AreEqual(3, BuildingSystem.BuildSlots(state), "Overdrive unlocks the third build line");
            Assert.AreEqual(Balance.BaseResearchSlots, ResearchSystem.ResearchSlots(state));
            state.Buffs.ExtraResearchSlotUntilTick = state.Tick + 1000;
            Assert.AreEqual(3, ResearchSystem.ResearchSlots(state), "Overclock unlocks the third tech line");
        }

        [Test]
        public void QueueShips_GatesOnTechReqs()
        {
            var state = GameState.CreateNewGame(42);
            state.Buildings[BuildingId.Shipyard].Level = 7;

            var res = FleetSystem.QueueShips(state, HullId.Vanguard, 1);
            Assert.IsFalse(res.Ok);
            StringAssert.Contains("Weapons Calibration", res.Reason);
            Assert.AreEqual(0, FleetSystem.MaxBuildable(state, HullId.Vanguard));

            state.Research[TechId.WeaponsCalibration] = 5;
            Assert.IsTrue(FleetSystem.QueueShips(state, HullId.Vanguard, 1).Ok, "tech met → buildable");
        }

        [Test]
        public void SaveCodec_RoundTripsNewHulls_AndOldSavesDefaultToZero()
        {
            var state = GameState.CreateNewGame(42);
            state.Ships[HullId.Talon] = 7;
            state.Ships[HullId.Reaper] = 2;
            var back = SaveManager.Unwrap(
                SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000))))!.Value.state;
            Assert.AreEqual(7, back.Ships[HullId.Talon]);
            Assert.AreEqual(2, back.Ships[HullId.Reaper]);

            // Pre-v15 save shape: strip the new keys → decode to zero, no throw.
            var encoded = SaveCodec.EncodeState(GameState.CreateNewGame(42));
            var ships = (Dictionary<string, object?>)encoded["ships"]!;
            foreach (var key in new[] { "talon", "vanguard", "lancer", "leviathan",
                "reaper", "atlas", "aegis", "scavenger" })
                ships.Remove(key);
            var old = SaveCodec.DecodeState(encoded);
            Assert.AreEqual(0, old.Ships[HullId.Reaper]);
        }

        [Test]
        public void SaveCodec_RoundTripsTriangleHulls()
        {
            var state = GameState.CreateNewGame(42);
            state.Ships[HullId.Sentinel] = 4;
            state.Ships[HullId.Wraith] = 3;
            state.Ships[HullId.Corsair] = 9;
            var back = SaveManager.Unwrap(
                SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000))))!.Value.state;
            Assert.AreEqual(4, back.Ships[HullId.Sentinel]);
            Assert.AreEqual(3, back.Ships[HullId.Wraith]);
            Assert.AreEqual(9, back.Ships[HullId.Corsair]);
        }

        [Test]
        public void EveryCombatTier_FormsAClosedCounterRing()
        {
            // Each combat Class must be a clean 3-cycle: A→B→C→A, no dangling counter.
            foreach (var cls in new[] { "Strike Craft", "Interceptors", "Battleships",
                "Corvettes", "Dreadnoughts", "Destroyers" })
            {
                var tier = new List<HullId>();
                foreach (var h in Ships.All)
                    if (Ships.Defs[h].Class == cls) tier.Add(h);
                Assert.AreEqual(3, tier.Count, $"{cls} must have exactly 3 hulls");
                // Following Counters from any member visits all three and returns home.
                var seen = new HashSet<HullId>();
                var cur = tier[0];
                for (int i = 0; i < 3; i++)
                {
                    Assert.IsTrue(seen.Add(cur), $"{cls} counter chain revisited {cur} early");
                    var next = Ships.Defs[cur].Counters;
                    Assert.IsTrue(next.HasValue && tier.Contains(next.Value),
                        $"{cur} must counter a sibling in {cls}");
                    cur = next!.Value;
                }
                Assert.AreEqual(tier[0], cur, $"{cls} ring must close on itself");
            }
        }
    }
}
