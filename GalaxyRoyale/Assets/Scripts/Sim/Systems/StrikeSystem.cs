// Joint strikes (user request 2026-09-28: "a joint strike where you could
// attack a planet or fleet with multiple people in the alliance or even defend
// a strike with your alliance"). Built on ClanSystem:
//   - JOINT STRIKE: clanmates' wings fly from their own colonies, reach the
//     target when your fleet does, fight in one line with it, and carry home
//     their share of the plunder.
//   - INTERCEPT: meet a rival fleet in flight — a raid heading for you or a
//     clanmate, or a raider flying home with its plunder — at a point on its
//     path. Break it and the raid is off (and the plunder retaken).
//   - GARRISONS: clanmates can station warships at your colony for twelve
//     hours, and you can station a fleet at a clanmate's colony until you
//     recall it. Garrisons fight in the defense line of any raid there.
// BotSystem.Advance runs BeforeRaids and AfterRaids around the raids it resolves
// (see "the clock" below) — live and in offline catch-up.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;

namespace GalaxyRoyale.Sim.Systems
{
    public static class StrikeSystem
    {
        /// <summary>Clanmates who can fly a joint strike or intercept with you.</summary>
        public const int MaxStrikeWings = 3;
        /// <summary>Share of each clanmate's docked warships a strike wing brings.</summary>
        public const double StrikeShare = 0.25;
        /// <summary>A strike wing's colony must be this close to the target (tiles).</summary>
        public const double StrikeRange = 900;
        public const int MaxGarrisonWings = 3;
        public const double GarrisonShare = 0.20;
        /// <summary>Clanmates this close to your colony answer a garrison request.</summary>
        public const double GarrisonRange = 600;
        /// <summary>A clan garrison stands guard this long after it lands.</summary>
        public const int GarrisonHoldSec = 12 * 3600;
        public const int GarrisonRequestCooldownSec = 12 * 3600;
        /// <summary>Safety net: a joint-strike wing whose raid never settled goes
        /// home after this long at the target (longer than any offline catch-up).</summary>
        public const int EscortWaitLimitSec = 9 * 3600;
        /// <summary>How far (tiles) the hunted fleet may be from the intercept point
        /// when the fleets meet (it changed course = it slipped away).</summary>
        public const double InterceptTolerance = 4;
        /// <summary>No intercept closer than this to launch.</summary>
        public const int MinInterceptLeadSec = 5;

        // ---------- wings: clanmates' fleets flying with yours ----------

        public sealed class Wing
        {
            public BotEmpire Bot = null!;
            public Dictionary<HullId, int> Ships = new();
            public int TravelSec;
            public int LaunchTick;
        }

        /// <summary>Clanmates who can reach <paramref name="target"/> exactly at
        /// <paramref name="arriveTick"/> (they launch late if they're close):
        /// rested, within StrikeRange, with warships docked — strongest first.</summary>
        public static List<Wing> StrikeWings(GameState player, BotGalaxy galaxy, Position target, int arriveTick,
            int excludeBotId)
        {
            var list = new List<Wing>();
            if (player.ClanId == 0) return list;
            foreach (var bot in galaxy.Bots)
            {
                if (bot.ClanId != player.ClanId || bot.Id == excludeBotId) continue;
                if (bot.SupportReadyTick > player.Tick) continue;
                double dist = Position.DistanceToTile(target, bot.HomeTile);
                if (dist > StrikeRange) continue;
                var ships = ClanSystem.Share(bot, StrikeShare);
                if (ships.Count == 0) continue;
                int travel = Balance.TravelSeconds(dist, Math.Max(1, MarchSystem.FleetSpeed(ships)));
                int launch = arriveTick - travel;
                if (launch < player.Tick) continue; // can't get there with you
                list.Add(new Wing { Bot = bot, Ships = ships, TravelSec = travel, LaunchTick = launch });
            }
            SortStrongestFirst(list);
            int cap = EmbassySystem.StrikeWings(player); // the Clan Embassy adds wings
            if (list.Count > cap) list.RemoveRange(cap, list.Count - cap);
            return list;
        }

        static void SortStrongestFirst(List<Wing> list) => list.Sort((a, b) =>
        {
            int c = BotSystem.EstimateFleetPower(b.Ships).CompareTo(BotSystem.EstimateFleetPower(a.Ships));
            return c != 0 ? c : a.Bot.Id.CompareTo(b.Bot.Id);
        });

        /// <summary>Commit the wings: their ships leave the docks now and fly (or
        /// wait to launch) so every wing lands at <paramref name="arriveTick"/>.</summary>
        public static List<BotMarch> LaunchWings(GameState player, BotGalaxy galaxy, List<Wing> wings,
            BotMarchKind kind, int linkId, int targetBotId, Position target, int arriveTick, int restSec)
        {
            var launched = new List<BotMarch>();
            var to = TileOf(target);
            foreach (var w in wings)
            {
                Take(w.Bot, w.Ships);
                w.Bot.SupportReadyTick = player.Tick + restSec;
                var m = new BotMarch
                {
                    Id = galaxy.NextMarchId++,
                    BotId = w.Bot.Id,
                    TargetBotId = targetBotId,
                    Kind = kind,
                    LinkId = linkId,
                    Ships = new Dictionary<HullId, int>(w.Ships),
                    From = w.Bot.HomeTile,
                    To = to,
                    LaunchTick = w.LaunchTick,
                    ArrivesAtTick = arriveTick,
                };
                galaxy.Marches.Add(m);
                launched.Add(m);
            }
            return launched;
        }

