// Building markers on the home planet: the 8 singleton buildings in the
// circuit-board layout, extra-mine instances (v1 F1) on a 9-plot expansion
// ring, and ghost "+ BUILD" plots for unlocked-but-empty mine slots.
//
// Tapping a marker opens the UI Toolkit BuildingPanel (the old IMGUI info card
// is gone); tapping a ghost opens BuildMineChooser. The only IMGUI left here is
// the world-anchored name/level labels.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;
using GalaxyRoyale.Game.UI;

namespace GalaxyRoyale.Game
{
    [AddComponentMenu("GalaxyRoyale/Building Markers")]
    [RequireComponent(typeof(GameContext))]
    public sealed class BuildingMarkers : MonoBehaviour
    {
        [Header("Circuit-board layout on the near hemisphere")]
        [SerializeField] float innerRingDegrees = 14f;
        [SerializeField] float outerRingDegrees = 28f;
        [Tooltip("Expansion plots (extra mines) sit on this third ring.")]
        [SerializeField] float plotRingDegrees = 40f;
        [SerializeField] float surfaceOffset = 0.02f;

        [Header("Marker appearance")]
        [SerializeField] float markerScale = 0.09f;
        [SerializeField] float ccScaleMultiplier = 2.2f;
        [Tooltip("Art billboards read better bigger than the old cubes; multiplies markerScale.")]
        [SerializeField] float artScaleMultiplier = 4.3f;
        [Tooltip("How far the art is lifted off the surface (× size). Lower = hugs the planet instead of floating, especially when the planet is angled.")]
        [SerializeField] float artLiftFactor = 0.28f;
        [SerializeField] bool showLabels = true;
        [SerializeField, Range(8, 22)] int labelFontSize = 14;

        [Header("Colors by level")]
        [SerializeField] Color level0Color = new(0.30f, 0.30f, 0.34f);
        [SerializeField] Color level1Color = new(0.55f, 0.85f, 1.00f);
        [SerializeField] Color leveledColor = new(0.55f, 1.00f, 0.55f);
        [SerializeField] Color maxLevelColor = new(1.00f, 0.80f, 0.30f);
        [SerializeField] Color ghostColor = new(0.35f, 0.55f, 0.70f);

        const int PlotCount = 9;

        // Markers live on their own layer, drawn by a depth-clearing OVERLAY camera
        // stacked on the base camera — so a rotated planet can never clip through
        // an icon (user report: buildings sank into the sphere near the limb).
        // Layer 1 is Unity's built-in TransparentFX — free to repurpose, no
        // project-settings edit needed. Far-side markers still hide via the
        // horizon cull below, so "always on top" never means "visible through".
        const int MarkerLayer = 1;
        /// <summary>Markers hide once their surface normal grazes the horizon —
        /// slightly before the exact limb so an always-on-top icon never hovers
        /// against empty space at the planet's edge.</summary>
        const float HorizonCullDot = 0.08f;
        Camera? _overlayCam;

        static BuildingMarkers? s_instance;

        readonly Dictionary<BuildingId, GameObject> _markers = new();
        readonly Dictionary<BuildingId, Renderer> _renderers = new();
        // Art-billboard extras (empty when a building has no processed art and uses the cube).
        readonly Dictionary<BuildingId, Transform> _artRoots = new();
        readonly Dictionary<BuildingId, Renderer> _glowRenderers = new();

        // Extra mines (keyed by ExtraMine.Id) + ghost expansion plots.
        readonly Dictionary<int, GameObject> _mineRoots = new();
        readonly Dictionary<int, GameObject> _mineMarkers = new();
        readonly Dictionary<int, Renderer> _mineRenderers = new();
        readonly Dictionary<int, Renderer> _mineGlows = new();
        readonly Dictionary<int, string> _mineLabels = new();
        readonly Dictionary<GameObject, int> _ghosts = new();

        GameContext? _ctx;
        Transform? _planet;
        float _planetRadius = 1f;

        BuildingId? _selected;
        int? _selectedMine;

        void OnEnable()
        {
            CameraController.OnTapHit += HandleTapHit;
            CameraController.OnTapEmpty += HandleTapEmpty;
        }

