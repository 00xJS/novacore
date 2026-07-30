// v12 save-format tests. The JSON shape must match v1's TypeScript client
// exactly (cloud saves are shared cross-client), so these check three things:
//   1. Encode → Decode → Encode is byte-identical (deterministic round trip).
//   2. The emitted key/enum strings are the v1 spellings (spot literals).
//   3. Every populated corner of GameState survives the trip intact.
using System.Collections.Generic;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Save;

namespace GalaxyRoyale.Sim.Tests
{
    public class SaveCodecTests
    {
        [Test]
        public void NewGame_RoundTrip_IsByteIdentical()
        {
            var state = GameState.CreateNewGame(42);
            var file = SaveManager.Wrap(state, 1_234_567_890_123);

            string json1 = SaveCodec.Encode(file);
            var decoded = SaveCodec.Decode(json1);
            string json2 = SaveCodec.Encode(decoded);

            Assert.AreEqual(json1, json2, "round trip must be deterministic");
            Assert.IsNotNull(SaveManager.Unwrap(decoded), "decoded file must pass the version gate");
            Assert.AreEqual(1_234_567_890_123, decoded.SavedAtMs);
            Assert.AreEqual(42, decoded.State.Seed);
        }

        [Test]
        public void Encode_UsesV1KeyAndEnumSpellings()
        {
            var file = SaveManager.Wrap(GameState.CreateNewGame(42), 1000);
            string json = SaveCodec.Encode(file);

            // Envelope + camelCase state keys.
            StringAssert.Contains($"\"version\":{SaveManager.CurrentVersion}", json);
            StringAssert.Contains("\"savedAtMs\":1000", json);
            StringAssert.Contains("\"homeTile\":", json);
            StringAssert.Contains("\"darkMatter\":", json);
            StringAssert.Contains("\"nextMarchId\":", json);
            StringAssert.Contains("\"nodeOverrides\":", json);
            // v1 enum ids (camelCase buildings, lowercase hulls).
            StringAssert.Contains("\"commandCenter\":{\"level\":1}", json);
            StringAssert.Contains("\"quartzExtractor\":{\"level\":0}", json);
            StringAssert.Contains("\"fighter\":0", json);
            StringAssert.Contains("\"researchLab\"", json);
        }

