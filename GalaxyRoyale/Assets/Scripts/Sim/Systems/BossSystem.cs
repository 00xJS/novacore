// The Pirate Dreadnought (world boss, build-all plan 2026-09-28). Every few
// days a dreadnought drops out of hyperspace somewhere in the middle rings and
// stays for a day. Its hull is one pool the whole galaxy shares: every strike
// — yours and the simulated commanders' — wears it down, and the clans race
// to deal the most damage.
//   - A strike is a few rounds of everything firing at the one hull while its
//     guns fire back (Combat/BossCombat), then the survivors fly home with
//     salvage in proportion to the damage they did.
//   - When it breaks apart (or leaves), everyone who struck it is paid Dark
//     Matter by their share of the damage; the final blow and the top clan earn
//     a bonus. The last visit's outcome lands in your mailbox.
// Tick runs from BotSystem.Advance as a time-ordered loop (arrival, strikes,
// rolls for the commanders' strikes, departure), so offline catch-up replays
// a visit in the order it happened.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;

namespace GalaxyRoyale.Sim.Systems
{
    public static class BossSystem
    {
        /// <summary>A new galaxy (or an older save) meets its first dreadnought this long after the system first runs.</summary>
        public const int FirstVisitDelaySec = 4 * 3600;
        public const int VisitSec = 24 * 3600;
        /// <summary>Quiet time between one leaving and the next arriving.</summary>
        public const int GapSec = 48 * 3600;
        /// <summary>Hull = this × the average full-fleet firepower (damage per round) of the
        /// ten best-armed commanders (Pace_DreadnoughtAgainstTheGalaxy).</summary>
        public const double HullPerTopFirepower = 80;
        public const long MinHull = 150_000;
        /// <summary>Its guns fire hull ÷ this per round.</summary>
        public const int CannonDivisor = 300;
        /// <summary>Where it drops in: this far from the Galactic Core (tiles).</summary>
        public const double MinCoreDist = 450, MaxCoreDist = 900;

        public const int RollIntervalSec = 20 * 60;
        public const double RollChance = 0.75;
        public const int StrikesPerRoll = 2;
        /// <summary>Share of their warships a commander sends at it.</summary>
        public const double BotCommit = 0.5;
        /// <summary>Commanders only fly at it from this close (tiles).</summary>
        public const double BotRange = 1100;

        /// <summary>Salvage your survivors carry off: whole resources per point of damage (cargo-capped).</summary>
        public const double SalvagePerDamage = 0.25;
        /// <summary>Dark Matter shared out by damage when it breaks apart (or when it escapes).</summary>
        public const int PoolDMKilled = 1200, PoolDMEscaped = 300;
        public const int MinRewardDM = 20, FinalBlowDM = 100, TopClanDM = 250;

        // ---------- queries ----------

        public static bool Active(BotGalaxy galaxy) => galaxy.Boss.Active;

        public static double HullShare(BossState boss) => boss.MaxHp > 0 ? boss.Hp / (double)boss.MaxHp : 0;

        /// <summary>Firepower per round (Σ ships × attack) — what the hull is measured against.</summary>
        public static long Firepower(Dictionary<HullId, int> fleet)
        {
            long f = 0;
            foreach (var kv in fleet) if (kv.Value > 0) f += (long)kv.Value * Ships.Defs[kv.Key].Atk;
            return f;
        }

        /// <summary>Full hull for a visit now: tougher as the galaxy's fleets grow.</summary>
        public static long TargetHull(BotGalaxy galaxy)
        {
            var fire = new List<long>();
            foreach (var bot in galaxy.Bots) fire.Add(Firepower(BotSystem.CombatFleetOf(bot.State, 1.0)));
            if (fire.Count == 0) return MinHull;
            fire.Sort((a, b) => b.CompareTo(a));
            int n = Math.Min(10, fire.Count);
            long sum = 0;
            for (int i = 0; i < n; i++) sum += fire[i];
            return Math.Max(MinHull, (long)(sum / (double)n * HullPerTopFirepower));
        }

        /// <summary>What a strike would do right now — exact (the fight is deterministic).</summary>
        public static BossCombat.Result Forecast(GameState player, BotGalaxy galaxy, Dictionary<HullId, int> fleet)
        {
            var boss = galaxy.Boss;
            return BossCombat.Strike(fleet, ResearchSystem.CombatMods(player), boss.Hp, boss.MaxHp, boss.Cannon);
        }