        /// <summary>Wings flying with the player march <paramref name="playerMarchId"/>, not yet settled.</summary>
        public static List<BotMarch> EscortsOf(BotGalaxy galaxy, int playerMarchId)
        {
            var list = new List<BotMarch>();
            foreach (var m in galaxy.Marches)
                if (m.Kind == BotMarchKind.Escort && m.LinkId == playerMarchId && !m.Resolved) list.Add(m);
            return list;
        }

        /// <summary>The strike was called off (recall, target gone): its wings head home untouched.</summary>
        public static void TurnEscortsHome(BotGalaxy galaxy, int playerMarchId, int now)
        {
            foreach (var m in EscortsOf(galaxy, playerMarchId)) TurnHome(galaxy, m, now);
        }

        /// <summary>A wing that never fought heads home from wherever it is (one
        /// still waiting to launch just stays home).</summary>
        public static void TurnHome(BotGalaxy galaxy, BotMarch wing, int now)
        {
            if (wing.Resolved) return;
            if (now <= wing.LaunchTick)
            {
                if (galaxy.Find(wing.BotId) is { } owner) Give(owner, wing.Ships);
                galaxy.Marches.Remove(wing);
                return;
            }
            var here = PositionOf(wing, now);
            wing.To = TileOf(here);
            LeaveAt(wing, now);
        }

        /// <summary>After its battle: survivors (and their plunder) fly home from the target.</summary>
        public static void SendHomeAfterBattle(BotGalaxy galaxy, BotMarch wing, Dictionary<HullId, int> survivors,
            ResourceBag loot, int atTick)
        {
            if (MarchSystem.FleetCount(survivors) == 0) { galaxy.Marches.Remove(wing); return; }
            wing.Ships = new Dictionary<HullId, int>(survivors);
            wing.LootMilli = loot.Clone();
            LeaveAt(wing, atTick);
        }

        /// <summary>Start the homeward leg at <paramref name="atTick"/> from wing.To.</summary>
        static void LeaveAt(BotMarch wing, int atTick)
        {
            wing.Resolved = true;
            wing.ArrivesAtTick = atTick;
            wing.ReturnsAtTick = atTick + Balance.TravelSeconds(TileXY.Distance(wing.To, wing.From),
                Math.Max(1, MarchSystem.FleetSpeed(wing.Ships)));
        }

        /// <summary>Where a bot fleet is at <paramref name="tick"/> (outbound or homeward leg).</summary>
        public static Position PositionOf(BotMarch m, int tick)
        {
            if (m.Resolved) return Lerp(m.To, m.From, m.ArrivesAtTick, m.ReturnsAtTick, tick);
            return Lerp(m.From, m.To, m.LaunchTick, m.ArrivesAtTick, tick);
        }

