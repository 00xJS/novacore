// App Store purchases (2026-09-30): each verified transaction credits once.
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class PurchaseTests
    {
        [Test]
        public void APurchase_CreditsItsPack_Once()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            int dm = s.Premium.DarkMatter;
            var pack = DarkMatterPacks.All[2];
            Assert.AreEqual(PurchaseSystem.Outcome.Credited, PurchaseSystem.Credit(s, pack.ProductId, "2000000001", out int got));
            Assert.AreEqual(pack.DarkMatter, got);
            Assert.AreEqual(dm + pack.DarkMatter, s.Premium.DarkMatter);
            Assert.AreEqual(PurchaseSystem.Outcome.AlreadyCredited, PurchaseSystem.Credit(s, pack.ProductId, "2000000001", out _),
                "the store handing the same transaction over again pays nothing");
            Assert.AreEqual(dm + pack.DarkMatter, s.Premium.DarkMatter);
            Assert.AreEqual(PurchaseSystem.Outcome.Credited, PurchaseSystem.Credit(s, pack.ProductId, "2000000002", out _));
            Assert.AreEqual(dm + 2 * pack.DarkMatter, s.Premium.DarkMatter);
        }

        [Test]
        public void AnUnknownProduct_CreditsNothing()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            int dm = s.Premium.DarkMatter;
            Assert.AreEqual(PurchaseSystem.Outcome.UnknownProduct, PurchaseSystem.Credit(s, "galaxyroyale.nope", "1", out _));
            Assert.AreEqual(dm, s.Premium.DarkMatter);
            Assert.IsEmpty(s.CreditedTransactions);
        }

        [Test]
        public void CreditedTransactions_SurviveASave()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            PurchaseSystem.Credit(s, DarkMatterPacks.All[0].ProductId, "77", out _);
            var back = SaveCodec.DecodeState(SaveCodec.EncodeState(s));
            Assert.AreEqual(PurchaseSystem.Outcome.AlreadyCredited,
                PurchaseSystem.Credit(back, DarkMatterPacks.All[0].ProductId, "77", out _));
        }

        [Test]
        public void ThePacks_AreDistinct_AndBetterValueAsTheyGrow()
        {
            Assert.AreEqual(DarkMatterPacks.All.Count, DarkMatterPacks.All.Select(p => p.ProductId).Distinct().Count());
            double lastRate = 0;
            foreach (var p in DarkMatterPacks.All)
            {
                StringAssert.StartsWith("galaxyroyale.darkmatter.", p.ProductId);
                double rate = p.DarkMatter / double.Parse(p.SuggestedUsd, System.Globalization.CultureInfo.InvariantCulture);
                Assert.Greater(rate, lastRate, $"{p.ProductId} gives more Dark Matter per dollar than the pack before");
                lastRate = rate;
            }
        }
    }
}
