// Map favorites — bookmarked tiles (planets, nodes, empty staging space),
// stored locally in PlayerPrefs as JSON. Device-local by design for now;
// syncing them to the account is trivial later if wanted.
using System.Collections.Generic;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Save;
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public sealed class FavoriteSpot
    {
        public string Name = "";
        public int X;
        public int Y;
    }

    public static class Favorites
    {
        const string PrefsKey = "galaxyroyale.favorites";
        const int Max = 30;

        public static List<FavoriteSpot> All()
        {
            var spots = new List<FavoriteSpot>();
            string raw = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(raw)) return spots;
            try
            {
                if (Json.Parse(raw) is not List<object?> rows) return spots;
                foreach (var r in rows)
                    if (r is Dictionary<string, object?> d)
                        spots.Add(new FavoriteSpot
                        {
                            Name = d["name"] as string ?? "",
                            X = d.TryGetValue("x", out var x) && x is long xl ? (int)xl : 0,
                            Y = d.TryGetValue("y", out var y) && y is long yl ? (int)yl : 0,
                        });
            }
            catch (System.Exception) { }
            return spots;
        }

        public static bool IsFavorite(TileXY tile)
        {
            foreach (var s in All())
                if (s.X == tile.X && s.Y == tile.Y) return true;
            return false;
        }

        /// <summary>Add (or re-name) a spot. Returns false when the list is full.</summary>
        public static bool Add(string name, TileXY tile)
        {
            var spots = All();
            spots.RemoveAll(s => s.X == tile.X && s.Y == tile.Y);
            if (spots.Count >= Max) return false;
            spots.Add(new FavoriteSpot { Name = name, X = tile.X, Y = tile.Y });
            Save(spots);
            return true;
        }

        public static void Remove(TileXY tile)
        {
            var spots = All();
            spots.RemoveAll(s => s.X == tile.X && s.Y == tile.Y);
            Save(spots);
        }

        static void Save(List<FavoriteSpot> spots)
        {
            var rows = new List<object?>();
            foreach (var s in spots)
                rows.Add(new Dictionary<string, object?>
                {
                    ["name"] = s.Name,
                    ["x"] = (long)s.X,
                    ["y"] = (long)s.Y,
                });
            PlayerPrefs.SetString(PrefsKey, Json.Write(rows));
            PlayerPrefs.Save();
        }
    }
}
