// The Galactic Core (endgame, 2026-09-28). An ancient station at the heart of
// the galaxy, first held by the Core Guardians. Any commander can assault it:
// win the battle and the surviving fleet stays behind as its garrison.
//   - The holder earns TRIBUTE every hour it holds: a share of its own hourly
//     production (the player also gets Dark Matter). The player also earns a
//     smaller clan tribute while a clanmate holds the core.
//   - The station's guns (CoreBattery) fight for whoever holds it, and the
//     holder's clanmates near the core send warships to defend it.
//   - The simulated commanders contest it: every couple of hours the strongest
//     commander who could win launches an assault, bringing clanmates' wings.
//   - The player's garrison is their Core march holding at the core; recall it
//     and the core falls back to the guardians.
// Tick runs from BotSystem.Advance as a small time-ordered event loop (guardian
// rebuilds, tribute, assault arrivals, assault rolls), so an offline catch-up
// replays the core's history in the order it happened.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;

namespace GalaxyRoyale.Sim.Systems
{
    public static class CoreSystem
    {
        public static readonly TileXY CoreTile = new(Balance.SectorSize / 2, Balance.SectorSize / 2);
        /// <summary>HolderId while nobody holds the core.</summary>
        public const int GuardiansId = -1;
        /// <summary>March.GuardEmpireId of the player's standing core garrison, and
        /// BattleMailReport.GuardedBotId on a report of a fight at the core.</summary>
        public const int CoreGuardId = -1;
        /// <summary>BotMarch.TargetBotId of clan wings flying with the player's core assault.</summary>
        public const int CoreTargetId = -1;

        /// <summary>The station's guns: an Orbital Batteries level for whoever holds it.</summary>
        public const int CoreBattery = 6;
        public const float GuardianStatMult = 1.25f;
        /// <summary>Guardians are this many times the tenth-strongest commander's full
        /// fleet: beyond any lone commander at first, within reach of the strongest
        /// clans' joint assaults in a day or two (Pace_GuardiansAgainstTheStrongestCommanders).</summary>
        public const double GuardianPowerFactor = 0.9;
        public const long MinGuardianPower = 20_000;
        /// <summary>Worn guardians rebuild to full strength after this long.</summary>
        public const int GuardianRebuildSec = 6 * 3600;
        public const int TributeIntervalSec = 3600;
        /// <summary>Share of the holder's hourly production paid every hour it holds.</summary>
        public const double TributeShare = 0.12;
        public const int TributeDarkMatter = 5;
        /// <summary>The player's share while a clanmate holds the core.</summary>
        public const double ClanTributeShare = 0.04;
        public const int RollIntervalSec = 2 * 3600;
        public const double RollChance = 0.6;
        /// <summary>No commander assaults the core this soon after it changes hands.</summary>
        public const int SettleSec = 6 * 3600;
        /// <summary>The holder's clanmates this close to the core (tiles) defend it.</summary>
        public const double HelperRange = 400;
        public const int MaxHelpers = 3;
        public const double HelperShare = 0.15;
        /// <summary>Galactic Command: the holder's marches fly this much faster.</summary>
        public const double CommandSpeedMult = 1.10;
        /// <summary>Entries kept in the core's history log.</summary>
        public const int MaxHistory = 30;

        // ---------- who holds it ----------

        public static bool PlayerHolds(BotGalaxy galaxy) => galaxy.Core.HolderId == 0;

        public static BotEmpire? HolderBot(BotGalaxy galaxy) =>
            galaxy.Core.HolderId > 0 ? galaxy.Find(galaxy.Core.HolderId) : null;

        /// <summary>A clanmate of the player holds the core.</summary>
        public static bool ClanHolds(GameState player, BotGalaxy galaxy) =>
            HolderBot(galaxy) is { } holder && ClanSystem.SameClanAsPlayer(player, holder);

        public static string HolderName(GameState player, BotGalaxy galaxy)
        {
            var core = galaxy.Core;
            if (core.HolderId == GuardiansId) return "the Core Guardians";
            if (core.HolderId == 0) return ClanSystem.Tagged(player, galaxy, 0, player.Profile.Name);
            var bot = galaxy.Find(core.HolderId);
            return bot != null ? ClanSystem.Tagged(player, galaxy, bot.Id, bot.Name) : "an unknown commander";
        }

        /// <summary>The player's standing core garrison (their Core march holding at the core).</summary>
        public static March? PlayerGarrison(GameState player)
        {
            foreach (var m in player.Marches)
                if (m.Mission == MarchMission.Core && m.Phase == MarchPhase.Gathering && m.GuardEmpireId == CoreGuardId)
                    return m;
            return null;
        }

        /// <summary>The fleet standing at the core right now (guardians, bot or player garrison).</summary>
        public static Dictionary<HullId, int> GarrisonShips(GameState player, BotGalaxy galaxy)
        {
            var core = galaxy.Core;
            if (core.HolderId == GuardiansId) return new Dictionary<HullId, int>(core.Guardians);
            if (core.HolderId == 0) return new Dictionary<HullId, int>(PlayerGarrison(player)?.Ships ?? new());
            return new Dictionary<HullId, int>(core.Garrison);
        }

        // ---------- defence ----------

        public sealed class Defence
        {
            /// <summary>Line 0 is the garrison (or guardians); the rest are helpers' shares.</summary>
            public List<Dictionary<HullId, int>> Lines = new();
            public List<BotEmpire> Helpers = new();
            public FleetMods Mods;
        }