        void OnDisable()
        {
            CameraController.OnTapHit -= HandleTapHit;
            CameraController.OnTapEmpty -= HandleTapEmpty;
        }

        void Start()
        {
            s_instance = this;
            _ctx = GetComponent<GameContext>();
            if (_ctx?.State == null || _ctx.Events == null) { Warn("GameContext not initialized"); return; }

            var planetGO = GameObject.Find("Home Planet");
            if (planetGO == null) { Warn("Home Planet not found in scene"); return; }
            _planet = planetGO.transform;
            var meshTransform = planetGO.transform.Find("Planet Mesh");
            _planetRadius = meshTransform != null ? meshTransform.localScale.x * 0.5f : 1f;

            SpawnMarkers(_ctx.State);
            SyncExtraMines(_ctx.State);
            RefreshAll(_ctx.State);
            _ctx.Events.Subscribe(OnSimEvent);
            EnsureOverlayCamera();
        }

        /// <summary>Overlay camera that redraws the marker layer on top of the planet
        /// (depth cleared). Child of the base camera, so it tracks every move/zoom.</summary>
        void EnsureOverlayCamera()
        {
            if (_overlayCam != null) return;
            var baseCam = Camera.main;
            if (baseCam == null) return;

            baseCam.cullingMask &= ~(1 << MarkerLayer); // base pass skips markers

            var go = new GameObject("Building Marker Overlay Camera");
            go.transform.SetParent(baseCam.transform, worldPositionStays: false);
            _overlayCam = go.AddComponent<Camera>();
            _overlayCam.cullingMask = 1 << MarkerLayer;
            _overlayCam.fieldOfView = baseCam.fieldOfView;
            _overlayCam.nearClipPlane = baseCam.nearClipPlane;
            _overlayCam.farClipPlane = baseCam.farClipPlane;

            // Overlay cameras clear depth by default (m_ClearDepth = true, and the
            // property is read-only in URP 17) — the planet's depth is wiped, so
            // markers always draw on top of the sphere.
            var overlayData = _overlayCam.GetUniversalAdditionalCameraData();
            overlayData.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;

            baseCam.GetUniversalAdditionalCameraData().cameraStack.Add(_overlayCam);
        }

        /// <summary>Put a marker (and its whole subtree) on the overlay layer.</summary>
        static void SetMarkerLayer(GameObject go)
        {
            go.layer = MarkerLayer;
            foreach (Transform child in go.transform) SetMarkerLayer(child.gameObject);
        }

        /// <summary>UI actions that add/remove mines (chooser, cancel) call this to resync markers.</summary>
        public static void RequestExtraMineSync()
        {
            var inst = s_instance;
            if (inst?._ctx?.State == null || inst._planet == null) return;
            inst.SyncExtraMines(inst._ctx.State);
            inst.RefreshAll(inst._ctx.State);
        }

