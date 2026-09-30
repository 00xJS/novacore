// The globe base's surface overlay (2026-09-28): a deck under each district
// (the Spaceport's on the far side since 2026-09-29), the resource lanes that run from each first mine out
// through the Mining Belt, the conduits between the Command Center and its
// neighbours, and a hex pad under every building spot.
// Everything is parented to the planet, so it turns with it, and sits on the
// default layer, so the planet hides whatever is on the far side.
using System.Collections.Generic;
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Game.UI;
using GalaxyRoyale.Sim;

namespace GalaxyRoyale.Game
{
    public static class BaseVisuals
    {
        public static readonly Color GoldLane = new(0.9f, 0.93f, 0.97f);
        public static readonly Color QuartzLane = new(0.24f, 0.96f, 1f);
        public static readonly Color HeliumLane = new(0.24f, 1f, 0.63f);
        public static readonly Color Muted = new(0.31f, 0.35f, 0.5f);

        static Material? s_lineMat;
        static Material? s_dashMat;

        /// <summary>Vertex-coloured, alpha-blended, unlit: lines and pad meshes (the Wilds' tiles,
        /// the Spaceport's field).</summary>
        public static Material SurfaceMaterial() => LineMaterial();

        static Material LineMaterial()
        {
            if (s_lineMat != null) return s_lineMat;
            s_lineMat = MapVisuals.SpriteDefault() ?? new Material(Shader.Find("Sprites/Default"));
            s_lineMat.renderQueue = 3005;
            return s_lineMat;
        }

        /// <summary>The same, dashed along its length (locked lanes, the uncharted edge).</summary>
        static Material DashMaterial()
        {
            if (s_dashMat != null) return s_dashMat;
            var tex = new Texture2D(8, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            for (int i = 0; i < 8; i++) tex.SetPixel(i, 0, i < 4 ? Color.white : Color.clear);
            tex.Apply();
            s_dashMat = new Material(LineMaterial()) { mainTexture = tex };
            return s_dashMat;
        }

        /// <summary>A point on the planet in its local space. East is +x at the equator's front.</summary>
        public static Vector3 Point(double latDeg, double lonDeg, float radius)
        {
            float lat = (float)latDeg * Mathf.Deg2Rad, lon = (float)lonDeg * Mathf.Deg2Rad;
            return new Vector3(
                radius * Mathf.Cos(lat) * Mathf.Sin(lon),
                radius * Mathf.Sin(lat),
                -radius * Mathf.Cos(lat) * Mathf.Cos(lon));
        }

        static LineRenderer Line(Transform parent, string name, IList<Vector3> points, Color color, float width,
            bool dashed = false, bool loop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = loop;
            lr.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) lr.SetPosition(i, points[i]);
            lr.startWidth = lr.endWidth = width;
            lr.startColor = lr.endColor = color;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = dashed ? DashMaterial() : LineMaterial();
            if (dashed)
            {
                lr.textureMode = LineTextureMode.Tile;
                lr.textureScale = new Vector2(1f / (width * 6f), 1f);
            }
            return lr;
        }

        static List<Vector3> Parallel(double lat, double lon0, double lon1, float r, double step = 3)
        {
            var pts = new List<Vector3>();
            int n = Mathf.Max(2, (int)System.Math.Ceiling(System.Math.Abs(lon1 - lon0) / step));
            for (int i = 0; i <= n; i++) pts.Add(Point(lat, lon0 + (lon1 - lon0) * i / n, r));
            return pts;
        }

        static List<Vector3> Arc(double lat0, double lon0, double lat1, double lon1, float r, int n = 20)
        {
            var a = Point(lat0, lon0, 1f);
            var b = Point(lat1, lon1, 1f);
            var pts = new List<Vector3>();
            for (int i = 0; i <= n; i++) pts.Add(Vector3.Slerp(a, b, i / (float)n).normalized * r);
            return pts;
        }

