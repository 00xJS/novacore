// "While you were away" — what the galaxy did while the app was closed or in
// the background, distilled from the catch-up. Waking up to last night's wars
// is the game's whole pitch, yet the catch-up only ever surfaced a resource
// toast: every event it produced was thrown away. Built from two sources:
//   * the mailbox — raids on the colony, radar-caught scans, your own battles
//     and spy reports all file mail with a SEQUENTIAL id, so "mail newer than
//     the id before the catch-up" is exactly what happened while away (the
//     50-item cap can evict some on a very long night: counts are lower bounds);
//   * the player-sim events collected during the suppressed fast-forward
//     (upgrades, research, ships, fleets home).
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Sim
{
    public sealed class OfflineDebrief
    {
        public int ElapsedSec;
        public ResourceBag Gained = new();

        // Your colony.
        public int RaidsSuffered, RaidsRepelled, RaidsDeflected;
        public long LootLostMilli;
        public int SpyScans;

        // Your fleets.
        public int BattlesWon, BattlesLost, SpyReports, FleetsHome;
        public long CargoHomeMilli;

        // Colony work.
        public int Upgrades, Research, ShipsBuilt;

        public int RankBefore, RankAfter;
        public int NewMail;

        /// <summary>Anything worth a full report (vs. a resources-only toast)?</summary>
        public bool Notable =>
            RaidsSuffered + RaidsRepelled + RaidsDeflected + SpyScans
            + BattlesWon + BattlesLost + SpyReports + FleetsHome
            + Upgrades + Research + ShipsBuilt > 0
            || RankAfter != RankBefore;

        /// <summary>1 + the number of rivals with MORE might (ties share the better rank).</summary>
        public static int RankOf(GameState player, BotGalaxy? bots)
        {
            if (bots == null) return 1;
            long mine = PowerSystem.ComputePower(player);
            int rank = 1;
            foreach (var bot in bots.Bots)
                if (bot.CachedMight > mine) rank++;
            return rank;
        }

        /// <param name="mailIdBefore">state.NextReportId captured BEFORE the catch-up.</param>
        /// <param name="rankBefore">RankOf() captured BEFORE the catch-up.</param>
        public static OfflineDebrief Build(GameState state, OfflineSummary summary,
            int mailIdBefore, int rankBefore, BotGalaxy? bots)
        {
            var d = new OfflineDebrief
            {
                ElapsedSec = summary.ElapsedSec,
                Gained = summary.Gained,
                RankBefore = rankBefore,
                RankAfter = RankOf(state, bots),
            };

            foreach (var mail in state.Mailbox)
            {
                if (mail.Id < mailIdBefore) continue;
                d.NewMail++;
                switch (mail)
                {
                    case BattleMailReport { Defending: true } defense:
                    {
                        var r = defense.Report;
                        if (r.Winner == BattleWinner.Defender && r.Rounds.Count == 0) d.RaidsDeflected++;
                        else if (r.Winner == BattleWinner.Attacker)
                        {
                            d.RaidsSuffered++;
                            d.LootLostMilli += r.Loot?.Total ?? 0;
                        }
                        else d.RaidsRepelled++;
                        break;
                    }
                    case BattleMailReport battle:
                        if (battle.Report.Winner == BattleWinner.Attacker) d.BattlesWon++;
                        else d.BattlesLost++;
                        break;
                    case RadarWarning:
                        d.SpyScans++;
                        break;
                    case SpyReport:
                        d.SpyReports++;
                        break;
                }
            }

            foreach (var e in summary.Events)
            {
                switch (e)
                {
                    case BuildingCompleted: d.Upgrades++; break;
                    case ResearchCompleted: d.Research++; break;
                    case ShipsCompleted ships: d.ShipsBuilt += ships.Count; break;
                    case MarchReturned home:
                        d.FleetsHome++;
                        d.CargoHomeMilli += home.Cargo.Total;
                        break;
                }
            }
            return d;
        }
    }
}
