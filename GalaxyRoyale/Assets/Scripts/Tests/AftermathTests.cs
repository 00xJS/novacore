// Batch-3 mechanics: burning planets after a lost defense, the galaxy news
// wire, the Aegis planet shield (block + deflect + break-on-aggression), and
// the codec round-trip of all three.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class AftermathTests
    {
        static BotGalaxy SmallGalaxy() =>
            BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 3);

        static BotAttack FleetAttack(BotGalaxy galaxy, BotEmpire bot, int arrivesAt) => new()
        {
            Id = galaxy.NextAttackId++,
            BotId = bot.Id,
            IsFleet = true,
            Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 40 },
            LaunchTick = 0,
            ArrivesAtTick = arrivesAt,
            From = bot.HomeTile,
        };

        [Test]
        public void LostDefense_SetsTheBurnScar_AndPostsNews()
        {
            var galaxy = SmallGalaxy();
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            var bot = galaxy.Bots[0];
            galaxy.Inbound.Add(FleetAttack(galaxy, bot, 50));

            player.Tick = 60;
            BotSystem.Advance(player, galaxy, new SimEventBus());

            // Undefended player loses → planet burns for 4 h FROM THE ARRIVAL tick
            // (a raid that landed mid-offline-stretch is already part-burned).
            Assert.AreEqual(50 + Balance.BurnDurationSec, player.BurningUntilTick,
                "the player's planet burns after a lost defense, stamped at arrival");
            Assert.IsTrue(galaxy.News.Count > 0, "the battle posted to the news wire");
            var latest = galaxy.News[^1];
            Assert.AreEqual(bot.Id, latest.AttackerId);
            Assert.AreEqual(0, latest.DefenderId);
            Assert.IsTrue(latest.AttackerWon);
        }

        [Test]
        public void PlayerRaidVictory_BurnsTheBotPlanet_AndPostsNews()
        {
            var galaxy = SmallGalaxy();
            var bot = galaxy.Bots[0];
            var report = new Combat.BattleReport
            {
                Winner = Combat.BattleWinner.Attacker,
                Defender = new Dictionary<HullId, int>(),
                DefenderSurvivors = new Dictionary<HullId, int>(),
            };
            BotSystem.ApplyPlayerRaid(galaxy, bot, report, new ResourceBag(5_000, 0, 0));

            Assert.AreEqual(bot.State.Tick + Balance.BurnDurationSec, bot.State.BurningUntilTick,
                "the raided bot's planet burns");
            var latest = galaxy.News[^1];
            Assert.AreEqual(0, latest.AttackerId, "player is attacker id 0 on the wire");
            Assert.AreEqual(bot.Id, latest.DefenderId);
            Assert.AreEqual(5_000, latest.LootMilli);
        }

        [Test]
        public void AegisShield_DeflectsInboundAtArrival()
        {
            var galaxy = SmallGalaxy();
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            player.Ships[HullId.Fighter] = 5; // a garrison that would have died
            var bot = galaxy.Bots[0];
            int botCruisersBefore = bot.State.Ships.TryGetValue(HullId.Cruiser, out var c) ? c : 0;
            galaxy.Inbound.Add(FleetAttack(galaxy, bot, 50));

            player.Tick = 60;
            player.Buffs.ShieldUntilTick = 100; // bubble up at impact
            BotSystem.Advance(player, galaxy, new SimEventBus());

            Assert.AreEqual(5, player.Ships[HullId.Fighter], "no battle happened — garrison intact");
            Assert.AreEqual(0, player.BurningUntilTick, "no scar on a deflected raid");
            Assert.AreEqual(botCruisersBefore + 40,
                bot.State.Ships.TryGetValue(HullId.Cruiser, out var after) ? after : 0,
                "the bot's fleet flew home intact");
            Assert.IsTrue(player.Mailbox.Count > 0
                && player.Mailbox[0].Subject.Contains("deflected"),
                "deflection filed to the mailbox");
        }

        [Test]
        public void AegisShield_ItemUse_And_BreakOnAggression()
        {
            var state = GameState.CreateNewGame(42);
            state.Inventory.Add(new InventoryEntry { ItemId = "shield-8h", Count = 1 });

            Assert.IsTrue(ShopSystem.UseItem(state, "shield-8h").Ok);
            Assert.AreEqual(Balance.ShieldShortSec, state.Buffs.ShieldUntilTick,
                "8h bubble raised from tick 0");
            // (TestMode pre-stocks speed-up tokens, so check THIS item specifically.)
            Assert.IsNull(state.Inventory.Find(e => e.ItemId == "shield-8h"), "consumable spent");

            // Raiding drops the bubble (user rule) — and reports that it did.
            Assert.IsTrue(BotSystem.BreakShieldForAggression(state));
            Assert.AreEqual(0, state.Buffs.ShieldUntilTick);
            Assert.IsFalse(BotSystem.BreakShieldForAggression(state), "nothing left to break");
        }

        [Test]
        public void News_Burn_And_Shield_RoundTripTheCodec()
        {
            var galaxy = SmallGalaxy();
            galaxy.AddNews(120, 2, 0, true, 7_500);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            player.BurningUntilTick = 999;
            player.Buffs.ShieldUntilTick = 888;

            string json = SaveCodec.Encode(SaveManager.Wrap(player, 0, galaxy));
            var back = SaveManager.Unwrap(SaveCodec.Decode(json))!.Value;

            Assert.AreEqual(999, back.state.BurningUntilTick);
            Assert.AreEqual(888, back.state.Buffs.ShieldUntilTick);
            var news = back.bots!.News;
            Assert.AreEqual(1, news.Count);
            Assert.AreEqual(120, news[0].Tick);
            Assert.AreEqual(2, news[0].AttackerId);
            Assert.AreEqual(0, news[0].DefenderId);
            Assert.IsTrue(news[0].AttackerWon);
            Assert.AreEqual(7_500, news[0].LootMilli);
        }

        [Test]
        public void PlanetaryResurfacing_RerollsTheLook_OneWay()
        {
            var state = GameState.CreateNewGame(42);
            state.Inventory.Add(new InventoryEntry { ItemId = "reroll-planet", Count = 2 });

            Assert.AreEqual(0, state.VisualSeedOffset, "natural look before any roll");
            Assert.IsTrue(ShopSystem.UseItem(state, "reroll-planet").Ok);
            int first = state.VisualSeedOffset;
            Assert.AreNotEqual(0, first, "the surface rerolled");

            Assert.IsTrue(ShopSystem.UseItem(state, "reroll-planet").Ok);
            Assert.AreNotEqual(first, state.VisualSeedOffset,
                "every roll lands somewhere new — no rolling back");

            // The rolled look survives the save.
            var back = SaveManager.Unwrap(SaveCodec.Decode(
                SaveCodec.Encode(SaveManager.Wrap(state, 0))))!.Value.state;
            Assert.AreEqual(state.VisualSeedOffset, back.VisualSeedOffset);
        }

        [Test]
        public void NewsFeed_CapsAtTheLimit()
        {
            var galaxy = new BotGalaxy();
            for (int i = 0; i < Balance.NewsCap + 25; i++)
                galaxy.AddNews(i, 1, 2, true, 100);
            Assert.AreEqual(Balance.NewsCap, galaxy.News.Count);
            Assert.AreEqual(25, galaxy.News[0].Tick, "oldest entries evicted first");
        }
    }
}
