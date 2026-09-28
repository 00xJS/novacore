// iCloud save backup (user request 2026-09-28): what goes up must come back
// byte for byte, and a damaged or half-synced backup must be refused rather
// than loaded.
using NUnit.Framework;
using GalaxyRoyale.Local;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Tests
{
    public class CloudSaveTests
    {
        [Test]
        public void Pack_RoundTripsARealSave_AndShrinksIt()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 3);
            var state = GameState.CreateNewGame(Spawn.GalaxySeed);
            string json = SaveCodec.Encode(SaveManager.Wrap(state, 1234, galaxy));

            string packed = CloudSave.Pack(json);
            Assert.AreEqual('z', packed[0], "gzip is available");
            Assert.Less(packed.Length, json.Length / 3, "the backup is a fraction of the save");
            Assert.AreEqual(json, CloudSave.Unpack(packed));
        }

        [Test]
        public void Header_RoundTrips_EvenWithAPipeInTheName()
        {
            var h = new CloudSave.Header
            {
                SavedAtMs = 1_790_000_000_000, Tick = 86_400, Might = 12_345, Name = "Kessler|Syndrome", DataLength = 40_000,
            };
            var back = CloudSave.ReadHeader(CloudSave.HeaderText(h));
            Assert.IsNotNull(back);
            Assert.AreEqual(h.SavedAtMs, back!.SavedAtMs);
            Assert.AreEqual(h.Tick, back.Tick);
            Assert.AreEqual(h.Might, back.Might);
            Assert.AreEqual(h.Name, back.Name);
            Assert.AreEqual(h.DataLength, back.DataLength);
        }

        [Test]
        public void DamagedBackups_AreRefused()
        {
            Assert.IsNull(CloudSave.ReadHeader(null));
            Assert.IsNull(CloudSave.ReadHeader("GR1|oops"));
            Assert.IsNull(CloudSave.ReadHeader("XX9|1|2|3|YQ==|4"));
            Assert.Throws<System.IO.InvalidDataException>(() => CloudSave.Unpack("q" + "AAAA"));
        }
    }
}