        /// <summary>What an assault has to beat right now: the garrison, the holder's
        /// clanmates near the core, and the station's guns.</summary>
        public static Defence DefenceOf(GameState player, BotGalaxy galaxy, int attackerId = GuardiansId)
        {
            var core = galaxy.Core;
            var d = new Defence();
            if (core.HolderId == GuardiansId)
            {
                d.Lines.Add(new Dictionary<HullId, int>(core.Guardians));
                d.Mods = new FleetMods(GuardianStatMult, GuardianStatMult, batteryLevel: CoreBattery);
                return d;
            }
            var holderState = core.HolderId == 0 ? player : HolderBot(galaxy)?.State;
            d.Lines.Add(GarrisonShips(player, galaxy));
            int clanId = ClanSystem.ClanOf(player, galaxy, core.HolderId);
            if (clanId != 0)
            {
                var near = new List<(BotEmpire bot, double dist)>();
                foreach (var bot in galaxy.Bots)
                {
                    if (bot.ClanId != clanId || bot.Id == core.HolderId || bot.Id == attackerId) continue;
                    double dist = TileXY.Distance(bot.HomeTile, CoreTile);
                    if (dist <= HelperRange) near.Add((bot, dist));
                }
                near.Sort((a, b) => a.dist != b.dist ? a.dist.CompareTo(b.dist) : a.bot.Id.CompareTo(b.bot.Id));
                foreach (var (bot, _) in near)
                {
                    if (d.Helpers.Count >= MaxHelpers) break;
                    var share = ClanSystem.Share(bot, HelperShare);
                    if (share.Count == 0) continue;
                    d.Lines.Add(share);
                    d.Helpers.Add(bot);
                }
            }
            d.Mods = holderState != null ? WithBattery(ResearchSystem.CombatMods(holderState), CoreBattery)
                : new FleetMods(batteryLevel: CoreBattery);
            return d;
        }

        static FleetMods WithBattery(FleetMods m, int battery) =>
            new(m.AtkMult, m.HpMult, m.AtkByHull, m.HpByHull, m.ShieldMult, battery);

        /// <summary>Crude defensive strength, on the same scale as BotSystem.EstimateFleetPower.</summary>
        public static long DefencePower(Defence d)
        {
            double scale = ((d.Mods.AtkMult > 0 ? d.Mods.AtkMult : 1f) + (d.Mods.HpMult > 0 ? d.Mods.HpMult : 1f)) * 0.5;
            long battery = (long)d.Mods.BatteryLevel * Balance.BatteryDamagePerLevel * Balance.BatteryOnlyRounds;
            return (long)(BotSystem.EstimateFleetPower(ClanSystem.Combine(d.Lines)) * scale) + battery;
        }

        // ---------- the guardians ----------

        /// <summary>A guardian fleet of roughly <paramref name="power"/> (cruisers, bombers, fighters 50/30/20).</summary>
        public static Dictionary<HullId, int> GuardianFleet(long power)
        {
            static long Unit(HullId h) { var d = Ships.Defs[h]; return d.Atk + d.Shield + d.Hp / 2; }
            var fleet = new Dictionary<HullId, int>
            {
                [HullId.Cruiser] = (int)Math.Max(1, power * 0.5 / Unit(HullId.Cruiser)),
                [HullId.Bomber] = (int)Math.Max(1, power * 0.3 / Unit(HullId.Bomber)),
                [HullId.Fighter] = (int)Math.Max(1, power * 0.2 / Unit(HullId.Fighter)),
            };
            return fleet;
        }

        /// <summary>Full guardian strength for this galaxy right now.</summary>
        public static long GuardianTargetPower(BotGalaxy galaxy)
        {
            var powers = new List<long>();
            foreach (var bot in galaxy.Bots)
                powers.Add(BotSystem.EstimateFleetPower(BotSystem.CombatFleetOf(bot.State, 1.0)));
            if (powers.Count == 0) return MinGuardianPower;
            powers.Sort((a, b) => b.CompareTo(a));
            long tenth = powers[Math.Min(9, powers.Count - 1)];
            return Math.Max(MinGuardianPower, (long)(tenth * GuardianPowerFactor));
        }

        static void RebuildGuardians(BotGalaxy galaxy, int at, double strength = 1.0)
        {
            var core = galaxy.Core;
            core.Guardians = GuardianFleet((long)(GuardianTargetPower(galaxy) * strength));
            core.GuardiansRebuildTick = at + GuardianRebuildSec;
        }

        // ---------- the player's moves ----------

        public static SimResult CanSend(GameState player, BotGalaxy galaxy)
        {
            if (ClanHolds(player, galaxy)) return SimResult.Fail("Your clan holds the core — no assault needed");
            return SimResult.Success;
        }

