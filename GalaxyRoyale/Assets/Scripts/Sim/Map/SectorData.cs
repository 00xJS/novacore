// The static sector — a pure function of the seed. See MapGenerator.
using System.Collections.Generic;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim.Map
{
    public sealed class SectorData
    {
        public int Size;
        public TileXY HomeTile;
        public TileXY Core;
        /// <summary>Keyed by TileXY.Key() (`"x,y"`).</summary>
        public Dictionary<string, MapNode> Nodes = new();
    }
}
