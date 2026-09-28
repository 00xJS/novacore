// Clans (user request 2026-09-28, "max limit to 15 per alliance group"): groups
// of up to 15 commanders; the simulated galaxy runs its own clan politics.
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
    public class ClanTests
    {
        static (GameState player, BotGalaxy galaxy) Setup(int bots = 60)
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, bots);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            player.Ships[HullId.Fighter] = 400; // an established empire, past the rookie shield
            player.Resources = new ResourceBag(50_000, 50_000, 50_000).Milli();
            foreach (var bot in galaxy.Bots)
            {
                // Park the rivals: no think steps or attack rolls during a test.
                bot.LastThinkTick = int.MaxValue / 4;
                bot.NextAttackRollTick = int.MaxValue / 4;
            }
            return (player, galaxy);
        }

        /// <summary>Independent, peaceable rivals the player's clan could recruit.</summary>
        static List<BotEmpire> Recruits(GameState player, BotGalaxy galaxy, int count)
        {
            long mine = PowerSystem.ComputePower(player);
            var list = new List<BotEmpire>();
            foreach (var bot in galaxy.Bots)
            {
                if (ClanSystem.IsLoneWolf(player.Seed, bot)) continue;
                bot.ClanId = 0;
                bot.CachedMight = mine;
                list.Add(bot);
                if (list.Count == count) break;
            }
            Assert.AreEqual(count, list.Count, "enough peaceable rivals in the test galaxy");
            return list;
        }

        static Clan FoundOne(GameState player, BotGalaxy galaxy, string name = "Test Fleet", string tag = "TST")
        {
            var result = ClanSystem.Found(player, galaxy, name, tag);
            Assert.IsTrue(result.Ok, result.Reason);
            return ClanSystem.PlayerClan(player, galaxy)!;
        }

        [Test]
        public void Seeding_FormsClansOfAtMost15_WithoutLoneWolves()
        {
            var (player, galaxy) = Setup();
            ClanSystem.SeedClans(player, galaxy);
            Assert.Greater(galaxy.Clans.Count, 3);
            Assert.LessOrEqual(galaxy.Clans.Count, ClanSystem.SeedClanCount);
            Assert.AreEqual(galaxy.Clans.Count, galaxy.Clans.Select(c => c.Tag).Distinct().Count(), "tags are unique");
            Assert.AreEqual(galaxy.Clans.Count, galaxy.Clans.Select(c => c.Name).Distinct().Count(), "names are unique");
            foreach (var clan in galaxy.Clans)
            {
                var members = ClanSystem.BotMembers(galaxy, clan.Id);
                Assert.LessOrEqual(members.Count, ClanSystem.MaxMembers);
                Assert.IsTrue(members.Exists(m => m.Id == clan.LeaderId), "the leader is a member");
                Assert.IsTrue(clan.Tag.All(c => c < 128) && clan.Name.All(c => c < 128), "ASCII only");
            }
            foreach (var bot in galaxy.Bots)
                if (ClanSystem.IsLoneWolf(player.Seed, bot)) Assert.AreEqual(0, bot.ClanId, "lone wolves stay independent");
        }

        [Test]
        public void Politics_AreDeterministic()
        {
            var (p1, g1) = Setup();
            var (p2, g2) = Setup();
            for (int pass = 0; pass < 6; pass++)
            {
                p1.Tick = p2.Tick = pass * ClanSystem.PoliticsIntervalSec;
                ClanSystem.Tick(p1, g1, new SimEventBus());
                ClanSystem.Tick(p2, g2, new SimEventBus());
            }
            CollectionAssert.AreEqual(g1.Clans.Select(c => $"{c.Tag}:{c.Name}:{c.WarWithClanId}"),
                g2.Clans.Select(c => $"{c.Tag}:{c.Name}:{c.WarWithClanId}"));
            CollectionAssert.AreEqual(g1.Bots.Select(b => b.ClanId), g2.Bots.Select(b => b.ClanId));
        }

        [Test]
        public void Found_Invite_Remove_Leave()
        {
            var (player, galaxy) = Setup();
            Assert.IsFalse(ClanSystem.Found(player, galaxy, "X", "TST").Ok, "names need 3+ characters");
            Assert.IsFalse(ClanSystem.Found(player, galaxy, "Test Fleet", "T!").Ok, "tags are letters and digits");
            long gold = player.Resources.Gold;
            var clan = FoundOne(player, galaxy);
            Assert.AreEqual(0, clan.LeaderId, "you lead what you found");
            Assert.AreEqual(gold - ClanSystem.FoundCost.Gold * 1000L, player.Resources.Gold);
            Assert.IsFalse(ClanSystem.Found(player, galaxy, "Other Fleet", "OTH").Ok, "one clan at a time");

            var recruit = Recruits(player, galaxy, 1)[0];
            Assert.IsTrue(ClanSystem.Invite(player, galaxy, recruit.Id).Ok);
            Assert.AreEqual(clan.Id, recruit.ClanId);
            Assert.AreEqual(2, ClanSystem.MemberCount(player, galaxy, clan.Id));
            Assert.AreEqual(2, player.Stats.BestClanSize);

            Assert.IsTrue(ClanSystem.Kick(player, galaxy, recruit.Id).Ok);
            Assert.AreEqual(0, recruit.ClanId);
            Assert.IsTrue(BotSystem.HoldsGrudge(recruit, player.Tick), "a removed member takes it personally");
            Assert.IsFalse(ClanSystem.CanInvite(player, galaxy, recruit).Ok);

            Assert.IsTrue(ClanSystem.Leave(player, galaxy).Ok);
            Assert.AreEqual(0, player.ClanId);
            Assert.IsNull(galaxy.FindClan(clan.Id), "a clan of one folds when its last member leaves");
        }

        [Test]
        public void LeavingLeader_HandsTheClanOn()
        {
            var (player, galaxy) = Setup();
            var clan = FoundOne(player, galaxy);
            var recruits = Recruits(player, galaxy, 2);
            recruits[1].CachedMight += 10; // the stronger one inherits
            foreach (var r in recruits) Assert.IsTrue(ClanSystem.Invite(player, galaxy, r.Id).Ok);
            Assert.IsTrue(ClanSystem.Leave(player, galaxy).Ok);
            Assert.IsNotNull(galaxy.FindClan(clan.Id));
            Assert.AreEqual(recruits[1].Id, clan.LeaderId);
        }

        [Test]
        public void Clans_CapAt15()
        {
            var (player, galaxy) = Setup(80);
            var clan = FoundOne(player, galaxy);
            var recruits = Recruits(player, galaxy, ClanSystem.MaxMembers);
            for (int i = 0; i < ClanSystem.MaxMembers - 1; i++)
                Assert.IsTrue(ClanSystem.Invite(player, galaxy, recruits[i].Id).Ok, $"member {i + 2}");
            Assert.AreEqual(ClanSystem.MaxMembers, ClanSystem.MemberCount(player, galaxy, clan.Id));
            var sixteenth = ClanSystem.Invite(player, galaxy, recruits[ClanSystem.MaxMembers - 1].Id);
            Assert.IsFalse(sixteenth.Ok);
            StringAssert.Contains("full", sixteenth.Reason);
            Assert.AreEqual(ClanSystem.MaxMembers, player.Stats.BestClanSize);
        }

        [Test]
        public void Joining_ABotClan_HasStandards()
        {
            var (player, galaxy) = Setup();
            ClanSystem.Tick(player, galaxy, new SimEventBus());
            var clan = galaxy.Clans.First(c => ClanSystem.BotMembers(galaxy, c.Id).Count < ClanSystem.MaxMembers);
            var members = ClanSystem.BotMembers(galaxy, clan.Id);

            player.Ships.Clear(); // a rookie
            StringAssert.Contains("might", ClanSystem.CanJoin(player, galaxy, clan).Reason);

            player.Ships[HullId.Fighter] = 400;
            foreach (var m in members) m.CachedMight = PowerSystem.ComputePower(player) * 10;
            StringAssert.Contains("that small", ClanSystem.CanJoin(player, galaxy, clan).Reason);

            foreach (var m in members) m.CachedMight = PowerSystem.ComputePower(player);
            members[0].FocusTargetId = 0;
            members[0].FocusSetTick = player.Tick;
            StringAssert.Contains("forgiven", ClanSystem.CanJoin(player, galaxy, clan).Reason);

            members[0].FocusTargetId = -1;
            Assert.IsTrue(ClanSystem.Join(player, galaxy, clan.Id).Ok);
            Assert.AreEqual(clan.Id, player.ClanId);
            Assert.IsFalse(ClanSystem.PlayerLeads(player, galaxy), "you join as a member, not the leader");
        }

        [Test]
        public void DefenseHelpers_AreTheNearestThree_InRange()
        {
            var (player, galaxy) = Setup();
            FoundOne(player, galaxy);
            var recruits = Recruits(player, galaxy, 5);
            for (int i = 0; i < recruits.Count; i++)
            {
                Assert.IsTrue(ClanSystem.Invite(player, galaxy, recruits[i].Id).Ok);
                recruits[i].State.HomeTile = new TileXY(player.HomeTile.X + 50 + i * 60, player.HomeTile.Y);
                recruits[i].State.Ships.Clear();
                recruits[i].State.Ships[HullId.Cruiser] = 100;
            }
            recruits[0].State.HomeTile = new TileXY(player.HomeTile.X + (int)ClanSystem.ReinforceRange + 100, player.HomeTile.Y);

            var helpers = ClanSystem.DefenseHelpers(player, galaxy, 0, attackerId: -1);
            Assert.AreEqual(ClanSystem.MaxReinforcers, helpers.Count);
            CollectionAssert.AreEqual(new[] { recruits[1].Id, recruits[2].Id, recruits[3].Id }, helpers.Select(h => h.ally.Id),
                "nearest first, out-of-range members skipped");
            Assert.AreEqual(15, helpers[0].ships[HullId.Cruiser], "15% of each warship hull");
        }

        [Test]
        public void SplitLosses_FollowsWhoStoodInTheLine()
        {
            var owner = new Dictionary<HullId, int> { [HullId.Cruiser] = 10 };
            var helper = new Dictionary<HullId, int> { [HullId.Cruiser] = 30 };
            var losses = ClanSystem.SplitLosses(new[] { owner, helper },
                new Dictionary<HullId, int> { [HullId.Cruiser] = 20 });
            Assert.AreEqual(15, losses[1][HullId.Cruiser]);
            Assert.AreEqual(5, losses[0][HullId.Cruiser]);
        }

        [Test]
        public void RaidOnYou_BringsYourClan_AndTheReportSurvivesASave()
        {
            var (player, galaxy) = Setup();
            FoundOne(player, galaxy);
            var mate = Recruits(player, galaxy, 1)[0];
            Assert.IsTrue(ClanSystem.Invite(player, galaxy, mate.Id).Ok);
            mate.State.HomeTile = new TileXY(player.HomeTile.X + 50, player.HomeTile.Y);
            mate.State.Ships.Clear();
            mate.State.Ships[HullId.Cruiser] = 200; // sends 30
            player.Ships.Clear();
            player.Ships[HullId.Cruiser] = 10;
            var raider = galaxy.Bots.First(b => b.ClanId != player.ClanId);

            galaxy.Inbound.Add(new BotAttack
            {
                Id = galaxy.NextAttackId++, BotId = raider.Id, IsFleet = true,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 25 },
                LaunchTick = 0, ArrivesAtTick = 50, From = raider.HomeTile,
            });
            player.Tick = 60;
            BotSystem.Advance(player, galaxy, new SimEventBus());

            var mail = player.Mailbox.OfType<BattleMailReport>().Single(m => m.Defending);
            Assert.AreEqual(30, mail.AllyShips![HullId.Cruiser]);
            Assert.AreEqual(mate.Name, mail.AllyNames);
            Assert.AreEqual(40, mail.Report.Defender[HullId.Cruiser], "yours + your clan's in one line");
            int lost = 40 - (mail.Report.DefenderSurvivors.TryGetValue(HullId.Cruiser, out var left) ? left : 0);
            Assert.AreEqual(lost, (200 - mate.State.Ships[HullId.Cruiser]) + (10 - player.Ships[HullId.Cruiser]),
                "every loss lands on someone");

            var back = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(player, 1000))).State;
            var saved = (BattleMailReport)back.Mailbox.First(m => m is BattleMailReport { Defending: true });
            Assert.AreEqual(30, saved.AllyShips![HullId.Cruiser]);
        }

        [Test]
        public void BotRaid_StandsDown_WhenTheyJoinedTheSameClanMidFlight()
        {
            var (player, galaxy) = Setup();
            var a = galaxy.Bots[0];
            var b = galaxy.Bots[1];
            b.State.Ships.Clear();
            b.State.Ships[HullId.Fighter] = 5;
            galaxy.Marches.Add(new BotMarch
            {
                Id = galaxy.NextMarchId++, BotId = a.Id, TargetBotId = b.Id,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 50 },
                From = a.HomeTile, To = b.HomeTile, LaunchTick = 0, ArrivesAtTick = 40,
            });
            a.ClanId = b.ClanId = 99;
            int news = galaxy.News.Count;
            player.Tick = 60;
            BotSystem.Advance(player, galaxy, new SimEventBus());
            Assert.AreEqual(5, b.State.Ships[HullId.Fighter], "no battle between clanmates");
            Assert.AreEqual(news, galaxy.News.Count);
        }

        [Test]
        public void Supplies_ArriveDaily_FromEveryMember_AndCapAt3()
        {
            var (player, galaxy) = Setup();
            FoundOne(player, galaxy);
            var recruits = Recruits(player, galaxy, 3);
            foreach (var r in recruits)
            {
                Assert.IsTrue(ClanSystem.Invite(player, galaxy, r.Id).Ok);
                r.State.Buildings[BuildingId.CommandCenter].Level = 4;
                r.State.Resources = new ResourceBag(1_000_000, 1_000_000, 1_000_000).Milli();
            }
            player.Tick = ClanSystem.SupplyIntervalSec;
            Assert.AreEqual(1, ClanSystem.Supplies(player, galaxy));
            Assert.AreEqual(3 * ClanSystem.SupplyGoldPerCc * 4 * 1000L, player.ClanSupplyPending.Gold);

            player.Tick = ClanSystem.SupplyIntervalSec * 10;
            ClanSystem.Supplies(player, galaxy);
            Assert.AreEqual(ClanSystem.MaxStoredRuns, player.ClanSupplyRuns, "supplies stop piling up");

            long gold = player.Resources.Gold, waiting = player.ClanSupplyPending.Gold;
            Assert.IsTrue(ClanSystem.CollectSupplies(player).Ok);
            Assert.AreEqual(gold + waiting, player.Resources.Gold);
            Assert.IsFalse(ClanSystem.CollectSupplies(player).Ok);
        }

        [Test]
        public void Wars_ScoreVictories_AndPayTheWinners()
        {
            var (player, galaxy) = Setup();
            ClanSystem.Tick(player, galaxy, new SimEventBus()); // seed the rival clans
            var ours = FoundOne(player, galaxy);
            var theirs = galaxy.Clans.First(c => c.LeaderId != 0 && c.WarWithClanId == 0
                && player.Tick >= c.WarCooldownUntilTick);
            var enemy = ClanSystem.BotMembers(galaxy, theirs.Id)[0];

            Assert.IsTrue(ClanSystem.DeclareWar(player, galaxy, theirs.Id).Ok);
            Assert.AreEqual(theirs.Id, ours.WarWithClanId);
            Assert.AreEqual(ours.Id, theirs.WarWithClanId);
            Assert.IsFalse(ClanSystem.DeclareWar(player, galaxy, theirs.Id).Ok, "one war at a time");

            ClanSystem.RecordBattle(player, galaxy, 0, enemy.Id, attackerWon: true);
            ClanSystem.RecordBattle(player, galaxy, enemy.Id, 0, attackerWon: false); // you held
            Assert.AreEqual(2, ours.WarScore);

            int dm = player.Premium.DarkMatter;
            var ended = new List<ClanWarEnded>();
            var events = new SimEventBus();
            events.Subscribe(e => { if (e is ClanWarEnded w) ended.Add(w); });
            player.Tick = ours.WarEndsTick + 1;
            galaxy.NextPoliticsTick = player.Tick;
            ClanSystem.Tick(player, galaxy, events);

            Assert.AreEqual(0, ours.WarWithClanId);
            Assert.AreEqual(dm + ClanSystem.WarWinRewardDM, player.Premium.DarkMatter);
            Assert.AreEqual(1, player.Stats.ClanWarsWon);
            Assert.AreEqual(1, ended.Count);
            Assert.IsTrue(ended[0].Won);
            Assert.IsTrue(galaxy.News.Exists(n => n.IsBulletin && n.Text!.Contains("won the war")));
            Assert.IsFalse(ClanSystem.CanDeclareWar(player, galaxy, theirs).Ok, "a breather after each war");
        }

        [Test]
        public void Invitations_ComeToEstablishedIndependents()
        {
            var (player, galaxy) = Setup();
            ClanSystem.Tick(player, galaxy, new SimEventBus());
            var clan = galaxy.Clans.First(c =>
            {
                int n = ClanSystem.BotMembers(galaxy, c.Id).Count;
                return n >= 3 && n < ClanSystem.MaxMembers - 2; // won't fold or fill up this pass
            });
            var member = ClanSystem.BotMembers(galaxy, clan.Id)[0];
            // Strong enough that recruits joining the clan this same pass can't
            // raise its bar past you; and no invitation left over from seeding.
            player.Ships[HullId.Fighter] = 20_000;
            player.ClanInviteId = 0;
            player.HomeTile = new TileXY(member.HomeTile.X + 30, member.HomeTile.Y);

            var invites = new List<int>();
            var events = new SimEventBus();
            events.Subscribe(e => { if (e is ClanInviteReceived i) invites.Add(i.ClanId); });
            player.Tick = galaxy.NextPoliticsTick;
            ClanSystem.Tick(player, galaxy, events);
            Assert.AreNotEqual(0, player.ClanInviteId,
                $"CanJoin: {ClanSystem.CanJoin(player, galaxy, clan).Reason ?? "ok"}; clan alive {galaxy.FindClan(clan.Id) != null}; " +
                $"members {ClanSystem.BotMembers(galaxy, clan.Id).Count}; nearest {ClanSystem.NearestMember(player, galaxy, clan.Id):0}; " +
                $"might {PowerSystem.ComputePower(player)}; nextInvite {player.ClanNextInviteTick}; tick {player.Tick}; " +
                $"clanId {player.ClanId}; nextPolitics {galaxy.NextPoliticsTick}");
            Assert.AreEqual(1, invites.Count);

            Assert.IsTrue(ClanSystem.DeclineInvite(player).Ok);
            player.Tick = galaxy.NextPoliticsTick;
            ClanSystem.Tick(player, galaxy, events);
            Assert.AreEqual(0, player.ClanInviteId, "no new invitation right after declining");
        }

        [Test]
        public void ClanAchievements_Unlock()
        {
            var s = GameState.CreateNewGame(42, testMode: false);
            s.ClanId = 7;
            s.Stats.BestClanSize = 15;
            s.Stats.ClanWarsWon = 3;
            var got = AchievementSystem.CheckNew(s).Select(a => a.Id).ToList();
            Assert.Contains("diplomat", got);
            Assert.Contains("full-ranks", got);
            Assert.Contains("warmaster", got);
        }

        [Test]
        public void EverythingClanSurvivesASave()
        {
            var (player, galaxy) = Setup();
            ClanSystem.Tick(player, galaxy, new SimEventBus());
            var clan = FoundOne(player, galaxy);
            var mate = Recruits(player, galaxy, 1)[0];
            Assert.IsTrue(ClanSystem.Invite(player, galaxy, mate.Id).Ok);
            mate.SupportReadyTick = 12345;
            var rival = galaxy.Clans.First(c => c.LeaderId != 0 && c.WarWithClanId == 0
                && player.Tick >= c.WarCooldownUntilTick);
            Assert.IsTrue(ClanSystem.DeclareWar(player, galaxy, rival.Id).Ok);
            clan.WarScore = 4;
            player.ClanSupplyPending = new ResourceBag(1000, 2000, 3000);
            player.ClanSupplyRuns = 2;
            player.ClanNextInviteTick = 999;
            player.Stats.BestClanSize = 2;
            player.Stats.ClanWarsWon = 1;
            player.Mailbox.Add(new BattleMailReport
            {
                Id = 1, Subject = "Raid victory — X", Report = new Combat.BattleReport(),
                EnemyAllyShips = new Dictionary<HullId, int> { [HullId.Fighter] = 7 }, EnemyAllyNames = "Moon Moon",
            });

            var file = SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(player, 1000, galaxy)));
            var s = file.State;
            var g = file.Bots!;
            Assert.AreEqual(player.ClanId, s.ClanId);
            Assert.AreEqual(2000, s.ClanSupplyPending.Quartz);
            Assert.AreEqual(2, s.ClanSupplyRuns);
            Assert.AreEqual(999, s.ClanNextInviteTick);
            Assert.AreEqual(2, s.Stats.BestClanSize);
            Assert.AreEqual(1, s.Stats.ClanWarsWon);
            Assert.AreEqual(galaxy.Clans.Count, g.Clans.Count);
            var savedClan = g.FindClan(clan.Id)!;
            Assert.AreEqual("TST", savedClan.Tag);
            Assert.AreEqual(rival.Id, savedClan.WarWithClanId);
            Assert.AreEqual(4, savedClan.WarScore);
            Assert.AreEqual(galaxy.NextClanId, g.NextClanId);
            Assert.AreEqual(galaxy.NextPoliticsTick, g.NextPoliticsTick);
            var savedMate = g.Find(mate.Id)!;
            Assert.AreEqual(clan.Id, savedMate.ClanId);
            Assert.AreEqual(12345, savedMate.SupportReadyTick);
            Assert.IsTrue(g.News.Exists(n => n.IsBulletin && n.Text!.Contains("declared war")));
            var mail = (BattleMailReport)s.Mailbox.First(m => m is BattleMailReport);
            Assert.AreEqual(7, mail.EnemyAllyShips![HullId.Fighter]);
            Assert.AreEqual("Moon Moon", mail.EnemyAllyNames);
        }
    }
}