        public sealed class Standing
        {
            public int ClanId;
            public string Name = "";
            public long Damage;
            public bool Yours;
        }

        /// <summary>The clan damage race: every clan's total (commanders without a clan
        /// count together), highest first.</summary>
        public static List<Standing> ClanRace(GameState player, BotGalaxy galaxy)
        {
            var byClan = new Dictionary<int, Standing>();
            foreach (var kv in galaxy.Boss.Damage)
            {
                int clanId = kv.Key == 0 ? player.ClanId : galaxy.Find(kv.Key)?.ClanId ?? 0;
                if (!byClan.TryGetValue(clanId, out var s))
                {
                    var clan = galaxy.FindClan(clanId);
                    s = byClan[clanId] = new Standing
                    {
                        ClanId = clanId,
                        Name = clan != null ? ClanSystem.Label(clan) : "Unaffiliated commanders",
                        Yours = clanId != 0 && clanId == player.ClanId,
                    };
                }
                s.Damage += kv.Value;
            }
            var list = new List<Standing>(byClan.Values);
            list.Sort((a, b) => a.Damage != b.Damage ? b.Damage.CompareTo(a.Damage) : a.ClanId.CompareTo(b.ClanId));
            return list;
        }

        /// <summary>Your damage this visit and where it ranks among everyone who struck (0 = not yet).</summary>
        public static (long damage, int rank, int of) YourStanding(BotGalaxy galaxy)
        {
            var dmg = galaxy.Boss.Damage;
            if (!dmg.TryGetValue(0, out long mine) || mine <= 0) return (0, 0, dmg.Count);
            int rank = 1;
            foreach (var kv in dmg) if (kv.Key != 0 && kv.Value > mine) rank++;
            return (mine, rank, dmg.Count);
        }

        static long TotalDamage(BossState boss)
        {
            long t = 0;
            foreach (var v in boss.Damage.Values) t += v;
            return t;
        }

        // ---------- your strikes ----------

        public static SimResult CanStrike(GameState player, BotGalaxy galaxy, Dictionary<HullId, int> ships)
        {
            var boss = galaxy.Boss;
            if (!boss.Active) return SimResult.Fail("No dreadnought in the galaxy right now");
            if (MarchSystem.FleetCount(ships) < 1) return SimResult.Fail("No ships selected");
            int arrive = player.Tick + MarchSystem.FlightSeconds(player, ships, boss.Tile);
            if (arrive >= boss.LeavesTick) return SimResult.Fail("It will be gone before your fleet gets there");
            return SimResult.Success;
        }

        public static SimResult SendStrike(GameState player, BotGalaxy galaxy, Dictionary<HullId, int> ships, out int marchId)
        {
            marchId = 0;
            var can = CanStrike(player, galaxy, ships);
            if (!can.Ok) return can;
            var boss = galaxy.Boss;
            int arrive = player.Tick + MarchSystem.FlightSeconds(player, ships, boss.Tile);
            var sent = MarchSystem.SendFlight(player, ships, boss.Tile, arrive, MarchMission.Boss, out var march);
            if (!sent.Ok || march == null) return sent;
            march.TargetFleetId = boss.Visit;
            marchId = march.Id;
            return SimResult.Success;
        }

        // ---------- the clock ----------

        public static void Tick(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            var boss = galaxy.Boss;
            int now = player.Tick;
            if (!boss.Active && boss.NextVisitTick == 0) boss.NextVisitTick = now + FirstVisitDelaySec;

            for (int guard = 0; guard < 512; guard++)
            {
                int arriveAt = boss.Active ? int.MaxValue : boss.NextVisitTick;
                int leaveAt = boss.Active ? boss.LeavesTick : int.MaxValue;
                int rollAt = boss.Active ? boss.NextRollTick : int.MaxValue;
                var (strikeAt, mine, theirs) = NextStrike(player, galaxy);
                int t = Math.Min(Math.Min(arriveAt, leaveAt), Math.Min(rollAt, strikeAt));
                if (t > now) break;

                // A strike landing the second it leaves still counts.
                if (strikeAt == t)
                {
                    if (mine != null) ResolveYourStrike(player, galaxy, events, mine, t);
                    else if (theirs != null) ResolveCommanderStrike(player, galaxy, events, theirs, t);
                }
                else if (leaveAt == t) EndVisit(player, galaxy, events, t, killerId: null);
                else if (arriveAt == t) BeginVisit(player, galaxy, events, t);
                else RollCommanderStrikes(player, galaxy, t);
            }
        }

