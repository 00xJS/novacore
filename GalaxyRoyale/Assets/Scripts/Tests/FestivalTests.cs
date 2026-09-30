// Seasonal festivals (2026-09-30).
using System;
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class FestivalTests
    {
        [TearDown]
        public void NoCalendar() => FestivalSystem.Today = null;

        [Test]
        public void Festivals_FollowTheCalendar_AcrossNewYear()
        {
            FestivalSystem.Today = new DateTime(2026, 9, 30);
            Assert.IsNull(FestivalSystem.Current());
            Assert.AreEqual("void-harvest", FestivalSystem.Next()!.Value.def.Id);
            FestivalSystem.Today = new DateTime(2026, 10, 25);
            Assert.AreEqual("void-harvest", FestivalSystem.Active!.Id);
            FestivalSystem.Today = new DateTime(2027, 1, 3);
            Assert.AreEqual("frost-nebula", FestivalSystem.Active!.Id, "December's festival runs into January");
            Assert.AreEqual(2026, FestivalSystem.Current()!.Value.year);
            FestivalSystem.Today = new DateTime(2027, 1, 6);
            Assert.IsNull(FestivalSystem.Current());
        }

        [Test]
        public void AFestival_BendsItsRule_ForEveryEmpire()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            FestivalSystem.Today = new DateTime(2026, 9, 30);
            long prod = ResourceSystem.GetRates(s).Total;
            float loot = TwistSystem.CampLootMult(s);
            FestivalSystem.Today = new DateTime(2027, 2, 1); // the Lantern Festival
            Assert.Greater(ResourceSystem.GetRates(s).Total, prod);
            FestivalSystem.Today = new DateTime(2026, 10, 21); // the Void Harvest
            Assert.AreEqual(loot * 1.25f, TwistSystem.CampLootMult(s), 1e-4);
        }

        [Test]
        public void TheGoal_CountsFromTheFirstDay_AndPaysOnceAYear()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.Stats.CampsCleared = 50;
            FestivalSystem.Today = new DateTime(2026, 10, 21);
            var events = new SimEventBus();
            var began = new List<FestivalBegan>();
            events.Subscribe(e => { if (e is FestivalBegan b) began.Add(b); });
            s.Tick = 59;
            new TickEngine(s, events).Advance(1);
            Assert.AreEqual(1, began.Count);
            Assert.AreEqual(0, FestivalSystem.Progress(s).have, "camps before the festival don't count");
            s.Stats.CampsCleared += 20;
            int dm = s.Premium.DarkMatter;
            Assert.IsTrue(FestivalSystem.Claim(s).Ok);
            Assert.AreEqual(dm + 300, s.Premium.DarkMatter);
            Assert.IsTrue(s.Skins.Owned.Contains("skin-harvest"));
            Assert.IsFalse(FestivalSystem.Claim(s).Ok, "once this year");
            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;
            Assert.IsTrue(FestivalSystem.Claimed(back));
            FestivalSystem.Today = new DateTime(2027, 10, 21);
            back.Stats.CampsCleared += 25;
            FestivalSystem.Progress(back); // next year's baseline
            back.Stats.CampsCleared += 20;
            Assert.IsTrue(FestivalSystem.CanClaim(back), "and again next year");
        }
    }
}
