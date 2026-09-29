// The galactic market (2026-09-28): drifting prices, exact quotes, the price
// impact of your own trades wearing off, and the fee.
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class MarketTests
    {
        static GameState Rich()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.Resources = new ResourceBag(1_000_000, 1_000_000, 1_000_000).Milli();
            s.Tick = 10_000;
            return s;
        }

        [Test]
        public void Prices_DriftWithinBounds_TheSameForEveryoneInTheGalaxy()
        {
            var a = Rich();
            var b = Rich();
            for (int t = 0; t < 7 * 24 * 3600; t += 1777)
            {
                a.Tick = b.Tick = t;
                foreach (var r in Resources.All)
                {
                    double d = MarketSystem.Drift(a.Seed, r, t);
                    Assert.That(d, Is.InRange(MarketSystem.DriftMin, MarketSystem.DriftMax));
                    Assert.AreEqual(MarketSystem.Price(a, r), MarketSystem.Price(b, r));
                }
            }
            // It moves smoothly: a minute changes it by a hair.
            double p0 = MarketSystem.Drift(a.Seed, ResourceId.Helium, 50_000), p1 = MarketSystem.Drift(a.Seed, ResourceId.Helium, 50_060);
            Assert.Less(System.Math.Abs(p1 - p0), 0.01);
        }

        [Test]
        public void ATrade_PaysExactlyItsQuote()
        {
            var s = Rich();
            var q = MarketSystem.GetQuote(s, ResourceId.Gold, ResourceId.Quartz, 40_000);
            Assert.IsTrue(q.Ok, q.Reason);
            long gold = s.Resources.Gold, quartz = s.Resources.Quartz;
            Assert.IsTrue(MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Quartz, 40_000).Ok);
            Assert.AreEqual(gold - 40_000_000, s.Resources.Gold);
            Assert.AreEqual(quartz + q.BuyWhole * 1000, s.Resources.Quartz);
            Assert.AreEqual(1, s.Stats.MarketTrades);

            // At the posted rate, less the fee and a little slippage.
            double posted = 40_000 * MarketSystem.Price(Rich(), ResourceId.Gold) / MarketSystem.Price(Rich(), ResourceId.Quartz);
            Assert.Less(q.BuyWhole, posted * (1 - MarketSystem.Fee));
            Assert.Greater(q.BuyWhole, posted * (1 - MarketSystem.Fee) * 0.5);
        }

        [Test]
        public void YourSelling_CheapensIt_AndThatWearsOff()
        {
            var s = Rich();
            double before = MarketSystem.Price(s, ResourceId.Gold);
            Assert.IsTrue(MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Helium, 40_000).Ok);
            double after = MarketSystem.Price(s, ResourceId.Gold);
            Assert.Less(after, before);
            Assert.Greater(MarketSystem.Impact(s, ResourceId.Helium), 1, "buying helium made it dearer");

            double impact = MarketSystem.Impact(s, ResourceId.Gold);
            s.Tick += MarketSystem.ImpactHalfLifeSec;
            Assert.AreEqual(1 + (impact - 1) / 2, MarketSystem.Impact(s, ResourceId.Gold), 1e-9, "half gone after a half-life");
            s.Tick += 10 * MarketSystem.ImpactHalfLifeSec;
            Assert.AreEqual(1, MarketSystem.Impact(s, ResourceId.Gold), 0.001);
        }

        [Test]
        public void ASplitTrade_CostsTheSameAsOne()
        {
            var whole = Rich();
            var split = Rich();
            MarketSystem.Trade(whole, ResourceId.Gold, ResourceId.Helium, 40_000);
            for (int i = 0; i < 4; i++) MarketSystem.Trade(split, ResourceId.Gold, ResourceId.Helium, 10_000);
            Assert.AreEqual(whole.Resources.Helium / 1000, split.Resources.Helium / 1000, 4, "only rounding apart");
        }

        /// <summary>Selling and buying back can never profit: the fee is paid twice, and
        /// the price moves only ever cost you (a little less than two full fees here,
        /// since buying back less moves the price less).</summary>
        [Test]
        public void ARoundTrip_AlwaysLoses_AtLeastAFee()
        {
            var s = Rich();
            long gold = s.Resources.Gold;
            var there = MarketSystem.GetQuote(s, ResourceId.Gold, ResourceId.Quartz, 30_000);
            Assert.IsTrue(MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Quartz, 30_000).Ok);
            Assert.IsTrue(MarketSystem.Trade(s, ResourceId.Quartz, ResourceId.Gold, there.BuyWhole).Ok);
            long back = 30_000 - (gold - s.Resources.Gold) / 1000;
            Assert.LessOrEqual(back, 30_000 * (1 - MarketSystem.Fee));
            Assert.Greater(back, 30_000 * 0.85, "the fees and a little slippage — nothing worse");
        }

        [Test]
        public void Trades_AreRefused_WhenTheyCantHappen()
        {
            var s = Rich();
            Assert.IsFalse(MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Gold, 10).Ok);
            Assert.IsFalse(MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Quartz, 0).Ok);
            Assert.IsFalse(MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Quartz, 2_000_000).Ok, "more than you have");
            long max = MarketSystem.MaxSell(s, ResourceId.Gold);
            Assert.Less(max, 1_000_000, "one trade moves at most a full depth");
            Assert.IsFalse(MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Quartz, max + 1).Ok);
            Assert.IsTrue(MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Quartz, max).Ok);
            Assert.IsFalse(MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Helium, 1).Ok, "a gold coin buys no helium");
            Assert.IsTrue(MarketSystem.Trade(s, ResourceId.Helium, ResourceId.Gold, 1).Ok, "but helium buys gold");
        }

        [Test]
        public void YourMarketImpact_SurvivesASave()
        {
            var s = Rich();
            Assert.IsTrue(MarketSystem.Trade(s, ResourceId.Quartz, ResourceId.Helium, 30_000).Ok);
            var back = SaveCodec.DecodeState(SaveCodec.EncodeState(s));
            foreach (var r in Resources.All)
                Assert.AreEqual(MarketSystem.Impact(s, r), MarketSystem.Impact(back, r), 1e-12);
            Assert.AreEqual(1, back.Stats.MarketTrades);
            Assert.AreEqual(1, MarketSystem.Impact(SaveCodec.DecodeState(SaveCodec.EncodeState(Rich())), ResourceId.Gold));
        }
    }
}