        /// <summary>The earliest strike waiting to be settled: a Boss march of yours that
        /// has landed (Hold stamps the landing tick), or a commander's strike arriving.</summary>
        static (int at, March? mine, BotMarch? theirs) NextStrike(GameState player, BotGalaxy galaxy)
        {
            int best = int.MaxValue;
            March? mine = null;
            BotMarch? theirs = null;
            foreach (var m in player.Marches)
                if (m.Mission == MarchMission.Boss && m.Phase == MarchPhase.Gathering && m.DepartedAtTick < best)
                {
                    best = m.DepartedAtTick;
                    mine = m;
                }
            foreach (var m in galaxy.Marches)
                if (m.Kind == BotMarchKind.BossStrike && !m.Resolved && m.ArrivesAtTick < best)
                {
                    best = m.ArrivesAtTick;
                    mine = null;
                    theirs = m;
                }
            return (best, mine, theirs);
        }

        static void BeginVisit(GameState player, BotGalaxy galaxy, SimEventBus events, int at)
        {
            var boss = galaxy.Boss;
            boss.Visit++;
            boss.Active = true;
            boss.Tile = DropPoint(player.Seed, boss.Visit);
            boss.MaxHp = boss.Hp = TargetHull(galaxy);
            boss.Cannon = (int)Math.Max(1, boss.MaxHp / CannonDivisor);
            boss.ArrivedTick = at;
            boss.LeavesTick = at + VisitSec;
            boss.NextRollTick = at + 10 * 60;
            boss.Damage.Clear();
            galaxy.AddBulletin(at, $"A Pirate Dreadnought dropped out of hyperspace at {boss.Tile.X}, {boss.Tile.Y}");
            events.Emit(new BossAppeared(boss.Tile, boss.LeavesTick));
        }

        /// <summary>Somewhere in the middle rings, clear of the core — the same spot for
        /// a visit however the galaxy got there.</summary>
        public static TileXY DropPointFor(int seed, int visit) => DropPoint(seed, visit);

        static TileXY DropPoint(int seed, int visit)
        {
            var rng = Rng.Mulberry32(unchecked((uint)seed * 0x85EBCA6Bu ^ (uint)visit * 0xC2B2AE35u ^ 0xB055u));
            var core = CoreSystem.CoreTile;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                double angle = rng() * Math.PI * 2;
                double r = MinCoreDist + rng() * (MaxCoreDist - MinCoreDist);
                var tile = new TileXY((int)Math.Round(core.X + Math.Cos(angle) * r), (int)Math.Round(core.Y + Math.Sin(angle) * r));
                if (tile.X < 20 || tile.Y < 20 || tile.X > Balance.SectorSize - 20 || tile.Y > Balance.SectorSize - 20) continue;
                return tile;
            }
            return new TileXY(core.X + (int)MinCoreDist, core.Y);
        }

        static void ResolveYourStrike(GameState player, BotGalaxy galaxy, SimEventBus events, March march, int at)
        {
            var boss = galaxy.Boss;
            if (!boss.Active || march.TargetFleetId != boss.Visit || !march.Node.Equals(boss.Tile))
            {
                // It's gone (broken apart or jumped away) — nothing there to fight.
                MarchSystem.ReturnHome(player, march, at);
                BotSystem.InsertMail(player, new BossReport
                {
                    Id = player.NextReportId++,
                    AtTick = at,
                    Target = march.Node,
                    Subject = "Dreadnought strike — it was already gone",
                    Kind = BossReportKind.Missed,
                    Visit = march.TargetFleetId,
                    Fleet = new Dictionary<HullId, int>(march.Ships),
                    Survivors = new Dictionary<HullId, int>(march.Ships),
                });
                return;
            }

            var fleet = new Dictionary<HullId, int>(march.Ships);
            long before = boss.Hp;
            var result = BossCombat.Strike(fleet, ResearchSystem.CombatMods(player), boss.Hp, boss.MaxHp, boss.Cannon);
            boss.Hp = Math.Max(0, boss.Hp - result.Damage);
            boss.Damage[0] = (boss.Damage.TryGetValue(0, out var d) ? d : 0) + result.Damage;
            var salvage = Salvage(result.Damage, result.Survivors);

            player.Stats.BossStrikes++;
            player.Stats.BossDamage += result.Damage;
            march.Ships = result.Survivors;
            if (MarchSystem.FleetCount(march.Ships) == 0) player.Marches.Remove(march);
            else
            {
                march.Cargo.Add(salvage);
                MarchSystem.ReturnHome(player, march, at);
            }

            var report = new BossReport
            {
                Id = player.NextReportId++,
                AtTick = at,
                Target = boss.Tile,
                Subject = result.Killed ? "Dreadnought destroyed — you landed the final blow"
                    : $"Dreadnought strike — {FmtDamage(result.Damage)} damage",
                Kind = BossReportKind.Strike,
                Visit = boss.Visit,
                Damage = result.Damage,
                HpBefore = before,
                HpAfter = boss.Hp,
                MaxHp = boss.MaxHp,
                Rounds = result.RoundsFought,
                Fleet = fleet,
                Survivors = new Dictionary<HullId, int>(result.Survivors),
                Salvage = MarchSystem.FleetCount(result.Survivors) > 0 ? salvage : new ResourceBag(),
                FinalBlow = result.Killed,
            };
            BotSystem.InsertMail(player, report);
            events.Emit(new BossStrikeLanded(report));
            if (result.Killed)
            {
                player.Stats.BossFinalBlows++;
                player.Premium.DarkMatter += FinalBlowDM;
                EndVisit(player, galaxy, events, at, killerId: 0);
            }
        }

