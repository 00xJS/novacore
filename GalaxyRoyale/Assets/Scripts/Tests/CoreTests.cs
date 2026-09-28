// The Galactic Core (endgame, 2026-09-28): assault it, hold it for tribute,
// and watch the simulated commanders take it from the guardians and from you.
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
    public class CoreTests
    {
        static (GameState player, BotGalaxy galaxy) Setup(int bots = 60)
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, bots);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            player.Ships[HullId.Fighter] = 400;
            player.Ships[HullId.Cruiser] = 300;
            player.Resources = new ResourceBag(1_000_000, 1_000_000, 10_000_000).Milli();
            foreach (var bot in galaxy.Bots)
            {
                bot.LastThinkTick = int.MaxValue / 4;
                bot.NextAttackRollTick = int.MaxValue / 4;
                bot.ClanId = 0;
            }
            galaxy.Clans.Clear();
            galaxy.NextPoliticsTick = int.MaxValue / 4;
            // A known, beatable garrison; no rebuilds or commander assaults unless a test wants them.
            galaxy.Core.Guardians = new Dictionary<HullId, int> { [HullId.Cruiser] = 20 };
            galaxy.Core.GuardiansRebuildTick = int.MaxValue / 4;
            galaxy.Core.NextRollTick = int.MaxValue / 4;
            return (player, galaxy);
        }

        static void To(GameState player, BotGalaxy galaxy, SimEventBus bus, int tick)
        {
            player.Tick = tick;
            MarchSystem.Tick(player, bus);
            BotSystem.Advance(player, galaxy, bus);
        }

        static March Assault(GameState player, BotGalaxy galaxy, Dictionary<HullId, int> ships)
        {
            var res = CoreSystem.SendToCore(player, galaxy, ships, false, out int id, out _);
            Assert.IsTrue(res.Ok, res.Reason);
            return player.Marches.Single(m => m.Id == id);
        }

        static BotEmpire StrongBot(GameState player, BotGalaxy galaxy, int index, int cruisers)
        {
            var bot = galaxy.Bots[index];
            bot.State.Ships.Clear();
            bot.State.Ships[HullId.Cruiser] = cruisers;
            bot.CachedMight = PowerSystem.ComputePower(bot.State);
            return bot;
        }

        [Test]
        public void GuardianFleet_MatchesTheTargetPower()
        {
            var fleet = CoreSystem.GuardianFleet(100_000);
            long power = BotSystem.EstimateFleetPower(fleet);
            Assert.AreEqual(100_000, power, 1_000);
            Assert.Greater(fleet[HullId.Cruiser], 0);
            var (_, galaxy) = Setup();
            Assert.GreaterOrEqual(CoreSystem.GuardianTargetPower(galaxy), CoreSystem.MinGuardianPower);
        }

        /// <summary>Balance readout (PACE lines): guardian strength in a full galaxy
        /// at its start and two days in, next to the strongest commanders' assault power.</summary>
        [Test]
        public void Pace_GuardiansAgainstTheStrongestCommanders()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            string Line(string when)
            {
                long guardians = CoreSystem.GuardianTargetPower(galaxy);
                var top = galaxy.Bots.Select(b => BotSystem.EstimateFleetPower(BotSystem.CombatFleetOf(b.State, BotSystem.RaidCommitFraction)))
                    .OrderByDescending(p => p).Take(3).ToList();
                int beat = galaxy.Bots.Count(b => BotSystem.EstimateFleetPower(BotSystem.CombatFleetOf(b.State, BotSystem.RaidCommitFraction))
                    >= guardians * CoreSystem.GuardianStatMult * BotSystem.BeatabilityEdge);
                return $"PACE core {when}: guardians {guardians:N0} ({string.Join(", ", CoreSystem.GuardianFleet(guardians).Select(kv => $"{kv.Key} {kv.Value}"))})" +
                    $" · top assaults {string.Join(" / ", top.Select(p => p.ToString("N0")))} · commanders who could win alone: {beat}";
            }
            TestContext.Out.WriteLine(Line("day 0"));
            player.Tick = 2 * 24 * 3600;
            BotSystem.Advance(player, galaxy, new SimEventBus());
            TestContext.Out.WriteLine(Line("day 2"));
            Assert.Pass();
        }

        [Test]
        public void YourAssault_TakesTheCore_AndTheSurvivorsStayAsTheGarrison()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var seen = new List<SimEvent>();
            bus.Subscribe(seen.Add);
            var march = Assault(player, galaxy, new Dictionary<HullId, int> { [HullId.Cruiser] = 300 });
            Assert.AreEqual(0, player.Ships[HullId.Cruiser], "the fleet left the dock");

            To(player, galaxy, bus, march.ArrivesAtTick);
            Assert.IsTrue(CoreSystem.PlayerHolds(galaxy));
            Assert.AreSame(march, CoreSystem.PlayerGarrison(player), "the assault fleet stands as the garrison");
            Assert.Greater(march.Ships[HullId.Cruiser], 250);
            Assert.AreEqual(1, player.Stats.CoresSeized);
            Assert.IsTrue(seen.OfType<CoreSeized>().Any(e => e.HolderId == 0 && e.PreviousHolderId == CoreSystem.GuardiansId));
            StringAssert.Contains("seized the Galactic Core", player.Mailbox.OfType<BattleMailReport>().First().Subject);
            Assert.IsTrue(galaxy.News.Exists(n => n.IsBulletin && n.Text!.Contains("seized the Galactic Core")));
        }

        [Test]
        public void AFailedAssault_WearsTheGuardiansDown()
        {
            var (player, galaxy) = Setup();
            galaxy.Core.Guardians = new Dictionary<HullId, int> { [HullId.Cruiser] = 400 };
            // Enough to punch through their shields, not enough to win.
            var march = Assault(player, galaxy, new Dictionary<HullId, int> { [HullId.Cruiser] = 300 });
            To(player, galaxy, new SimEventBus(), march.ArrivesAtTick);
            Assert.AreEqual(CoreSystem.GuardiansId, galaxy.Core.HolderId);
            Assert.Less(galaxy.Core.Guardians[HullId.Cruiser], 400, "they took losses");
            StringAssert.StartsWith("Core assault repelled", player.Mailbox.OfType<BattleMailReport>().First().Subject);
        }

        [Test]
        public void HoldingTheCore_PaysTributeEveryHour()
        {
            var (player, galaxy) = Setup();
            var march = Assault(player, galaxy, new Dictionary<HullId, int> { [HullId.Cruiser] = 300 });
            var bus = new SimEventBus();
            int landed = march.ArrivesAtTick; // a held march's arrival reads "never" afterwards
            To(player, galaxy, bus, landed);
            long gold = player.Resources.Gold;
            int dm = player.Premium.DarkMatter;
            long hourly = ResourceSystem.GetRates(player).Gold;

            // One step, three hours later (an offline catch-up).
            To(player, galaxy, bus, landed + 3 * CoreSystem.TributeIntervalSec);
            Assert.AreEqual(3, player.Stats.CoreHoursHeld);
            Assert.AreEqual(dm + 3 * CoreSystem.TributeDarkMatter, player.Premium.DarkMatter);
            Assert.AreEqual(gold + 3 * (long)(hourly * CoreSystem.TributeShare), player.Resources.Gold, 3);
        }

        [Test]
        public void Reinforcements_JoinTheGarrison_AndWithdrawingHandsItBack()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var seen = new List<SimEvent>();
            bus.Subscribe(seen.Add);
            var garrison = Assault(player, galaxy, new Dictionary<HullId, int> { [HullId.Cruiser] = 300 });
            To(player, galaxy, bus, garrison.ArrivesAtTick);
            int before = garrison.Ships[HullId.Cruiser];

            var extra = Assault(player, galaxy, new Dictionary<HullId, int> { [HullId.Fighter] = 100 });
            To(player, galaxy, bus, extra.ArrivesAtTick);
            Assert.IsFalse(player.Marches.Contains(extra), "merged");
            Assert.AreEqual(100, garrison.Ships[HullId.Fighter]);
            Assert.AreEqual(before, garrison.Ships[HullId.Cruiser]);

            Assert.IsTrue(MarchSystem.RecallMarch(player, garrison.Id).Ok);
            To(player, galaxy, bus, player.Tick + 1);
            Assert.AreEqual(CoreSystem.GuardiansId, galaxy.Core.HolderId, "the guardians came back");
            Assert.Greater(galaxy.Core.Guardians.Values.Sum(), 0);
            Assert.IsTrue(seen.OfType<CoreSeized>().Any(e => e.HolderId == CoreSystem.GuardiansId && e.PreviousHolderId == 0));
        }

        [Test]
        public void Commanders_AssaultTheCore_WithTheirClanmates()
        {
            var (player, galaxy) = Setup();
            galaxy.Core.Guardians = new Dictionary<HullId, int> { [HullId.Fighter] = 5 };
            var lead = StrongBot(player, galaxy, 3, 2000);
            var mate = StrongBot(player, galaxy, 4, 800);
            lead.ClanId = mate.ClanId = 77;
            mate.State.HomeTile = new TileXY(CoreSystem.CoreTile.X + 300, CoreSystem.CoreTile.Y);
            lead.State.HomeTile = new TileXY(CoreSystem.CoreTile.X - 400, CoreSystem.CoreTile.Y);
            foreach (var b in galaxy.Bots) if (b != lead && b != mate) b.State.Ships.Clear(); // nobody else can

            // Roll until a (deterministic) roll fires with one of them awake to lead.
            galaxy.Core.NextRollTick = 1;
            var bus = new SimEventBus();
            List<BotMarch> group = new();
            for (int t = 1; t < 72 * 3600 && group.Count == 0; t += CoreSystem.RollIntervalSec)
            {
                player.Tick = t;
                CoreSystem.Tick(player, galaxy, bus);
                group = galaxy.Marches.FindAll(m => m.Kind == BotMarchKind.CoreAssault);
            }
            Assert.AreEqual(2, group.Count, "a clan assault: the leader and one clanmate's wing");
            CollectionAssert.AreEquivalent(new[] { lead.Id, mate.Id }, group.Select(m => m.BotId));
            Assert.AreEqual(1, group.Select(m => m.LinkId).Distinct().Count(), "one assault group");
            Assert.AreEqual(1, group.Select(m => m.ArrivesAtTick).Distinct().Count(), "they land together");
            Assert.Less(lead.State.Ships[HullId.Cruiser] + mate.State.Ships[HullId.Cruiser], 2800, "fleets left their docks");

            To(player, galaxy, bus, group[0].ArrivesAtTick);
            Assert.Contains(galaxy.Core.HolderId, new[] { lead.Id, mate.Id });
            Assert.Greater(galaxy.Core.Garrison.Values.Sum(), 0, "the leader's survivors hold the core");
            Assert.AreEqual(1, group.Count(m => !galaxy.Marches.Contains(m)), "the leader's fleet stayed at the core");
            Assert.IsTrue(group.Where(m => galaxy.Marches.Contains(m)).All(m => m.Resolved), "the wing flies home");
        }

        [Test]
        public void ACommandersAssault_OnYourCore_FilesADefenceReport()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var garrison = Assault(player, galaxy, new Dictionary<HullId, int> { [HullId.Cruiser] = 50 });
            To(player, galaxy, bus, garrison.ArrivesAtTick);
            Assert.IsTrue(CoreSystem.PlayerHolds(galaxy));

            var attacker = StrongBot(player, galaxy, 5, 3000);
            int at = player.Tick + 600;
            galaxy.Marches.Add(new BotMarch
            {
                Id = 900, BotId = attacker.Id, Kind = BotMarchKind.CoreAssault, LinkId = 900,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 2000 },
                From = attacker.HomeTile, To = CoreSystem.CoreTile, LaunchTick = player.Tick, ArrivesAtTick = at,
            });
            var seen = new List<SimEvent>();
            bus.Subscribe(seen.Add);
            To(player, galaxy, bus, at);

            Assert.AreEqual(attacker.Id, galaxy.Core.HolderId, "it fell");
            Assert.IsNull(CoreSystem.PlayerGarrison(player));
            Assert.IsFalse(player.Marches.Contains(garrison));
            var mail = player.Mailbox.OfType<BattleMailReport>().First();
            Assert.IsTrue(mail.Defending);
            Assert.AreEqual(CoreSystem.CoreGuardId, mail.GuardedBotId);
            Assert.AreEqual(attacker.Id, mail.AttackerBotId);
            Assert.IsTrue(seen.OfType<CoreSeized>().Any(e => e.HolderId == attacker.Id && e.PreviousHolderId == 0));
        }

        [Test]
        public void CatchUp_ReplaysTheCoresHistoryInOrder()
        {
            var (player, galaxy) = Setup();
            var bus = new SimEventBus();
            var mine = Assault(player, galaxy, new Dictionary<HullId, int> { [HullId.Cruiser] = 300 });
            var attacker = StrongBot(player, galaxy, 6, 3000);
            int later = mine.ArrivesAtTick + 2 * 3600;
            galaxy.Marches.Add(new BotMarch
            {
                Id = 901, BotId = attacker.Id, Kind = BotMarchKind.CoreAssault, LinkId = 901,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 2500 },
                From = attacker.HomeTile, To = CoreSystem.CoreTile, LaunchTick = 0, ArrivesAtTick = later,
            });

            // One step across both arrivals: you take it, collect two tributes, then lose it.
            To(player, galaxy, bus, later + 3600);
            Assert.AreEqual(1, player.Stats.CoresSeized, "you took it first");
            Assert.AreEqual(2, player.Stats.CoreHoursHeld, "two hours of tribute before it fell");
            Assert.AreEqual(attacker.Id, galaxy.Core.HolderId);
            Assert.IsTrue(player.Mailbox.OfType<BattleMailReport>().Any(m => m.Defending && m.GuardedBotId == CoreSystem.CoreGuardId));
        }

        [Test]
        public void AClanmateHoldingTheCore_PaysYouClanTribute_AndYouCantAssaultIt()
        {
            var (player, galaxy) = Setup();
            Assert.IsTrue(ClanSystem.Found(player, galaxy, "Test Fleet", "TST").Ok);
            var mate = galaxy.Bots.First(b => !ClanSystem.IsLoneWolf(player.Seed, b));
            mate.CachedMight = PowerSystem.ComputePower(player);
            Assert.IsTrue(ClanSystem.Invite(player, galaxy, mate.Id).Ok);
            galaxy.Core.HolderId = mate.Id;
            galaxy.Core.HeldSinceTick = 0;
            galaxy.Core.NextTributeTick = 3600;
            galaxy.Core.Garrison = new Dictionary<HullId, int> { [HullId.Cruiser] = 10 };

            Assert.IsFalse(CoreSystem.CanSend(player, galaxy).Ok);
            long gold = player.Resources.Gold;
            long hourly = ResourceSystem.GetRates(player).Gold;
            To(player, galaxy, new SimEventBus(), 3600);
            Assert.AreEqual(gold + (long)(hourly * CoreSystem.ClanTributeShare), player.Resources.Gold, 2);
        }

        [Test]
        public void YouCanIntercept_AnAssaultOnYourCore()
        {
            var (player, galaxy) = Setup();
            var attacker = StrongBot(player, galaxy, 7, 100);
            var from = new TileXY(CoreSystem.CoreTile.X - 700, CoreSystem.CoreTile.Y);
            galaxy.Marches.Add(new BotMarch
            {
                Id = 902, BotId = attacker.Id, Kind = BotMarchKind.CoreAssault, LinkId = 902,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 30 },
                From = from, To = CoreSystem.CoreTile, LaunchTick = 0, ArrivesAtTick = 4000,
            });
            var track = StrikeSystem.Track(player, galaxy, false, 902);
            Assert.IsNotNull(track);
            Assert.IsTrue(track!.TargetsCore);
            Assert.IsTrue(StrikeSystem.PlanIntercept(player, track, new Dictionary<HullId, int> { [HullId.Cruiser] = 100 }).Ok);
        }

        [Test]
        public void CoreState_SurvivesASave()
        {
            var (player, galaxy) = Setup();
            galaxy.Core.HolderId = galaxy.Bots[2].Id;
            galaxy.Core.HeldSinceTick = 1234;
            galaxy.Core.Garrison = new Dictionary<HullId, int> { [HullId.Cruiser] = 77 };
            galaxy.Core.Guardians = new Dictionary<HullId, int> { [HullId.Bomber] = 5 };
            galaxy.Core.GuardiansRebuildTick = 555;
            galaxy.Core.NextRollTick = 666;
            galaxy.Core.NextTributeTick = 4834;
            galaxy.Core.TimesSeized = 4;
            galaxy.Marches.Add(new BotMarch
            {
                Id = 903, BotId = galaxy.Bots[3].Id, Kind = BotMarchKind.CoreAssault, LinkId = 903,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 9 },
                From = galaxy.Bots[3].HomeTile, To = CoreSystem.CoreTile, LaunchTick = 1, ArrivesAtTick = 99,
            });
            player.Marches.Add(new March
            {
                Id = 44, Phase = MarchPhase.Gathering, Mission = MarchMission.Core, GuardEmpireId = CoreSystem.CoreGuardId,
                Ships = new Dictionary<HullId, int> { [HullId.Fighter] = 12 },
                Node = CoreSystem.CoreTile, LegFrom = CoreSystem.CoreTile, LegTo = CoreSystem.CoreTile,
                DepartedAtTick = 10, ArrivesAtTick = int.MaxValue,
            });
            player.Stats.CoresSeized = 2;
            player.Stats.CoreHoursHeld = 30;

            var file = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(player, 1000, galaxy)));
            var core = file.Bots!.Core;
            Assert.AreEqual(galaxy.Bots[2].Id, core.HolderId);
            Assert.AreEqual(1234, core.HeldSinceTick);
            Assert.AreEqual(77, core.Garrison[HullId.Cruiser]);
            Assert.AreEqual(5, core.Guardians[HullId.Bomber]);
            Assert.AreEqual(555, core.GuardiansRebuildTick);
            Assert.AreEqual(666, core.NextRollTick);
            Assert.AreEqual(4834, core.NextTributeTick);
            Assert.AreEqual(4, core.TimesSeized);
            Assert.AreEqual(BotMarchKind.CoreAssault, file.Bots.Marches.Single(m => m.Id == 903).Kind);
            var saved = file.State.Marches.Single(m => m.Id == 44);
            Assert.AreEqual(MarchMission.Core, saved.Mission);
            Assert.AreEqual(CoreSystem.CoreGuardId, saved.GuardEmpireId);
            Assert.AreEqual(2, file.State.Stats.CoresSeized);
            Assert.AreEqual(30, file.State.Stats.CoreHoursHeld);
        }
    }
}
