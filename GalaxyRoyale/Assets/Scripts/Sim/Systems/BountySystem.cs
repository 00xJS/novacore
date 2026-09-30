// The Bounty Board (rival events, 2026-09-30): for the event's day, the rim's
// most-wanted raider — the commander near you with the most battles won, within
// reach of your might — has a price on their head. Win a raid on them to collect
// (StrikeSystem.ResolveRaid calls Claim). The rivals hunt them too (BotSystem
// weighs them BountyHuntWeight× as a target), and if one of them wins a raid on
// the marked commander first, the bounty is gone.
using System;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Sim.Systems
{
    public static class BountySystem
    {
        /// <summary>Per galaxy step (ProgressionSystem): post the bounty when the event begins.</summary>
        public static void Tick(GameState player, BotGalaxy galaxy, SimEventBus events)
        {
            var live = EventSystem.Current(player.Tick);
            if (live.Def.Kind != GalaxyEventKind.BountyBoard || player.BountyInstance == live.Instance) return;
            var target = PickTarget(player, galaxy);
            player.BountyInstance = live.Instance;
            player.BountyClaimed = target == null;
            player.BountyTargetId = target?.Id ?? 0;
            player.BountyTargetName = target?.Name ?? "";
            player.BountyTile = target?.State.HomeTile ?? default;
            if (target == null) return;
            galaxy.AddBulletin(player.Tick, $"A bounty is posted on {target.Name}, the rim's most wanted raider");
            events.Emit(new BountyPosted(target.Name, target.State.HomeTile));
        }

        /// <summary>The commander near you with the most battles won whose might is within
        /// BountyMightBand of yours (not a clanmate); the nearest rival if none fits.</summary>
        public static BotEmpire? PickTarget(GameState player, BotGalaxy galaxy)
        {
            long mine = Math.Max(1, PowerSystem.ComputePower(player));
            BotEmpire? best = null, nearest = null;
            double nearestD = double.MaxValue;
            foreach (var bot in galaxy.Bots)
            {
                if (ClanSystem.SameClanAsPlayer(player, bot)) continue;
                double d = TileXY.Distance(bot.State.HomeTile, player.HomeTile);
                if (d > GalaxyEvents.BountyRange) continue;
                if (d < nearestD) { nearestD = d; nearest = bot; }
                double ratio = bot.CachedMight / (double)mine;
                if (ratio > GalaxyEvents.BountyMightBand || ratio < 1 / GalaxyEvents.BountyMightBand) continue;
                if (best == null || bot.State.Stats.BattlesWon > best.State.Stats.BattlesWon
                    || (bot.State.Stats.BattlesWon == best.State.Stats.BattlesWon && bot.Id < best.Id))
                    best = bot;
            }
            return best ?? nearest;
        }

        /// <summary>Is this commander's head worth a bounty right now?</summary>
        public static bool IsMarked(GameState player, int botId)
        {
            if (player.BountyClaimed || player.BountyTargetId == 0 || botId != player.BountyTargetId) return false;
            var live = EventSystem.Current(player.Tick);
            return live.Def.Kind == GalaxyEventKind.BountyBoard && live.Instance == player.BountyInstance;
        }

        /// <summary>What the bounty pays (milli), and its Dark Matter.</summary>
        public static (ResourceBag pay, int darkMatter) Reward(GameState player)
        {
            long stock = Nodes.CampStockpile(2, player.Buildings[BuildingId.CommandCenter].Level)
                * GalaxyEvents.BountyStockpiles * 1000;
            return (new ResourceBag(stock * 40 / 100, stock * 35 / 100, stock * 25 / 100), GalaxyEvents.BountyDarkMatter);
        }

        /// <summary>You won a raid on <paramref name="botId"/>: collect if they're marked.
        /// Paid straight home. Returns the pay, or null.</summary>
        public static (ResourceBag pay, int darkMatter)? Claim(GameState player, int botId)
        {
            if (!IsMarked(player, botId)) return null;
            var (pay, dm) = Reward(player);
            ResourceSystem.Add(player, pay);
            player.Premium.DarkMatter += dm;
            player.BountyClaimed = true;
            player.Stats.BountiesClaimed++;
            return (pay, dm);
        }

        /// <summary>A rival won a raid on the marked commander first: the bounty is gone.</summary>
        public static void TakenByRival(GameState player, BotGalaxy galaxy, BotEmpire hunter, int at, SimEventBus events)
        {
            player.BountyClaimed = true;
            galaxy.AddBulletin(at, $"{hunter.Name} collected the bounty on {player.BountyTargetName}");
            events.Emit(new BountyTaken(hunter.Name, player.BountyTargetName));
        }
    }
}
