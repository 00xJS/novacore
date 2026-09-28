// Joint strikes (user request 2026-09-28): clanmates' wings on your raids,
// intercepting fleets in flight, and garrisons at each other's colonies.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class StrikeTests
    {
        static (GameState player, BotGalaxy galaxy) Setup(int bots = 60)
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, bots);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            player.Ships[HullId.Fighter] = 400; // an established empire, past the rookie shield
            player.Ships[HullId.Cruiser] = 200;
            // Plenty of fuel: these tests are about who fights whom, not helium.
            player.Resources = new ResourceBag(1_000_000, 1_000_000, 10_000_000).Milli();
            foreach (var bot in galaxy.Bots)
            {
                // Park the rivals: no think steps or attack rolls during a test.
                bot.LastThinkTick = int.MaxValue / 4;
                bot.NextAttackRollTick = int.MaxValue / 4;
                bot.ClanId = 0;
            }
            galaxy.Clans.Clear();
            galaxy.NextPoliticsTick = int.MaxValue / 4;
            return (player, galaxy);
        }

        /// <summary>Found a clan and recruit <paramref name="count"/> peaceable rivals into it.</summary>
        static List<BotEmpire> ClanOf(GameState player, BotGalaxy galaxy, int count)
        {
            Assert.IsTrue(ClanSystem.Found(player, galaxy, "Test Fleet", "TST").Ok);
            long mine = PowerSystem.ComputePower(player);
            var mates = new List<BotEmpire>();
            foreach (var bot in galaxy.Bots)
            {
                if (mates.Count == count) break;
                if (ClanSystem.IsLoneWolf(player.Seed, bot)) continue;
                bot.CachedMight = mine;
                Assert.IsTrue(ClanSystem.Invite(player, galaxy, bot.Id).Ok);
                mates.Add(bot);
            }
            Assert.AreEqual(count, mates.Count);
            return mates;
        }

        static TileXY Off(TileXY t, int dx, int dy) => new(t.X + dx, t.Y + dy);

        static BotEmpire Outsider(GameState player, BotGalaxy galaxy, params BotEmpire[] not) =>
            galaxy.Bots.First(b => b.ClanId == 0 && !not.Contains(b));

        /// <summary>Walk the clock to <paramref name="tick"/>: your marches, then the galaxy.</summary>
        static void To(GameState player, BotGalaxy galaxy, SimEventBus bus, int tick)
        {
            player.Tick = tick;
            MarchSystem.Tick(player, bus);
            BotSystem.Advance(player, galaxy, bus);
        }

        // ---------- intercepts ----------

        [Test]
        public void PlanIntercept_FindsTheEarliestPointYouCanReach()
        {
            var (player, galaxy) = Setup();
            var track = new StrikeSystem.FleetTrack
            {
                BotId = galaxy.Bots[0].Id,
                From = Off(player.HomeTile, 600, 0), To = player.HomeTile,
                DepartTick = 0, ArriveTick = 3000,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 10 },
            };
            var ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 50 };
            var plan = StrikeSystem.PlanIntercept(player, track, ships);
            Assert.IsTrue(plan.Ok, plan.Reason);
            Assert.Less(plan.EngageTick, track.ArriveTick, "before they land");
            Assert.LessOrEqual(player.Tick + MarchSystem.FlightSeconds(player, ships, plan.Point), plan.EngageTick);
            var earlier = track.At(plan.EngageTick - 1);
            Assert.Greater(player.Tick + MarchSystem.FlightSeconds(player, ships, earlier), plan.EngageTick - 1,
                "the earliest point you can make");
            Assert.Less(Position.Distance(plan.Point, track.At(plan.EngageTick)), 1e-6, "on their path");

            // A fleet that lands far away before you could get anywhere near it.
            var gone = new StrikeSystem.FleetTrack
            {
                BotId = galaxy.Bots[0].Id,
                From = Off(player.HomeTile, 1500, 0), To = Off(player.HomeTile, 1500, 60),
                DepartTick = 0, ArriveTick = 60,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 10 },
            };
            Assert.IsFalse(StrikeSystem.PlanIntercept(player, gone, ships).Ok);
        }

        [Test]
        public void Intercept_BreaksARaidOnYou_BeforeItLands()
        {
            var (player, galaxy) = Setup();
            var raider = galaxy.Bots[0];
            var atk = new BotAttack
            {
                Id = galaxy.NextAttackId++, BotId = raider.Id, IsFleet = true,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 20 },
                LaunchTick = 0, ArrivesAtTick = 3000, From = Off(player.HomeTile, 600, 0),
            };
            galaxy.Inbound.Add(atk);
            var track = StrikeSystem.Track(player, galaxy, true, atk.Id)!;
            var sent = StrikeSystem.LaunchIntercept(player, galaxy, track,
                new Dictionary<HullId, int> { [HullId.Cruiser] = 200 }, false, out int marchId, out _);
            Assert.IsTrue(sent.Ok, sent.Reason);
            var march = player.Marches.Single(m => m.Id == marchId);
            Assert.AreEqual(0, player.Ships[HullId.Cruiser], "the fleet left the dock");

            var bus = new SimEventBus();
            To(player, galaxy, bus, march.EngageTick);
            Assert.IsFalse(galaxy.Inbound.Exists(a => a.Id == atk.Id), "the raid is broken");
            var mail = player.Mailbox.OfType<BattleMailReport>().First();
            StringAssert.StartsWith("Intercept", mail.Subject);
            Assert.AreEqual(BattleWinner.Attacker, mail.Report.Winner);
            Assert.AreEqual(MarchPhase.Returning, march.Phase, "flying home from the fight");
            Assert.AreEqual(1, player.Stats.BattlesWon);

            To(player, galaxy, bus, 3100);
            Assert.IsFalse(player.Mailbox.OfType<BattleMailReport>().Any(m => m.Defending), "nothing lands on the colony");
            Assert.IsTrue(StrikeSystem.CanIntercept(player, galaxy, track).Ok);
        }

        [Test]
        public void Intercept_SavesAClanmate_FromARaid()
        {
            var (player, galaxy) = Setup();
            var mate = ClanOf(player, galaxy, 1)[0];
            mate.State.HomeTile = Off(player.HomeTile, 100, 0);
            mate.State.Ships.Clear();
            mate.State.Ships[HullId.Fighter] = 5;
            var raider = Outsider(player, galaxy, mate);
            var raid = new BotMarch
            {
                Id = galaxy.NextMarchId++, BotId = raider.Id, TargetBotId = mate.Id,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 20 },
                From = Off(mate.HomeTile, 600, 0), To = mate.HomeTile, LaunchTick = 0, ArrivesAtTick = 3000,
            };
            galaxy.Marches.Add(raid);
            var result = StrikeSystem.LaunchIntercept(player, galaxy, StrikeSystem.Track(player, galaxy, false, raid.Id)!,
                new Dictionary<HullId, int> { [HullId.Cruiser] = 200 }, false, out int marchId, out _);
            Assert.IsTrue(result.Ok, result.Reason);

            var hunt = player.Marches.Single(m => m.Id == marchId);
            var meet = StrikeSystem.TileOf(hunt.LegTo);
            var bus = new SimEventBus();
            To(player, galaxy, bus, hunt.EngageTick);
            Assert.IsFalse(galaxy.Marches.Contains(raid), "the raiders were wiped out");
            var mail = player.Mailbox.OfType<BattleMailReport>().First();
            Assert.AreEqual(meet, mail.Target, "the report is filed where the fleets met");
            Assert.AreNotEqual(player.HomeTile, mail.Target);
            To(player, galaxy, bus, 3100);
            Assert.AreEqual(5, mate.State.Ships[HullId.Fighter], "the raid never landed");
            Assert.IsFalse(galaxy.News.Exists(n => !n.IsBulletin && n.DefenderId == mate.Id));
            Assert.IsTrue(galaxy.News.Exists(n => n.IsBulletin && n.Text!.Contains("intercepted")));
        }

        [Test]
        public void Intercept_RetakesPlunder_FromARaiderFlyingHome()
        {
            var (player, galaxy) = Setup();
            var raider = galaxy.Bots[0];
            var victim = galaxy.Bots[1];
            // Their way home passes right over your colony.
            var loaded = new BotMarch
            {
                Id = galaxy.NextMarchId++, BotId = raider.Id, TargetBotId = victim.Id,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 10 },
                From = Off(player.HomeTile, 400, 0), To = Off(player.HomeTile, -400, 0),
                LaunchTick = -4000, ArrivesAtTick = 0, ReturnsAtTick = 4000, Resolved = true,
                LootMilli = new ResourceBag(30_000, 20_000, 10_000).Milli(),
            };
            galaxy.Marches.Add(loaded);
            var track = StrikeSystem.Track(player, galaxy, false, loaded.Id)!;
            Assert.AreEqual(1, track.Leg, "homeward");
            var result = StrikeSystem.LaunchIntercept(player, galaxy, track,
                new Dictionary<HullId, int> { [HullId.Cruiser] = 200 }, false, out int marchId, out _);
            Assert.IsTrue(result.Ok, result.Reason);
            var march = player.Marches.Single(m => m.Id == marchId);

            To(player, galaxy, new SimEventBus(), march.EngageTick);
            Assert.Greater(march.Cargo.Total, 0, "plunder retaken");
            Assert.LessOrEqual(march.Cargo.Total, new ResourceBag(30_000, 20_000, 10_000).Milli().Total);
            var mail = player.Mailbox.OfType<BattleMailReport>().First();
            StringAssert.Contains("plunder retaken", mail.Subject);
            Assert.AreEqual(march.Cargo.Total, mail.Report.Loot.Total);
        }

        [Test]
        public void Intercept_Misses_WhenTheFleetIsGone()
        {
            var (player, galaxy) = Setup();
            var raider = galaxy.Bots[0];
            var raid = new BotMarch
            {
                Id = galaxy.NextMarchId++, BotId = raider.Id, TargetBotId = galaxy.Bots[1].Id,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 20 },
                From = Off(player.HomeTile, 600, 0), To = Off(player.HomeTile, -100, 0), LaunchTick = 0, ArrivesAtTick = 3000,
            };
            galaxy.Marches.Add(raid);
            Assert.IsTrue(StrikeSystem.LaunchIntercept(player, galaxy, StrikeSystem.Track(player, galaxy, false, raid.Id)!,
                new Dictionary<HullId, int> { [HullId.Cruiser] = 50 }, false, out int marchId, out _).Ok);
            galaxy.Marches.Remove(raid); // someone else got there first

            var bus = new SimEventBus();
            var seen = new List<SimEvent>();
            bus.Subscribe(seen.Add);
            var march = player.Marches.Single(m => m.Id == marchId);
            To(player, galaxy, bus, march.EngageTick);
            Assert.IsTrue(seen.OfType<InterceptMissed>().Any());
            Assert.AreEqual(MarchPhase.Returning, march.Phase);
            Assert.AreEqual(50, march.Ships[HullId.Cruiser], "no fight, no losses");
            Assert.IsFalse(player.Mailbox.OfType<BattleMailReport>().Any());
        }

        [Test]
        public void Intercept_RefusesYourClanmatesFleets_AndDuplicates()
        {
            var (player, galaxy) = Setup();
            var mate = ClanOf(player, galaxy, 1)[0];
            var victim = Outsider(player, galaxy, mate);
            var theirs = new BotMarch
            {
                Id = galaxy.NextMarchId++, BotId = mate.Id, TargetBotId = victim.Id,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 20 },
                From = Off(player.HomeTile, 600, 0), To = Off(player.HomeTile, -100, 0), LaunchTick = 0, ArrivesAtTick = 3000,
            };
            galaxy.Marches.Add(theirs);
            Assert.IsFalse(StrikeSystem.CanIntercept(player, galaxy, StrikeSystem.Track(player, galaxy, false, theirs.Id)!).Ok);

            var raider = Outsider(player, galaxy, mate, victim);
            var raid = new BotMarch
            {
                Id = galaxy.NextMarchId++, BotId = raider.Id, TargetBotId = victim.Id,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 20 },
                From = Off(player.HomeTile, 600, 0), To = Off(player.HomeTile, -100, 0), LaunchTick = 0, ArrivesAtTick = 3000,
            };
            galaxy.Marches.Add(raid);
            var track = StrikeSystem.Track(player, galaxy, false, raid.Id)!;
            Assert.IsTrue(StrikeSystem.LaunchIntercept(player, galaxy, track,
                new Dictionary<HullId, int> { [HullId.Cruiser] = 50 }, false, out _, out _).Ok);
            Assert.IsFalse(StrikeSystem.CanIntercept(player, galaxy, track).Ok, "one intercept per fleet");
        }

        // ---------- joint strikes ----------

        static (BotEmpire target, List<BotEmpire> mates) JointSetup(GameState player, BotGalaxy galaxy)
        {
            var mates = ClanOf(player, galaxy, 2);
            var target = Outsider(player, galaxy, mates.ToArray());
            target.State.HomeTile = Off(player.HomeTile, 300, 0);
            target.State.Ships.Clear();
            target.State.Ships[HullId.Fighter] = 10;
            target.State.Resources = new ResourceBag(300_000, 300_000, 300_000).Milli();
            mates[0].State.HomeTile = Off(target.HomeTile, -100, 60);
            mates[1].State.HomeTile = Off(target.HomeTile, 80, -90);
            foreach (var m in mates)
            {
                m.State.Ships.Clear();
                m.State.Ships[HullId.Cruiser] = 400; // a quarter (100) flies
            }
            return (target, mates);
        }

        [Test]
        public void JointStrike_WingsLandWithYou_AndShareThePlunder()
        {
            var (player, galaxy) = Setup();
            var (target, mates) = JointSetup(player, galaxy);
            var sent = new Dictionary<HullId, int> { [HullId.Cruiser] = 100 };
            Assert.IsTrue(MarchSystem.SendRaidMarch(player, sent, target.HomeTile, out int marchId).Ok);
            var march = player.Marches.Single(m => m.Id == marchId);

            var wings = StrikeSystem.StrikeWings(player, galaxy, target.HomeTile, march.ArrivesAtTick, target.Id);
            Assert.AreEqual(2, wings.Count);
            var launched = StrikeSystem.LaunchWings(player, galaxy, wings, BotMarchKind.Escort, marchId, target.Id,
                target.HomeTile, march.ArrivesAtTick, ClanSystem.RaidSupportCooldownSec);
            foreach (var w in launched)
            {
                Assert.AreEqual(march.ArrivesAtTick, w.ArrivesAtTick, "everyone lands together");
                Assert.GreaterOrEqual(w.LaunchTick, 0);
            }
            Assert.AreEqual(300, mates[0].State.Ships[HullId.Cruiser], "a quarter left the dock");
            Assert.IsEmpty(StrikeSystem.StrikeWings(player, galaxy, target.HomeTile, march.ArrivesAtTick, target.Id),
                "they rest after a sortie");

            // The galaxy doesn't fight the wings as raids of their own — they wait for yours.
            To(player, galaxy, new SimEventBus(), march.ArrivesAtTick + 1);
            Assert.AreEqual(10, target.State.Ships[HullId.Fighter]);
            Assert.IsTrue(launched.All(w => !w.Resolved));

            long before = target.State.Resources.Total;
            var mail = StrikeSystem.ResolveRaid(player, galaxy, target, marchId, sent, target.HomeTile);
            Assert.AreEqual(BattleWinner.Attacker, mail.Report.Winner);
            StringAssert.StartsWith("Joint strike", mail.Subject);
            Assert.AreEqual(200, mail.AllyShips![HullId.Cruiser]);
            Assert.AreEqual(300, mail.Report.Attacker[HullId.Cruiser], "one line: yours + your clan's");
            Assert.Greater(mail.AllyLootMilli, 0);
            Assert.AreEqual(before - target.State.Resources.Total, mail.Report.Loot.Total + mail.AllyLootMilli,
                "every bit of plunder lands with someone");
            Assert.IsTrue(launched.All(w => w.Resolved && w.LootMilli.Total > 0));

            // Home they go, plunder and all.
            long mateGold = mates[0].State.Resources.Total;
            To(player, galaxy, new SimEventBus(), launched.Max(w => w.ReturnsAtTick) + 1);
            Assert.Greater(mates[0].State.Resources.Total, mateGold);
            Assert.Greater(mates[0].State.Ships[HullId.Cruiser], 300, "survivors docked again");
        }

        [Test]
        public void JointStrike_CalledOff_SendsTheWingsHomeUntouched()
        {
            var (player, galaxy) = Setup();
            var (target, mates) = JointSetup(player, galaxy);
            var sent = new Dictionary<HullId, int> { [HullId.Cruiser] = 100 };
            Assert.IsTrue(MarchSystem.SendRaidMarch(player, sent, target.HomeTile, out int marchId).Ok);
            var march = player.Marches.Single(m => m.Id == marchId);
            var launched = StrikeSystem.LaunchWings(player, galaxy,
                StrikeSystem.StrikeWings(player, galaxy, target.HomeTile, march.ArrivesAtTick, target.Id),
                BotMarchKind.Escort, marchId, target.Id, target.HomeTile, march.ArrivesAtTick, ClanSystem.RaidSupportCooldownSec);
            Assert.AreEqual(2, launched.Count);

            player.Tick = march.ArrivesAtTick - 5;
            Assert.IsTrue(MarchSystem.RecallMarch(player, marchId).Ok);
            StrikeSystem.TurnEscortsHome(galaxy, marchId, player.Tick); // what RaidArrivals does on a recall
            Assert.IsTrue(launched.All(w => w.Resolved || !galaxy.Marches.Contains(w)));

            To(player, galaxy, new SimEventBus(), march.ArrivesAtTick + 2000);
            foreach (var m in mates) Assert.AreEqual(400, m.State.Ships[HullId.Cruiser], "every ship back on the dock");
        }

        // ---------- garrisons ----------

        [Test]
        public void ClanGarrison_StandsGuard_FightsARaidOnYou_ThenGoesHome()
        {
            var (player, galaxy) = Setup();
            var mate = ClanOf(player, galaxy, 1)[0];
            mate.State.HomeTile = Off(player.HomeTile, 60, 0);
            mate.State.Ships.Clear();
            mate.State.Ships[HullId.Cruiser] = 200; // 20% (40) stands guard

            Assert.IsTrue(StrikeSystem.RequestGarrison(player, galaxy, out int wings).Ok);
            Assert.AreEqual(1, wings);
            Assert.IsFalse(StrikeSystem.CanRequestGarrison(player, galaxy).Ok, "one call every twelve hours");
            var wing = StrikeSystem.GarrisonAtPlayer(galaxy).Single();
            Assert.AreEqual(160, mate.State.Ships[HullId.Cruiser]);

            var bus = new SimEventBus();
            var seen = new List<SimEvent>();
            bus.Subscribe(seen.Add);
            To(player, galaxy, bus, wing.ArrivesAtTick);
            Assert.IsTrue(seen.OfType<ClanGarrisonArrived>().Any());

            player.Ships.Clear();
            player.Ships[HullId.Cruiser] = 10;
            var raider = Outsider(player, galaxy, mate);
            galaxy.Inbound.Add(new BotAttack
            {
                Id = galaxy.NextAttackId++, BotId = raider.Id, IsFleet = true,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 25 },
                LaunchTick = player.Tick, ArrivesAtTick = player.Tick + 50, From = raider.HomeTile,
            });
            To(player, galaxy, bus, player.Tick + 60);
            var mail = player.Mailbox.OfType<BattleMailReport>().Single(m => m.Defending);
            Assert.AreEqual(40, mail.AllyShips![HullId.Cruiser], "the garrison fights — and the same clanmate doesn't also scramble");
            Assert.AreEqual(mate.Name, mail.AllyNames);
            Assert.AreEqual(160, mate.State.Ships[HullId.Cruiser], "the dock only lost what left for guard duty");

            int left = galaxy.Marches.Contains(wing) && wing.Ships.TryGetValue(HullId.Cruiser, out var l) ? l : 0;
            To(player, galaxy, bus, wing.ArrivesAtTick + StrikeSystem.GarrisonHoldSec + 1);
            if (left > 0) Assert.IsTrue(wing.Resolved, "the watch is over");
            To(player, galaxy, bus, wing.ArrivesAtTick + StrikeSystem.GarrisonHoldSec + 3600);
            Assert.AreEqual(160 + left, mate.State.Ships[HullId.Cruiser], "the survivors came home");
        }

        /// <summary>Offline catch-up resolves hours in one step: a raid that landed
        /// during the watch still meets the garrison; one after it doesn't.</summary>
        [TestCase(3600, true)]
        [TestCase(StrikeSystem.GarrisonHoldSec + 600, false)]
        public void ClanGarrison_InCatchUp_FightsOnlyTheRaidsOfItsWatch(int impactAfterLanding, bool onGuard)
        {
            var (player, galaxy) = Setup();
            var mate = ClanOf(player, galaxy, 1)[0];
            mate.State.HomeTile = Off(player.HomeTile, 60, 0);
            mate.State.Ships.Clear();
            mate.State.Ships[HullId.Cruiser] = 200; // 40 stand guard; 15% of the 160 left would scramble
            Assert.IsTrue(StrikeSystem.RequestGarrison(player, galaxy, out _).Ok);
            var wing = StrikeSystem.GarrisonAtPlayer(galaxy).Single();

            player.Ships.Clear();
            player.Ships[HullId.Cruiser] = 10;
            var raider = Outsider(player, galaxy, mate);
            int impact = wing.ArrivesAtTick + impactAfterLanding;
            galaxy.Inbound.Add(new BotAttack
            {
                Id = galaxy.NextAttackId++, BotId = raider.Id, IsFleet = true,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 25 },
                LaunchTick = impact - 50, ArrivesAtTick = impact, From = raider.HomeTile,
            });
            // One catch-up step from before the garrison landed to well past its watch.
            To(player, galaxy, new SimEventBus(), wing.ArrivesAtTick + StrikeSystem.GarrisonHoldSec + 900);

            var mail = player.Mailbox.OfType<BattleMailReport>().Single(m => m.Defending);
            Assert.AreEqual(onGuard ? 40 : 24, mail.AllyShips![HullId.Cruiser],
                onGuard ? "the garrison was on guard when the raid landed" : "the watch was over — only the usual help came");
        }

        [Test]
        public void YourGarrison_FightsARaidOnYourClanmate_AndFollowsThem()
        {
            var (player, galaxy) = Setup();
            var mate = ClanOf(player, galaxy, 1)[0];
            mate.State.HomeTile = Off(player.HomeTile, 100, 0);
            mate.State.Ships.Clear();
            mate.State.Ships[HullId.Fighter] = 5;
            var guardFleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 100 };
            Assert.IsTrue(StrikeSystem.SendGarrison(player, galaxy, mate.Id, guardFleet, out int guardId).Ok);
            Assert.IsFalse(StrikeSystem.CanSendGarrison(player, galaxy, mate).Ok, "one garrison per colony");
            var guard = player.Marches.Single(m => m.Id == guardId);

            var bus = new SimEventBus();
            var seen = new List<SimEvent>();
            bus.Subscribe(seen.Add);
            To(player, galaxy, bus, guard.ArrivesAtTick);
            Assert.AreEqual(MarchPhase.Gathering, guard.Phase, "on guard");

            var raider = Outsider(player, galaxy, mate);
            galaxy.Marches.Add(new BotMarch
            {
                Id = galaxy.NextMarchId++, BotId = raider.Id, TargetBotId = mate.Id,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 30 },
                From = Off(mate.HomeTile, 500, 0), To = mate.HomeTile,
                LaunchTick = player.Tick, ArrivesAtTick = player.Tick + 100,
            });
            To(player, galaxy, bus, player.Tick + 120);
            Assert.IsTrue(seen.OfType<GarrisonFought>().Single().Held);
            var mail = player.Mailbox.OfType<BattleMailReport>().First();
            StringAssert.Contains("garrison", mail.Subject);
            Assert.AreEqual(mate.Id, mail.GuardedBotId, "a fight at their colony, not a raid on yours");
            Assert.AreEqual(100, mail.Report.Defender[HullId.Cruiser], "your garrison stood in their line");
            Assert.AreEqual(MarchPhase.Gathering, guard.Phase, "still on guard");

            // They leave the clan: your garrison comes home.
            mate.ClanId = 0;
            To(player, galaxy, bus, player.Tick + 1);
            Assert.AreEqual(MarchPhase.Returning, guard.Phase);
        }

        // ---------- bits ----------

        [Test]
        public void SplitLoot_ByCargoRoom_AddsUpToTheWhole()
        {
            var total = new ResourceBag(1001, 2003, 5);
            var shares = StrikeSystem.SplitLoot(total, new long[] { 100, 300, 600 });
            Assert.AreEqual(total.Gold, shares.Sum(s => s.Gold));
            Assert.AreEqual(total.Quartz, shares.Sum(s => s.Quartz));
            Assert.AreEqual(total.Helium, shares.Sum(s => s.Helium));
            Assert.AreEqual(600, shares[2].Gold, 1);
        }

        [Test]
        public void StrikeState_SurvivesASave()
        {
            var (player, galaxy) = Setup();
            galaxy.Marches.Add(new BotMarch
            {
                Id = 700, BotId = galaxy.Bots[0].Id, Kind = BotMarchKind.Garrison, LinkId = 0,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 40 },
                From = galaxy.Bots[0].HomeTile, To = player.HomeTile, LaunchTick = 5, ArrivesAtTick = 90,
            });
            galaxy.Marches.Add(new BotMarch
            {
                Id = 701, BotId = galaxy.Bots[1].Id, Kind = BotMarchKind.Escort, LinkId = 42, TargetBotId = 3,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 25 },
                From = galaxy.Bots[1].HomeTile, To = player.HomeTile, LaunchTick = 5, ArrivesAtTick = 90,
            });
            player.Marches.Add(new March
            {
                Id = 42, Phase = MarchPhase.Outbound, Mission = MarchMission.Intercept,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 50 },
                Node = Off(player.HomeTile, 10, 10), LegFrom = player.HomeTile, LegTo = new Position(56.25, 933.5),
                DepartedAtTick = 1, ArrivesAtTick = 777,
                TargetFleetId = 9, TargetInbound = true, TargetLeg = 1, EngageTick = 777,
            });
            player.Marches.Add(new March
            {
                Id = 43, Phase = MarchPhase.Gathering, Mission = MarchMission.Garrison, GuardEmpireId = 12,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 30 },
                Node = Off(player.HomeTile, 20, 0), LegFrom = Off(player.HomeTile, 20, 0), LegTo = Off(player.HomeTile, 20, 0),
                DepartedAtTick = 1, ArrivesAtTick = int.MaxValue,
            });
            player.ClanGarrisonReadyTick = 12345;
            player.Mailbox.Add(new BattleMailReport { Id = 9001, Subject = "Joint strike victory — X", Report = new BattleReport(), AllyLootMilli = 999 });
            player.Mailbox.Add(new BattleMailReport { Id = 9002, Subject = "Your garrison held Y's colony against Z", Report = new BattleReport(),
                Defending = true, AttackerBotId = 4, GuardedBotId = 12 });

            var file = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(player, 1000, galaxy)));
            var s = file.State;
            var g = file.Bots!;
            var guard = g.Marches.Single(m => m.Id == 700);
            Assert.AreEqual(BotMarchKind.Garrison, guard.Kind);
            var escort = g.Marches.Single(m => m.Id == 701);
            Assert.AreEqual(BotMarchKind.Escort, escort.Kind);
            Assert.AreEqual(42, escort.LinkId);
            Assert.IsFalse(escort.IsSpy);
            var hunt = s.Marches.Single(m => m.Id == 42);
            Assert.AreEqual(MarchMission.Intercept, hunt.Mission);
            Assert.AreEqual(9, hunt.TargetFleetId);
            Assert.IsTrue(hunt.TargetInbound);
            Assert.AreEqual(1, hunt.TargetLeg);
            Assert.AreEqual(777, hunt.EngageTick);
            Assert.AreEqual(56.25, hunt.LegTo.X, 1e-9);
            var post = s.Marches.Single(m => m.Id == 43);
            Assert.AreEqual(MarchMission.Garrison, post.Mission);
            Assert.AreEqual(12, post.GuardEmpireId);
            Assert.AreEqual(12345, s.ClanGarrisonReadyTick);
            Assert.AreEqual(999, ((BattleMailReport)s.Mailbox.First(m => m.Id == 9001)).AllyLootMilli);
            var guardMail = (BattleMailReport)s.Mailbox.First(m => m.Id == 9002);
            Assert.AreEqual(12, guardMail.GuardedBotId);
            Assert.IsTrue(guardMail.Defending);
        }
    }
}
