// How fast the 249 rivals grow (2026-09-30): commander level, skills, the Path,
// Command Center, might, late buildings, relics and how much of their wallets a
// raid could take. Explicit: run it by name when tuning Bots/BotCareer.
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class RivalGrowthReport
    {
        [Test, Explicit("a report: how fast the 249 rivals grow")]
        public void Rivals_Days3To14()
        {
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, player.HomeTile);
            var events = new SimEventBus();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (int day in new[] { 3, 7, 14 })
            {
                while (player.Tick < day * 86400)
                {
                    player.Tick += 3600;
                    BotSystem.Advance(player, galaxy, events);
                    ProgressionSystem.Advance(player, galaxy, events);
                }
                var might = galaxy.Bots.Select(b => b.CachedMight).OrderBy(m => m).ToList();
                var lv = galaxy.Bots.Select(b => CommanderSystem.RivalLevel(b.State)).OrderBy(m => m).ToList();
                var cc = galaxy.Bots.Select(b => b.State.Buildings[BuildingId.CommandCenter].Level).OrderBy(m => m).ToList();
                int late = galaxy.Bots.Count(b => Buildings.All.Any(id => Buildings.Defs[id].UnlockCc > 0 && b.State.Buildings[id].Level > 0));
                var mid = galaxy.Bots.OrderBy(b => b.CachedMight).ElementAt(galaxy.Bots.Count / 2);
                var st = mid.State;
                TestContext.Progress.WriteLine($"MEDIAN d{day}: {mid.Name} res {st.Resources.Gold / 1000}/{st.Resources.Quartz / 1000}/{st.Resources.Helium / 1000} " +
                    $"rates/h {ResourceSystem.GetRates(st).Gold / 1000}/{ResourceSystem.GetRates(st).Quartz / 1000}/{ResourceSystem.GetRates(st).Helium / 1000} · " +
                    $"energy {ResourceSystem.GetEnergyBalance(st).Factor:0.00} · bldg {string.Join(",", Buildings.All.Where(id => st.Buildings[id].Level > 0).Select(id => $"{id}:{st.Buildings[id].Level}"))} · " +
                    $"mines {st.ExtraMines.Count} · CC next cost {BuildingSystem.GetUpgradeCost(BuildingId.CommandCenter, st.Buildings[BuildingId.CommandCenter].Level + 1).Total / 1000} · " +
                    $"CC check: {BuildingSystem.CheckUpgrade(st, BuildingId.CommandCenter).Reason} · queue {st.BuildQueue.Count} · ships {st.Ships.Values.Sum()} · research {st.Research.Values.Sum()}");
                var loot = galaxy.Bots.Select(b => BotSystem.SnapshotOf(b).LootableMilli.Total / 1000).OrderBy(x => x).ToList();
                var wallet = galaxy.Bots.Select(b => b.State.Resources.Total / 1000).OrderBy(x => x).ToList();
                var prot = galaxy.Bots.Select(b => ResourceSystem.GetProtected(b.State).Total / 1000).OrderBy(x => x).ToList();
                TestContext.Progress.WriteLine($"LOOT d{day}: rivals with loot {loot.Count(x => x > 0)} · median lootable {loot[loot.Count / 2]} top {loot[^1]} · median wallet {wallet[wallet.Count / 2]} · median protected {prot[prot.Count / 2]}");
                TestContext.Progress.WriteLine($"RIVALS d{day}: might median {might[might.Count / 2]:N0} top {might[^1]:N0} · level median {lv[lv.Count / 2]} top {lv[^1]} · CC median {cc[cc.Count / 2]} top {cc[^1]} · " +
                    $"skills {galaxy.Bots.Average(b => b.State.Commander.Skills.Values.Sum()):0.0} · quests {galaxy.Bots.Average(b => b.State.QuestStep):0.0} · late bldg {late} · relics {galaxy.Bots.Average(b => b.State.Relics.Values.Sum()):0.00} · {clock.ElapsedMilliseconds} ms");
            }
        }
    }
}
