// Regression tests for the 2026-09-27 bug sweep:
//  * a camp battle that WIPES the fleet removed its march from state.Marches
//    while MarchSystem.Tick was enumerating that list — InvalidOperationException,
//    which during offline catch-up aborted the load (and every relaunch replayed it);
//  * the camp-battle event fired BEFORE its report was filed, so the UI (which
//    pops Mailbox[0] on BattleResolved) showed the previous battle's report;
//  * defense reports must remember the player was DEFENDING, so the mailbox can
//    show a lost raid as a defeat — including reports saved before the flag.
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
    public class MarchWipeTests
    {
        /// <summary>One Fighter vs a level-5 pirate camp (80/40/20): a certain wipe.</summary>
        static (GameState state, TileXY camp) DoomedRaid()
        {
            var s = GameState.CreateNewGame(42);
            s.Ships[HullId.Fighter] = 1;
            s.Resources.Helium = 100_000_000;
            var tile = new TileXY(s.HomeTile.X + 6, s.HomeTile.Y);
            if (MapLookup.NodeAt(s, tile) != null) tile = new TileXY(s.HomeTile.X + 7, s.HomeTile.Y + 1);
            s.Map.DynamicNodes.Add(new MapNode
            {
                Id = "dyn-doom", Kind = NodeKind.Camp, Tile = tile,
                Tier = 3, Amount = 0, RatePerSec = 0, Resource = null, CampLevel = 5,
            });
            return (s, tile);
        }

        [Test]
        public void CampWipe_DuringTick_DoesNotThrow_AndDropsTheMarch()
        {
            var (state, camp) = DoomedRaid();
            var engine = new TickEngine(state, new SimEventBus());
            var res = MarchSystem.SendMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 1 }, camp, MarchMission.Attack, out int id);
            Assert.IsTrue(res.Ok, res.Reason);
            int arrive = state.Marches.Find(m => m.Id == id)!.ArrivesAtTick;

            Assert.DoesNotThrow(() => engine.Advance(arrive - state.Tick + 1),
                "a wiped fleet must not break the march loop");
            Assert.IsFalse(state.Marches.Exists(m => m.Id == id), "the wiped march is removed");
            Assert.AreEqual(1, state.Stats.BattlesLost);
        }

        [Test]
        public void CampWipe_DuringOfflineCatchUp_CompletesAndUnmutesEvents()
        {
            var (state, camp) = DoomedRaid();
            var events = new SimEventBus();
            var engine = new TickEngine(state, events);
            MarchSystem.SendMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 1 }, camp, MarchMission.Attack, out _);

            // Two hours "away": the wipe happens mid catch-up.
            var summary = SaveManager.ApplyOfflineProgress(state, engine, events, 0, 2 * 3600 * 1000L);
            Assert.AreEqual(2 * 3600, summary.ElapsedSec, "catch-up ran the full window");
            Assert.IsFalse(events.Suppressed, "events are live again after catch-up");
            Assert.AreEqual(0, state.Marches.Count);
        }

        [Test]
        public void BattleResolved_FiresAfterItsReportIsFiled()
        {
            // A battle the player WINS (no wipe), so this isolates the event order
            // from the enumeration crash above.
            var (state, camp) = DoomedRaid();
            state.Map.DynamicNodes.Find(n => n.Id == "dyn-doom")!.CampLevel = 1; // 5 Fighters
            state.Ships[HullId.Cruiser] = 40;
            // A stale report already in the mailbox — the bug popped THIS one.
            state.Mailbox.Insert(0, new BattleMailReport { Id = 999, Subject = "older battle" });
            var events = new SimEventBus();
            var engine = new TickEngine(state, events);
            BattleReport? fired = null;
            MailItem? topAtEvent = null;
            events.Subscribe(e =>
            {
                if (e is not BattleResolved br) return;
                fired = br.Report;
                topAtEvent = state.Mailbox.Count > 0 ? state.Mailbox[0] : null;
            });

            var send = MarchSystem.SendMarch(state,
                new Dictionary<HullId, int> { [HullId.Cruiser] = 40 }, camp, MarchMission.Attack, out int id);
            Assert.IsTrue(send.Ok, send.Reason);
            engine.Advance(state.Marches.Find(m => m.Id == id)!.ArrivesAtTick - state.Tick + 1);

            Assert.IsNotNull(fired, "the camp battle emitted BattleResolved");
            Assert.AreEqual(BattleWinner.Attacker, fired!.Winner, "setup: the player wins this one");
            Assert.IsInstanceOf<BattleMailReport>(topAtEvent);
            Assert.AreSame(fired, ((BattleMailReport)topAtEvent!).Report,
                "Mailbox[0] at event time is THIS battle's report");
        }

        [Test]
        public void DefendingFlag_RoundTrips_AndLegacyDefenseSubjectsAreRecognised()
        {
            var state = GameState.CreateNewGame(42);
            state.Mailbox.Add(new BattleMailReport
            {
                Id = 1, Subject = "Colony raided by Plasma Karen", Defending = true,
                Report = new BattleReport { Winner = BattleWinner.Attacker },
            });
            // Written before the flag existed: no "defending" key, recognised by subject.
            state.Mailbox.Add(new BattleMailReport
            {
                Id = 2, Subject = "Raid repelled — Moon Moon", Defending = false,
                Report = new BattleReport { Winner = BattleWinner.Defender },
            });
            state.Mailbox.Add(new BattleMailReport
            {
                Id = 3, Subject = "Victory at 10,10 — Pirate Camp", Defending = false,
                Report = new BattleReport { Winner = BattleWinner.Attacker },
            });

            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000))).State;
            Assert.IsTrue(((BattleMailReport)back.Mailbox[0]).Defending, "flag round-trips");
            Assert.IsTrue(((BattleMailReport)back.Mailbox[1]).Defending, "legacy defense subject upgraded");
            Assert.IsFalse(((BattleMailReport)back.Mailbox[2]).Defending, "your own attacks stay attacks");
        }
    }
}
