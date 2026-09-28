// Progression systems (user request 2026-09-28): timed galaxy events, seasons,
// achievements + titles, and alliances with simulated commanders.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class GalaxyEventTests
    {
        const int Hour = 3600;

        const int LeadIn = GalaxyEvents.LeadInSec;

        [Test]
        public void Rotation_FollowsGalaxyTime()
        {
            Assert.AreEqual(7 * 24 * Hour, EventSystem.CycleSec, "the rotation spans one week");
            Assert.AreEqual(GalaxyEventKind.None, EventSystem.Current(0).Def.Kind, "a new galaxy starts quiet");
            Assert.AreEqual(GalaxyEventKind.GoldRush, EventSystem.Current(LeadIn).Def.Kind);
            Assert.AreEqual(GalaxyEventKind.ResearchSurge, EventSystem.Current(LeadIn + 48 * Hour).Def.Kind);
            Assert.AreEqual(GalaxyEventKind.PirateArmada, EventSystem.Current(LeadIn + 72 * Hour).Def.Kind);
            Assert.AreEqual(GalaxyEventKind.WarGames, EventSystem.Current(LeadIn + 120 * Hour).Def.Kind);
            var nextWeek = EventSystem.Current(LeadIn + EventSystem.CycleSec + 5);
            Assert.AreEqual(GalaxyEventKind.GoldRush, nextWeek.Def.Kind);
            Assert.AreEqual(4, nextWeek.Instance, "every occurrence has its own number");
            Assert.AreEqual(LeadIn + EventSystem.CycleSec, nextWeek.StartTick);
            Assert.AreEqual(GalaxyEventKind.GoldRush, EventSystem.Next(0).Def.Kind, "the chip can say what's first");
        }

        [Test]
        public void LeadIn_CoversTheRivalsHeadStart()
        {
            Assert.GreaterOrEqual(LeadIn, BotSystem.MaxPreSimTicks,
                "no event may boost the bots' pre-simulated opening");
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Stats.UpgradesDone = 9;
            EventSystem.Tick(s);
            Assert.AreEqual(-1, s.EventInstance);
            Assert.IsFalse(EventSystem.CanClaim(s), "nothing to claim while it's quiet");
            Assert.AreEqual(1f, EventSystem.ProductionMult(s));
        }

        [Test]
        public void Goal_CountsFromTheEventsStart_AndPaysOnce()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Tick = LeadIn; // Gold Rush
            s.Stats.UpgradesDone = 5; // upgrades from before the event don't count
            EventSystem.Tick(s);
            Assert.AreEqual((0L, 3L), EventSystem.Progress(s));
            Assert.IsFalse(EventSystem.Claim(s).Ok);

            s.Stats.UpgradesDone += 3;
            Assert.IsTrue(EventSystem.CanClaim(s));
            long gold = s.Resources.Gold;
            int dm = s.Premium.DarkMatter;
            Assert.IsTrue(EventSystem.Claim(s).Ok);
            var def = EventSystem.Current(s.Tick).Def;
            Assert.AreEqual(gold + def.Reward.Gold * 1000L, s.Resources.Gold);
            Assert.AreEqual(dm + def.RewardDM, s.Premium.DarkMatter);
            Assert.AreEqual(1, s.Stats.EventsCompleted);
            Assert.IsFalse(EventSystem.Claim(s).Ok, "one reward per event");
        }

        [Test]
        public void NextEvent_ResetsTheGoal()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Tick = LeadIn;
            EventSystem.Tick(s);
            s.Stats.UpgradesDone = 3;
            Assert.IsTrue(EventSystem.Claim(s).Ok);

            s.Tick = LeadIn + 48 * Hour; // Research Surge
            s.Stats.ResearchDone = 7;
            EventSystem.Tick(s);
            Assert.IsFalse(s.EventClaimed);
            Assert.AreEqual(7, s.EventBaseline);
            Assert.AreEqual((0L, 2L), EventSystem.Progress(s));
        }

        [Test]
        public void TickEngine_CapturesTheBaselineAtTheRealStart()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Tick = LeadIn - 2;
            new TickEngine(s, new SimEventBus()).Advance(3);
            Assert.AreEqual(0, s.EventInstance, "the first event is tracked from its first tick");
        }

        [Test]
        public void GoldRush_RaisesProduction_ForEveryEmpire()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Buildings[BuildingId.GoldMine].Level = 5;
            s.Tick = 1; // quiet lead-in
            long normal = ResourceSystem.GetRates(s).Gold;
            s.Tick = LeadIn + 1; // Gold Rush
            long rush = ResourceSystem.GetRates(s).Gold;
            Assert.Greater(normal, 0);
            Assert.AreEqual(normal * GalaxyEvents.GoldRushProduction, rush, normal * 0.01);
        }

        [Test]
        public void ResearchSurge_ShortensNewResearch()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Tick = 0; // quiet — no research effect
            int normal = ResearchSystem.GetResearchTime(s, TechId.YieldOptimization, 1);
            s.Tick = LeadIn + 50 * Hour; // Research Surge
            int surge = ResearchSystem.GetResearchTime(s, TechId.YieldOptimization, 1);
            Assert.AreEqual(normal * GalaxyEvents.ResearchSurgeTime, surge, 2.0);
        }
    }

    public class SeasonTests
    {
        static BotGalaxy Galaxy() => BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 3);

        [Test]
        public void FirstTick_StartsSeasonOne()
        {
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            var galaxy = Galaxy();
            Assert.IsNull(SeasonSystem.Tick(player, galaxy));
            Assert.AreEqual(1, player.Season);
            Assert.AreEqual(PowerSystem.ComputePower(player), player.SeasonStartMight);
            foreach (var bot in galaxy.Bots) Assert.AreEqual(bot.CachedMight, bot.SeasonStartMight);
        }

        [Test]
        public void Board_RanksGains_NotSize()
        {
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            var galaxy = Galaxy();
            SeasonSystem.Tick(player, galaxy);
            // The rivals grew nothing; the player built a squadron.
            player.Ships[HullId.Fighter] = (player.Ships.TryGetValue(HullId.Fighter, out var f) ? f : 0) + 50;
            Assert.Greater(SeasonSystem.PlayerGain(player), 0);
            Assert.AreEqual(1, SeasonSystem.PlayerRank(player, galaxy),
                "a young colony that grew beats giants that didn't");
        }

        [Test]
        public void SeasonEnd_PaysByRank_RecordsIt_AndStartsTheNext()
        {
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            var galaxy = Galaxy();
            SeasonSystem.Tick(player, galaxy);
            player.Ships[HullId.Fighter] = (player.Ships.TryGetValue(HullId.Fighter, out var f) ? f : 0) + 50;
            int dm = player.Premium.DarkMatter;

            player.Tick = SeasonSystem.SeasonSec; // season 2 begins
            var record = SeasonSystem.Tick(player, galaxy);

            Assert.IsNotNull(record);
            Assert.AreEqual(1, record!.Season);
            Assert.AreEqual(1, record.Rank);
            Assert.AreEqual(galaxy.Bots.Count + 1, record.Of);
            Assert.AreEqual(SeasonSystem.RewardFor(1), record.RewardDM);
            Assert.AreEqual(dm + record.RewardDM, player.Premium.DarkMatter);
            Assert.AreEqual(1, player.Stats.BestSeasonRank);
            Assert.AreEqual(2, player.Season);
            Assert.AreEqual(0, SeasonSystem.PlayerGain(player), "the new season starts from zero");
            Assert.AreEqual(1, player.SeasonHistory.Count);
            Assert.IsNull(SeasonSystem.Tick(player, galaxy), "settled once");
        }

        [Test]
        public void Rewards_ShrinkDownTheBoard()
        {
            Assert.Greater(SeasonSystem.RewardFor(1), SeasonSystem.RewardFor(2));
            Assert.Greater(SeasonSystem.RewardFor(3), SeasonSystem.RewardFor(4));
            Assert.Greater(SeasonSystem.RewardFor(10), SeasonSystem.RewardFor(11));
            Assert.Greater(SeasonSystem.RewardFor(50), SeasonSystem.RewardFor(51));
            Assert.Greater(SeasonSystem.RewardFor(250), 0, "everyone who plays a season gets something");
        }
    }

    public class AchievementTests
    {
        [Test]
        public void Definitions_AreUniqueAndPay()
        {
            var ids = new HashSet<string>();
            foreach (var a in Achievements.All)
            {
                Assert.IsTrue(ids.Add(a.Id), $"duplicate id {a.Id}");
                Assert.Greater(a.RewardDM, 0, a.Id);
                Assert.Greater(a.Target, 0, a.Id);
            }
        }

        [Test]
        public void CheckNew_UnlocksAndPaysExactlyOnce()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            Assert.IsEmpty(AchievementSystem.CheckNew(s), "a fresh commander has earned nothing");
            s.Stats.BattlesWon = 1;
            var first = AchievementSystem.CheckNew(s);
            Assert.AreEqual(new[] { "first-blood" }, first.Select(a => a.Id).ToArray());
            Assert.AreEqual(Achievements.ById("first-blood")!.RewardDM, s.Premium.DarkMatter);
            Assert.IsEmpty(AchievementSystem.CheckNew(s));
        }

        [Test]
        public void Titles_NeedTheirAchievement()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            Assert.IsFalse(AchievementSystem.EquipTitle(s, "veteran").Ok, "locked titles can't be worn");
            Assert.IsFalse(AchievementSystem.EquipTitle(s, "first-blood").Ok, "not every award has a title");
            s.Stats.BattlesWon = 25;
            AchievementSystem.CheckNew(s);
            Assert.IsTrue(AchievementSystem.EquipTitle(s, "veteran").Ok);
            Assert.AreEqual("Veteran", AchievementSystem.TitleText(s));
            Assert.IsTrue(AchievementSystem.EquipTitle(s, null).Ok);
            Assert.IsNull(AchievementSystem.TitleText(s));
        }

        [Test]
        public void SeasonAwards_ReadTheBestFinish()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Stats.BestSeasonRank = 7;
            var got = AchievementSystem.CheckNew(s).Select(a => a.Id).ToList();
            Assert.Contains("contender", got);
            Assert.IsFalse(got.Contains("champion"));
            s.Stats.BestSeasonRank = 1;
            Assert.AreEqual(new[] { "champion" }, AchievementSystem.CheckNew(s).Select(a => a.Id).ToArray());
        }

        [Test]
        public void Counters_TickFromTheSystems()
        {
            var s = GameState.CreateNewGame(42, testMode: true);
            var started = BuildingSystem.StartUpgrade(s, BuildingId.CommandCenter);
            Assert.IsTrue(started.Ok, started.Reason);
            new TickEngine(s, new SimEventBus()).Advance(s.BuildQueue[0].EndsAtTick - s.Tick + 1);
            Assert.AreEqual(1, s.Stats.UpgradesDone, "a finished upgrade counts");
        }
    }

    public class ProgressionSaveTests
    {
        [Test]
        public void EverythingSurvivesASave()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 3);
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.Stats.CampsCleared = 4;
            s.Stats.RaidsWon = 3;
            s.Stats.DefensesWon = 2;
            s.Stats.ShipsBuilt = 120;
            s.Stats.UpgradesDone = 9;
            s.Stats.ResearchDone = 6;
            s.Stats.EventsCompleted = 1;
            s.Stats.LootMilli = 5_000_000_000L; // past int range
            s.Stats.BestSeasonRank = 12;
            s.Achievements.Add("first-blood");
            s.Achievements.Add("veteran");
            s.Title = "veteran";
            s.EventInstance = 3;
            s.EventBaseline = 17;
            s.EventClaimed = true;
            s.Season = 2;
            s.SeasonStartMight = 4321;
            s.SeasonHistory.Add(new SeasonRecord { Season = 1, Rank = 12, Of = 4, Gain = 999, RewardDM = 120 });
            galaxy.Bots[1].SeasonStartMight = 777;

            var file = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000, galaxy)));
            var b = file.State;
            Assert.AreEqual(4, b.Stats.CampsCleared);
            Assert.AreEqual(3, b.Stats.RaidsWon);
            Assert.AreEqual(2, b.Stats.DefensesWon);
            Assert.AreEqual(120, b.Stats.ShipsBuilt);
            Assert.AreEqual(9, b.Stats.UpgradesDone);
            Assert.AreEqual(6, b.Stats.ResearchDone);
            Assert.AreEqual(1, b.Stats.EventsCompleted);
            Assert.AreEqual(5_000_000_000L, b.Stats.LootMilli);
            Assert.AreEqual(12, b.Stats.BestSeasonRank);
            CollectionAssert.AreEquivalent(s.Achievements, b.Achievements);
            Assert.AreEqual("veteran", b.Title);
            Assert.AreEqual(3, b.EventInstance);
            Assert.AreEqual(17, b.EventBaseline);
            Assert.IsTrue(b.EventClaimed);
            Assert.AreEqual(2, b.Season);
            Assert.AreEqual(4321, b.SeasonStartMight);
            Assert.AreEqual(1, b.SeasonHistory.Count);
            Assert.AreEqual(12, b.SeasonHistory[0].Rank);
            Assert.AreEqual(999, b.SeasonHistory[0].Gain);
            Assert.AreEqual(777, file.Bots!.Bots[1].SeasonStartMight);
        }

        [Test]
        public void OldSaves_LoadWithFreshProgression()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            var b = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;
            Assert.AreEqual(0, b.Achievements.Count);
            Assert.IsNull(b.Title);
            Assert.AreEqual(-1, b.EventInstance);
            Assert.AreEqual(0, b.Season);
            Assert.AreEqual(0, b.ClanId);
        }
    }
}
