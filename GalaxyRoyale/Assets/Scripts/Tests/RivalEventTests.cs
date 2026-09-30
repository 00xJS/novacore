// Rival events (2026-09-30): the Bounty Board, the Core Tournament, and the
// Pirate Dreadnought's variants.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class RivalEventTests
    {
        const int Hour = 3600;

        static int StartOf(GalaxyEventKind kind)
        {
            for (int t = GalaxyEvents.LeadInSec; t < GalaxyEvents.LeadInSec + EventSystem.CycleSec; t += Hour)
                if (EventSystem.Current(t).Def.Kind == kind) return EventSystem.Current(t).StartTick;
            Assert.Fail($"{kind} isn't in the rotation");
            return 0;
        }

        static (GameState player, BotGalaxy galaxy) Setup()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 60);
            var player = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            player.Buffs.ProtectionUntilTick = 0;
            foreach (var bot in galaxy.Bots)
            {
                bot.ClanId = 0;
                bot.CachedMight = PowerSystem.ComputePower(bot.State);
            }
            galaxy.Clans.Clear();
            return (player, galaxy);
        }

        static GameState RoundTrip(GameState s) => SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;

        // ---------- the Bounty Board ----------

        [Test]
        public void TheBounty_MarksARivalNearby_AndPaysOnce()
        {
            var (player, galaxy) = Setup();
            player.Tick = StartOf(GalaxyEventKind.BountyBoard) + 5;
            BountySystem.Tick(player, galaxy, new SimEventBus());
            Assert.AreNotEqual(0, player.BountyTargetId, "someone is marked");
            var target = galaxy.Find(player.BountyTargetId)!;
            Assert.LessOrEqual(TileXY.Distance(target.HomeTile, player.HomeTile), GalaxyEvents.BountyRange);
            Assert.IsTrue(BountySystem.IsMarked(player, target.Id));
            Assert.IsFalse(BountySystem.IsMarked(player, target.Id + 1000));

            var back = RoundTrip(player);
            Assert.AreEqual(player.BountyTargetId, back.BountyTargetId);
            Assert.AreEqual(player.BountyTargetName, back.BountyTargetName);

            int dm = player.Premium.DarkMatter;
            Assert.IsNotNull(BountySystem.Claim(player, target.Id));
            Assert.AreEqual(dm + GalaxyEvents.BountyDarkMatter, player.Premium.DarkMatter);
            Assert.AreEqual(1, player.Stats.BountiesClaimed);
            Assert.IsNull(BountySystem.Claim(player, target.Id), "one bounty per event");
        }

        [Test]
        public void ARivalCanCollectFirst_AndTheBountyEndsWithTheEvent()
        {
            var (player, galaxy) = Setup();
            int start = StartOf(GalaxyEventKind.BountyBoard);
            player.Tick = start + 5;
            BountySystem.Tick(player, galaxy, new SimEventBus());
            var hunter = galaxy.Bots.First(b => b.Id != player.BountyTargetId);
            BountySystem.TakenByRival(player, galaxy, hunter, player.Tick, new SimEventBus());
            Assert.IsFalse(BountySystem.IsMarked(player, player.BountyTargetId));

            var (p2, g2) = Setup();
            p2.Tick = start + 5;
            BountySystem.Tick(p2, g2, new SimEventBus());
            p2.Tick = EventSystem.Current(p2.Tick).EndTick + 1;
            Assert.IsFalse(BountySystem.IsMarked(p2, p2.BountyTargetId), "gone when the event ends");
        }

        // ---------- the Core Tournament ----------

        [Test]
        public void TheTournament_ThrowsTheHolderOut_AndHalvesTheGuardians()
        {
            var (player, galaxy) = Setup();
            var holder = galaxy.Bots[3];
            galaxy.Core.HolderId = holder.Id;
            galaxy.Core.Garrison = new Dictionary<HullId, int> { [HullId.Cruiser] = 50 };
            holder.State.Ships[HullId.Cruiser] = 0;
            player.Tick = StartOf(GalaxyEventKind.CoreTournament) + 1;
            var bus = new SimEventBus();
            var seen = new List<CoreTournament>();
            bus.Subscribe(e => { if (e is CoreTournament t) seen.Add(t); });
            CoreSystem.Tick(player, galaxy, bus);

            Assert.AreEqual(CoreSystem.GuardiansId, galaxy.Core.HolderId);
            Assert.AreEqual(50, holder.State.Ships[HullId.Cruiser], "their garrison flew home");
            long half = CoreSystem.DefencePower(CoreSystem.DefenceOf(player, galaxy));
            Assert.Greater(half, 0);
            Assert.IsTrue(seen.Single().Began);
            Assert.AreEqual(CoreLogKind.TournamentOpened, galaxy.Core.History[0].Kind);
            Assert.IsTrue(CoreSystem.InTournament(player.Tick));
        }

        [Test]
        public void HoldingTheCore_WhenTheTournamentEnds_WinsThePrize()
        {
            var (player, galaxy) = Setup();
            int start = StartOf(GalaxyEventKind.CoreTournament);
            player.Tick = start + 1;
            var bus = new SimEventBus();
            CoreSystem.Tick(player, galaxy, bus); // opens it
            // You take it (a garrison march standing at the core).
            player.Marches.Add(new March
            {
                Id = player.NextMarchId++, Mission = MarchMission.Core, Phase = MarchPhase.Gathering,
                GuardEmpireId = CoreSystem.CoreGuardId, Node = CoreSystem.CoreTile, LegTo = CoreSystem.CoreTile,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 10 }, ArrivesAtTick = int.MaxValue,
            });
            galaxy.Core.HolderId = 0;
            galaxy.Core.NextRollTick = int.MaxValue / 4;
            galaxy.Core.NextTributeTick = int.MaxValue / 4;

            int dm = player.Premium.DarkMatter;
            var seen = new List<CoreTournament>();
            bus.Subscribe(e => { if (e is CoreTournament t) seen.Add(t); });
            player.Tick = EventSystem.Current(start).EndTick + 1;
            CoreSystem.Tick(player, galaxy, bus);
            Assert.IsTrue(seen.Single().Won);
            Assert.AreEqual(dm + GalaxyEvents.TournamentPrizeDarkMatter, player.Premium.DarkMatter);
            Assert.AreEqual(1, player.Stats.TournamentsWon);
            Assert.AreEqual(-1, galaxy.Core.TournamentInstance);
        }

        // ---------- the Dreadnought's variants ----------

        [Test]
        public void TheVariants_TakeTurns_StartingWithTheClassic()
        {
            Assert.AreEqual(BossVariant.Dreadnought, BossSystem.VariantFor(1));
            Assert.AreEqual(BossVariant.Carrier, BossSystem.VariantFor(2));
            Assert.AreEqual(BossVariant.Siege, BossSystem.VariantFor(3));
            Assert.AreEqual(BossVariant.Stealth, BossSystem.VariantFor(4));
            Assert.AreEqual(BossVariant.Dreadnought, BossSystem.VariantFor(5));
        }

        static BossState Visit(GameState player, BotGalaxy galaxy, int visitNumber)
        {
            var bus = new SimEventBus();
            galaxy.Boss.Visit = visitNumber - 1;
            galaxy.Boss.NextVisitTick = player.Tick + 1;
            player.Tick += 1;
            BossSystem.Tick(player, galaxy, bus);
            Assert.IsTrue(galaxy.Boss.Active);
            return galaxy.Boss;
        }

        [Test]
        public void TheCarrier_HitsHarder_OnALighterHull()
        {
            var (p1, g1) = Setup();
            var classic = Visit(p1, g1, 1);
            var (p2, g2) = Setup();
            var carrier = Visit(p2, g2, 2);
            Assert.AreEqual(BossVariant.Carrier, carrier.Variant);
            Assert.Less(carrier.MaxHp, classic.MaxHp);
            Assert.Greater(carrier.Cannon, classic.Cannon);
        }

        [Test]
        public void TheStealthDreadnought_HidesAtFirst()
        {
            var (player, galaxy) = Setup();
            var boss = Visit(player, galaxy, 4);
            Assert.AreEqual(BossVariant.Stealth, boss.Variant);
            Assert.IsFalse(BossSystem.Revealed(player, galaxy));
            player.Ships[HullId.Cruiser] = 10;
            Assert.IsFalse(BossSystem.CanStrike(player, galaxy, new Dictionary<HullId, int> { [HullId.Cruiser] = 10 }).Ok);
            player.Buildings[BuildingId.Observatory].Level = BossSystem.StealthSeeLevel;
            Assert.IsTrue(BossSystem.Revealed(player, galaxy), "a level 3 Observatory sees it");
            player.Buildings[BuildingId.Observatory].Level = 0;
            player.Tick = boss.ArrivedTick + BossSystem.StealthCloakSec;
            Assert.IsTrue(BossSystem.Revealed(player, galaxy), "the cloak fails in time");
        }

        [Test]
        public void TheSiegeDreadnought_ShellsNearbyColonies_ButNotAShieldedOne()
        {
            var (player, galaxy) = Setup();
            var boss = Visit(player, galaxy, 3);
            Assert.AreEqual(BossVariant.Siege, boss.Variant);
            boss.Tile = new TileXY(player.HomeTile.X + 100, player.HomeTile.Y); // right next door
            player.Resources = new ResourceBag(1_000_000, 1_000_000, 1_000_000).Milli();
            long before = player.Resources.Total;
            var bus = new SimEventBus();
            var shells = new List<SiegeShelled>();
            bus.Subscribe(e => { if (e is SiegeShelled s) shells.Add(s); });
            player.Tick = boss.NextSiegeTick;
            BossSystem.Tick(player, galaxy, bus);
            Assert.AreEqual(1, shells.Count);
            Assert.Less(player.Resources.Total, before);

            player.Buffs.ShieldUntilTick = int.MaxValue / 2;
            before = player.Resources.Total;
            player.Tick = boss.NextSiegeTick;
            BossSystem.Tick(player, galaxy, bus);
            Assert.AreEqual(1, shells.Count, "the Aegis keeps the shells off");
            Assert.AreEqual(before, player.Resources.Total);
        }
    }
}