        /// <summary>A district's outline: a lat/lon box with cut corners.</summary>
        static List<Vector3> DeckOutline(double lat0, double lat1, double lon0, double lon1, float r, double cut = 5)
        {
            var corners = new (double lat, double lon)[]
            {
                (lat1, lon0 + cut), (lat1, lon1 - cut), (lat1 - cut, lon1), (lat0 + cut, lon1),
                (lat0, lon1 - cut), (lat0, lon0 + cut), (lat0 + cut, lon0), (lat1 - cut, lon0),
            };
            var pts = new List<Vector3>();
            for (int i = 0; i < corners.Length; i++)
            {
                var (la, lo) = corners[i];
                var (lb, lb2) = corners[(i + 1) % corners.Length];
                int n = Mathf.Max(2, (int)(System.Math.Max(System.Math.Abs(lb - la), System.Math.Abs(lb2 - lo)) / 2));
                for (int k = 0; k < n; k++)
                {
                    double t = k / (double)n;
                    pts.Add(Point(la + (lb - la) * t, lo + (lb2 - lo) * t, r));
                }
            }
            return pts;
        }

        /// <summary>A faint filled patch over a district (a lat/lon grid mesh).</summary>
        static GameObject DeckFill(Transform parent, string name, double lat0, double lat1, double lon0, double lon1,
            float r, Color color)
        {
            const int n = 16;
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    verts.Add(Point(lat0 + (lat1 - lat0) * i / n, lon0 + (lon1 - lon0) * j / n, r));
                    cols.Add(color);
                }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    int a = i * (n + 1) + j, b = a + 1, c = a + n + 1, d = c + 1;
                    tris.AddRange(new[] { a, c, b, b, c, d });
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = LineMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>The grid, decks, conduits and a lanes holder (refreshed by <see cref="RefreshLanes"/>).</summary>
        public static Transform BuildOverlay(Transform planet, float radius)
        {
            var root = new GameObject("Base Overlay").transform;
            root.SetParent(planet, false);
            float r = radius * 1.003f;
            float thin = radius * 0.0032f;
            var accent = UiTheme.Accent;

            // No lat/lon grid (user 2026-09-29: it read as time zones and wasn't
            // needed); the district decks, conduits and lanes carry the hologram look.

            var decks = new GameObject("District Decks").transform;
            decks.SetParent(root, false);
            void Deck(string name, double lon0, double lon1)
            {
                var outline = DeckOutline(0, 54, lon0, lon1, r * 1.0005f);
                DeckFill(decks, name + " Fill", 0.5, 53.5, lon0 + 1, lon1 - 1, r * 0.9995f, UiTheme.A(accent, 0.035f));
                Line(decks, name, outline, UiTheme.A(accent, 0.55f), thin * 2f, loop: true);
            }
            Deck("Command Deck", -33, 33);
            Deck("Mining Belt Deck", 58, 122);
            Deck("Frontier Deck", -121, -59);
            Deck("Spaceport Deck", 150, 210);
            // The Citadel (2026-09-30): a ring round the north pole, spokes to the Terraformer.
            Line(decks, "Citadel Deck", Parallel(BaseLayout.CitadelRing - 9, 0, 360, r * 1.0005f, 3), UiTheme.A(accent, 0.55f), thin * 2f);
            Line(decks, "Citadel Ring", Parallel(BaseLayout.CitadelRing, 0, 360, r * 1.001f, 3), UiTheme.A(accent, 0.3f),
                thin * 1.6f, dashed: true);

            var conduits = new GameObject("Conduits").transform;
            conduits.SetParent(root, false);
            var cc = BaseLayout.BuildingPad(BuildingId.CommandCenter);
            foreach (var p in BaseLayout.Pads)
            {
                if (p.Kind != PadKind.Building || p.Building == BuildingId.CommandCenter) continue;
                Line(conduits, $"To {p.Key}", Arc(cc.Lat, cc.Lon, p.Lat, p.Lon, r * 1.001f), UiTheme.A(accent, 0.4f),
                    thin * 2.2f, dashed: true);
            }
            // The road west to the Frontier, and the Frontier's own spokes.
            Line(conduits, "Frontier Road", Parallel(BaseLayout.MiddleRow, -BaseLayout.CoreSpread, -66, r * 1.001f), UiTheme.A(accent, 0.3f),
                thin * 1.6f, dashed: true);
            var hub = BaseLayout.BuildingPad(BuildingId.Observatory); // the Frontier's centre pad
            foreach (var p in BaseLayout.Pads)
                if (p.District == BaseDistrict.Frontier && p != hub)
                    Line(conduits, $"To {p.Key}", Arc(hub.Lat, hub.Lon, p.Lat, p.Lon, r * 1.001f), UiTheme.A(accent, 0.25f),
                        thin * 1.6f, dashed: true);

            foreach (var p in BaseLayout.Pads)
                if (p.District == BaseDistrict.Citadel && p.Building != BuildingId.Terraformer)
                    Line(conduits, $"To {p.Key}", Arc(89.5, p.Lon, p.Lat, p.Lon, r * 1.001f), UiTheme.A(accent, 0.25f),
                        thin * 1.6f, dashed: true);

            // The roads round the back to the Spaceport, from the Mining Belt and the Frontier.
            Line(conduits, "Port Road East", Parallel(BaseLayout.MiddleRow, 124, 157, r * 1.001f), UiTheme.A(accent, 0.3f),
                thin * 1.6f, dashed: true);
            Line(conduits, "Port Road West", Parallel(BaseLayout.MiddleRow, -124, -157, r * 1.001f), UiTheme.A(accent, 0.3f),
                thin * 1.6f, dashed: true);

            var lanes = new GameObject("Lanes").transform;
            lanes.SetParent(root, false);
            return root;
        }