        /// <summary>
        /// Send a fleet to the core: an assault (with joint-strike wings when
        /// <paramref name="joint"/>), or — while you hold it — reinforcements that
        /// join your garrison when they land.
        /// </summary>
        public static SimResult SendToCore(GameState player, BotGalaxy galaxy, Dictionary<HullId, int> ships,
            bool joint, out int marchId, out int wings)
        {
            marchId = 0;
            wings = 0;
            var can = CanSend(player, galaxy);
            if (!can.Ok) return can;
            if (MarchSystem.FleetCount(ships) < 1) return SimResult.Fail("No ships selected");
            int arrive = player.Tick + MarchSystem.FlightSeconds(player, ships, CoreTile);
            var sent = MarchSystem.SendFlight(player, ships, CoreTile, arrive, MarchMission.Core, out var march);
            if (!sent.Ok || march == null) return sent;
            marchId = march.Id;
            if (PlayerHolds(galaxy)) return SimResult.Success; // reinforcements
            BotSystem.BreakShieldForAggression(player);
            if (joint)
            {
                int exclude = galaxy.Core.HolderId > 0 ? galaxy.Core.HolderId : 0;
                wings = StrikeSystem.LaunchWings(player, galaxy,
                    StrikeSystem.StrikeWings(player, galaxy, CoreTile, arrive, exclude),
                    BotMarchKind.Escort, march.Id, CoreTargetId, CoreTile, arrive, ClanSystem.RaidSupportCooldownSec).Count;
            }
            return SimResult.Success;
        }

        // ---------- the clock ----------

        /// <summary>Per sim tick (BotSystem.Advance): replays everything that happened at
        /// the core up to now, in time order.</summary>
        public static void Tick(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            var core = galaxy.Core;
            int now = player.Tick;
            if (core.HolderId == GuardiansId && core.Guardians.Count == 0 && core.GuardiansRebuildTick == 0)
                RebuildGuardians(galaxy, now);
            if (core.NextRollTick == 0) core.NextRollTick = now + RollIntervalSec;

            TournamentTick(player, galaxy, events, now);
            // You recalled your garrison (or it was wiped): the core falls back to the guardians.
            if (core.HolderId == 0 && PlayerGarrison(player) == null) Release(player, galaxy, events, now);
            SyncCommand(player, galaxy);

            for (int guard = 0; guard < 512; guard++)
            {
                // The next thing due, earliest first: rebuild, tribute, arrival, roll.
                int rebuildAt = core.HolderId == GuardiansId ? core.GuardiansRebuildTick : int.MaxValue;
                int tributeAt = core.HolderId != GuardiansId ? core.NextTributeTick : int.MaxValue;
                var (arrivalAt, playerMarch, botGroup) = NextArrival(player, galaxy);
                int rollAt = core.NextRollTick;
                int t = Math.Min(Math.Min(rebuildAt, tributeAt), Math.Min(arrivalAt, rollAt));
                if (t > now) break;

                if (rebuildAt == t)
                {
                    RebuildGuardians(galaxy, t);
                    Log(galaxy, new CoreLogEntry { AtTick = t, Kind = CoreLogKind.Rebuilt, ActorId = GuardiansId, Actor = "the Core Guardians" });
                }
                else if (tributeAt == t) PayTribute(player, galaxy, events, t);
                else if (arrivalAt == t)
                {
                    if (playerMarch != null) ResolvePlayerArrival(player, galaxy, events, playerMarch, t);
                    else ResolveBotAssault(player, galaxy, events, botGroup, t);
                }
                else RollBotAssault(player, galaxy, events, t);
            }
            SyncCommand(player, galaxy);
        }

        /// <summary>The earliest core arrival waiting to be settled: a Core march of
        /// yours that has landed, or a commander's assault group.</summary>
        static (int at, March? playerMarch, int botGroup) NextArrival(GameState player, BotGalaxy galaxy)
        {
            int best = int.MaxValue;
            March? pm = null;
            int group = 0;
            foreach (var m in player.Marches)
                if (m.Mission == MarchMission.Core && m.Phase == MarchPhase.Gathering && m.GuardEmpireId == 0
                    && m.DepartedAtTick < best) // Hold stamps the landing tick
                {
                    best = m.DepartedAtTick;
                    pm = m;
                }
            foreach (var m in galaxy.Marches)
                if (m.Kind == BotMarchKind.CoreAssault && !m.Resolved && m.ArrivesAtTick < best)
                {
                    best = m.ArrivesAtTick;
                    pm = null;
                    group = m.LinkId;
                }
            return (best, pm, group);
        }

        static void Take(GameState player, BotGalaxy galaxy, SimEventBus events, int holderId, int at)
        {
            var core = galaxy.Core;
            int prev = core.HolderId;
            string from = prev == GuardiansId ? "the Core Guardians" : HolderName(player, galaxy);
            int heldSec = prev == GuardiansId ? 0 : Math.Max(0, at - core.HeldSinceTick);
            HandOver(galaxy, prev);
            core.HolderId = holderId;
            core.HeldSinceTick = at;
            core.NextTributeTick = at + TributeIntervalSec;
            core.TimesSeized++;
            core.Garrison.Clear();
            core.Guardians.Clear();
            core.GuardiansRebuildTick = 0;
            core.NextRollTick = Math.Max(core.NextRollTick, at + (InTournament(at) ? SettleSec / 3 : SettleSec));
            galaxy.AddBulletin(at, $"{HolderName(player, galaxy)} seized the Galactic Core");
            Log(galaxy, new CoreLogEntry
            {
                AtTick = at, Kind = CoreLogKind.Seized, ActorId = holderId,
                Actor = HolderName(player, galaxy), Other = from, HeldSec = heldSec,
            });
            SyncCommand(player, galaxy);
            events.Emit(new CoreSeized(holderId, prev));
        }

