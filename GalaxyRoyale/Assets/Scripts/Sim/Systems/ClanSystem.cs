// Clans (user request 2026-09-28, "max limit to 15 per alliance group"):
// groups of up to 15 commanders. Clanmates never raid each other; when one is
// raided, up to three clanmates within reinforcement range send warships (the
// rivals' clans too); joint strikes, intercepts and garrisons (StrikeSystem)
// bring clanmates' fleets into the player's own fights; a clan pays its
// members a daily supply run; and clans go to war — three-day feuds scored by
// battles won against the enemy, carried on the news wire.
//
// The simulated commanders run their own politics every six hours of galaxy
// time: independents join clans, clans form and fold, wars start and end, and
// a clan near the player may send an invitation. Lone wolves (the most warlike
// commanders) never join anyone. Everything is deterministic in the galaxy
// seed and galaxy time, so the offline catch-up plays out the same politics.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Sim.Systems
{
    public static class ClanSystem
    {
        public const int MaxMembers = 15;
        public const int PoliticsIntervalSec = 6 * 3600;
        public const int WarDurationSec = 3 * 24 * 3600;
        public const int WarCooldownSec = 2 * 24 * 3600;
        public const int WarWinRewardDM = 100;
        /// <summary>Clanmates this close (tiles, home to home) answer a call for help.</summary>
        public const double ReinforceRange = 400;
        public const int MaxReinforcers = 3;
        /// <summary>Share of each docked warship hull a helping clanmate sends.</summary>
        public const double ReinforceFraction = 0.15;
        /// <summary>A clanmate who flew a joint strike or intercept with you rests this long.</summary>
        public const int RaidSupportCooldownSec = 6 * 3600;
        public const int SupplyIntervalSec = 24 * 3600;
        /// <summary>Uncollected supply runs that pile up before the clan stops sending.</summary>
        public const int MaxStoredRuns = 3;
        /// <summary>Each bot member's share of a supply run, per its Command Center level.</summary>
        public const int SupplyGoldPerCc = 75, SupplyQuartzPerCc = 50, SupplyHeliumPerCc = 20;
        /// <summary>A member never chips in more than this share of its own stock.</summary>
        public const double SupplyStockShare = 0.05;
        /// <summary>Commanders more warlike than this never join a clan.</summary>
        public const double MaxAggression = 0.75;
        /// <summary>Commanders this many times your might won't join the clan you lead.</summary>
        public const double MaxMightRatio = 3.0;
        /// <summary>To join a clan you need this share of its average member's might.</summary>
        public const double MinMightShare = 0.25;
        /// <summary>Founding a clan costs this (whole units).</summary>
        public static readonly ResourceBag FoundCost = new(1500, 1000, 0);
        public const int SeedClanCount = 14;
        public const int MaxBotClans = 20;
        /// <summary>How far (tiles) a clan recruits from, and where it looks for rivals.</summary>
        public const double RecruitRange = 450;
        public const double RivalRange = 700;
        public const int InviteLifetimeSec = 48 * 3600;
        public const int InviteCooldownSec = 24 * 3600;
        /// <summary>Commanders at war roll for attacks this much more eagerly…</summary>
        public const double WarFever = 1.6;
        /// <summary>…and rank an enemy clan's members this much richer as targets.</summary>
        public const double WarTargetWeight = 3.0;

        // ---------- membership ----------

        public static int ClanOf(GameState player, BotGalaxy galaxy, int empireId) =>
            empireId == 0 ? player.ClanId : galaxy.Find(empireId)?.ClanId ?? 0;

        public static bool SameClan(BotEmpire a, BotEmpire b) => a.ClanId != 0 && a.ClanId == b.ClanId;

        public static bool SameClanAsPlayer(GameState player, BotEmpire bot) =>
            bot.ClanId != 0 && bot.ClanId == player.ClanId;

        public static bool IsLoneWolf(int seed, BotEmpire bot) =>
            BotSystem.PersonalityOf(seed, bot.Id).Aggression > MaxAggression;

        public static List<BotEmpire> BotMembers(BotGalaxy galaxy, int clanId)
        {
            var list = new List<BotEmpire>();
            if (clanId == 0) return list;
            foreach (var bot in galaxy.Bots) if (bot.ClanId == clanId) list.Add(bot);
            return list;
        }

        public static int MemberCount(GameState player, BotGalaxy galaxy, int clanId) =>
            BotMembers(galaxy, clanId).Count + (clanId != 0 && player.ClanId == clanId ? 1 : 0);

        /// <summary>Sum of the members' might (the player's included when a member).</summary>
        public static long ClanMight(GameState player, BotGalaxy galaxy, int clanId)
        {
            long sum = 0;
            foreach (var bot in BotMembers(galaxy, clanId)) sum += bot.CachedMight;
            if (clanId != 0 && player.ClanId == clanId) sum += PowerSystem.ComputePower(player);
            return sum;
        }

        public static Clan? PlayerClan(GameState player, BotGalaxy galaxy) => galaxy.FindClan(player.ClanId);

        public static bool PlayerLeads(GameState player, BotGalaxy galaxy) =>
            PlayerClan(player, galaxy) is { LeaderId: 0 };

        /// <summary>"[VOID] Moon Moon" — or just the name when independent.</summary>
        public static string Tagged(GameState player, BotGalaxy galaxy, int empireId, string name)
        {
            var clan = galaxy.FindClan(ClanOf(player, galaxy, empireId));
            return clan != null ? $"[{clan.Tag}] {name}" : name;
        }

        public static string Label(Clan clan) => $"[{clan.Tag}] {clan.Name}";

        public static bool AtWarWith(BotGalaxy galaxy, int clanA, int clanB) =>
            clanA != 0 && clanB != 0 && galaxy.FindClan(clanA) is { } a && a.WarWithClanId == clanB;

        static TileXY HomeOf(GameState player, BotEmpire? bot) => bot?.HomeTile ?? player.HomeTile;

        // ---------- the player's moves ----------

        public static SimResult CanFound(GameState player, BotGalaxy galaxy, string name, string tag)
        {
            if (player.ClanId != 0) return SimResult.Fail("You're already in a clan — leave it first");
            if (PowerSystem.ComputePower(player) < BotSystem.PlayerShieldMight)
                return SimResult.Fail($"Reach {BotSystem.PlayerShieldMight:N0} might to found a clan");
            name = name.Trim();
            tag = tag.Trim().ToUpperInvariant();
            if (name.Length < 3 || name.Length > 24) return SimResult.Fail("Clan names are 3–24 characters");
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) && c < 128) && c != ' ' && c != '-' && c != '\'')
                    return SimResult.Fail("Use letters, digits, spaces, - and ' in the name");
            if (tag.Length < 2 || tag.Length > 4) return SimResult.Fail("Tags are 2–4 letters or digits");
            foreach (char c in tag)
                if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                    return SimResult.Fail("Tags are letters and digits only");
            foreach (var clan in galaxy.Clans)
            {
                if (clan.Tag == tag) return SimResult.Fail($"[{tag}] is taken");
                if (string.Equals(clan.Name, name, StringComparison.OrdinalIgnoreCase))
                    return SimResult.Fail($"\"{name}\" is taken");
            }
            if (!ResourceSystem.CanAfford(player, FoundCost.Milli()))
                return SimResult.Fail($"Founding costs {FoundCost.Gold:N0} gold and {FoundCost.Quartz:N0} quartz");
            return SimResult.Success;
        }

        public static SimResult Found(GameState player, BotGalaxy galaxy, string name, string tag)
        {
            var ok = CanFound(player, galaxy, name, tag);
            if (!ok.Ok) return ok;
            ResourceSystem.Spend(player, FoundCost.Milli());
            var clan = new Clan
            {
                Id = galaxy.NextClanId++,
                Name = name.Trim(),
                Tag = tag.Trim().ToUpperInvariant(),
                LeaderId = 0,
                FoundedTick = player.Tick,
            };
            galaxy.Clans.Add(clan);
            Enter(player, galaxy, clan);
            galaxy.AddBulletin(player.Tick, $"{player.Profile.Name} founded {Label(clan)}");
            return SimResult.Success;
        }

        /// <summary>Would this clan take the player right now? (Reason when not.)</summary>
        public static SimResult CanJoin(GameState player, BotGalaxy galaxy, Clan clan)
        {
            if (player.ClanId != 0) return SimResult.Fail("You're already in a clan — leave it first");
            var members = BotMembers(galaxy, clan.Id);
            if (members.Count >= MaxMembers) return SimResult.Fail($"[{clan.Tag}] is full ({MaxMembers}/{MaxMembers})");
            long mine = PowerSystem.ComputePower(player);
            if (mine < BotSystem.PlayerShieldMight)
                return SimResult.Fail($"Clans take established empires — reach {BotSystem.PlayerShieldMight:N0} might");
            long total = 0;
            foreach (var bot in members)
            {
                if (BotSystem.HoldsGrudge(bot, player.Tick))
                    return SimResult.Fail($"{bot.Name} of [{clan.Tag}] hasn't forgiven you");
                total += bot.CachedMight;
            }
            if (members.Count > 0 && mine < total / members.Count * MinMightShare)
                return SimResult.Fail($"[{clan.Tag}] won't take a colony that small yet");
            return SimResult.Success;
        }

        public static SimResult Join(GameState player, BotGalaxy galaxy, int clanId)
        {
            var clan = galaxy.FindClan(clanId);
            if (clan == null) return SimResult.Fail("That clan is gone");
            var ok = CanJoin(player, galaxy, clan);
            if (!ok.Ok) return ok;
            Enter(player, galaxy, clan);
            return SimResult.Success;
        }

        public static SimResult DeclineInvite(GameState player)
        {
            if (player.ClanInviteId == 0) return SimResult.Fail("No invitation waiting");
            player.ClanInviteId = 0;
            player.ClanNextInviteTick = player.Tick + InviteCooldownSec;
            return SimResult.Success;
        }

        static void Enter(GameState player, BotGalaxy galaxy, Clan clan)
        {
            player.ClanId = clan.Id;
            player.ClanInviteId = 0;
            player.ClanSupplyNextTick = player.Tick + SupplyIntervalSec;
            player.ClanSupplyPending = new ResourceBag();
            player.ClanSupplyRuns = 0;
            TrackClanSize(player, galaxy);
        }

        /// <summary>Leave the clan. Supplies already sent are banked; a leader hands
        /// the clan to its strongest member (a clan of one simply folds).</summary>
        public static SimResult Leave(GameState player, BotGalaxy galaxy)
        {
            var clan = PlayerClan(player, galaxy);
            if (clan == null) return SimResult.Fail("You're not in a clan");
            player.Resources.Add(player.ClanSupplyPending);
            player.ClanSupplyPending = new ResourceBag();
            player.ClanSupplyRuns = 0;
            player.ClanId = 0;
            player.ClanNextInviteTick = player.Tick + InviteCooldownSec;
            if (clan.LeaderId == 0)
            {
                var members = BotMembers(galaxy, clan.Id);
                if (members.Count == 0) Disband(galaxy, clan);
                else
                {
                    members.Sort((a, b) => b.CachedMight.CompareTo(a.CachedMight));
                    clan.LeaderId = members[0].Id;
                }
            }
            return SimResult.Success;
        }

        public static SimResult CanInvite(GameState player, BotGalaxy galaxy, BotEmpire bot)
        {
            var clan = PlayerClan(player, galaxy);
            if (clan == null || clan.LeaderId != 0) return SimResult.Fail("Only a clan's leader can invite");
            if (MemberCount(player, galaxy, clan.Id) >= MaxMembers)
                return SimResult.Fail($"[{clan.Tag}] is full ({MaxMembers}/{MaxMembers})");
            if (bot.ClanId == clan.Id) return SimResult.Fail($"{bot.Name} is already in [{clan.Tag}]");
            if (galaxy.FindClan(bot.ClanId) is { } theirs)
                return SimResult.Fail($"{bot.Name} flies with [{theirs.Tag}]");
            if (IsLoneWolf(player.Seed, bot)) return SimResult.Fail($"{bot.Name} is a lone wolf — no clans");
            if (BotSystem.HoldsGrudge(bot, player.Tick)) return SimResult.Fail($"{bot.Name} hasn't forgiven you");
            if (galaxy.Inbound.Exists(a => a.BotId == bot.Id && a.IsFleet))
                return SimResult.Fail($"{bot.Name}'s fleet is already on its way to you");
            if (bot.CachedMight > PowerSystem.ComputePower(player) * MaxMightRatio)
                return SimResult.Fail($"{bot.Name} doesn't see you as an equal yet");
            return SimResult.Success;
        }

        public static SimResult Invite(GameState player, BotGalaxy galaxy, int botId)
        {
            var bot = galaxy.Find(botId);
            if (bot == null) return SimResult.Fail("Unknown commander");
            var ok = CanInvite(player, galaxy, bot);
            if (!ok.Ok) return ok;
            bot.ClanId = player.ClanId;
            bot.SpyBackAtTick = 0;
            if (bot.FocusTargetId == 0) bot.FocusTargetId = -1; // clanmates don't hunt clanmates
            TrackClanSize(player, galaxy);
            return SimResult.Success;
        }

        /// <summary>Leader only: the kicked commander takes it personally.</summary>
        public static SimResult Kick(GameState player, BotGalaxy galaxy, int botId)
        {
            var clan = PlayerClan(player, galaxy);
            if (clan == null || clan.LeaderId != 0) return SimResult.Fail("Only a clan's leader can remove members");
            var bot = galaxy.Find(botId);
            if (bot == null || bot.ClanId != clan.Id) return SimResult.Fail("They're not in your clan");
            bot.ClanId = 0;
            bot.FocusTargetId = 0;
            bot.FocusSetTick = player.Tick;
            return SimResult.Success;
        }

        public static SimResult CanDeclareWar(GameState player, BotGalaxy galaxy, Clan target)
        {
            var clan = PlayerClan(player, galaxy);
            if (clan == null || clan.LeaderId != 0) return SimResult.Fail("Only a clan's leader can declare war");
            if (target.Id == clan.Id) return SimResult.Fail("That's your own clan");
            if (clan.WarWithClanId != 0) return SimResult.Fail("Your clan is already at war");
            if (player.Tick < clan.WarCooldownUntilTick)
                return SimResult.Fail("Your clan needs a breather before the next war");
            if (target.WarWithClanId != 0) return SimResult.Fail($"[{target.Tag}] is already at war");
            if (player.Tick < target.WarCooldownUntilTick) return SimResult.Fail($"[{target.Tag}] just ended a war");
            return SimResult.Success;
        }

        public static SimResult DeclareWar(GameState player, BotGalaxy galaxy, int targetClanId)
        {
            var target = galaxy.FindClan(targetClanId);
            if (target == null) return SimResult.Fail("That clan is gone");
            var ok = CanDeclareWar(player, galaxy, target);
            if (!ok.Ok) return ok;
            StartWar(galaxy, PlayerClan(player, galaxy)!, target, player.Tick);
            return SimResult.Success;
        }

        // ---------- supplies ----------

        /// <summary>One bot member's share of a supply run, milli (not yet deducted).</summary>
        public static ResourceBag ShareOf(BotEmpire bot)
        {
            int cc = Math.Max(1, bot.State.Buildings[BuildingId.CommandCenter].Level);
            var want = new ResourceBag(SupplyGoldPerCc * cc, SupplyQuartzPerCc * cc, SupplyHeliumPerCc * cc).Milli();
            var have = bot.State.Resources;
            return new ResourceBag(
                Math.Max(0, Math.Min(want.Gold, (long)(have.Gold * SupplyStockShare))),
                Math.Max(0, Math.Min(want.Quartz, (long)(have.Quartz * SupplyStockShare))),
                Math.Max(0, Math.Min(want.Helium, (long)(have.Helium * SupplyStockShare))));
        }

        /// <summary>Deliver due supply runs to the player. Returns how many arrived.</summary>
        public static int Supplies(GameState player, BotGalaxy galaxy)
        {
            if (player.ClanId == 0) return 0;
            int delivered = 0, guard = 0;
            while (player.Tick >= player.ClanSupplyNextTick && guard++ < 64)
            {
                player.ClanSupplyNextTick += SupplyIntervalSec;
                if (player.ClanSupplyRuns >= MaxStoredRuns) continue; // waiting on you
                var run = new ResourceBag();
                foreach (var bot in BotMembers(galaxy, player.ClanId))
                {
                    var share = ShareOf(bot);
                    bot.State.Resources.Gold -= share.Gold;
                    bot.State.Resources.Quartz -= share.Quartz;
                    bot.State.Resources.Helium -= share.Helium;
                    run.Add(share);
                }
                player.ClanSupplyPending.Add(run);
                player.ClanSupplyRuns++;
                delivered++;
            }
            if (player.Tick >= player.ClanSupplyNextTick) player.ClanSupplyNextTick = player.Tick + SupplyIntervalSec;
            return delivered;
        }

        public static SimResult CollectSupplies(GameState player)
        {
            if (player.ClanSupplyPending.Total <= 0) return SimResult.Fail("No supplies waiting");
            player.Resources.Add(player.ClanSupplyPending);
            player.ClanSupplyPending = new ResourceBag();
            player.ClanSupplyRuns = 0;
            return SimResult.Success;
        }

        // ---------- in battle ----------

        /// <summary>
        /// Warships the defender's clanmates send when <paramref name="defenderId"/>
        /// (0 = the player) is attacked by <paramref name="attackerId"/>: up to
        /// MaxReinforcers clanmates within ReinforceRange, nearest first, each
        /// committing ReinforceFraction of its docked warships.
        /// </summary>
        public static List<(BotEmpire ally, Dictionary<HullId, int> ships)> DefenseHelpers(
            GameState player, BotGalaxy galaxy, int defenderId, int attackerId, ICollection<int>? exclude = null)
        {
            var list = new List<(BotEmpire, Dictionary<HullId, int>)>();
            int clanId = ClanOf(player, galaxy, defenderId);
            if (clanId == 0) return list;
            var home = defenderId == 0 ? player.HomeTile : galaxy.Find(defenderId)?.HomeTile ?? player.HomeTile;
            var near = new List<(BotEmpire bot, double dist)>();
            foreach (var bot in galaxy.Bots)
            {
                if (bot.ClanId != clanId || bot.Id == defenderId || bot.Id == attackerId) continue;
                if (exclude != null && exclude.Contains(bot.Id)) continue; // already there as a garrison
                double d = TileXY.Distance(bot.HomeTile, home);
                if (d <= ReinforceRange) near.Add((bot, d));
            }
            near.Sort((a, b) => a.dist.CompareTo(b.dist));
            foreach (var (bot, _) in near)
            {
                if (list.Count >= MaxReinforcers) break;
                var ships = Share(bot, ReinforceFraction);
                if (ships.Count > 0) list.Add((bot, ships));
            }
            return list;
        }

        /// <summary><paramref name="fraction"/> of each docked warship hull (haulers and probes stay home).</summary>
        public static Dictionary<HullId, int> Share(BotEmpire bot, double fraction)
        {
            var ships = new Dictionary<HullId, int>();
            foreach (var kv in bot.State.Ships)
            {
                if (kv.Key == HullId.Hauler || kv.Key == HullId.Probe) continue;
                int n = (int)(kv.Value * fraction);
                if (n > 0) ships[kv.Key] = n;
            }
            return ships;
        }

        /// <summary>
        /// Split each hull's losses across the fleets that fought in one line, in
        /// proportion to what each put in. <paramref name="contributions"/>[0] is
        /// the owner of the fight and absorbs the rounding.
        /// </summary>
        public static List<Dictionary<HullId, int>> SplitLosses(
            IReadOnlyList<Dictionary<HullId, int>> contributions, Dictionary<HullId, int> survivors)
        {
            var losses = new List<Dictionary<HullId, int>>();
            foreach (var _ in contributions) losses.Add(new Dictionary<HullId, int>());
            foreach (var hull in Ships.All)
            {
                int total = 0;
                foreach (var c in contributions) total += c.TryGetValue(hull, out var n) ? n : 0;
                if (total <= 0) continue;
                int lost = total - (survivors.TryGetValue(hull, out var s) ? s : 0);
                if (lost <= 0) continue;
                int owner = contributions[0].TryGetValue(hull, out var o) ? o : 0;
                int assigned = 0;
                for (int i = 1; i < contributions.Count; i++)
                {
                    int mine = contributions[i].TryGetValue(hull, out var m) ? m : 0;
                    if (mine <= 0) continue;
                    int share = Math.Min(mine, (int)Math.Round(lost * (double)mine / total));
                    if (share > 0) losses[i][hull] = share;
                    assigned += share;
                }
                int ownerLost = Math.Min(owner, Math.Max(0, lost - assigned));
                if (ownerLost > 0) losses[0][hull] = ownerLost;
            }
            return losses;
        }

        /// <summary>Take losses off a dock.</summary>
        public static void Deduct(Dictionary<HullId, int> dock, Dictionary<HullId, int> losses)
        {
            foreach (var kv in losses)
                dock[kv.Key] = Math.Max(0, (dock.TryGetValue(kv.Key, out var n) ? n : 0) - kv.Value);
        }

        public static Dictionary<HullId, int> Combine(IEnumerable<Dictionary<HullId, int>> fleets)
        {
            var sum = new Dictionary<HullId, int>();
            foreach (var fleet in fleets)
                foreach (var kv in fleet)
                    sum[kv.Key] = (sum.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
            return sum;
        }

        /// <summary>A battle between members of two warring clans scores for the victor.</summary>
        public static void RecordBattle(GameState player, BotGalaxy galaxy, int attackerId, int defenderId, bool attackerWon)
        {
            int a = ClanOf(player, galaxy, attackerId), d = ClanOf(player, galaxy, defenderId);
            if (!AtWarWith(galaxy, a, d)) return;
            var winner = galaxy.FindClan(attackerWon ? a : d);
            if (winner != null) winner.WarScore++;
        }

        // ---------- the clock ----------

        /// <summary>Per sim tick (ProgressionSystem): politics every six hours of
        /// galaxy time, supply runs, and a lapsing invitation.</summary>
        public static void Tick(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            int passes = 0;
            while (player.Tick >= galaxy.NextPoliticsTick && passes++ < 4)
            {
                int passTick = Math.Max(0, galaxy.NextPoliticsTick);
                galaxy.NextPoliticsTick = passTick + PoliticsIntervalSec;
                Politics(player, galaxy, events, passTick);
            }
            if (player.Tick >= galaxy.NextPoliticsTick) galaxy.NextPoliticsTick = player.Tick + PoliticsIntervalSec;

            int runs = Supplies(player, galaxy);
            if (runs > 0) events.Emit(new ClanSuppliesArrived(runs));
            TrackClanSize(player, galaxy);
        }

        static void TrackClanSize(GameState player, BotGalaxy galaxy)
        {
            if (player.ClanId == 0) return;
            int size = MemberCount(player, galaxy, player.ClanId);
            if (size > player.Stats.BestClanSize) player.Stats.BestClanSize = size;
        }

        /// <summary>One politics pass: seed the clans (first pass of any galaxy),
        /// settle finished wars, recruit, fold and found clans, start wars, and
        /// maybe invite the player.</summary>
        public static void Politics(GameState player, BotGalaxy galaxy, SimEventBus events, int passTick)
        {
            var rng = Rng.Mulberry32(unchecked((uint)player.Seed * 747796405u
                ^ (uint)(passTick / PoliticsIntervalSec) * 2891336453u ^ 0x5EEDC1A4u));
            if (galaxy.Clans.Count == 0) SeedClans(player, galaxy);
            EndWars(player, galaxy, events, passTick);
            Recruit(player, galaxy, rng);
            FoldAndFound(player, galaxy, rng, passTick);
            StartWars(player, galaxy, events, rng, passTick);
            Invite(player, galaxy, events, passTick);
        }

        /// <summary>The opening map of the galaxy's clans: the strongest spread-out
        /// commanders found them and gather their nearest neighbours.</summary>
        public static void SeedClans(GameState player, BotGalaxy galaxy)
        {
            var rng = Rng.Mulberry32(unchecked((uint)player.Seed ^ 0xC1A45EEDu));
            var free = new List<BotEmpire>();
            foreach (var bot in galaxy.Bots)
                if (bot.ClanId == 0 && !IsLoneWolf(player.Seed, bot)) free.Add(bot);
            free.Sort((a, b) => b.CachedMight != a.CachedMight ? b.CachedMight.CompareTo(a.CachedMight) : a.Id.CompareTo(b.Id));

            var founders = new List<BotEmpire>();
            foreach (var bot in free)
            {
                if (founders.Count >= SeedClanCount) break;
                bool crowded = false;
                foreach (var f in founders)
                    if (TileXY.Distance(f.HomeTile, bot.HomeTile) < 250) { crowded = true; break; }
                if (!crowded) founders.Add(bot);
            }

            var firstWords = Shuffled(ClanNames.First, rng);
            var secondWords = Shuffled(ClanNames.Second, rng);
            var clans = new List<(Clan clan, BotEmpire leader, int target)>();
            for (int i = 0; i < founders.Count; i++)
            {
                var clan = NewBotClan(galaxy, firstWords, secondWords, founders[i], 0);
                if (clan == null) break;
                clans.Add((clan, founders[i], 8 + (int)(rng() * 8)));
            }

            // Round-robin recruiting keeps the clans even: each takes its nearest
            // free neighbour in turn until it's full or nobody is close enough.
            var counts = new Dictionary<int, int>();
            foreach (var c in clans) counts[c.clan.Id] = 1;
            for (bool any = true; any;)
            {
                any = false;
                foreach (var (clan, leader, target) in clans)
                {
                    if (counts[clan.Id] >= Math.Min(target, MaxMembers)) continue;
                    BotEmpire? best = null;
                    double bestD = RecruitRange;
                    foreach (var bot in free)
                    {
                        if (bot.ClanId != 0) continue;
                        double d = TileXY.Distance(bot.HomeTile, leader.HomeTile);
                        if (d < bestD) { bestD = d; best = bot; }
                    }
                    if (best == null) continue;
                    best.ClanId = clan.Id;
                    counts[clan.Id]++;
                    any = true;
                }
            }
        }

        static List<string> Shuffled(string[] words, Func<double> rng)
        {
            var list = new List<string>(words);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = (int)(rng() * (i + 1)) % (i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        /// <summary>A new bot-led clan with a name nobody holds (null when the name
        /// lists are exhausted).</summary>
        static Clan? NewBotClan(BotGalaxy galaxy, List<string> firstWords, List<string> secondWords,
            BotEmpire leader, int tick)
        {
            foreach (var first in firstWords)
            {
                string tag = ClanNames.TagOf(first);
                if (galaxy.Clans.Exists(c => c.Tag == tag)) continue;
                string second = secondWords[galaxy.NextClanId % secondWords.Count];
                var clan = new Clan
                {
                    Id = galaxy.NextClanId++,
                    Name = $"{first} {second}",
                    Tag = tag,
                    LeaderId = leader.Id,
                    FoundedTick = tick,
                };
                galaxy.Clans.Add(clan);
                leader.ClanId = clan.Id;
                return clan;
            }
            return null;
        }

        static void Recruit(GameState player, BotGalaxy galaxy, Func<double> rng)
        {
            var members = new Dictionary<int, List<BotEmpire>>();
            foreach (var clan in galaxy.Clans) members[clan.Id] = new List<BotEmpire>();
            foreach (var bot in galaxy.Bots)
                if (bot.ClanId != 0 && members.TryGetValue(bot.ClanId, out var list)) list.Add(bot);

            foreach (var bot in galaxy.Bots)
            {
                if (bot.ClanId != 0 || IsLoneWolf(player.Seed, bot)) continue;
                if (rng() >= 0.2) continue;
                Clan? best = null;
                double bestD = RecruitRange;
                foreach (var clan in galaxy.Clans)
                {
                    if (clan.LeaderId == 0) continue; // the player recruits by invitation
                    var list = members[clan.Id];
                    int count = list.Count + (player.ClanId == clan.Id ? 1 : 0);
                    if (count >= MaxMembers) continue;
                    foreach (var m in list)
                    {
                        double d = TileXY.Distance(m.HomeTile, bot.HomeTile);
                        if (d < bestD) { bestD = d; best = clan; }
                    }
                }
                if (best == null) continue;
                bot.ClanId = best.Id;
                members[best.Id].Add(bot);
            }
        }

        static void FoldAndFound(GameState player, BotGalaxy galaxy, Func<double> rng, int passTick)
        {
            // Fold: a bot-led clan down to one or two commanders may call it quits.
            for (int i = galaxy.Clans.Count - 1; i >= 0; i--)
            {
                var clan = galaxy.Clans[i];
                if (clan.LeaderId == 0 || player.ClanId == clan.Id) continue;
                if (BotMembers(galaxy, clan.Id).Count >= 3 || rng() >= 0.25) continue;
                galaxy.AddBulletin(passTick, $"{Label(clan)} disbanded");
                Disband(galaxy, clan);
            }

            // Found: one new clan per pass, around the strongest free commander
            // with enough free neighbours.
            int botClans = 0;
            foreach (var clan in galaxy.Clans) if (clan.LeaderId != 0) botClans++;
            if (botClans >= MaxBotClans || rng() >= 0.3) return;
            var free = new List<BotEmpire>();
            foreach (var bot in galaxy.Bots)
                if (bot.ClanId == 0 && !IsLoneWolf(player.Seed, bot)) free.Add(bot);
            free.Sort((a, b) => b.CachedMight != a.CachedMight ? b.CachedMight.CompareTo(a.CachedMight) : a.Id.CompareTo(b.Id));
            foreach (var founder in free)
            {
                var near = free.FindAll(b => b != founder && TileXY.Distance(b.HomeTile, founder.HomeTile) <= 300);
                if (near.Count < 3) continue;
                var clan = NewBotClan(galaxy, Shuffled(ClanNames.First, rng), Shuffled(ClanNames.Second, rng),
                    founder, passTick);
                if (clan == null) return;
                near.Sort((a, b) => TileXY.Distance(a.HomeTile, founder.HomeTile)
                    .CompareTo(TileXY.Distance(b.HomeTile, founder.HomeTile)));
                for (int i = 0; i < Math.Min(4, near.Count); i++) near[i].ClanId = clan.Id;
                galaxy.AddBulletin(passTick, $"{founder.Name} founded {Label(clan)}");
                return;
            }
        }

        static void Disband(BotGalaxy galaxy, Clan clan)
        {
            foreach (var bot in galaxy.Bots) if (bot.ClanId == clan.Id) bot.ClanId = 0;
            if (galaxy.FindClan(clan.WarWithClanId) is { } enemy && enemy.WarWithClanId == clan.Id)
            {
                enemy.WarWithClanId = 0;
                enemy.WarScore = 0;
            }
            galaxy.Clans.Remove(clan);
        }

        static void StartWars(GameState player, BotGalaxy galaxy, SimEventBus events, Func<double> rng, int passTick)
        {
            foreach (var clan in galaxy.Clans.ToArray())
            {
                if (clan.LeaderId == 0) continue; // a player-led clan declares its own wars
                if (clan.WarWithClanId != 0 || passTick < clan.WarCooldownUntilTick) continue;
                if (rng() >= 0.12) continue;
                long might = ClanMight(player, galaxy, clan.Id);
                var mine = MemberHomes(player, galaxy, clan.Id);
                Clan? pick = null;
                double pickD = RivalRange;
                foreach (var other in galaxy.Clans)
                {
                    if (other == clan || other.WarWithClanId != 0 || passTick < other.WarCooldownUntilTick) continue;
                    long theirs = ClanMight(player, galaxy, other.Id);
                    if (theirs <= 0 || might <= 0) continue;
                    double ratio = theirs / (double)might;
                    if (ratio < 0.5 || ratio > 2.0) continue;
                    double d = Closest(mine, MemberHomes(player, galaxy, other.Id));
                    if (d < pickD) { pickD = d; pick = other; }
                }
                if (pick == null) continue;
                StartWar(galaxy, clan, pick, passTick);
                if (player.ClanId == pick.Id) events.Emit(new ClanWarDeclared(clan.Id, pick.Id, true));
                else if (player.ClanId == clan.Id) events.Emit(new ClanWarDeclared(clan.Id, pick.Id, false));
            }
        }

        static void StartWar(BotGalaxy galaxy, Clan a, Clan b, int tick)
        {
            a.WarWithClanId = b.Id;
            b.WarWithClanId = a.Id;
            a.WarEndsTick = b.WarEndsTick = tick + WarDurationSec;
            a.WarScore = b.WarScore = 0;
            galaxy.AddBulletin(tick, $"{Label(a)} declared war on {Label(b)}");
        }

        static void EndWars(GameState player, BotGalaxy galaxy, SimEventBus events, int passTick)
        {
            foreach (var clan in galaxy.Clans.ToArray())
            {
                if (clan.WarWithClanId == 0 || passTick < clan.WarEndsTick) continue;
                var enemy = galaxy.FindClan(clan.WarWithClanId);
                if (enemy == null || enemy.WarWithClanId != clan.Id)
                {
                    clan.WarWithClanId = 0;
                    clan.WarScore = 0;
                    continue;
                }
                var (win, lose) = clan.WarScore >= enemy.WarScore ? (clan, enemy) : (enemy, clan);
                bool draw = clan.WarScore == enemy.WarScore;
                galaxy.AddBulletin(passTick, draw
                    ? $"{Label(clan)} and {Label(enemy)} fought to a draw ({clan.WarScore}–{enemy.WarScore})"
                    : $"{Label(win)} won the war against {Label(lose)} ({win.WarScore}–{lose.WarScore})");
                if (player.ClanId == clan.Id || player.ClanId == enemy.Id)
                {
                    var ours = player.ClanId == clan.Id ? clan : enemy;
                    var theirs = ours == clan ? enemy : clan;
                    bool won = !draw && ours == win;
                    if (won)
                    {
                        player.Premium.DarkMatter += WarWinRewardDM;
                        player.Stats.ClanWarsWon++;
                    }
                    events.Emit(new ClanWarEnded(ours.Id, theirs.Id, won, draw, ours.WarScore, theirs.WarScore));
                }
                foreach (var c in new[] { clan, enemy })
                {
                    c.WarWithClanId = 0;
                    c.WarScore = 0;
                    c.WarCooldownUntilTick = passTick + WarCooldownSec;
                }
            }
        }

        static void Invite(GameState player, BotGalaxy galaxy, SimEventBus events, int passTick)
        {
            if (player.ClanId != 0) { player.ClanInviteId = 0; return; }
            if (player.ClanInviteId != 0)
            {
                bool alive = galaxy.FindClan(player.ClanInviteId) != null;
                if (alive && player.Tick < player.ClanInviteExpiresTick) return; // still standing
                player.ClanInviteId = 0;
                // An invitation left unanswered earns a breather; one from a clan
                // that has since disbanded doesn't count against you.
                if (alive) player.ClanNextInviteTick = player.Tick + InviteCooldownSec;
            }
            if (player.Tick < player.ClanNextInviteTick) return;
            if (PowerSystem.ComputePower(player) < BotSystem.PlayerShieldMight) return;
            Clan? pick = null;
            double pickD = RecruitRange;
            foreach (var clan in galaxy.Clans)
            {
                if (clan.LeaderId == 0 || !CanJoin(player, galaxy, clan).Ok) continue;
                foreach (var m in BotMembers(galaxy, clan.Id))
                {
                    double d = TileXY.Distance(m.HomeTile, player.HomeTile);
                    if (d < pickD) { pickD = d; pick = clan; }
                }
            }
            if (pick == null) return;
            player.ClanInviteId = pick.Id;
            player.ClanInviteExpiresTick = player.Tick + InviteLifetimeSec;
            events.Emit(new ClanInviteReceived(pick.Id));
        }

        static List<TileXY> MemberHomes(GameState player, BotGalaxy galaxy, int clanId)
        {
            var homes = new List<TileXY>();
            foreach (var bot in galaxy.Bots) if (bot.ClanId == clanId) homes.Add(bot.HomeTile);
            if (player.ClanId == clanId) homes.Add(player.HomeTile);
            return homes;
        }

        static double Closest(List<TileXY> a, List<TileXY> b)
        {
            double best = double.MaxValue;
            foreach (var x in a) foreach (var y in b) best = Math.Min(best, TileXY.Distance(x, y));
            return best;
        }

        // ---------- lists for the screens ----------

        /// <summary>Independent commanders who'd join the clan the player leads,
        /// nearest first.</summary>
        public static List<BotEmpire> InviteCandidates(GameState player, BotGalaxy galaxy, int max)
        {
            var list = new List<BotEmpire>();
            var clan = PlayerClan(player, galaxy);
            if (clan == null || clan.LeaderId != 0) return list;
            if (MemberCount(player, galaxy, clan.Id) >= MaxMembers) return list;
            long mine = PowerSystem.ComputePower(player);
            foreach (var bot in galaxy.Bots)
            {
                if (bot.ClanId != 0 || IsLoneWolf(player.Seed, bot)) continue;
                if (BotSystem.HoldsGrudge(bot, player.Tick)) continue;
                if (bot.CachedMight > mine * MaxMightRatio) continue;
                if (galaxy.Inbound.Exists(a => a.BotId == bot.Id && a.IsFleet)) continue;
                list.Add(bot);
            }
            list.Sort((a, b) => TileXY.Distance(a.HomeTile, player.HomeTile)
                .CompareTo(TileXY.Distance(b.HomeTile, player.HomeTile)));
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        /// <summary>Distance (tiles) from the player's colony to the clan's nearest member.</summary>
        public static double NearestMember(GameState player, BotGalaxy galaxy, int clanId)
        {
            double best = double.MaxValue;
            foreach (var bot in galaxy.Bots)
                if (bot.ClanId == clanId) best = Math.Min(best, TileXY.Distance(bot.HomeTile, player.HomeTile));
            return best;
        }

        // ---------- boards ----------

        /// <summary>Every clan with its size and total might, strongest first.</summary>
        public static List<(Clan clan, int members, long might)> Standings(GameState player, BotGalaxy galaxy)
        {
            var rows = new List<(Clan clan, int members, long might)>();
            foreach (var clan in galaxy.Clans)
                rows.Add((clan, MemberCount(player, galaxy, clan.Id), ClanMight(player, galaxy, clan.Id)));
            rows.Sort((a, b) => b.might != a.might ? b.might.CompareTo(a.might) : a.clan.Id.CompareTo(b.clan.Id));
            return rows;
        }
    }
}