        public static Color LaneColor(MineType type) => type switch
        {
            MineType.GoldMine => GoldLane,
            MineType.QuartzExtractor => QuartzLane,
            _ => HeliumLane,
        };

        /// <summary>Each resource's lane: bright out to the last unlocked belt column, dashed beyond.</summary>
        public static void RefreshLanes(Transform overlay, float radius, int commandCenter)
        {
            var lanes = overlay.Find("Lanes");
            if (lanes == null) return;
            for (int i = lanes.childCount - 1; i >= 0; i--) Object.Destroy(lanes.GetChild(i).gameObject);
            float r = radius * 1.002f;
            double start = BaseLayout.CoreSpread + 6, open = start;
            for (int t = 0; t < BaseLayout.BeltLon.Length; t++)
                if (commandCenter >= Balance.MineSlotUnlocks[t + 1]) open = BaseLayout.BeltLon[t];
            double end = BaseLayout.BeltLon[^1] + 7;
            foreach (var type in MineTypes.All)
            {
                double lat = BaseLayout.MineRow(type);
                var c = LaneColor(type);
                if (open > start)
                    Line(lanes, $"{type} Lane", Parallel(lat, start, open, r, 2), UiTheme.A(c, 0.55f), radius * 0.01f);
                Line(lanes, $"{type} Lane Ahead", Parallel(lat, open, end, r, 2), UiTheme.A(c, 0.28f),
                    radius * 0.007f, dashed: true);
            }
        }

        // ---------- pads ----------

        public enum PadLook { Built, Open, Locked, Planned, Reserved }