        [Test]
        public void BusyState_EveryFieldSurvivesTheTrip()
        {
            var s = GameState.CreateNewGame(7);
            s.Tick = 9_999;
            s.HomeTile = new TileXY(1250, 800);
            s.Profile = new Profile { Name = "Ares \"the\" Bold", AvatarSeed = 5 };
            s.Resources = new ResourceBag { Gold = 10_000_000_000L, Quartz = 3, Helium = 0 };
            s.Premium.DarkMatter = 1_775;
            s.Buildings[BuildingId.CommandCenter].Level = 12;

            s.BuildQueue.Add(new BuildOrder { Building = BuildingId.Warehouse, ToLevel = 4, EndsAtTick = 10_100 });
            s.BuildQueue.Add(new BuildOrder { Building = BuildingId.GoldMine, ToLevel = 1, EndsAtTick = 0, MineId = 3 });
            s.ExtraMines.Add(new ExtraMine { Id = 3, Type = MineType.GoldMine, Level = 0, Plot = 4 });
            s.NextMineId = 4;

            s.Ships[HullId.Fighter] = 120;
            s.Ships[HullId.Probe] = 2;
            s.ShipQueue.Add(new ShipOrder { Hull = HullId.Cruiser, Remaining = 5, NextDoneAtTick = 10_050 });

            s.Marches.Add(new March
            {
                Id = 9,
                Phase = MarchPhase.Returning,
                Ships = new Dictionary<HullId, int> { [HullId.Hauler] = 7 },
                Node = new TileXY(140, 220),
                LegFrom = new Position(140.5, 219.25),
                LegTo = new Position(1250, 800),
                DepartedAtTick = 9_000,
                ArrivesAtTick = 10_800,
                Cargo = new ResourceBag { Gold = 50_000, Quartz = 0, Helium = 0 },
                HeliumSpent = 12_000,
                CargoDm = 42_000, // v13: 42 DM on board
                Mission = MarchMission.Gather,
            });
            s.NextMarchId = 10;

            s.Map.NodeOverrides["140,220"] = new NodeOverride
                { Remaining = 4_000, Cleared = false, RespawnAtTick = 0, Retired = false };
            s.Map.NodeOverrides["90,90"] = new NodeOverride
                { Remaining = null, Cleared = true, RespawnAtTick = 31_600, Retired = true };
            s.Map.DynamicNodes.Add(new MapNode
            {
                Id = "dyn-1", Kind = NodeKind.HeliumCloud, Tile = new TileXY(95, 92),
                Tier = 2, Amount = 800_000, RatePerSec = 40, Resource = ResourceId.Helium, CampLevel = 0,
            });
            s.Map.DynamicNodes.Add(new MapNode
            {
                Id = "dyn-2", Kind = NodeKind.Camp, Tile = new TileXY(60, 61),
                Tier = 3, Amount = 0, RatePerSec = 0, Resource = null, CampLevel = 4,
            });
            s.Map.NextDynId = 3;

            s.Research[TechId.CargoHolds] = 3;
            s.Research[TechId.PrefabAssembly] = 1;
            s.ResearchQueue.Add(new ResearchOrder { TechId = TechId.IonThrusters, ToLevel = 2, EndsAtTick = 11_000 });

            s.Inventory.Clear(); // TestMode pre-fills speed-ups; index assertions below want exactly one entry
            s.Inventory.Add(new InventoryEntry { ItemId = "speed-15m", Count = 3 });
            s.Buffs = new Buffs
            {
                ProdBoostUntilTick = 12_000,
                ExtraBuildSlotUntilTick = 0,
                EnergyBoostUntilTick = 13_000,
                ExtraResearchSlotUntilTick = 1,
            };

            s.Mailbox.Add(new SpyReport
            {
                Id = 21, AtTick = 9_500, Target = new TileXY(60, 61),
                Subject = "Recon: Pirate Camp", Read = true, Favorite = true,
                Intel = new SpyIntel
                {
                    Kind = NodeKind.Camp, Tier = 3, CampLevel = 4,
                    Garrison = new Dictionary<HullId, int> { [HullId.Fighter] = 30, [HullId.Bomber] = 10 },
                },
            });
            s.Mailbox.Add(new SpyReport
            {
                Id = 22, AtTick = 9_600, Target = new TileXY(10, 10),
                Subject = "Recon: empty space", Read = false, Favorite = false,
                Intel = new SpyIntel { Kind = null }, // "empty" in v1
            });
            s.Mailbox.Add(new BattleMailReport
            {
                Id = 23, AtTick = 9_700, Target = new TileXY(60, 61),
                Subject = "Battle at 60,61", Read = false, Favorite = false,
                Report = new BattleReport
                {
                    Attacker = new Dictionary<HullId, int> { [HullId.Cruiser] = 20 },
                    Defender = new Dictionary<HullId, int> { [HullId.Fighter] = 30 },
                    Winner = BattleWinner.Attacker,
                    Rounds =
                    {
                        new RoundLog
                        {
                            Round = 1,
                            AttackerLosses = new Dictionary<HullId, int> { [HullId.Cruiser] = 2 },
                            DefenderLosses = new Dictionary<HullId, int> { [HullId.Fighter] = 30 },
                        },
                    },
                    AttackerSurvivors = new Dictionary<HullId, int> { [HullId.Cruiser] = 18 },
                    DefenderSurvivors = new Dictionary<HullId, int>(),
                    Loot = new ResourceBag { Gold = 9_000, Quartz = 1_000, Helium = 0 },
                    Location = new TileXY(60, 61),
                    DefenderName = "Pirate Camp",
                },
            });
            s.NextReportId = 24;

            s.Skins.Owned.Add("crimson");
            s.Skins.ActivePlanet = "crimson";
            s.Stats = new Stats { BattlesWon = 4, BattlesLost = 1, MarchesSent = 17 };

            // ---- the actual trip ----
            string json1 = SaveCodec.Encode(SaveManager.Wrap(s, 99));
            var back = SaveCodec.Decode(json1).State;
            string json2 = SaveCodec.Encode(SaveManager.Wrap(back, 99));
            Assert.AreEqual(json1, json2, "busy state must re-encode identically");

            // ---- spot checks across every section ----
            Assert.AreEqual(9_999, back.Tick);
            Assert.AreEqual(new TileXY(1250, 800), back.HomeTile);
            Assert.AreEqual("Ares \"the\" Bold", back.Profile.Name);
            Assert.AreEqual(10_000_000_000L, back.Resources.Gold, "long gold survives");
            Assert.AreEqual(12, back.Buildings[BuildingId.CommandCenter].Level);
            Assert.AreEqual(2, back.BuildQueue.Count);
            Assert.AreEqual(3, back.BuildQueue[1].MineId);
            Assert.IsNull(back.BuildQueue[0].MineId);
            Assert.AreEqual(MineType.GoldMine, back.ExtraMines[0].Type);
            Assert.AreEqual(120, back.Ships[HullId.Fighter]);
            Assert.AreEqual(HullId.Cruiser, back.ShipQueue[0].Hull);

            var march = back.Marches[0];
            Assert.AreEqual(MarchPhase.Returning, march.Phase);
            Assert.AreEqual(MarchMission.Gather, march.Mission);
            Assert.AreEqual(140.5, march.LegFrom.X, 1e-12, "sub-tile leg position survives");
            Assert.AreEqual(219.25, march.LegFrom.Y, 1e-12);
            Assert.AreEqual(7, march.Ships[HullId.Hauler]);
            Assert.AreEqual(50_000, march.Cargo.Gold);
            Assert.AreEqual(42_000, march.CargoDm, "v13 dark-matter cargo survives");

            Assert.AreEqual(4_000, back.Map.NodeOverrides["140,220"].Remaining);
            Assert.IsTrue(back.Map.NodeOverrides["90,90"].Cleared);
            Assert.IsTrue(back.Map.NodeOverrides["90,90"].Retired);
            Assert.AreEqual(31_600, back.Map.NodeOverrides["90,90"].RespawnAtTick);
            Assert.AreEqual(NodeKind.HeliumCloud, back.Map.DynamicNodes[0].Kind);
            Assert.AreEqual(ResourceId.Helium, back.Map.DynamicNodes[0].Resource);
            Assert.IsNull(back.Map.DynamicNodes[1].Resource);
            Assert.AreEqual(4, back.Map.DynamicNodes[1].CampLevel);

            Assert.AreEqual(3, back.Research[TechId.CargoHolds]);
            Assert.AreEqual(TechId.IonThrusters, back.ResearchQueue[0].TechId);
            Assert.AreEqual("speed-15m", back.Inventory[0].ItemId);
            Assert.AreEqual(13_000, back.Buffs.EnergyBoostUntilTick);

            var spy = (SpyReport)back.Mailbox[0];
            Assert.IsTrue(spy.Favorite);
            Assert.AreEqual(NodeKind.Camp, spy.Intel.Kind);
            Assert.AreEqual(30, spy.Intel.Garrison![HullId.Fighter]);
            Assert.IsNull(((SpyReport)back.Mailbox[1]).Intel.Kind, "empty intel kind survives");

            var battle = (BattleMailReport)back.Mailbox[2];
            Assert.AreEqual(BattleWinner.Attacker, battle.Report.Winner);
            Assert.AreEqual(18, battle.Report.AttackerSurvivors[HullId.Cruiser]);
            Assert.AreEqual(1, battle.Report.Rounds.Count);
            Assert.AreEqual(9_000, battle.Report.Loot!.Gold);
            Assert.AreEqual("Pirate Camp", battle.Report.DefenderName);

            Assert.AreEqual("crimson", back.Skins.ActivePlanet);
            CollectionAssert.AreEqual(new[] { "default", "crimson" }, back.Skins.Owned);
            Assert.AreEqual(17, back.Stats.MarchesSent);
        }
    }
}
