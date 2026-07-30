// Sub-tile floating position. Marches need this: recall mid-flight leaves the
// fleet at a fractional coordinate that must be preserved for the return-leg
// distance calc. v1 uses `number` throughout for TileXY; C# splits them:
// TileXY is integer grid, Position is float sub-tile.
using System;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Sim
{
    public readonly struct Position
    {
        public readonly double X;
        public readonly double Y;

        public Position(double x, double y) { X = x; Y = y; }

        public static implicit operator Position(TileXY t) => new(t.X, t.Y);

        public static double Distance(Position a, Position b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double DistanceToTile(Position a, TileXY b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
