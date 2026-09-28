// Alliances with AI commanders (user request 2026-09-28). The player may hold
// pacts with up to three simulated commanders:
//   * an ally never raids you (and you can't raid them);
//   * once a day it sends a supply run from its REAL stock, which waits in the
//     ALLIES panel to be collected;
//   * when a raider hits your colony, every ally within reinforcement range
//     commits a share of its docked warships to the defense — they fight
//     beside yours and take their share of the losses (Bots.ResolveInbound).
// Not every commander will sign: warlike ones never do, giants won't treat a
// small colony as an equal, and nobody allies with you while nursing a grudge.
// Breaking a pact is an insult — they'll hold one.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Sim.Systems
{
    public static class AllianceSystem
    {
        public const int MaxAllies = 3;
        public const int AidIntervalSec = 24 * 3600;
        /// <summary>Uncollected supply runs an ally lets pile up before it stops sending.</summary>
        public const int MaxStoredRuns = 3;
        /// <summary>One supply run, per Command Center level of the ally (whole units).</summary>
        public const int AidGoldPerCc = 300, AidQuartzPerCc = 200, AidHeliumPerCc = 80;
        /// <summary>A run never takes more than this share of the ally's own stock.</summary>
        public const double AidStockShare = 0.2;
        /// <summary>Allies farther than this (tiles, home to home) can't reinforce in time.</summary>
        public const double ReinforceRange = 400;
        /// <summary>Share of each docked warship hull an ally commits to your defense.</summary>
        public const double ReinforceFraction = 0.15;
        /// <summary>Commanders this many times your might won't treat you as an equal.</summary>
        public const double MaxMightRatio = 3.0;
        /// <summary>Commanders more warlike than this never sign pacts.</summary>
        public const double MaxAggression = 0.75;

        public static bool IsAlly(GameState player, int botId) =>
            player.Allies.Exists(a => a.BotId == botId);

        public static Alliance? PactWith(GameState player, int botId) =>
            player.Allies.Find(a => a.BotId == botId);

        public static bool InReinforceRange(GameState player, BotEmpire bot) =>
            TileXY.Distance(bot.HomeTile, player.HomeTile) <= ReinforceRange;

        /// <summary>Would this commander accept a pact right now? (Reason when not.)</summary>
        public static SimResult CanPropose(GameState player, BotGalaxy galaxy, BotEmpire bot) =>
            CanPropose(player, galaxy, bot, PowerSystem.ComputePower(player));

        static SimResult CanPropose(GameState player, BotGalaxy galaxy, BotEmpire bot, long mine)
        {
            if (IsAlly(player, bot.Id)) return SimResult.Fail($"You're already allied with {bot.Name}");
            if (player.Allies.Count >= MaxAllies)
                return SimResult.Fail($"You can hold {MaxAllies} alliances at once");
            if (mine < BotSystem.PlayerShieldMight)
                return SimResult.Fail("Grow your colony first — commanders ally with established empires");
            if (BotSystem.HoldsGrudge(bot, player.Tick))
                return SimResult.Fail($"{bot.Name} hasn't forgiven you");
            if (galaxy.Inbound.Exists(a => a.BotId == bot.Id && a.IsFleet))
                return SimResult.Fail($"{bot.Name}'s fleet is already on its way to you");
            if (BotSystem.PersonalityOf(player.Seed, bot.Id).Aggression > MaxAggression)
                return SimResult.Fail($"{bot.Name} is a lone wolf — no pacts");
            if (bot.CachedMight > mine * MaxMightRatio)
                return SimResult.Fail($"{bot.Name} doesn't see you as an equal yet");
            return SimResult.Success;
        }

        public static SimResult Propose(GameState player, BotGalaxy galaxy, int botId)
        {
            var bot = galaxy.Find(botId);
            if (bot == null) return SimResult.Fail("Unknown commander");
            var ok = CanPropose(player, galaxy, bot);
            if (!ok.Ok) return ok;
            player.Allies.Add(new Alliance
            {
                BotId = botId,
                SinceTick = player.Tick,
                NextAidTick = player.Tick + AidIntervalSec,
            });
            // Friends don't scout friends.
            bot.SpyBackAtTick = 0;
            if (bot.FocusTargetId == 0) bot.FocusTargetId = -1;
            return SimResult.Success;
        }

        /// <summary>End a pact: supplies already sent are yours to keep, but the
        /// commander takes it personally and holds a grudge.</summary>
        public static SimResult Break(GameState player, BotGalaxy galaxy, int botId)
        {
            int i = player.Allies.FindIndex(a => a.BotId == botId);
            if (i < 0) return SimResult.Fail("You're not allied with them");
            var pact = player.Allies[i];
            player.Resources.Add(pact.Pending);
            player.Allies.RemoveAt(i);
            var bot = galaxy.Find(botId);
            if (bot != null)
            {
                bot.FocusTargetId = 0;
                bot.FocusSetTick = player.Tick;
            }
            return SimResult.Success;
        }

        /// <summary>One supply run from <paramref name="ally"/>, milli (not yet deducted).</summary>
        public static ResourceBag SupplyRun(BotEmpire ally)
        {
            int cc = Math.Max(1, ally.State.Buildings[BuildingId.CommandCenter].Level);
            var want = new ResourceBag(AidGoldPerCc * cc, AidQuartzPerCc * cc, AidHeliumPerCc * cc).Milli();
            var have = ally.State.Resources;
            return new ResourceBag(
                Math.Max(0, Math.Min(want.Gold, (long)(have.Gold * AidStockShare))),
                Math.Max(0, Math.Min(want.Quartz, (long)(have.Quartz * AidStockShare))),
                Math.Max(0, Math.Min(want.Helium, (long)(have.Helium * AidStockShare))));
        }

        /// <summary>Per tick (after the bots advance): deliver due supply runs.
        /// Returns how many runs arrived.</summary>
        public static int Tick(GameState player, BotGalaxy galaxy)
        {
            int delivered = 0;
            for (int i = player.Allies.Count - 1; i >= 0; i--)
            {
                var pact = player.Allies[i];
                var ally = galaxy.Find(pact.BotId);
                if (ally == null) { player.Allies.RemoveAt(i); continue; }
                while (player.Tick >= pact.NextAidTick)
                {
                    pact.NextAidTick += AidIntervalSec;
                    if (pact.PendingRuns >= MaxStoredRuns) continue; // waiting on you
                    var run = SupplyRun(ally);
                    ally.State.Resources.Gold -= run.Gold;
                    ally.State.Resources.Quartz -= run.Quartz;
                    ally.State.Resources.Helium -= run.Helium;
                    pact.Pending.Add(run);
                    pact.PendingRuns++;
                    delivered++;
                }
            }
            return delivered;
        }

        public static ResourceBag PendingTotal(GameState player)
        {
            var sum = new ResourceBag();
            foreach (var pact in player.Allies) sum.Add(pact.Pending);
            return sum;
        }

        /// <summary>Bank every waiting supply run.</summary>
        public static SimResult Collect(GameState player)
        {
            var sum = PendingTotal(player);
            if (sum.Total <= 0) return SimResult.Fail("No supplies waiting");
            player.Resources.Add(sum);
            foreach (var pact in player.Allies)
            {
                pact.Pending = new ResourceBag();
                pact.PendingRuns = 0;
            }
            return SimResult.Success;
        }

        /// <summary>The warships each in-range ally commits when a raider (not an
        /// ally itself) hits the player's colony.</summary>
        public static List<(BotEmpire ally, Dictionary<HullId, int> ships)> Reinforcements(
            GameState player, BotGalaxy galaxy)
        {
            var list = new List<(BotEmpire, Dictionary<HullId, int>)>();
            foreach (var pact in player.Allies)
            {
                var ally = galaxy.Find(pact.BotId);
                if (ally == null || !InReinforceRange(player, ally)) continue;
                var ships = new Dictionary<HullId, int>();
                foreach (var kv in ally.State.Ships)
                {
                    if (kv.Key == HullId.Hauler || kv.Key == HullId.Probe) continue;
                    int n = (int)(kv.Value * ReinforceFraction);
                    if (n > 0) ships[kv.Key] = n;
                }
                if (ships.Count > 0) list.Add((ally, ships));
            }
            return list;
        }

        /// <summary>
        /// Commanders who'd sign right now, best first: nearest in reinforcement
        /// range first, then by might. Capped at <paramref name="max"/>.
        /// </summary>
        public static List<BotEmpire> Candidates(GameState player, BotGalaxy galaxy, int max = 8)
        {
            var list = new List<BotEmpire>();
            if (player.Allies.Count >= MaxAllies) return list;
            long mine = PowerSystem.ComputePower(player);
            foreach (var bot in galaxy.Bots)
                if (CanPropose(player, galaxy, bot, mine).Ok) list.Add(bot);
            list.Sort((a, b) =>
            {
                bool ar = InReinforceRange(player, a), br = InReinforceRange(player, b);
                if (ar != br) return ar ? -1 : 1;
                return b.CachedMight != a.CachedMight ? b.CachedMight.CompareTo(a.CachedMight) : a.Id.CompareTo(b.Id);
            });
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }
    }
}