        void SpawnMarkers(GameState state)
        {
            foreach (var id in Buildings.All)
            {
                Vector3 localPos = BuildingSpherePosition(id);
                float scale = markerScale * (id == BuildingId.CommandCenter ? ccScaleMultiplier : 1f);

                // Processed illustration (BuildingArtProcessor output) → billboard quad;
                // otherwise fall back to the original level-tinted cube.
                var art = UnityEngine.Resources.Load<Texture2D>($"Buildings/{id}");
                if (art != null)
                {
                    var root = SpawnArtBillboard($"Building_{id}", localPos, scale * artScaleMultiplier,
                        art, out var quad, out var artRenderer, out var glowRenderer);
                    _artRoots[id] = root.transform;
                    _markers[id] = quad;          // tap raycasts hit the art quad's BoxCollider
                    _renderers[id] = artRenderer; // culling + labels track the art
                    _glowRenderers[id] = glowRenderer;
                    continue;
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Building_{id}";
                go.transform.SetParent(_planet, worldPositionStays: false);
                go.transform.localPosition = localPos;
                go.transform.up = localPos.normalized;
                go.transform.localScale = Vector3.one * scale;
                // Keep the BoxCollider so raycasts hit us.

                var renderer = go.GetComponent<Renderer>();
                var cubeMat = MapVisuals.LitOpaque(); // per-cube instance — RefreshAll tints each independently
                if (cubeMat != null) renderer.sharedMaterial = cubeMat;
                SetMarkerLayer(go);
                _markers[id] = go;
                _renderers[id] = renderer;
            }
        }

        /// <summary>Billboarded art quad (tap collider + level-glow halo behind), parented to the planet.</summary>
        GameObject SpawnArtBillboard(string name, Vector3 localPos, float size, Texture2D art,
            out GameObject quad, out Renderer artRenderer, out Renderer glowRenderer)
        {
            // Root sits on the sphere and billboards toward the camera each frame;
            // children hang off it at local z offsets (+z = away from camera).
            var root = new GameObject(name);
            root.transform.SetParent(_planet, worldPositionStays: false);
            // Lift so the illustration's base hugs the surface instead of sinking into it.
            // Kept small so icons don't float high above an angled planet (user feedback).
            root.transform.localPosition = localPos + localPos.normalized * (size * artLiftFactor);

            float aspect = art.height / (float)art.width;

            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Art";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, worldPositionStays: false);
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localScale = new Vector3(size, size * aspect, 1f);
            var box = quad.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 0.05f); // give the tap raycast some thickness
            artRenderer = quad.GetComponent<Renderer>();
            artRenderer.sharedMaterial = MapVisuals.UnlitTransparent(art);

            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "Level Glow";
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(root.transform, worldPositionStays: false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.02f); // just behind the art
            glow.transform.localScale = new Vector3(size * 1.5f, size * 1.5f, 1f);
            glowRenderer = glow.GetComponent<Renderer>();
            glowRenderer.sharedMaterial = MapVisuals.UnlitTransparent(MapVisuals.Glow.texture);

            SetMarkerLayer(root);
            return root;
        }

        // ---------- extra mines + ghost plots (v1 F1) ----------

        void SyncExtraMines(GameState state)
        {
            // Drop markers whose mine no longer exists (cancelled level-0 placement).
            var live = new HashSet<int>();
            foreach (var m in state.ExtraMines) live.Add(m.Id);
            var stale = new List<int>();
            foreach (var kv in _mineRoots) if (!live.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (int mid in stale)
            {
                Destroy(_mineRoots[mid]);
                _mineRoots.Remove(mid);
                _mineMarkers.Remove(mid);
                _mineRenderers.Remove(mid);
                _mineGlows.Remove(mid);
                _mineLabels.Remove(mid);
                if (_selectedMine == mid) _selectedMine = null;
            }

            // Spawn markers for new mines.
            foreach (var mine in state.ExtraMines)
            {
                if (_mineRoots.ContainsKey(mine.Id)) continue;
                var bid = MineTypes.ToBuildingId(mine.Type);
                Vector3 localPos = PlotSpherePosition(mine.Plot);
                float size = markerScale * artScaleMultiplier * 0.85f; // slightly smaller than singletons

                var art = UnityEngine.Resources.Load<Texture2D>($"Buildings/{bid}");
                if (art != null)
                {
                    var root = SpawnArtBillboard($"Mine_{mine.Id}", localPos, size,
                        art, out var quad, out var artRenderer, out var glowRenderer);
                    _mineRoots[mine.Id] = root;
                    _mineMarkers[mine.Id] = quad;
                    _mineRenderers[mine.Id] = artRenderer;
                    _mineGlows[mine.Id] = glowRenderer;
                }
                else
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Mine_{mine.Id}";
                    go.transform.SetParent(_planet, worldPositionStays: false);
                    go.transform.localPosition = localPos;
                    go.transform.up = localPos.normalized;
                    go.transform.localScale = Vector3.one * markerScale;
                    var renderer = go.GetComponent<Renderer>();
                    var mat = MapVisuals.LitOpaque();
                    if (mat != null) renderer.sharedMaterial = mat;
                    SetMarkerLayer(go);
                    _mineRoots[mine.Id] = go;
                    _mineMarkers[mine.Id] = go;
                    _mineRenderers[mine.Id] = renderer;
                }

                int n = 2;
                foreach (var other in state.ExtraMines)
                    if (other.Type == mine.Type && other.Id < mine.Id) n++;
                _mineLabels[mine.Id] = $"{Buildings.Defs[bid].Name} #{n}";
            }

            // Ghost plots: one "+ BUILD" cube per free plot, up to the total number
            // of extra mines the Command Center currently allows but aren't built.
            foreach (var kv in _ghosts) Destroy(kv.Key);
            _ghosts.Clear();

            int allowed = BuildingSystem.AllowedMinesForType(state);
            int buildable = 0;
            buildable += Mathf.Max(0, allowed - BuildingSystem.MineCountOfType(state, MineType.GoldMine));
            buildable += Mathf.Max(0, allowed - BuildingSystem.MineCountOfType(state, MineType.QuartzExtractor));
            buildable += Mathf.Max(0, allowed - BuildingSystem.MineCountOfType(state, MineType.HeliumRefinery));
            if (buildable <= 0) return;

            var occupied = new HashSet<int>();
            foreach (var m in state.ExtraMines) occupied.Add(m.Plot);

            int ghostsLeft = buildable;
            for (int plot = 0; plot < PlotCount && ghostsLeft > 0; plot++)
            {
                if (occupied.Contains(plot)) continue;
                ghostsLeft--;

                Vector3 localPos = PlotSpherePosition(plot);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"GhostPlot_{plot}";
                go.transform.SetParent(_planet, worldPositionStays: false);
                go.transform.localPosition = localPos;
                go.transform.up = localPos.normalized;
                go.transform.localScale = Vector3.one * (markerScale * 0.8f);
                var renderer = go.GetComponent<Renderer>();
                var mat = MapVisuals.LitOpaque();
                if (mat != null)
                {
                    mat.color = ghostColor;
                    renderer.sharedMaterial = mat;
                }
                SetMarkerLayer(go);
                _ghosts[go] = plot;
            }
        }

