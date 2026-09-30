// Deterministic spawn placement. Originally the shared-server spawner (FNV-1a
// of the account id); now it seats the player on the outer rim and SCATTERS
// the 99 simulated commanders across the whole disk (user feedback 2026-07-07:
// everyone on one narrow rim ring read as "not random"). Candidates avoid
// static nodes — including a clearance ring so resource tiles never hug a
// planet — taken home tiles, and the supernova core zone. Sim-pure.
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Sim
{
    public static class Spawn
    {
        /// <summary>The one galaxy seed (0xF0CAC01A) — every save uses the same static map.</summary>
        public const int GalaxySeed = unchecked((int)0xF0CAC01Au);

        /// <summary>No static node may sit within this many tiles of a spawned home
        /// (user report: resource tiles were touching planets).</summary>
        public const int HomeNodeClearance = 4;

        static SectorData? _sector;
        static int _sectorSeed;
        static SectorData Sector(int seed)
        {
            if (_sector == null || _sectorSeed != seed)
            {
                _sector = MapGenerator.GenerateSector(seed);
                _sectorSeed = seed;
            }
            return _sector;
        }

        /// <summary>FNV-1a over the id string — spread, not cryptography.</summary>
        static uint HashId(string s)
        {
            uint h = 0x811c9dc5;
            foreach (char c in s)
            {
                h ^= c;
                h = unchecked(h * 0x01000193);
            }
            return h;
        }

        /// <summary>Candidate home check: inside bounds, off taken homes, outside the
        /// core zone, and no static node within `clear` tiles.</summary>
        static bool IsClearHomeSite(SectorData sector, TileXY tile, HashSet<string>? taken, int clear)
        {
            if (taken != null && taken.Contains(tile.Key())) return false;
            int center = Balance.SectorSize / 2;
            double ox = tile.X - center, oy = tile.Y - center, keep = Balance.CoreZoneRadius + clear;
            if (ox * ox + oy * oy <= keep * keep) return false; // no homes in the Core Zone
            for (int dy = -clear; dy <= clear; dy++)
                for (int dx = -clear; dx <= clear; dx++)
                    if (sector.Nodes.ContainsKey(new TileXY(tile.X + dx, tile.Y + dy).Key()))
                        return false;
            return true;
        }

        /// <summary>
        /// Pick a home tile for a new empire inside the [rMinFrac, rMaxFrac] radius
        /// band (fractions of the half-size). Defaults = the player's outer-rim ring;
        /// the bot galaxy passes a wide band to scatter rivals across the disk.
        /// Deterministic per id string.
        /// </summary>
        public static TileXY SpawnTileFor(string id, int galaxySeed, HashSet<string>? taken = null,
            double rMinFrac = 0.94, double rMaxFrac = 1.02)
        {
            var sector = Sector(galaxySeed);
            uint h = HashId(id);
            int size = Balance.SectorSize;
            int center = size / 2;
            int rMin = (int)System.Math.Floor(center * rMinFrac);
            int rSpan = System.Math.Max(1, (int)System.Math.Floor(center * (rMaxFrac - rMinFrac)));

            for (int attempt = 0; attempt < 128; attempt++)
            {
                uint a = unchecked(h + (uint)(attempt * 7919));
                double angle = (a % 4096) / 4096.0 * System.Math.PI * 2;
                int radius = rMin + (int)((((h >> 5) + (uint)(attempt * 131)) & 0xffff) % (uint)rSpan);
                int x = (int)System.Math.Round(center + System.Math.Cos(angle) * radius);
                int y = (int)System.Math.Round(center + System.Math.Sin(angle) * radius);
                int cx = System.Math.Max(4, System.Math.Min(size - 5, x));
                int cy = System.Math.Max(4, System.Math.Min(size - 5, y));
                var cand = new TileXY(cx, cy);
                // Late attempts relax the clearance ring (never the exact-tile rules)
                // so a crowded band still seats everyone.
                int clear = attempt < 96 ? HomeNodeClearance : 0;
                if (!IsClearHomeSite(sector, cand, taken, clear)) continue;
                return cand;
            }
            return new TileXY(size - 10, 10); // absurdly unlikely fallback
        }

        /// <summary>
        /// Deterministic empty tile within [minR, maxR] tiles of `target` — the bots'
        /// precision hunt-jump landing pad. Null when nothing nearby qualifies.
        /// </summary>
        public static TileXY? TileNear(string id, int galaxySeed, TileXY target,
            int minR, int maxR, HashSet<string>? taken = null)
        {
            var sector = Sector(galaxySeed);
            uint h = HashId(id);
            int size = Balance.SectorSize;
            for (int attempt = 0; attempt < 64; attempt++)
            {
                uint a = unchecked(h + (uint)(attempt * 7919));
                double angle = (a % 4096) / 4096.0 * System.Math.PI * 2;
                int radius = minR + (int)((((h >> 7) + (uint)(attempt * 53)) & 0xffff)
                    % (uint)System.Math.Max(1, maxR - minR));
                int x = (int)System.Math.Round(target.X + System.Math.Cos(angle) * radius);
                int y = (int)System.Math.Round(target.Y + System.Math.Sin(angle) * radius);
                if (x < 4 || y < 4 || x >= size - 4 || y >= size - 4) continue;
                var cand = new TileXY(x, y);
                if (!IsClearHomeSite(sector, cand, taken, HomeNodeClearance)) continue;
                return cand;
            }
            return null;
        }

        /// <summary>Back-compat shim for the original account-spawn call shape.</summary>
        public static TileXY SpawnTileForUser(string userId) => SpawnTileFor(userId, GalaxySeed);
    }
}
