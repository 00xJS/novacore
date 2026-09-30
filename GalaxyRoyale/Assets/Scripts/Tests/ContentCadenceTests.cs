// Content cadence (user request 2026-09-30: "make sure this game is packed with
// content"). The StandardPacingTests commander plays a full galaxy — 249 rivals,
// the Core, the dreadnought, clans, seasons — for CADENCE_DAYS (default 90) and
// every check-in logs the FIRSTS it meets: a building, a ship, an event, a
// story, an award. The report shows how often a player gets something new and
// where the game runs dry: the target is something new every session for the
// first two weeks and every few days after.
//
//   CADENCE_DAYS=90 CADENCE_OUT=/tmp/cadence.json dotnet test ci/Tests --filter ContentCadence
//
// Explicit: a 90-day run takes a minute or two, so CI skips it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim.Tests
{
    public class ContentCadenceTests
    {
        const int Hour = 3600, Day = 24 * Hour;
        const int CheckInSec = 20 * 60;

        /// <summary>How big a first is: a new system or place (Major), a new
        /// chapter of something known (Notable), or a step up (Minor).</summary>
        public enum Tier { Minor, Notable, Major }

        /// <summary>(A class: Unity's compiler has no IsExternalInit for records in this assembly.)</summary>
        public sealed class First
        {
            public readonly int Tick;
            public readonly Tier Tier;
            public readonly string Kind, What;
            public First(int tick, Tier tier, string kind, string what) { Tick = tick; Tier = tier; Kind = kind; What = what; }
        }

        /// <summary>Everything the player has met so far, as keys → (tier, kind, label).</summary>
        static Dictionary<string, (Tier tier, string kind, string what)> Seen(GameState s, BotGalaxy galaxy)
        {
            var d = new Dictionary<string, (Tier, string, string)>();
            foreach (var id in Buildings.All)
                if (s.Buildings[id].Level >= 1)
                    d[$"building:{id}"] = (Tier.Major, "building", Buildings.Defs[id].Name);
            int cc = s.Buildings[BuildingId.CommandCenter].Level;
            for (int l = 2; l <= cc; l++) d[$"cc:{l}"] = (Tier.Notable, "command", $"Command Center {l}");
            foreach (var h in Ships.All)
                if (FleetSystem.UnlockBlocker(s, h) == null)
                    d[$"hull:{h}"] = (Tier.Major, "ship", $"{Ships.Defs[h].Name} unlocked");
            foreach (var kv in s.Research)
                if (kv.Value >= 1) d[$"tech:{kv.Key}"] = (Tier.Minor, "research", Techs.Defs[kv.Key].Name);
            foreach (var kv in s.Commander.Skills)
                if (kv.Value >= 1) d[$"skill:{kv.Key}"] = (Tier.Minor, "skill", kv.Key);
            for (int l = 2; l <= s.Commander.Level; l++)
                d[$"level:{l}"] = (Tier.Minor, "commander", $"Commander level {l}");
            foreach (var a in s.Achievements)
                d[$"ach:{a}"] = (Tier.Notable, "award", Achievements.All.FirstOrDefault(x => x.Id == a)?.Name ?? a);
            for (int q = 0; q < s.QuestStep; q++)
                d[$"quest:{q}"] = (Tier.Notable, "quest", Quests.Chain[q].Title);
            foreach (var kv in s.Relics)
                if (kv.Value > 0) d[$"relic:{kv.Key}"] = (Tier.Notable, "relic", Relics.Def(kv.Key).Name);
            foreach (var log in s.ExpeditionLog)
                d[$"exp:{log.Kind}"] = (Tier.Notable, "expedition", Expeditions.Def(log.Kind).Name);
            for (int st = 1; st <= s.Terraform.Stage; st++)
                d[$"tf:{st}"] = (Tier.Notable, "terraform", $"Terraform stage {st}");
            foreach (int camp in s.CampFirstClears) d[$"camp:{camp}"] = (Tier.Minor, "camp", $"Lv {camp} camp beaten");
            foreach (var kv in s.Wilds.Sectors)
                if (kv.Value.Surveys > 0) d[$"wilds:{kv.Key}"] = (Tier.Minor, "wilds", $"Wilds sector {kv.Key}");
            for (int n = 1; n <= s.SeasonHistory.Count; n++)
                d[$"season:{n}"] = (Tier.Notable, "season", $"Season {n} finished");
            if (s.ClanId != 0) d["clan"] = (Tier.Major, "social", "Joined a clan");
            // The campaign (2026-09-30): each chapter opening is a new story; lords beaten, lords back.
            for (int ch = 0; ch < s.Campaign.Chapter + (s.Campaign.Open ? 1 : 0) && ch < Campaign.Chapters.Count; ch++)
                d[$"chapter:{ch}"] = (Tier.Major, "story", $"Chapter {ch + 1}: {Campaign.Chapters[ch].Title}");
            for (int ch = 0; ch < s.Campaign.Chapter; ch++)
                d[$"chapterdone:{ch}"] = (Tier.Notable, "story", $"Chapter {ch + 1} claimed");
            foreach (var kv in s.Campaign.LordWins)
                for (int w = 1; w <= kv.Value; w++)
                    d[$"lord:{kv.Key}:{w}"] = (w == 1 ? Tier.Major : Tier.Notable, "lord",
                        w == 1 ? $"Beat {PirateLords.Def(kv.Key).Name}" : $"Beat {PirateLords.Def(kv.Key).Name} again");
            if (LairSystem.Find(s, s.Campaign.RematchId) is { } back)
                d[$"return:{back.Id}"] = (Tier.Notable, "lord", $"{LairSystem.LordOf(back).Name} returns");
            if (s.Campaign.Open && Campaign.Chapters[s.Campaign.Chapter] is { } open)
                for (int i = 0; i < open.Objectives.Count; i++)
                    if (CampaignSystem.Done(s, i)) d[$"objective:{s.Campaign.Chapter}:{i}"] = (Tier.Notable, "story", open.Objectives[i].Text);
            if (NemesisSystem.Active(s)) d[$"nemesis:{s.Nemesis.BotId}"] = (Tier.Notable, "rival", $"Nemesis {s.Nemesis.Name}");
            var live = EventSystem.Current(s.Tick);
            if (!EventSystem.IsQuiet(live))
            {
                d[$"event:{live.Def.Kind}"] = (Tier.Major, "event", live.Def.Name);
                d[$"eventrun:{live.Instance}"] = (Tier.Notable, "event", $"{live.Def.Name} (again)");
            }
            if (TwistSystem.KindAt(s.Tick) is var tk && tk != TwistKind.None)
                d[$"twist:{tk}"] = (Tier.Major, "twist", $"Twist: {Twists.Def(tk).Name}");
            if (galaxy.Boss.Visit > 0)
            {
                var v = BossSystem.VariantFor(galaxy.Boss.Visit);
                d[$"boss:{v}"] = (Tier.Major, "boss", BossSystem.Name(v));
                d[$"bossvisit:{galaxy.Boss.Visit}"] = (Tier.Notable, "boss", $"{BossSystem.Name(v)} visit");
            }
            // Counters whose first tick marks a system tried for the first time.
            void Stat(long n, string key, string label) { if (n > 0) d[$"did:{key}"] = (Tier.Major, "did", label); }
            Stat(s.Stats.RaidsWon, "raid", "First raid on a rival won");
            Stat(s.Stats.DefensesWon, "defense", "First defense held");
            Stat(s.Stats.CoresSeized, "core", "Seized the Galactic Core");
            Stat(s.Stats.BossStrikes, "boss", "First dreadnought strike");
            Stat(s.Stats.MarketTrades, "market", "First market trade");
            Stat(s.Stats.CometHauled, "comet", "Hauled from a comet");
            Stat(s.Stats.CaravansDone, "caravan", "First caravan");
            Stat(s.Stats.BountiesClaimed, "bounty", "First bounty");
            Stat(s.Stats.TournamentsWon, "tournament", "Won a Core Tournament");
            Stat(s.Stats.MissileKills, "silo", "First missile salvo");
            Stat(s.Stats.ContractsDone, "contract", "First trade contract");
            Stat(s.Stats.NemesesDefeated, "nemesis", "Broke a nemesis");
            return d;
        }

        /// <summary>What a real player does on top of the pacing script: builds each new
        /// building the moment it opens, tries each new hull, strikes the dreadnought,
        /// fires the silo, takes trade contracts and starts a terraform path. Raiding
        /// rivals runs in the Unity layer (RaidService), so it isn't measured here.</summary>
        static void Curious(GameState s, BotGalaxy galaxy)
        {
            foreach (var id in Buildings.All)
                if (s.Buildings[id].Level == 0 && s.BuildQueue.Count < BuildingSystem.BuildSlots(s)
                    && s.BuildQueue.All(o => o.Building != id)
                    && BuildingSystem.CheckUpgrade(s, id).Ok)
                    BuildingSystem.StartUpgrade(s, id);
            if (s.ShipQueue.Count == 0)
                foreach (var h in Ships.All)
                    if (FleetSystem.UnlockBlocker(s, h) == null && Count(s, h) == 0 && FleetSystem.QueueShips(s, h, 3).Ok) break;
            // Claim the Wilds' finds: claimed sectors fog over again and can be surveyed anew.
            foreach (var index in s.Wilds.Sectors.Keys.ToList()) WildsSystem.Claim(s, index);
            if (TerraformSystem.Level(s) >= 1 && s.Terraform.Path == TerraformPath.None)
                TerraformSystem.Start(s, TerraformPath.Metallic);
            else if (s.Terraform.Path != TerraformPath.None) TerraformSystem.Start(s, s.Terraform.Path);
            if (galaxy.Boss.Active && !s.Marches.Any(m => m.Mission == MarchMission.Boss) && Count(s, HullId.Fighter) >= 20)
                BossSystem.SendStrike(s, galaxy, new Dictionary<HullId, int> { [HullId.Fighter] = 20 }, out _);
            foreach (var attack in SiloSystem.Targets(s, galaxy))
                if (SiloSystem.CanFire(s, galaxy, attack.Id).Ok) { SiloSystem.Fire(s, galaxy, attack.Id, null, out _); break; }
            if (ConsulateSystem.Level(s) >= 1 && !s.Marches.Any(m => m.Mission == MarchMission.Trade))
                foreach (var c in ConsulateSystem.Board(s, galaxy))
                    if (!ConsulateSystem.Taken(s, c.Code) && ConsulateSystem.CanAccept(s, c).Ok) { ConsulateSystem.Accept(s, c, out _); break; }
        }

        static int Count(GameState s, HullId h) => s.Ships.TryGetValue(h, out var n) ? n : 0;

        // ---------- the veteran (user 2026-09-30: scripted players use every feature) ----------

        sealed class RaidOut
        {
            public int BotId;
            public Dictionary<HullId, int> Sent = new();
            public TileXY Tile;
        }

        static readonly Dictionary<int, RaidOut> s_raids = new();
        static int s_nextRaidTick, s_nextCoreTick;

        static Dictionary<HullId, int> WarFleet(GameState s) =>
            s.Ships.Where(kv => kv.Value > 0 && ResourceSystem.IsWarship(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        /// <summary>What a seasoned player does on top of the week-one script: spends
        /// the surplus on a real fleet, joins a clan that asks, raids rivals it can
        /// beat (the bounty first), takes a swing at the Core, hauls from the comet and
        /// escorts the caravan, and lets the commander lead the big fights.</summary>
        static void Veteran(GameState s, BotGalaxy galaxy)
        {
            if (s.ClanId == 0 && s.ClanInviteId != 0) ClanSystem.Join(s, galaxy, s.ClanInviteId);
            SpendSurplus(s);
            RaidRival(s, galaxy);
            AssaultCore(s, galaxy);
            MapEvents(s);
        }

        static void SpendSurplus(GameState s)
        {
            if (s.ShipQueue.Count >= Balance.ShipQueueSlots || s.Buildings[BuildingId.CommandCenter].Level < 8) return;
            // Construction first: only a surplus left over with both build queues busy.
            if (s.BuildQueue.Count < BuildingSystem.BuildSlots(s)) return;
            long hourly = ResourceSystem.GetRates(s).Total;
            if (s.Resources.Total < hourly * 6) return;
            // The three strongest hulls the shipyard can build, a tenth of what's affordable each.
            var best = Ships.All.Where(h => ResourceSystem.IsWarship(h) && h != HullId.Aegis && FleetSystem.UnlockBlocker(s, h) == null)
                .Reverse().Take(3).ToList();
            foreach (var h in best)
            {
                if (s.ShipQueue.Count >= Balance.ShipQueueSlots) return;
                int n = FleetSystem.MaxBuildable(s, h) / 10;
                if (n >= 1) FleetSystem.QueueShips(s, h, n);
            }
        }

        static void RaidRival(GameState s, BotGalaxy galaxy)
        {
            if (s_raids.Count > 0 || s.Tick < s_nextRaidTick) return;
            s_nextRaidTick = s.Tick + 24 * Hour;
            var fleet = WarFleet(s);
            if (fleet.Count == 0) return;
            var mods = ResearchSystem.CombatMods(s);
            BotEmpire? pick = null;
            Dictionary<HullId, int>? squad = null;
            long bestLoot = 0;
            foreach (var bot in galaxy.Bots)
            {
                if (bot.CachedMight < BotSystem.PlayerShieldMight || ClanSystem.SameClanAsPlayer(s, bot)) continue;
                var home = bot.State.HomeTile;
                if (TileXY.Distance(home, s.HomeTile) > 600) continue;
                var snap = BotSystem.SnapshotOf(bot);
                // Rivals' wallets usually sit inside their Warehouse's shield, so a raid is
                // for the fight: the bounty first, then the nemesis, then the weakest rival.
                long score = snap.LootableMilli.Total / 1000 + (BountySystem.IsMarked(s, bot.Id) ? 1L << 40 : 0)
                    + (NemesisSystem.Is(s, bot.Id) ? 1L << 39 : 0) + (1L << 30) - Math.Min(1L << 30, bot.CachedMight);
                if (score <= bestLoot) continue;
                if (StandardPacingTests.Squad(s, fleet, snap.Ships, home, ResearchSystem.CombatMods(bot.State)) is not { } sq) continue;
                pick = bot;
                bestLoot = score;
                squad = sq;
            }
            if (pick == null || squad == null) return;
            fleet = squad;
            var tile = pick.State.HomeTile;
            if (!MarchSystem.SendRaidMarch(s, fleet, tile, out int id).Ok) return;
            BotSystem.BreakShieldForAggression(s);
            AcademySystem.Lead(s, id);
            s_raids[id] = new RaidOut { BotId = pick.Id, Sent = fleet, Tile = tile };
        }

        /// <summary>After each advance: a raid that reached its target fights (as RaidArrivals does in the app).</summary>
        static void SettleRaids(GameState s, BotGalaxy galaxy)
        {
            foreach (var id in s_raids.Keys.ToList())
            {
                var march = s.Marches.Find(m => m.Id == id);
                if (march == null) { s_raids.Remove(id); continue; }
                if (march.Phase == MarchPhase.Outbound) continue;
                var raid = s_raids[id];
                s_raids.Remove(id);
                if (galaxy.Find(raid.BotId) is { } bot) StrikeSystem.ResolveRaid(s, galaxy, bot, id, raid.Sent, raid.Tile);
            }
        }

        static void AssaultCore(GameState s, BotGalaxy galaxy)
        {
            if (s.Tick < s_nextCoreTick || CoreSystem.PlayerHolds(galaxy) || !CoreSystem.CanSend(s, galaxy).Ok) return;
            s_nextCoreTick = s.Tick + 24 * Hour;
            if (s.Marches.Any(m => m.Mission == MarchMission.Core)) return;
            var fleet = WarFleet(s);
            if (fleet.Count == 0) return;
            var d = CoreSystem.DefenceOf(s, galaxy);
            var all = new Dictionary<HullId, int>();
            foreach (var line in d.Lines)
                foreach (var kv in line) all[kv.Key] = (all.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            if (StandardPacingTests.Squad(s, fleet, all, CoreSystem.CoreTile, d.Mods) is not { } squad) return;
            if (CoreSystem.SendToCore(s, galaxy, squad, joint: true, out int id, out _).Ok) AcademySystem.Lead(s, id);
        }

        static void MapEvents(GameState s)
        {
            if (EventSites.Focus(s) is not { node: { } node }) return;
            if (s.Marches.Any(m => m.Node.Equals(node.Tile))) return;
            if (node.Kind == NodeKind.Comet && Count(s, HullId.Hauler) > 0)
                MarchSystem.SendMarch(s, new Dictionary<HullId, int> { [HullId.Hauler] = Math.Max(1, Count(s, HullId.Hauler) / 2) },
                    node.Tile, MarchMission.Gather, out _);
            else if (node.Kind == NodeKind.Caravan && Count(s, HullId.Fighter) >= 20)
                MarchSystem.SendMarch(s, new Dictionary<HullId, int> { [HullId.Fighter] = Count(s, HullId.Fighter) / 3 },
                    node.Tile, MarchMission.Gather, out _); // escort it
        }

        /// <summary>The campaign: claim a finished chapter, and storm a lord's lair with the
        /// whole docked war fleet when the forecast says it wins.</summary>
        static void Story(GameState s)
        {
            CampaignSystem.Claim(s);
            // An objective that names a building's stage: push that building.
            if (CampaignSystem.Current(s) is { } ch && s.Campaign.Open)
                for (int i = 0; i < ch.Objectives.Count; i++)
                    if (ch.Objectives[i].Goal == CampaignGoal.TerraformStage && !CampaignSystem.Done(s, i)
                        && s.BuildQueue.All(o => o.Building != BuildingId.Terraformer))
                        BuildingSystem.StartUpgrade(s, BuildingId.Terraformer);
            if (s.Marches.Any(m => m.Mission == MarchMission.Attack)) return;
            foreach (var id in new[] { s.Campaign.LairId, s.Campaign.RematchId })
            {
                if (LairSystem.Find(s, id) is not { } lair) continue;
                var fleet = WarFleet(s);
                if (fleet.Count == 0) return;
                if (StandardPacingTests.Squad(s, fleet, MarchSystem.CampGarrison(lair), lair.Tile) is not { } squad) continue;
                if (MarchSystem.SendMarch(s, squad, lair.Tile, MarchMission.Attack, out int marchId).Ok) { AcademySystem.Lead(s, marchId); return; }
            }
        }

        [TearDown]
        public void RestoreScript() => StandardPacingTests.FakeClan = true;

        [Test, Explicit("a 90-day full-galaxy run: run it by name")]
        public void ContentCadence_ScriptedCommander_FullGalaxy()
        {
            int days = int.TryParse(Environment.GetEnvironmentVariable("CADENCE_DAYS"), out var dd) ? dd : 90;
            var s = GameState.CreateNewGame(Spawn.GalaxySeed, testMode: false);
            s.HomeTile = Spawn.SpawnTileFor("player-pacing", Spawn.GalaxySeed);
            var galaxy = BotSystem.CreateGalaxy(Spawn.GalaxySeed, s.HomeTile);
            var events = new SimEventBus();
            var engine = new TickEngine(s, events);
            var pace = new StandardPacingTests.Pace();
            StandardPacingTests.FakeClan = false; // a real clan, through ClanSystem's invites
            s_raids.Clear();
            s_nextRaidTick = s_nextCoreTick = 0;

            var known = new HashSet<string>(Seen(s, galaxy).Keys);
            var firsts = new List<First>();
            var sessions = new List<(int tick, int notable)>(); // each awake check-in: firsts at Notable+
            var ccByDay = new int[days + 1];
            var lvlByDay = new int[days + 1];
            var campsByDay = new int[days + 1];
            var battlesByDay = new int[days + 1];
            var mightByDay = new long[days + 1];
            var raidsByDay = new int[days + 1];
            var clock = System.Diagnostics.Stopwatch.StartNew();

            while (s.Tick < days * Day)
            {
                bool awake = s.Tick % Day < 16 * Hour;
                if (awake)
                {
                    Curious(s, galaxy);
                    Veteran(s, galaxy);
                    Story(s);
                    StandardPacingTests.CheckIn(s, pace);
                }
                engine.Advance(awake ? CheckInSec : Hour);
                SettleRaids(s, galaxy);
                BotSystem.Advance(s, galaxy, events);
                ProgressionSystem.Advance(s, galaxy, events);

                int found = 0;
                foreach (var (key, (tier, kind, what)) in Seen(s, galaxy))
                {
                    if (!known.Add(key)) continue;
                    firsts.Add(new First(s.Tick, tier, kind, what));
                    if (tier >= Tier.Notable) found++;
                }
                if (awake) sessions.Add((s.Tick, found));
                int day = Math.Min(days, s.Tick / Day);
                ccByDay[day] = s.Buildings[BuildingId.CommandCenter].Level;
                lvlByDay[day] = s.Commander.Level;
                campsByDay[day] = s.Stats.CampsCleared;
                battlesByDay[day] = s.Stats.BattlesWon;
                raidsByDay[day] = s.Stats.RaidsWon;
                if (s.Tick % Day == 0) mightByDay[day] = PowerSystem.ComputePower(s);
            }

            // ---- the report ----
            string T(int tick) => $"d{tick / Day} {tick % Day / Hour:00}h";
            var log = TestContext.Out;
            log.WriteLine($"CADENCE {days} days in {clock.Elapsed.TotalSeconds:0}s · CC {ccByDay[days]} · commander L{lvlByDay[days]} · " +
                          $"{firsts.Count} firsts ({firsts.Count(f => f.Tier == Tier.Major)} major, {firsts.Count(f => f.Tier == Tier.Notable)} notable)");
            log.WriteLine("CADENCE per day  major/notable/minor:");
            for (int d0 = 0; d0 < days; d0++)
            {
                var today = firsts.Where(f => f.Tick / Day == d0).ToList();
                int sess = sessions.Count(x => x.tick / Day == d0), fresh = sessions.Count(x => x.tick / Day == d0 && x.notable > 0);
                log.WriteLine($"  d{d0,-3} {today.Count(f => f.Tier == Tier.Major),2} / {today.Count(f => f.Tier == Tier.Notable),2} / " +
                              $"{today.Count(f => f.Tier == Tier.Minor),3}   fresh sessions {fresh}/{sess}   CC {ccByDay[d0]} L{lvlByDay[d0]} camps {campsByDay[d0]} wins {battlesByDay[d0]} raids {raidsByDay[d0]} might {mightByDay[d0] / 1000}k   " +
                              string.Join(", ", today.Where(f => f.Tier == Tier.Major).Select(f => f.What)));
            }
            // Dry stretches: the longest runs of awake check-ins with nothing Notable or bigger.
            var dry = new List<(int from, int to, int checks)>();
            int runStart = -1, runLen = 0;
            foreach (var (tick, n) in sessions)
            {
                if (n == 0) { if (runLen++ == 0) runStart = tick; }
                else { if (runLen > 0) dry.Add((runStart, tick, runLen)); runLen = 0; }
            }
            if (runLen > 0) dry.Add((runStart, days * Day, runLen));
            log.WriteLine("CADENCE longest dry stretches (awake check-ins with nothing new):");
            foreach (var (from, to, checks) in dry.OrderByDescending(x => x.checks).Take(8))
                log.WriteLine($"  {T(from)} → {T(to)}: {checks} check-ins ({checks * CheckInSec / 3600.0:0.#} awake hours)");

            // Content that exists but never showed up.
            var all = new List<string>();
            foreach (var id in Buildings.All) all.Add($"building:{id}");
            foreach (var h in Ships.All) all.Add($"hull:{h}");
            foreach (var a in Achievements.All) all.Add($"ach:{a.Id}");
            foreach (var r in Relics.All) all.Add($"relic:{r.Kind}");
            foreach (var e in Expeditions.All) all.Add($"exp:{e.Kind}");
            foreach (var e in GalaxyEvents.Rotation) all.Add($"event:{e.Kind}");
            foreach (BossVariant v in Enum.GetValues(typeof(BossVariant))) all.Add($"boss:{v}");
            var missing = all.Where(k => !known.Contains(k)).ToList();
            log.WriteLine($"CADENCE never reached ({missing.Count}): {string.Join(", ", missing)}");

            if (Environment.GetEnvironmentVariable("CADENCE_OUT") is { Length: > 0 } outPath)
            {
                var sb = new StringBuilder();
                sb.Append("{\"days\":").Append(days).Append(",\"firsts\":[");
                sb.Append(string.Join(",", firsts.Select(f =>
                    $"{{\"t\":{f.Tick},\"tier\":\"{f.Tier}\",\"kind\":\"{f.Kind}\",\"what\":\"{f.What.Replace("\"", "'")}\"}}")));
                sb.Append("],\"sessions\":[").Append(string.Join(",", sessions.Select(x => $"[{x.tick},{x.notable}]")));
                sb.Append("],\"cc\":[").Append(string.Join(",", ccByDay)).Append("],\"level\":[").Append(string.Join(",", lvlByDay));
                sb.Append("],\"missing\":[").Append(string.Join(",", missing.Select(m => $"\"{m}\""))).Append("]}");
                File.WriteAllText(outPath, sb.ToString());
            }
            Assert.Greater(firsts.Count, 0);
        }
    }
}
