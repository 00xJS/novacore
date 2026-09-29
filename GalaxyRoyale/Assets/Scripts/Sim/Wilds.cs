// The Wilds (2026-09-29): the southern half of the home planet, under survey
// fog. 58 sectors in five rings from just south of the equator to the pole:
// survey them one at a time, starting from the first ring and spreading south
// from what's charted. A survey finds a deposit (a gold seam, a crystal field or
// a helium vent) for the harvester drones to work, a supply cache or a relic.
// Drained deposits refill; claimed finds drift back under the fog to be found
// again (WildsSystem), so the Wilds never run out.
//
// Pure data: WildsLayout places the sectors (BaseVisuals-style latitude and
// longitude), GameState.Wilds holds what's been found.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim
{
    /// <summary>What a survey found in a sector (None: still under the fog).</summary>
    public enum WildsFind { None, Deposit, Cache, Relic }

    /// <summary>A sector the player has surveyed (sectors never touched aren't stored).</summary>
    public sealed class WildsSector
    {
        public int Index;
        public WildsFind Find;
        /// <summary>Deposits: the resource, what's left and the size when full (milli).</summary>
        public ResourceId Resource;
        public long StockMilli;
        public long MaxMilli;
        /// <summary>A drained deposit fills back up at this tick (0 = not waiting).</summary>
        public int RefillTick;
        /// <summary>Caches: the resources inside (milli). Relics: the Dark Matter.</summary>
        public ResourceBag Reward = new();
        public int RewardDM;
        public bool Claimed;
        /// <summary>A claimed find's sector goes back under the fog at this tick.</summary>
        public int FogTick;
        /// <summary>Surveys of this sector so far (they seed what the next one finds).</summary>
        public int Surveys;
        /// <summary>What the drones have brought home from it (milli).</summary>
        public long HarvestedMilli;
    }

    public sealed class WildsState
    {
        public Dictionary<int, WildsSector> Sectors = new();
        /// <summary>The sector being surveyed (-1 = none) and the tick the survey lands.</summary>
        public int Surveying = -1;
        public int SurveyDoneTick;
        /// <summary>The drones' last run (they're settled up once a minute; 0 = not started).</summary>
        public int HarvestTick;
        /// <summary>Everything the drones have brought home (milli).</summary>
        public ResourceBag Harvested = new();
    }

    public static class WildsLayout
    {
        /// <summary>The rings, north to south: their latitude and how many sectors each holds.</summary>
        public static readonly IReadOnlyList<(double Lat, int Count)> Rings = new[]
        {
            (-13.0, 18), (-28.0, 16), (-43.0, 12), (-58.0, 8), (-73.0, 4),
        };

        public static readonly int Total;
        static readonly int[] s_ringStart;
        static readonly IReadOnlyList<int>[] s_neighbours;

        static WildsLayout()
        {
            s_ringStart = new int[Rings.Count];
            int n = 0;
            for (int r = 0; r < Rings.Count; r++)
            {
                s_ringStart[r] = n;
                n += Rings[r].Count;
            }
            Total = n;
            s_neighbours = new IReadOnlyList<int>[Total];
            for (int i = 0; i < Total; i++) s_neighbours[i] = FindNeighbours(i);
        }

        public static bool Valid(int index) => index >= 0 && index < Total;

        public static int RingOf(int index)
        {
            for (int r = Rings.Count - 1; r >= 0; r--)
                if (index >= s_ringStart[r]) return r;
            return 0;
        }

        public static int Index(int ring, int k) => s_ringStart[ring] + ((k % Rings[ring].Count) + Rings[ring].Count) % Rings[ring].Count;

        /// <summary>The sector's centre and how many degrees of longitude it spans.</summary>
        public static (double Lat, double Lon, double Width) Place(int index)
        {
            int ring = RingOf(index);
            int n = Rings[ring].Count;
            int k = index - s_ringStart[ring];
            double width = 360.0 / n;
            return (Rings[ring].Lat, -180 + (k + 0.5) * width, width);
        }

        /// <summary>Sectors in the ring to the north whose span overlaps this one's: a
        /// survey there spreads the charted land south to here.</summary>
        public static IReadOnlyList<int> Neighbours(int index) => s_neighbours[index];

        static List<int> FindNeighbours(int index)
        {
            var list = new List<int>();
            int ring = RingOf(index);
            if (ring == 0) return list;
            var (_, lon, width) = Place(index);
            int up = ring - 1;
            for (int k = 0; k < Rings[up].Count; k++)
            {
                int j = s_ringStart[up] + k;
                var (_, lon2, width2) = Place(j);
                if (Math.Abs(DeltaLon(lon, lon2)) < (width + width2) * 0.5 - 1e-6) list.Add(j);
            }
            return list;
        }

        /// <summary>Signed smallest difference between two longitudes (degrees).</summary>
        public static double DeltaLon(double a, double b)
        {
            double d = (b - a) % 360.0;
            if (d > 180) d -= 360;
            if (d < -180) d += 360;
            return d;
        }

        /// <summary>"A-07": the ring's letter and the sector's number around it.</summary>
        public static string Name(int index)
        {
            int ring = RingOf(index);
            return $"{(char)('A' + ring)}-{index - s_ringStart[ring] + 1:00}";
        }
    }
}