        // ---------- layout ----------

        Vector3 BuildingSpherePosition(BuildingId id)
        {
            (float azDeg, float distDeg) = id switch
            {
                BuildingId.CommandCenter    => (0f,   0f),
                BuildingId.GoldMine        => (0f,   innerRingDegrees),
                BuildingId.QuartzExtractor => (120f, innerRingDegrees),
                BuildingId.HeliumRefinery      => (240f, innerRingDegrees),
                BuildingId.PowerPlant       => (45f,  outerRingDegrees),
                BuildingId.ResearchLab      => (315f, outerRingDegrees),
                BuildingId.Warehouse        => (135f, outerRingDegrees),
                BuildingId.Shipyard         => (225f, outerRingDegrees),
                BuildingId.RadarStation     => (0f,   outerRingDegrees),
                _                            => (0f, 0f),
            };
            return SpherePoint(azDeg, distDeg);
        }

        /// <summary>Expansion plots: evenly spread around the plot ring, offset so they
        /// don't sit on top of the outer-ring buildings (at 45/135/225/315°).</summary>
        Vector3 PlotSpherePosition(int plot) =>
            SpherePoint(20f + plot * (360f / PlotCount), plotRingDegrees);

        Vector3 SpherePoint(float azDeg, float distDeg)
        {
            float azRad = azDeg * Mathf.Deg2Rad;
            float distRad = distDeg * Mathf.Deg2Rad;
            float latRad = Mathf.Cos(azRad) * distRad;
            float lonRad = Mathf.Sin(azRad) * distRad;
            float r = _planetRadius + surfaceOffset;
            return new Vector3(
                r * Mathf.Cos(latRad) * Mathf.Sin(lonRad),
                r * Mathf.Sin(latRad),
                -r * Mathf.Cos(latRad) * Mathf.Cos(lonRad));
        }

        // ---------- visual refresh ----------

        void RefreshAll(GameState state)
        {
            foreach (var id in Buildings.All)
            {
                if (!_renderers.TryGetValue(id, out var rend)) continue;
                Color c = ColorForLevel(state.Buildings[id].Level);
                if (id == _selected) c = Color.Lerp(c, Color.white, 0.55f);
                Tint(id, rend, c, id == _selected);
            }

            foreach (var mine in state.ExtraMines)
            {
                if (!_mineRenderers.TryGetValue(mine.Id, out var rend)) continue;
                Color c = ColorForLevel(mine.Level);
                bool selected = _selectedMine == mine.Id;
                if (selected) c = Color.Lerp(c, Color.white, 0.55f);
                if (_mineGlows.TryGetValue(mine.Id, out var glow))
                {
                    c.a = selected ? 0.95f : 0.65f;
                    glow.sharedMaterial.color = c;
                }
                else
                {
                    rend.sharedMaterial.color = c;
                }
            }
        }

