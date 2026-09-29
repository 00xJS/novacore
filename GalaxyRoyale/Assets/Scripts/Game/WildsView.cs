// The Wilds on the globe (2026-09-29): a hex tile on every sector of the
// southern hemisphere — dark under the survey fog until it's charted (the ones
// the survey teams can reach edged in magenta), then lit in the colour of its
// find with the find's art standing on it and a chip naming it. A survey beacon
// and a countdown mark the sector being surveyed, harvester drones fly between
// the colony and the deposits they work, and caches and relics say CLAIM.
// Taps open the sector's panel.
//
// Tiles lie on the planet (the planet hides the far side); the art and the
// drones are billboards on BuildingMarkers' overlay layer, culled at the
// horizon like the buildings. Chips show while the globe is tipped south.
using System.Collections.Generic;
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;
using GalaxyRoyale.Game.UI;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Wilds View")]
    [RequireComponent(typeof(GameContext))]
    public sealed class WildsView : MonoBehaviour
    {
        [Tooltip("A sector's hex (fraction of the planet's radius).")]
        [SerializeField] float tileSize = 0.124f;
        [Tooltip("The find's art width (fraction of the planet's radius).")]
        [SerializeField] float artSize = 0.155f;
        [SerializeField] float droneSize = 0.075f;
        [Tooltip("Chips show on sectors facing the camera at least this much.")]
        [SerializeField] float chipFacing = 0.45f;
        [Tooltip("Seconds for a drone's round trip on screen.")]
        [SerializeField] float droneLoopSeconds = 14f;

        const int MarkerLayer = 1; // BuildingMarkers' overlay layer
        const float HorizonCullDot = 0.08f;
        const int MaxDrones = 12;

        sealed class Tile
        {
            public int Index;
            public GameObject Pad = null!;
            public string Look = "";
            public Transform? ArtRoot;
            public GameObject? ArtQuad;
            public Renderer? ArtRenderer;
            public Material? ArtMaterial;
            public string ArtName = "";
        }

        sealed class Drone
        {
            public Transform Root = null!;
            public Renderer Renderer = null!;
            public float Phase;
        }

        readonly List<Tile> _tiles = new();
        readonly Dictionary<GameObject, Tile> _byCollider = new();
        readonly List<Drone> _drones = new();
        readonly Dictionary<string, Texture2D?> _art = new();
        GameContext? _ctx;
        Transform? _planet;
        float _radius = 1f;
        Camera? _cam;
        BaseChipLayer? _chips;
        float _nextRefresh;

        void OnEnable() => CameraController.OnTapHit += HandleTapHit;
        void OnDisable() => CameraController.OnTapHit -= HandleTapHit;

        void Start()
        {
            _ctx = GetComponent<GameContext>();
            var planetGO = GameObject.Find("Home Planet");
            if (_ctx?.State == null || planetGO == null) return;
            _planet = planetGO.transform;
            var mesh = planetGO.transform.Find("Planet Mesh");
            _radius = mesh != null ? mesh.localScale.x * 0.5f : 1f;
            _cam = GameObject.Find("Main Camera")?.GetComponent<Camera>();
            for (int i = 0; i < WildsLayout.Total; i++) SpawnTile(i);
            _ctx.Events?.Subscribe(OnSimEvent);
            Refresh(_ctx.State);
        }

        Texture2D? Art(string name)
        {
            if (!_art.TryGetValue(name, out var tex))
            {
                tex = UnityEngine.Resources.Load<Texture2D>($"Wilds/{name}");
                _art[name] = tex;
            }
            return tex;
        }

        // ---------- tiles ----------

        void SpawnTile(int index)
        {
            var (lat, lon, _) = WildsLayout.Place(index);
            var go = new GameObject($"Wilds {WildsLayout.Name(index)}");
            go.transform.SetParent(_planet, false);
            var normal = BaseVisuals.Point(lat, lon, 1f);
            go.transform.localPosition = normal * _radius * 1.004f;
            var north = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            go.transform.localRotation = Quaternion.LookRotation(north, normal);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = BaseVisuals.SurfaceMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            float size = _radius * tileSize;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size * 1.7f, size * 0.3f, size * 1.7f);
            var tile = new Tile { Index = index, Pad = go };
            _tiles.Add(tile);
            _byCollider[go] = tile;
        }

        static Color ResourceColor(ResourceId r) => r switch
        {
            ResourceId.Gold => UiTheme.Gold,
            ResourceId.Quartz => UiTheme.Quartz,
            _ => UiTheme.Helium,
        };

        static string ResourceArt(ResourceId r) => r switch
        {
            ResourceId.Gold => "GoldSeam",
            ResourceId.Quartz => "CrystalField",
            _ => "HeliumVent",
        };

        void Refresh(GameState state)
        {
            if (_planet == null) return;
            foreach (var tile in _tiles)
            {
                var sector = WildsSystem.Sector(state, tile.Index);
                bool surveying = state.Wilds.Surveying == tile.Index;
                Color ring, fill;
                bool dashed;
                string? art = null;
                bool dim = false;
                string look;
                var dark = new Color(0.02f, 0.01f, 0.05f, 0.62f);
                if (surveying)
                {
                    (ring, fill, dashed, art, look) = (UiTheme.Magenta, UiTheme.A(UiTheme.Magenta, 0.16f), false, "SurveyBeacon", "survey");
                }
                else if (sector == null || sector.Find == WildsFind.None)
                {
                    bool reach = WildsSystem.Reachable(state, tile.Index);
                    (ring, fill, dashed, look) = reach
                        ? (UiTheme.A(UiTheme.Magenta, 0.5f), new Color(0.09f, 0.02f, 0.1f, 0.55f), true, "reach")
                        : (UiTheme.A(BaseVisuals.Muted, 0.45f), dark, true, "fog");
                }
                else if (sector.Find == WildsFind.Deposit)
                {
                    var c = ResourceColor(sector.Resource);
                    art = ResourceArt(sector.Resource);
                    bool dry = sector.StockMilli <= 0;
                    dim = dry;
                    (ring, fill, dashed, look) = dry
                        ? (UiTheme.A(c, 0.4f), dark, true, $"dry{sector.Resource}")
                        : (UiTheme.A(c, 0.9f), UiTheme.A(c, 0.12f), false, $"dep{sector.Resource}");
                }
                else
                {
                    var c = sector.Find == WildsFind.Relic ? UiTheme.DarkMatter : UiTheme.Accent;
                    bool claimed = sector.Claimed;
                    art = claimed ? null : sector.Find == WildsFind.Relic ? "Relic" : "SupplyCache";
                    (ring, fill, dashed, look) = claimed
                        ? (UiTheme.A(c, 0.35f), dark, true, $"claimed{sector.Find}")
                        : (c, UiTheme.A(c, 0.14f), false, $"find{sector.Find}");
                }
                if (look != tile.Look)
                {
                    tile.Look = look;
                    var mf = tile.Pad.GetComponent<MeshFilter>();
                    if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
                    mf.sharedMesh = BaseVisuals.PadMesh(_radius * tileSize, ring, fill, dashed);
                }
                EnsureArt(tile, art, dim);
            }
            SyncDrones(state);
        }

        void EnsureArt(Tile tile, string? name, bool dim)
        {
            if (name == null)
            {
                if (tile.ArtRoot != null) RemoveArt(tile);
                return;
            }
            if (tile.ArtName != name || tile.ArtRoot == null)
            {
                if (tile.ArtRoot != null) RemoveArt(tile);
                var tex = Art(name);
                if (tex == null) return;
                var root = new GameObject($"Wilds Art {WildsLayout.Name(tile.Index)}");
                root.transform.SetParent(_planet, worldPositionStays: false);
                root.transform.localPosition = tile.Pad.transform.localPosition;
                float width = _radius * artSize;
                float height = width * tex.height / (float)tex.width;
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Art";
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(root.transform, worldPositionStays: false);
                // The art's base plate sits a fifth of the way up from its bottom edge.
                quad.transform.localPosition = new Vector3(0f, height * 0.3f, 0f);
                quad.transform.localScale = new Vector3(width, height, 1f);
                var box = quad.AddComponent<BoxCollider>();
                box.size = new Vector3(0.8f, 0.8f, 0.05f);
                var mat = MapVisuals.UnlitTransparent(tex);
                var renderer = quad.GetComponent<Renderer>();
                renderer.sharedMaterial = mat;
                SetLayer(root);
                tile.ArtRoot = root.transform;
                tile.ArtQuad = quad;
                tile.ArtRenderer = renderer;
                tile.ArtMaterial = mat;
                tile.ArtName = name;
                _byCollider[quad] = tile;
            }
            if (tile.ArtMaterial != null) tile.ArtMaterial.color = dim ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
        }

        void RemoveArt(Tile tile)
        {
            if (tile.ArtQuad != null) _byCollider.Remove(tile.ArtQuad);
            if (tile.ArtRoot != null) Destroy(tile.ArtRoot.gameObject);
            tile.ArtRoot = null;
            tile.ArtQuad = null;
            tile.ArtRenderer = null;
            tile.ArtMaterial = null;
            tile.ArtName = "";
        }

        static void SetLayer(GameObject go)
        {
            go.layer = MarkerLayer;
            foreach (Transform child in go.transform) SetLayer(child.gameObject);
        }

        // ---------- drones ----------

        void SyncDrones(GameState state)
        {
            int want = WildsSystem.ActiveDeposits(state) > 0 ? Mathf.Min(MaxDrones, WildsSystem.Drones(state)) : 0;
            var tex = Art("Drone");
            if (tex == null) return;
            while (_drones.Count < want)
            {
                var root = new GameObject($"Wilds Drone {_drones.Count + 1}");
                root.transform.SetParent(_planet, worldPositionStays: false);
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.transform.SetParent(root.transform, worldPositionStays: false);
                float s = _radius * droneSize;
                quad.transform.localScale = new Vector3(s, s, 1f);
                var renderer = quad.GetComponent<Renderer>();
                var mat = MapVisuals.UnlitTransparent(tex);
                mat.color = Color.white;
                renderer.sharedMaterial = mat;
                SetLayer(root);
                _drones.Add(new Drone { Root = root.transform, Renderer = renderer, Phase = _drones.Count * 0.37f % 1f });
            }
            while (_drones.Count > want)
            {
                var last = _drones[^1];
                Destroy(last.Root.gameObject);
                _drones.RemoveAt(_drones.Count - 1);
            }
        }

        /// <summary>Where the drones take off: the Drone Factory once it's built, the Command Center before.</summary>
        static BasePad Hangar(GameState state) => state.Buildings[BuildingId.DroneFactory].Level > 0
            ? BaseLayout.BuildingPad(BuildingId.DroneFactory)
            : BaseLayout.BuildingPad(BuildingId.CommandCenter);

        void FlyDrones(GameState state, Vector3 centre, Vector3 toCamera)
        {
            if (_drones.Count == 0 || _cam == null) return;
            var targets = new List<int>();
            foreach (var s in state.Wilds.Sectors.Values)
                if (s.Find == WildsFind.Deposit && s.StockMilli > 0) targets.Add(s.Index);
            if (targets.Count == 0) return;
            targets.Sort();
            var hangar = Hangar(state);
            var from = BaseVisuals.Point(hangar.Lat, hangar.Lon, 1f);
            for (int i = 0; i < _drones.Count; i++)
            {
                var d = _drones[i];
                var (lat, lon, _) = WildsLayout.Place(targets[i % targets.Count]);
                var to = BaseVisuals.Point(lat, lon, 1f);
                float t = Mathf.Repeat(Time.time / droneLoopSeconds + d.Phase, 1f);
                float u = Mathf.SmoothStep(0f, 1f, t < 0.5f ? t * 2f : (1f - t) * 2f);
                var dir = Vector3.Slerp(from, to, u).normalized;
                float lift = 1.01f + 0.16f * Mathf.Sin(u * Mathf.PI);
                d.Root.localPosition = dir * _radius * lift;
                var world = d.Root.position;
                d.Root.rotation = Quaternion.LookRotation(world - _cam.transform.position, _cam.transform.up);
                d.Renderer.enabled = Vector3.Dot((world - centre).normalized, toCamera) > HorizonCullDot;
            }
        }

        // ---------- per frame ----------

        void LateUpdate()
        {
            var state = _ctx?.State;
            if (state == null || _planet == null || _cam == null) return;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.5f;
                Refresh(state);
            }
            Vector3 centre = _planet.position;
            Vector3 toCamera = (_cam.transform.position - centre).normalized;
            foreach (var tile in _tiles)
            {
                if (tile.ArtRoot == null) continue;
                tile.ArtRoot.rotation = Quaternion.LookRotation(tile.ArtRoot.position - _cam.transform.position, _cam.transform.up);
                bool visible = Vector3.Dot((tile.ArtRoot.position - centre).normalized, toCamera) > HorizonCullDot;
                if (tile.ArtRenderer != null) tile.ArtRenderer.enabled = visible;
            }
            FlyDrones(state, centre, toCamera);
            PlaceChips(state, centre, toCamera);
        }

        void PlaceChips(GameState state, Vector3 centre, Vector3 toCamera)
        {
            _chips ??= UIController.Instance?.CreateBaseChipLayer();
            if (_chips == null) return;
            var globe = BaseGlobe.Instance;
            if (globe == null || !globe.South || globe.Zoom > 0.45f || !_cam!.isActiveAndEnabled
                || UIController.Instance?.HasModal == true)
            {
                _chips.Clear();
                return;
            }
            _chips.Begin();
            foreach (var tile in _tiles)
            {
                var world = tile.Pad.transform.position;
                float facing = Vector3.Dot((world - centre).normalized, toCamera);
                if (facing < chipFacing) continue;
                if (_chips.ToPanel(_cam.WorldToScreenPoint(world)) is not { } p) continue;
                float fade = Mathf.Clamp01((facing - chipFacing) / 0.15f);
                string key = $"wilds-{tile.Index}";
                var below = new Vector2(p.x, p.y + 9f);
                Vector2? top = null;
                if (tile.ArtQuad != null)
                {
                    var t = tile.ArtQuad.transform;
                    top = _chips.ToPanel(_cam.WorldToScreenPoint(t.position + _cam.transform.up * (t.lossyScale.y * 0.5f)));
                }
                var sector = WildsSystem.Sector(state, tile.Index);
                if (state.Wilds.Surveying == tile.Index)
                {
                    int left = WildsSystem.SurveyLeft(state);
                    int total = Mathf.Max(1, WildsSystem.SurveySeconds(tile.Index));
                    var ringAt = top is { } tp ? new Vector2(tp.x, tp.y + 18f) : new Vector2(p.x, p.y - 30f);
                    _chips.Place(key, BaseChipLayer.Kind.Timer, below, ringAt, UiTheme.FmtDuration(left),
                        progress: Mathf.Clamp01(1f - left / (float)total), alpha: fade);
                    continue;
                }
                if (sector == null || sector.Find == WildsFind.None)
                {
                    _chips.Place(key, BaseChipLayer.Kind.Fog, p, p, "", alpha: fade * (WildsSystem.Reachable(state, tile.Index) ? 1f : 0.6f));
                    continue;
                }
                var (name, _, color) = SectorPanel.Look(sector);
                if (sector.Find == WildsFind.Deposit)
                {
                    string sub = sector.StockMilli > 0 ? UiTheme.FmtAmount(sector.StockMilli) : "DRY";
                    _chips.Place(key, BaseChipLayer.Kind.Name, below, null, name, sub,
                        sector.StockMilli > 0 ? color : BaseVisuals.Muted, alpha: fade);
                    continue;
                }
                if (sector.Claimed)
                {
                    _chips.Place(key, BaseChipLayer.Kind.Reserved, below, null, "CLAIMED", alpha: fade * 0.8f);
                    continue;
                }
                // A tap on the find opens its panel, with CLAIM (a bubble over the art
                // sat on the sector above: the hexes are close).
                _chips.Place(key, BaseChipLayer.Kind.Name, below, null, name, "CLAIM", color, alpha: fade);
            }
            _chips.End();
        }

        // ---------- taps and events ----------

        void HandleTapHit(RaycastHit hit)
        {
            if (_ctx == null || !_byCollider.TryGetValue(hit.collider.gameObject, out var tile)) return;
            var globe = BaseGlobe.Instance;
            var (_, lon, _) = WildsLayout.Place(tile.Index);
            if (globe != null && (!globe.South || globe.Zoom > 0.45f))
            {
                globe.FlyToSouth(lon); // from the band or orbit, a tap brings the Wilds round first
                return;
            }
            GameAudio.Play(Sfx.Toggle);
            SectorPanel.Open(_ctx, tile.Index);
        }

        void OnSimEvent(SimEvent e)
        {
            if (e is not WildsSurveyed done || _ctx?.State == null) return;
            var sector = WildsSystem.Sector(_ctx.State, done.Sector);
            if (sector == null) return;
            var (name, icon, color) = SectorPanel.Look(sector);
            string what = sector.Find switch
            {
                WildsFind.Deposit => $"{name}, {UiTheme.FmtAmount(sector.MaxMilli)}",
                WildsFind.Relic => $"a relic: +{sector.RewardDM} Dark Matter to claim",
                WildsFind.Cache => $"a supply cache: +{UiTheme.FmtAmount(sector.Reward.Total)} to claim",
                _ => name,
            };
            GameAudio.Feedback(Sfx.Confirm, Haptic.Success);
            UIController.Instance?.Toast($"Survey of {WildsLayout.Name(done.Sector)} done: {what}", icon, color);
            Refresh(_ctx.State);
        }
    }
}
