// Performance tripwires for the simulated galaxy. These run the REAL 99-bot
// configuration (not the small test roster) because both paths sit on the
// app's critical launch path:
//   - CreateGalaxy runs once per new game (pre-sims up to 3 days per bot);
//   - Advance covers the offline catch-up cap (8 h) after every long absence.
// Thresholds are deliberately loose (editor Mono is slower than IL2CPP, CI
// machines vary) — they exist to catch order-of-magnitude regressions like an
// accidental per-tick loop, not to benchmark.
using System.Diagnostics;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;

namespace GalaxyRoyale.Sim.Tests
{
    public class BotPerfTests
    {
        [Test]
        public void FullGalaxy_CreationAndOfflineCatchUp_StayCheap()
        {
            var sw = Stopwatch.StartNew();
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile);
            long createMs = sw.ElapsedMilliseconds;
            Assert.AreEqual(BotSystem.BotCount, galaxy.Bots.Count);

            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            player.Tick = Balance.OfflineCapHours * 3600; // the 8 h catch-up cap
            var events = new SimEventBus { Suppressed = true };
            sw.Restart();
            BotSystem.Advance(player, galaxy, events);
            long catchUpMs = sw.ElapsedMilliseconds;
            events.DrainSuppressed();

            // Autosave splits here: the snapshot tree is built on the main thread,
            // the text on a worker (LocalSave.SaveInBackground).
            sw.Restart();
            var tree = SaveCodec.EncodeTree(SaveManager.Wrap(player, 0, galaxy));
            long treeMs = sw.ElapsedMilliseconds;
            sw.Restart();
            string json = Json.Write(tree);
            long textMs = sw.ElapsedMilliseconds;
            long encodeMs = treeMs + textMs;

            TestContext.Out.WriteLine(
                $"create({BotSystem.BotCount} bots, pre-sim)={createMs}ms  catchUp(8h)={catchUpMs}ms  " +
                $"encode={encodeMs}ms (main-thread snapshot {treeMs}ms + worker text {textMs}ms)  " +
                $"saveSize={json.Length / 1024}KB");

            // Loose ceilings — a phone is ~3-5× slower than an editor Mono run,
            // so these bounds keep the worst device case inside a launch screen.
            Assert.Less(createMs, 5000, "new-game galaxy creation ballooned");
            Assert.Less(catchUpMs, 3000, "8h offline catch-up ballooned");
            Assert.Less(encodeMs, 2000, "save encoding ballooned");
            Assert.Less(json.Length, 4_000_000, "save file ballooned");
        }
    }
}
