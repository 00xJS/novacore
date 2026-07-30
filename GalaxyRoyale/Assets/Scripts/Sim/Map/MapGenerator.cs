// Deterministic node placement. Direct port of v1's `src/sim/map/MapGenerator.ts`.
// Same seed → identical sector — the static map is never saved; only diffs are.
using System;
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Map
{
    public static class MapGenerator
    {
        /// <summary>Keep camps out of the immediate starter area (tiles from home).</summary>
        const int HomeSafeRadius = 30;
        /// <summary>No nodes on/next to the home tile (tiles from home).</summary>
        const int HomeClearRadius = 6;

        /// <summary>Pure function of the seed. Same seed → identical sector.</summary>
        public static SectorData GenerateSector(int seed)
        {
            uint useed = unchecked((uint)seed);
            int size = Balance.SectorSize;
            var home = Balance.HomeTile;
            var core = new TileXY(size / 2, size / 2);
            var nodes = new Dictionary<string, MapNode>();

            int cs = Balance.NodeCellSize;
            int cells = (int)Math.Ceiling((double)size / cs);
            for (int cy = 0; cy < cells; cy++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    if (Rng.Hash2d(useed, cx, cy) >= Nodes.NodeCellOccupancy)
                    {
                        // Dark Matter fields spawn ONLY in otherwise-empty cells, so
                        // adding them (v13) never reshuffles the pre-existing layout.
                        if (Rng.Hash2d(useed ^ 0xD31Au, cx, cy) < 0.02)
                        {
                            int dmx = (int)Math.Floor(Rng.Hash2d(useed ^ 0x77AAu, cx, cy) * cs);
                            int dmy = (int)Math.Floor(Rng.Hash2d(useed ^ 0x33EEu, cx, cy) * cs);
                            var dmTile = new TileXY(cx * cs + dmx, cy * cs + dmy);
                            if (dmTile.X < size && dmTile.Y < size
                                && TileXY.Distance(dmTile, home) > HomeClearRadius)
                            {
                                nodes[dmTile.Key()] = new MapNode
                                {
                                    Id = dmTile.Key(),
                                    Kind = NodeKind.DMField,
                                    Tile = dmTile,
                                    Tier = Balance.TierOf((int)TileXY.Distance(dmTile, core)),
                                    Amount = 250_000,   // 250 DM (milli)
                                    RatePerSec = 6,     // ≈21.6 DM/h — the F2P premium path
                                    CampLevel = 0,
                                };
                            }
                        }
                        continue;
                    }

                    int jx = (int)Math.Floor(Rng.Hash2d(useed ^ 0x5f3au, cx, cy) * cs);
                    int jy = (int)Math.Floor(Rng.Hash2d(useed ^ 0x91cdu, cx, cy) * cs);
                    var tile = new TileXY(cx * cs + jx, cy * cs + jy);
                    if (tile.X >= size || tile.Y >= size) continue;
                    if (TileXY.Distance(tile, home) <= HomeClearRadius) continue;

                    int tier = Balance.TierOf((int)TileXY.Distance(tile, core));
                    var kind = PickKind(tier, Rng.Hash2d(useed ^ 0x2b7eu, cx, cy));
                    if (kind == NodeKind.Camp && TileXY.Distance(tile, home) < HomeSafeRadius)
                        kind = NodeKind.Asteroid;

                    float richness = Balance.TierRichnessMult[tier];
                    if (kind == NodeKind.Camp)
                    {
                        nodes[tile.Key()] = new MapNode
                        {
                            Id = tile.Key(),
                            Kind = kind,
                            Tile = tile,
                            Tier = tier,
                            Amount = 0,
                            RatePerSec = 0,
                            CampLevel = Balance.TierCampLevel[tier],
                        };
                    }
                    else
                    {
                        var def = Nodes.Defs[kind];
                        nodes[tile.Key()] = new MapNode
                        {
                            Id = tile.Key(),
                            Kind = kind,
                            Tile = tile,
                            Tier = tier,
                            Amount = (int)Math.Round(def.BaseAmount * richness) * 1000,
                            RatePerSec = (int)Math.Round(def.BaseRatePerSec * richness * 1000),
                            Resource = def.Resource,
                            CampLevel = 0,
                        };
                    }
                }
            }

            return new SectorData
            {
                Size = size,
                HomeTile = home,
                Core = core,
                Nodes = nodes,
            };
        }

        /// <summary>Weighted pick over NodeKinds per tier. Iterates in Nodes.All order for determinism.</summary>
        static NodeKind PickKind(int tier, double roll)
        {
            var weights = Nodes.TierKindWeights[tier];
            float total = 0;
            foreach (var kind in Nodes.All)
                if (weights.TryGetValue(kind, out var w)) total += w;

            double cursor = roll * total;
            foreach (var kind in Nodes.All)
            {
                if (!weights.TryGetValue(kind, out var w)) continue;
                cursor -= w;
                if (cursor <= 0) return kind;
            }
            return NodeKind.Asteroid;
        }
    }
}
