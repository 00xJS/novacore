// Redeem codes (Settings › REDEEM A CODE): once per game, fingerprinted.
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class RedeemTests
    {
        const string Launch = "galaxy-launch"; // RedeemCodes.All[0], typed loosely

        [Test]
        public void ACode_PaysOnce_PerGame()
        {
            var state = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            state.Resources = new ResourceBag();
            int dm = state.Premium.DarkMatter;

            Assert.IsTrue(RedeemSystem.Redeem(state, Launch, out var paid).Ok);
            Assert.AreEqual("Launch gift", paid!.Title);
            Assert.AreEqual(20_000_000, state.Resources.Gold, "20K gold, in milli-units");
            Assert.AreEqual(dm + 200, state.Premium.DarkMatter);

            var again = RedeemSystem.Redeem(state, "GALAXYLAUNCH", out _);
            Assert.IsFalse(again.Ok, "once per game, however it's typed");
            Assert.AreEqual(dm + 200, state.Premium.DarkMatter);

            var fresh = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            Assert.IsTrue(RedeemSystem.Redeem(fresh, Launch, out _).Ok, "a new game can use it again");
        }

        [Test]
        public void UnknownAndEmptyCodes_AreRefused()
        {
            var state = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            Assert.AreEqual("Enter a code", RedeemSystem.Redeem(state, "  - ", out _).Reason);
            Assert.AreEqual("That code isn't valid", RedeemSystem.Redeem(state, "NOT-A-CODE", out _).Reason);
            Assert.IsEmpty(state.RedeemedCodes);
        }

        [Test]
        public void TheCodeItself_NeverShips_OnlyItsFingerprint()
        {
            foreach (var def in RedeemCodes.All)
            {
                Assert.AreEqual(64, def.Hash.Length);
                StringAssert.DoesNotContain("LAUNCH", def.Hash.ToUpperInvariant());
            }
            Assert.AreEqual(RedeemCodes.All[0].Hash, RedeemSystem.Fingerprint(Launch));
        }

        [Test]
        public void UsedCodes_SurviveASave()
        {
            var state = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            Assert.IsTrue(RedeemSystem.Redeem(state, Launch, out _).Ok);
            var file = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(state, 1000)));
            Assert.IsTrue(file.State.RedeemedCodes.Contains(RedeemCodes.All[0].Hash));
            Assert.IsFalse(RedeemSystem.Redeem(file.State, Launch, out _).Ok);
        }
    }
}
