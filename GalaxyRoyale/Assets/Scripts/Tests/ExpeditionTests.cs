// Expeditions (2026-09-30): fleets beyond the map, the halfway call, the story.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class ExpeditionTests
    {
        const int Hour = 3600;

        static GameState Fresh()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.Buffs.ProtectionUntilTick = 0;
            s.Ships[HullId.Cruiser] = 200;
            s.Ships[HullId.Fighter] = 50;
            return s;
        }

        static GameState RoundTrip(GameState s) => SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;

        [Test]
        public void TheBoard_OffersThreeDestinations_ThatChangeEvery12Hours()
        {
            var s = Fresh();
            var board = ExpeditionSystem.Board(s);
            Assert.AreEqual(3, board.Count);
            Assert.AreEqual(3, board.Select(o => o.Code).Distinct().Count());
            CollectionAssert.AreEqual(board.Select(o => o.Code), ExpeditionSystem.Board(s).Select(o => o.Code), "stable within a window");
            s.Tick += ExpeditionSystem.WindowSec;
            Assert.AreNotEqual(board[0].Code, ExpeditionSystem.Board(s)[0].Code);
        }

        [Test]
        public void AnExpedition_TakesTheShips_AsksHalfway_AndComesHome()
        {
            var s = Fresh();
            var offer = ExpeditionSystem.Board(s)[0];
            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 50 };
            long might = PowerSystem.ComputePower(s);
            Assert.IsTrue(ExpeditionSystem.Send(s, offer, fleet, false, out int id).Ok);
            Assert.AreEqual(150, s.Ships[HullId.Cruiser], "off the docks");
            Assert.AreEqual(might, PowerSystem.ComputePower(s), "ships away still count for might");
            Assert.IsFalse(ExpeditionSystem.Send(s, offer, fleet, false, out _).Ok, "one fleet per destination");
            Assert.IsFalse(ExpeditionSystem.Decide(s, id, true).Ok, "nothing to decide yet");

            var events = new SimEventBus();
            var moments = new List<ExpeditionMoment>();
            var returns = new List<ExpeditionReturned>();
            events.Subscribe(e =>
            {
                if (e is ExpeditionMoment m) moments.Add(m);
                if (e is ExpeditionReturned r) returns.Add(r);
            });
            var engine = new TickEngine(s, events);
            engine.Advance(offer.Hours * Hour / 2);
            Assert.AreEqual(1, moments.Count, "the halfway moment is announced");
            Assert.AreEqual(1, RoundTrip(s).Expeditions.Count, "an expedition out survives the save");

            long before = s.Resources.Total;
            engine.Advance(offer.Hours * Hour / 2 + 1);
            Assert.AreEqual(1, returns.Count);
            var log = returns[0].Log;
            Assert.IsFalse(log.Bold, "no answer means the careful call");
            Assert.AreEqual(0, log.ShipsLost);
            Assert.AreEqual(200, s.Ships[HullId.Cruiser], "every ship home");
            Assert.Greater(s.Resources.Total, before);
            Assert.AreEqual(1, s.Stats.ExpeditionsDone);
            Assert.AreEqual(log.Story, RoundTrip(s).ExpeditionLog[0].Story);
        }

        [Test]
        public void ABoldCall_PaysMore_WhenItComesOff_AndCostsShipsWhenItDoesnt()
        {
            int wins = 0, losses = 0;
            long boldWinLoot = 0, safeLoot = 0;
            for (int seed = 1; seed <= 24; seed++)
            {
                foreach (bool bold in new[] { true, false })
                {
                    var s = GameState.CreateNewGame(seed, testMode: false);
                    s.Ships[HullId.Cruiser] = 30;
                    var offer = ExpeditionSystem.Board(s)[0];
                    ExpeditionSystem.Send(s, offer, new Dictionary<HullId, int> { [HullId.Cruiser] = 30 }, false, out int id);
                    var engine = new TickEngine(s, new SimEventBus());
                    engine.Advance(offer.Hours * Hour / 2);
                    Assert.IsTrue(ExpeditionSystem.Decide(s, id, bold).Ok);
                    engine.Advance(offer.Hours * Hour / 2 + 1);
                    var log = s.ExpeditionLog[0];
                    if (!bold) { safeLoot += log.LootMilli.Total / offer.Hours; continue; }
                    if (log.Won) { wins++; boldWinLoot += log.LootMilli.Total / offer.Hours; Assert.AreEqual(0, log.ShipsLost); }
                    else { losses++; Assert.Greater(log.ShipsLost, 0); }
                }
            }
            Assert.Greater(wins, 0);
            Assert.Greater(losses, 0, "bold calls can go wrong");
            Assert.Greater(boldWinLoot / wins, safeLoot / 24 * 1.5, "a bold win pays well over a careful trip");
        }

        [Test]
        public void TheCommander_CanLeadAnExpedition_ForBetterOdds()
        {
            var s = Fresh();
            s.Buildings[BuildingId.Academy].Level = 10;
            s.Commander.Level = 20;
            var offer = ExpeditionSystem.Board(s)[0];
            Assert.IsTrue(ExpeditionSystem.Send(s, offer, new Dictionary<HullId, int> { [HullId.Fighter] = 5 }, true, out int id).Ok);
            var exp = s.Expeditions.Single();
            Assert.IsTrue(exp.Led);
            Assert.AreEqual(-id, s.CaptainMarchId);
            var unled = new Expedition { Ships = exp.Ships, Recommended = exp.Recommended };
            Assert.Greater(ExpeditionSystem.BoldOdds(s, exp), ExpeditionSystem.BoldOdds(s, unled));
            AcademySystem.Tick(s, new SimEventBus());
            Assert.AreEqual(-id, s.CaptainMarchId, "not taken for a lost fleet while it's away");
            new TickEngine(s, new SimEventBus()).Advance(offer.Hours * Hour + 1);
            Assert.AreEqual(0, s.CaptainMarchId, "home again");
        }
    }
}
