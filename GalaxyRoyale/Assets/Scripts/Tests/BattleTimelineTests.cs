// Battle replay timeline: rebuilt from the report's per-round losses, it must
// start at the fleets that fought, only ever lose ships, and end exactly at
// the reported survivors — so the replay can't disagree with the report.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Combat;

namespace GalaxyRoyale.Sim.Tests
{
    public class BattleTimelineTests
    {
        static void AssertSame(Dictionary<HullId, int> expected, Dictionary<HullId, int> actual, string what)
        {
            foreach (var hull in Ships.All)
            {
                int e = expected.TryGetValue(hull, out var x) ? x : 0;
                int a = actual.TryGetValue(hull, out var y) ? y : 0;
                Assert.AreEqual(e, a, $"{what}: {hull}");
            }
        }

        static void AssertMatchesReport(BattleReport report)
        {
            var t = BattleTimeline.From(report);
            Assert.AreEqual(report.Rounds.Count, t.Rounds);
            AssertSame(report.Attacker, t.Attacker[0], "attacker start");
            AssertSame(report.Defender, t.Defender[0], "defender start");
            AssertSame(report.AttackerSurvivors, t.Attacker[t.Rounds], "attacker end");
            AssertSame(report.DefenderSurvivors, t.Defender[t.Rounds], "defender end");
            for (int r = 1; r <= t.Rounds; r++)
            {
                Assert.LessOrEqual(BattleTimeline.Count(t.Attacker[r]), BattleTimeline.Count(t.Attacker[r - 1]));
                Assert.LessOrEqual(BattleTimeline.Count(t.Defender[r]), BattleTimeline.Count(t.Defender[r - 1]));
            }
        }

        [TestCase(30, 10, 6, TestName = "Win with losses")]
        [TestCase(5, 0, 0, TestName = "Attacker wiped")]
        [TestCase(400, 0, 0, TestName = "Overwhelming force")]
        public void Timeline_EndsAtTheReportedSurvivors(int fighters, int bombers, int cruisers)
        {
            var attacker = new Dictionary<HullId, int>
            {
                [HullId.Fighter] = fighters, [HullId.Bomber] = bombers, [HullId.Cruiser] = cruisers,
            };
            var camp = new Dictionary<HullId, int>
            {
                [HullId.Fighter] = 20, [HullId.Bomber] = 8, [HullId.Cruiser] = 2,
            };
            AssertMatchesReport(CombatResolver.Resolve(attacker, camp));
        }

        [Test]
        public void MirrorMatch_LongSlog_StillLinesUp()
        {
            var fleet = new Dictionary<HullId, int>
            {
                [HullId.Fighter] = 60, [HullId.Bomber] = 25, [HullId.Cruiser] = 12, [HullId.Hauler] = 4,
            };
            var report = CombatResolver.Resolve(fleet, new Dictionary<HullId, int>(fleet));
            Assert.Greater(report.Rounds.Count, 2, "a real slog, not a one-round rout");
            AssertMatchesReport(report);
        }

        [Test]
        public void Deflection_HasNoRoundsToReplay()
        {
            var raiders = new Dictionary<HullId, int> { [HullId.Cruiser] = 40 };
            var report = new BattleReport
            {
                Attacker = raiders,
                AttackerSurvivors = new Dictionary<HullId, int>(raiders),
                Winner = BattleWinner.Defender,
            };
            var t = BattleTimeline.From(report);
            Assert.AreEqual(0, t.Rounds);
            Assert.AreEqual(40, BattleTimeline.Count(t.Attacker[0]));
        }
    }
}
