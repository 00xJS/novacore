// The Pirate Dreadnought (world boss, 2026-09-28): its visits, your strikes,
// the commanders' strikes, the clan damage race and the rewards.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class BossTests
    {
        /// <summary>A quiet galaxy: no commander thinks, rolls or strikes unless a test says so.</summary>
        static (GameState player, BotGalaxy galaxy) Setup(int bots = 30)
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, bots);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            player.Ships[HullId.Fighter] = 600;
            player.Ships[HullId.Cruiser] = 200;
            player.Resources = new ResourceBag(1_000_000, 1_000_000, 10_000_000).Milli();
            foreach (var bot in galaxy.Bots)
            {
                bot.LastThinkTick = int.MaxValue / 4;
                bot.NextAttackRollTick = int.MaxValue / 4;
                bot.CachedMight = 0; // under the shield: nobody flies at the dreadnought
                bot.ClanId = 0;
            }
            galaxy.Clans.Clear();
            galaxy.NextPoliticsTick = int.MaxValue / 4;
            galaxy.Core.NextRollTick = int.MaxValue / 4;
            galaxy.Core.GuardiansRebuildTick = int.MaxValue / 4;
            return (player, galaxy);
        }

        static void To(GameState player, BotGalaxy galaxy, SimEventBus bus, int tick)
        {
            player.Tick = tick;
            MarchSystem.Tick(player, bus);
            BotSystem.Advance(player, galaxy, bus);
        }

        /// <summary>Bring the first dreadnought in (it's scheduled on the first tick).</summary>
        static BossState Arrive(GameState player, BotGalaxy galaxy, SimEventBus bus)
        {
            To(player, galaxy, bus, 1);
            To(player, galaxy, bus, 1 + BossSystem.FirstVisitDelaySec);
            Assert.IsTrue(galaxy.Boss.Active);
            return galaxy.Boss;
        }

        [Test]
        public void ADreadnoughtArrives_OnSchedule_AndLeavesADayLater()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var seen = new List<SimEvent>();
            bus.Subscribe(e => { if (e is BossAppeared || e is BossDeparted) seen.Add(e); });

            To(player, galaxy, bus, 1);
            Assert.IsFalse(galaxy.Boss.Active);
            Assert.AreEqual(1 + BossSystem.FirstVisitDelaySec, galaxy.Boss.NextVisitTick);
            var boss = Arrive(player, galaxy, bus);

            double fromCore = TileXY.Distance(boss.Tile, CoreSystem.CoreTile);
            Assert.That(fromCore, Is.InRange(BossSystem.MinCoreDist - 1, BossSystem.MaxCoreDist + 1));
            Assert.GreaterOrEqual(boss.MaxHp, BossSystem.MinHull);
            Assert.AreEqual(boss.MaxHp, boss.Hp);
            Assert.AreEqual(boss.ArrivedTick + BossSystem.VisitSec, boss.LeavesTick);
            Assert.IsInstanceOf<BossAppeared>(seen.Single());
            Assert.That(galaxy.News.Last().Text, Does.Contain("Pirate Dreadnought"));

            To(player, galaxy, bus, boss.LeavesTick);
            Assert.IsFalse(boss.Active);
            Assert.AreEqual(boss.LeavesTick + BossSystem.GapSec, boss.NextVisitTick);
            var left = (BossDeparted)seen.Last();
            Assert.IsFalse(left.Killed);
            Assert.AreEqual(0, left.RewardDM, "you never struck it");
        }

        [Test]
        public void YourStrike_WearsTheHullDown_AndBringsBackSalvage()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var boss = Arrive(player, galaxy, bus);
            var fleet = new Dictionary<HullId, int> { [HullId.Fighter] = 600, [HullId.Cruiser] = 200 };
            var forecast = BossSystem.Forecast(player, galaxy, fleet);
            Assert.Greater(forecast.Damage, 0);
            Assert.IsFalse(forecast.Killed);

            Assert.IsTrue(BossSystem.SendStrike(player, galaxy, fleet, out int id).Ok);
            var march = player.Marches.Single(m => m.Id == id);
            Assert.AreEqual(boss.Visit, march.TargetFleetId);
            To(player, galaxy, bus, march.ArrivesAtTick);

            Assert.AreEqual(boss.MaxHp - forecast.Damage, boss.Hp, "the forecast is exact");
            Assert.AreEqual(forecast.Damage, boss.Damage[0]);
            var report = player.Mailbox.OfType<BossReport>().Single();
            Assert.AreEqual(BossReportKind.Strike, report.Kind);
            Assert.AreEqual(forecast.Damage, report.Damage);
            CollectionAssert.AreEquivalent(forecast.Survivors, report.Survivors);
            Assert.AreEqual(MarchPhase.Returning, march.Phase);
            Assert.Greater(march.Cargo.Total, 0, "salvage rides home");
            Assert.LessOrEqual(march.Cargo.Total, MarchSystem.FleetCargoCap(march.Ships));
            Assert.AreEqual(1, player.Stats.BossStrikes);
            Assert.AreEqual(forecast.Damage, player.Stats.BossDamage);
            Assert.AreEqual((forecast.Damage, 1, 1), BossSystem.YourStanding(galaxy));
        }

        [Test]
        public void TheFinalBlow_EndsTheVisit_AndPaysByShare()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var boss = Arrive(player, galaxy, bus);
            var rival = galaxy.Bots[0];
            boss.Damage[rival.Id] = 30_000;
            boss.Hp = 10_000;
            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 200 }; // 14,000 a round
            Assert.IsTrue(BossSystem.SendStrike(player, galaxy, fleet, out int id).Ok);
            int dm = player.Premium.DarkMatter;
            To(player, galaxy, bus, player.Marches.Single(m => m.Id == id).ArrivesAtTick);

            Assert.IsFalse(boss.Active);
            Assert.AreEqual(0, boss.Hp);
            Assert.IsTrue(boss.LastKilled);
            Assert.AreEqual(1, player.Stats.BossFinalBlows);
            // 10,000 of 40,000 damage: a quarter of the pool, plus the final blow.
            int share = (int)System.Math.Round(BossSystem.PoolDMKilled * 0.25);
            Assert.AreEqual(share + BossSystem.FinalBlowDM, player.Premium.DarkMatter - dm);
            var result = player.Mailbox.OfType<BossReport>().First(r => r.Kind == BossReportKind.Result);
            Assert.IsTrue(result.Killed);
            Assert.AreEqual(2, result.Rank);
            Assert.AreEqual(2, result.Of);
            Assert.AreEqual(share, result.RewardDM);
            Assert.That(galaxy.News.Last().Text, Does.Contain("final blow"));
        }

        [Test]
        public void TheTopClan_GetsItsBonus()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var boss = Arrive(player, galaxy, bus);
            var mate = galaxy.Bots[1];
            var clan = new Clan { Id = 7, Name = "Void Runners", Tag = "VOID", LeaderId = mate.Id };
            galaxy.Clans.Add(clan);
            mate.ClanId = 7;
            player.ClanId = 7;
            var loner = galaxy.Bots[2];
            boss.Damage[mate.Id] = 20_000;
            boss.Damage[loner.Id] = 25_000; // more than either of you — but no clan
            boss.Hp = 5_000;

            var race = BossSystem.ClanRace(player, galaxy);
            Assert.AreEqual(0, race[0].ClanId, "unaffiliated commanders count together");
            Assert.AreEqual(7, race[1].ClanId);
            Assert.IsTrue(race[1].Yours);

            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 200 };
            Assert.IsTrue(BossSystem.SendStrike(player, galaxy, fleet, out int id).Ok);
            int dm = player.Premium.DarkMatter;
            To(player, galaxy, bus, player.Marches.Single(m => m.Id == id).ArrivesAtTick);
            var result = player.Mailbox.OfType<BossReport>().First(r => r.Kind == BossReportKind.Result);
            Assert.AreEqual("[VOID] Void Runners", result.TopClan, "the top CLAN, not the unaffiliated pile");
            int share = (int)System.Math.Round(BossSystem.PoolDMKilled * (5_000 / 50_000.0));
            Assert.AreEqual(share + BossSystem.TopClanDM, result.RewardDM);
            Assert.AreEqual(share + BossSystem.TopClanDM + BossSystem.FinalBlowDM, player.Premium.DarkMatter - dm);
        }

        [Test]
        public void ACommandersStrike_WearsItDown_AndFliesHomeWithSalvage()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var boss = Arrive(player, galaxy, bus);
            var bot = galaxy.Bots[3];
            var ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 100 };
            galaxy.Marches.Add(new BotMarch
            {
                Id = galaxy.NextMarchId++, BotId = bot.Id, TargetBotId = boss.Visit, Kind = BotMarchKind.BossStrike,
                Ships = ships, From = bot.HomeTile, To = boss.Tile, LaunchTick = player.Tick, ArrivesAtTick = player.Tick + 60,
            });
            To(player, galaxy, bus, player.Tick + 60);
            Assert.Greater(boss.Damage[bot.Id], 0);
            Assert.AreEqual(boss.MaxHp - boss.Damage[bot.Id], boss.Hp);
            var march = galaxy.Marches.Single(m => m.BotId == bot.Id);
            Assert.IsTrue(march.Resolved, "flying home");
            Assert.Greater(march.LootMilli.Total, 0);
        }

        [Test]
        public void AStrikeThatFindsItGone_ComesHome()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var boss = Arrive(player, galaxy, bus);
            var fleet = new Dictionary<HullId, int> { [HullId.Fighter] = 50 };
            Assert.IsTrue(BossSystem.SendStrike(player, galaxy, fleet, out int id).Ok);
            var march = player.Marches.Single(m => m.Id == id);
            boss.LeavesTick = player.Tick + 5; // it jumps away early
            To(player, galaxy, bus, march.ArrivesAtTick);
            Assert.IsFalse(boss.Active);
            Assert.AreEqual(MarchPhase.Returning, march.Phase);
            CollectionAssert.AreEquivalent(fleet, march.Ships);
            Assert.AreEqual(BossReportKind.Missed, player.Mailbox.OfType<BossReport>().Single().Kind);
        }

        [Test]
        public void YouCantLaunch_AStrikeThatWouldArriveTooLate()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            Assert.IsFalse(BossSystem.CanStrike(player, galaxy, new Dictionary<HullId, int> { [HullId.Fighter] = 5 }).Ok,
                "nothing to strike yet");
            var boss = Arrive(player, galaxy, bus);
            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 5 };
            boss.LeavesTick = player.Tick + MarchSystem.FlightSeconds(player, fleet, boss.Tile) - 1;
            var res = BossSystem.SendStrike(player, galaxy, fleet, out _);
            Assert.IsFalse(res.Ok);
            StringAssert.Contains("gone before", res.Reason);
        }

        [Test]
        public void TheDreadnought_SurvivesASave()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var boss = Arrive(player, galaxy, bus);
            boss.Damage[0] = 1234;
            boss.Damage[galaxy.Bots[0].Id] = 5678;
            Assert.IsTrue(BossSystem.SendStrike(player, galaxy, new Dictionary<HullId, int> { [HullId.Cruiser] = 150 }, out int id).Ok);
            To(player, galaxy, bus, player.Marches.Single(m => m.Id == id).ArrivesAtTick);

            var g2 = SaveCodec.DecodeBots(SaveCodec.EncodeBots(galaxy));
            Assert.AreEqual(boss.Visit, g2.Boss.Visit);
            Assert.IsTrue(g2.Boss.Active);
            Assert.AreEqual(boss.Tile, g2.Boss.Tile);
            Assert.AreEqual(boss.Hp, g2.Boss.Hp);
            Assert.AreEqual(boss.MaxHp, g2.Boss.MaxHp);
            Assert.AreEqual(boss.Cannon, g2.Boss.Cannon);
            Assert.AreEqual(boss.LeavesTick, g2.Boss.LeavesTick);
            CollectionAssert.AreEquivalent(boss.Damage, g2.Boss.Damage);

            var s2 = SaveCodec.DecodeState(SaveCodec.EncodeState(player));
            var r1 = player.Mailbox.OfType<BossReport>().Single();
            var r2 = s2.Mailbox.OfType<BossReport>().Single();
            Assert.AreEqual(r1.Damage, r2.Damage);
            Assert.AreEqual(r1.HpAfter, r2.HpAfter);
            CollectionAssert.AreEquivalent(r1.Survivors, r2.Survivors);
            Assert.AreEqual(r1.Salvage.Total, r2.Salvage.Total);
            Assert.AreEqual(player.Stats.BossDamage, s2.Stats.BossDamage);
            var home = s2.Marches.Single();
            Assert.AreEqual(MarchMission.Boss, home.Mission);
            Assert.AreEqual(boss.Visit, home.TargetFleetId);
        }

        /// <summary>Balance readout (PACE): the first dreadnought in a full galaxy with
        /// no help from you — how many commanders strike it, and when it breaks.</summary>
        [Test]
        public void Pace_DreadnoughtAgainstTheGalaxy()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            var bus = new SimEventBus();
            var boss = galaxy.Boss;
            int strikes = 0, killedAt = -1;
            var launched = new HashSet<int>();
            for (int t = 60; t <= BossSystem.FirstVisitDelaySec + BossSystem.VisitSec + 60; t += 120)
            {
                player.Tick = t;
                BotSystem.Advance(player, galaxy, bus);
                ProgressionSystem.Advance(player, galaxy, bus); // clan politics: the race is between real clans
                foreach (var m in galaxy.Marches)
                    if (m.Kind == BotMarchKind.BossStrike && launched.Add(m.Id)) strikes++;
                if (killedAt < 0 && boss.Visit == 1 && !boss.Active && boss.LastKilled) killedAt = t;
            }
            var race = BossSystem.ClanRace(player, galaxy);
            string top = string.Join(" / ", race.Take(3).Select(r => $"{r.Name} {r.Damage:N0}"));
            TestContext.Out.WriteLine($"PACE dreadnought: hull {boss.MaxHp:N0} · guns {boss.Cannon:N0}/round · " +
                $"{strikes} commander strikes · " +
                (killedAt >= 0 ? $"destroyed {((killedAt - BossSystem.FirstVisitDelaySec) / 3600.0):0.0} h after arriving"
                    : $"escaped with {BossSystem.HullShare(boss) * 100:0}% hull") + $" · top: {top}");
            Assert.Greater(strikes, 10, "the commanders go after it");
        }
    }
}
