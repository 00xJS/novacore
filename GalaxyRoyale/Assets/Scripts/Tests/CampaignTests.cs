// The campaign "The Long Night" and the Pirate Lords (2026-09-30).
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class CampaignTests
    {
        const int Day = 24 * 3600;

        static (GameState s, TickEngine engine, SimEventBus events) Colony(int cc, int day)
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Buffs.ProtectionUntilTick = 0;
            s.Buildings[BuildingId.CommandCenter].Level = cc;
            s.Resources = new ResourceBag(5_000_000, 5_000_000, 5_000_000).Milli();
            s.Tick = day * Day - 1;
            var events = new SimEventBus();
            var engine = new TickEngine(s, events);
            engine.Advance(61); // the campaign looks once a minute
            return (s, engine, events);
        }

        static GameState RoundTrip(GameState s) => SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;

        /// <summary>Send a fleet big enough to flatten the lair and wait for the battle.</summary>
        static void Storm(GameState s, TickEngine engine, MapNode lair)
        {
            long budget = LairSystem.Parse(lair).budget;
            int n = (int)(budget * 3 / Ships.Defs[HullId.Vanguard].Cost.Total) + 10;
            s.Ships[HullId.Vanguard] = (s.Ships.TryGetValue(HullId.Vanguard, out var v) ? v : 0) + n;
            var fleet = new Dictionary<HullId, int> { [HullId.Vanguard] = n };
            Assert.IsTrue(MarchSystem.SendMarch(s, fleet, lair.Tile, MarchMission.Attack, out _).Ok);
            engine.Advance(6 * 3600);
        }

        [Test]
        public void TheChapters_AreWellFormed_AndSpreadAcrossTheLateGame()
        {
            Assert.AreEqual(10, Campaign.Chapters.Count);
            int lastDay = -1, lastCc = 0;
            var lords = new HashSet<int>();
            foreach (var ch in Campaign.Chapters)
            {
                Assert.Greater(ch.UnlockDay, lastDay, $"chapter {ch.Number} opens after the one before");
                Assert.GreaterOrEqual(ch.UnlockCc, lastCc);
                Assert.IsTrue(lords.Add(ch.Lord), "a new lord every chapter");
                Assert.AreEqual(1, ch.Objectives.Count(o => o.Goal == CampaignGoal.DefeatLord && true));
                Assert.LessOrEqual(ch.Intro.Length, 420, $"chapter {ch.Number}'s briefing fits the card");
                lastDay = ch.UnlockDay;
                lastCc = ch.UnlockCc;
            }
            Assert.GreaterOrEqual(Campaign.Chapters[^1].UnlockDay, 60, "the saga reaches deep into the late game");
        }

        [Test]
        public void AChapter_WaitsForItsDayAndCommandCenter_ThenOpensWithALair()
        {
            var (s, _, _) = Colony(cc: 4, day: 3);
            Assert.IsFalse(s.Campaign.Open, "CC 4 is too young");
            StringAssert.Contains("Command Center 5", CampaignSystem.Blocker(s));

            var (s2, _, events) = Colony(cc: 5, day: 1);
            Assert.IsFalse(s2.Campaign.Open, "day 1 is too early");

            var (s3, _, _) = Colony(cc: 5, day: 2);
            Assert.IsTrue(s3.Campaign.Open);
            var lair = LairSystem.Find(s3, s3.Campaign.LairId);
            Assert.IsNotNull(lair, "the Rust Queen's lair is on the map");
            Assert.AreEqual(NodeKind.Camp, lair!.Kind);
            Assert.AreSame(lair, MapLookup.NodeAt(s3, lair.Tile));
            Assert.AreEqual("Grisha Vane, the Rust Queen", LairSystem.LordOf(lair).FullName);
            var g = MarchSystem.CampGarrison(lair);
            Assert.Greater(g[HullId.Fighter], g[HullId.Bomber], "a swarm doctrine");
        }

        [Test]
        public void Objectives_CountFromTheChaptersStart()
        {
            var (s, engine, _) = Colony(cc: 5, day: 2);
            s.Stats.CampsCleared += 3; // (before the chapter these wouldn't have counted)
            Assert.AreEqual(3, CampaignSystem.Progress(s, 0).have);
            var fresh = GameState.CreateNewGame(42, testMode: false);
            fresh.Stats.CampsCleared = 50;
            fresh.Buildings[BuildingId.CommandCenter].Level = 5;
            fresh.Tick = 2 * Day - 1;
            new TickEngine(fresh, new SimEventBus()).Advance(61);
            Assert.AreEqual(0, CampaignSystem.Progress(fresh, 0).have, "camps from before the chapter don't count");
        }

        [Test]
        public void BeatingTheLord_Pays_BringsARelic_AndTheChapterClaims()
        {
            var (s, engine, events) = Colony(cc: 5, day: 2);
            var got = new List<SimEvent>();
            events.Subscribe(e => got.Add(e));
            var lair = LairSystem.Find(s, s.Campaign.LairId)!;
            int dm = s.Premium.DarkMatter;
            Storm(s, engine, lair);

            var fell = got.OfType<LordDefeated>().Single();
            Assert.AreEqual(0, fell.Lord);
            Assert.AreEqual(RelicKind.AncientDrill, fell.Relic);
            Assert.AreEqual(1, RelicSystem.Count(s, RelicKind.AncientDrill));
            Assert.Greater(s.Premium.DarkMatter, dm);
            Assert.IsNull(LairSystem.Find(s, lair.Id), "the lair leaves the map");
            Assert.IsTrue(CampaignSystem.Done(s, 2));
            Assert.IsTrue(s.Mailbox.OfType<BattleMailReport>().Any(m => m.Report.DefenderName == "Grisha Vane, the Rust Queen"));

            Assert.IsFalse(CampaignSystem.CanClaim(s), "camps and battles still to do");
            s.Stats.CampsCleared += 5;
            s.Stats.BattlesWon += 8;
            engine.Advance(60);
            Assert.IsTrue(got.OfType<ChapterReady>().Any());
            int before = s.Premium.DarkMatter;
            Assert.IsTrue(CampaignSystem.Claim(s, events).Ok);
            Assert.AreEqual(before + Campaign.Chapters[0].RewardDM, s.Premium.DarkMatter);
            Assert.AreEqual(1, s.Campaign.Chapter);
            Assert.IsFalse(s.Campaign.Open, "chapter 2 waits for day 6 and CC 7");
            Assert.IsFalse(CampaignSystem.Claim(s).Ok, "claimed once");
        }

        [Test]
        public void ALair_OutfightsTheColonysFleetAsItStands_ButNotForever()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Ships[HullId.Fighter] = 400;
            s.Ships[HullId.Cruiser] = 40;
            var fleet = new Dictionary<HullId, int> { [HullId.Fighter] = 400, [HullId.Cruiser] = 40 };
            var mods = ResearchSystem.CombatMods(s);
            var lair = LairSystem.Garrison(0, LairSystem.Budget(s, 0, 0));
            Assert.AreNotEqual(Combat.BattleWinner.Attacker, Combat.BattleForecast.Predict(fleet, lair, mods).Winner,
                "the whole fleet today isn't enough");
            var grown = new Dictionary<HullId, int> { [HullId.Fighter] = 800, [HullId.Cruiser] = 80 };
            Assert.AreEqual(Combat.BattleWinner.Attacker, Combat.BattleForecast.Predict(grown, lair, mods).Winner,
                "twice the fleet wins it");
        }

        [Test]
        public void ABeatenLord_ReturnsStronger_AFewDaysLater()
        {
            var (s, engine, events) = Colony(cc: 5, day: 2);
            var rose = new List<LordReturns>();
            events.Subscribe(e => { if (e is LordReturns r) rose.Add(r); });
            Storm(s, engine, LairSystem.Find(s, s.Campaign.LairId)!);
            engine.Advance(LairSystem.RematchEverySec);
            Assert.AreEqual(1, rose.Count);
            Assert.AreEqual(0, rose[0].Lord);
            Assert.AreEqual(1, rose[0].Tier);
            var again = LairSystem.Find(s, s.Campaign.RematchId)!;
            Assert.AreEqual(1, LairSystem.Parse(again).tier);
            Assert.Greater(LairSystem.Budget(s, 0, 1), LairSystem.Budget(s, 0, 0), "a rematch is the bigger fight");
            Storm(s, engine, again);
            Assert.AreEqual(2, LairSystem.Wins(s, 0));
            Assert.AreEqual("", s.Campaign.RematchId);
        }

        [Test]
        public void TheCampaign_SurvivesASave()
        {
            var (s, engine, _) = Colony(cc: 5, day: 2);
            Storm(s, engine, LairSystem.Find(s, s.Campaign.LairId)!);
            s.Stats.CampsCleared += 2;
            var back = RoundTrip(s);
            var c = back.Campaign;
            Assert.IsTrue(c.Open);
            Assert.AreEqual(s.Campaign.Baseline, c.Baseline);
            Assert.AreEqual(1, LairSystem.Wins(back, 0));
            Assert.AreEqual(s.Campaign.NextRematchTick, c.NextRematchTick);
            Assert.AreEqual(CampaignSystem.Progress(s, 0), CampaignSystem.Progress(back, 0));
            Assert.IsTrue(CampaignSystem.Done(back, 2));
        }

        [Test]
        public void TheLastChapter_ClosesTheSaga_AndItsAwardsUnlock()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Campaign.Chapter = Campaign.Chapters.Count;
            for (int i = 0; i < 10; i++) s.Campaign.LordWins[i] = 1;
            Assert.IsTrue(CampaignSystem.Finished(s));
            Assert.IsNull(CampaignSystem.Current(s));
            var unlocked = AchievementSystem.CheckNew(s).Select(a => a.Id).ToList();
            CollectionAssert.IsSubsetOf(new[] { "lord-breaker", "court-breaker", "nightwalker", "dawnbringer" }, unlocked);
        }
    }
}