        void Tint(BuildingId id, Renderer rend, Color c, bool selected)
        {
            // Art markers keep the illustration untinted — the level reads from
            // the glow halo behind it (and selection brightens the halo).
            if (_glowRenderers.TryGetValue(id, out var glow))
            {
                c.a = selected ? 0.95f : 0.65f;
                glow.sharedMaterial.color = c;
            }
            else
            {
                rend.sharedMaterial.color = c;
            }
        }

        Color ColorForLevel(int level) => level switch
        {
            0     => level0Color,
            1     => level1Color,
            >= 10 => maxLevelColor,
            _     => leveledColor,
        };

        void LateUpdate()
        {
            if (_planet == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            EnsureOverlayCamera(); // camera can spawn after our Start on domain reload
            Vector3 planetCenter = _planet.position;
            Vector3 toCamera = (cam.transform.position - planetCenter).normalized;

            // Billboard the art roots: Unity quads face their -Z side, so forward
            // points AWAY from the camera. Camera-up as the up hint keeps every
            // illustration screen-upright no matter how the planet is rotated.
            foreach (var kv in _artRoots)
            {
                if (kv.Value == null) continue;
                kv.Value.rotation = Quaternion.LookRotation(
                    kv.Value.position - cam.transform.position, cam.transform.up);
            }
            foreach (var kv in _mineRoots)
            {
                // Only art roots billboard (cube fallbacks stay surface-aligned).
                if (kv.Value == null || !_mineGlows.ContainsKey(kv.Key)) continue;
                kv.Value.transform.rotation = Quaternion.LookRotation(
                    kv.Value.transform.position - cam.transform.position, cam.transform.up);
            }

            foreach (var kv in _renderers)
            {
                var rend = kv.Value;
                if (rend == null) continue;
                Vector3 outward = (rend.transform.position - planetCenter).normalized;
                bool visible = Vector3.Dot(outward, toCamera) > HorizonCullDot;
                rend.enabled = visible;
                if (_glowRenderers.TryGetValue(kv.Key, out var glow)) glow.enabled = visible;
            }
            foreach (var kv in _mineRenderers)
            {
                var rend = kv.Value;
                if (rend == null) continue;
                Vector3 outward = (rend.transform.position - planetCenter).normalized;
                bool visible = Vector3.Dot(outward, toCamera) > HorizonCullDot;
                rend.enabled = visible;
                if (_mineGlows.TryGetValue(kv.Key, out var glow)) glow.enabled = visible;
            }
            foreach (var kv in _ghosts)
            {
                var rend = kv.Key != null ? kv.Key.GetComponent<Renderer>() : null;
                if (rend == null) continue;
                Vector3 outward = (kv.Key!.transform.position - planetCenter).normalized;
                rend.enabled = Vector3.Dot(outward, toCamera) > HorizonCullDot;
            }

            // If the panel got replaced/closed without our onClosed firing (e.g. nav
            // switch calls CloseModal directly), drop the stale highlight.
            if ((_selected != null || _selectedMine != null)
                && UIController.Instance != null && !UIController.Instance.HasModal)
                Deselect();
        }

        // ---------- tap handling ----------

        void HandleTapHit(RaycastHit hit)
        {
            foreach (var kv in _markers)
            {
                if (kv.Value == hit.collider.gameObject) { SelectBuilding(kv.Key); return; }
            }
            foreach (var kv in _mineMarkers)
            {
                if (kv.Value == hit.collider.gameObject) { SelectMine(kv.Key); return; }
            }
            if (_ghosts.TryGetValue(hit.collider.gameObject, out int plot))
            {
                if (_ctx != null) BuildMineChooser.Open(_ctx, plot);
            }
        }

        void HandleTapEmpty() => Deselect();

        void SelectBuilding(BuildingId id)
        {
            if (_ctx?.State == null) return;
            _selected = id;
            _selectedMine = null;
            RefreshAll(_ctx.State);
            BuildingPanel.Open(_ctx, id, null, onClosed: Deselect);
        }

        void SelectMine(int mineId)
        {
            if (_ctx?.State == null) return;
            var mine = BuildingSystem.GetMine(_ctx.State, mineId);
            if (mine == null) return;
            _selected = null;
            _selectedMine = mineId;
            RefreshAll(_ctx.State);
            BuildingPanel.Open(_ctx, MineTypes.ToBuildingId(mine.Type), mineId, onClosed: Deselect);
        }

        void Deselect()
        {
            _selected = null;
            _selectedMine = null;
            if (_ctx?.State != null) RefreshAll(_ctx.State);
        }

        // ---------- sim events ----------

        void OnSimEvent(SimEvent e)
        {
            if (e is BuildingCompleted && _ctx?.State != null)
            {
                // A CC level-up can unlock new plots; a mine completion changes tints.
                SyncExtraMines(_ctx.State);
                RefreshAll(_ctx.State);
            }
        }

        // ---------- labels (world-anchored IMGUI text) ----------

        // Cached — constructing GUIStyles every OnGUI frame was pure GC churn.
        GUIStyle? _labelStyle, _labelShadow;

        void OnGUI()
        {
            if (!showLabels || _ctx?.State == null || Camera.main == null) return;
            // IMGUI draws over UI Toolkit no matter what — suppress the labels while
            // a modal is up so building names don't bleed through panels.
            if (UIController.Instance != null && UIController.Instance.HasModal) return;
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = labelFontSize,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                };
                _labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.95f);
                _labelShadow = new GUIStyle(_labelStyle);
                _labelShadow.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
            }
            var style = _labelStyle;
            var shadow = _labelShadow!;
            var cam = Camera.main;

