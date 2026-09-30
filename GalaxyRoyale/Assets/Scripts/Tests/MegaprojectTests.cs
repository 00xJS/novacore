// Mega-projects (2026-09-30).
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class MegaprojectTests
    {
        static GameState Colony(int cc)
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Buildings[BuildingId.CommandCenter].Level = cc;
            foreach (var id in new[] { BuildingId.GoldMine, BuildingId.QuartzExtractor, BuildingId.HeliumRefinery, BuildingId.PowerPlant })
                s.Buildings[id].Level = cc;
            s.Resources = new ResourceBag(100_000_000, 100_000_000, 100_000_000).Milli();
            return s;
        }

        [Test]
        public void AProject_OpensAtItsCommandCenter_BuildsOneStageAtATime_AndPaysForGood()
        {
            var s = Colony(14);
            StringAssert.Contains("Command Center 15", MegaprojectSystem.Check(s, MegaprojectKind.DysonSwarm).Reason);
            s = Colony(15);
            long prod = ResourceSystem.GetRates(s).Total;
            int power = ResourceSystem.GetEnergyBalance(s).Supply;
            Assert.IsTrue(MegaprojectSystem.Start(s, MegaprojectKind.DysonSwarm).Ok);
            Assert.IsFalse(MegaprojectSystem.Start(s, MegaprojectKind.DysonSwarm).Ok, "one stage at a time");
            var events = new SimEventBus();
            var done = new List<MegaprojectStageDone>();
            events.Subscribe(e => { if (e is MegaprojectStageDone d) done.Add(d); });
            new TickEngine(s, events).Advance(MegaprojectSystem.BuildSeconds(s, MegaprojectKind.DysonSwarm) + 1);
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual(1, MegaprojectSystem.Stage(s, MegaprojectKind.DysonSwarm));
            Assert.Greater(ResourceSystem.GetRates(s).Total, prod, "+production (the rates cache noticed)");
            Assert.Greater(ResourceSystem.GetEnergyBalance(s).Supply, power, "+power");
        }

        [Test]
        public void Stages_GetDearer_AndAllFourCompleteForTheAwards()
        {
            var s = Colony(21);
            long first = MegaprojectSystem.Cost(s, MegaprojectKind.Stargate).Total;
            s.Mega.Stages[MegaprojectKind.Stargate] = 2;
            Assert.AreEqual(first * 3, MegaprojectSystem.Cost(s, MegaprojectKind.Stargate).Total, 3);
            foreach (var def in Megaprojects.All) s.Mega.Stages[def.Kind] = Megaprojects.Stages;
            Assert.AreEqual(4, MegaprojectSystem.Completed(s));
            Assert.IsFalse(MegaprojectSystem.Check(s, MegaprojectKind.Stargate).Ok, "complete");
            Assert.Greater(ResearchSystem.AtkMult(s), 1.14f, "the Foundry's +15% attack");
            var ids = new List<string>();
            foreach (var a in AchievementSystem.CheckNew(s)) ids.Add(a.Id);
            CollectionAssert.IsSubsetOf(new[] { "wonder-builder", "architect-of-worlds" }, ids);
        }

        [Test]
        public void Projects_SurviveASave()
        {
            var s = Colony(17);
            s.Mega.Stages[MegaprojectKind.DysonSwarm] = 3;
            Assert.IsTrue(MegaprojectSystem.Start(s, MegaprojectKind.Stargate).Ok);
            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;
            Assert.AreEqual(3, MegaprojectSystem.Stage(back, MegaprojectKind.DysonSwarm));
            Assert.AreEqual((int)MegaprojectKind.Stargate, back.Mega.Active);
            Assert.AreEqual(s.Mega.EndsTick, back.Mega.EndsTick);
        }
    }
}
