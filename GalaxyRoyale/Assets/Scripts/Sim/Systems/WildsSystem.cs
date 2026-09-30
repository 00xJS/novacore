// The Wilds (2026-09-29): surveys, finds, the harvester drones, and the
// refills and fog that keep the Wilds from running out.
//
// - Survey one sector at a time: any sector of the first ring, or one next to a
//   charted sector in the ring above. Deeper rings cost more, take longer and
//   hold more.
// - A survey finds a deposit (70%), a supply cache (22%) or a relic (8%),
//   seeded by the galaxy, the sector and how many times it has been surveyed.
// - Harvester drones (the colony's own one, plus the Drone Factory's) share
//   their haul across every charted deposit with stock left, settled once a
//   minute straight into the treasury. A deposit that runs dry fills back up
//   Balance.WildsRefillSec later.
// - Caches and relics are claimed with a tap; Balance.WildsShiftSec later their
//   sector drifts back under the fog, ready to be surveyed for something new.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Systems
{
    public static class WildsSystem
    {
        /// <summary>The drones are settled up once a minute.</summary>
        public const int HarvestEverySec = 60;

        public static int FactoryLevel(GameState s) =>
            s.Buildings.TryGetValue(BuildingId.DroneFactory, out var b) ? b.Level : 0;

        public static int Drones(GameState s) => Balance.WildsDrones(FactoryLevel(s));

        /// <summary>Whole units one drone carries home a trip.</summary>
        public static long Carry(GameState s) => Balance.DroneCarry(FactoryLevel(s));

        /// <summary>What all the drones can bring home in an hour (milli), deposits permitting.</summary>
        public static long HaulPerHourMilli(GameState s) =>
            Drones(s) * Carry(s) * 1000L * 3600 / Balance.DroneTripSec;

        public static WildsSector? Sector(GameState s, int index) =>
            s.Wilds.Sectors.TryGetValue(index, out var sector) ? sector : null;

        public static bool Charted(GameState s, int index) => Sector(s, index) is { Find: not WildsFind.None };

        public static int ChartedCount(GameState s)
        {
            int n = 0;
            foreach (var sector in s.Wilds.Sectors.Values)
                if (sector.Find != WildsFind.None) n++;
            return n;
        }

        /// <summary>Deposits the drones can work right now (stock left).</summary>
        public static int ActiveDeposits(GameState s)
        {
            int n = 0;
            foreach (var sector in s.Wilds.Sectors.Values)
                if (sector.Find == WildsFind.Deposit && sector.StockMilli > 0) n++;
            return n;
        }

        /// <summary>A sector the survey teams can reach: the first ring, or next to a charted sector.</summary>
        public static bool Reachable(GameState s, int index)
        {
            if (!WildsLayout.Valid(index)) return false;
            if (WildsLayout.RingOf(index) == 0) return true;
            foreach (int n in WildsLayout.Neighbours(index))
                if (Charted(s, n)) return true;
            return false;
        }

        /// <summary>Survey cost (milli) and time for a sector.</summary>
        public static ResourceBag SurveyCost(int index) => Balance.WildsSurveyCost(WildsLayout.RingOf(index)).Milli();

        public static int SurveySeconds(int index) => Balance.WildsSurveySec[WildsLayout.RingOf(index)];

        /// <summary>A survey's time for this colony (the Deep Space Observatory shortens it).</summary>
        public static int SurveySeconds(GameState s, int index) =>
            System.Math.Max(10, (int)System.Math.Round(SurveySeconds(index) * ObservatorySystem.SurveyMult(s)));

        /// <summary>Seconds left on the survey under way (0 = none).</summary>
        public static int SurveyLeft(GameState s) =>
            s.Wilds.Surveying < 0 ? 0 : Math.Max(0, s.Wilds.SurveyDoneTick - s.Tick);

        public static SimResult CheckSurvey(GameState s, int index)
        {
            if (!WildsLayout.Valid(index)) return SimResult.Fail("No such sector");
            if (Charted(s, index)) return SimResult.Fail("Already charted");
            if (s.Wilds.Surveying >= 0) return SimResult.Fail("A survey is already under way");
            if (!Reachable(s, index)) return SimResult.Fail("Chart a sector next to it first");
            if (!ResourceSystem.CanAfford(s, SurveyCost(index))) return SimResult.Fail("Not enough resources");
            return SimResult.Success;
        }

        public static SimResult StartSurvey(GameState s, int index)
        {
            var check = CheckSurvey(s, index);
            if (!check.Ok) return check;
            ResourceSystem.Spend(s, SurveyCost(index));
            s.Wilds.Surveying = index;
            s.Wilds.SurveyDoneTick = s.Tick + SurveySeconds(s, index);
            return SimResult.Success;
        }

        /// <summary>Speed-ups: take <paramref name="seconds"/> off the survey under way.</summary>
        public static void SpeedUpSurvey(GameState s, int seconds)
        {
            if (s.Wilds.Surveying < 0 || seconds <= 0) return;
            s.Wilds.SurveyDoneTick = Math.Max(s.Tick, s.Wilds.SurveyDoneTick - seconds);
        }

        /// <summary>What SURVEY NEXT picks: the cheapest reachable sector still under
        /// the fog, the one nearest <paramref name="nearLon"/> (-1 = none left).</summary>
        public static int NextSurvey(GameState s, double nearLon)
        {
            int best = -1, bestRing = int.MaxValue;
            double bestGap = double.MaxValue;
            for (int i = 0; i < WildsLayout.Total; i++)
            {
                if (Charted(s, i) || i == s.Wilds.Surveying || !Reachable(s, i)) continue;
                int ring = WildsLayout.RingOf(i);
                double gap = Math.Abs(WildsLayout.DeltaLon(nearLon, WildsLayout.Place(i).Lon));
                if (ring < bestRing || (ring == bestRing && gap < bestGap))
                {
                    best = i;
                    bestRing = ring;
                    bestGap = gap;
                }
            }
            return best;
        }

        /// <summary>Claim a cache's resources or a relic's Dark Matter.</summary>
        public static SimResult Claim(GameState s, int index) => Claim(s, index, out _);

        /// <summary>…and, for a relic, the relic that comes home for the Relic Vault (2026-09-30).</summary>
        public static SimResult Claim(GameState s, int index, out RelicKind? relic)
        {
            relic = null;
            var sector = Sector(s, index);
            if (sector == null || sector.Find is not (WildsFind.Cache or WildsFind.Relic))
                return SimResult.Fail("Nothing to claim here");
            if (sector.Claimed) return SimResult.Fail("Already claimed");
            ResourceSystem.Add(s, sector.Reward);
            s.Premium.DarkMatter += sector.RewardDM;
            if (sector.Find == WildsFind.Relic) relic = RelicSystem.Grant(s, index, sector.Surveys);
            sector.Claimed = true;
            sector.FogTick = s.Tick + Balance.WildsShiftSec;
            return SimResult.Success;
        }

        // ---------- tick ----------

        public static void Tick(GameState s, SimEventBus events)
        {
            var w = s.Wilds;
            if (w.Surveying >= 0 && s.Tick >= w.SurveyDoneTick)
            {
                int index = w.Surveying;
                w.Surveying = -1;
                var find = Reveal(s, index);
                events.Emit(new WildsSurveyed(index, find));
            }
            if (w.HarvestTick <= 0 || w.HarvestTick > s.Tick)
            {
                w.HarvestTick = s.Tick; // the drones start their rounds now (new game, old save)
                return;
            }
            int span = s.Tick - w.HarvestTick;
            if (span < HarvestEverySec) return;
            w.HarvestTick = s.Tick;
            Harvest(s, span);
            Upkeep(s);
        }

        /// <summary>The drones' haul for <paramref name="seconds"/>: shared across the
        /// deposits with stock left, straight into the treasury.</summary>
        static void Harvest(GameState s, int seconds)
        {
            var active = new List<WildsSector>();
            foreach (var sector in s.Wilds.Sectors.Values)
                if (sector.Find == WildsFind.Deposit && sector.StockMilli > 0) active.Add(sector);
            if (active.Count == 0) return;
            active.Sort((a, b) => a.Index.CompareTo(b.Index)); // deterministic whatever the dictionary order
            long budget = HaulPerHourMilli(s) * seconds / 3600;
            long share = budget / active.Count;
            if (share <= 0) return;
            var haul = new ResourceBag();
            foreach (var sector in active)
            {
                long take = Math.Min(share, sector.StockMilli);
                sector.StockMilli -= take;
                sector.HarvestedMilli += take;
                haul.Set(sector.Resource, haul.Get(sector.Resource) + take);
                if (sector.StockMilli == 0) sector.RefillTick = s.Tick + Balance.WildsRefillSec;
            }
            ResourceSystem.Add(s, haul);
            s.Wilds.Harvested.Add(haul);
        }

        /// <summary>Drained deposits fill back up; claimed finds drift back under the fog.</summary>
        static void Upkeep(GameState s)
        {
            foreach (var sector in s.Wilds.Sectors.Values)
            {
                if (sector.Find == WildsFind.Deposit && sector.StockMilli == 0
                    && sector.RefillTick > 0 && s.Tick >= sector.RefillTick)
                {
                    sector.StockMilli = sector.MaxMilli;
                    sector.RefillTick = 0;
                }
                else if (sector.Find is WildsFind.Cache or WildsFind.Relic && sector.Claimed
                         && sector.FogTick > 0 && s.Tick >= sector.FogTick)
                {
                    sector.Find = WildsFind.None;
                    sector.Claimed = false;
                    sector.FogTick = 0;
                    sector.Reward = new ResourceBag();
                    sector.RewardDM = 0;
                }
            }
        }

        /// <summary>What a survey turns up, from the galaxy's seed, the sector and how
        /// many times it has been surveyed before.</summary>
        public static WildsFind Reveal(GameState s, int index)
        {
            if (!s.Wilds.Sectors.TryGetValue(index, out var sector))
            {
                sector = new WildsSector { Index = index };
                s.Wilds.Sectors[index] = sector;
            }
            sector.Surveys++;
            int ring = WildsLayout.RingOf(index);
            uint seed = unchecked((uint)s.Seed * 0x9e3779b1u ^ (uint)(index * 7919) ^ (uint)(sector.Surveys * 104729));
            var rng = Rng.Mulberry32(seed);
            double roll = rng();
            sector.Claimed = false;
            sector.FogTick = 0;
            sector.RefillTick = 0;
            sector.Reward = new ResourceBag();
            sector.RewardDM = 0;
            sector.StockMilli = 0;
            sector.MaxMilli = 0;
            if (roll < 0.70)
            {
                sector.Find = WildsFind.Deposit;
                double kind = rng();
                sector.Resource = kind < 0.35 ? ResourceId.Gold : kind < 0.70 ? ResourceId.Quartz : ResourceId.Helium;
                long size = (long)Math.Round(Balance.WildsDepositSize(ring) * (0.7 + 0.6 * rng()));
                sector.MaxMilli = size * 1000;
                sector.StockMilli = sector.MaxMilli;
            }
            else if (roll < 0.92)
            {
                sector.Find = WildsFind.Cache;
                long total = (long)Math.Round(Balance.WildsCacheSize(ring) * (0.8 + 0.4 * rng())) * 1000;
                long gold = total * 45 / 100, quartz = total * 35 / 100;
                sector.Reward = new ResourceBag(gold, quartz, total - gold - quartz);
            }
            else
            {
                sector.Find = WildsFind.Relic;
                sector.RewardDM = Balance.WildsRelicDarkMatter(ring);
            }
            return sector.Find;
        }
    }
}