        /// <summary>Salvage in proportion to the damage, as much as the survivors can carry (milli).</summary>
        static ResourceBag Salvage(long damage, Dictionary<HullId, int> survivors)
        {
            long want = (long)(damage * SalvagePerDamage * 1000);
            long room = MarchSystem.FleetCargoCap(survivors);
            long total = Math.Min(want, room);
            if (total <= 0) return new ResourceBag();
            return new ResourceBag(total * 40 / 100, total * 35 / 100, total - total * 40 / 100 - total * 35 / 100);
        }

        // ---------- the simulated commanders ----------

        static bool Busy(BotGalaxy galaxy, int botId)
        {
            foreach (var m in galaxy.Marches) if (m.BotId == botId) return true;
            foreach (var a in galaxy.Inbound) if (a.BotId == botId) return true;
            return false;
        }

        /// <summary>Every twenty minutes a couple of commanders (the stronger, the likelier)
        /// may send half their warships at it, if they can get there before it leaves.</summary>
        static void RollCommanderStrikes(GameState player, BotGalaxy galaxy, int at)
        {
            var boss = galaxy.Boss;
            boss.NextRollTick = at + RollIntervalSec;
            var rng = Rng.Mulberry32(unchecked((uint)player.Seed * 0x9E3779B1u ^ (uint)boss.Visit * 0x7F4A7C15u
                ^ (uint)(at / RollIntervalSec)));
            if (rng() >= RollChance) return;

            var able = new List<(BotEmpire bot, Dictionary<HullId, int> ships, int arrive)>();
            foreach (var bot in galaxy.Bots)
            {
                if (bot.CachedMight < BotSystem.PlayerShieldMight) continue;
                if (TileXY.Distance(bot.HomeTile, boss.Tile) > BotRange) continue;
                var personality = BotSystem.PersonalityOf(player.Seed, bot.Id);
                if (!BotSystem.IsAwake(player.Seed, bot.Id, personality.Activity, at)) continue;
                if (Busy(galaxy, bot.Id)) continue;
                var ships = BotSystem.CombatFleetOf(bot.State, BotCommit);
                if (MarchSystem.FleetCount(ships) < 8) continue;
                int arrive = at + Balance.TravelSeconds(TileXY.Distance(bot.HomeTile, boss.Tile),
                    Math.Max(1, MarchSystem.FleetSpeed(ships)));
                if (arrive >= boss.LeavesTick) continue;
                able.Add((bot, ships, arrive));
            }
            if (able.Count == 0) return;
            able.Sort((a, b) => a.bot.CachedMight != b.bot.CachedMight
                ? b.bot.CachedMight.CompareTo(a.bot.CachedMight) : a.bot.Id.CompareTo(b.bot.Id));
            for (int n = 0; n < StrikesPerRoll && able.Count > 0; n++)
            {
                // Squared roll: the strongest few go most often, with some spread.
                int reach = Math.Min(20, able.Count);
                int pick = (int)(rng() * rng() * reach) % reach;
                var (bot, ships, arrive) = able[pick];
                able.RemoveAt(pick);
                foreach (var kv in ships)
                    bot.State.Ships[kv.Key] = Math.Max(0, (bot.State.Ships.TryGetValue(kv.Key, out var have) ? have : 0) - kv.Value);
                bot.CachedMight = PowerSystem.ComputePower(bot.State);
                galaxy.Marches.Add(new BotMarch
                {
                    Id = galaxy.NextMarchId++,
                    BotId = bot.Id,
                    TargetBotId = boss.Visit,
                    Kind = BotMarchKind.BossStrike,
                    Ships = new Dictionary<HullId, int>(ships),
                    From = bot.HomeTile,
                    To = boss.Tile,
                    LaunchTick = at,
                    ArrivesAtTick = arrive,
                });
            }
        }

