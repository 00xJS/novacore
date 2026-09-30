// The codex (2026-09-30).
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class CodexTests
    {
        [Test]
        public void TheRecord_FillsTheCodex_AndAFullCollectionEarnsItsSkinAndTitle()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            var events = new SimEventBus();
            var done = new List<CodexCategoryComplete>();
            events.Subscribe(e => { if (e is CodexCategoryComplete c) done.Add(c); });
            s.Tick = 59;
            var engine = new TickEngine(s, events);
            engine.Advance(1);
            foreach (var h in Ships.All) s.Ships[h] = 1;
            engine.Advance(60);
            Assert.AreEqual(Ships.All.Count, CodexSystem.Progress(s, Codex.ById("fleet")!).found);
            Assert.IsTrue(done.Any(d => d.Category == "fleet"));
            Assert.IsTrue(s.Skins.Owned.Contains("skin-forge"), "an earned skin");
            Assert.IsTrue(ShopSystem.ApplySkin(s, "skin-forge").Ok);
            Assert.IsTrue(AchievementSystem.CheckNew(s).Any(a => a.Id == "master-shipwright"));
        }

        [Test]
        public void AWorld_IsRecordedWhenAFleetReachesIt()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            var lair = LairSystem.Spawn(s, 0, 0, null);
            CodexSystem.OnArrive(s, lair, null);
            Assert.IsTrue(CodexSystem.Has(s, "world:Lair"));
            Assert.IsFalse(CodexSystem.Has(s, "world:Camp"), "a lair is its own entry");
        }

        [Test]
        public void EveryCodexAchievement_TargetsItsWholeCollection_AndTheCodexSaves()
        {
            foreach (var a in Achievements.All.Where(a => a.Goal == AchievementGoal.CodexCategory))
                Assert.AreEqual(Codex.ById(a.Codex!)!.Entries.Count, a.Target, a.Id);
            var s = GameState.CreateNewGame(42, testMode: false);
            CodexSystem.Record(s, "relic:VoidLens", null);
            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;
            Assert.IsTrue(CodexSystem.Has(back, "relic:VoidLens"));
        }
    }
}
