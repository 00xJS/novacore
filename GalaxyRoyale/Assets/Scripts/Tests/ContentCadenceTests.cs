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

        public sealed record First(int Tick, Tier Tier, string Kind, string What);

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
            if (NemesisSystem.Active(s)) d[$"nemesis:{s.Nemesis.BotId}"] = (Tier.Notable, "rival", $"Nemesis {s.Nemesis.Name}");
            var live = EventSystem.Current(s.Tick);
            if (!EventSystem.IsQuiet(live))
            {
                d[$"event:{live.Def.Kind}"] = (Tier.Major, "event", live.Def.Name);
                d[$"eventrun:{live.Instance}"] = (Tier.Notable, "event", $"{live.Def.Name} (again)");
            }
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

            var known = new HashSet<string>(Seen(s, galaxy).Keys);
            var firsts = new List<First>();
            var sessions = new List<(int tick, int notable)>(); // each awake check-in: firsts at Notable+
            var ccByDay = new int[days + 1];
            var lvlByDay = new int[days + 1];
            var clock = System.Diagnostics.Stopwatch.StartNew();

            while (s.Tick < days * Day)
            {
                bool awake = s.Tick % Day < 16 * Hour;
                if (awake)
                {
                    Curious(s, galaxy);
                    StandardPacingTests.CheckIn(s, pace);
                }
                engine.Advance(awake ? CheckInSec : Hour);
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
                              $"{today.Count(f => f.Tier == Tier.Minor),3}   fresh sessions {fresh}/{sess}   CC {ccByDay[d0]} L{lvlByDay[d0]}   " +
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
