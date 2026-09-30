// Sector cache + node/tile lookup helpers. Extracted from v1's MarchSystem so
// MapSystem doesn't have to depend on the (larger) MarchSystem port later.
// Every lookup goes through this class; the sector is memoized per seed.
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Map
{
    public static class MapLookup
    {
        static int _cachedSeed;
        static SectorData? _cachedSector;

        /// <summary>The static sector for `state.Seed`. Memoized — regenerates on seed change.</summary>
        public static SectorData GetSector(GameState state)
        {
            if (_cachedSector == null || _cachedSeed != state.Seed)
            {
                _cachedSeed = state.Seed;
                _cachedSector = MapGenerator.GenerateSector(state.Seed);
            }
            return _cachedSector;
        }

        /// <summary>A dynamically-respawned node sitting on this tile, if any.</summary>
        public static MapNode? DynamicNodeAt(GameState state, TileXY tile)
        {
            foreach (var n in state.Map.DynamicNodes)
                if (n.Tile.X == tile.X && n.Tile.Y == tile.Y) return InZone(n) ? null : n;
            return null;
        }

        /// <summary>A respawn a save made before the Core Zone existed may sit inside
        /// it; such nodes are treated as gone.</summary>
        static bool InZone(MapNode n) => Balance.InCoreZone(n.Tile);

        /// <summary>The live node at a tile — a dynamic respawn, or a non-retired seed node.</summary>
        public static MapNode? NodeAt(GameState state, TileXY tile)
        {
            var dyn = DynamicNodeAt(state, tile);
            if (dyn != null) return dyn;
            var sector = GetSector(state);
            if (!sector.Nodes.TryGetValue(tile.Key(), out var stat)) return null;
            if (state.Map.NodeOverrides.TryGetValue(stat.Id, out var ov) && ov.Retired) return null;
            return stat;
        }

        /// <summary>Look a node up by id (tile-key for static, `dyn-N` for dynamic).</summary>
        public static MapNode? NodeById(GameState state, string id)
        {
            if (id.StartsWith("dyn-", StringComparison.Ordinal))
            {
                foreach (var n in state.Map.DynamicNodes)
                    if (n.Id == id) return InZone(n) ? null : n;
                return null;
            }
            return GetSector(state).Nodes.TryGetValue(id, out var n2) ? n2 : null;
        }

        /// <summary>Every live node: non-retired seed nodes + dynamic respawns.</summary>
        public static System.Collections.Generic.List<MapNode> AllNodes(GameState state)
        {
            var out_ = new System.Collections.Generic.List<MapNode>();
            var sector = GetSector(state);
            foreach (var n in sector.Nodes.Values)
            {
                if (state.Map.NodeOverrides.TryGetValue(n.Id, out var ov) && ov.Retired) continue;
                out_.Add(n);
            }
            foreach (var n in state.Map.DynamicNodes)
                if (!InZone(n)) out_.Add(n);
            return out_;
        }

        /// <summary>A tile is blank when it's in-bounds, has no seed node, and isn't the current home.</summary>
        public static bool IsBlankTile(GameState state, TileXY tile)
        {
            var sector = GetSector(state);
            if (tile.X < 0 || tile.Y < 0 || tile.X >= sector.Size || tile.Y >= sector.Size) return false;
            if (Balance.InCoreZone(tile)) return false; // respawns and ports never land in the Core Zone
            if (sector.Nodes.ContainsKey(tile.Key())) return false;
            return tile.X != state.HomeTile.X || tile.Y != state.HomeTile.Y;
        }

        /// <summary>Free = blank AND no dynamic node there.</summary>
        public static bool IsTileFree(GameState state, TileXY tile) =>
            IsBlankTile(state, tile) && DynamicNodeAt(state, tile) == null;

        /// <summary>Respawned nodes keep this much distance from the player's planet
        /// (user report: relocated resource tiles were touching the home world).</summary>
        public const int HomeRespawnClearance = 5;

        /// <summary>Nearest free tile in an expanding ring around `from` (deterministic scan). Null = none in maxR.</summary>
        public static TileXY? NearbyFreeTile(GameState state, TileXY from, int maxR = 6)
        {
            for (int r = 1; r <= maxR; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue; // ring perimeter only
                        var tile = new TileXY(from.X + dx, from.Y + dy);
                        if (TileXY.Distance(tile, state.HomeTile) <= HomeRespawnClearance) continue;
                        if (IsTileFree(state, tile)) return tile;
                    }
                }
            }
            return null;
        }
    }
}