        static void ResolveCommanderStrike(GameState player, BotGalaxy galaxy, SimEventBus events, BotMarch march, int at)
        {
            var boss = galaxy.Boss;
            var bot = galaxy.Find(march.BotId);
            if (bot == null) { galaxy.Marches.Remove(march); return; }
            if (!boss.Active || march.TargetBotId != boss.Visit)
            {
                StrikeSystem.SendHomeAfterBattle(galaxy, march, march.Ships, new ResourceBag(), at);
                return;
            }
            var result = BossCombat.Strike(march.Ships, ResearchSystem.CombatMods(bot.State), boss.Hp, boss.MaxHp, boss.Cannon);
            boss.Hp = Math.Max(0, boss.Hp - result.Damage);
            boss.Damage[bot.Id] = (boss.Damage.TryGetValue(bot.Id, out var d) ? d : 0) + result.Damage;
            StrikeSystem.SendHomeAfterBattle(galaxy, march, result.Survivors, Salvage(result.Damage, result.Survivors), at);
            if (result.Killed) EndVisit(player, galaxy, events, at, killerId: bot.Id);
        }

        // ---------- the end of a visit ----------

        /// <param name="killerId">Who broke it apart (0 = you); null = it jumped away.</param>
        static void EndVisit(GameState player, BotGalaxy galaxy, SimEventBus events, int at, int? killerId)
        {
            var boss = galaxy.Boss;
            bool killed = killerId != null;
            var race = ClanRace(player, galaxy);
            var top = race.Find(s => s.ClanId != 0);
            var (mine, rank, of) = YourStanding(galaxy);
            long total = TotalDamage(boss);

            int reward = 0;
            if (mine > 0 && total > 0)
            {
                int pool = killed ? PoolDMKilled : PoolDMEscaped;
                reward = Math.Max(MinRewardDM, (int)Math.Round(pool * (mine / (double)total)));
                if (killed && top != null && top.Yours) reward += TopClanDM;
                player.Premium.DarkMatter += reward;
            }

            string killer = killerId == 0 ? ClanSystem.Tagged(player, galaxy, 0, player.Profile.Name)
                : killerId is int id && galaxy.Find(id) is { } k ? ClanSystem.Tagged(player, galaxy, k.Id, k.Name) : "";
            galaxy.AddBulletin(at, killed
                ? $"The Pirate Dreadnought was destroyed — {killer} landed the final blow" +
                  (top != null ? $"; {top.Name} dealt the most damage" : "")
                : $"The Pirate Dreadnought jumped away with {Math.Round(HullShare(boss) * 100):0}% of its hull left");

            if (mine > 0)
                BotSystem.InsertMail(player, new BossReport
                {
                    Id = player.NextReportId++,
                    AtTick = at,
                    Target = boss.Tile,
                    Subject = killed ? $"Dreadnought destroyed — you placed #{rank} of {of} · +{reward} DM"
                        : $"Dreadnought escaped — you placed #{rank} of {of} · +{reward} DM",
                    Kind = BossReportKind.Result,
                    Visit = boss.Visit,
                    Killed = killed,
                    YourDamage = mine,
                    TotalDamage = total,
                    MaxHp = boss.MaxHp,
                    HpAfter = boss.Hp,
                    Rank = rank,
                    Of = of,
                    TopClan = top?.Name,
                    RewardDM = reward,
                });

            boss.LastKilled = killed;
            boss.LastTopClan = top?.Name;
            boss.LastYourDamage = mine;
            boss.LastYourRank = rank;
            boss.LastRewardDM = reward;
            boss.Active = false;
            boss.NextVisitTick = at + GapSec;
            events.Emit(new BossDeparted(killed, mine, reward));
        }

        static string FmtDamage(long d) =>
            d >= 1_000_000 ? $"{d / 1_000_000.0:0.0}M" : d >= 10_000 ? $"{d / 1000.0:0.#}K" : d.ToString("N0");
    }
}
