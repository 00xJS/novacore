// "While you were away" debrief: counts come from mail filed during the
// catch-up (sequential ids) plus the player-sim events it collected.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class OfflineDebriefTests
    {
        static BattleMailReport Defense(int id, BattleWinner winner, bool withRounds, long lootGold = 0) => new()
        {
            Id = id,
            Defending = true,
            Report = new BattleReport
            {
                Winner = winner,
                Rounds = withRounds ? new List<RoundLog> { new() { Round = 1 } } : new List<RoundLog>(),
                Loot = lootGold > 0 ? new ResourceBag(lootGold, 0, 0) : null,
            },
        };

        [Test]
        public void Build_CountsOnlyMailFiledDuringTheCatchUp()
        {
            var state = GameState.CreateNewGame(42);
            // Filed BEFORE the catch-up (id 1) — must be ignored.
            state.Mailbox.Add(Defense(1, BattleWinner.Attacker, withRounds: true, lootGold: 999_000));
            int mailIdBefore = 2;
            state.Mailbox.Add(Defense(2, BattleWinner.Attacker, withRounds: true, lootGold: 45_000)); // raided
            state.Mailbox.Add(Defense(3, BattleWinner.Defender, withRounds: true));                    // repelled
            state.Mailbox.Add(Defense(4, BattleWinner.Defender, withRounds: false));                   // Aegis deflected
            state.Mailbox.Add(new RadarWarning { Id = 5, Subject = "RADAR ALERT — scan" });            // spy sweep
            state.Mailbox.Add(new BattleMailReport                                                     // your own win
            {
                Id = 6,
                Report = new BattleReport { Winner = BattleWinner.Attacker },
            });

            var summary = new OfflineSummary
            {
                ElapsedSec = 3 * 3600,
                Events = new SimEvent[]
                {
                    new BuildingCompleted(BuildingId.GoldMine, 2),
                    new ResearchCompleted(TechId.IonThrusters, 1),
                    new ShipsCompleted(HullId.Fighter, 1),
                    new ShipsCompleted(HullId.Fighter, 1),
                    new MarchReturned(7, new ResourceBag(10_000, 5_000, 0)),
                },
            };

            var d = OfflineDebrief.Build(state, summary, mailIdBefore, rankBefore: 1, bots: null);

            Assert.AreEqual(5, d.NewMail);
            Assert.AreEqual(1, d.RaidsSuffered);
            Assert.AreEqual(45_000, d.LootLostMilli, "only the in-window raid's loot counts");
            Assert.AreEqual(1, d.RaidsRepelled);
            Assert.AreEqual(1, d.RaidsDeflected);
            Assert.AreEqual(1, d.SpyScans);
            Assert.AreEqual(1, d.BattlesWon);
            Assert.AreEqual(1, d.Upgrades);
            Assert.AreEqual(1, d.Research);
            Assert.AreEqual(2, d.ShipsBuilt);
            Assert.AreEqual(1, d.FleetsHome);
            Assert.AreEqual(15_000, d.CargoHomeMilli);
            Assert.IsTrue(d.Notable);
        }

        [Test]
        public void QuietNight_IsNotNotable()
        {
            var state = GameState.CreateNewGame(42);
            var d = OfflineDebrief.Build(state, new OfflineSummary { ElapsedSec = 600 },
                state.NextReportId, rankBefore: 1, bots: null);
            Assert.IsFalse(d.Notable, "nothing happened → the resources toast, not a full report");
        }

        [Test]
        public void OfflineCampWipe_ShowsUpAsALostBattle()
        {
            var state = GameState.CreateNewGame(42);
            state.Ships[HullId.Fighter] = 1;
            state.Resources.Helium = 100_000_000;
            var tile = new TileXY(state.HomeTile.X + 6, state.HomeTile.Y);
            if (MapLookup.NodeAt(state, tile) != null) tile = new TileXY(state.HomeTile.X + 7, state.HomeTile.Y + 1);
            state.Map.DynamicNodes.Add(new MapNode
            {
                Id = "dyn-debrief", Kind = NodeKind.Camp, Tile = tile,
                Tier = 3, Amount = 0, RatePerSec = 0, Resource = null, CampLevel = 5,
            });
            var events = new SimEventBus();
            var engine = new TickEngine(state, events);
            Assert.IsTrue(MarchSystem.SendMarch(state,
                new Dictionary<HullId, int> { [HullId.Fighter] = 1 }, tile, MarchMission.Attack, out _).Ok);

            int mailIdBefore = state.NextReportId;
            var summary = SaveManager.ApplyOfflineProgress(state, engine, events, 0, 2 * 3600 * 1000L);
            var d = OfflineDebrief.Build(state, summary, mailIdBefore, rankBefore: 1, bots: null);

            Assert.AreEqual(1, d.BattlesLost, "the camp wipe during the catch-up is reported");
            Assert.AreEqual(0, d.FleetsHome, "a wiped fleet never comes home");
        }
    }
}
