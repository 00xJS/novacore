// Map events (2026-09-30): the Comet Pass, the Trade Caravan, the Ion Storm and
// the Supernova Warning put something on the map near the colony.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class MapEventTests
    {
        const int Hour = 3600;

        /// <summary>The first tick of the next occurrence of <paramref name="kind"/>.</summary>
        static int StartOf(GalaxyEventKind kind)
        {
            for (int t = GalaxyEvents.LeadInSec; t < GalaxyEvents.LeadInSec + EventSystem.CycleSec; t += Hour)
                if (EventSystem.Current(t).Def.Kind == kind) return EventSystem.Current(t).StartTick;
            Assert.Fail($"{kind} isn't in the rotation");
            return 0;
        }

        /// <summary>A colony at <paramref name="tick"/> with the engine run one tick so the sites appear.</summary>
        static (GameState s, TickEngine engine, SimEventBus events) At(int tick)
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Buffs.ProtectionUntilTick = 0;
            s.Resources = new ResourceBag(1_000_000, 1_000_000, 1_000_000).Milli();
            s.Tick = tick - 1;
            var events = new SimEventBus();
            var engine = new TickEngine(s, events);
            engine.Advance(1);
            return (s, engine, events);
        }

        static MapNode Site(GameState s) => s.Map.DynamicNodes.Single(n => n.Id == s.EventSiteId);

        static GameState RoundTrip(GameState s) => SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;

        [Test]
        public void EventRewards_KeepPaceWithTheColony()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            var def = GalaxyEvents.Rotation[0];
            long small = EventSystem.RewardMilli(s, def).Total;
            foreach (var id in new[] { BuildingId.GoldMine, BuildingId.QuartzExtractor, BuildingId.HeliumRefinery })
                s.Buildings[id].Level = 15;
            long grown = EventSystem.RewardMilli(s, def).Total / 1000;
            Assert.Greater(grown, 200_000, "a grown colony's event pays hundreds of thousands");
            Assert.Greater(grown * 1000, small * 10);
        }

        [Test]
        public void EveryMapEvent_IsInTheRotation()
        {
            foreach (var k in new[] { GalaxyEventKind.CometPass, GalaxyEventKind.TradeCaravan,
                         GalaxyEventKind.IonStorm, GalaxyEventKind.Supernova })
                Assert.Greater(StartOf(k), 0);
        }

        [Test]
        public void TheComet_ArrivesNearby_AndLeavesWhenTheEventEnds()
        {
            int start = StartOf(GalaxyEventKind.CometPass);
            var (s, engine, _) = At(start);
            var comet = Site(s);
            Assert.AreEqual(NodeKind.Comet, comet.Kind);
            double d = TileXY.Distance(comet.Tile, s.HomeTile);
            Assert.That(d, Is.InRange(GalaxyEvents.CometMinDist - 3, GalaxyEvents.CometMaxDist + 3));
            Assert.AreSame(comet, MapLookup.NodeAt(s, comet.Tile));
            Assert.AreEqual(comet.Id, RoundTrip(s).EventSiteId, "the save knows it");

            engine.Advance(EventSystem.Current(s.Tick).EndTick - s.Tick + 1);
            Assert.IsNull(s.Map.DynamicNodes.Find(n => n.Id == comet.Id), "gone with its event");
        }

        [Test]
        public void GatheringTheComet_HaulsEveryResource_AndDarkMatter_AndCountsForTheGoal()
        {
            int start = StartOf(GalaxyEventKind.CometPass);
            var (s, engine, _) = At(start);
            var comet = Site(s);
            s.Ships[HullId.Hauler] = 5;
            var fleet = new Dictionary<HullId, int> { [HullId.Hauler] = 5 };
            Assert.IsTrue(MarchSystem.SendMarch(s, fleet, comet.Tile, MarchMission.Gather, out int id).Ok);
            long gold = s.Resources.Gold, quartz = s.Resources.Quartz, helium = s.Resources.Helium;
            int dm = s.Premium.DarkMatter;
            engine.Advance(4 * Hour);
            Assert.IsFalse(s.Marches.Exists(m => m.Id == id), "home again");
            Assert.Greater(s.Resources.Gold, gold);
            Assert.Greater(s.Resources.Quartz, quartz);
            Assert.Greater(s.Premium.DarkMatter, dm, "comets carry Dark Matter");
            Assert.Greater(s.Stats.CometHauled, 0);
            var (have, _) = EventSystem.Progress(s);
            Assert.Greater(have, 0, "the event's goal counts the haul");
        }

        [Test]
        public void RivalsMineTheComet_Hourly()
        {
            int start = StartOf(GalaxyEventKind.CometPass);
            var (s, engine, _) = At(start);
            var comet = Site(s);
            engine.Advance(3 * Hour);
            Assert.Less(s.Map.NodeOverrides[comet.Id].Remaining!.Value, comet.Amount);
        }

        [Test]
        public void TheCaravan_MovesBetweenWaystations()
        {
            int start = StartOf(GalaxyEventKind.TradeCaravan);
            var (s, engine, _) = At(start);
            var first = Site(s);
            Assert.AreEqual(NodeKind.Caravan, first.Kind);
            Assert.IsTrue(MapLookup.IsLive(s, first), "a caravan holds no stock but is live");
            engine.Advance(GalaxyEvents.CaravanStopSec);
            var second = Site(s);
            Assert.AreNotEqual(first.Id, second.Id);
            Assert.IsNull(s.Map.DynamicNodes.Find(n => n.Id == first.Id));
        }

        [Test]
        public void InterceptingTheCaravan_FightsItsEscort_AndTakesItsCargo()
        {
            int start = StartOf(GalaxyEventKind.TradeCaravan);
            var (s, engine, _) = At(start);
            var caravan = Site(s);
            s.Ships[HullId.Cruiser] = 200;
            s.Ships[HullId.Hauler] = 20;
            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 200, [HullId.Hauler] = 20 };
            Assert.IsTrue(MarchSystem.SendMarch(s, fleet, caravan.Tile, MarchMission.Attack, out int id).Ok);
            long before = s.Resources.Total;
            engine.Advance(3 * Hour);
            Assert.AreEqual(1, s.Stats.CaravansDone);
            Assert.IsTrue(s.Map.NodeOverrides[caravan.Id].Cleared);
            Assert.Greater(s.Resources.Total, before, "the cargo came home");
            Assert.IsTrue(s.Mailbox.OfType<BattleMailReport>().Any(m => m.Subject.StartsWith("Caravan intercepted")));
        }

        [Test]
        public void EscortingTheCaravan_PaysWhenItMovesOn()
        {
            int start = StartOf(GalaxyEventKind.TradeCaravan);
            var (s, engine, events) = At(start);
            var caravan = Site(s);
            var paid = new List<CaravanEscorted>();
            events.Subscribe(e => { if (e is CaravanEscorted c) paid.Add(c); });
            s.Ships[HullId.Fighter] = 400;
            var fleet = new Dictionary<HullId, int> { [HullId.Fighter] = 400 };
            Assert.IsTrue(MarchSystem.SendMarch(s, fleet, caravan.Tile, MarchMission.Gather, out int id).Ok);
            engine.Advance(Hour);
            Assert.AreEqual(MarchPhase.Gathering, s.Marches.Find(m => m.Id == id)!.Phase, "riding with it");
            engine.Advance(GalaxyEvents.CaravanStopSec);
            Assert.AreEqual(1, paid.Count);
            Assert.Greater(paid[0].PayMilli.Total, 0);
            Assert.AreEqual(1, s.Stats.CaravansDone);
            Assert.AreNotEqual(MarchPhase.Gathering, s.Marches.Find(m => m.Id == id)?.Phase ?? MarchPhase.Returning,
                "the escort heads home");
        }

        [Test]
        public void TheIonStorm_SlowsFleets_AndBlindsTheRadar()
        {
            int start = StartOf(GalaxyEventKind.IonStorm);
            var (s, _, _) = At(start);
            var zone = EventSites.ZoneNow(s)!.Value;
            Assert.AreEqual(GalaxyEventKind.IonStorm, zone.Kind);
            Assert.IsTrue(zone.Contains(s.HomeTile), "it covers the colony");
            s.Buildings[BuildingId.RadarStation].Level = 5;
            Assert.AreEqual(0, RadarSystem.WarnLeadSeconds(s));

            var fleet = new Dictionary<HullId, int> { [HullId.Fighter] = 1 };
            s.Ships[HullId.Fighter] = 1;
            var target = new TileXY(s.HomeTile.X + 60, s.HomeTile.Y);
            int stormy = MarchSystem.PreviewMarch(s, fleet, target).TravelSec;
            s.Tick = EventSystem.Current(s.Tick).EndTick + 1; // the storm has passed
            int clear = MarchSystem.PreviewMarch(s, fleet, target).TravelSec;
            Assert.Greater(stormy, clear);
            Assert.Greater(RadarSystem.WarnLeadSeconds(s), 0);
        }

        [Test]
        public void TheSupernova_SparesTheColony_SpeedsItsWorlds_ThenDestroysWhatsLeft()
        {
            int start = StartOf(GalaxyEventKind.Supernova);
            var (s, engine, events) = At(start);
            var zone = EventSites.ZoneNow(s)!.Value;
            Assert.IsFalse(zone.Contains(s.HomeTile), "the star never takes the colony");
            var world = MapLookup.AllNodes(s).FirstOrDefault(n => zone.Contains(n.Tile) && n.Resource != null
                && MapLookup.IsLive(s, n));
            Assert.IsNotNull(world, "the doomed sector has worlds to strip");

            // A fleet parked there when the star goes off is lost.
            s.Ships[HullId.Hauler] = 3;
            var fleet = new Dictionary<HullId, int> { [HullId.Hauler] = 3 };
            int end = EventSystem.Current(s.Tick).EndTick;
            s.Tick = end - 2 * Hour;
            Assert.IsTrue(MarchSystem.SendMarch(s, fleet, world!.Tile, MarchMission.Gather, out int id).Ok);
            var lost = new List<SupernovaDetonated>();
            events.Subscribe(e => { if (e is SupernovaDetonated d) lost.Add(d); });
            engine.Advance(2 * Hour + 2);
            Assert.AreEqual(1, lost.Count);
            if (s.Marches.Exists(m => m.Id == id && m.Phase != MarchPhase.Returning))
                Assert.Fail("a fleet still in the sector survived");
            Assert.IsFalse(MapLookup.IsLive(s, world), "its worlds are stripped");
            Assert.AreEqual(-1, s.PendingNova);
        }
    }
}
