// Every world in the galaxy in one draw (map redesign, 2026-09-29). The user
// wants to see ALL of the planets zoomed all the way out, so resource worlds are
// no longer one GameObject each (that capped out, and they hid past a zoom):
// one mesh holds a camera-facing quad per live node, textured from the planet
// atlas (Resources/Map/map-planets, 3 × 3 cells — scripts/art/map_art.py).
//
// The map camera never rotates (it pans and dollies at a fixed tilt), so the
// quads are baked facing it. The mesh is rebuilt when the node set changes
// (depleted, respawned, filtered) and its corners re-spread when the zoom moves
// enough to change the world size (planets keep a readable size on screen).
using System.Collections.Generic;
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Map;

namespace GalaxyRoyale.Game
{
    public sealed class GalaxyField
    {
        /// <summary>The sphere fills half its atlas cell (rings and glow take the rest).</summary>
        public const float QuadPerPlanet = 2f;

        readonly MeshFilter _filter;
        readonly MeshRenderer _renderer;
        readonly Mesh _mesh;
        readonly List<Vector3> _centres = new();
        readonly List<int> _cells = new();
        Vector3[] _verts = System.Array.Empty<Vector3>();
        Quaternion _facing = Quaternion.identity;
        float _planetSize = -1f;

        public GalaxyField(Transform parent, System.Func<TileXY, Vector3> tileToWorld)
        {
            _tileToWorld = tileToWorld;
            var go = new GameObject("Galaxy Field");
            go.transform.SetParent(parent, false);
            _filter = go.AddComponent<MeshFilter>();
            _renderer = go.AddComponent<MeshRenderer>();
            _mesh = new Mesh { name = "Galaxy Field", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;
            var atlas = UnityEngine.Resources.Load<Texture2D>("Map/map-planets");
            _renderer.sharedMaterial = MapVisuals.UnlitTransparent(atlas != null ? atlas : Texture2D.whiteTexture);
            _renderer.sortingOrder = 10;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        readonly System.Func<TileXY, Vector3> _tileToWorld;

        public static int Cell(NodeKind kind) => kind switch
        {
            NodeKind.Asteroid => 0,
            NodeKind.Nebula => 1,
            NodeKind.HeliumCloud => 2,
            NodeKind.Derelict => 3,
            NodeKind.Camp => 4,
            _ => 5, // DMField
        };

        /// <summary>Which worlds draw: the map's layer filter.</summary>
        public System.Func<NodeKind, bool> Show = _ => true;

        /// <summary>Re-collect the live worlds (after a depletion, respawn or filter change).</summary>
        public void Rebuild(GameState state)
        {
            _centres.Clear();
            _cells.Clear();
            var sector = MapLookup.GetSector(state);
            foreach (var node in sector.Nodes.Values)
            {
                state.Map.NodeOverrides.TryGetValue(node.Id, out var ov);
                if (ov != null && (ov.Retired || ov.Cleared)) continue;
                if (node.Kind != NodeKind.Camp && (ov?.Remaining ?? node.Amount) <= 0) continue;
                Add(node);
            }
            foreach (var node in MapLookup.AllNodes(state))
                if (node.Id.StartsWith("dyn-", System.StringComparison.Ordinal)) Add(node);

            int n = _centres.Count;
            _verts = new Vector3[n * 4];
            var uvs = new Vector2[n * 4];
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int cell = _cells[i];
                float u0 = (cell % 3) / 3f, u1 = u0 + 1f / 3f;
                float v1 = 1f - (cell / 3) / 3f, v0 = v1 - 1f / 3f;
                int v = i * 4;
                uvs[v] = new Vector2(u0, v0);
                uvs[v + 1] = new Vector2(u1, v0);
                uvs[v + 2] = new Vector2(u1, v1);
                uvs[v + 3] = new Vector2(u0, v1);
                int t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            }
            _mesh.Clear();
            _planetSize = -1f; // force the corners
            Spread(_lastFacing, _lastSize);
            _mesh.uv = uvs;
            _mesh.triangles = tris;
            _mesh.bounds = new Bounds(new Vector3(Balance.SectorSize * 0.5f, -Balance.SectorSize * 0.5f, 0f),
                new Vector3(Balance.SectorSize + 200f, Balance.SectorSize + 200f, 400f));
        }

        void Add(MapNode node)
        {
            if (!Show(node.Kind)) return;
            _centres.Add(_tileToWorld(node.Tile));
            _cells.Add(Cell(node.Kind));
        }

        Quaternion _lastFacing = Quaternion.identity;
        float _lastSize = 5f;

        /// <summary>Face the camera and size each world (planet diameter in world units).
        /// Cheap to call every frame: it only rewrites the corners when something changed.</summary>
        public void Spread(Quaternion facing, float planetSize)
        {
            _lastFacing = facing;
            _lastSize = planetSize;
            if (Mathf.Abs(planetSize - _planetSize) < _planetSize * 0.04f && facing == _facing) return;
            _planetSize = planetSize;
            _facing = facing;
            float h = planetSize * QuadPerPlanet * 0.5f;
            var right = facing * Vector3.right * h;
            var up = facing * Vector3.up * h;
            for (int i = 0; i < _centres.Count; i++)
            {
                var c = _centres[i];
                int v = i * 4;
                _verts[v] = c - right - up;
                _verts[v + 1] = c + right - up;
                _verts[v + 2] = c + right + up;
                _verts[v + 3] = c - right + up;
            }
            _mesh.vertices = _verts;
        }

        public void SetVisible(bool on) => _renderer.enabled = on;
    }
}
