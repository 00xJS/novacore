// Gzipped saves (2026-09-29): a whole galaxy packs small and comes back
// byte-identical, and the plain-JSON saves older builds wrote still read.
using System.Text;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;

namespace GalaxyRoyale.Sim.Tests
{
    public class SaveCompressionTests
    {
        static string GalaxyJson()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            return SaveCodec.Encode(SaveManager.Wrap(player, 1_234_567_890_123, galaxy));
        }

        [Test]
        public void Galaxy_PacksToAFractionAndRoundTrips()
        {
            string json = GalaxyJson();
            byte[] packed = SaveCompression.Pack(json);

            Assert.IsTrue(SaveCompression.IsPacked(packed));
            Assert.Less(packed.Length, Encoding.UTF8.GetByteCount(json) / 4, "gzip should shrink a save at least 4×");
            Assert.AreEqual(json, SaveCompression.Unpack(packed));
            Assert.IsNotNull(SaveManager.Unwrap(SaveCodec.Decode(SaveCompression.Unpack(packed))));
        }

        [Test]
        public void PlainJson_FromOlderBuilds_StillReads()
        {
            string json = SaveCodec.Encode(SaveManager.Wrap(GameState.CreateNewGame(42), 1000));
            byte[] plain = Encoding.UTF8.GetBytes(json);

            Assert.IsFalse(SaveCompression.IsPacked(plain));
            Assert.AreEqual(json, SaveCompression.Unpack(plain));

            // With a byte-order mark, as a text editor might save it.
            var bom = new byte[plain.Length + 3];
            bom[0] = 0xEF; bom[1] = 0xBB; bom[2] = 0xBF;
            plain.CopyTo(bom, 3);
            Assert.AreEqual(json, SaveCompression.Unpack(bom));
        }

        [Test]
        public void NonAsciiNames_SurviveTheTrip()
        {
            var state = GameState.CreateNewGame(42);
            state.Profile.Name = "Zoë ⭐ 銀河";
            string json = SaveCodec.Encode(SaveManager.Wrap(state, 1000));

            var back = SaveCodec.Decode(SaveCompression.Unpack(SaveCompression.Pack(json)));
            Assert.AreEqual("Zoë ⭐ 銀河", back.State.Profile.Name);
        }
    }
}