        /// <summary>Nobody holds the core any more: the guardians return, half-strength
        /// until they rebuild.</summary>
        static void Release(GameState player, BotGalaxy galaxy, SimEventBus events, int at)
        {
            var core = galaxy.Core;
            int prev = core.HolderId;
            string was = HolderName(player, galaxy);
            int heldSec = Math.Max(0, at - core.HeldSinceTick);
            HandOver(galaxy, prev);
            core.HolderId = GuardiansId;
            core.HeldSinceTick = at;
            core.NextTributeTick = 0;
            core.Garrison.Clear();
            RebuildGuardians(galaxy, at, 0.5);
            galaxy.AddBulletin(at, $"{was} abandoned the Galactic Core — the guardians returned");
            Log(galaxy, new CoreLogEntry { AtTick = at, Kind = CoreLogKind.Abandoned, ActorId = prev, Actor = was, HeldSec = heldSec });
            SyncCommand(player, galaxy);
            events.Emit(new CoreSeized(GuardiansId, prev));
        }

        // ---------- tribute ----------

        static ResourceBag HourShare(GameState state, double share)
        {
            var rates = ResourceSystem.GetRates(state); // milli per hour
            return new ResourceBag((long)(rates.Gold * share), (long)(rates.Quartz * share), (long)(rates.Helium * share));
        }

        static void PayTribute(GameState player, BotGalaxy galaxy, SimEventBus events, int at)
        {
            var core = galaxy.Core;
            core.NextTributeTick = at + TributeIntervalSec;
            if (core.HolderId == 0)
            {
                var bag = HourShare(player, TributeShare);
                ResourceSystem.Add(player, bag);
                player.Premium.DarkMatter += TributeDarkMatter;
                player.Stats.CoreHoursHeld++;
                events.Emit(new CoreTributePaid(bag, TributeDarkMatter, Clan: false));
                return;
            }
            if (HolderBot(galaxy) is not { } holder) return;
            ResourceSystem.Add(holder.State, HourShare(holder.State, TributeShare));
            if (ClanSystem.SameClanAsPlayer(player, holder))
            {
                var bag = HourShare(player, EmbassySystem.ClanTributeShare(player));
                ResourceSystem.Add(player, bag);
                events.Emit(new CoreTributePaid(bag, 0, Clan: true));
            }
        }

        // ---------- the player's assault ----------

