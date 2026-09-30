// Seasons (user request 2026-09-28): one-week races on galaxy time (cut from
// two weeks 2026-09-29 so a single-player run sees a finish line every week,
// in step with the weekly event rotation). The season
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
        public const int SeasonSec = 7 * 24 * 3600;

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

        /// <summary>The record a season recap reports, by key: lifetime counters
        /// whose difference over a season is what you did in it.</summary>
        public static Dictionary<string, long> Tally(Stats s) => new()
        {
            ["raids"] = s.RaidsWon,
            ["camps"] = s.CampsCleared,
            ["defenses"] = s.DefensesWon,
            ["loot"] = s.LootMilli / 1000,
            ["gathered"] = s.GatheredMilli / 1000,
            ["ships"] = s.ShipsBuilt,
            ["upgrades"] = s.UpgradesDone,
            ["research"] = s.ResearchDone,
            ["events"] = s.EventsCompleted,
            ["boss"] = s.BossStrikes,
            ["cores"] = s.CoresSeized,
            ["expeditions"] = s.ExpeditionsDone,
            ["nemeses"] = s.NemesesDefeated,
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
            // A save from before the recap: count from here, so this season's recap has something.
            if (player.SeasonStartTally.Count == 0) player.SeasonStartTally = Tally(player.Stats);
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
                Champion = rows[0].name,
                ChampionGain = rows[0].gain,
            };
            // Only a season that started with a tally knows what happened in it.
            if (player.SeasonStartTally.Count > 0)
                foreach (var (key, count) in Tally(player.Stats))
                {
                    long delta = count - (player.SeasonStartTally.TryGetValue(key, out var was) ? was : 0);
                    if (delta > 0) record.Highlights[key] = delta;
                }
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
            player.SeasonStartTally = Tally(player.Stats);
            foreach (var bot in galaxy.Bots) bot.SeasonStartMight = bot.CachedMight;
        }
    }
}