        /// <summary>A flat hex pad in the XZ plane (y up), ring plus a faint fill. <paramref name="ringWidth"/>
        /// is the ring's share of the radius (0 = the pads' usual weight); big fields want a thin one.</summary>
        public static Mesh PadMesh(float size, Color ring, Color fill, bool dashed, float ringWidth = 0f)
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            Vector3 Corner(float r, int i)
            {
                float a = (90f - 60f * i) * Mathf.Deg2Rad; // a point at north
                return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            // Fill: a hex fan.
            if (fill.a > 0f)
            {
                verts.Add(Vector3.zero); cols.Add(fill);
                for (int i = 0; i < 6; i++) { verts.Add(Corner(size * 0.9f, i)); cols.Add(fill); }
                for (int i = 0; i < 6; i++) tris.AddRange(new[] { 0, 1 + (i + 1) % 6, 1 + i });
            }
            // Ring: a band between two hexes, split into dashes when asked.
            float outer = size, inner = size * (ringWidth > 0f ? 1f - ringWidth : dashed ? 0.86f : 0.84f);
            int segs = dashed ? 4 : 1;
            for (int i = 0; i < 6; i++)
            {
                for (int s = 0; s < segs; s++)
                {
                    if (dashed && s % 2 == 1) continue;
                    float t0 = s / (float)segs, t1 = (s + 1) / (float)segs;
                    var o0 = Vector3.Lerp(Corner(outer, i), Corner(outer, i + 1), t0);
                    var o1 = Vector3.Lerp(Corner(outer, i), Corner(outer, i + 1), t1);
                    var i0 = Vector3.Lerp(Corner(inner, i), Corner(inner, i + 1), t0);
                    var i1 = Vector3.Lerp(Corner(inner, i), Corner(inner, i + 1), t1);
                    int b = verts.Count;
                    verts.AddRange(new[] { o0, o1, i0, i1 });
                    cols.AddRange(new[] { ring, ring, ring, ring });
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh { name = "Pad" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The colours a pad is drawn in.</summary>
        public static (Color ring, Color fill, bool dashed) PadColors(PadLook look, int level)
        {
            return look switch
            {
                PadLook.Built => (LevelRim(level), new Color(0.04f, 0.02f, 0.1f, 0.55f), false),
                PadLook.Open => (UiTheme.Accent, UiTheme.A(UiTheme.Accent, 0.16f), true),
                PadLook.Planned => (UiTheme.A(UiTheme.Magenta, 0.8f), new Color(0.16f, 0.03f, 0.16f, 0.45f), true),
                PadLook.Reserved => (UiTheme.A(Muted, 0.6f), new Color(0.02f, 0.01f, 0.05f, 0.35f), true),
                _ => (UiTheme.A(Muted, 0.85f), new Color(0.02f, 0.01f, 0.05f, 0.55f), true),
            };
        }

        /// <summary>Level rims: steel, bronze from 5, silver from 10, gold at the top level.</summary>
        public static Color LevelRim(int level) => level >= 30 ? new Color(1f, 0.82f, 0.4f)
            : level >= 10 ? new Color(0.9f, 0.93f, 0.97f)
            : level >= 5 ? new Color(0.79f, 0.51f, 0.31f)
            : new Color(0.54f, 0.59f, 0.72f);

        public static GameObject SpawnPad(Transform planet, BasePad pad, float radius, float size)
        {
            var go = new GameObject($"Pad {pad.Key}");
            go.transform.SetParent(planet, false);
            var normal = Point(pad.Lat, pad.Lon, 1f);
            go.transform.localPosition = normal * radius * 1.004f;
            // Flat on the surface, one corner pointing north.
            var north = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            go.transform.localRotation = Quaternion.LookRotation(north, normal);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = LineMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            // Taps land on the pad (open pads build, locked ones say what opens them).
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size * 1.8f, size * 0.4f, size * 1.8f);
            return go;
        }

        public static void PaintPad(GameObject padObject, float size, PadLook look, int level, bool selected = false)
        {
            var (ring, fill, dashed) = PadColors(look, level);
            if (selected) { ring = UiTheme.Accent; fill = UiTheme.A(UiTheme.Accent, 0.22f); }
            var mf = padObject.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) Object.Destroy(mf.sharedMesh);
            mf.sharedMesh = PadMesh(size, ring, fill, dashed);
        }
    }
}
