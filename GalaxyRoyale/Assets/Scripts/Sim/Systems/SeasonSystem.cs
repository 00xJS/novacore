// Seasons (user request 2026-09-28): two-week races on galaxy time. The season
// board ranks every empire by the might it GAINED this season, so a young
// colony can win one against giants; at the end the finishing rank pays Dark
// Matter, lands in the season history, and every baseline resets. Runs after
// the bots advance (live and in the offline catch-up), so a season that ends
// while the app is closed is still settled.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Sim.Systems
{
    public static class SeasonSystem
    {
        public const int SeasonSec = 14 * 24 * 3600;

        public static int SeasonAt(int tick) => 1 + Math.Max(0, tick) / SeasonSec;
        public static int EndTick(int season) => season * SeasonSec;

        /// <summary>Dark Matter for a finishing rank.</summary>
        public static int RewardFor(int rank) => rank switch
        {
            1 => 1000,
            <= 3 => 600,
            <= 10 => 300,
            <= 50 => 120,
            _ => 40,
        };

        public static long PlayerGain(GameState player) =>
            PowerSystem.ComputePower(player) - player.SeasonStartMight;

        /// <summary>This season's board: (bot id, 0 = you; name; might gained), best first.</summary>
        public static List<(int id, string name, long gain)> Standings(GameState player, BotGalaxy galaxy)
        {
            var rows = new List<(int id, string name, long gain)>
            {
                (0, player.Profile.Name, PlayerGain(player)),
            };
            foreach (var bot in galaxy.Bots)
                rows.Add((bot.Id, bot.Name, bot.CachedMight - bot.SeasonStartMight));
            // Ties go to the player's rivals by id, deterministic either way.
            rows.Sort((a, b) => b.gain != a.gain ? b.gain.CompareTo(a.gain) : a.id.CompareTo(b.id));
            return rows;
        }

        public static int PlayerRank(GameState player, BotGalaxy galaxy)
        {
            var rows = Standings(player, galaxy);
            return rows.FindIndex(r => r.id == 0) + 1;
        }

        /// <summary>
        /// Start the first season, or settle the finished one and start the next.
        /// Returns the finished season's record (null when nothing ended).
        /// </summary>
        public static SeasonRecord? Tick(GameState player, BotGalaxy galaxy)
        {
            int now = SeasonAt(player.Tick);
            if (player.Season == 0) { Begin(player, galaxy, now); return null; }
            if (now == player.Season) return null;

            var rows = Standings(player, galaxy);
            int rank = rows.FindIndex(r => r.id == 0) + 1;
            var record = new SeasonRecord
            {
                Season = player.Season,
                Rank = rank,
                Of = rows.Count,
                Gain = PlayerGain(player),
                RewardDM = RewardFor(rank),
            };
            player.SeasonHistory.Add(record);
            player.Premium.DarkMatter += record.RewardDM;
            if (player.Stats.BestSeasonRank == 0 || rank < player.Stats.BestSeasonRank)
                player.Stats.BestSeasonRank = rank;
            Begin(player, galaxy, now);
            return record;
        }

        static void Begin(GameState player, BotGalaxy galaxy, int season)
        {
            player.Season = season;
            player.SeasonStartMight = PowerSystem.ComputePower(player);
            foreach (var bot in galaxy.Bots) bot.SeasonStartMight = bot.CachedMight;
        }
    }
}
