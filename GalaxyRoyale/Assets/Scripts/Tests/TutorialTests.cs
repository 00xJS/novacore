// The new-commander training (2026-09-30). The playthrough is a rookie who does
// only what the training's current step says, as soon as it can be done, on an
// honest STANDARD start and on a TESTING galaxy — every step has to be doable
// with what the game has handed over by then, and none may leave the player
// waiting long. TUTORIAL lines in the output show how long each step took.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class TutorialTests
    {
        const int StepSec = 5;

        sealed class Run
        {
            public readonly Dictionary<string, int> SecondsOnStep = new();
            public readonly List<string> Blocked = new();
            public int Finished = -1;
        }

        static Run Play(bool testMode)
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode);
            s.HomeTile = Spawn.SpawnTileFor("player-tutorial", Spawn.GalaxySeed);
            var engine = new TickEngine(s, new SimEventBus());
            var run = new Run();
            MapNode? camp = null;

            while (s.Tick < 3 * 3600)
            {
                TutorialSystem.Check(s);
                var step = TutorialSystem.Current(s);
                if (step == null) { run.Finished = s.Tick; break; }
                run.SecondsOnStep[step.Id] = run.SecondsOnStep.TryGetValue(step.Id, out var t) ? t + StepSec : StepSec;

                string? why = Act(s, step, ref camp);
                if (why != null && !run.Blocked.Contains($"{step.Id}: {why}")) run.Blocked.Add($"{step.Id}: {why}");
                TutorialSystem.Check(s);
                if (TutorialSystem.Current(s) == step) engine.Advance(StepSec);
            }
            return run;
        }

        /// <summary>What the step asks for; returns why it couldn't be done right now (null = done or waiting).</summary>
        static string? Act(GameState s, TutorialStepDef step, ref MapNode? camp)
        {
            SimResult Build(BuildingId id)
            {
                if (s.BuildQueue.Any(o => o.Building == id && o.MineId == null)) return SimResult.Success; // under way
                var check = BuildingSystem.CheckUpgrade(s, id);
                return check.Ok ? BuildingSystem.StartUpgrade(s, id) : check;
            }

            switch (step.Id)
            {
                case "build-quartz":
                case "command-center":
                {
                    var r = Build(step.Building);
                    return r.Ok ? null : r.Reason;
                }
                case "queues":
                    TutorialSystem.Notice(s, TutorialSeen.Queues);
                    return null;
                case "claim":
                    if (QuestSystem.Current(s) is { } q && QuestSystem.IsComplete(s, q)) QuestSystem.Claim(s);
                    return null; // waiting on the Quartz Extractor
                case "speed-up":
                {
                    int i = s.BuildQueue.FindIndex(o => o.Building == BuildingId.CommandCenter && o.EndsAtTick > 0);
                    if (i < 0) return null; // already done
                    var use = ShopSystem.ConsumeItem(s, Tutorial.SpeedupItem);
                    if (!use.Ok) return "no speed-up to use: " + use.Reason;
                    BuildingSystem.SpeedUpBuildOrder(s, i, 300);
                    return null;
                }
                case "path":
                {
                    var quest = QuestSystem.Current(s);
                    if (quest == null) return "the path ran out before the Shipyard";
                    if (QuestSystem.IsComplete(s, quest)) { QuestSystem.Claim(s); return null; }
                    if (quest.Goal != QuestGoal.BuildingLevel) return $"quest '{quest.Title}' isn't a building";
                    if (s.BuildQueue.Any(o => o.Building == quest.Building)) return null; // waiting on the timer
                    var r = Build(quest.Building);
                    return r.Ok ? null : $"{quest.Title}: {r.Reason}";
                }
                case "fleet":
                    TutorialSystem.Notice(s, TutorialSeen.Fleet);
                    return null;
                case "map":
                    TutorialSystem.Notice(s, TutorialSeen.Map);
                    return null;
                case "find-camp":
                    camp = NearestCamp(s);
                    if (camp == null) return "no pirate camp to find";
                    TutorialSystem.Notice(s, TutorialSeen.Camp);
                    return null;
                case "spy":
                {
                    if (s.Marches.Any(m => m.Mission == MarchMission.Spy)) return null; // probe en route
                    camp ??= NearestCamp(s);
                    var r = MarchSystem.SendMarch(s, new Dictionary<HullId, int> { [HullId.Probe] = 1 }, camp!.Tile,
                        MarchMission.Spy, out _);
                    return r.Ok ? null : "spy: " + r.Reason;
                }
                case "intel":
                    TutorialSystem.Notice(s, TutorialSeen.SpyReport);
                    return null;
                case "attack":
                {
                    if (s.Marches.Any(m => m.Mission == MarchMission.Attack)) return null; // flying
                    camp ??= NearestCamp(s);
                    // ALL DOCKED (every docked ship but probes), then ATTACK — what the step says.
                    var fleet = s.Ships.Where(kv => kv.Value > 0 && kv.Key != HullId.Probe).ToDictionary(kv => kv.Key, kv => kv.Value);
                    if (fleet.Count == 0) return "no ships docked to attack with";
                    var r = MarchSystem.SendMarch(s, fleet, camp!.Tile, MarchMission.Attack, out _);
                    return r.Ok ? null : "attack: " + r.Reason;
                }
                case "report":
                    TutorialSystem.Notice(s, TutorialSeen.BattleReport);
                    return null;
                case "spoils":
                    while (QuestSystem.Current(s) is { } done && QuestSystem.IsComplete(s, done)) QuestSystem.Claim(s);
                    return null;
                case "research":
                {
                    // The coach points at CLAIM first whenever a Path reward is ready.
                    while (QuestSystem.Current(s) is { } ready && QuestSystem.IsComplete(s, ready)) QuestSystem.Claim(s);
                    if (s.Buildings[BuildingId.ResearchLab].Level < 1)
                    {
                        var r = Build(BuildingId.ResearchLab);
                        return r.Ok ? null : "lab: " + r.Reason;
                    }
                    if (s.Buildings[BuildingId.ResearchLab].Level < 1) return null;
                    // The technology the coach spotlights.
                    if (TutorialSystem.SuggestedTech(s) is not { } pick)
                        return "no technology can be started: " + ResearchSystem.CheckResearch(s, Techs.All[0]).Reason;
                    var start = ResearchSystem.StartResearch(s, pick);
                    return start.Ok ? null : "research: " + start.Reason;
                }
                case "more":
                    TutorialSystem.Notice(s, TutorialSeen.More);
                    return null;
                default:
                    Assert.AreEqual(TutorialGoal.Next, step.Goal, $"step '{step.Id}' has no action in the playthrough");
                    TutorialSystem.Next(s);
                    return null;
            }
        }

        static MapNode? NearestCamp(GameState s) => MapLookup.AllNodes(s)
            .Where(n => n.Kind == NodeKind.Camp
                && !(s.Map.NodeOverrides.TryGetValue(n.Id, out var o) && o.Cleared))
            .OrderBy(n => TileXY.Distance(n.Tile, s.HomeTile))
            .FirstOrDefault();

        static void Report(string label, Run run)
        {
            string M(int sec) => sec >= 60 ? $"{sec / 60}m{sec % 60:00}s" : $"{sec}s";
            TestContext.Out.WriteLine($"TUTORIAL {label}: finished at {M(run.Finished)} · " +
                string.Join("  ", Tutorial.Steps.Select(st => $"{st.Id} {M(run.SecondsOnStep.TryGetValue(st.Id, out var t) ? t : 0)}")));
            foreach (var b in run.Blocked) TestContext.Out.WriteLine($"TUTORIAL {label} blocked: {b}");
        }

        [Test]
        public void ARookie_FollowingOnlyTheTraining_FinishesIt_OnAnHonestStart()
        {
            var run = Play(testMode: false);
            Report("standard", run);
            Assert.GreaterOrEqual(run.Finished, 0, "the training can be finished");
            Assert.Less(run.Finished, 25 * 60, "…in under 25 minutes of play");
            foreach (var kv in run.SecondsOnStep)
                Assert.LessOrEqual(kv.Value, 6 * 60, $"step '{kv.Key}' keeps the player waiting too long");
            Assert.IsEmpty(run.Blocked, "no step asks for something the player can't do yet");
        }

        [Test]
        public void ARookie_FinishesIt_InATestingGalaxy()
        {
            var run = Play(testMode: true);
            Report("testing", run);
            Assert.GreaterOrEqual(run.Finished, 0);
            Assert.IsEmpty(run.Blocked);
        }

        [Test]
        public void NewGamesStartIt_OldSavesDoNot()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            Assert.AreEqual(0, s.TutorialStep);
            var encoded = SaveCodec.EncodeState(s);
            encoded.Remove("tutorial"); // a save written before the training existed
            Assert.AreEqual(TutorialSystem.Done, SaveCodec.DecodeState(encoded).TutorialStep);
        }

        [Test]
        public void ReadingSteps_WaitForNext_AndSeenSteps_ForTheirOwnScreen()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            Assert.IsFalse(TutorialSystem.Check(s), "WELCOME waits for NEXT");
            Assert.IsFalse(TutorialSystem.Notice(s, TutorialSeen.Map), "a screen doesn't pass a reading step");
            Assert.IsTrue(TutorialSystem.Next(s).Ok);
            Assert.IsTrue(TutorialSystem.Next(s).Ok);
            Assert.AreEqual("build-quartz", TutorialSystem.Current(s)!.Id);
            Assert.IsFalse(TutorialSystem.Next(s).Ok, "NEXT can't skip a doing step");

            BuildingSystem.StartUpgrade(s, BuildingId.QuartzExtractor);
            Assert.IsTrue(TutorialSystem.Check(s));
            Assert.AreEqual("queues", TutorialSystem.Current(s)!.Id);
            Assert.IsFalse(TutorialSystem.Notice(s, TutorialSeen.Fleet), "only its own screen");
            Assert.IsTrue(TutorialSystem.Notice(s, TutorialSeen.Queues));
        }

        [Test]
        public void StepsAlreadyDone_ArePassedAtOnce()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: true);
            s.Buildings[BuildingId.QuartzExtractor].Level = 3;
            s.Buildings[BuildingId.CommandCenter].Level = 4;
            s.TutorialStep = Tutorial.Steps.ToList().FindIndex(st => st.Id == "build-quartz");
            TutorialSystem.Check(s);
            Assert.AreEqual("queues", TutorialSystem.Current(s)!.Id, "the extractor was already built");
            TutorialSystem.Notice(s, TutorialSeen.Queues);
            s.QuestStep = 1;
            TutorialSystem.Check(s);
            Assert.AreEqual("path", TutorialSystem.Current(s)!.Id, "claim, the Command Center and its speed-up were already done");
        }

        [Test]
        public void TheGifts_AreGivenOnce_AndSkippingGivesNothing()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.Resources = new ResourceBag(100_000, 100_000, 100_000).Milli();
            s.TutorialStep = Tutorial.Steps.ToList().FindIndex(st => st.Id == "command-center");
            Assert.IsTrue(BuildingSystem.StartUpgrade(s, BuildingId.CommandCenter).Ok);
            TutorialSystem.Check(s);
            Assert.AreEqual("speed-up", TutorialSystem.Current(s)!.Id);
            int Speedups() => s.Inventory.FirstOrDefault(i => i.ItemId == Tutorial.SpeedupItem)?.Count ?? 0;
            Assert.AreEqual(Tutorial.SpeedupCount, Speedups());

            s.TutorialStep = Tutorial.Steps.Count - 1;
            int dm = s.Premium.DarkMatter;
            long gold = s.Resources.Gold;
            Assert.IsTrue(TutorialSystem.Next(s).Ok);
            Assert.AreEqual(TutorialSystem.Done, s.TutorialStep);
            Assert.AreEqual(dm + Tutorial.RewardDarkMatter, s.Premium.DarkMatter);
            Assert.AreEqual(gold + Tutorial.Reward.Gold * 1000, s.Resources.Gold);

            TutorialSystem.Restart(s);
            s.TutorialStep = Tutorial.Steps.ToList().FindIndex(st => st.Id == "command-center");
            s.BuildQueue.Clear();
            s.Buildings[BuildingId.CommandCenter].Level = 1;
            BuildingSystem.StartUpgrade(s, BuildingId.CommandCenter);
            TutorialSystem.Check(s);
            Assert.AreEqual(Tutorial.SpeedupCount, Speedups(), "a replay hands out no second set");
            s.TutorialStep = Tutorial.Steps.Count - 1;
            TutorialSystem.Next(s);
            Assert.AreEqual(dm + Tutorial.RewardDarkMatter, s.Premium.DarkMatter, "…and no second reward");

            var skipper = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            int before = skipper.Premium.DarkMatter;
            TutorialSystem.Skip(skipper);
            Assert.IsFalse(TutorialSystem.Active(skipper));
            Assert.AreEqual(before, skipper.Premium.DarkMatter);
        }

        [Test]
        public void TheStepAndItsGifts_SurviveASave()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.TutorialStep = 7;
            s.TutorialFlags.Add("speedups");
            var back = SaveCodec.DecodeState(SaveCodec.EncodeState(s));
            Assert.AreEqual(7, back.TutorialStep);
            CollectionAssert.AreEquivalent(new[] { "speedups" }, back.TutorialFlags);
        }

        [Test]
        public void TheHandbook_HasEveryTopicWrittenOut()
        {
            Assert.GreaterOrEqual(Handbook.Topics.Count, 12);
            foreach (var t in Handbook.Topics)
            {
                Assert.IsNotEmpty(t.Title);
                Assert.IsNotEmpty(t.Summary, t.Title);
                Assert.GreaterOrEqual(t.Paragraphs.Length, 2, t.Title);
                foreach (var p in t.Paragraphs) Assert.Greater(p.Length, 30, t.Title);
            }
        }

        [Test]
        public void EveryStep_IsWrittenOut()
        {
            Assert.AreEqual(Tutorial.Steps.Count, Tutorial.Steps.Select(st => st.Id).Distinct().Count(), "ids are unique");
            foreach (var st in Tutorial.Steps)
            {
                Assert.IsNotEmpty(st.Title, st.Id);
                Assert.IsNotEmpty(st.Chapter, st.Id);
                Assert.Greater(st.Text.Length, 40, $"{st.Id} explains itself");
                Assert.LessOrEqual(st.Text.Length, 260, $"{st.Id} fits the coach card");
                if (st.Goal == TutorialGoal.Seen) Assert.IsNotEmpty(st.Seen, st.Id);
            }
        }
    }
}
