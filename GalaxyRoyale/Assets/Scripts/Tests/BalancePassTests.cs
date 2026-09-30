// The 2026-09-30 balance pass: beginner protection, mines worth upgrading,
// camps worth raiding, supply drops, the Home Guard, deeper research, more
// sources of commander XP, and a Commander's Path that teaches every feature.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class BalancePassTests
    {
        const int Hour = 3600;

        static GameState Fresh() => GameState.CreateNewGame(42, testMode: false);

        static GameState RoundTrip(GameState s) => SaveCodec.Decode(SaveCodec.Encode(SaveManager.Wrap(s, 1000))).State;

        // ---------- beginner protection ----------

        [Test]
        public void ANewColony_IsProtected_ForTwoDays()
        {
            var s = Fresh();
            var events = new SimEventBus();
            var ended = new List<string>();
            events.Subscribe(e => { if (e is ProtectionEnded p) ended.Add(p.Reason); });
            Assert.IsTrue(ProtectionSystem.Untargetable(s, 0));
            new TickEngine(s, events).Advance(Balance.BeginnerProtectionSec + 1);
            Assert.IsFalse(ProtectionSystem.Active(s));
            CollectionAssert.AreEqual(new[] { "time" }, ended);
        }

        [Test]
        public void Protection_EndsAtCommandCenter5_OrOnARaid()
        {
            var s = Fresh();
            var events = new SimEventBus();
            s.Buildings[BuildingId.CommandCenter].Level = Balance.BeginnerProtectionEndsAtCc;
            new TickEngine(s, events).Advance(1);
            Assert.IsFalse(ProtectionSystem.Active(s), "Command Center 5 ends it");

            var raider = Fresh();
            Assert.IsFalse(BotSystem.BreakShieldForAggression(raider), "no Aegis to report");
            Assert.IsFalse(ProtectionSystem.Active(raider), "raiding a commander ends it");
        }

        [Test]
        public void Protection_SurvivesTheSave_AndOldSavesHaveNone()
        {
            var s = Fresh();
            Assert.AreEqual(Balance.BeginnerProtectionSec, RoundTrip(s).Buffs.ProtectionUntilTick);
            s.Buffs.ProtectionUntilTick = 0;
            Assert.AreEqual(0, RoundTrip(s).Buffs.ProtectionUntilTick);
        }

        // ---------- mines ----------

        [Test]
        public void Mines_RampTo2x_ByLevel6_AndLevel1IsUnchanged()
        {
            Assert.AreEqual(30, Balance.ProdPerHour(30, 1));
            Assert.AreEqual(1.0, Balance.MineOutputMult(1));
            Assert.AreEqual(2.0, Balance.MineOutputMult(6));
            Assert.AreEqual(2.0, Balance.MineOutputMult(20));
            Assert.Greater(Balance.MineOutputMult(3), Balance.MineOutputMult(2));
        }

        // ---------- raids ----------

        static (GameState s, TileXY tile) CampNextDoor(int level)
        {
            var s = Fresh();
            s.Resources.Helium = 100_000_000;
            var tile = new TileXY(s.HomeTile.X + 6, s.HomeTile.Y);
            if (MapLookup.NodeAt(s, tile) != null) tile = new TileXY(s.HomeTile.X + 7, s.HomeTile.Y + 1);
            s.Map.DynamicNodes.Add(new MapNode
            {
                Id = "dyn-camp", Kind = NodeKind.Camp, Tile = tile,
                Tier = 0, Amount = 0, RatePerSec = 0, Resource = null, CampLevel = level,
            });
            return (s, tile);
        }

        [Test]
        public void CampStockpiles_GrowWithTheLevel_AndTheRaidersColony()
        {
            var (s, _) = CampNextDoor(1);
            var camp = s.Map.DynamicNodes[0];
            long cc1 = MarchSystem.CampLoot(s, camp).Total;
            Assert.GreaterOrEqual(cc1 / 1000, Nodes.CampStockpile(1), "the stockpile rides on top of the garrison's worth");
            s.Buildings[BuildingId.CommandCenter].Level = 6;
            Assert.Greater(MarchSystem.CampLoot(s, camp).Total, cc1 * 2);
            Assert.Greater(Nodes.CampStockpile(2), Nodes.CampStockpile(1) * 2);
        }

        [Test]
        public void TheFirstWin_AtACampLevel_PaysABonus_Once()
        {
            var (s, tile) = CampNextDoor(1);
            s.Ships[HullId.Cruiser] = 40;
            s.Ships[HullId.Hauler] = 5;
            var events = new SimEventBus();
            var firsts = new List<CampFirstClear>();
            events.Subscribe(e => { if (e is CampFirstClear f) firsts.Add(f); });
            var engine = new TickEngine(s, events);

            int dm = s.Premium.DarkMatter;
            var fleet = new Dictionary<HullId, int> { [HullId.Cruiser] = 40, [HullId.Hauler] = 5 };
            Assert.IsTrue(MarchSystem.SendMarch(s, fleet, tile, MarchMission.Attack, out int id).Ok);
            var march = s.Marches.Find(m => m.Id == id)!;
            Assert.Greater(MarchSystem.RaidCargoBonus(s, march), 0, "Haulers carry more on a raid");
            engine.Advance(march.ArrivesAtTick - s.Tick + 1);

            Assert.AreEqual(1, firsts.Count);
            Assert.AreEqual(dm + Balance.CampFirstClearDarkMatter(1), s.Premium.DarkMatter);
            CollectionAssert.Contains(RoundTrip(s).CampFirstClears, 1);

            // The camp comes back; beating it again pays no bonus.
            s.Map.NodeOverrides.Clear();
            engine.Advance(3 * Hour); // the fleet is home
            Assert.IsTrue(MarchSystem.SendMarch(s, fleet, tile, MarchMission.Attack, out id).Ok);
            engine.Advance(s.Marches.Find(m => m.Id == id)!.ArrivesAtTick - s.Tick + 1);
            Assert.AreEqual(2, s.Stats.CampsCleared);
            Assert.AreEqual(1, firsts.Count, "one bonus per camp level");
        }

        [Test]
        public void RaidCargoBonus_OnlyOnAttacks_AndOnlyForHaulers()
        {
            var s = Fresh();
            var gather = new March { Mission = MarchMission.Gather, Ships = new() { [HullId.Hauler] = 2 } };
            var fighters = new March { Mission = MarchMission.Attack, Ships = new() { [HullId.Fighter] = 20 } };
            var haulers = new March { Mission = MarchMission.Attack, Ships = new() { [HullId.Hauler] = 2 } };
            Assert.AreEqual(0, MarchSystem.RaidCargoBonus(s, gather));
            Assert.AreEqual(0, MarchSystem.RaidCargoBonus(s, fighters));
            Assert.AreEqual((long)(MarchSystem.EffCargoCap(s, haulers.Ships) * 0.5), MarchSystem.RaidCargoBonus(s, haulers));
        }

        // ---------- supply drops ----------

        [Test]
        public void SupplyDrops_LandEvery4Hours_AndUpTo3Wait()
        {
            var s = Fresh();
            var engine = new TickEngine(s, new SimEventBus());
            engine.Advance(Balance.SupplyDropEverySec);
            Assert.AreEqual(1, s.SupplyCrates);
            engine.Advance(Balance.SupplyDropEverySec * 5);
            Assert.AreEqual(Balance.SupplyDropMaxStored, s.SupplyCrates, "no more than three wait");

            var one = SupplySystem.CrateContents(s);
            long gold = s.Resources.Gold;
            var got = SupplySystem.Collect(s)!;
            Assert.AreEqual(one.Gold * 3, got.Gold);
            Assert.AreEqual(gold + got.Gold, s.Resources.Gold);
            Assert.AreEqual(0, s.SupplyCrates);
            Assert.AreEqual(3, s.Stats.SupplyDropsCollected);
            Assert.IsNull(SupplySystem.Collect(s));

            var back = RoundTrip(s);
            Assert.AreEqual(s.NextSupplyDropTick, back.NextSupplyDropTick);
            Assert.AreEqual(3, back.Stats.SupplyDropsCollected);
        }

        [Test]
        public void ACrate_KeepsPaceWithTheMines()
        {
            var s = Fresh();
            Assert.GreaterOrEqual(SupplySystem.CrateContents(s).Gold, Balance.SupplyDropFloor * 1000L, "a floor for a new colony");
            s.Buildings[BuildingId.GoldMine].Level = 4;
            long small = SupplySystem.CrateContents(s).Gold;
            s.Buildings[BuildingId.GoldMine].Level = 10;
            Assert.Greater(SupplySystem.CrateContents(s).Gold, small * 2);
        }

        // ---------- Home Guard ----------

        [Test]
        public void HomeGuard_CountsDockedWarshipsOnly_UpTo15Percent()
        {
            var s = Fresh();
            Assert.AreEqual(0f, ResourceSystem.HomeGuardBonus(s));
            s.Ships[HullId.Hauler] = 1000;
            s.Ships[HullId.Probe] = 1000;
            Assert.AreEqual(0f, ResourceSystem.HomeGuardBonus(s), "cargo and recon hulls don't guard");
            long gold = ResourceSystem.GetRates(s).Gold;
            s.Ships[HullId.Fighter] = 10_000;
            Assert.AreEqual(Balance.HomeGuardMaxBonus, ResourceSystem.HomeGuardBonus(s), 1e-6);
            Assert.AreEqual(gold * 1.15, ResourceSystem.GetRates(s).Gold, gold * 0.01);
            s.Ships[HullId.Fighter] = 5; // 200 invested → 20 might, of 25 needed at CC 1
            Assert.AreEqual(Balance.HomeGuardMaxBonus * 20 / 25f, ResourceSystem.HomeGuardBonus(s), 1e-4);
        }

        // ---------- research and XP ----------

        [Test]
        public void TierThreeTechs_NeedResearchLab10Plus_AndSurviveTheSave()
        {
            foreach (var id in new[] { TechId.DeepSpaceMining, TechId.SubspaceNavigation, TechId.AdaptiveShielding, TechId.CommandDoctrine })
            {
                Assert.GreaterOrEqual(Techs.Defs[id].LabLevelReq, 10, id.ToString());
                CollectionAssert.Contains(Techs.All, id);
                var s = Fresh();
                s.Research[id] = 3;
                Assert.AreEqual(3, RoundTrip(s).Research[id]);
            }
        }

        [Test]
        public void CommandDoctrine_AddsCommanderXp()
        {
            var plain = Fresh();
            var doctrine = Fresh();
            doctrine.Buildings[BuildingId.ResearchLab].Level = 10;
            doctrine.Research[TechId.CommandDoctrine] = 10; // +50%
            plain.Buildings[BuildingId.ResearchLab].Level = 10;
            var bus = new SimEventBus();
            CommanderSystem.Tick(plain, bus);
            CommanderSystem.Tick(doctrine, bus);
            Assert.Greater(doctrine.Commander.Xp, plain.Commander.Xp * 1.3);
        }

        [Test]
        public void Gathering_Dailies_SupplyAndTheWilds_EarnXp()
        {
            var s = Fresh();
            long before = CommanderSystem.Score(s);
            s.Stats.GatheredMilli = 20_000_000;
            s.Stats.DailiesClaimed = 2;
            s.Stats.SupplyDropsCollected = 3;
            Assert.AreEqual(before + 10 + 80 + 30, CommanderSystem.Score(s));
            var back = RoundTrip(s);
            Assert.AreEqual(20_000_000, back.Stats.GatheredMilli);
            Assert.AreEqual(2, back.Stats.DailiesClaimed);
        }

        // ---------- the Commander's Path teaches everything ----------

        [Test]
        public void ThePath_TeachesEveryBasicFeature()
        {
            var goals = Quests.Chain.Select(q => q.Goal).ToHashSet();
            foreach (var g in new[]
                     {
                         QuestGoal.Gathered, QuestGoal.ExtraMines, QuestGoal.DailiesClaimed, QuestGoal.SkillsLearned,
                         QuestGoal.ItemsUsed, QuestGoal.ClanJoined, QuestGoal.MarketTrades, QuestGoal.WildsSurveyed,
                         QuestGoal.SupplyDrops, QuestGoal.CampsCleared, QuestGoal.ShipsBuilt, QuestGoal.CampScouted,
                         QuestGoal.BattlesWon, QuestGoal.ResearchLevel,
                     })
                Assert.IsTrue(goals.Contains(g), $"the Path teaches {g}");
            var buildings = Quests.Chain.Where(q => q.Goal == QuestGoal.BuildingLevel).Select(q => q.Building).ToHashSet();
            foreach (var b in new[]
                     {
                         BuildingId.CommandCenter, BuildingId.QuartzExtractor, BuildingId.HeliumRefinery, BuildingId.PowerPlant,
                         BuildingId.Shipyard, BuildingId.ResearchLab, BuildingId.RadarStation, BuildingId.Warehouse,
                     })
                Assert.IsTrue(buildings.Contains(b), $"the Path builds the {b}");
            Assert.AreEqual(Quests.Chain.Count,
                Achievements.All.First(a => a.Id == "trailblazer").Target, "Trailblazer means the whole Path");
        }

        [Test]
        public void NewPathGoals_ReadTheRecord()
        {
            var s = Fresh();
            s.Stats.GatheredMilli = 3_000_000;
            s.Stats.ItemsUsed = 1;
            s.ClanId = 7;
            s.Commander.Skills["any"] = 2;
            QuestDef Q(QuestGoal g) => Quests.Chain.First(q => q.Goal == g);
            Assert.IsTrue(QuestSystem.IsComplete(s, Q(QuestGoal.Gathered)));
            Assert.IsTrue(QuestSystem.IsComplete(s, Q(QuestGoal.ItemsUsed)));
            Assert.IsTrue(QuestSystem.IsComplete(s, Q(QuestGoal.ClanJoined)));
            Assert.IsTrue(QuestSystem.IsComplete(s, Q(QuestGoal.SkillsLearned)));
            Assert.IsFalse(QuestSystem.IsComplete(s, Q(QuestGoal.MarketTrades)));
        }

        // ---------- dailies in the save (2026-09-30) ----------

        [Test]
        public void TheDaysObjectives_SurviveTheSave()
        {
            var s = Fresh();
            s.Daily.Day = "20260930";
            s.Daily.Builds = 2;
            s.Daily.GatherWhole = 40_000;
            s.Daily.ShipsBase = 7;
            s.Daily.Claimed.Add("spy");
            var back = RoundTrip(s).Daily;
            Assert.AreEqual("20260930", back.Day);
            Assert.AreEqual(2, back.Builds);
            Assert.AreEqual(40_000, back.GatherWhole);
            Assert.AreEqual(7, back.ShipsBase);
            CollectionAssert.Contains(back.Claimed, "spy");
        }
    }
}
