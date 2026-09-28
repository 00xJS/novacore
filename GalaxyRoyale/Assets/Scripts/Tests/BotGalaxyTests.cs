// The simulated galaxy — determinism, progression, the player-exclusive
// limitations (no shop/DM/buffs, single queues), raid resolution against the
// player, and the v17 codec round-trip of the whole bot section.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class BotGalaxyTests
    {
        static BotGalaxy SmallGalaxy(int count = 4)
        {
            // Small roster keeps the pre-sim cheap; same code path as the real 99.
            return BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, count);
        }

        [Test]
        public void CreateGalaxy_IsDeterministic()
        {
            var a = SmallGalaxy();
            var b = SmallGalaxy();
            Assert.AreEqual(a.Bots.Count, b.Bots.Count);
            for (int i = 0; i < a.Bots.Count; i++)
            {
                Assert.AreEqual(a.Bots[i].Name, b.Bots[i].Name);
                Assert.AreEqual(a.Bots[i].HomeTile, b.Bots[i].HomeTile);
                Assert.AreEqual(a.Bots[i].CachedMight, b.Bots[i].CachedMight);
                Assert.AreEqual(a.Bots[i].State.Resources.Gold, b.Bots[i].State.Resources.Gold);
            }
        }

        [Test]
        public void Bots_NeverTouchPlayerExclusives()
        {
            var galaxy = SmallGalaxy();
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            player.Tick = 4 * 3600; // four hours of galaxy life
            var events = new SimEventBus();
            BotSystem.Advance(player, galaxy, events);

            foreach (var bot in galaxy.Bots)
            {
                Assert.AreEqual(0, bot.State.Premium.DarkMatter, "bots never hold Dark Matter");
                Assert.IsEmpty(bot.State.Inventory, "bots never hold shop items");
                Assert.AreEqual(0, bot.State.Buffs.ProdBoostUntilTick, "bots never run buffs");
                Assert.AreEqual(0, bot.State.Buffs.ExtraBuildSlotUntilTick, "no third build slot");
                Assert.AreEqual(0, bot.State.Buffs.ExtraResearchSlotUntilTick, "no third research slot");
                Assert.LessOrEqual(bot.State.BuildQueue.Count, Balance.BaseBuildSlots,
                    "bots run the standard two build orders, never the shop's third");
                Assert.LessOrEqual(bot.State.ResearchQueue.Count, Balance.BaseResearchSlots,
                    "bots run the standard two research orders, never the shop's third");
                Assert.LessOrEqual(bot.State.ShipQueue.Count, Balance.ShipQueueSlots,
                    "bots respect the two ship production lines");
            }
        }

        [Test]
        public void Bots_ProgressOverTime()
        {
            var galaxy = SmallGalaxy();
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            var events = new SimEventBus();

            long mightBefore = 0;
            foreach (var bot in galaxy.Bots) mightBefore += bot.CachedMight;

            player.Tick = 24 * 3600; // a full day
            BotSystem.Advance(player, galaxy, events);

            long mightAfter = 0;
            bool anyCommandCenterGrew = false;
            foreach (var bot in galaxy.Bots)
            {
                mightAfter += bot.CachedMight;
                if (bot.State.Buildings[BuildingId.CommandCenter].Level > 1
                    || bot.State.Buildings[BuildingId.GoldMine].Level > 1) anyCommandCenterGrew = true;
                Assert.AreEqual(player.Tick, bot.State.Tick, "bots ride the player clock");
            }
            Assert.Greater(mightAfter, mightBefore, "the galaxy grows while time passes");
            Assert.IsTrue(anyCommandCenterGrew, "someone broke past the CC-1 cap within a day");
        }

        [Test]
        public void InboundRaid_ResolvesAgainstPlayerAtArrival()
        {
            var galaxy = SmallGalaxy();
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            player.Resources = new ResourceBag(500_000, 500_000, 500_000).Milli();

            var bot = galaxy.Bots[0];
            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 40 };
            galaxy.Inbound.Add(new BotAttack
            {
                Id = galaxy.NextAttackId++,
                BotId = bot.Id,
                IsFleet = true,
                Ships = fleet,
                LaunchTick = 0,
                ArrivesAtTick = 50,
                From = bot.HomeTile,
            });

            player.Tick = 60; // past arrival
            var events = new SimEventBus();
            ColonyRaided? raided = null;
            events.Subscribe(e => { if (e is ColonyRaided cr) raided = cr; });
            BotSystem.Advance(player, galaxy, events);

            Assert.IsEmpty(galaxy.Inbound, "attack consumed at arrival");
            Assert.IsNotNull(raided, "ColonyRaided event fired");
            Assert.IsTrue(player.Mailbox.Count > 0 && player.Mailbox[0] is BattleMailReport,
                "battle report filed to the player's mailbox");
            // Undefended colony with a fat unshielded wallet → the bot won and looted.
            Assert.Less(player.Resources.Gold, 500_000_000L, "loot left the player's wallet");
            Assert.Greater(bot.State.Resources.Gold, 0, "loot reached the bot");
        }

        [Test]
        public void PlayerRaid_AppliesToBotDefender()
        {
            var galaxy = SmallGalaxy();
            var bot = galaxy.Bots[0];
            bot.State.Ships[HullId.Fighter] = 10;
            bot.State.Resources = new ResourceBag(100_000, 100_000, 100_000).Milli();
            long goldBefore = bot.State.Resources.Gold;

            var attacker = new Dictionary<HullId, int> { [HullId.Cruiser] = 60 };
            var snapshot = BotSystem.SnapshotOf(bot);
            var report = GalaxyRoyale.Sim.Combat.CombatResolver.Resolve(
                attacker, snapshot.Ships, GalaxyRoyale.Sim.Combat.FleetMods.None);
            Assert.AreEqual(GalaxyRoyale.Sim.Combat.BattleWinner.Attacker, report.Winner);

            var loot = new ResourceBag(1_000_000, 0, 0);
            BotSystem.ApplyPlayerRaid(galaxy, bot, report, loot);

            Assert.AreEqual(0, bot.State.Ships[HullId.Fighter], "garrison wiped");
            Assert.AreEqual(goldBefore - 1_000_000, bot.State.Resources.Gold, "loot deducted");
            Assert.AreEqual(1, bot.State.Stats.BattlesLost);
        }

        [Test]
        public void BotGalaxy_CodecRoundTrips()
        {
            var galaxy = SmallGalaxy();
            galaxy.Inbound.Add(new BotAttack
            {
                Id = galaxy.NextAttackId++,
                BotId = galaxy.Bots[1].Id,
                IsFleet = true,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 12 },
                LaunchTick = 100,
                ArrivesAtTick = 400,
                From = galaxy.Bots[1].HomeTile,
            });
            // A bot-vs-bot raid mid-return-leg must survive too (its ships left the
            // attacker's dock at launch — losing it on load would leak the fleet).
            galaxy.Marches.Add(new BotMarch
            {
                Id = galaxy.NextMarchId++,
                BotId = galaxy.Bots[0].Id,
                TargetBotId = galaxy.Bots[2].Id,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 9 },
                From = galaxy.Bots[0].HomeTile,
                To = galaxy.Bots[2].HomeTile,
                LaunchTick = 50,
                ArrivesAtTick = 300,
                ReturnsAtTick = 550,
                Resolved = true,
                LootMilli = new ResourceBag(7_000, 0, 500),
            });
            galaxy.Bots[0].LastPortTick = 1234;
            galaxy.Bots[0].LastHuntJumpTick = 777;
            galaxy.Bots[0].FocusTargetId = 3;
            galaxy.Bots[1].SpyBackAtTick = 4321;

            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            string json = SaveCodec.Encode(SaveManager.Wrap(player, 1234, galaxy));
            var decoded = SaveManager.Unwrap(SaveCodec.Decode(json));
            Assert.IsNotNull(decoded);
            var back = decoded!.Value.bots;
            Assert.IsNotNull(back, "bots section survives the envelope");
            Assert.AreEqual(galaxy.Bots.Count, back!.Bots.Count);
            Assert.AreEqual(galaxy.NextAttackId, back.NextAttackId);
            Assert.AreEqual(1, back.Inbound.Count);
            Assert.AreEqual(400, back.Inbound[0].ArrivesAtTick);
            Assert.AreEqual(1, back.Marches.Count, "in-flight bot raid survives the save");
            Assert.AreEqual(galaxy.NextMarchId, back.NextMarchId);
            Assert.AreEqual(9, back.Marches[0].Ships[HullId.Cruiser]);
            Assert.AreEqual(550, back.Marches[0].ReturnsAtTick);
            Assert.IsTrue(back.Marches[0].Resolved);
            Assert.AreEqual(7_000, back.Marches[0].LootMilli.Gold);
            Assert.AreEqual(1234, back.Bots[0].LastPortTick);
            Assert.AreEqual(777, back.Bots[0].LastHuntJumpTick);
            Assert.AreEqual(3, back.Bots[0].FocusTargetId);
            Assert.AreEqual(-1, back.Bots[1].FocusTargetId, "no-focus default survives");
            Assert.AreEqual(4321, back.Bots[1].SpyBackAtTick);
            for (int i = 0; i < galaxy.Bots.Count; i++)
            {
                Assert.AreEqual(galaxy.Bots[i].Id, back.Bots[i].Id);
                Assert.AreEqual(galaxy.Bots[i].CachedMight, back.Bots[i].CachedMight);
                Assert.AreEqual(galaxy.Bots[i].HomeTile, back.Bots[i].HomeTile);
                Assert.AreEqual(galaxy.Bots[i].State.Buildings[BuildingId.CommandCenter].Level,
                    back.Bots[i].State.Buildings[BuildingId.CommandCenter].Level);
            }
        }

        [Test]
        public void BotMarch_ResolvesAtArrival_AndSurvivorsFlyLootHome()
        {
            var galaxy = SmallGalaxy();
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            var attacker = galaxy.Bots[0];
            var defender = galaxy.Bots[1];
            defender.State.Ships.Clear(); // undefended
            defender.State.Resources = new ResourceBag(100_000, 0, 0).Milli();
            // Mute everyone's own attack rolls so the hand-crafted march is the
            // only battle in the galaxy (keeps the assertions deterministic).
            foreach (var b in galaxy.Bots) b.NextAttackRollTick = int.MaxValue / 2;

            galaxy.Marches.Add(new BotMarch
            {
                Id = galaxy.NextMarchId++,
                BotId = attacker.Id,
                TargetBotId = defender.Id,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 30 },
                From = attacker.HomeTile,
                To = defender.HomeTile,
                LaunchTick = 0,
                ArrivesAtTick = 100,
            });

            player.Tick = 120; // past arrival, before return
            BotSystem.Advance(player, galaxy, new SimEventBus());

            Assert.AreEqual(1, galaxy.Marches.Count, "march flies home after the battle");
            var march = galaxy.Marches[0];
            Assert.IsTrue(march.Resolved, "battle resolved at arrival");
            Assert.Greater(march.LootMilli.Total, 0, "survivors carry plunder");
            Assert.Greater(march.ReturnsAtTick, march.ArrivesAtTick);
            Assert.AreEqual(attacker.Id, defender.FocusTargetId,
                "the loser remembers who raided it (reclaim grudge)");
            Assert.IsTrue(galaxy.News.Count > 0, "the battle hit the wire");
            Assert.AreEqual(100, galaxy.News[^1].Tick, "news stamped at the ARRIVAL tick");

            long goldBefore = attacker.State.Resources.Gold;
            player.Tick = march.ReturnsAtTick + 60;
            BotSystem.Advance(player, galaxy, new SimEventBus());
            Assert.IsEmpty(galaxy.Marches, "round trip complete");
            Assert.Greater(attacker.State.Resources.Gold, goldBefore, "loot docked at return");
        }

        [Test]
        public void FarmedBot_PanicPortsAway_AfterRepeatedLosses()
        {
            var galaxy = SmallGalaxy();
            var bot = galaxy.Bots[0];
            var homeBefore = bot.HomeTile;
            var winnerReport = new GalaxyRoyale.Sim.Combat.BattleReport
            {
                Winner = GalaxyRoyale.Sim.Combat.BattleWinner.Attacker,
                Defender = new Dictionary<HullId, int>(),
                DefenderSurvivors = new Dictionary<HullId, int>(),
            };

            // Three lost defenses inside the window → the bot flees (user spec).
            for (int i = 0; i < BotSystem.PortLossThreshold; i++)
                BotSystem.ApplyPlayerRaid(galaxy, bot, winnerReport, new ResourceBag());

            Assert.AreNotEqual(homeBefore, bot.HomeTile, "the hounded bot ported away");
            Assert.Greater(bot.LastPortTick, -1, "port tick recorded");
            Assert.AreEqual(0, bot.DefenseLossCount, "loss window reset after the port");
        }

        [Test]
        public void SpyIntel_CarriesFullBaseAndTechStack_ThroughTheCodec()
        {
            var galaxy = SmallGalaxy();
            var bot = galaxy.Bots[0];
            bot.State.Research[TechId.YieldOptimization] = 3;

            var snap = BotSystem.SnapshotOf(bot);
            Assert.AreEqual(3, snap.Research[TechId.YieldOptimization],
                "snapshot exposes the tech stack");
            Assert.GreaterOrEqual(snap.Buildings[BuildingId.CommandCenter], 1,
                "snapshot exposes building levels");

            // The recon mail (what RaidArrivals files) survives a save round-trip.
            var player = GameState.CreateNewGame(Spawn.GalaxySeed);
            player.Mailbox.Insert(0, new SpyReport
            {
                Id = 1, AtTick = 0, Target = bot.HomeTile, Subject = "Recon",
                Intel = new SpyIntel
                {
                    Kind = NodeKind.Camp,
                    Garrison = new Dictionary<HullId, int>(snap.Ships),
                    Research = new Dictionary<TechId, int>(snap.Research),
                    Buildings = new Dictionary<BuildingId, int>(snap.Buildings),
                },
            });
            var back = SaveManager.Unwrap(SaveCodec.Decode(
                SaveCodec.Encode(SaveManager.Wrap(player, 0, galaxy))))!.Value.state;
            var spy = (SpyReport)back.Mailbox[0];
            Assert.AreEqual(3, spy.Intel.Research![TechId.YieldOptimization]);
            Assert.AreEqual(snap.Buildings[BuildingId.CommandCenter],
                spy.Intel.Buildings![BuildingId.CommandCenter]);
        }

        [Test]
        public void EstimateFleetPower_RanksFleetsSanely()
        {
            var fighters = new Dictionary<HullId, int> { [HullId.Fighter] = 10 };
            var moreFighters = new Dictionary<HullId, int> { [HullId.Fighter] = 30 };
            var reapers = new Dictionary<HullId, int> { [HullId.Reaper] = 10 };
            Assert.Greater(BotSystem.EstimateFleetPower(moreFighters),
                BotSystem.EstimateFleetPower(fighters), "more ships → more power");
            Assert.Greater(BotSystem.EstimateFleetPower(reapers),
                BotSystem.EstimateFleetPower(moreFighters), "capital wing beats a fighter swarm");
            Assert.AreEqual(0, BotSystem.EstimateFleetPower(new Dictionary<HullId, int>()));
        }

        [Test]
        public void BotNames_AreStableAndUniqueAcrossTheFullRoster()
        {
            Assert.AreEqual(BotNames.NameOf(7), BotNames.NameOf(7));
            var seen = new HashSet<string>();
            for (int id = 1; id <= BotSystem.BotCount; id++)
            {
                string name = BotNames.NameOf(id);
                Assert.IsNotEmpty(name);
                Assert.IsTrue(seen.Add(name), $"duplicate commander name '{name}' (bot {id})");
            }
        }
    }
}
