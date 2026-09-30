// Rivals play the whole game (2026-09-30): skills, the Commander's Path, the
// Frontier and the Citadel, the market, expeditions, and saving for the
// Command Center instead of stalling below it.
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class BotCareerTests
    {
        static BotGalaxy Run(int days, int bots = 12)
        {
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, player.HomeTile, bots);
            var events = new SimEventBus();
            while (player.Tick < days * 86400)
            {
                player.Tick += 3600;
                BotSystem.Advance(player, galaxy, events);
            }
            return galaxy;
        }

        [Test]
        public void Rivals_LevelUp_LearnSkills_AndWalkThePath()
        {
            var galaxy = Run(6);
            Assert.IsTrue(galaxy.Bots.All(b => b.State.Commander.Level > 1), "every rival's commander levels up");
            Assert.Greater(galaxy.Bots.Average(b => b.State.Commander.Skills.Values.Sum()), 5, "and spends the points");
            Assert.Greater(galaxy.Bots.Average(b => b.State.QuestStep), 10, "they walk the Commander's Path");
            Assert.IsTrue(galaxy.Bots.All(b => CommanderSystem.PointsFree(b.State) <= 1), "no points left lying around");
        }

        [Test]
        public void Rivals_UseTheFrontierTheCitadelAndTheMarket()
        {
            var galaxy = Run(10);
            bool Late(BotEmpire b, BuildingId id) => b.State.Buildings[id].Level > 0;
            Assert.IsTrue(galaxy.Bots.Any(b => Late(b, BuildingId.RelicVault) || Late(b, BuildingId.Academy)
                || Late(b, BuildingId.Terraformer) || Late(b, BuildingId.TradeConsulate)), "Citadel buildings go up");
            Assert.IsTrue(galaxy.Bots.Any(b => Late(b, BuildingId.SalvageYard) || Late(b, BuildingId.JumpGate)
                || Late(b, BuildingId.RepairDock)), "and Frontier ones");
            Assert.IsTrue(galaxy.Bots.Any(b => b.State.Stats.MarketTrades > 0), "someone trades on the market");
            Assert.IsTrue(galaxy.Bots.Any(b => b.State.Stats.ExpeditionsDone > 0), "someone comes home from an expedition");
            Assert.IsTrue(galaxy.Bots.All(b => b.State.Stats.ShipsBuilt > 0), "ships built are on the record");
        }

        [Test]
        public void Rivals_KeepGrowing_PastCommandCenter10()
        {
            var galaxy = Run(14);
            var cc = galaxy.Bots.Select(b => b.State.Buildings[BuildingId.CommandCenter].Level).OrderBy(x => x).ToList();
            Assert.GreaterOrEqual(cc[cc.Count / 2], 12, "the median rival is past the old CC 9-10 wall in two weeks");
            Assert.LessOrEqual(cc[^1], 20, "but nobody runs away with the galaxy");
        }

        [Test]
        public void SavingForTheCommandCenter_OnlyWhenItsWhatHoldsTheColonyBack()
        {
            var s = GameState.CreateNewGame(1, testMode: false);
            s.Buildings[BuildingId.CommandCenter].Level = 8;
            foreach (var id in new[] { BuildingId.GoldMine, BuildingId.QuartzExtractor, BuildingId.HeliumRefinery, BuildingId.PowerPlant })
                s.Buildings[id].Level = 8;
            s.Resources = new ResourceBag(10, 10, 10).Milli();
            Assert.IsTrue(BotCareer.SavingForCommand(s), "producers capped, CC unaffordable: save");
            s.Resources = new ResourceBag(10_000_000, 10_000_000, 10_000_000).Milli();
            Assert.IsFalse(BotCareer.SavingForCommand(s), "affordable: just build it");
            s.Resources = new ResourceBag(10, 10, 10).Milli();
            s.Buildings[BuildingId.GoldMine].Level = 4;
            s.Buildings[BuildingId.QuartzExtractor].Level = 4;
            Assert.IsFalse(BotCareer.SavingForCommand(s), "producers behind: grow them first");
        }
    }
}
