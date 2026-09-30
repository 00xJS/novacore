// NEW GAME modes + the Commander's Path (user request 2026-09-27): a STANDARD
// game starts on the honest wallet, a TESTING game keeps the playtest kit, and
// the quest chain — hosted in both — must carry a standard start through every
// step without waiting on production.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class QuestTests
    {
        [Test]
        public void StandardStart_IsTheHonestWallet()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            Assert.IsFalse(s.TestMode);
            Assert.AreEqual(Balance.StartResources().Milli().Gold, s.Resources.Gold);
            Assert.AreEqual(Balance.StartResources().Milli().Helium, s.Resources.Helium);
            Assert.AreEqual(0, s.Premium.DarkMatter);
            Assert.AreEqual(0, s.Inventory.Count, "no free speed-ups");
            Assert.AreEqual(0, s.QuestStep);
        }

        [Test]
        public void TestingStart_KeepsThePlaytestKit()
        {
            var s = GameState.CreateNewGame(42, testMode: true);
            Assert.IsTrue(s.TestMode);
            Assert.AreEqual(Balance.TestModeResources * 1000L, s.Resources.Gold);
            Assert.AreEqual(Balance.TestModeDarkMatter, s.Premium.DarkMatter);
            Assert.Greater(s.Inventory.Count, 0);
            Assert.AreEqual(0, s.QuestStep, "the quests run in testing too");
        }

        [Test]
        public void Claim_RefusesAnUnfinishedQuest()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            Assert.IsFalse(QuestSystem.Claim(s).Ok);
            Assert.AreEqual(0, s.QuestStep);
        }

        [Test]
        public void Claim_PaysTheRewardAndMovesOn()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Buildings[BuildingId.QuartzExtractor].Level = 1;
            long gold = s.Resources.Gold, quartz = s.Resources.Quartz;
            Assert.IsTrue(QuestSystem.Claim(s).Ok);
            Assert.AreEqual(1, s.QuestStep);
            Assert.AreEqual(gold + Quests.Chain[0].Reward.Gold * 1000L, s.Resources.Gold);
            Assert.AreEqual(quartz + Quests.Chain[0].Reward.Quartz * 1000L, s.Resources.Quartz);
        }

        [Test]
        public void ShipyardQuest_HandsOverTheStarterSquadron()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.QuestStep = -1;
            for (int i = 0; i < Quests.Chain.Count; i++)
                if (Quests.Chain[i].Goal == QuestGoal.BuildingLevel && Quests.Chain[i].Building == BuildingId.Shipyard)
                    s.QuestStep = i;
            Assert.GreaterOrEqual(s.QuestStep, 0, "the chain has a Shipyard quest");
            s.Buildings[BuildingId.Shipyard].Level = 1;
            Assert.IsTrue(QuestSystem.Claim(s).Ok);
            Assert.AreEqual(10, s.Ships[HullId.Fighter]);
            Assert.AreEqual(2, s.Ships[HullId.Probe]);
        }

        [Test]
        public void HonestStart_EveryStepIsAffordable()
        {
            // Walk Act I on a STANDARD wallet with zero production: pay each
            // step's real cost, then collect its reward. It must never strand a
            // new commander waiting on the mines. (Act II assumes a working
            // colony; StandardPacingTests plays it.)
            var s = GameState.CreateNewGame(42, testMode: false);
            void Pay(ResourceBag milli, string what)
            {
                Assert.IsTrue(ResourceSystem.CanAfford(s, milli),
                    $"{what}: need {milli.Gold / 1000}/{milli.Quartz / 1000}/{milli.Helium / 1000}, have " +
                    $"{s.Resources.Gold / 1000}/{s.Resources.Quartz / 1000}/{s.Resources.Helium / 1000}");
                Assert.IsTrue(ResourceSystem.Spend(s, milli).Ok);
            }

            while (s.QuestStep < Quests.ActOneSteps && QuestSystem.Current(s) is QuestDef quest)
            {
                switch (quest.Goal)
                {
                    case QuestGoal.BuildingLevel:
                        for (int lvl = s.Buildings[quest.Building].Level + 1; lvl <= quest.Target; lvl++)
                        {
                            if (quest.Building != BuildingId.CommandCenter)
                                Assert.LessOrEqual(lvl, s.Buildings[BuildingId.CommandCenter].Level,
                                    $"{quest.Title}: the Command Center caps {quest.Building} at this point");
                            Pay(BuildingSystem.GetUpgradeCost(quest.Building, lvl), quest.Title);
                            s.Buildings[quest.Building].Level = lvl;
                        }
                        break;
                    case QuestGoal.ResearchLevel:
                        Assert.GreaterOrEqual(s.Buildings[BuildingId.ResearchLab].Level,
                            Techs.Defs[quest.Tech].LabLevelReq, $"{quest.Title}: lab level");
                        Pay(ResearchSystem.GetResearchCost(quest.Tech, quest.Target), quest.Title);
                        s.Research[quest.Tech] = quest.Target;
                        break;
                    case QuestGoal.CampScouted:
                        Assert.Greater(s.Ships.TryGetValue(HullId.Probe, out var probes) ? probes : 0, 0,
                            "a probe to scout with");
                        s.Mailbox.Add(new SpyReport
                        {
                            Id = s.NextReportId++,
                            Intel = new SpyIntel { Kind = NodeKind.Camp, Garrison = new Dictionary<HullId, int>() },
                        });
                        break;
                    case QuestGoal.BattlesWon:
                        Assert.GreaterOrEqual(s.Ships.TryGetValue(HullId.Fighter, out var f) ? f : 0, 10,
                            "a squadron that can beat a Lv 1-2 camp");
                        s.Stats.BattlesWon++;
                        break;
                }
                Assert.IsTrue(QuestSystem.Claim(s).Ok, quest.Title);
            }
            Assert.AreEqual(Quests.ActOneSteps, s.QuestStep);
        }

        [Test]
        public void ModeAndQuestStep_SurviveTheSave()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.QuestStep = 4;
            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;
            Assert.IsFalse(back.TestMode);
            Assert.AreEqual(4, back.QuestStep);
        }

        [Test]
        public void OldSaves_WereTestGames()
        {
            var s = GameState.CreateNewGame(42, testMode: true);
            var tree = SaveCodec.EncodeTree(SaveManager.Wrap(s, 1000));
            ((Dictionary<string, object?>)tree["state"]!).Remove("testMode"); // a pre-choice save
            var back = SaveCodec.Decode(Json.Write(tree)).State;
            Assert.IsTrue(back.TestMode);
            Assert.AreEqual(0, back.QuestStep);
        }
    }
}
