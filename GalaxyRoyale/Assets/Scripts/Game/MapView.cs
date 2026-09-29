// The galaxy map view — Unity port of v1's MapScene (Phaser). Toggles against
// the Base (planet) view: entering hides the planet rig and enables a second,
// orthographic camera over a lazily-built 2D map world.
//
// World mapping: 1 tile = 1 world unit, tile (x,y) centers at (x+0.5, -(y+0.5)).
// The Y flip keeps v1's screen orientation (y grows downward on the map).
//
// Ported from v1: parallax starfield, seeded nebula blobs + diagonal lane,
// tier rings, orbital rings + decorative orbiting worlds, layered pulsing
// supernova, sector labels, camera-culled node markers with zoom LOD, home
// planet, node selection + callout, live march lines/dots.
// Deferred (needs B.4/B.5): relocation targeting, probe redirect, remote players.
using System.Collections.Generic;
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Map View")]
    [RequireComponent(typeof(GameContext))]
    public sealed class MapView : MonoBehaviour
    {
        [Header("Zoom LOD (orthographic size, in tiles)")]
        [Tooltip("Above this, node markers hide entirely. Kept ABOVE the max zoom so nodes never vanish in-range — they just shrink to dots.")]
        [SerializeField] float hideNodesAbove = 380f;
        [Tooltip("Legacy LOD-dot threshold (nodes are a fixed native size now, so this is effectively unused).")]
        [SerializeField] float dotLodAbove = 220f;
        [SerializeField] float initialOrthoSize = 45f;
        [Tooltip("Extra heading rotation for the spy-probe sprite so its point faces forward (the probe's spear runs on the top-right/bottom-left diagonal).")]
        [SerializeField] float probeRollOffset = -135f;

        // Marker sizing lives in ONE place so the tap-picking radius always
        // matches what's drawn. Resource planets are a FIXED native world size
        // (user feedback: don't grow them as you zoom out — they should shrink to
        // little dots on the overview). The player's own planet keeps its
        // zoom-scaled prominence.
        static float NodeMarkerScale(float size, bool lodDot) => 5f;
        static float PlayerMarkerScale(float size) =>
            Mathf.Max(7f, 7f * Mathf.Sqrt(size / 18f));

        static readonly Color[] TierTint =
        {
            new(1f, 1f, 1f), new(0.87f, 0.91f, 0.69f), new(0.95f, 0.83f, 0.53f),
            new(0.95f, 0.66f, 0.40f), new(0.94f, 0.49f, 0.35f),
        };
        static readonly Color[] NebulaTints =
        {
            new(0.37f, 0.29f, 0.56f), new(0.18f, 0.44f, 0.56f),
            new(0.56f, 0.18f, 0.35f), new(0.18f, 0.56f, 0.44f),
        };

        static Color KindColor(NodeKind kind) => kind switch
        {
            NodeKind.Asteroid => new Color(0.82f, 0.55f, 0.35f),
            NodeKind.Nebula   => new Color(0.50f, 0.83f, 1.00f),
            NodeKind.HeliumCloud => new Color(0.62f, 0.91f, 0.48f),
            NodeKind.Derelict => new Color(0.81f, 0.85f, 1.00f),
            NodeKind.Camp     => new Color(0.94f, 0.35f, 0.35f),
            NodeKind.DMField  => new Color(0.79f, 0.63f, 0.91f), // Dark Matter violet
            _ => Color.white,
        };

        GameContext _ctx = null!;
        bool _active;
        GameObject? _root;
        Camera? _mapCam;
        MapCameraController? _camCtl;

        // Base-view objects we disable while the map is up.
        GameObject? _baseCamera;
        GameObject? _basePlanet;
        CameraController? _baseCamCtl;

        // Backdrop + animated pieces.
        Transform? _starFar, _starNear;
        Material? _starFarMat, _starNearMat;
        Transform? _novaShock, _novaCore;
        float _novaShockScale, _novaCoreScale;
        readonly List<(Transform t, float r, float speed, float phase, SpriteRenderer sr)> _orbiters = new();
        Vector3 _coreWorld;

        // Home + selection.
        Transform? _home;
        TileXY _homeCache;

        // Rival commanders (the 99 simulated empires): violet planets + name
        // labels, built straight from ctx.Bots — no polling, no wire.
        sealed class RemoteVisual
        {
            public Transform Root = null!;
            public string Name = "";
            public TileXY Tile;
            /// <summary>Starts EMPTY (not "default") so the first sync always applies
            /// the real skin — the old sentinel skipped ApplySkin for default-skin
            /// bots entirely, leaving them the untinted purple disc (user report).</summary>
            public string Skin = "";
            public SpriteRenderer Disc = null!;
            public SpriteRenderer Glow = null!;
            /// <summary>Live fire shown while this planet's BurningUntilTick is live.</summary>
            public BurnFx Burn = null!;
            public bool Burning;
        }

        int? _followMarchId; // camera chases this fleet while its callout is open
        // …or another commander's flight (user request: rival fleets flying
        // around the map were untappable). Key into _rivalFlights, or into
        // _contactVisuals when _followContact (a radar-tracked inbound hostile).
        long? _followRivalKey;
        bool _followContact;
        SpriteRenderer? _homeDisc, _homeGlow, _homeShield;
        BurnFx? _homeBurn;
        string _homeSkinCache = "";
        TileXY _coordsCacheTile = new(-1, -1);
        bool _novaStatic; // user art supplies the core — no pulsing

        // Runs AFTER MapCameraController so follow-cam wins the frame; labels
        // are placed last, against the camera pose this frame actually renders.
        void LateUpdate()
        {
            if (!_active || _mapCam == null) { _labels?.Clear(); return; }
            var state = _ctx.State;
            if (state == null) return;
            FollowTick(state);
            PlaceLabels(state);
        }

        void FollowTick(GameState state)
        {
            if (_followMarchId is int followId)
            {
                March? followed = null;
                foreach (var m in state.Marches)
                    if (m.Id == followId) { followed = m; break; }
                if (followed == null) { StopFollowing(); return; }
                var p = MarchSystem.GetPositionSmooth(followed, _ctx.PreciseTick);
                var w = TileToWorld(p.X, p.Y);
                _camCtl!.Frame(new Vector2(w.x, w.y));
                return;
            }

            if (_followRivalKey is long key)
            {
                Transform? dot = null;
                if (_followContact)
                {
                    if (_contactVisuals.TryGetValue(key, out var contact)) dot = contact.Dot;
                }
                else if (_rivalFlights.TryGetValue(key, out var flight)) dot = flight.Dot;
                // Landed, returned home, or dropped off radar — the chase is over.
                if (dot == null) { StopFollowing(); return; }
                var d = dot.position;
                _camCtl!.Frame(new Vector2(d.x, d.y));
            }
        }

        /// <summary>End any follow-cam and close its callout.</summary>
        void StopFollowing()
        {
            bool wasFollowing = _followMarchId != null || _followRivalKey != null;
            _followMarchId = null;
            _followRivalKey = null;
            if (wasFollowing) UI.UIController.Instance?.CloseNodeCallout();
        }
        readonly Dictionary<int, RemoteVisual> _remotes = new();
        const float RemotePollSeconds = 5f; // cheap local refresh (skins/new galaxy)
        float _nextRemotePoll;
        GameObject? _selectRing;
        MapNode? _selected;

        // Node culling.
        readonly Dictionary<string, Transform> _nodeSprites = new();
        Vector2 _lastSyncPos = new(-1e9f, -1e9f);
        float _lastSyncSize = -1f;
        bool _needsSync;

        // March visuals.
        sealed class MarchVisual
        {
            public GameObject Root = null!;
            public LineRenderer Line = null!;
            public Transform Dot = null!;
            public SpriteRenderer DotSr = null!;
            public bool HasArt;
            // v1 march-trail: ring buffer of fading engine-glow dots.
            public readonly SpriteRenderer[] Trail = new SpriteRenderer[8];
            public readonly float[] TrailAge = new float[8];
            public int TrailHead;
            public float NextTrailDrop;
        }
        const float TrailDropInterval = 0.08f;
        const float TrailLifetime = 0.6f;
        readonly Dictionary<int, MarchVisual> _marchVisuals = new();
        Material? _lineMat;

        // Commander / HQ names (UI Toolkit layer under the HUD — see WorldLabelLayer).
        UI.WorldLabelLayer? _labels;
        /// <summary>Rival ids, strongest first — the biggest names win label space.
        /// Re-sorted on the 5 s rival sync, not per frame.</summary>
        readonly List<int> _labelOrder = new();

        // Targeting modes (Precision Warp relocation / spy-probe redirect).
        bool _relocating;
        int? _redirectMarchId;

        // Reused per-frame scratch collections — the march/flight/contact visual
        // updaters run every frame, and allocating fresh sets/lists 60×/s was
        // steady GC pressure on device. Cleared and refilled in place instead.
        readonly HashSet<int> _marchLive = new();
        readonly List<int> _marchGone = new();
        readonly HashSet<long> _flightLive = new();
        readonly List<long> _flightGone = new();
        readonly HashSet<long> _contactLive = new();
        readonly List<long> _contactGone = new();

        void Awake() => _ctx = GetComponent<GameContext>();

        public bool IsActive => _active;

        public void ToggleMap()
        {
            if (_active) ExitMap();
            else EnterMap();
        }

        /// <summary>Center the map camera on a tile (mailbox "view on map"). Enters the map if needed.</summary>
        public void FocusTile(TileXY tile)
        {
            if (!_active) EnterMap();
            if (_mapCam == null || _camCtl == null) return;
            var w = TileToWorld(tile.X, tile.Y);
            _camCtl.Frame(new Vector2(w.x, w.y));
            var node = LiveNodeAt(_ctx.State!, tile);
            if (node != null) SelectNode(node);
        }

        /// <summary>Center the map on a tile at a zoom (half-height in tiles), selecting
        /// nothing — for launch hooks and screenshots.</summary>
        public void Frame(TileXY tile, float viewSize)
        {
            if (!_active) EnterMap();
            if (_mapCam == null || _camCtl == null) return;
            var w = TileToWorld(tile.X, tile.Y);
            _camCtl.ViewSize = viewSize;
            _camCtl.Frame(new Vector2(w.x, w.y));
        }

        static Vector3 TileToWorld(double x, double y, float z = 0f) =>
            new((float)(x + 0.5), -(float)(y + 0.5), z);

        static TileXY WorldToTile(Vector3 w) =>
            new(Mathf.FloorToInt(w.x), Mathf.FloorToInt(-w.y));

        // ---------- view switching ----------

        public void EnterMap()
        {
            var state = _ctx.State;
            if (state == null) return;

            _baseCamera  = GameObject.Find("Main Camera");
            _basePlanet  = GameObject.Find("Home Planet");
            _baseCamCtl  = GetComponent<CameraController>();

            if (_baseCamera != null) _baseCamera.GetComponent<Camera>().enabled = false;
            if (_basePlanet != null) _basePlanet.SetActive(false);
            if (_baseCamCtl != null) _baseCamCtl.enabled = false;

            EnsureBuilt(state);
            _root!.SetActive(true);

            var camGO = _mapCam!.gameObject;
            camGO.SetActive(true);
            _mapCam.enabled = true;
            var homeW = TileToWorld(state.HomeTile.X, state.HomeTile.Y);
            _camCtl!.ViewSize = initialOrthoSize;
            _camCtl.Frame(new Vector2(homeW.x, homeW.y)); // perspective pose framing home

            _active = true;
            _needsSync = true;
            _nextRemotePoll = 0f; // fresh player-position fetch on every map entry
        }

        public void ExitMap()
        {
            _active = false;
            _followMarchId = null;
            _followRivalKey = null;
            _labels?.Clear();
            UI.UIController.Instance?.SetHqOverride(null); // header HQ line back to normal
            _coordsCacheTile = new TileXY(-1, -1);
            ClearSelection();
            if (_root != null) _root.SetActive(false);
            if (_mapCam != null) _mapCam.gameObject.SetActive(false);
            MapCameraController.BlockRects.Clear();

            if (_baseCamera != null) _baseCamera.GetComponent<Camera>().enabled = true;
            if (_basePlanet != null) _basePlanet.SetActive(true);
            if (_baseCamCtl != null) _baseCamCtl.enabled = true;

            UI.UIController.Instance?.OnMapExited();
        }

        // ---------- world construction (lazy, once) ----------

        void EnsureBuilt(GameState state)
        {
            if (_root != null && _mapCam != null) return;

            var sector = MapLookup.GetSector(state);
            _root = new GameObject("Galaxy Map");
            _root.SetActive(false);

            // Map camera — separate rig so the base camera's transform is untouched.
            var camGO = new GameObject("Map Camera");
            _mapCam = camGO.AddComponent<Camera>();
            _mapCam.orthographic = false; // perspective tilt rig (MapCameraController)
            _mapCam.clearFlags = CameraClearFlags.SolidColor;
            _mapCam.backgroundColor = new Color(0.012f, 0.016f, 0.05f);
            // Clip planes are managed per-pose by MapCameraController.ApplyPose.
            camGO.SetActive(false);

            _camCtl = camGO.AddComponent<MapCameraController>();
            _camCtl.Cam = _mapCam;
            const float pad = 40f;
            _camCtl.BoundsMin = new Vector2(-pad, -(sector.Size + pad));
            _camCtl.BoundsMax = new Vector2(sector.Size + pad, pad);
            _camCtl.OnTap = OnMapTap;

            _coreWorld = TileToWorld(sector.Core.X, sector.Core.Y);

            BuildStarfield();
            BuildNebulae(state, sector.Size);
            BuildRingsAndSupernova();
            BuildHome(state);

            // Select reticle (hidden until a node is tapped).
            _selectRing = MapVisuals.Spawn(_root.transform, "Select Ring", MapVisuals.Ring,
                Vector3.zero, 4f, 4f, new Color(0.5f, 0.83f, 1f, 0.9f), 30).gameObject;
            _selectRing.AddComponent<MapBillboard>(); // stays a circle at the tilt
            _selectRing.SetActive(false);

            _lineMat = MapVisuals.SpriteDefault()!;

            // Map-affecting sim events → re-cull on the next update.
            _ctx.Events!.Subscribe(e =>
            {
                if (e is BattleResolved || e is MarchReturned || e is NodeDepleted || e is NodeRespawned)
                    _needsSync = true;
            });
        }

        void BuildStarfield()
        {
            // Star layers are CAMERA CHILDREN now (perspective tilt): a fixed-depth
            // backdrop that always faces the camera and fills the view at any pitch.
            // Parallax is UV offset driven by camera world position (see UpdateLayer).
            (Transform, Material) MakeLayer(string name, float localZ, float alpha)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = name;
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(_mapCam!.transform, worldPositionStays: false);
                go.transform.localPosition = new Vector3(0f, 0f, localZ);
                go.transform.localRotation = Quaternion.identity;
                // Overscan generously so the backdrop covers the frustum at any zoom.
                go.transform.localScale = new Vector3(localZ * 3f, localZ * 3f, 1f);
                var mat = MapVisuals.UnlitTransparent(MapVisuals.Stars);
                mat.color = new Color(1f, 1f, 1f, alpha);
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                return (go.transform, mat);
            }
            (_starFar, _starFarMat)   = MakeLayer("Starfield Far", 620f, 0.7f);
            (_starNear, _starNearMat) = MakeLayer("Starfield Near", 480f, 0.9f);
        }

        void BuildNebulae(GameState state, int worldSize)
        {
            uint seed = (uint)state.Seed;
            // Ambient blobs, deterministic per seed (v1: 10 blobs, hash2d-driven).
            for (int i = 0; i < 10; i++)
            {
                float nx = (float)(Rng.Hash2d(seed, i, 101) * worldSize);
                float ny = (float)(Rng.Hash2d(seed, i, 202) * worldSize);
                float alpha = 0.5f + (float)Rng.Hash2d(seed, i, 303) * 0.3f;
                float scale = (2f + (float)Rng.Hash2d(seed, i, 404) * 3f) * 26f;
                float angle = (float)Rng.Hash2d(seed, i, 505) * 180f;
                var sr = MapVisuals.Spawn(_root!.transform, $"Nebula {i}", MapVisuals.Glow,
                    new Vector3(nx, -ny, 10f), scale, scale,
                    WithAlpha(NebulaTints[i % NebulaTints.Length], alpha * 0.5f), 2);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, -angle);
            }
            // Signature diagonal nebula lane (v1: 14 stretched blobs corner to corner).
            for (int i = 0; i < 14; i++)
            {
                float t = i / 13f;
                float lx = worldSize * (0.14f + t * 0.72f);
                float ly = worldSize * (0.86f - t * 0.72f);
                var tint = i % 2 == 1 ? new Color(0.18f, 0.44f, 0.62f) : new Color(0.35f, 0.25f, 0.56f);
                var sr = MapVisuals.Spawn(_root!.transform, $"Nebula Lane {i}", MapVisuals.Glow,
                    new Vector3(lx, -ly, 10f), 80f * 26f * 0.05f, 34f * 26f * 0.05f,
                    WithAlpha(tint, 0.2f), 2);
                sr.transform.localScale = new Vector3(80f, 34f, 1f) * 2.6f;
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, -37f);
            }
        }

        void BuildRingsAndSupernova()
        {
            // Tier rings — "risk rises inward" (radii match tierOf breaks).
            foreach (float r in new[] { 300f, 600f, 900f, 1200f })
                MapVisuals.Spawn(_root!.transform, $"Tier Ring {r}", MapVisuals.Ring,
                    _coreWorld + new Vector3(0f, 0f, 8f), r * 2f, r * 2f,
                    new Color(0.94f, 0.62f, 0.35f, 0.07f), 3); // fainter (still marks risk tiers)

            // Orbital rings — calmed down to two faint ellipses (user feedback:
            // the five-ring + four-tier-ring stack was visual noise).
            foreach (float r in new[] { 700f, 1300f })
                MapVisuals.Spawn(_root!.transform, $"Orbit Ring {r}", MapVisuals.Ring,
                    _coreWorld + new Vector3(0f, 0f, 8f), r * 2f, r * 2f * 0.62f,
                    new Color(0.5f, 0.83f, 1f, 0.05f), 4);

            // Decorative worlds riding the orbit rings (trimmed to four).
            var orbitDefs = new (float r, float speed, float phase, Color tint)[]
            {
                (700f, -0.041f, 2.1f, new Color(0.7f, 0.85f, 0.7f)),
                (700f, -0.041f, 5.3f, new Color(0.9f, 0.6f, 0.55f)),
                (1300f, 0.022f, 4.0f, new Color(0.85f, 0.8f, 0.6f)),
                (1300f, 0.022f, 0.9f, new Color(0.6f, 0.85f, 0.85f)),
            };
            foreach (var d in orbitDefs)
            {
                var sr = MapVisuals.Spawn(_root!.transform, "Orbit World", MapVisuals.Disc,
                    _coreWorld + new Vector3(0f, 0f, 6f), 14f, 14f, d.tint, 6);
                sr.gameObject.AddComponent<MapBillboard>(); // orbiting worlds stand up
                _orbiters.Add((sr.transform, d.r, d.speed, d.phase, sr));
            }

            // THE SUPERNOVA — layered halo, pulsing shockwave ring, pulsing core.
            // With user art (Resources/Map/supernova-core.png — the blue-violet
            // Kingdom stronghold w/ ring station) the warm procedural palette
            // shifts blue-violet to match and the art replaces the core glow.
            var novaArt = MapVisuals.OverrideSprite("supernova-core");
            var haloColor = novaArt != null
                ? new Color(0.45f, 0.4f, 1f, 0.35f) : new Color(1f, 0.75f, 0.45f, 0.35f);
            var shockColor = novaArt != null
                ? new Color(0.6f, 0.55f, 1f, 0.5f) : new Color(1f, 0.85f, 0.6f, 0.5f);
            MapVisuals.Spawn(_root!.transform, "Nova Halo", MapVisuals.Glow,
                _coreWorld + new Vector3(0f, 0f, 7f), 1800f, 1800f, haloColor, 4);
            var shock = MapVisuals.Spawn(_root!.transform, "Nova Shock", MapVisuals.Ring,
                _coreWorld + new Vector3(0f, 0f, 7f), 1200f, 1200f, shockColor, 4);
            _novaShock = shock.transform;
            _novaShockScale = 1200f;
            var core = MapVisuals.Spawn(_root!.transform, "Nova Core", MapVisuals.Glow,
                _coreWorld + new Vector3(0f, 0f, 6f), 520f, 520f,
                new Color(1f, 0.95f, 0.85f, 0.95f), 5);
            if (novaArt != null)
            {
                core.sprite = novaArt;
                core.color = Color.white;
                _novaCoreScale = 640f; // art carries its own glow — a bit larger reads regal
                _novaStatic = true;    // user call: no pulsing on the art core
                core.transform.localScale = new Vector3(_novaCoreScale, _novaCoreScale, 1f);
            }
            else
            {
                _novaCoreScale = 520f;
            }
            core.gameObject.AddComponent<MapBillboard>(); // the core stands up facing the viewer
            _novaCore = core.transform;
        }

        void BuildHome(GameState state)
        {
            var root = new GameObject("Map Home Planet");
            root.transform.SetParent(_root!.transform, worldPositionStays: false);
            root.transform.localPosition = TileToWorld(state.HomeTile.X, state.HomeTile.Y);
            _homeGlow = MapVisuals.Spawn(root.transform, "Glow", MapVisuals.Glow, Vector3.zero, 2.6f, 2.6f,
                new Color(0.4f, 0.7f, 1f, 0.6f), 10);
            _homeDisc = MapVisuals.Spawn(root.transform, "Disc", MapVisuals.Disc, Vector3.zero, 1f, 1f,
                new Color(0.45f, 0.75f, 1f), 11);
            _homeBurn = SpawnBurnFlame(root.transform);
            _homeShield = SpawnShieldBubble(root.transform);
            _home = root.transform;
            root.AddComponent<MapBillboard>(); // stand the planet up facing the viewer
            _homeCache = state.HomeTile;
            ApplySkin(_homeDisc, _homeGlow, state.Skins.ActivePlanet);
            _homeSkinCache = state.Skins.ActivePlanet;
        }

        // ---------- battle scars + the Aegis bubble ----------

        /// <summary>Two-layer LIVE fire on a planet that recently lost a defense
        /// (4 h burn): a flickering under-glow behind the disc plus rising ember
        /// particles licking over the limb. Fully procedural (user spec: a static
        /// PNG would look fake) — see MapVisuals.FireParticles.</summary>
        sealed class BurnFx
        {
            public GameObject Root = null!;
            public SpriteRenderer Glow = null!;
        }

        static BurnFx SpawnBurnFlame(Transform parent)
        {
            var root = new GameObject("Burn");
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.localPosition = Vector3.zero;
            var glow = MapVisuals.Spawn(root.transform, "Fire Glow", MapVisuals.Glow, Vector3.zero,
                2.2f, 2.2f, new Color(1f, 0.45f, 0.08f, 0.70f), 9); // behind the disc
            MapVisuals.FireParticles(root.transform, 12);           // flames + sparks OVER the limb
            root.SetActive(false); // activating replays the particles (playOnAwake)
            return new BurnFx { Root = root, Glow = glow };
        }

        /// <summary>Solid blue energy dome while the Aegis Shield is up — procedural
        /// like the flames (MapVisuals.Bubble: dome fill + bright rim), sized just
        /// past the planet's limb. Optional user art: Resources/Map/shield-bubble.png.</summary>
        static SpriteRenderer SpawnShieldBubble(Transform parent)
        {
            var sr = MapVisuals.Spawn(parent, "Shield Bubble", MapVisuals.Bubble, Vector3.zero,
                1.25f, 1.25f, new Color(0.30f, 0.62f, 1f, 0.55f), 12); // slightly bigger than the disc
            var art = MapVisuals.OverrideSprite("shield-bubble");
            if (art != null) sr.sprite = art;
            sr.gameObject.SetActive(false);
            return sr;
        }

        /// <summary>Flame flicker + shield pulse, shared by home and rival planets.</summary>
        void UpdateAftermathFx(BurnFx? burn, SpriteRenderer? bubble,
            bool burning, bool shielded, float seed)
        {
            if (burn != null)
            {
                if (burn.Root.activeSelf != burning) burn.Root.SetActive(burning);
                if (burning)
                {
                    // Two offset sine waves read as firelight, not a metronome —
                    // the ember particles on top carry the actual flame motion.
                    float t = Time.time * 7f + seed;
                    float flicker = 0.45f + 0.20f * Mathf.Sin(t) + 0.10f * Mathf.Sin(t * 2.7f);
                    burn.Glow.color = new Color(1f, 0.45f, 0.08f, Mathf.Clamp01(flicker));
                }
            }
            if (bubble != null)
            {
                if (bubble.gameObject.activeSelf != shielded) bubble.gameObject.SetActive(shielded);
                if (shielded)
                {
                    // Solid blue dome with a slow breathing pulse (the bubble sprite
                    // bakes the bright rim, so the tint stays a clean force-field blue).
                    float pulse = 0.52f + 0.14f * Mathf.PingPong(Time.time * 0.8f + seed, 1f);
                    bubble.color = new Color(0.30f, 0.62f, 1f, pulse);
                }
            }
        }

        // ---------- planet skins on the map (yours AND other players') ----------

        static string SkinKey(string skinId) => skinId switch
        {
            // Bare names are the legacy bot-personality strings (pre-2026-07-07
            // saves) — they must keep mapping to the same art as the shop ids.
            "skin-crimson" or "crimson" => "crimson",
            "skin-emerald" or "emerald" => "emerald",
            _ => "default",
        };

        static Color SkinTint(string skinId) => SkinKey(skinId) switch
        {
            "crimson" => new Color(1.00f, 0.42f, 0.45f),
            "emerald" => new Color(0.45f, 1.00f, 0.55f),
            _ => new Color(0.45f, 0.75f, 1.00f),
        };

        static void ApplySkin(SpriteRenderer disc, SpriteRenderer glow, string skinId)
        {
            var tint = SkinTint(skinId);
            // Optional user art: Resources/Map/planet-<skin>.png replaces the disc.
            var art = MapVisuals.OverrideSprite($"planet-{SkinKey(skinId)}");
            if (art != null) { disc.sprite = art; disc.color = Color.white; }
            else disc.color = tint;
            glow.color = WithAlpha(tint, 0.55f);
        }

        static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

        // ---------- node culling (v1 syncNodes) ----------

        void SyncNodes(GameState state)
        {
            var sector = MapLookup.GetSector(state);
            var cam = _mapCam!;
            float size = cam.orthographicSize;
            // Overscan the cull box (perspective + tilt shows more ground than the
            // old orthographic half-height) and center on the framed TARGET, not
            // the tilted camera position. Kept modest to bound node count at max
            // zoom-out (nodes stay visible as dots; see hideNodesAbove).
            float halfH = size * 1.8f + 30f;
            float halfW = size * cam.aspect * 1.4f + 30f;
            Vector3 c = _camCtl != null
                ? new Vector3(_camCtl.Target.x, _camCtl.Target.y, 0f)
                : cam.transform.position;

            bool hideAll = size > hideNodesAbove;
            bool lodDot = !hideAll && size > dotLodAbove;

            bool InView(TileXY tile)
            {
                var w = TileToWorld(tile.X, tile.Y);
                return w.x >= c.x - halfW && w.x <= c.x + halfW
                    && w.y >= c.y - halfH && w.y <= c.y + halfH;
            }

            var wanted = new HashSet<string>();
            if (!hideAll)
            {
                foreach (var node in sector.Nodes.Values)
                {
                    if (!InView(node.Tile)) continue;
                    state.Map.NodeOverrides.TryGetValue(node.Id, out var ov);
                    if (ov != null && (ov.Retired || ov.Cleared)) continue;
                    if (node.Kind != NodeKind.Camp && (ov?.Remaining ?? node.Amount) <= 0) continue;
                    wanted.Add(node.Id);
                }
                foreach (var node in state.Map.DynamicNodes)
                    if (InView(node.Tile)) wanted.Add(node.Id);
            }

            // Drop sprites that fell out of view / got hidden.
            var stale = new List<string>();
            foreach (var kv in _nodeSprites)
                if (!wanted.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                Destroy(_nodeSprites[id].gameObject);
                _nodeSprites.Remove(id);
            }

            // Materialize newly-wanted nodes.
            foreach (var id in wanted)
            {
                if (_nodeSprites.ContainsKey(id)) continue;
                var node = MapLookup.NodeById(state, id);
                if (node != null) MakeNodeSprite(node);
            }

            // Zoom-compensated scale so markers stay tappable at any zoom; LOD shrinks them.
            float markerScale = NodeMarkerScale(size, lodDot);
            foreach (var t in _nodeSprites.Values)
                t.localScale = new Vector3(markerScale, markerScale, 1f);
        }

        void MakeNodeSprite(MapNode node)
        {
            var root = new GameObject($"Node {node.Id}");
            root.transform.SetParent(_root!.transform, worldPositionStays: false);
            root.transform.localPosition = TileToWorld(node.Tile.X, node.Tile.Y);
            var color = KindColor(node.Kind) * TierTint[Mathf.Clamp(node.Tier, 0, TierTint.Length - 1)];
            color.a = 1f;
            MapVisuals.Spawn(root.transform, "Glow", MapVisuals.Glow, Vector3.zero, 2.2f, 2.2f,
                WithAlpha(color, 0.35f), 9);
            // Optional user art: Resources/Map/node-<kind>.png replaces the disc.
            var art = MapVisuals.OverrideSprite($"node-{node.Kind.ToString().ToLowerInvariant()}");
            // The procedural disc is fully tinted; ART shows at (near) native color
            // — multiplying by the dark kind-color was making quartz/helium/derelict
            // look muddy. A faint tier warmth still hints at risk.
            var discColor = art != null
                ? Color.Lerp(Color.white, TierTint[Mathf.Clamp(node.Tier, 0, TierTint.Length - 1)], 0.22f)
                : color;
            var disc = MapVisuals.Spawn(root.transform, "Disc", MapVisuals.Disc, Vector3.zero, 0.9f, 0.9f, discColor, 10);
            if (art != null) disc.sprite = art;
            root.AddComponent<MapBillboard>(); // face the viewer at the tilt
            _nodeSprites[node.Id] = root.transform;
        }

        // ---------- tap handling ----------

        void OnMapTap(Vector3 world)
        {
            var state = _ctx.State;
            if (state == null) return;
            var tile = WorldToTile(world);

            if (_relocating) { HandleRelocationTap(state, tile); return; }
            if (_redirectMarchId is int marchId) { HandleRedirectTap(state, marchId, tile); return; }

            StopFollowing(); // any new tap breaks fleet-follow

            // Pick radii MATCH the drawn marker size (world units), so tapping
            // anywhere on the visible orb selects it — not just its exact tile.
            float size = _mapCam!.orthographicSize;
            float nodeR = NodeMarkerScale(size, size > dotLodAbove) * 0.62f;
            float playerR = PlayerMarkerScale(size) * 0.62f;
            float WorldDist(Vector3 a, TileXY b)
            {
                var bw = TileToWorld(b.X, b.Y);
                return Mathf.Sqrt((bw.x - a.x) * (bw.x - a.x) + (bw.y - a.y) * (bw.y - a.y));
            }

            if (WorldDist(world, state.HomeTile) <= playerR) { ExitMap(); return; }

            // A fleet in flight? (grab radius scales a bit with zoom)
            float grab = Mathf.Max(1.5f, size * 0.03f);
            foreach (var m in state.Marches)
            {
                var p = MarchSystem.GetPosition(m, state.Tick);
                var mw = TileToWorld(p.X, p.Y);
                if (Mathf.Abs(mw.x - world.x) <= grab && Mathf.Abs(mw.y - world.y) <= grab)
                {
                    int id = m.Id;
                    _followMarchId = id;
                    UI.MarchCallout.Open(_ctx, id, onClose: () => _followMarchId = null);
                    return;
                }
            }

            // The Pirate Dreadnought, while one is in the galaxy.
            if (_boss != null)
            {
                float bx = world.x - _boss.position.x, by = world.y - _boss.position.y;
                float br = Mathf.Max(30f, _boss.localScale.x * 0.9f);
                if (bx * bx + by * by <= br * br)
                {
                    _selected = null;
                    UI.BossPanel.OpenCallout(_ctx);
                    return;
                }
            }

            // The Galactic Core: a tap on the station itself opens it (the traffic
            // crossing it can still be tapped anywhere else along its path).
            float dx = world.x - _coreWorld.x, dy = world.y - _coreWorld.y;
            if (dx * dx + dy * dy <= CoreTapRadius * CoreTapRadius)
            {
                _selected = null;
                UI.CorePanel.OpenCallout(_ctx);
                return;
            }

            // Another commander's flight? Radar-tracked inbound hostiles first, then
            // bot raids / spy probes / gather runs — follow them like your own.
            if (TryPickRivalFlight(world, grab, out long flightKey, out bool isContact))
            {
                _followRivalKey = flightKey;
                _followContact = isContact;
                OpenRivalFlightCallout(flightKey, isContact);
                return;
            }

            // Nearest live node whose orb covers the tap point.
            var node = PickNearestNode(state, world, nodeR);

            // Another commander's colony? Match the player-planet radius.
            if (node == null)
            {
                foreach (var kv in _remotes)
                {
                    if (WorldDist(world, kv.Value.Tile) <= playerR)
                    {
                        // Ring the planet like any other selected tile (user report:
                        // rival planets gave no selection feedback).
                        _selected = null;
                        SelectTile(kv.Value.Tile, planet: true);
                        UI.RemoteCallout.Open(_ctx, kv.Key); // bottom strip, like camps
                        return;
                    }
                }
            }

            if (node != null)
            {
                SelectNode(node);
                UI.UIController.Instance?.OpenNodeCallout(node);
            }
            else
            {
                // Empty space: circle the tapped point (so it's clear what the
                // callout references), then stage a fleet / port / bookmark it.
                _selected = null;
                SelectTile(tile);
                UI.BlankCallout.Open(_ctx, tile);
            }
        }

        /// <summary>Nearest visible rival flight dot within <paramref name="grab"/> world
        /// units of the tap. Inbound radar contacts win ties — they're the threat.</summary>
        bool TryPickRivalFlight(Vector3 world, float grab, out long key, out bool isContact)
        {
            key = 0;
            isContact = false;
            float best = grab * grab;
            bool found = false;
            foreach (var kv in _contactVisuals)
            {
                var p = kv.Value.Dot.position;
                float d2 = (p.x - world.x) * (p.x - world.x) + (p.y - world.y) * (p.y - world.y);
                if (d2 > best) continue;
                best = d2; key = kv.Key; isContact = true; found = true;
            }
            if (found) return true;
            foreach (var kv in _rivalFlights)
            {
                var p = kv.Value.Dot.position;
                float d2 = (p.x - world.x) * (p.x - world.x) + (p.y - world.y) * (p.y - world.y);
                if (d2 > best) continue;
                best = d2; key = kv.Key; found = true;
            }
            return found;
        }

        void OpenRivalFlightCallout(long key, bool isContact)
        {
            void OnClosed() => _followRivalKey = null; // the card's ×: stop chasing
            if (isContact) UI.RivalFlightCallout.OpenContact(_ctx, (int)key, OnClosed);
            else if (key >= GatherKeyBase) UI.RivalFlightCallout.OpenGatherRun(_ctx, (int)(key - GatherKeyBase), OnClosed);
            else UI.RivalFlightCallout.OpenRaid(_ctx, (int)key, OnClosed);
        }

        // ---------- targeting modes ----------

        /// <summary>Precision Warp: next map tap proposes a new home tile.</summary>
        public void BeginRelocation()
        {
            _relocating = true;
            _redirectMarchId = null;
            Notify("Tap an empty tile to warp your planet");
        }

        /// <summary>Spy-probe redirect: next map tap proposes the probe's new destination.</summary>
        public void BeginProbeRedirect(int marchId)
        {
            _redirectMarchId = marchId;
            _relocating = false;
            Notify("Tap anywhere on the map to redirect your probe");
        }

        public void CancelTargeting()
        {
            _relocating = false;
            _redirectMarchId = null;
        }

        void HandleRelocationTap(GameState state, TileXY tile)
        {
            // Validate everything BEFORE the confirm so the warp item can't be
            // consumed by a jump the sim will refuse (fleets out / core zone).
            if (state.Marches.Count > 0)
            {
                Notify("Recall all fleets before relocating");
                return;
            }
            if (MarchSystem.InCoreExclusion(tile))
            {
                Notify("The supernova core is forbidden space");
                return;
            }
            var sector = MapLookup.GetSector(state);
            if (tile.X < 0 || tile.Y < 0 || tile.X >= sector.Size || tile.Y >= sector.Size)
            {
                Notify("That's beyond the universe boundary");
                return;
            }
            if (!MapLookup.IsBlankTile(state, tile))
            {
                Notify("Pick an empty tile — no resource or pirate there");
                return;
            }
            // Preview: park the reticle on the proposed tile while the confirm is up.
            if (_selectRing != null)
            {
                _selectRing.transform.localPosition = TileToWorld(tile.X, tile.Y) + new Vector3(0f, 0f, -1f);
                _selectRing.SetActive(true);
            }
            UI.ConfirmPanel.Open($"Warp your planet to {tile.X},{tile.Y}?", "WARP", () =>
            {
                if (ShopSystem.ConsumeItem(state, "relocate-target").Ok)
                {
                    MarchSystem.RelocateHome(state, tile);
                    Notify("Planet relocated!");
                }
                else Notify("No Precision Warp in inventory");
                CancelTargeting();
                ClearSelection();
                FocusTile(state.HomeTile);
            },
            onCancel: () => { CancelTargeting(); ClearSelection(); }); // same escape-hatch fix
        }

        void HandleRedirectTap(GameState state, int marchId, TileXY tile)
        {
            March? march = null;
            foreach (var m in state.Marches)
                if (m.Id == marchId) { march = m; break; }
            if (march == null)
            {
                Notify("That probe already finished");
                CancelTargeting();
                return;
            }
            var sector = MapLookup.GetSector(state);
            if (tile.X < 0 || tile.Y < 0 || tile.X >= sector.Size || tile.Y >= sector.Size)
            {
                Notify("Pick a tile inside the sector");
                return;
            }
            UI.ConfirmPanel.Open($"Redirect probe to {tile.X},{tile.Y}?", "REDIRECT", () =>
            {
                var res = MarchSystem.RedirectMarch(state, marchId, tile);
                Notify(res.Ok ? $"Probe redirected to {tile.X},{tile.Y}" : res.Reason ?? "Cannot redirect");
                CancelTargeting();
            },
            // BACK must also exit targeting — it used to leave the mode armed
            // forever, re-prompting on every tap (user bug report).
            onCancel: CancelTargeting);
        }

        // ---------- search (v1 MapScene.searchNearest) ----------

        /// <summary>Center + select the nearest live node of a kind. level 0 = any; else camp level / tier+1.</summary>
        public bool SearchNearest(NodeKind kind, int level = 0)
        {
            var state = _ctx.State;
            if (state == null) return false;

            MapNode? best = null;
            double bestDist = double.MaxValue;
            foreach (var n in MapLookup.AllNodes(state))
            {
                if (n.Kind != kind) continue;
                state.Map.NodeOverrides.TryGetValue(n.Id, out var ov);
                if (ov != null && ov.Cleared) continue;
                if (kind != NodeKind.Camp && (ov?.Remaining ?? n.Amount) <= 0) continue;
                if (level > 0)
                {
                    int nodeLevel = kind == NodeKind.Camp ? n.CampLevel : n.Tier + 1;
                    if (nodeLevel != level) continue;
                }
                double d = TileXY.Distance(n.Tile, state.HomeTile);
                if (d < bestDist) { bestDist = d; best = n; }
            }
            if (best == null) return false;

            FocusTile(best.Tile);
            UI.UIController.Instance?.OpenNodeCallout(best);
            return true;
        }

        static MapNode? LiveNodeAt(GameState state, TileXY tile)
        {
            var node = MapLookup.NodeAt(state, tile);
            if (node == null) return null;
            state.Map.NodeOverrides.TryGetValue(node.Id, out var ov);
            if (ov != null && ov.Cleared) return null;
            if (node.Kind != NodeKind.Camp && (ov?.Remaining ?? node.Amount) <= 0) return null;
            return node;
        }

        /// <summary>Nearest live node whose marker (radius `worldRadius`) covers the
        /// tapped point — scans the tile window the radius spans.</summary>
        static MapNode? PickNearestNode(GameState state, Vector3 world, float worldRadius)
        {
            var center = WorldToTile(world);
            int r = Mathf.CeilToInt(worldRadius) + 1;
            MapNode? best = null;
            float bestD = worldRadius;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var node = LiveNodeAt(state, new TileXY(center.X + dx, center.Y + dy));
                    if (node == null) continue;
                    var nw = TileToWorld(node.Tile.X, node.Tile.Y);
                    float d = Mathf.Sqrt((nw.x - world.x) * (nw.x - world.x) + (nw.y - world.y) * (nw.y - world.y));
                    if (d <= bestD) { bestD = d; best = node; }
                }
            return best;
        }

        void SelectNode(MapNode node)
        {
            _selected = node;
            SelectTile(node.Tile);
        }

        /// <summary>True while the reticle circles a PLANET-sized marker (rival colony)
        /// rather than a node/tile — planets draw much larger than node orbs.</summary>
        bool _ringOnPlanet;

        /// <summary>Show the selection reticle at a bare tile (empty-space + remote taps).</summary>
        void SelectTile(TileXY tile, bool planet = false)
        {
            _ringOnPlanet = planet;
            if (_selectRing != null)
            {
                _selectRing.transform.localPosition = TileToWorld(tile.X, tile.Y) + new Vector3(0f, 0f, -1f);
                _selectRing.SetActive(true);
            }
        }

        public void ClearSelection()
        {
            _selected = null;
            if (_selectRing != null) _selectRing.SetActive(false);
        }

        void Notify(string msg) => UI.UIController.Instance?.Toast(msg, UI.Icon.Info, UI.UiTheme.Accent);

        // ---------- per-frame ----------

        void Update()
        {
            if (!_active || _mapCam == null) return;
            var state = _ctx.State;
            if (state == null) return;

            var cam = _mapCam;
            float t = Time.time;

            // Keep all billboarded markers facing the (fixed-rotation) camera so
            // planets/nodes/fleets stand up instead of foreshortening at the tilt.
            MapBillboard.Rotation = cam.transform.rotation;

            // Starfield is a camera-child backdrop; its DEPTH tracks the dolly
            // distance so it always sits behind the ground plane at any zoom, and
            // the UV offset drives parallax as the framed target pans the galaxy.
            float camDist = cam.orthographicSize / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            void UpdateLayer(Transform? layer, Material? mat, float depthMul, float parallax, float repeat)
            {
                if (layer == null || mat == null) return;
                float z = camDist * depthMul;
                layer.localPosition = new Vector3(0f, 0f, z);
                layer.localScale = new Vector3(z * 3f, z * 3f, 1f);
                var g = _camCtl != null ? _camCtl.Target : Vector2.zero;
                mat.mainTextureScale = new Vector2(6f, 6f);
                mat.mainTextureOffset = new Vector2(g.x, g.y) * (parallax / repeat);
            }
            UpdateLayer(_starFar, _starFarMat, 2.0f, 0.25f, 90f);
            UpdateLayer(_starNear, _starNearMat, 1.6f, 0.5f, 55f);

            // Reduced motion (Settings): no pulsing, and the decorative worlds hold still.
            bool still = Settings.ReducedMotion;

            // Supernova pulse (procedural core only — the art version stays regal and still).
            if (!_novaStatic && !still)
            {
                if (_novaShock != null)
                {
                    float s = _novaShockScale * (1f + 0.08f * Mathf.Sin(t * 2.6f));
                    _novaShock.localScale = new Vector3(s, s, 1f);
                }
                if (_novaCore != null)
                {
                    float s = _novaCoreScale * (1f + 0.18f * Mathf.Sin(t * 3.5f));
                    _novaCore.localScale = new Vector3(s, s, 1f);
                }
            }

            // Decorative worlds orbit the core; far side dims and slips behind.
            foreach (var o in _orbiters)
            {
                float a = (still ? 0f : t * o.speed) + o.phase;
                o.t.position = new Vector3(
                    _coreWorld.x + Mathf.Cos(a) * o.r,
                    _coreWorld.y + Mathf.Sin(a) * o.r * 0.62f,
                    o.t.position.z);
                bool front = Mathf.Sin(a) < 0f; // lower half = near side (y-down map read)
                o.sr.sortingOrder = front ? 6 : 3;
                float sc = front ? 14f : 11f;
                o.t.localScale = new Vector3(sc, sc, 1f);
                o.sr.color = WithAlpha(o.sr.color, front ? 1f : 0.6f);
            }

            // Home planet moves when relocated; scale stays legible at any zoom.
            if (_home != null)
            {
                if (!_homeCache.Equals(state.HomeTile))
                {
                    _homeCache = state.HomeTile;
                    _home.localPosition = TileToWorld(state.HomeTile.X, state.HomeTile.Y);
                }
                float hs = PlayerMarkerScale(cam.orthographicSize);
                _home.localScale = new Vector3(hs, hs, 1f);
                if (_homeSkinCache != state.Skins.ActivePlanet && _homeDisc != null && _homeGlow != null)
                {
                    _homeSkinCache = state.Skins.ActivePlanet;
                    ApplySkin(_homeDisc, _homeGlow, _homeSkinCache);
                }
                // Battle scar + Aegis bubble on your own planet.
                UpdateAftermathFx(_homeBurn, _homeShield,
                    burning: state.BurningUntilTick > state.Tick,
                    shielded: state.Buffs.ShieldUntilTick > state.Tick,
                    seed: 0f);
            }

            SyncBoss(t);

            // Coordinates readout replaces the header's HQ line while on the map.
            // Use the ground TARGET the camera frames, not its (tilted) position.
            var lookingAt = WorldToTile(new Vector3(_camCtl!.Target.x, _camCtl.Target.y, 0f));
            if (!lookingAt.Equals(_coordsCacheTile))
            {
                _coordsCacheTile = lookingAt;
                UI.UIController.Instance?.SetHqOverride($"◈ {lookingAt.X}, {lookingAt.Y}");
            }

            if (_selectRing != null && _selectRing.activeSelf)
            {
                // A touch larger than whatever it circles (node orb vs planet).
                float ringBase = _ringOnPlanet
                    ? PlayerMarkerScale(cam.orthographicSize)
                    : NodeMarkerScale(cam.orthographicSize, false);
                float rs = ringBase * 1.35f * (still ? 1f : 1f + 0.08f * Mathf.Sin(t * 7f));
                _selectRing.transform.localScale = new Vector3(rs, rs, 1f);
            }

            // Rival planets: refresh from the simulated galaxy on a light cadence
            // (they never move, but skins/rosters change on a galaxy reset).
            if (Time.time >= _nextRemotePoll)
            {
                _nextRemotePoll = Time.time + RemotePollSeconds;
                SyncRivalPlanets();
            }
            foreach (var kv in _remotes)
            {
                float rs = PlayerMarkerScale(cam.orthographicSize); // same size as home
                kv.Value.Root.localScale = new Vector3(rs, rs, 1f);
                UpdateAftermathFx(kv.Value.Burn, null,
                    kv.Value.Burning, false, seed: kv.Key * 1.7f);
            }

            UpdateMarchVisuals(state, cam);
            UpdateRivalFlights(state, cam);
            UpdateRadarContacts(state, cam);

            // Re-cull when the camera moved ~4 tiles or zoomed ~10% (v1 thresholds).
            var camPos = _camCtl != null ? _camCtl.Target : (Vector2)cam.transform.position;
            bool moved = Mathf.Abs(camPos.x - _lastSyncPos.x) > 4f || Mathf.Abs(camPos.y - _lastSyncPos.y) > 4f;
            bool zoomed = _lastSyncSize <= 0f || Mathf.Abs(cam.orthographicSize - _lastSyncSize) / _lastSyncSize > 0.1f;
            if (_needsSync || moved || zoomed)
            {
                _needsSync = false;
                _lastSyncPos = camPos;
                _lastSyncSize = cam.orthographicSize;
                SyncNodes(state);
            }
        }

        void UpdateMarchVisuals(GameState state, Camera cam)
        {
            _marchLive.Clear(); var live = _marchLive;
            float width = Mathf.Max(0.07f, cam.orthographicSize * 0.0035f); // thinner trail line (user feedback)
            float dotScale = Mathf.Max(1.8f, cam.orthographicSize * 0.035f); // bigger fleet/probe icons

            foreach (var march in state.Marches)
            {
                live.Add(march.Id);
                if (!_marchVisuals.TryGetValue(march.Id, out var vis))
                {
                    var root = new GameObject($"March {march.Id}");
                    root.transform.SetParent(_root!.transform, worldPositionStays: false);
                    var line = root.AddComponent<LineRenderer>();
                    line.material = _lineMat;
                    line.positionCount = 2;
                    line.sortingOrder = 7; // UNDER planets (10-11) — lines fly out from behind them
                    var dotSr = MapVisuals.Spawn(root.transform, "Dot", MapVisuals.Disc, Vector3.zero,
                        1f, 1f, Color.white, 13);
                    // Optional user art: Resources/Map/probe.png (spy) or fleet.png.
                    bool probeOnly = march.Ships.Count == 1 && march.Ships.ContainsKey(HullId.Probe);
                    var fleetArt = MapVisuals.OverrideSprite(probeOnly ? "probe" : "fleet");
                    if (fleetArt != null) dotSr.sprite = fleetArt;
                    vis = new MarchVisual
                    {
                        Root = root, Line = line, Dot = dotSr.transform,
                        DotSr = dotSr, HasArt = fleetArt != null,
                    };
                    for (int i = 0; i < vis.Trail.Length; i++)
                    {
                        vis.Trail[i] = MapVisuals.Spawn(root.transform, "Trail", MapVisuals.Glow,
                            Vector3.zero, 1f, 1f, Color.clear, 8); // above lines, below planets
                        vis.TrailAge[i] = TrailLifetime; // born expired → invisible
                    }
                    _marchVisuals[march.Id] = vis;
                }

                var color = march.Mission switch
                {
                    MarchMission.Attack    => new Color(0.95f, 0.45f, 0.4f),
                    MarchMission.Spy       => new Color(0.75f, 0.63f, 0.91f),
                    MarchMission.Intercept => new Color(1f, 0.66f, 0.25f),
                    MarchMission.Garrison  => new Color(0.45f, 0.9f, 0.55f),
                    MarchMission.Core      => new Color(0.95f, 0.82f, 0.35f),
                    MarchMission.Boss      => new Color(1f, 0.42f, 0.36f),
                    _                      => new Color(0.5f, 0.83f, 1f),
                };
                vis.Line.startColor = WithAlpha(color, 0.55f);
                vis.Line.endColor   = WithAlpha(color, 0.55f);
                vis.Line.startWidth = width;
                vis.Line.endWidth   = width;
                vis.Line.SetPosition(0, TileToWorld(march.LegFrom.X, march.LegFrom.Y, -0.5f));
                vis.Line.SetPosition(1, TileToWorld(march.LegTo.X, march.LegTo.Y, -0.5f));

                // Fractional tick → butter-smooth motion instead of 1 Hz hops.
                var pos = MarchSystem.GetPositionSmooth(march, _ctx.PreciseTick);
                var dotWorld = TileToWorld(pos.X, pos.Y, -1f);
                vis.Dot.position = dotWorld;
                vis.Dot.localScale = new Vector3(dotScale, dotScale, 1f);
                // Billboard the fleet marker so it faces the viewer at the tilt;
                // art rolls around the view axis to point along the heading.
                var legA = TileToWorld(march.LegFrom.X, march.LegFrom.Y);
                var legB = TileToWorld(march.LegTo.X, march.LegTo.Y);
                var dir = legB - legA;
                bool isProbeArt = march.Ships.Count == 1 && march.Ships.ContainsKey(HullId.Probe);
                float roll = vis.HasArt && dir.sqrMagnitude > 0.0001f
                    ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f + (isProbeArt ? probeRollOffset : 0f)
                    : 0f;
                vis.Dot.rotation = MapBillboard.Rotation * Quaternion.Euler(0f, 0f, roll);
                // User art keeps its own colors; the mission tint stays on the line/trail.
                vis.DotSr.color = vis.HasArt ? Color.white : color;

                // v1 engine trail: drop a glow dot every ~80ms while flying (parked
                // fleets — gathering or holding — don't trail), age it over 0.6s.
                bool flying = march.Phase != MarchPhase.Gathering;
                if (flying && Time.time >= vis.NextTrailDrop)
                {
                    vis.NextTrailDrop = Time.time + TrailDropInterval;
                    vis.TrailHead = (vis.TrailHead + 1) % vis.Trail.Length;
                    vis.TrailAge[vis.TrailHead] = 0f;
                    vis.Trail[vis.TrailHead].transform.position =
                        new Vector3(dotWorld.x, dotWorld.y, -0.9f);
                }
                for (int i = 0; i < vis.Trail.Length; i++)
                {
                    vis.TrailAge[i] = Mathf.Min(TrailLifetime, vis.TrailAge[i] + Time.deltaTime);
                    float t = vis.TrailAge[i] / TrailLifetime;
                    var tr = vis.Trail[i];
                    if (t >= 1f) { tr.color = Color.clear; continue; }
                    tr.color = WithAlpha(color, 0.8f * (1f - t));
                    float ts = dotScale * 0.8f * (1f + 0.6f * t);
                    tr.transform.localScale = new Vector3(ts, ts, 1f);
                }
            }

            _marchGone.Clear(); var gone = _marchGone;
            foreach (var id in _marchVisuals.Keys)
                if (!live.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                Destroy(_marchVisuals[id].Root);
                _marchVisuals.Remove(id);
            }
        }

        // ---------- rival fleet activity (user spec 2026-07-07) ----------
        // Every bot flight the player is allowed to see: bot-vs-bot raid marches
        // (real sim state — BotGalaxy.Marches) plus purely-cosmetic gather loops
        // for awake rivals. Each is a small dot + a very light directional line.
        // Fleets aimed at the PLAYER deliberately stay radar-gated (that's the
        // Radar Station's whole job), so they render via UpdateRadarContacts only.

        sealed class RivalFlightVisual
        {
            public GameObject Root = null!;
            public LineRenderer Line = null!;
            public Transform Dot = null!;
            public SpriteRenderer DotSr = null!;
        }

        readonly Dictionary<long, RivalFlightVisual> _rivalFlights = new();
        /// <summary>Gather loops hide past this zoom (raid marches stay — wars are the point).</summary>
        const float HideGatherLoopsAbove = 300f;
        const long GatherKeyBase = 1_000_000_000L; // key space separate from march ids

        RivalFlightVisual GetRivalFlight(long key, Color color, string artName)
        {
            if (_rivalFlights.TryGetValue(key, out var vis)) return vis;
            var root = new GameObject($"Rival Flight {key}");
            root.transform.SetParent(_root!.transform, worldPositionStays: false);
            var line = root.AddComponent<LineRenderer>();
            line.material = _lineMat;
            line.positionCount = 2;
            line.sortingOrder = 6; // under player march lines (7) and planets
            var dotSr = MapVisuals.Spawn(root.transform, "Dot", MapVisuals.Disc, Vector3.zero,
                1f, 1f, color, 12);
            var art = MapVisuals.OverrideSprite(artName);
            if (art != null) dotSr.sprite = art;
            vis = new RivalFlightVisual { Root = root, Line = line, Dot = dotSr.transform, DotSr = dotSr };
            _rivalFlights[key] = vis;
            return vis;
        }

        // Bot activity (a personality trait) is fixed per (seed, bot) but costs a
        // fresh closure-based RNG to derive — it used to be recomputed for all 249
        // rivals every frame. Cached per galaxy seed instead.
        readonly Dictionary<int, double> _activityCache = new();
        int _activitySeed = int.MinValue;

        double ActivityOf(int seed, int botId)
        {
            if (seed != _activitySeed) { _activityCache.Clear(); _activitySeed = seed; }
            if (!_activityCache.TryGetValue(botId, out var activity))
            {
                activity = GalaxyRoyale.Sim.Bots.BotSystem.PersonalityOf(seed, botId).Activity;
                _activityCache[botId] = activity;
            }
            return activity;
        }

        void UpdateRivalFlights(GameState state, Camera cam)
        {
            _flightLive.Clear(); var live = _flightLive;
            var galaxy = _ctx.Bots;
            if (galaxy != null)
            {
                float size = cam.orthographicSize;
                float width = Mathf.Max(0.05f, size * 0.002f);          // very light line
                float dotScale = Mathf.Max(1.2f, size * 0.022f);        // smaller than player fleets
                double now = _ctx.PreciseTick;

                // In-view test against the framed target (same idea as SyncNodes).
                float halfH = size * 1.8f + 40f;
                float halfW = size * cam.aspect * 1.4f + 40f;
                Vector2 c = _camCtl != null ? _camCtl.Target : (Vector2)cam.transform.position;
                bool InView(Vector3 w) =>
                    w.x >= c.x - halfW && w.x <= c.x + halfW && w.y >= c.y - halfH && w.y <= c.y + halfH;

                void Draw(long key, Vector3 fromW, Vector3 toW, double t, Color color, float alpha,
                    string artName = "fleet")
                {
                    var pos = Vector3.Lerp(fromW, toW, (float)t);
                    if (!InView(pos) && !InView(fromW) && !InView(toW)) return;
                    live.Add(key);
                    var vis = GetRivalFlight(key, color, artName);
                    vis.Line.startColor = WithAlpha(color, alpha);
                    vis.Line.endColor = WithAlpha(color, alpha * 0.4f); // fades toward the destination
                    vis.Line.startWidth = width;
                    vis.Line.endWidth = width;
                    vis.Line.SetPosition(0, new Vector3(pos.x, pos.y, -0.4f));
                    vis.Line.SetPosition(1, new Vector3(toW.x, toW.y, -0.4f));
                    vis.Dot.position = new Vector3(pos.x, pos.y, -0.8f);
                    vis.Dot.localScale = new Vector3(dotScale, dotScale, 1f);
                    bool hasArt = MapVisuals.OverrideSprite(artName) != null; // cached lookup
                    var dir = toW - fromW;
                    float roll = hasArt && dir.sqrMagnitude > 0.0001f
                        ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f
                          + (artName == "probe" ? probeRollOffset : 0f)
                        : 0f;
                    vis.Dot.rotation = MapBillboard.Rotation * Quaternion.Euler(0f, 0f, roll);
                    vis.DotSr.color = hasArt ? WithAlpha(Color.white, 0.9f) : color;
                }

                // --- real bot flights: raid fleets (red), recon probes (violet), and
                // your clanmates' strike wings (teal) and garrisons (green) ---
                var raidColor = new Color(0.95f, 0.5f, 0.42f);
                var spyColor = new Color(0.75f, 0.63f, 0.91f); // matches player spy missions
                var wingColor = new Color(0.35f, 0.9f, 0.8f);
                var guardColor = new Color(0.45f, 0.9f, 0.55f); // matches your own garrisons
                var coreColor = new Color(0.98f, 0.72f, 0.3f);  // an assault on the Galactic Core
                var bossColor = new Color(1f, 0.36f, 0.3f);     // a strike on the Pirate Dreadnought
                foreach (var m in galaxy.Marches)
                {
                    var color = m.Kind switch
                    {
                        GalaxyRoyale.Sim.Bots.BotMarchKind.Spy => spyColor,
                        GalaxyRoyale.Sim.Bots.BotMarchKind.Escort => wingColor,
                        GalaxyRoyale.Sim.Bots.BotMarchKind.Garrison => guardColor,
                        GalaxyRoyale.Sim.Bots.BotMarchKind.CoreAssault => coreColor,
                        GalaxyRoyale.Sim.Bots.BotMarchKind.BossStrike => bossColor,
                        _ => raidColor,
                    };
                    string art = m.IsSpy ? "probe" : "fleet";
                    var fromW = TileToWorld(m.From.X, m.From.Y);
                    var toW = TileToWorld(m.To.X, m.To.Y);
                    if (!m.Resolved)
                    {
                        double span = System.Math.Max(1, m.ArrivesAtTick - m.LaunchTick);
                        double t = System.Math.Clamp((now - m.LaunchTick) / span, 0.0, 1.0);
                        Draw(m.Id, fromW, toW, t, color, 0.30f, art);
                    }
                    else
                    {
                        double span = System.Math.Max(1, m.ReturnsAtTick - m.ArrivesAtTick);
                        double t = System.Math.Clamp((now - m.ArrivesAtTick) / span, 0.0, 1.0);
                        Draw(m.Id, toW, fromW, t, color, 0.22f, art); // homeward, dimmer
                    }
                }

                // --- cosmetic gather loops: awake rivals shuttling to nearby nodes ---
                // Hidden when zoomed far out — except one the camera is following.
                long followedGather = !_followContact && _followRivalKey is long fk && fk >= GatherKeyBase ? fk : -1;
                if (size <= HideGatherLoopsAbove || followedGather >= 0)
                {
                    var gatherColor = new Color(0.5f, 0.83f, 1f);
                    int hour = state.Tick / 3600;
                    foreach (var bot in galaxy.Bots)
                    {
                        long key = GatherKeyBase + bot.Id;
                        if (size > HideGatherLoopsAbove && key != followedGather) continue;
                        if (!GalaxyRoyale.Sim.Bots.BotSystem.IsAwake(
                            state.Seed, bot.Id, ActivityOf(state.Seed, bot.Id), state.Tick))
                            continue;
                        uint s = unchecked((uint)state.Seed * 97u + (uint)bot.Id);
                        double ang = Rng.Hash2d(s, hour, 11) * System.Math.PI * 2;
                        double dist = 8 + Rng.Hash2d(s, hour, 22) * 18;
                        var homeW = TileToWorld(bot.HomeTile.X, bot.HomeTile.Y);
                        var nodeW = homeW + new Vector3(
                            (float)(System.Math.Cos(ang) * dist), (float)(System.Math.Sin(ang) * dist), 0f);
                        // 10-min loop, phase-shifted per bot: fly out, sit, fly home.
                        double phase = ((now + bot.Id * 37) % 600) / 600.0;
                        if (phase < 0.42)
                            Draw(key, homeW, nodeW, phase / 0.42, gatherColor, 0.20f);
                        else if (phase < 0.58)
                            Draw(key, homeW, nodeW, 1.0, gatherColor, 0.14f); // on station
                        else
                            Draw(key, nodeW, homeW, (phase - 0.58) / 0.42, gatherColor, 0.20f);
                    }
                }
            }

            _flightGone.Clear(); var goneFlights = _flightGone;
            foreach (var key in _rivalFlights.Keys)
                if (!live.Contains(key)) goneFlights.Add(key);
            foreach (var key in goneFlights)
            {
                Destroy(_rivalFlights[key].Root);
                _rivalFlights.Remove(key);
            }
        }

        // ---------- radar contacts (Radar Station, content expansion) ----------
        // Inbound spies/fleets the radar has detected (only inside the lead
        // window — user decision 2026-07-05): a pulsing red contact dot
        // interpolated along its launch→home track, plus the red target line
        // pointing at the player's planet.

        sealed class ContactVisual
        {
            public GameObject Root = null!;
            public LineRenderer Line = null!;
            public Transform Dot = null!;
            public SpriteRenderer DotSr = null!;
        }

        readonly Dictionary<long, ContactVisual> _contactVisuals = new();

        void UpdateRadarContacts(GameState state, Camera cam)
        {
            _contactLive.Clear(); var live = _contactLive;
            float width = Mathf.Max(0.14f, cam.orthographicSize * 0.007f);
            float dotScale = Mathf.Max(2f, cam.orthographicSize * 0.04f);
            var red = new Color(1f, 0.25f, 0.2f);
            double nowTick = _ctx.PreciseTick;
            var homeW = TileToWorld(state.HomeTile.X, state.HomeTile.Y, -0.5f);

            foreach (var ev in RadarService.DetectedThreats)
            {
                if (ev.ArrivesAtTick <= nowTick) continue; // impact passed — glow handles the aftermath
                live.Add(ev.Id);
                if (!_contactVisuals.TryGetValue(ev.Id, out var vis))
                {
                    var root = new GameObject($"Radar Contact {ev.Id}");
                    root.transform.SetParent(_root!.transform, worldPositionStays: false);
                    var line = root.AddComponent<LineRenderer>();
                    line.material = _lineMat;
                    line.positionCount = 2;
                    line.sortingOrder = 7; // under planets, like march lines
                    var dotSr = MapVisuals.Spawn(root.transform, "Contact", MapVisuals.Disc,
                        Vector3.zero, 1f, 1f, red, 13);
                    vis = new ContactVisual { Root = root, Line = line, Dot = dotSr.transform, DotSr = dotSr };
                    _contactVisuals[ev.Id] = vis;
                }

                // Interpolate along the launch→home track by sim-clock progress.
                double span = ev.ArrivesAtTick - ev.LaunchTick;
                double t = span > 0 ? (nowTick - ev.LaunchTick) / span : 1.0;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                double px = ev.FromX + (state.HomeTile.X - ev.FromX) * t;
                double py = ev.FromY + (state.HomeTile.Y - ev.FromY) * t;
                var dotW = TileToWorld(px, py, -1f);

                vis.Line.startColor = WithAlpha(red, 0.75f);
                vis.Line.endColor = WithAlpha(red, 0.35f);
                vis.Line.startWidth = width;
                vis.Line.endWidth = width;
                vis.Line.SetPosition(0, new Vector3(dotW.x, dotW.y, -0.5f));
                vis.Line.SetPosition(1, homeW);

                vis.Dot.position = dotW;
                vis.Dot.localScale = new Vector3(dotScale, dotScale, 1f);
                vis.Dot.rotation = MapBillboard.Rotation; // face the viewer at the tilt
                float pulse = 0.55f + 0.45f * Mathf.PingPong(Time.time * 2.2f, 1f);
                vis.DotSr.color = WithAlpha(red, pulse);
            }

            _contactGone.Clear(); var goneContacts = _contactGone;
            foreach (var id in _contactVisuals.Keys)
                if (!live.Contains(id)) goneContacts.Add(id);
            foreach (var id in goneContacts)
            {
                Destroy(_contactVisuals[id].Root);
                _contactVisuals.Remove(id);
            }
        }

        // ---------- name labels (UI Toolkit layer under the HUD) ----------
        //
        // Were IMGUI: they painted over the header (a rival's name printed across
        // the commander pill in the user's screenshots), sized text in raw pixels,
        // and piled up. Now they're occluded by the HUD, scale with the screen,
        // and declutter with the home label + strongest commanders placed first.

        static readonly Color HomeLabelColor = new(1f, 0.6f, 0.24f, 0.97f); // your colony in the accent orange
        static readonly Color RemoteLabelColor = new(0.84f, 0.66f, 1f, 0.97f);

        void PlaceLabels(GameState state)
        {
            _labels ??= UI.UIController.Instance?.CreateWorldLabelLayer("map-labels");
            if (_labels == null || _mapCam == null) return;
            if (UI.UIController.Instance?.HasModal == true) { _labels.Clear(); return; } // panel up: no clutter
            _labels.Begin();

            if (_home != null)
            {
                // Might only moves when the sim ticks — rebuild the string 1×/s, not 60×.
                if (state.Tick != _homeLabelTick)
                {
                    _homeLabelTick = state.Tick;
                    long might = GalaxyRoyale.Sim.Systems.PowerSystem.ComputePower(state);
                    _homeLabelText =
                        $"{state.Profile.Name}\nCC {state.Buildings[BuildingId.CommandCenter].Level} · might {might:N0}";
                }
                _labels.Place(LabelAnchor(_home.position, _home.localScale.y), _homeLabelText,
                    11, HomeLabelColor, above: true, declutter: false);
            }

            // Who holds the Galactic Core (rebuilt 1×/s with the sim clock).
            if (_novaCore != null && _ctx.Bots != null)
            {
                if (state.Tick != _coreLabelTick)
                {
                    _coreLabelTick = state.Tick;
                    _coreLabelText = $"GALACTIC CORE\nheld by {GalaxyRoyale.Sim.Systems.CoreSystem.HolderName(state, _ctx.Bots)}";
                }
                _labels.Place(LabelAnchor(_novaCore.position, _novaCore.localScale.y * 0.5f), _coreLabelText,
                    12, CoreLabelColor, above: true, declutter: false);
            }

            // The Pirate Dreadnought's hull, while it's here.
            if (_boss != null && _ctx.Bots is { } g && g.Boss.Active)
            {
                if (state.Tick != _bossLabelTick)
                {
                    _bossLabelTick = state.Tick;
                    _bossLabelText = $"PIRATE DREADNOUGHT\n{System.Math.Round(GalaxyRoyale.Sim.Systems.BossSystem.HullShare(g.Boss) * 100):0}% hull";
                }
                _labels.Place(LabelAnchor(_boss.position, _boss.localScale.y * 0.6f), _bossLabelText,
                    12, BossLabelColor, above: true, declutter: false);
            }

            if (_mapCam.orthographicSize <= HideNameLabelsAbove)
            {
                foreach (int id in _labelOrder)
                {
                    if (!_remotes.TryGetValue(id, out var vis)) continue;
                    _labels.Place(LabelAnchor(vis.Root.position, vis.Root.localScale.y), vis.Name,
                        10, RemoteLabelColor, above: true);
                }
            }
            _labels.End();
        }

        /// <summary>Above this zoom the rival name labels are unreadable soup —
        /// the whole-universe view shows planets only.</summary>
        const float HideNameLabelsAbove = 420f;

        /// <summary>Screen point just above a billboarded planet marker — offset along
        /// the CAMERA's up axis (world-up foreshortens at the map tilt, which is
        /// what made labels clip into their planets).</summary>
        Vector3 LabelAnchor(Vector3 planetPos, float planetScale)
        {
            var top = planetPos + _mapCam!.transform.up * (planetScale * 0.68f);
            return _mapCam.WorldToScreenPoint(top);
        }

        int _homeLabelTick = -1;
        string _homeLabelText = "";
        int _coreLabelTick = -1;
        string _coreLabelText = "";
        /// <summary>World units (≈ tiles) around the core centre that open the core.</summary>
        const float CoreTapRadius = 110f;
        static readonly Color CoreLabelColor = new(0.98f, 0.82f, 0.45f, 0.97f);
        int _bossLabelTick = -1;
        string _bossLabelText = "";
        static readonly Color BossLabelColor = new(1f, 0.5f, 0.45f, 0.97f);

        // ---------- the Pirate Dreadnought ----------

        Transform? _boss;
        int _bossVisit = -1;

        /// <summary>A red hulk (the fleet art, or a disc) in a pulsing glow while a
        /// dreadnought is in the galaxy; gone when it leaves.</summary>
        void SyncBoss(float t)
        {
            var boss = _ctx.Bots?.Boss;
            if (boss == null || !boss.Active || _root == null)
            {
                if (_boss != null) { Destroy(_boss.gameObject); _boss = null; }
                return;
            }
            if (_boss == null || _bossVisit != boss.Visit)
            {
                if (_boss != null) Destroy(_boss.gameObject);
                _bossVisit = boss.Visit;
                var root = new GameObject("Pirate Dreadnought").transform;
                root.SetParent(_root.transform, false);
                root.localPosition = TileToWorld(boss.Tile.X, boss.Tile.Y);
                MapVisuals.Spawn(root, "Glow", MapVisuals.Glow, new Vector3(0f, 0f, 0.4f), 2.4f, 2.4f,
                    new Color(1f, 0.22f, 0.18f, 0.6f), 6);
                var art = MapVisuals.OverrideSprite("fleet");
                MapVisuals.Spawn(root, "Hull", art ?? MapVisuals.Disc, Vector3.zero, 1f, 1f,
                    art != null ? new Color(1f, 0.55f, 0.5f) : new Color(0.85f, 0.25f, 0.22f), 9);
                root.gameObject.AddComponent<MapBillboard>(); // stands up facing the viewer
                _boss = root;
            }
            float pulse = Settings.ReducedMotion ? 1f : 1f + 0.06f * Mathf.Sin(t * 3f);
            float s = PlayerMarkerScale(_mapCam!.orthographicSize) * 1.6f * pulse;
            _boss.localScale = new Vector3(s, s, 1f);
        }

        // ---------- rival planets (the simulated commanders) ----------

        void SyncRivalPlanets()
        {
            var galaxy = _ctx.Bots;
            if (galaxy == null || _root == null) return;

            var live = new HashSet<int>();
            foreach (var bot in galaxy.Bots)
            {
                live.Add(bot.Id);
                if (!_remotes.TryGetValue(bot.Id, out var vis))
                {
                    var root = new GameObject($"Rival {bot.Id}");
                    root.transform.SetParent(_root!.transform, worldPositionStays: false);
                    var glow = MapVisuals.Spawn(root.transform, "Glow", MapVisuals.Glow, Vector3.zero, 2.6f, 2.6f,
                        new Color(0.72f, 0.45f, 1f, 0.55f), 10);
                    var disc = MapVisuals.Spawn(root.transform, "Disc", MapVisuals.Disc, Vector3.zero, 1f, 1f,
                        new Color(0.72f, 0.50f, 0.95f), 11);
                    var burn = SpawnBurnFlame(root.transform);
                    root.AddComponent<MapBillboard>(); // rival planets stand up too
                    vis = new RemoteVisual { Root = root.transform, Disc = disc, Glow = glow, Burn = burn };
                    _remotes[bot.Id] = vis;
                }
                vis.Name = bot.Name;
                vis.Tile = bot.HomeTile;
                vis.Root.localPosition = TileToWorld(bot.HomeTile.X, bot.HomeTile.Y);
                // Battle scar: their planet burns for 4 h after a lost defense —
                // returning players can read last night's wars off the map.
                vis.Burning = bot.State.BurningUntilTick > (_ctx.State?.Tick ?? 0);
                string skin = bot.State.Skins.ActivePlanet;
                if (vis.Skin != skin)
                {
                    vis.Skin = skin;
                    ApplySkin(vis.Disc, vis.Glow, skin);
                }
            }

            // Rivals that vanished (galaxy reset) drop off.
            var stale = new List<int>();
            foreach (var kv in _remotes)
                if (!live.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                Destroy(_remotes[id].Root.gameObject);
                _remotes.Remove(id);
            }

            // Label priority for the declutter pass: strongest commanders first.
            // (Snapshot might once — BotGalaxy.Find is a linear scan.)
            _labelOrder.Clear();
            _mightSnapshot.Clear();
            foreach (var bot in galaxy.Bots)
            {
                _labelOrder.Add(bot.Id);
                _mightSnapshot[bot.Id] = bot.CachedMight;
            }
            _labelOrder.Sort(_byMightDesc ??= (a, b) =>
            {
                int byMight = _mightSnapshot[b].CompareTo(_mightSnapshot[a]);
                return byMight != 0 ? byMight : a.CompareTo(b);
            });
        }

        readonly Dictionary<int, long> _mightSnapshot = new();
        System.Comparison<int>? _byMightDesc;
    }
}
