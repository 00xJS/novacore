// Standard-mode pacing (balance pass, user request 2026-09-28): a scripted
// "diligent commander" plays a STANDARD galaxy — the honest 500/300/100 start —
// for a week of galaxy time. They check in every 20 minutes while awake and
// sleep 8 hours a night; on each check-in they claim quests and event goals,
// keep both build queues busy (energy first, then production, then the rest,
// with the Command Center when it caps them), research, keep haulers
// gathering from the nearest fields, and scout + raid pirate camps the
// forecast says they can beat. Since the 2026-09-30 balance pass they also
// open supply drops, survey the Wilds, bring Haulers on raids, keep a Home
// Guard docked and follow Act II of the Commander's Path.
// The timeline (PACE lines in the test output)
// shows when each milestone lands and how long the queues sat waiting on
// resources — the dead zones a balance pass looks for.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class StandardPacingTests
    {
        const int Hour = 3600, Day = 24 * Hour;
        const int CheckInSec = 20 * 60;

        sealed class Pace
        {
            public readonly Dictionary<int, int> CommandCenterAt = new();
            public readonly List<(int step, int tick)> QuestsAt = new();
            /// <summary>Awake check-ins that found a build slot free and nothing affordable.</summary>
            public readonly int[] StarvedChecksByDay = new int[8];
            public readonly int[] ChecksByDay = new int[8];
            /// <summary>Awake time in a row with nothing under construction (sleep doesn't count).</summary>
            public int LongestStarvedStreakSec, StarvedStreakSec;
            public int CampsRaided, GatherTrips;
            // Where the week's resources came from and went (whole units, per day).
            public readonly long[] MinedByDay = new long[8], GatheredByDay = new long[8], LootedByDay = new long[8],
                SupplyByDay = new long[8];
            public readonly long[] BuildSpendByDay = new long[8], ResearchSpendByDay = new long[8], ShipSpendByDay = new long[8];
            public readonly Dictionary<int, MarchMission> MissionOf = new();
            /// <summary>Why the quest's first battle couldn't launch, first time seen (debug).</summary>
            public readonly List<string> QuestFightBlocks = new();
        }

        static int DayOf(GameState s) => Math.Min(7, s.Tick / Day);

        [Test]
        public void DiligentCommander_StandardGalaxy_FirstWeek()
        {
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.HomeTile = Spawn.SpawnTileFor("player-pacing", Spawn.GalaxySeed);
            var events = new SimEventBus();
            var engine = new TickEngine(s, events);
            var pace = new Pace();
            events.Subscribe(e =>
            {
                if (e is not MarchReturned back) return;
                long whole = back.Cargo.Total / 1000;
                if (!pace.MissionOf.TryGetValue(back.MarchId, out var mission)) return;
                if (mission == MarchMission.Gather) pace.GatheredByDay[DayOf(s)] += whole;
                else pace.LootedByDay[DayOf(s)] += whole;
            });
            int lastCc = s.Buildings[BuildingId.CommandCenter].Level;
            var levelByDay = new int[8];

            while (s.Tick < 7 * Day)
            {
                bool awake = s.Tick % Day < 16 * Hour;
                if (awake) CheckIn(s, pace);
                int step = awake ? CheckInSec : Hour;
                pace.MinedByDay[DayOf(s)] += ResourceSystem.GetRates(s).Total * step / Hour / 1000;
                engine.Advance(step);
                // The galaxy-level progression the game runs each tick (no rivals here).
                AchievementSystem.CheckNew(s);
                CommanderSystem.Tick(s, events);
                levelByDay[DayOf(s)] = s.Commander.Level;

                int cc = s.Buildings[BuildingId.CommandCenter].Level;
                for (int l = lastCc + 1; l <= cc; l++) pace.CommandCenterAt[l] = s.Tick;
                lastCc = cc;
            }

            string T(int tick) => $"{tick / Day}d {tick % Day / Hour:00}h";
            var cc2 = pace.CommandCenterAt.OrderBy(kv => kv.Key).Select(kv => $"CC{kv.Key}@{T(kv.Value)}");
            TestContext.Out.WriteLine($"PACE command center: {string.Join("  ", cc2)}");
            TestContext.Out.WriteLine($"PACE quests: {string.Join("  ", pace.QuestsAt.Select(q => $"Q{q.step + 1}@{T(q.tick)}"))}");
            TestContext.Out.WriteLine("PACE commander level at the end of each day: " + string.Join("  ",
                Enumerable.Range(0, 7).Select(d => $"d{d}:L{levelByDay[d]}")) + $" · {s.Commander.Xp:N0} XP");
            TestContext.Out.WriteLine("PACE starved check-ins by day: " + string.Join("  ",
                Enumerable.Range(0, 7).Select(d => $"d{d}:{pace.StarvedChecksByDay[d]}/{pace.ChecksByDay[d]}")));
            string Days(long[] v) => string.Join(" ", v.Take(7).Select(x => UiK(x)));
            TestContext.Out.WriteLine($"PACE income/day  mined [{Days(pace.MinedByDay)}]  gathered [{Days(pace.GatheredByDay)}]  looted [{Days(pace.LootedByDay)}]  supply [{Days(pace.SupplyByDay)}]");
            TestContext.Out.WriteLine($"PACE home guard at the end: +{ResourceSystem.HomeGuardBonus(s) * 100:0}% production · wilds surveys {Surveys(s)} · camp levels first-cleared {string.Join(",", s.CampFirstClears)}");
            TestContext.Out.WriteLine($"PACE spend/day  buildings [{Days(pace.BuildSpendByDay)}]  research [{Days(pace.ResearchSpendByDay)}]  ships [{Days(pace.ShipSpendByDay)}]");
            if (pace.QuestFightBlocks.Count > 0)
                TestContext.Out.WriteLine($"PACE first-blood blocked: {string.Join(" | ", pace.QuestFightBlocks)}");
            TestContext.Out.WriteLine($"PACE longest wait on resources (awake): {pace.LongestStarvedStreakSec / 60} min · " +
                $"camps raided {pace.CampsRaided} · gather trips {pace.GatherTrips} · might {PowerSystem.ComputePower(s):N0} · " +
                $"research levels {s.Research.Values.Sum()} · ships {s.Ships.Values.Sum()}");

            // Balance-pass guard rails. The 2026-09-28 run: the path done at 3 h
            // (First Blood had been grounded for two days by helium), CC 9 by
            // day 5, 17 camps raided, and mid-game saving stretches of up to ~7 h
            // (a CC 8-9 upgrade costs 3-4 h of income) while gathering continues.
            Assert.AreEqual(Quests.Chain.Count, s.QuestStep, "both acts of the Commander's Path get finished in the week");
            Assert.Less(pace.QuestsAt[Quests.ActOneSteps - 1].tick, Day, "Act I on the first day — no quest may stall on the honest start");
            Assert.GreaterOrEqual(s.Buildings[BuildingId.CommandCenter].Level, 8, "a diligent week reaches CC 8+");
            Assert.Less(pace.LongestStarvedStreakSec, 8 * Hour,
                "no awake stretch of a full working day with nothing affordable to build");
            Assert.Greater(pace.CampsRaided, 5, "rim camps are worth raiding");
        }

        static string UiK(long whole) => whole >= 10_000 ? $"{whole / 1000.0:0.#}k" : whole.ToString();

        // ---------- the commander ----------

        static void CheckIn(GameState s, Pace pace)
        {
            int day = Math.Min(7, s.Tick / Day);
            pace.ChecksByDay[day]++;

            while (QuestSystem.Current(s) != null && QuestSystem.Claim(s).Ok)
                pace.QuestsAt.Add((s.QuestStep - 1, s.Tick));
            EventSystem.Claim(s);
            Chores(s);
            if (SupplySystem.Collect(s) is { } crate) pace.SupplyByDay[day] += crate.Total / 1000;

            long before = s.Resources.Total;
            while (s.BuildQueue.Count < BuildingSystem.BuildSlots(s))
                if (!Build(s)) break;
            pace.BuildSpendByDay[day] += (before - s.Resources.Total) / 1000;
            // A dead zone: nothing under construction at all, and nothing affordable.
            if (s.BuildQueue.Count == 0)
            {
                pace.StarvedChecksByDay[day]++;
                pace.StarvedStreakSec += CheckInSec;
                pace.LongestStarvedStreakSec = Math.Max(pace.LongestStarvedStreakSec, pace.StarvedStreakSec);
            }
            else pace.StarvedStreakSec = 0;

            before = s.Resources.Total;
            Research(s);
            pace.ResearchSpendByDay[day] += (before - s.Resources.Total) / 1000;
            Survey(s);
            before = s.Resources.Total;
            Shipyard(s);
            pace.ShipSpendByDay[day] += (before - s.Resources.Total) / 1000;
            Scout(s);
            Raid(s, pace);
            Gather(s, pace);
        }

        static bool Up(GameState s, BuildingId id) =>
            BuildingSystem.CheckUpgrade(s, id).Ok && BuildingSystem.StartUpgrade(s, id).Ok;

        static bool Build(GameState s)
        {
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            // The quest's building first — it pays out.
            if (QuestSystem.Current(s) is { Goal: QuestGoal.BuildingLevel } q && Up(s, q.Building)) return true;
            if (ResourceSystem.GetEnergyBalance(s).Factor < 1f && Up(s, BuildingId.PowerPlant)) return true;
            foreach (var type in MineTypes.All)
            {
                if (BuildingSystem.MineCountOfType(s, type) >= BuildingSystem.AllowedMinesForType(s)) continue;
                if (!BuildingSystem.CheckBuildMine(s, type).Ok) continue;
                int plot = Enumerable.Range(0, 9).First(p => s.ExtraMines.All(m => m.Plot != p));
                if (BuildingSystem.BuildMine(s, type, plot, out _).Ok) return true;
            }
            // Production next: the lowest producer below the cap (extras included).
            var producers = new List<(int level, Func<bool> start)>
            {
                (s.Buildings[BuildingId.GoldMine].Level, () => Up(s, BuildingId.GoldMine)),
                (s.Buildings[BuildingId.QuartzExtractor].Level, () => Up(s, BuildingId.QuartzExtractor)),
                (s.Buildings[BuildingId.HeliumRefinery].Level + 1, () => Up(s, BuildingId.HeliumRefinery)),
            };
            foreach (var mine in s.ExtraMines)
            {
                int id = mine.Id;
                producers.Add((mine.Level, () => BuildingSystem.CheckUpgradeMine(s, id).Ok
                    && BuildingSystem.StartUpgradeMine(s, id).Ok));
            }
            foreach (var p in producers.OrderBy(p => p.level))
                if (p.level < cc && p.start()) return true;
            // Everything else a notch under the cap, then the cap itself.
            foreach (var id in new[] { BuildingId.Shipyard, BuildingId.ResearchLab, BuildingId.Warehouse,
                         BuildingId.RadarStation, BuildingId.PowerPlant })
                if (s.Buildings[id].Level < cc - 1 && Up(s, id)) return true;
            if (Up(s, BuildingId.CommandCenter)) return true;
            return Up(s, BuildingSystem.NextBestUpgrade(s));
        }

        static void Research(GameState s)
        {
            if (s.Buildings[BuildingId.ResearchLab].Level < 1) return;
            while (s.ResearchQueue.Count < ResearchSystem.ResearchSlots(s))
            {
                // The quest's tech, then the cheapest thing on the tree.
                if (QuestSystem.Current(s) is { Goal: QuestGoal.ResearchLevel } q
                    && ResearchSystem.CheckResearch(s, q.Tech).Ok)
                {
                    ResearchSystem.StartResearch(s, q.Tech);
                    continue;
                }
                long Cost(TechId id) => ResearchSystem.GetResearchCost(id, ResearchSystem.TechLevel(s, id) + 1).Total;
                var pick = Techs.All.Where(id => ResearchSystem.CheckResearch(s, id).Ok)
                    .OrderBy(Cost).FirstOrDefault();
                if (!ResearchSystem.CheckResearch(s, pick).Ok) return;
                // Construction comes first: research only spends while both build
                // queues are already running.
                if (s.BuildQueue.Count < BuildingSystem.BuildSlots(s)) return;
                ResearchSystem.StartResearch(s, pick);
            }
        }

        static void Shipyard(GameState s)
        {
            if (s.Buildings[BuildingId.Shipyard].Level < 1 || s.ShipQueue.Count > 0) return;
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            int haulers = Count(s, HullId.Hauler) + InFlight(s, HullId.Hauler);
            if (haulers < 2 + cc) { FleetSystem.QueueShips(s, HullId.Hauler, 1); return; }
            int fighters = Count(s, HullId.Fighter) + InFlight(s, HullId.Fighter);
            // Enough warships to take the rim's camps (their loot pays for them).
            if (fighters < 10 + 4 * cc) { FleetSystem.QueueShips(s, HullId.Fighter, 5); return; }
            // With both build queues busy, grow the Home Guard toward its full bonus
            // (the Path's ship goals ride on it).
            if (s.BuildQueue.Count < BuildingSystem.BuildSlots(s)) return;
            if (ResourceSystem.HomeGuardMight(s) < Balance.HomeGuardFullMight(cc))
                FleetSystem.QueueShips(s, HullId.Fighter, 10);
        }

        /// <summary>The Path's teaching steps, the way a player does them: one daily
        /// objective a day (dailies live in the Unity layer, so the claim is counted
        /// here), skill points spent, a speed-up used, a clan, a market trade.</summary>
        static void Chores(GameState s)
        {
            if (s.Stats.DailiesClaimed <= s.Tick / Day && s.Stats.BattlesWon > 0) s.Stats.DailiesClaimed++;
            foreach (var skill in CommanderSkills.All)
                while (CommanderSystem.Learn(s, skill.Id).Ok) { }
            var speed = s.Inventory.FirstOrDefault(e => Shop.ById.TryGetValue(e.ItemId, out var d) && d.Effect == ShopEffect.Speedup);
            if (speed != null && s.BuildQueue.Count > 0 && ShopSystem.ConsumeItem(s, speed.ItemId).Ok)
                s.BuildQueue[0].EndsAtTick = Math.Max(s.Tick, s.BuildQueue[0].EndsAtTick - 300);
            if (s.ClanId == 0 && s.Buildings[BuildingId.CommandCenter].Level >= 5) s.ClanId = 1; // joins (no rivals here)
            if (s.Stats.MarketTrades == 0 && s.Buildings[BuildingId.CommandCenter].Level >= 5)
                MarketSystem.Trade(s, ResourceId.Gold, ResourceId.Helium, 500);
        }

        /// <summary>Chart the Wilds while both build queues are busy.</summary>
        static void Survey(GameState s)
        {
            if (s.BuildQueue.Count < BuildingSystem.BuildSlots(s)) return;
            int next = WildsSystem.NextSurvey(s, 0);
            if (next >= 0) WildsSystem.StartSurvey(s, next);
        }

        static int Surveys(GameState s) => s.Wilds.Sectors.Values.Sum(x => x.Surveys);

        static void Scout(GameState s)
        {
            if (QuestSystem.Current(s) is not { Goal: QuestGoal.CampScouted }) return;
            if (s.Marches.Any(m => m.Mission == MarchMission.Spy) || Count(s, HullId.Probe) < 1) return;
            var camp = Nearby(s).FirstOrDefault(n => n.Kind == NodeKind.Camp && !Cleared(s, n));
            if (camp == null) return;
            MarchSystem.SendMarch(s, new Dictionary<HullId, int> { [HullId.Probe] = 1 }, camp.Tile,
                MarchMission.Spy, out _);
        }

        static void Raid(GameState s, Pace pace)
        {
            if (s.Marches.Any(m => m.Mission == MarchMission.Attack)) return;
            var fleet = new Dictionary<HullId, int>();
            foreach (var kv in s.Ships)
                if (kv.Key != HullId.Hauler && kv.Key != HullId.Probe && kv.Value > 0) fleet[kv.Key] = kv.Value;
            // The raiding squad; the rest stays docked as the Home Guard.
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            if (fleet.TryGetValue(HullId.Fighter, out var nf)) fleet[HullId.Fighter] = Math.Min(nf, 10 + 4 * cc);
            if (fleet.Count == 0) return;
            // A few Haulers fly along to carry the stockpile home (+50% hold on raids).
            int haulers = Count(s, HullId.Hauler);
            foreach (var camp in Nearby(s).Where(n => n.Kind == NodeKind.Camp && !Cleared(s, n)).Take(6))
            {
                var odds = BattleForecast.Predict(fleet, MarchSystem.CampGarrison(camp), ResearchSystem.CombatMods(s));
                if (odds.Winner != BattleWinner.Attacker) continue;
                long lossCost = odds.YourLossesByHull.Sum(kv => (long)Ships.Defs[kv.Key].Cost.Total * kv.Value);
                bool questFight = QuestSystem.Current(s) is { Goal: QuestGoal.BattlesWon };
                if (!questFight && MarchSystem.CampLoot(s, camp).Total / 1000 <= lossCost) continue;
                // Enough Haulers to carry the camp's stockpile home.
                long perHauler = (long)(MarchSystem.EffCargoCap(s, new Dictionary<HullId, int> { [HullId.Hauler] = 1 })
                    * (1 + Balance.RaidHaulerCargoBonus));
                haulers = (int)Math.Min(haulers, MarchSystem.CampLoot(s, camp).Total / perHauler + 1);
                if (haulers > 0) fleet[HullId.Hauler] = haulers;
                var sent = MarchSystem.SendMarch(s, fleet, camp.Tile, MarchMission.Attack, out int id);
                if (!sent.Ok && haulers > 0)
                {
                    // Short of helium for the Haulers too: the squad alone.
                    fleet.Remove(HullId.Hauler);
                    sent = MarchSystem.SendMarch(s, fleet, camp.Tile, MarchMission.Attack, out id);
                }
                if (sent.Ok)
                {
                    pace.CampsRaided++;
                    pace.MissionOf[id] = MarchMission.Attack;
                }
                else if (questFight && pace.QuestFightBlocks.Count < 6)
                    pace.QuestFightBlocks.Add($"{s.Tick / Hour}h:{sent.Reason} (He {s.Resources.Helium / 1000}, " +
                        $"needs {MarchSystem.PreviewMarch(s, fleet, camp.Tile).HeliumCost / 1000}, {TileXY.Distance(camp.Tile, s.HomeTile):0} tiles)");
                return;
            }
        }

        static void Gather(GameState s, Pace pace)
        {
            int idle = Count(s, HullId.Hauler);
            if (idle < 1) return;
            // The resource running lowest decides the field.
            var want = new[] { ResourceId.Gold, ResourceId.Quartz, ResourceId.Helium }
                .OrderBy(r => s.Resources.Get(r)).ToArray();
            var busy = new HashSet<string>(s.Marches.Select(m => m.Node.Key()));
            long perHauler = MarchSystem.EffCargoCap(s, new Dictionary<HullId, int> { [HullId.Hauler] = 1 });
            int rounds = 0;
            while (idle > 0 && rounds++ < 12)
            {
                bool sent = false;
                foreach (var res in want)
                {
                    var field = Nearby(s).FirstOrDefault(n => n.Resource == res && !busy.Contains(n.Tile.Key())
                        && Remaining(s, n) > 0);
                    if (field == null) continue;
                    int take = (int)Math.Min(idle, Math.Max(1, (Remaining(s, field) + perHauler - 1) / perHauler));
                    var ships = new Dictionary<HullId, int> { [HullId.Hauler] = take };
                    if (!MarchSystem.SendMarch(s, ships, field.Tile, MarchMission.Gather, out int id).Ok) return;
                    pace.GatherTrips++;
                    pace.MissionOf[id] = MarchMission.Gather;
                    busy.Add(field.Tile.Key());
                    idle -= take;
                    sent = true;
                    break;
                }
                if (!sent) return;
            }
        }

        // ---------- helpers ----------

        static int Count(GameState s, HullId hull) => s.Ships.TryGetValue(hull, out var n) ? n : 0;

        static long Remaining(GameState s, MapNode n) =>
            s.Map.NodeOverrides.TryGetValue(n.Id, out var o) ? o.Remaining ?? n.Amount : n.Amount;

        static int InFlight(GameState s, HullId hull) =>
            s.Marches.Sum(m => m.Ships.TryGetValue(hull, out var n) ? n : 0);

        static bool Cleared(GameState s, MapNode n) =>
            s.Map.NodeOverrides.TryGetValue(n.Id, out var o) && (o.Cleared || o.Retired);

        static List<MapNode>? s_nearby;
        static int s_nearbyTick = -1;

        /// <summary>Live nodes within reach of home, nearest first (refreshed hourly —
        /// fields deplete and respawn elsewhere).</summary>
        static List<MapNode> Nearby(GameState s)
        {
            if (s_nearby != null && s.Tick - s_nearbyTick < Hour) return s_nearby;
            s_nearbyTick = s.Tick;
            s_nearby = MapLookup.AllNodes(s)
                .Where(n => TileXY.Distance(n.Tile, s.HomeTile) <= 120 && n.Tier == 0)
                .Where(n => !(s.Map.NodeOverrides.TryGetValue(n.Id, out var o) && o.Retired))
                .OrderBy(n => TileXY.Distance(n.Tile, s.HomeTile))
                .ToList();
            return s_nearby;
        }
    }
}
