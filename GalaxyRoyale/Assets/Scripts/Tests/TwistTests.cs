// Weekly galaxy twists (2026-09-30).
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class TwistTests
    {
        const int Week = Twists.WeekSec;

        static int WeekOf(TwistKind kind)
        {
            for (int w = 1; w <= Twists.Rotation.Count; w++)
                if (TwistSystem.At(w * Week).Kind == kind) return w;
            Assert.Fail($"{kind} isn't in the rotation");
            return 0;
        }

        [Test]
        public void TheFirstWeekIsPlain_ThenANewTwistEveryWeek_ForTenWeeks()
        {
            Assert.AreEqual(TwistKind.None, TwistSystem.At(0).Kind);
            Assert.AreEqual(TwistKind.None, TwistSystem.At(Week - 1).Kind);
            var seen = new HashSet<TwistKind>();
            for (int w = 1; w <= Twists.Rotation.Count; w++) Assert.IsTrue(seen.Add(TwistSystem.At(w * Week).Kind));
            Assert.AreEqual(10, seen.Count);
            Assert.AreEqual(TwistSystem.At(Week).Kind, TwistSystem.At((Twists.Rotation.Count + 1) * Week).Kind, "then it repeats");
        }

        [Test]
        public void TheTwists_BendTheirRules()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Buildings[BuildingId.ResearchLab].Level = 5;
            int Speed() => MarchSystem.PreviewMarch(s, new Dictionary<HullId, int> { [HullId.Fighter] = 1 },
                new TileXY(s.HomeTile.X + 50, s.HomeTile.Y)).TravelSec;
            int Ship() => FleetSystem.ShipBuildTime(s, HullId.Cruiser);
            int Build() => BuildingSystem.GetBuildTime(s, BuildingId.GoldMine, 5);
            long Prod() => ResourceSystem.GetRates(s).Total;

            s.Tick = 0;
            int speed = Speed(), ship = Ship(), build = Build();
            long prod = Prod();
            s.Tick = WeekOf(TwistKind.LowGravity) * Week;
            Assert.Less(Speed(), speed, "Low Gravity: faster flights");
            s.Tick = WeekOf(TwistKind.ShipwrightsWeek) * Week;
            Assert.Less(Ship(), ship);
            s.Tick = WeekOf(TwistKind.BuildersBoom) * Week;
            Assert.Less(Build(), build);
            s.Tick = WeekOf(TwistKind.SolarMaximum) * Week;
            Assert.Greater(Prod(), prod, "Solar Maximum (and the rates cache notices the week)");
            s.Tick = WeekOf(TwistKind.HuntersMoon) * Week;
            var moon = LairSystem.Reward(s, 0, 0).darkMatter;
            s.Tick = WeekOf(TwistKind.HuntersMoon) * Week + Week;
            Assert.Greater(moon, LairSystem.Reward(s, 0, 0).darkMatter);
        }

        [Test]
        public void ANewWeek_IsAnnouncedOnce_AndSaved()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            var events = new SimEventBus();
            var got = new List<TwistBegan>();
            events.Subscribe(e => { if (e is TwistBegan t) got.Add(t); });
            s.Tick = Week - 5;
            var engine = new TickEngine(s, events);
            engine.Advance(20);
            Assert.AreEqual(1, got.Count);
            Assert.AreEqual(TwistSystem.At(Week).Kind, got[0].Kind);
            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;
            var again = new List<TwistBegan>();
            var events2 = new SimEventBus();
            events2.Subscribe(e => { if (e is TwistBegan t) again.Add(t); });
            new TickEngine(back, events2).Advance(10);
            Assert.AreEqual(0, again.Count, "a reload doesn't announce it twice");
        }

        [Test]
        public void HuntersMoon_WakesABeatenLordAtOnce()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Campaign.LordWins[0] = 1;
            int moon = WeekOf(TwistKind.HuntersMoon) * Week;
            s.Campaign.NextRematchTick = moon + 3 * 24 * 3600;
            s.Tick = moon - 1;
            var events = new SimEventBus();
            var rose = new List<LordReturns>();
            events.Subscribe(e => { if (e is LordReturns r) rose.Add(r); });
            new TickEngine(s, events).Advance(120);
            Assert.AreEqual(1, rose.Count);
        }
    }
}