            // Clip bands (screen px) where the header and bottom nav sit, so a
            // marker label scrolled to the screen edge doesn't bleed over the UI.
            float scale = Screen.width / (float)UI.UiTheme.W; // px per UITK point
            float topInset = UI.Widgets.SafeAreaTopPoints();
            float headerPx = (Mathf.Max(topInset + 4f, 34f) + 20f + 50f) * scale;
            const float tickerH = 22f;
            float navPx = (UI.UiTheme.NavH + tickerH) * scale;

            void DrawLabel(Renderer rend, string text)
            {
                if (rend == null || !rend.enabled) return;
                // Anchor the label just under the marker's bottom edge in screen space.
                float halfHeight = rend.transform.lossyScale.y * 0.5f;
                var bottom = rend.transform.position - cam.transform.up * halfHeight;
                var sp = cam.WorldToScreenPoint(bottom);
                if (sp.z < 0) return;
                var rect = new Rect(sp.x - 75f, Screen.height - sp.y + 2f, 150f, 40f);
                // Suppress labels that fall into the header or nav bands.
                if (rect.y < headerPx || rect.yMax > Screen.height - navPx) return;
                // 4-way black outline so names read over any planet palette.
                foreach (var (ox, oy) in new[] { (-1f, 0f), (1f, 0f), (0f, -1f), (0f, 1f) })
                    GUI.Label(new Rect(rect.x + ox, rect.y + oy, rect.width, rect.height), text, shadow);
                GUI.Label(rect, text, style);
            }

            foreach (var kv in _renderers)
            {
                int level = _ctx.State.Buildings[kv.Key].Level;
                DrawLabel(kv.Value, $"{Buildings.Defs[kv.Key].Name}\nL{level}");
            }
            foreach (var mine in _ctx.State.ExtraMines)
            {
                if (!_mineRenderers.TryGetValue(mine.Id, out var rend)) continue;
                _mineLabels.TryGetValue(mine.Id, out var name);
                DrawLabel(rend, $"{name}\nL{mine.Level}");
            }
            foreach (var kv in _ghosts)
            {
                var rend = kv.Key != null ? kv.Key.GetComponent<Renderer>() : null;
                if (rend != null) DrawLabel(rend, "+ BUILD");
            }
        }

        static void Warn(string msg) => Debug.LogWarning($"[GalaxyRoyale] BuildingMarkers: {msg}. Skipping spawn.");
    }
}