        static Position Lerp(Position a, Position b, int t0, int t1, int tick)
        {
            int span = t1 - t0;
            double t = span <= 0 ? 1 : Math.Clamp((tick - t0) / (double)span, 0.0, 1.0);
            return new Position(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        public static TileXY TileOf(Position p) => new((int)Math.Round(p.X), (int)Math.Round(p.Y));

        /// <summary>Split plunder in proportion to each line's cargo room; line 0
        /// (the player) absorbs the rounding.</summary>
        public static List<ResourceBag> SplitLoot(ResourceBag total, IReadOnlyList<long> room)
        {
            var shares = new List<ResourceBag>();
            for (int i = 0; i < room.Count; i++) shares.Add(new ResourceBag());
            long sum = 0;
            foreach (var r in room) sum += Math.Max(0, r);
            if (room.Count == 0) return shares;
            if (sum <= 0) { shares[0] = total.Clone(); return shares; }
            long g = 0, q = 0, h = 0;
            for (int i = 1; i < room.Count; i++)
            {
                double f = Math.Max(0, room[i]) / (double)sum;
                shares[i] = new ResourceBag((long)(total.Gold * f), (long)(total.Quartz * f), (long)(total.Helium * f));
                g += shares[i].Gold; q += shares[i].Quartz; h += shares[i].Helium;
            }
            shares[0] = new ResourceBag(total.Gold - g, total.Quartz - q, total.Helium - h);
            return shares;
        }

        // ---------- a raid on a rival colony (RaidArrivals calls this at arrival) ----------

        /// <summary>
        /// Your raid lands: your fleet plus any joint-strike wings against the
        /// garrison plus their clanmates in range. Plunder is split by cargo room;
        /// your share rides your march home (or lands in your stores if offline
        /// catch-up already docked it). Files the report and returns it.
        /// </summary>
        public static BattleMailReport ResolveRaid(GameState state, BotGalaxy galaxy, BotEmpire bot, int marchId,
            Dictionary<HullId, int> sent, TileXY tile)
        {
            var snapshot = BotSystem.SnapshotOf(bot);
            var escorts = EscortsOf(galaxy, marchId);
            var attackLines = new List<Dictionary<HullId, int>> { new Dictionary<HullId, int>(sent) };
            foreach (var e in escorts) attackLines.Add(e.Ships);
            var defenseHelpers = ClanSystem.DefenseHelpers(state, galaxy, bot.Id, 0);
            var defenseLines = new List<Dictionary<HullId, int>> { new Dictionary<HullId, int>(snapshot.Ships) };
            foreach (var (_, ships) in defenseHelpers) defenseLines.Add(ships);

            // RaidPanel's forecast makes the same call at launch — keep the inputs in step.
            var report = CombatResolver.Resolve(ClanSystem.Combine(attackLines), ClanSystem.Combine(defenseLines),
                ResearchSystem.CombatModsFor(state, marchId), ResearchSystem.DefenseMods(bot.State));
            report.Location = tile;
            report.DefenderName = snapshot.CommanderName;
            bool won = report.Winner == BattleWinner.Attacker;

            var attackLosses = ClanSystem.SplitLosses(attackLines, report.AttackerSurvivors);
            var survivors = new List<Dictionary<HullId, int>>();
            for (int i = 0; i < attackLines.Count; i++) survivors.Add(Minus(attackLines[i], attackLosses[i]));

            var loot = new ResourceBag();
            var shares = new List<ResourceBag>();
            for (int i = 0; i < attackLines.Count; i++) shares.Add(new ResourceBag());
            if (won)
            {
                // Every surviving wing hauls; War Games (galaxy event) hauls more.
                var room = new List<long> { MarchSystem.EffCargoCap(state, survivors[0]) };
                for (int i = 1; i < survivors.Count; i++) room.Add(MarchSystem.FleetCargoCap(survivors[i]));
                long cap = 0;
                foreach (var r in room) cap += r;
                cap = (long)(cap * EventSystem.RaidLootMult(state));
                long total = snapshot.LootableMilli.Total;
                double scale = total > 0 ? Math.Min(1.0, cap / (double)total) : 0;
                loot = new ResourceBag(
                    (long)Math.Floor(snapshot.LootableMilli.Gold * scale),
                    (long)Math.Floor(snapshot.LootableMilli.Quartz * scale),
                    (long)Math.Floor(snapshot.LootableMilli.Helium * scale));
                shares = SplitLoot(loot, room);
            }
            var yourLoot = shares[0];
            report.Loot = yourLoot.Clone();

            Dictionary<HullId, int>? allyShips = escorts.Count > 0 ? ClanSystem.Combine(escorts.ConvertAll(e => e.Ships)) : null;
            string? allyNames = escorts.Count > 0 ? NamesOf(galaxy, escorts) : null;
            long allyLoot = 0;
            for (int i = 0; i < escorts.Count; i++)
            {
                allyLoot += shares[i + 1].Total;
                SendHomeAfterBattle(galaxy, escorts[i], survivors[i + 1], shares[i + 1], state.Tick);
            }

            var march = state.Marches.Find(m => m.Id == marchId);
            if (march != null)
            {
                // Flying home — survivors carry your share.
                march.Ships = new Dictionary<HullId, int>(survivors[0]);
                march.Cargo = yourLoot.Clone();
                if (MarchSystem.FleetCount(march.Ships) == 0) state.Marches.RemoveAll(m => m.Id == marchId);
            }
            else
            {
                // Offline catch-up already docked the round trip intact — square it up.
                ClanSystem.Deduct(state.Ships, attackLosses[0]);
                state.Resources.Add(yourLoot);
            }

            // Bounty Board (rival events, 2026-09-30): a win on the marked commander pays.
            var bounty = won ? BountySystem.Claim(state, bot.Id) : null;
            if (won)
            {
                state.Stats.BattlesWon++;
                state.Stats.RaidsWon++;
                state.Stats.LootMilli += yourLoot.Total;
            }
            else state.Stats.BattlesLost++;

            // The defending clanmates take their share of the losses.
            var defenseLosses = ClanSystem.SplitLosses(defenseLines, report.DefenderSurvivors);
            for (int i = 0; i < defenseHelpers.Count; i++)
            {
                ClanSystem.Deduct(defenseHelpers[i].ally.State.Ships, defenseLosses[i + 1]);
                defenseHelpers[i].ally.CachedMight = PowerSystem.ComputePower(defenseHelpers[i].ally.State);
            }

            var mail = new BattleMailReport
            {
                Id = state.NextReportId++,
                AtTick = state.Tick,
                Target = tile,
                Subject = report.Winner switch
                {
                    BattleWinner.Attacker => $"{(escorts.Count > 0 ? "Joint strike" : "Raid")} victory — {snapshot.CommanderName}"
                        + (bounty is { } b ? $" · bounty collected: +{b.pay.Total / 1000:N0} and {b.darkMatter} DM" : ""),
                    BattleWinner.Defender => $"{(escorts.Count > 0 ? "Joint strike" : "Raid")} repelled — {snapshot.CommanderName}",
                    _ => $"{(escorts.Count > 0 ? "Joint strike" : "Raid")} stalemate — {snapshot.CommanderName}",
                },
                Report = report,
                AllyShips = allyShips,
                AllyNames = allyNames,
                AllyLootMilli = allyLoot,
                EnemyAllyShips = defenseHelpers.Count > 0 ? ClanSystem.Combine(defenseHelpers.ConvertAll(h => h.ships)) : null,
                EnemyAllyNames = defenseHelpers.Count > 0 ? string.Join(", ", defenseHelpers.ConvertAll(h => h.ally.Name)) : null,
            };
            BotSystem.InsertMail(state, mail);

            // Their colony takes the hit, burns on the map, and the news carries it.
            BotSystem.ApplyPlayerRaid(galaxy, bot, report, loot, state, defenseLosses[0]);
            return mail;
        }

        // ---------- intercepts ----------

        /// <summary>A rival fleet's current leg, as seen from the player.</summary>
        public sealed class FleetTrack
        {
            /// <summary>True: a BotAttack aimed at the player; false: a BotMarch.</summary>
            public bool Inbound;
            public int FleetId;
            public int BotId;
            /// <summary>Who it's raiding: 0 = you, a bot id, or -1 when it's flying home.</summary>
            public int TargetId;
            /// <summary>An assault on the Galactic Core (outbound).</summary>
            public bool TargetsCore;
            /// <summary>0 = outbound to the target, 1 = homeward with its plunder.</summary>
            public int Leg;
            public Position From, To;
            public int DepartTick, ArriveTick;
            public Dictionary<HullId, int> Ships = new();
            public ResourceBag Loot = new();

            public Position At(int tick) => Lerp(From, To, DepartTick, ArriveTick, tick);
        }

        /// <summary>The fleet to hunt, or null when it's gone, a probe, or not a raid.</summary>
        public static FleetTrack? Track(GameState player, BotGalaxy galaxy, bool inbound, int fleetId)
        {
            if (inbound)
            {
                var a = galaxy.Inbound.Find(x => x.Id == fleetId);
                if (a == null || !a.IsFleet) return null;
                return new FleetTrack
                {
                    Inbound = true, FleetId = a.Id, BotId = a.BotId, TargetId = 0, Leg = 0,
                    From = a.From, To = player.HomeTile, DepartTick = a.LaunchTick, ArriveTick = a.ArrivesAtTick,
                    Ships = a.Ships,
                };
            }
            var m = galaxy.Marches.Find(x => x.Id == fleetId);
            if (m == null || (m.Kind != BotMarchKind.Raid && m.Kind != BotMarchKind.CoreAssault)) return null;
            bool core = m.Kind == BotMarchKind.CoreAssault;
            return m.Resolved
                ? new FleetTrack
                {
                    FleetId = m.Id, BotId = m.BotId, TargetId = -1, Leg = 1,
                    From = m.To, To = m.From, DepartTick = m.ArrivesAtTick, ArriveTick = m.ReturnsAtTick,
                    Ships = m.Ships, Loot = m.LootMilli,
                }
                : new FleetTrack
                {
                    FleetId = m.Id, BotId = m.BotId, TargetId = core ? -1 : m.TargetBotId, Leg = 0,
                    TargetsCore = core,
                    From = m.From, To = m.To, DepartTick = m.LaunchTick, ArriveTick = m.ArrivesAtTick,
                    Ships = m.Ships,
                };
        }

        public sealed class InterceptPlan
        {
            public bool Ok;
            public string? Reason;
            public int EngageTick;
            public Position Point;
            /// <summary>Milli-helium (round trip).</summary>
            public int HeliumCost;
        }

        /// <summary>The earliest point on the fleet's path your <paramref name="ships"/>
        /// can reach before it does (before it lands, or before it gets home).</summary>
        public static InterceptPlan PlanIntercept(GameState player, FleetTrack track, Dictionary<HullId, int> ships)
        {
            var plan = new InterceptPlan();
            if (MarchSystem.FleetCount(ships) < 1) { plan.Reason = "No ships selected"; return plan; }
            int now = player.Tick;
            for (int t = Math.Max(now + MinInterceptLeadSec, track.DepartTick); t < track.ArriveTick; t++)
            {
                var p = track.At(t);
                if (now + MarchSystem.FlightSeconds(player, ships, p) > t) continue;
                var preview = MarchSystem.PreviewMarch(player, ships, TileOf(p));
                if (!preview.Ok) { plan.Reason = preview.Reason; return plan; }
                plan.Ok = true;
                plan.EngageTick = t;
                plan.Point = p;
                plan.HeliumCost = preview.HeliumCost;
                return plan;
            }
            plan.Reason = track.Leg == 0
                ? "Your fleet can't reach their path before they land"
                : "Your fleet can't catch them before they're home";
            return plan;
        }

        public static SimResult CanIntercept(GameState player, BotGalaxy galaxy, FleetTrack track)
        {
            if (galaxy.Find(track.BotId) is { } owner && ClanSystem.SameClanAsPlayer(player, owner))
                return SimResult.Fail("That's your clanmate's fleet");
            foreach (var m in player.Marches)
                if (m.Mission == MarchMission.Intercept && !m.Recalled && m.Phase != MarchPhase.Returning
                    && m.TargetFleetId == track.FleetId && m.TargetInbound == track.Inbound)
                    return SimResult.Fail("You're already intercepting that fleet");
            return SimResult.Success;
        }

        /// <summary>Launch an intercept (with joint-strike wings when <paramref name="joint"/>).</summary>
        public static SimResult LaunchIntercept(GameState player, BotGalaxy galaxy, FleetTrack track,
            Dictionary<HullId, int> ships, bool joint, out int marchId, out int wings)
        {
            marchId = 0;
            wings = 0;
            var can = CanIntercept(player, galaxy, track);
            if (!can.Ok) return can;
            var plan = PlanIntercept(player, track, ships);
            if (!plan.Ok) return SimResult.Fail(plan.Reason ?? "Can't intercept that fleet");
            var sent = MarchSystem.SendFlight(player, ships, plan.Point, plan.EngageTick, MarchMission.Intercept, out var march);
            if (!sent.Ok || march == null) return sent;
            march.TargetFleetId = track.FleetId;
            march.TargetInbound = track.Inbound;
            march.TargetLeg = track.Leg;
            march.EngageTick = plan.EngageTick;
            marchId = march.Id;
            if (joint)
                wings = LaunchWings(player, galaxy, StrikeWings(player, galaxy, plan.Point, plan.EngageTick, track.BotId),
                    BotMarchKind.Escort, march.Id, 0, plan.Point, plan.EngageTick, ClanSystem.RaidSupportCooldownSec).Count;
            return SimResult.Success;
        }

        static void ResolveIntercepts(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            foreach (var march in new List<March>(player.Marches))
            {
                if (march.Mission != MarchMission.Intercept || march.Recalled) continue;
                if (march.Phase == MarchPhase.Returning || march.EngageTick > player.Tick) continue;
                ResolveIntercept(player, galaxy, events, march);
            }
        }

        static void ResolveIntercept(GameState player, BotGalaxy galaxy, SimEventBus events, March march)
        {
            int at = march.EngageTick;
            // Where the fleets meet — read before the march is sent home (that rewrites its legs).
            var point = march.LegTo;
            var pointTile = TileOf(point);
            var escorts = EscortsOf(galaxy, march.Id);
            var track = Track(player, galaxy, march.TargetInbound, march.TargetFleetId);
            var enemy = track != null ? galaxy.Find(track.BotId) : null;
            bool there = track != null && enemy != null && track.Leg == march.TargetLeg
                && at >= track.DepartTick && at < track.ArriveTick
                && Position.Distance(track.At(at), point) <= InterceptTolerance;
            if (!there)
            {
                // It changed course (its target moved, someone else broke it) — nothing to fight.
                foreach (var e in escorts) TurnHome(galaxy, e, at);
                MarchSystem.ReturnHome(player, march, at);
                events.Emit(new InterceptMissed(enemy?.Name ?? "the fleet"));
                return;
            }

            var lines = new List<Dictionary<HullId, int>> { new Dictionary<HullId, int>(march.Ships) };
            foreach (var e in escorts) lines.Add(e.Ships);
            // Open space: no batteries, no Defense branch — each side fights with its fleet research.
            var report = CombatResolver.Resolve(ClanSystem.Combine(lines), new Dictionary<HullId, int>(track!.Ships),
                ResearchSystem.CombatMods(player), ResearchSystem.CombatMods(enemy!.State));
            report.Location = pointTile;
            report.DefenderName = enemy.Name;
            bool won = report.Winner == BattleWinner.Attacker;
            // A draw still breaks an outbound raid: it turns for home.
            bool broken = report.Winner != BattleWinner.Defender;

            var losses = ClanSystem.SplitLosses(lines, report.AttackerSurvivors);
            var survivors = new List<Dictionary<HullId, int>>();
            for (int i = 0; i < lines.Count; i++) survivors.Add(Minus(lines[i], losses[i]));

            // Plunder retaken from a raider flying home.
            var retaken = new ResourceBag();
            var shares = new List<ResourceBag>();
            for (int i = 0; i < lines.Count; i++) shares.Add(new ResourceBag());
            if (won && track.Loot.Total > 0)
            {
                var room = new List<long> { MarchSystem.FreeCargo(player, new March { Ships = survivors[0], Cargo = march.Cargo }) };
                for (int i = 1; i < survivors.Count; i++) room.Add(MarchSystem.FleetCargoCap(survivors[i]));
                long cap = 0;
                foreach (var r in room) cap += r;
                double scale = Math.Min(1.0, cap / (double)track.Loot.Total);
                retaken = new ResourceBag(
                    (long)Math.Floor(track.Loot.Gold * scale),
                    (long)Math.Floor(track.Loot.Quartz * scale),
                    (long)Math.Floor(track.Loot.Helium * scale));
                shares = SplitLoot(retaken, room);
            }
            report.Loot = shares[0].Clone();

            Dictionary<HullId, int>? allyShips = escorts.Count > 0 ? ClanSystem.Combine(escorts.ConvertAll(e => e.Ships)) : null;
            string? allyNames = escorts.Count > 0 ? NamesOf(galaxy, escorts) : null;
            long allyLoot = 0;
            for (int i = 0; i < escorts.Count; i++)
            {
                allyLoot += shares[i + 1].Total;
                SendHomeAfterBattle(galaxy, escorts[i], survivors[i + 1], shares[i + 1], at);
            }

            march.Ships = new Dictionary<HullId, int>(survivors[0]);
            march.Cargo.Add(shares[0]);
            if (MarchSystem.FleetCount(march.Ships) == 0) player.Marches.Remove(march);
            else MarchSystem.ReturnHome(player, march, at);

            ApplyToHunted(player, galaxy, track, report.DefenderSurvivors, broken, retaken, point, at);

            if (won) { player.Stats.BattlesWon++; player.Stats.LootMilli += shares[0].Total; }
            else player.Stats.BattlesLost++;
            ClanSystem.RecordBattle(player, galaxy, 0, enemy.Id, won);
            // They know who broke their raid.
            enemy.FocusTargetId = 0;
            enemy.FocusSetTick = at;

            string victim = track.TargetsCore ? "the Galactic Core" : track.TargetId switch
            {
                0 => "your colony",
                > 0 => galaxy.Find(track.TargetId)?.Name ?? "their target",
                _ => "",
            };
            string raid = track.TargetsCore ? "assault" : "raid";
            galaxy.AddBulletin(at, broken
                ? $"{ClanSystem.Tagged(player, galaxy, 0, player.Profile.Name)} intercepted {ClanSystem.Tagged(player, galaxy, enemy.Id, enemy.Name)}'s fleet"
                : $"{ClanSystem.Tagged(player, galaxy, enemy.Id, enemy.Name)}'s fleet fought through an intercept");
            BotSystem.InsertMail(player, new BattleMailReport
            {
                Id = player.NextReportId++,
                AtTick = at,
                Target = pointTile,
                Subject = won
                    ? track.Leg == 1 ? $"Intercept — plunder retaken from {enemy.Name}" : $"Intercept — {enemy.Name}'s {raid} on {victim} broken"
                    : broken ? $"Intercept — {enemy.Name}'s {raid} turned back"
                    : $"Intercept failed — {enemy.Name}'s fleet held",
                Report = report,
                AllyShips = allyShips,
                AllyNames = allyNames,
                AllyLootMilli = allyLoot,
            });
            events.Emit(new BattleResolved(report));
        }

        /// <summary>What's left of the hunted fleet: a broken raid turns for home
        /// (an inbound one limps straight back to its dock); a raider flying home
        /// keeps going, lighter by the plunder retaken.</summary>
        static void ApplyToHunted(GameState player, BotGalaxy galaxy, FleetTrack track, Dictionary<HullId, int> left,
            bool broken, ResourceBag retaken, Position where, int at)
        {
            var owner = galaxy.Find(track.BotId);
            bool alive = MarchSystem.FleetCount(left) > 0;
            if (track.Inbound)
            {
                var atk = galaxy.Inbound.Find(a => a.Id == track.FleetId);
                if (atk == null) return;
                if (!alive || broken)
                {
                    galaxy.Inbound.Remove(atk);
                    if (alive && owner != null) Give(owner, left);
                }
                else atk.Ships = new Dictionary<HullId, int>(left);
                return;
            }
            var m = galaxy.Marches.Find(x => x.Id == track.FleetId);
            if (m == null) return;
            if (!alive) { galaxy.Marches.Remove(m); return; }
            m.Ships = new Dictionary<HullId, int>(left);
            if (track.Leg == 1)
            {
                m.LootMilli = new ResourceBag(
                    Math.Max(0, m.LootMilli.Gold - retaken.Gold),
                    Math.Max(0, m.LootMilli.Quartz - retaken.Quartz),
                    Math.Max(0, m.LootMilli.Helium - retaken.Helium));
            }
            else if (broken)
            {
                // Turned back mid-flight: the homeward leg starts at the intercept point.
                m.To = TileOf(where);
                m.LootMilli = new ResourceBag();
                LeaveAt(m, at);
            }
        }

        // ---------- garrisons ----------

        /// <summary>Clanmates who'd garrison your colony now: rested, within
        /// GarrisonRange, with warships docked — strongest first.</summary>
        public static List<Wing> GarrisonCandidates(GameState player, BotGalaxy galaxy)
        {
            var list = new List<Wing>();
            if (player.ClanId == 0) return list;
            foreach (var bot in galaxy.Bots)
            {
                if (bot.ClanId != player.ClanId || bot.SupportReadyTick > player.Tick) continue;
                double dist = TileXY.Distance(bot.HomeTile, player.HomeTile);
                if (dist > GarrisonRange) continue;
                var ships = ClanSystem.Share(bot, GarrisonShare);
                if (ships.Count == 0) continue;
                int travel = Balance.TravelSeconds(dist, Math.Max(1, MarchSystem.FleetSpeed(ships)));
                list.Add(new Wing { Bot = bot, Ships = ships, TravelSec = travel, LaunchTick = player.Tick });
            }
            SortStrongestFirst(list);
            if (list.Count > MaxGarrisonWings) list.RemoveRange(MaxGarrisonWings, list.Count - MaxGarrisonWings);
            return list;
        }

        public static SimResult CanRequestGarrison(GameState player, BotGalaxy galaxy)
        {
            if (player.ClanId == 0) return SimResult.Fail("Join a clan first");
            if (player.Tick < player.ClanGarrisonReadyTick) return SimResult.Fail("Your clan answered a call recently");
            if (GarrisonCandidates(player, galaxy).Count == 0)
                return SimResult.Fail($"No clanmates within {GarrisonRange:N0} tiles are free to garrison you");
            return SimResult.Success;
        }

        /// <summary>Clanmates send garrison wings to your colony; each stands guard
        /// GarrisonHoldSec after it lands, then flies home.</summary>
        public static SimResult RequestGarrison(GameState player, BotGalaxy galaxy, out int wings)
        {
            wings = 0;
            var can = CanRequestGarrison(player, galaxy);
            if (!can.Ok) return can;
            foreach (var w in GarrisonCandidates(player, galaxy))
            {
                // Busy for the whole watch — out, on guard, and back.
                var launched = LaunchWings(player, galaxy, new List<Wing> { w }, BotMarchKind.Garrison, 0, 0,
                    player.HomeTile, player.Tick + w.TravelSec, 2 * w.TravelSec + GarrisonHoldSec);
                wings += launched.Count;
            }
            player.ClanGarrisonReadyTick = player.Tick + GarrisonRequestCooldownSec;
            return SimResult.Success;
        }

        /// <summary>Clan garrison wings bound for or standing at your colony.</summary>
        public static List<BotMarch> GarrisonAtPlayer(BotGalaxy galaxy)
        {
            var list = new List<BotMarch>();
            foreach (var m in galaxy.Marches)
                if (m.Kind == BotMarchKind.Garrison && m.LinkId == 0 && !m.Resolved) list.Add(m);
            return list;
        }

        /// <summary>Clan garrison wings standing at your colony at <paramref name="tick"/>
        /// — landed, and their watch not yet over (a raid resolved during offline
        /// catch-up can be hours older than now).</summary>
        public static List<BotMarch> StationedAtPlayer(BotGalaxy galaxy, int tick)
        {
            var list = GarrisonAtPlayer(galaxy);
            list.RemoveAll(m => m.ArrivesAtTick > tick || tick >= m.ArrivesAtTick + GarrisonHoldSec);
            return list;
        }

        public static SimResult CanSendGarrison(GameState player, BotGalaxy galaxy, BotEmpire host)
        {
            if (!ClanSystem.SameClanAsPlayer(player, host)) return SimResult.Fail("You can only garrison a clanmate's colony");
            foreach (var m in player.Marches)
                if (m.Mission == MarchMission.Garrison && m.GuardEmpireId == host.Id && m.Phase != MarchPhase.Returning)
                    return SimResult.Fail($"You already have a garrison at {host.Name}'s colony");
            return SimResult.Success;
        }

        /// <summary>Station a fleet at a clanmate's colony until you recall it.</summary>
        public static SimResult SendGarrison(GameState player, BotGalaxy galaxy, int hostBotId,
            Dictionary<HullId, int> ships, out int marchId)
        {
            marchId = 0;
            var host = galaxy.Find(hostBotId);
            if (host == null) return SimResult.Fail("That colony is gone");
            var can = CanSendGarrison(player, galaxy, host);
            if (!can.Ok) return can;
            if (MarchSystem.FleetCount(ships) < 1) return SimResult.Fail("No ships selected");
            int arrive = player.Tick + MarchSystem.FlightSeconds(player, ships, host.HomeTile);
            var sent = MarchSystem.SendFlight(player, ships, host.HomeTile, arrive, MarchMission.Garrison, out var march);
            if (!sent.Ok || march == null) return sent;
            march.GuardEmpireId = hostBotId;
            marchId = march.Id;
            return SimResult.Success;
        }

        /// <summary>Your garrisons standing at <paramref name="host"/>'s colony at
        /// <paramref name="tick"/> (landed by then, not recalled).</summary>
        public static List<March> PlayerGarrisonsAt(GameState player, BotEmpire host, int tick)
        {
            var list = new List<March>();
            foreach (var m in player.Marches)
                if (m.Mission == MarchMission.Garrison && m.GuardEmpireId == host.Id
                    && m.Phase == MarchPhase.Gathering && m.Node.Equals(host.HomeTile)
                    && m.DepartedAtTick <= tick) // Hold stamps the landing tick here
                    list.Add(m);
            return list;
        }

        /// <summary>Your garrisons fought a raid on <paramref name="host"/>: losses,
        /// the report (the host and their helpers are the clan side), and a toast.</summary>
        public static void SettlePlayerGarrisons(GameState player, BotEmpire host, BotEmpire attacker,
            BattleReport report, List<March> guards, List<Dictionary<HullId, int>> guardLosses,
            Dictionary<HullId, int> clanShips, string clanNames, int atTick, SimEventBus events)
        {
            if (guards.Count == 0) return;
            for (int i = 0; i < guards.Count; i++)
            {
                ClanSystem.Deduct(guards[i].Ships, guardLosses[i]);
                if (MarchSystem.FleetCount(guards[i].Ships) == 0) player.Marches.Remove(guards[i]);
            }
            bool held = report.Winner != BattleWinner.Attacker;
            if (held) { player.Stats.BattlesWon++; player.Stats.DefensesWon++; }
            else player.Stats.BattlesLost++;
            BotSystem.InsertMail(player, new BattleMailReport
            {
                Id = player.NextReportId++,
                AtTick = atTick,
                Target = host.HomeTile,
                Subject = held
                    ? $"Your garrison held {host.Name}'s colony against {attacker.Name}"
                    : $"{attacker.Name} broke through your garrison at {host.Name}'s colony",
                Defending = true,
                AttackerBotId = attacker.Id,
                GuardedBotId = host.Id,
                Report = report,
                AllyShips = MarchSystem.FleetCount(clanShips) > 0 ? clanShips : null,
                AllyNames = MarchSystem.FleetCount(clanShips) > 0 ? clanNames : null,
            });
            events.Emit(new GarrisonFought(host.Name, attacker.Name, held));
        }

        // ---------- the clock ----------

        // BotSystem.Advance calls BeforeRaids, resolves the raids that landed, then
        // AfterRaids. Offline catch-up runs one Advance for hours of galaxy time,
        // so the order matters: an intercept always fights before the raid it was
        // sent to stop, and a garrison's watch only ends after every raid that
        // landed during it has been fought (StationedAtPlayer checks the window).

        /// <summary>Intercepts fight; wings whose strike was called off go home.</summary>
        public static void BeforeRaids(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            int now = player.Tick;
            ResolveIntercepts(player, galaxy, events);

            for (int i = galaxy.Marches.Count - 1; i >= 0; i--)
            {
                if (i >= galaxy.Marches.Count) continue;
                var m = galaxy.Marches[i];
                if (m.Resolved || m.Kind != BotMarchKind.Escort) continue;
                bool orphan;
                if (m.TargetBotId <= 0)
                {
                    // An intercept or core-assault wing follows its march: recalled or gone → home.
                    var lead = player.Marches.Find(x => x.Id == m.LinkId);
                    var mission = m.TargetBotId == 0 ? MarchMission.Intercept : MarchMission.Core;
                    orphan = lead == null || lead.Recalled || lead.Mission != mission
                        || lead.Phase == MarchPhase.Returning;
                }
                // A joint-strike wing is settled by the raid (RaidArrivals); this is the net.
                else orphan = now > m.ArrivesAtTick + EscortWaitLimitSec;
                if (orphan) TurnHome(galaxy, m, now);
            }
        }

        /// <summary>Garrisons change the guard: clan wings arrive and, their watch
        /// over, fly home; yours follow their hosts.</summary>
        public static void AfterRaids(GameState player, BotGalaxy galaxy, SimEventBus events, int prevTick)
        {
            int now = player.Tick;
            int landed = 0;
            for (int i = galaxy.Marches.Count - 1; i >= 0; i--)
            {
                if (i >= galaxy.Marches.Count) continue;
                var m = galaxy.Marches[i];
                if (m.Resolved || m.Kind != BotMarchKind.Garrison || m.LinkId != 0) continue;
                var owner = galaxy.Find(m.BotId);
                if (owner == null || !ClanSystem.SameClanAsPlayer(player, owner) || !m.To.Equals(player.HomeTile))
                {
                    TurnHome(galaxy, m, now); // you left the clan, they did, or you moved
                    continue;
                }
                int watchEnds = m.ArrivesAtTick + GarrisonHoldSec;
                if (m.ArrivesAtTick > prevTick && m.ArrivesAtTick <= now && now < watchEnds) landed++;
                if (now >= watchEnds) LeaveAt(m, watchEnds);
            }
            if (landed > 0) events.Emit(new ClanGarrisonArrived(landed));

            // Your garrisons follow their hosts: one who left the clan or moved away sends them home.
            foreach (var march in new List<March>(player.Marches))
            {
                if (march.Mission != MarchMission.Garrison || march.Phase == MarchPhase.Returning) continue;
                var host = galaxy.Find(march.GuardEmpireId);
                if (host != null && ClanSystem.SameClanAsPlayer(player, host) && host.HomeTile.Equals(march.Node)) continue;
                if (march.Phase == MarchPhase.Gathering) MarchSystem.ReturnHome(player, march, now);
                else MarchSystem.RecallMarch(player, march.Id);
            }
        }

        // ---------- bits ----------

        static string NamesOf(BotGalaxy galaxy, List<BotMarch> wings)
        {
            var names = new List<string>();
            foreach (var w in wings)
                if (galaxy.Find(w.BotId) is { } b && !names.Contains(b.Name)) names.Add(b.Name);
            return string.Join(", ", names);
        }

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

        static void Take(BotEmpire bot, Dictionary<HullId, int> ships)
        {
            foreach (var kv in ships)
                bot.State.Ships[kv.Key] = Math.Max(0, (bot.State.Ships.TryGetValue(kv.Key, out var n) ? n : 0) - kv.Value);
            bot.CachedMight = PowerSystem.ComputePower(bot.State);
        }

        static void Give(BotEmpire bot, Dictionary<HullId, int> ships)
        {
            foreach (var kv in ships)
                bot.State.Ships[kv.Key] = (bot.State.Ships.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            bot.CachedMight = PowerSystem.ComputePower(bot.State);
        }
    }
}
