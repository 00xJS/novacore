// Commander progression and difficulty (build-all plan, 2026-09-28): XP off the
// empire's record, levels and their rewards, the skill tree's points and tiers,
// skills feeding the research totals, resets, saves — and how the difficulty
// changes the way rivals treat the player.
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
    public class CommanderTests
    {
        static GameState Fresh(Difficulty difficulty = Difficulty.Standard) =>
            GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false, difficulty);

        /// <summary>Raise the commander to a level through the record (raids won are 40 XP each).</summary>
        static void LevelTo(GameState s, int level, SimEventBus? bus = null)
        {
            bus ??= new SimEventBus();
            while (s.Commander.Level < level)
            {
                s.Stats.RaidsWon++;
                CommanderSystem.Tick(s, bus);
            }
        }

        [Test]
        public void Levels_FollowTheCurve()
        {
            Assert.AreEqual(0, CommanderSystem.XpForLevel(1));
            Assert.AreEqual(25, CommanderSystem.XpForLevel(2));
            Assert.AreEqual(100, CommanderSystem.XpForLevel(3));
            Assert.AreEqual(1, CommanderSystem.LevelFor(24));
            Assert.AreEqual(2, CommanderSystem.LevelFor(25));
            Assert.AreEqual(3, CommanderSystem.LevelFor(100));
            Assert.AreEqual(CommanderSystem.MaxLevel, CommanderSystem.LevelFor(long.MaxValue / 4));
        }

        [Test]
        public void TheRecord_EarnsXp_AndEachLevelPays()
        {
            var s = Fresh();
            var bus = new SimEventBus();
            var ups = new List<CommanderLevelUp>();
            bus.Subscribe(e => { if (e is CommanderLevelUp up) ups.Add(up); });

            CommanderSystem.Tick(s, bus);
            Assert.AreEqual(15, s.Commander.Xp, "three level-1 buildings at the start: 3 × 5");
            Assert.AreEqual(1, s.Commander.Level);

            s.Stats.RaidsWon = 10; // +400
            int dmBefore = s.Premium.DarkMatter;
            CommanderSystem.Tick(s, bus);
            Assert.AreEqual(415, s.Commander.Xp);
            Assert.AreEqual(5, s.Commander.Level, "400 ≤ 415 < 625");
            Assert.AreEqual(1, ups.Count, "one event for the four levels");
            Assert.AreEqual(5, ups[0].Level);
            Assert.AreEqual(4, ups[0].Gained);
            Assert.AreEqual(10 * (2 + 3 + 4 + 5), ups[0].DarkMatter);
            Assert.AreEqual(10 * (2 + 3 + 4 + 5), s.Premium.DarkMatter - dmBefore);
            CollectionAssert.AreEqual(new[] { "speed-1h" }, ups[0].Items, "every fifth level adds an item");
            Assert.AreEqual(1, s.Inventory.Single(i => i.ItemId == "speed-1h").Count);
            Assert.AreEqual(4, CommanderSystem.PointsFree(s));

            // Higher building levels are worth more: Command Center 1 → 3 adds (2 + 3) × 5.
            s.Buildings[BuildingId.CommandCenter].Level = 3;
            CommanderSystem.Tick(s, bus);
            Assert.AreEqual(440, s.Commander.Xp);
        }

        [Test]
        public void OnlyANewHigh_EarnsXp()
        {
            var s = Fresh();
            var bus = new SimEventBus();
            s.Stats.ShipsBuilt = 100;
            CommanderSystem.Tick(s, bus);
            long xp = s.Commander.Xp;
            s.Stats.ShipsBuilt = 60; // (can't happen today, but a record must never pay twice)
            CommanderSystem.Tick(s, bus);
            s.Stats.ShipsBuilt = 100;
            CommanderSystem.Tick(s, bus);
            Assert.AreEqual(xp, s.Commander.Xp);
            s.Stats.ShipsBuilt = 101;
            CommanderSystem.Tick(s, bus);
            Assert.AreEqual(xp + 1, s.Commander.Xp);
        }

        [Test]
        public void Brutal_EarnsHalfAgainAsMuch_WithNothingLostToRounding()
        {
            var s = Fresh(Difficulty.Brutal);
            var bus = new SimEventBus();
            CommanderSystem.Tick(s, bus);
            long start = s.Commander.Xp;
            for (int i = 0; i < 10; i++)
            {
                s.Stats.ShipsBuilt++; // one XP at a time
                CommanderSystem.Tick(s, bus);
            }
            Assert.AreEqual(start + 15, s.Commander.Xp);
        }

        [Test]
        public void Skills_NeedPoints_AndOpenTierByTier()
        {
            var s = Fresh();
            LevelTo(s, 5); // 4 points
            Assert.AreEqual(4, CommanderSystem.PointsFree(s));

            Assert.IsFalse(CommanderSystem.Learn(s, "site-foremen").Ok, "tier 2 needs 3 points in Industry");
            for (int i = 0; i < 3; i++) Assert.IsTrue(CommanderSystem.Learn(s, "prospector").Ok);
            Assert.IsFalse(CommanderSystem.Learn(s, "prospector").Ok, "rank 3 is the most");
            Assert.IsTrue(CommanderSystem.TierOpen(s, SkillBranch.Industry, 2));
            Assert.IsFalse(CommanderSystem.TierOpen(s, SkillBranch.Admiralty, 2), "points count per branch");
            Assert.IsTrue(CommanderSystem.Learn(s, "site-foremen").Ok);
            Assert.AreEqual(0, CommanderSystem.PointsFree(s));
            var none = CommanderSystem.Learn(s, "salvage-crews");
            Assert.IsFalse(none.Ok);
            StringAssert.Contains("No skill points", none.Reason);
            Assert.IsFalse(CommanderSystem.Learn(s, "no-such-skill").Ok);
        }

        [Test]
        public void Skills_FeedTheResearchTotals()
        {
            var s = Fresh();
            s.Buildings[BuildingId.GoldMine].Level = 5;
            long goldBefore = ResourceSystem.GetRates(s).Gold;
            long vaultBefore = ResourceSystem.GetProtected(s).Gold;
            float prodBefore = ResearchSystem.ProdMultiplier(s);
            LevelTo(s, 20);

            for (int i = 0; i < 3; i++) Assert.IsTrue(CommanderSystem.Learn(s, "prospector").Ok);
            Assert.AreEqual(prodBefore + 0.12f, ResearchSystem.ProdMultiplier(s), 1e-4);
            Assert.AreEqual(goldBefore * 1.12, ResourceSystem.GetRates(s).Gold, goldBefore * 0.002);

            // Bastion: 6 points open tier 3 — the Orbital Gunners add battery levels.
            for (int i = 0; i < 3; i++) Assert.IsTrue(CommanderSystem.Learn(s, "home-guard").Ok);
            for (int i = 0; i < 3; i++) Assert.IsTrue(CommanderSystem.Learn(s, "vault-wardens").Ok);
            Assert.AreEqual(0, ResearchSystem.BatteryLevel(s));
            Assert.IsTrue(CommanderSystem.Learn(s, "orbital-gunners").Ok);
            Assert.AreEqual(1, ResearchSystem.BatteryLevel(s));
            Assert.AreEqual(1.12f, ResearchSystem.DefenseMods(s).AtkMult, 1e-4, "home guard: +12% defending damage");
            Assert.AreEqual(vaultBefore * 1.36, ResourceSystem.GetProtected(s).Gold, vaultBefore * 0.002);

            // Time reductions share research's floor.
            float before = ResearchSystem.BuildTimeMult(s);
            for (int i = 0; i < 3; i++) Assert.IsTrue(CommanderSystem.Learn(s, "site-foremen").Ok);
            Assert.AreEqual(before - 0.12f, ResearchSystem.BuildTimeMult(s), 1e-4);

            // Rivals have no skills: their totals don't move.
            var rival = Fresh();
            Assert.AreEqual(1f, ResearchSystem.ProdMultiplier(rival), 1e-6);
        }

        [Test]
        public void Respec_HandsEveryPointBack_TheFirstForFree()
        {
            var s = Fresh();
            LevelTo(s, 6);
            s.Premium.DarkMatter = 1000;
            Assert.IsFalse(CommanderSystem.Respec(s).Ok, "nothing to reset yet");
            CommanderSystem.Learn(s, "gunnery-drills");
            CommanderSystem.Learn(s, "hull-riveters");
            Assert.AreEqual(0, CommanderSystem.RespecCost(s));
            Assert.IsTrue(CommanderSystem.Respec(s).Ok);
            Assert.AreEqual(1000, s.Premium.DarkMatter);
            Assert.AreEqual(5, CommanderSystem.PointsFree(s));
            CommanderSystem.Learn(s, "gunnery-drills");
            Assert.AreEqual(CommanderSystem.RespecDarkMatter, CommanderSystem.RespecCost(s));
            Assert.IsTrue(CommanderSystem.Respec(s).Ok);
            Assert.AreEqual(1000 - CommanderSystem.RespecDarkMatter, s.Premium.DarkMatter);
        }

        [Test]
        public void AnOlderSave_StartsAtTheLevelItsHistoryEarned()
        {
            var s = Fresh();
            s.Stats.RaidsWon = 30;
            s.Stats.CampsCleared = 40;
            s.QuestStep = 11;
            var json = SaveCodec.EncodeState(s);
            json.Remove("commander"); // written before commanders existed
            var loaded = SaveCodec.DecodeState(json);
            Assert.AreEqual(1, loaded.Commander.Level);
            CommanderSystem.Tick(loaded, new SimEventBus());
            Assert.AreEqual(CommanderSystem.LevelFor(CommanderSystem.Score(loaded)), loaded.Commander.Level);
            Assert.Greater(loaded.Commander.Level, 5);
        }

        [Test]
        public void CommanderAndDifficulty_SurviveASave()
        {
            var s = Fresh(Difficulty.Brutal);
            LevelTo(s, 8);
            s.Stats.ShipsBuilt++;
            CommanderSystem.Tick(s, new SimEventBus()); // leaves half an XP carried
            CommanderSystem.Learn(s, "gunnery-drills");
            CommanderSystem.Learn(s, "bulwark");
            CommanderSystem.Respec(s);
            CommanderSystem.Learn(s, "prospector");
            var json = SaveCodec.EncodeState(s);
            ((Dictionary<string, object?>)((Dictionary<string, object?>)json["commander"]!)["skills"]!)["retired-skill"] = 2L;

            var back = SaveCodec.DecodeState(json);
            Assert.AreEqual(Difficulty.Brutal, back.Difficulty);
            Assert.AreEqual(s.Commander.Xp, back.Commander.Xp);
            Assert.AreEqual(s.Commander.ScoreSeen, back.Commander.ScoreSeen);
            Assert.AreEqual(s.Commander.XpCarry, back.Commander.XpCarry);
            Assert.AreEqual(s.Commander.Level, back.Commander.Level);
            Assert.AreEqual(1, back.Commander.Respecs);
            CollectionAssert.AreEquivalent(new Dictionary<string, int> { ["prospector"] = 1 }, back.Commander.Skills,
                "an unknown skill is dropped and its points come back");
            Assert.AreEqual(Difficulty.Standard, SaveCodec.DecodeState(SaveCodec.EncodeState(Fresh())).Difficulty);
        }

        [Test]
        public void Easy_VaultsHoldHalfAgainAsMuch()
        {
            var easy = Fresh(Difficulty.Easy);
            var standard = Fresh();
            Assert.AreEqual(ResourceSystem.GetProtected(standard).Gold * 1.5, ResourceSystem.GetProtected(easy).Gold, 1);
        }

        /// <summary>The same galaxy for a day, three times: rival raids (and scans) aimed
        /// at a rich, undefended player grow from Easy to Standard to Brutal.</summary>
        [Test]
        public void Difficulty_SetsHowHardRivalsLeanOnYou()
        {
            int Launches(Difficulty difficulty)
            {
                var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 60);
                var player = Fresh(difficulty);
                player.Buffs.ProtectionUntilTick = 0; // an established colony
                foreach (var id in Buildings.All) player.Buildings[id].Level = 4;
                player.Resources = new ResourceBag(5_000_000, 5_000_000, 5_000_000).Milli();
                var bus = new SimEventBus();
                var seen = new HashSet<int>();
                for (int t = 60; t <= 24 * 3600; t += 60)
                {
                    player.Tick = t;
                    BotSystem.Advance(player, galaxy, bus);
                    foreach (var a in galaxy.Inbound) seen.Add(a.Id);
                }
                TestContext.Out.WriteLine($"PACE difficulty {Difficulties.Name(difficulty)}: {seen.Count} raids and scans at you in a day");
                return seen.Count;
            }
            int easy = Launches(Difficulty.Easy), standard = Launches(Difficulty.Standard), brutal = Launches(Difficulty.Brutal);
            Assert.Greater(standard, easy);
            Assert.Greater(brutal, standard);
        }
    }
}