        static void ResolvePlayerArrival(GameState player, BotGalaxy galaxy, SimEventBus events, March march, int at)
        {
            var core = galaxy.Core;
            if (core.HolderId == 0)
            {
                if (PlayerGarrison(player) is { } garrison)
                {
                    // Reinforcements join the garrison.
                    foreach (var kv in march.Ships)
                        garrison.Ships[kv.Key] = (garrison.Ships.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
                    StrikeSystem.TurnEscortsHome(galaxy, march.Id, at);
                    player.Marches.Remove(march);
                    return;
                }
                Release(player, galaxy, events, at); // no garrison left: this fleet assaults the guardians
            }
            if (ClanHolds(player, galaxy))
            {
                // A clanmate took it while you were on the way — stand down.
                StrikeSystem.TurnEscortsHome(galaxy, march.Id, at);
                MarchSystem.ReturnHome(player, march, at);
                return;
            }

            var escorts = StrikeSystem.EscortsOf(galaxy, march.Id);
            var atkLines = new List<Dictionary<HullId, int>> { new Dictionary<HullId, int>(march.Ships) };
            foreach (var e in escorts) atkLines.Add(e.Ships);
            int holderId = core.HolderId;
            var holderBot = HolderBot(galaxy);
            string holderName = HolderName(player, galaxy);
            var defence = DefenceOf(player, galaxy);

            var report = CombatResolver.Resolve(ClanSystem.Combine(atkLines), ClanSystem.Combine(defence.Lines),
                ResearchSystem.CombatMods(player), defence.Mods);
            report.Location = CoreTile;
            report.DefenderName = holderId == GuardiansId ? "Core Guardians" : holderName;
            bool won = report.Winner == BattleWinner.Attacker;

            var atkLosses = ClanSystem.SplitLosses(atkLines, report.AttackerSurvivors);
            ApplyDefenceLosses(player, galaxy, defence, report.DefenderSurvivors, at);
            for (int i = 0; i < escorts.Count; i++)
                StrikeSystem.SendHomeAfterBattle(galaxy, escorts[i], Minus(atkLines[i + 1], atkLosses[i + 1]), new ResourceBag(), at);

            march.Ships = Minus(atkLines[0], atkLosses[0]);
            if (won)
            {
                player.Stats.BattlesWon++;
                player.Stats.CoresSeized++;
                march.GuardEmpireId = CoreGuardId; // it stays as the garrison
                Take(player, galaxy, events, 0, at);
            }
            else
            {
                player.Stats.BattlesLost++;
                if (MarchSystem.FleetCount(march.Ships) == 0) player.Marches.Remove(march);
                else MarchSystem.ReturnHome(player, march, at);
                Log(galaxy, new CoreLogEntry
                {
                    AtTick = at, Kind = CoreLogKind.Repelled, ActorId = 0,
                    Actor = ClanSystem.Tagged(player, galaxy, 0, player.Profile.Name),
                    Other = holderId == GuardiansId ? "the Core Guardians" : holderName, ShipsLost = ShipsIn(atkLosses),
                });
                galaxy.AddBulletin(at,
                    $"{holderName} held the Galactic Core against {ClanSystem.Tagged(player, galaxy, 0, player.Profile.Name)}");
            }
            if (holderBot != null) ClanSystem.RecordBattle(player, galaxy, 0, holderBot.Id, won);

            BotSystem.InsertMail(player, new BattleMailReport
            {
                Id = player.NextReportId++,
                AtTick = at,
                Target = CoreTile,
                Subject = won ? "Core assault — you seized the Galactic Core"
                    : report.Winner == BattleWinner.Draw ? $"Core assault — stalemate against {report.DefenderName}"
                    : $"Core assault repelled — {report.DefenderName}",
                Report = report,
                AllyShips = escorts.Count > 0 ? ClanSystem.Combine(escorts.ConvertAll(e => e.Ships)) : null,
                AllyNames = escorts.Count > 0 ? NamesOf(galaxy, escorts.ConvertAll(e => e.BotId)) : null,
                EnemyAllyShips = defence.Helpers.Count > 0 ? ClanSystem.Combine(defence.Lines.GetRange(1, defence.Lines.Count - 1)) : null,
                EnemyAllyNames = defence.Helpers.Count > 0 ? string.Join(", ", defence.Helpers.ConvertAll(h => h.Name)) : null,
            });
            events.Emit(new BattleResolved(report));
        }

        /// <summary>The defenders' losses land: guardians worn, a garrison thinned, helpers' docks.</summary>
        static void ApplyDefenceLosses(GameState player, BotGalaxy galaxy, Defence defence,
            Dictionary<HullId, int> survivors, int at)
        {
            var core = galaxy.Core;
            var losses = ClanSystem.SplitLosses(defence.Lines, survivors);
            // Worn guardians stay worn until their next six-hourly muster (RebuildGuardians).
            if (core.HolderId == GuardiansId) ClanSystem.Deduct(core.Guardians, losses[0]);
            else if (core.HolderId == 0)
            {
                if (PlayerGarrison(player) is { } garrison) ClanSystem.Deduct(garrison.Ships, losses[0]);
            }
            else ClanSystem.Deduct(core.Garrison, losses[0]);
            RemoveEmpty(core.Guardians);
            RemoveEmpty(core.Garrison);
            for (int i = 0; i < defence.Helpers.Count; i++)
            {
                ClanSystem.Deduct(defence.Helpers[i].State.Ships, losses[i + 1]);
                defence.Helpers[i].CachedMight = PowerSystem.ComputePower(defence.Helpers[i].State);
            }
        }

        // ---------- the simulated commanders ----------

        /// <summary>Clanmates of <paramref name="lead"/> who'd fly with its assault:
        /// rested, idle, within strike range of the core — strongest first.</summary>
        static List<(BotEmpire bot, Dictionary<HullId, int> ships)> BotWings(BotGalaxy galaxy, BotEmpire lead, int at)
        {
            var list = new List<(BotEmpire bot, Dictionary<HullId, int> ships)>();
            if (lead.ClanId == 0) return list;
            foreach (var bot in galaxy.Bots)
            {
                if (bot.ClanId != lead.ClanId || bot.Id == lead.Id || bot.SupportReadyTick > at) continue;
                if (TileXY.Distance(bot.HomeTile, CoreTile) > StrikeSystem.StrikeRange) continue;
                if (Busy(galaxy, bot.Id)) continue;
                var ships = ClanSystem.Share(bot, StrikeSystem.StrikeShare);
                if (ships.Count > 0) list.Add((bot, ships));
            }
            list.Sort((a, b) =>
            {
                int c = BotSystem.EstimateFleetPower(b.ships).CompareTo(BotSystem.EstimateFleetPower(a.ships));
                return c != 0 ? c : a.bot.Id.CompareTo(b.bot.Id);
            });
            if (list.Count > StrikeSystem.MaxStrikeWings) list.RemoveRange(StrikeSystem.MaxStrikeWings, list.Count - StrikeSystem.MaxStrikeWings);
            return list;
        }

        static bool Busy(BotGalaxy galaxy, int botId)
        {
            foreach (var m in galaxy.Marches) if (m.BotId == botId) return true;
            foreach (var a in galaxy.Inbound) if (a.BotId == botId) return true;
            return false;
        }

        static void RollBotAssault(GameState player, BotGalaxy galaxy, SimEventBus events, int at)
        {
            var core = galaxy.Core;
            core.NextRollTick = at + (InTournament(at) ? RollIntervalSec / 2 : RollIntervalSec);
            foreach (var m in galaxy.Marches)
                if (m.Kind == BotMarchKind.CoreAssault && !m.Resolved) return; // one assault at a time
            var rng = Rng.Mulberry32(unchecked((uint)player.Seed * 2654435761u ^ (uint)(at / RollIntervalSec) ^ 0xC0DEC0DEu));
            if (rng() >= (InTournament(at) ? 0.9 : RollChance)) return;

            int holderClan = core.HolderId == GuardiansId ? 0 : ClanSystem.ClanOf(player, galaxy, core.HolderId);
            // Against your garrison, the difficulty sets the edge they want.
            double edge = core.HolderId == 0 ? Difficulties.BeatabilityEdge(player.Difficulty) : BotSystem.BeatabilityEdge;
            long toBeat = (long)(DefencePower(DefenceOf(player, galaxy)) * edge);

            BotEmpire? best = null;
            Dictionary<HullId, int>? bestLead = null;
            List<(BotEmpire bot, Dictionary<HullId, int> ships)>? bestWings = null;
            long bestPower = 0;
            foreach (var bot in galaxy.Bots)
            {
                if (bot.Id == core.HolderId || (holderClan != 0 && bot.ClanId == holderClan)) continue;
                if (bot.CachedMight < BotSystem.PlayerShieldMight) continue;
                var personality = BotSystem.PersonalityOf(player.Seed, bot.Id);
                if (!BotSystem.IsAwake(player.Seed, bot.Id, personality.Activity, at)) continue;
                if (Busy(galaxy, bot.Id)) continue;
                var lead = BotSystem.CombatFleetOf(bot.State, BotSystem.RaidCommitFraction);
                if (MarchSystem.FleetCount(lead) < 8) continue;
                var wings = BotWings(galaxy, bot, at);
                long power = BotSystem.EstimateFleetPower(lead);
                foreach (var (_, ships) in wings) power += BotSystem.EstimateFleetPower(ships);
                if (power < toBeat || power <= bestPower) continue;
                best = bot;
                bestLead = lead;
                bestWings = wings;
                bestPower = power;
            }
            if (best == null || bestLead == null || bestWings == null) return;

            // Everyone lands together: the slowest flight sets the arrival.
            int Travel(BotEmpire b, Dictionary<HullId, int> ships) =>
                Balance.TravelSeconds(TileXY.Distance(b.HomeTile, CoreTile), Math.Max(1, MarchSystem.FleetSpeed(ships)));
            int arrive = at + Travel(best, bestLead);
            foreach (var (bot, ships) in bestWings) arrive = Math.Max(arrive, at + Travel(bot, ships));

            int group = galaxy.NextMarchId;
            Launch(galaxy, best, bestLead, group, arrive - Travel(best, bestLead), arrive, core.HolderId);
            foreach (var (bot, ships) in bestWings)
            {
                bot.SupportReadyTick = at + ClanSystem.RaidSupportCooldownSec;
                Launch(galaxy, bot, ships, group, arrive - Travel(bot, ships), arrive, core.HolderId);
            }
            if (core.HolderId == 0 || ClanHolds(player, galaxy))
            {
                galaxy.AddBulletin(at, $"{ClanSystem.Tagged(player, galaxy, best.Id, best.Name)} is marching on the Galactic Core");
                if (core.HolderId == 0) events.Emit(new CoreUnderAttack(best.Id, arrive));
            }
        }

        static void Launch(BotGalaxy galaxy, BotEmpire bot, Dictionary<HullId, int> ships, int group,
            int launch, int arrive, int holderId)
        {
            foreach (var kv in ships)
                bot.State.Ships[kv.Key] = Math.Max(0, (bot.State.Ships.TryGetValue(kv.Key, out var n) ? n : 0) - kv.Value);
            bot.CachedMight = PowerSystem.ComputePower(bot.State);
            galaxy.Marches.Add(new BotMarch
            {
                Id = galaxy.NextMarchId++,
                BotId = bot.Id,
                TargetBotId = holderId,
                Kind = BotMarchKind.CoreAssault,
                LinkId = group,
                Ships = new Dictionary<HullId, int>(ships),
                From = bot.HomeTile,
                To = CoreTile,
                LaunchTick = launch,
                ArrivesAtTick = arrive,
            });
        }

        static void ResolveBotAssault(GameState player, BotGalaxy galaxy, SimEventBus events, int group, int at)
        {
            var core = galaxy.Core;
            var wings = galaxy.Marches.FindAll(m => m.Kind == BotMarchKind.CoreAssault && !m.Resolved && m.LinkId == group);
            if (wings.Count == 0) return;
            wings.Sort((a, b) => a.Id.CompareTo(b.Id)); // the lead launched first
            var lead = wings[0];
            var attacker = galaxy.Find(lead.BotId);
            int holderClan = core.HolderId == GuardiansId ? 0 : ClanSystem.ClanOf(player, galaxy, core.HolderId);
            if (attacker == null || attacker.Id == core.HolderId || (holderClan != 0 && attacker.ClanId == holderClan))
            {
                // Their own clan holds it now — they stand down.
                foreach (var w in wings) StrikeSystem.SendHomeAfterBattle(galaxy, w, w.Ships, new ResourceBag(), at);
                return;
            }

            int prev = core.HolderId;
            string defenderName = prev == GuardiansId ? "Core Guardians" : HolderName(player, galaxy);
            var playerGarrison = prev == 0 ? PlayerGarrison(player) : null;
            var atkLines = wings.ConvertAll(w => new Dictionary<HullId, int>(w.Ships));
            var defence = DefenceOf(player, galaxy, attacker.Id);
            var report = CombatResolver.Resolve(ClanSystem.Combine(atkLines), ClanSystem.Combine(defence.Lines),
                ResearchSystem.CombatMods(attacker.State), defence.Mods);
            report.Location = CoreTile;
            report.DefenderName = defenderName;
            bool won = report.Winner == BattleWinner.Attacker;

            var atkLosses = ClanSystem.SplitLosses(atkLines, report.AttackerSurvivors);
            ApplyDefenceLosses(player, galaxy, defence, report.DefenderSurvivors, at);
            if (prev >= 0) ClanSystem.RecordBattle(player, galaxy, attacker.Id, prev, won);

            var survivors = new List<Dictionary<HullId, int>>();
            for (int i = 0; i < wings.Count; i++) survivors.Add(Minus(atkLines[i], atkLosses[i]));
            // The lead's survivors stay on as the garrison — or, if the lead fell, the
            // strongest surviving wing's.
            int keeper = -1;
            if (won)
            {
                if (MarchSystem.FleetCount(survivors[0]) > 0) keeper = 0;
                else
                {
                    long bestPower = 0;
                    for (int i = 1; i < wings.Count; i++)
                    {
                        long p = BotSystem.EstimateFleetPower(survivors[i]);
                        if (p > bestPower) { bestPower = p; keeper = i; }
                    }
                }
            }
            for (int i = 0; i < wings.Count; i++)
                if (i != keeper) StrikeSystem.SendHomeAfterBattle(galaxy, wings[i], survivors[i], new ResourceBag(), at);

            if (keeper >= 0)
            {
                var holder = galaxy.Find(wings[keeper].BotId) ?? attacker;
                if (playerGarrison != null) player.Marches.Remove(playerGarrison);
                galaxy.Marches.Remove(wings[keeper]);
                Take(player, galaxy, events, holder.Id, at);
                core.Garrison = survivors[keeper];
            }
            else
            {
                if (prev != 0)
                    galaxy.AddBulletin(at,
                        $"{defenderName} held the Galactic Core against {ClanSystem.Tagged(player, galaxy, attacker.Id, attacker.Name)}");
                Log(galaxy, new CoreLogEntry
                {
                    AtTick = at, Kind = CoreLogKind.Repelled, ActorId = attacker.Id,
                    Actor = ClanSystem.Tagged(player, galaxy, attacker.Id, attacker.Name),
                    Other = prev == GuardiansId ? "the Core Guardians" : defenderName, ShipsLost = ShipsIn(atkLosses),
                });
            }

            if (prev != 0) return;
            // It was YOUR core: file the defence report.
            bool held = !won;
            if (held) { player.Stats.BattlesWon++; player.Stats.DefensesWon++; }
            else player.Stats.BattlesLost++;
            var clanDefence = defence.Lines.GetRange(1, defence.Lines.Count - 1);
            BotSystem.InsertMail(player, new BattleMailReport
            {
                Id = player.NextReportId++,
                AtTick = at,
                Target = CoreTile,
                Subject = held ? $"Core defended — {attacker.Name}'s assault broke on your garrison"
                    : $"The Galactic Core fell to {attacker.Name}",
                Defending = true,
                AttackerBotId = attacker.Id,
                GuardedBotId = CoreGuardId,
                Report = report,
                AllyShips = clanDefence.Count > 0 ? ClanSystem.Combine(clanDefence) : null,
                AllyNames = defence.Helpers.Count > 0 ? string.Join(", ", defence.Helpers.ConvertAll(h => h.Name)) : null,
                EnemyAllyShips = wings.Count > 1 ? ClanSystem.Combine(atkLines.GetRange(1, atkLines.Count - 1)) : null,
                EnemyAllyNames = wings.Count > 1 ? NamesOf(galaxy, wings.GetRange(1, wings.Count - 1).ConvertAll(w => w.BotId)) : null,
            });
            events.Emit(new BattleResolved(report));
            if (held && playerGarrison != null && MarchSystem.FleetCount(playerGarrison.Ships) == 0)
                Release(player, galaxy, events, at); // the guns held, but nobody is left to hold them
        }

        // ---------- history + buffs ----------

        static void Log(BotGalaxy galaxy, CoreLogEntry entry)
        {
            var log = galaxy.Core.History;
            log.Insert(0, entry);
            if (log.Count > MaxHistory) log.RemoveRange(MaxHistory, log.Count - MaxHistory);
        }

        static int ShipsIn(List<Dictionary<HullId, int>> lines)
        {
            int n = 0;
            foreach (var l in lines) n += MarchSystem.FleetCount(l);
            return n;
        }

        /// <summary>Galactic Command follows the holder: the player's flag every tick,
        /// a bot's whenever the core changes hands (and after a load).</summary>
        static void SyncCommand(GameState player, BotGalaxy galaxy)
        {
            player.Buffs.CoreHolder = galaxy.Core.HolderId == 0;
            if (HolderBot(galaxy) is { } holder) holder.State.Buffs.CoreHolder = true;
        }

        // ---------- the Core Tournament (rival events, 2026-09-30) ----------

        /// <summary>A Core Tournament is under way at this galaxy time (assaults come
        /// twice as often, and the Core changes hands sooner).</summary>
        public static bool InTournament(int tick) => EventSystem.KindAt(tick) == GalaxyEventKind.CoreTournament;

        /// <summary>Open a tournament as it begins (the holder is thrown out, the guardians
        /// drop to half strength) and settle it when it ends (the holder wins the prize).</summary>
        static void TournamentTick(GameState player, BotGalaxy galaxy, SimEventBus events, int now)
        {
            var core = galaxy.Core;
            var live = EventSystem.Current(now);
            bool on = live.Def.Kind == GalaxyEventKind.CoreTournament;
            if (core.TournamentInstance >= 0 && (!on || live.Instance != core.TournamentInstance))
                EndTournament(player, galaxy, events, now);
            if (on && core.TournamentInstance != live.Instance) OpenTournament(player, galaxy, events, live.Instance, now);
        }

        static void OpenTournament(GameState player, BotGalaxy galaxy, SimEventBus events, int instance, int at)
        {
            var core = galaxy.Core;
            core.TournamentInstance = instance;
            string was = core.HolderId == GuardiansId ? "" : HolderName(player, galaxy);
            if (core.HolderId == 0 && PlayerGarrison(player) is { } garrison)
            {
                garrison.GuardEmpireId = 0;
                MarchSystem.ReturnHome(player, garrison, at);
            }
            else if (HolderBot(galaxy) is { } bot)
                foreach (var kv in core.Garrison)
                    bot.State.Ships[kv.Key] = (bot.State.Ships.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            HandOver(galaxy, core.HolderId);
            core.HolderId = GuardiansId;
            core.HeldSinceTick = at;
            core.NextTributeTick = 0;
            core.Garrison.Clear();
            RebuildGuardians(galaxy, at, GalaxyEvents.TournamentGuardians);
            core.NextRollTick = at + 10 * 60;
            galaxy.AddBulletin(at, "The Core Tournament begins: the Galactic Core is open to all");
            Log(galaxy, new CoreLogEntry { AtTick = at, Kind = CoreLogKind.TournamentOpened, ActorId = GuardiansId,
                Actor = "the Core Tournament", Other = was });
            SyncCommand(player, galaxy);
            events.Emit(new CoreTournament(true, false, new ResourceBag(), 0, was));
        }

        static void EndTournament(GameState player, BotGalaxy galaxy, SimEventBus events, int at)
        {
            var core = galaxy.Core;
            core.TournamentInstance = -1;
            if (core.HolderId == GuardiansId)
            {
                galaxy.AddBulletin(at, "The Core Tournament ended with the guardians still holding the Core");
                events.Emit(new CoreTournament(false, false, new ResourceBag(), 0, "the Core Guardians"));
                return;
            }
            string holder = HolderName(player, galaxy);
            Log(galaxy, new CoreLogEntry { AtTick = at, Kind = CoreLogKind.TournamentWon, ActorId = core.HolderId, Actor = holder });
            galaxy.AddBulletin(at, $"{holder} won the Core Tournament");
            if (core.HolderId == 0)
            {
                var prize = HourShare(player, GalaxyEvents.TournamentPrizeHours);
                ResourceSystem.Add(player, prize);
                player.Premium.DarkMatter += GalaxyEvents.TournamentPrizeDarkMatter;
                player.Stats.TournamentsWon++;
                events.Emit(new CoreTournament(false, true, prize, GalaxyEvents.TournamentPrizeDarkMatter, holder));
                return;
            }
            bool clan = HolderBot(galaxy) is { } hb && ClanSystem.SameClanAsPlayer(player, hb);
            if (clan) player.Premium.DarkMatter += GalaxyEvents.TournamentClanDarkMatter;
            events.Emit(new CoreTournament(false, false, new ResourceBag(), clan ? GalaxyEvents.TournamentClanDarkMatter : 0, holder));
        }

        static void HandOver(BotGalaxy galaxy, int prev)
        {
            if (prev > 0 && galaxy.Find(prev) is { } was) was.State.Buffs.CoreHolder = false;
        }

        /// <summary>Core Beacon: while the player holds the core, every assault flying
        /// at it — lead and wings, with where they launched and when they land.</summary>
        public static List<BotMarch> BeaconContacts(GameState player, BotGalaxy galaxy)
        {
            var list = new List<BotMarch>();
            if (galaxy.Core.HolderId != 0) return list;
            foreach (var m in galaxy.Marches)
                if (m.Kind == BotMarchKind.CoreAssault && !m.Resolved && m.ArrivesAtTick > player.Tick)
                    list.Add(m);
            list.Sort((a, b) => a.ArrivesAtTick.CompareTo(b.ArrivesAtTick));
            return list;
        }

        /// <summary>Every assault flying at the core right now, whoever holds it.</summary>
        public static int InboundAssaults(GameState player, BotGalaxy galaxy)
        {
            var groups = new HashSet<int>();
            foreach (var m in galaxy.Marches)
                if (m.Kind == BotMarchKind.CoreAssault && !m.Resolved && m.ArrivesAtTick > player.Tick)
                    groups.Add(m.LinkId);
            foreach (var m in player.Marches)
                if (m.Mission == MarchMission.Core && m.Phase == MarchPhase.Outbound) groups.Add(-m.Id);
            return groups.Count;
        }

        // ---------- bits ----------

        static Dictionary<HullId, int> Minus(Dictionary<HullId, int> fleet, Dictionary<HullId, int> losses)
        {
            var left = new Dictionary<HullId, int>();
            foreach (var kv in fleet)
            {
                int n = kv.Value - (losses.TryGetValue(kv.Key, out var l) ? l : 0);
                if (n > 0) left[kv.Key] = n;
            }
            return left;
        }

        static void RemoveEmpty(Dictionary<HullId, int> fleet)
        {
            var empty = new List<HullId>();
            foreach (var kv in fleet) if (kv.Value <= 0) empty.Add(kv.Key);
            foreach (var h in empty) fleet.Remove(h);
        }

        static string NamesOf(BotGalaxy galaxy, List<int> botIds)
        {
            var names = new List<string>();
            foreach (var id in botIds)
                if (galaxy.Find(id) is { } b && !names.Contains(b.Name)) names.Add(b.Name);
            return string.Join(", ", names);
        }
    }
}
