// The Citadel (2026-09-30): the Relic Vault, the Academy, the Missile Silo, the
// Trade Consulate and the Terraformer on the north pole.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class CitadelTests
    {
        const int Hour = 3600;

        static GameState Fresh()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.Buffs.ProtectionUntilTick = 0;
            s.Resources = new ResourceBag(5_000_000, 5_000_000, 5_000_000).Milli();
            return s;
        }

        static GameState RoundTrip(GameState s) => SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;

        [Test]
        public void TheCitadel_HasFivePads_OnTheNorthPole()
        {
            var pads = BaseLayout.Pads.Where(p => p.District == BaseDistrict.Citadel).ToList();
            Assert.AreEqual(5, pads.Count);
            foreach (var id in new[] { BuildingId.RelicVault, BuildingId.Academy, BuildingId.MissileSilo,
                         BuildingId.TradeConsulate, BuildingId.Terraformer })
            {
                Assert.IsTrue(Buildings.IsCitadel(id));
                Assert.IsFalse(Buildings.IsFrontier(id));
                Assert.AreEqual(BaseDistrict.Citadel, BaseLayout.BuildingPad(id).District);
                Assert.Greater(Buildings.Defs[id].UnlockCc, 0);
            }
            var s = Fresh();
            s.Buildings[BuildingId.Academy].Level = 3;
            Assert.AreEqual(3, RoundTrip(s).Buildings[BuildingId.Academy].Level);
        }

        // ---------- the Relic Vault ----------

        [Test]
        public void Relics_GiveTheirBonus_UpToTheVaultsLevel()
        {
            var s = Fresh();
            s.Relics[RelicKind.AncientDrill] = 5;
            Assert.AreEqual(0f, RelicSystem.EffectTotal(s, TechEffectKind.ProdMultiplier), "no vault, no display");
            s.Buildings[BuildingId.RelicVault].Level = 3;
            Assert.AreEqual(0.03f, RelicSystem.EffectTotal(s, TechEffectKind.ProdMultiplier), 1e-5);
            Assert.AreEqual(1.03f, ResearchSystem.ProdMultiplier(s), 1e-5, "it joins the research totals");
            s.Buildings[BuildingId.RelicVault].Level = 10;
            Assert.AreEqual(0.05f, RelicSystem.EffectTotal(s, TechEffectKind.ProdMultiplier), 1e-5, "only as many as you own");
            Assert.AreEqual(5, RoundTrip(s).Relics[RelicKind.AncientDrill]);
        }

        [Test]
        public void ClaimingAWildsRelic_BringsOneHome()
        {
            var s = Fresh();
            s.Wilds.Sectors[3] = new WildsSector { Index = 3, Find = WildsFind.Relic, RewardDM = 15, Surveys = 2 };
            Assert.IsTrue(WildsSystem.Claim(s, 3, out var relic).Ok);
            Assert.IsNotNull(relic);
            Assert.AreEqual(1, RelicSystem.Count(s, relic!.Value));
            var again = Fresh();
            Assert.AreEqual(relic, RelicSystem.Grant(again, 3, 2), "the same sector and survey give the same relic");
        }

        // ---------- the Terraformer ----------

        [Test]
        public void Terraforming_RaisesItsResource_StageByStage()
        {
            var s = Fresh();
            Assert.IsFalse(TerraformSystem.Start(s, TerraformPath.Metallic).Ok, "needs the Terraformer");
            s.Buildings[BuildingId.Terraformer].Level = 4; // two stages allowed
            long gold = ResourceSystem.GetRates(s).Gold;
            long before = s.Resources.Total;
            Assert.IsTrue(TerraformSystem.Start(s, TerraformPath.Metallic).Ok);
            Assert.Less(s.Resources.Total, before, "the project costs resources");
            Assert.IsFalse(TerraformSystem.Start(s, TerraformPath.Metallic).Ok, "one project at a time");
            new TickEngine(s, new SimEventBus()).Advance(TerraformSystem.ProjectSeconds(1));
            Assert.AreEqual(1, s.Terraform.Stage);
            Assert.AreEqual(gold * 1.05, ResourceSystem.GetRates(s).Gold, gold * 0.01);
            var back = RoundTrip(s);
            Assert.AreEqual(TerraformPath.Metallic, back.Terraform.Path);
            Assert.AreEqual(1, back.Terraform.Stage);

            // Switching paths starts over.
            Assert.IsTrue(TerraformSystem.Start(s, TerraformPath.Temperate).Ok);
            Assert.AreEqual(TerraformPath.Temperate, s.Terraform.Path);
            Assert.AreEqual(0, s.Terraform.Stage);
        }

        [Test]
        public void TheTerraformersLevel_CapsTheStages()
        {
            Assert.AreEqual(0, TerraformSystem.MaxStage(0));
            Assert.AreEqual(1, TerraformSystem.MaxStage(1));
            Assert.AreEqual(2, TerraformSystem.MaxStage(4));
            Assert.AreEqual(5, TerraformSystem.MaxStage(30));
            var s = Fresh();
            s.Buildings[BuildingId.Terraformer].Level = 1;
            s.Terraform.Path = TerraformPath.Oceanic;
            s.Terraform.Stage = 1;
            Assert.IsFalse(TerraformSystem.Check(s, TerraformPath.Oceanic).Ok);
        }

        // ---------- the Academy ----------

        [Test]
        public void TheCommander_LeadsAFleet_ThatFightsHarder_AndIsWoundedIfItsLost()
        {
            var s = Fresh();
            s.Ships[HullId.Fighter] = 20;
            var fleet = new Dictionary<HullId, int> { [HullId.Fighter] = 10 };
            var target = new TileXY(s.HomeTile.X + 30, s.HomeTile.Y);
            Assert.IsTrue(MarchSystem.SendRaidMarch(s, fleet, target, out int id).Ok);
            Assert.IsFalse(AcademySystem.Lead(s, id).Ok, "no Academy yet");
            s.Buildings[BuildingId.Academy].Level = 5;
            Assert.IsTrue(AcademySystem.Lead(s, id).Ok);
            Assert.Greater(ResearchSystem.CombatModsFor(s, id).AtkMult, ResearchSystem.CombatMods(s).AtkMult);
            Assert.AreEqual(ResearchSystem.CombatMods(s).AtkMult, ResearchSystem.CombatModsFor(s, id + 1).AtkMult);
            Assert.AreEqual(id, RoundTrip(s).CaptainMarchId);

            // The fleet is destroyed: the commander is wounded.
            s.Marches.RemoveAll(m => m.Id == id);
            AcademySystem.Tick(s, new SimEventBus());
            Assert.AreEqual(0, s.CaptainMarchId);
            Assert.IsTrue(AcademySystem.Wounded(s));
            Assert.IsFalse(AcademySystem.CanLead(s).Ok);
        }

        [Test]
        public void ALedFleet_ComingHome_FreesTheCommander()
        {
            var s = Fresh();
            s.Buildings[BuildingId.Academy].Level = 1;
            s.Ships[HullId.Fighter] = 5;
            var target = new TileXY(s.HomeTile.X + 10, s.HomeTile.Y);
            Assert.IsTrue(MarchSystem.SendRaidMarch(s, new Dictionary<HullId, int> { [HullId.Fighter] = 5 }, target, out int id).Ok);
            Assert.IsTrue(AcademySystem.Lead(s, id).Ok);
            new TickEngine(s, new SimEventBus()).Advance(2 * Hour);
            Assert.AreEqual(0, s.CaptainMarchId);
            Assert.IsFalse(AcademySystem.Wounded(s), "home safe");
        }

        [Test]
        public void TheAcademy_AddsXp_AndCutsResets()
        {
            var s = Fresh();
            s.Commander.Respecs = 1;
            int full = CommanderSystem.RespecCost(s);
            s.Buildings[BuildingId.Academy].Level = 10;
            Assert.Less(CommanderSystem.RespecCost(s), full);
            Assert.AreEqual(0.3f, AcademySystem.XpBonus(s), 1e-5);
        }

        // ---------- the Missile Silo ----------

        [Test]
        public void TheSilo_FiresOnARaidTheRadarHasSeen_ThenReloads()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 20);
            var s = Fresh();
            s.Buildings[BuildingId.RadarStation].Level = 10;
            s.Buildings[BuildingId.MissileSilo].Level = 10;
            var atk = new BotAttack
            {
                Id = 77, BotId = galaxy.Bots[0].Id, IsFleet = true,
                Ships = new Dictionary<HullId, int> { [HullId.Cruiser] = 100 },
                LaunchTick = s.Tick, ArrivesAtTick = s.Tick + 60,
            };
            galaxy.Inbound.Add(atk);
            Assert.IsTrue(SiloSystem.Fire(s, galaxy, 77, null, out int killed).Ok);
            Assert.AreEqual(20, killed, "20% at level 10");
            Assert.AreEqual(80, atk.Ships[HullId.Cruiser]);
            Assert.IsFalse(SiloSystem.CanFire(s, galaxy, 77).Ok, "reloading");
            Assert.AreEqual(SiloSystem.ReloadSec(10), s.SiloReadyTick - s.Tick);

            // A raid still beyond the radar's reach can't be targeted.
            s.SiloReadyTick = 0;
            atk.ArrivesAtTick = s.Tick + 10 * Hour;
            Assert.IsFalse(SiloSystem.CanFire(s, galaxy, 77).Ok);
        }

        // ---------- the Trade Consulate ----------

        [Test]
        public void AContract_FliesTheGoodsOut_AndComesHomeWithThePay()
        {
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, Balance.HomeTile, 60);
            var s = Fresh();
            Assert.IsEmpty(ConsulateSystem.Board(s, galaxy), "no consulate, no board");
            s.Buildings[BuildingId.TradeConsulate].Level = 5;
            var board = ConsulateSystem.Board(s, galaxy);
            Assert.AreEqual(2, board.Count);
            var c = board[0];
            Assert.AreNotEqual(c.Give, c.Get);
            Assert.Greater(c.GetMilli, c.GiveMilli);
            s.Ships[HullId.Hauler] = ConsulateSystem.HaulersNeeded(s, c) + 5;
            long giveBefore = s.Resources.Get(c.Give);
            long getBefore = s.Resources.Get(c.Get);
            if (c.Get == ResourceId.Helium) getBefore -= MarchSystem.PreviewMarch(s,
                new Dictionary<HullId, int> { [HullId.Hauler] = ConsulateSystem.HaulersNeeded(s, c) }, c.ClientTile).HeliumCost;
            int dm = s.Premium.DarkMatter;

            Assert.IsTrue(ConsulateSystem.Accept(s, c, out int id).Ok);
            // The goods are aboard (and helium goods share the tank with the flight's fuel).
            Assert.LessOrEqual(s.Resources.Get(c.Give), giveBefore - c.GiveMilli, "the goods are aboard");
            Assert.IsFalse(ConsulateSystem.Accept(s, c, out _).Ok, "once per contract");
            var back = RoundTrip(s);
            Assert.IsNotNull(back.Marches.Single(m => m.Id == id).ContractPay);

            var events = new SimEventBus();
            var delivered = new List<ContractDelivered>();
            events.Subscribe(e => { if (e is ContractDelivered d) delivered.Add(d); });
            new TickEngine(s, events).Advance(24 * Hour);
            Assert.AreEqual(1, delivered.Count);
            Assert.IsFalse(s.Marches.Exists(m => m.Id == id), "home again");
            Assert.AreEqual(getBefore + c.GetMilli, s.Resources.Get(c.Get), c.GetMilli / 1000.0);
            Assert.AreEqual(dm + c.DarkMatter, s.Premium.DarkMatter);
            Assert.AreEqual(1, s.Stats.ContractsDone);
        }
    }
}
