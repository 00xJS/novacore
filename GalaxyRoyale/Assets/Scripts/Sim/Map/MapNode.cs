// A live node on the galactic map. Direct port of v1's MapNode
// (`src/sim/map/MapGenerator.ts`). Static seed-generated nodes and dynamic
// respawns share this shape; ids for dynamic nodes are prefixed `dyn-`.
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Map
{
    public sealed class MapNode
    {
        /// <summary>Static nodes: tile key. Dynamic respawns: `dyn-N`.</summary>
        public string Id = "";
        public NodeKind Kind;
        public TileXY Tile;
        /// <summary>0 (outer rim) … 4 (core).</summary>
        public int Tier;
        /// <summary>Resource nodes: gatherable milli-units. Derelicts: total loot milli. Camps: 0.</summary>
        public int Amount;
        /// <summary>Milli-units/sec gather speed (resource nodes only).</summary>
        public int RatePerSec;
        /// <summary>What gathering yields. Null for derelicts + camps.</summary>
        public ResourceId? Resource;
        /// <summary>Camp garrison level (1..5); 0 for non-camp.</summary>
        public int CampLevel;
    }
}
