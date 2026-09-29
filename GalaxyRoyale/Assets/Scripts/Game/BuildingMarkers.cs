// Building markers on the home planet: the globe base (2026-09-28). Every
// building stands on a fixed pad (Sim.BaseLayout): the nine core buildings in
// the Command district, extra mines on the Mining Belt's pads in the order they
// were built, and the Frontier's planned buildings as holograms. Each pad shows
// what it is (the approved building states): a level rim under a building,
// BUILD HERE on an open mine pad, the Command Center level a locked pad waits
// for. Tapping a building shows its quick actions (UPGRADE, INFO, BOOST) on
// the base; INFO opens the full panel. BaseGlobe turns the planet; this draws
// what's on it and answers taps.
//
// Art is billboarded on its own layer and redrawn by a depth-clearing overlay
// camera, so an icon never sinks into the planet near the limb; the far side
// hides via a horizon cull. Name chips, timers and marks are UI Toolkit
// (BaseChipLayer), painted under the HUD.
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
        [Header("Art width (fraction of the planet's radius)")]
        [SerializeField] float artSize = 0.2f;
        [SerializeField] float commandCenterArtSize = 0.27f;
        [SerializeField] float mineArtSize = 0.16f;
        [Header("Pad radius (fraction of the planet's radius)")]
        [SerializeField] float padSize = 0.088f;
        [SerializeField] float commandCenterPadSize = 0.112f;
        [SerializeField] float minePadSize = 0.072f;
        [Tooltip("Chips show on pads facing the camera at least this much (dot of normal and view).")]
        [SerializeField] float chipFacing = 0.55f;

        // Markers live on their own layer, drawn by a depth-clearing OVERLAY camera
        // stacked on the base camera, so a rotated planet never clips through an
        // icon. Layer 1 is Unity's built-in TransparentFX.
        const int MarkerLayer = 1;
        /// <summary>Art hides once its pad grazes the horizon.</summary>
        const float HorizonCullDot = 0.08f;
        /// <summary>Holograms: a building that isn't there yet (planned, locked or under construction).</summary>
        static readonly Color HoloTint = new(1f, 0.64f, 0.34f, 0.42f);

        sealed class PadView
        {
            public BasePad Pad = null!;
            public GameObject PadObject = null!;
            public float Size;
            public BaseVisuals.PadLook Look = (BaseVisuals.PadLook)(-1);
            public int Level = -1;
            public bool Selected;
            public int? MineId;
            public Transform? ArtRoot;
            public GameObject? ArtQuad;
            public Renderer? ArtRenderer;
            public Material? ArtMaterial;
            public string ArtName = "";
            public float ArtHeight;
            public bool Holo;
            public GameObject? Scaffold;
        }

        readonly Dictionary<string, PadView> _pads = new();
        readonly Dictionary<GameObject, PadView> _byCollider = new();
        Camera? _overlayCam;
        Camera? _baseCam;
        static BuildingMarkers? s_instance;

        GameContext? _ctx;
        Transform? _planet;
        Transform? _overlay;
        float _radius = 1f;
        int _laneCc = -1;
        BaseChipLayer? _chips;
        BuildingId? _selected;
        int? _selectedMine;
        /// <summary>INFO or BOOST opened a panel for the selection; closing it deselects.</summary>
        bool _panelOpen;
        float _nextRefresh;

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
            if (GetComponent<BaseGlobe>() == null) gameObject.AddComponent<BaseGlobe>();
            // The rest of the globe (2026-09-29): the Wilds to the south, the Spaceport round the back.
            if (GetComponent<WildsView>() == null) gameObject.AddComponent<WildsView>();
            if (GetComponent<SpaceportView>() == null) gameObject.AddComponent<SpaceportView>();

            var planetGO = GameObject.Find("Home Planet");
            if (planetGO == null) { Warn("Home Planet not found in scene"); return; }
            _planet = planetGO.transform;
            var meshTransform = planetGO.transform.Find("Planet Mesh");
            _radius = meshTransform != null ? meshTransform.localScale.x * 0.5f : 1f;
            _baseCam = GameObject.Find("Main Camera")?.GetComponent<Camera>();

            _overlay = BaseVisuals.BuildOverlay(_planet, _radius);
            foreach (var pad in BaseLayout.Pads) SpawnPad(pad);
            RefreshAll(_ctx.State);
            _ctx.Events.Subscribe(OnSimEvent);
            EnsureOverlayCamera();
        }

        /// <summary>Overlay camera that redraws the marker layer on top of the planet
        /// (depth cleared). Child of the base camera, so it tracks every move.</summary>
        void EnsureOverlayCamera()
        {
            if (_overlayCam != null) return;
            var baseCam = _baseCam != null ? _baseCam : Camera.main;
            if (baseCam == null) return;

            baseCam.cullingMask &= ~(1 << MarkerLayer); // base pass skips markers

            var go = new GameObject("Building Marker Overlay Camera");
            go.transform.SetParent(baseCam.transform, worldPositionStays: false);
            _overlayCam = go.AddComponent<Camera>();
            _overlayCam.cullingMask = 1 << MarkerLayer;
            _overlayCam.fieldOfView = baseCam.fieldOfView;
            _overlayCam.nearClipPlane = baseCam.nearClipPlane;
            _overlayCam.farClipPlane = baseCam.farClipPlane;
            // Overlay cameras clear depth (read-only in URP 17), so markers always
            // draw on top of the sphere.
            var overlayData = _overlayCam.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
            baseCam.GetUniversalAdditionalCameraData().cameraStack.Add(_overlayCam);
        }

        static void SetMarkerLayer(GameObject go)
        {
            go.layer = MarkerLayer;
            foreach (Transform child in go.transform) SetMarkerLayer(child.gameObject);
        }

        /// <summary>UI actions that add or remove mines (chooser, cancel) call this to resync.</summary>
        public static void RequestExtraMineSync()
        {
            var inst = s_instance;
            if (inst?._ctx?.State == null || inst._planet == null) return;
            inst.RefreshAll(inst._ctx.State);
        }

        // ---------- pads ----------

        float PadRadius(BasePad pad) => _radius * (pad.Kind == PadKind.Mine ? minePadSize
            : pad.Kind == PadKind.Building && pad.Building == BuildingId.CommandCenter ? commandCenterPadSize : padSize);

        float ArtWidth(BasePad pad) => _radius * (pad.Kind == PadKind.Mine ? mineArtSize
            : pad.Kind == PadKind.Building && pad.Building == BuildingId.CommandCenter ? commandCenterArtSize : artSize);

        void SpawnPad(BasePad pad)
        {
            var view = new PadView { Pad = pad, Size = PadRadius(pad) };
            view.PadObject = BaseVisuals.SpawnPad(_planet!, pad, _radius, view.Size);
            _pads[pad.Key] = view;
            _byCollider[view.PadObject] = view;
        }

        void RefreshAll(GameState state)
        {
            if (_planet == null) return;
            int cc = state.Buildings[BuildingId.CommandCenter].Level;
            if (cc != _laneCc && _overlay != null)
            {
                _laneCc = cc;
                BaseVisuals.RefreshLanes(_overlay, _radius, cc);
            }
            foreach (var view in _pads.Values)
            {
                var pad = view.Pad;
                BaseVisuals.PadLook look;
                int level = 0;
                string? art = null;
                bool holo = false;
                int? mineId = null;
                switch (pad.Kind)
                {
                    case PadKind.Building:
                        level = state.Buildings[pad.Building].Level;
                        look = level > 0 ? BaseVisuals.PadLook.Built
                            : BaseLayout.Unlocked(state, pad) ? BaseVisuals.PadLook.Open : BaseVisuals.PadLook.Locked;
                        art = pad.Building.ToString();
                        holo = level == 0;
                        break;
                    case PadKind.Mine:
                    {
                        var mine = BaseLayout.MineOn(state, pad);
                        if (mine != null)
                        {
                            level = mine.Level;
                            mineId = mine.Id;
                            look = level > 0 ? BaseVisuals.PadLook.Built : BaseVisuals.PadLook.Open;
                            art = pad.Building.ToString();
                            holo = level == 0;
                        }
                        else if (BaseLayout.Unlocked(state, pad))
                        {
                            look = BaseVisuals.PadLook.Open;
                        }
                        else
                        {
                            look = BaseVisuals.PadLook.Locked;
                            art = pad.Building.ToString();
                            holo = true;
                        }
                        break;
                    }
                    case PadKind.Planned:
                    {
                        bool online = BaseLayout.PlannedOnline(state, pad.Planned);
                        look = online ? BaseVisuals.PadLook.Built : BaseVisuals.PadLook.Planned;
                        level = online ? 1 : 0;
                        art = pad.Planned.ToString();
                        holo = !online;
                        break;
                    }
                    default:
                        look = BaseVisuals.PadLook.Reserved;
                        break;
                }
                bool selected = (pad.Kind == PadKind.Building && _selected == pad.Building)
                                || (mineId != null && _selectedMine == mineId);
                view.MineId = mineId;
                if (look != view.Look || level != view.Level || selected != view.Selected)
                {
                    view.Look = look;
                    view.Level = level;
                    view.Selected = selected;
                    BaseVisuals.PaintPad(view.PadObject, view.Size, look, level, selected);
                }
                EnsureArt(view, art, holo);
            }
        }

        void EnsureArt(PadView view, string? artName, bool holo)
        {
            if (artName == null)
            {
                if (view.ArtRoot != null) RemoveArt(view);
                return;
            }
            if (view.ArtName != artName || view.ArtRoot == null)
            {
                if (view.ArtRoot != null) RemoveArt(view);
                var tex = UnityEngine.Resources.Load<Texture2D>($"Buildings/{artName}");
                if (tex == null) return;
                SpawnArt(view, tex);
                view.ArtName = artName;
                view.Holo = !holo; // force the tint below
            }
            if (view.Holo != holo && view.ArtMaterial != null)
            {
                view.Holo = holo;
                view.ArtMaterial.color = holo ? HoloTint : Color.white;
            }
        }

        void RemoveArt(PadView view)
        {
            if (view.ArtQuad != null) _byCollider.Remove(view.ArtQuad);
            if (view.ArtRoot != null) Destroy(view.ArtRoot.gameObject);
            view.ArtRoot = null;
            view.ArtQuad = null;
            view.ArtRenderer = null;
            view.ArtMaterial = null;
            view.Scaffold = null;
            view.ArtName = "";
        }

        /// <summary>A billboarded art quad standing on the pad (its plate over the pad's
        /// centre) with a tap collider, on the overlay layer.</summary>
        void SpawnArt(PadView view, Texture2D art)
        {
            var root = new GameObject($"Art {view.Pad.Key}");
            root.transform.SetParent(_planet, worldPositionStays: false);
            root.transform.localPosition = view.PadObject.transform.localPosition;

            float width = ArtWidth(view.Pad);
            float height = width * art.height / (float)art.width;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Art";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, worldPositionStays: false);
            // The art's plate centre sits about a quarter up from its bottom edge.
            quad.transform.localPosition = new Vector3(0f, height * 0.24f, 0f);
            quad.transform.localScale = new Vector3(width, height, 1f);
            var box = quad.AddComponent<BoxCollider>();
            box.size = new Vector3(1f, 1f, 0.05f);
            var renderer = quad.GetComponent<Renderer>();
            var mat = MapVisuals.UnlitTransparent(art);
            renderer.sharedMaterial = mat;
            SetMarkerLayer(root);

            view.ArtRoot = root.transform;
            view.ArtQuad = quad;
            view.ArtRenderer = renderer;
            view.ArtMaterial = mat;
            view.ArtHeight = height;
            _byCollider[quad] = view;
        }

        // ---------- scaffolding (upgrading) ----------

        static Texture2D? s_scaffoldTex;

        static Texture2D ScaffoldTexture()
        {
            if (s_scaffoldTex != null) return s_scaffoldTex;
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            var c = new Color(1f, 0.6f, 0.24f, 0.95f);
            void Dot(int x, int y)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx >= 0 && yy >= 0 && xx < n && yy < n) px[yy * n + xx] = c;
                    }
            }
            void LineTo(int x0, int y0, int x1, int y1)
            {
                int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
                for (int i = 0; i <= steps; i++) Dot(x0 + (x1 - x0) * i / Mathf.Max(1, steps), y0 + (y1 - y0) * i / Mathf.Max(1, steps));
            }
            int[] xs = { 6, n / 2, n - 7 };
            int[] ys = { 6, n / 2, n - 12 };
            foreach (int x in xs) LineTo(x, 2, x, n - 12);
            foreach (int y in ys) LineTo(4, y, n - 5, y);
            LineTo(6, n / 2, n / 2, 6);
            LineTo(n / 2, n / 2, n - 7, 6);
            tex.SetPixels(px);
            tex.Apply();
            return s_scaffoldTex = tex;
        }

        void SetScaffold(PadView view, bool on)
        {
            if (!on)
            {
                if (view.Scaffold != null) { Destroy(view.Scaffold); view.Scaffold = null; }
                return;
            }
            if (view.Scaffold != null || view.ArtQuad == null || view.ArtRoot == null) return;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Scaffold";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(view.ArtRoot, worldPositionStays: false);
            var s = view.ArtQuad.transform.localScale;
            quad.transform.localPosition = view.ArtQuad.transform.localPosition + new Vector3(0f, -s.y * 0.08f, -0.01f);
            quad.transform.localScale = new Vector3(s.x * 0.72f, s.y * 0.72f, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = MapVisuals.UnlitTransparent(ScaffoldTexture());
            SetMarkerLayer(quad);
            view.Scaffold = quad;
        }

        // ---------- per frame ----------

        void LateUpdate()
        {
            if (_ctx?.State != null && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 1f; // Command Center levels, mines placed elsewhere
                RefreshAll(_ctx.State);
            }
            UpdateMarkers();
            PlaceChips();
        }

        void UpdateMarkers()
        {
            if (_planet == null) return;
            var cam = _baseCam != null ? _baseCam : Camera.main;
            if (cam == null) return;
            EnsureOverlayCamera();
            Vector3 centre = _planet.position;
            Vector3 toCamera = (cam.transform.position - centre).normalized;
            foreach (var view in _pads.Values)
            {
                if (view.ArtRoot == null) continue;
                // Billboard: quads face -Z, so forward points away from the camera.
                view.ArtRoot.rotation = Quaternion.LookRotation(view.ArtRoot.position - cam.transform.position, cam.transform.up);
                Vector3 outward = (view.ArtRoot.position - centre).normalized;
                bool visible = Vector3.Dot(outward, toCamera) > HorizonCullDot;
                if (view.ArtRenderer != null) view.ArtRenderer.enabled = visible;
                if (view.Scaffold != null) view.Scaffold.GetComponent<Renderer>().enabled = visible;
            }

            // A panel we opened that closed without its onClosed (a nav switch) drops the selection.
            if (_panelOpen && UIController.Instance != null && !UIController.Instance.HasModal)
            {
                _panelOpen = false;
                Deselect();
            }
        }

        void PlaceChips()
        {
            if (_ctx?.State == null || _planet == null) return;
            _chips ??= UIController.Instance?.CreateBaseChipLayer();
            if (_chips == null) return;
            var cam = _baseCam;
            var globe = BaseGlobe.Instance;
            float zoom = globe != null ? globe.Zoom : 0f;
            if (cam == null || !cam.isActiveAndEnabled || UIController.Instance?.HasModal == true)
            {
                _chips.Clear();
                _chips.HideRing();
                return;
            }
            var state = _ctx.State;
            if (zoom > 0.45f)
            {
                _chips.HideRing();
                PlaceDistrictLabels(cam, state);
                return;
            }
            bool ringShown = false;
            float alpha = Mathf.Clamp01(1f - zoom * 2.2f);
            Vector3 centre = _planet.position;
            Vector3 toCamera = (cam.transform.position - centre).normalized;
            _chips.Begin();
            foreach (var view in _pads.Values)
            {
                var pad = view.Pad;
                var world = view.PadObject.transform.position;
                float facing = Vector3.Dot((world - centre).normalized, toCamera);
                if (facing < chipFacing) continue;
                var at = _chips.ToPanel(cam.WorldToScreenPoint(world));
                if (at is not { } p) continue;
                float fade = alpha * Mathf.Clamp01((facing - chipFacing) / 0.15f);
                var below = new Vector2(p.x, p.y + 7f);
                Vector2? top = null;
                if (view.ArtQuad != null)
                {
                    var t = view.ArtQuad.transform;
                    var topWorld = t.position + cam.transform.up * (t.lossyScale.y * 0.5f);
                    top = _chips.ToPanel(cam.WorldToScreenPoint(topWorld));
                }

                var order = FindOrder(state, pad, view.MineId);
                SetScaffold(view, order != null);
                if (IsSelected(view) && fade > 0.5f)
                {
                    ringShown = true;
                    PlaceRing(cam, state, view, p, order);
                }
                if (order != null)
                {
                    bool queued = order.EndsAtTick <= 0; // waiting for a free build slot
                    int left = queued ? 0 : order.EndsAtTick - state.Tick;
                    int total = Mathf.Max(1, BuildingSystem.GetBuildTime(state, order.Building, order.ToLevel));
                    float pct = queued ? 0f : Mathf.Clamp01(1f - left / (float)total);
                    var ringAt = top is { } tp ? new Vector2(tp.x, tp.y + 18f) : new Vector2(p.x, p.y - 40f);
                    _chips.Place(pad.Key, BaseChipLayer.Kind.Timer, below, ringAt,
                        queued ? "QUEUED" : UiTheme.FmtDuration(Mathf.Max(0, left)), progress: pct, alpha: fade);
                    continue;
                }

                switch (pad.Kind)
                {
                    case PadKind.Building when view.Level == 0 && !BaseLayout.Unlocked(state, pad):
                        _chips.Place(pad.Key, BaseChipLayer.Kind.Planned, below, null, pad.Name, $"CC {pad.UnlockCc}", alpha: fade);
                        break;
                    case PadKind.Building when view.Level == 0 && Buildings.IsFrontier(pad.Building):
                        _chips.Place(pad.Key, BaseChipLayer.Kind.Online, below, null, pad.Name, "BUILD", alpha: fade);
                        break;
                    case PadKind.Building:
                    case PadKind.Mine when view.MineId != null:
                        _chips.Place(pad.Key, BaseChipLayer.Kind.Name, below, null, ChipName(pad),
                            view.Level.ToString(), BaseVisuals.LevelRim(view.Level), alpha: fade);
                        break;
                    case PadKind.Mine when view.Look == BaseVisuals.PadLook.Open:
                        _chips.Place(pad.Key, BaseChipLayer.Kind.Open, new Vector2(p.x, p.y + 12f), new Vector2(p.x, p.y - 2f),
                            "BUILD HERE", alpha: fade);
                        break;
                    case PadKind.Mine:
                        _chips.Place(pad.Key, BaseChipLayer.Kind.Locked, below, new Vector2(p.x, p.y - (top != null ? 26f : 2f)),
                            $"CC {pad.UnlockCc}", alpha: fade);
                        break;
                    case PadKind.Planned when view.Look == BaseVisuals.PadLook.Built:
                        _chips.Place(pad.Key, BaseChipLayer.Kind.Online, below, null, pad.Name, "MARKET", alpha: fade);
                        break;
                    case PadKind.Planned:
                        _chips.Place(pad.Key, BaseChipLayer.Kind.Planned, below, null, pad.Name,
                            pad.Planned == PlannedBuilding.ExchangeTerminal ? $"CC {pad.UnlockCc}" : "SOON", alpha: fade);
                        break;
                    default:
                        _chips.Place(pad.Key, BaseChipLayer.Kind.Reserved, new Vector2(p.x, p.y + 12f), new Vector2(p.x, p.y - 2f),
                            "RESERVED", alpha: fade);
                        break;
                }

                // Ready to collect: clan supplies at the Warehouse, salvage at the Salvage Yard.
                if (pad.Kind == PadKind.Building && pad.Building == BuildingId.Warehouse
                    && state.ClanSupplyRuns > 0 && top is { } wt)
                    _chips.Bubble("supplies", new Vector2(wt.x, wt.y - 2f), Icon.Pact,
                        $"+{UiTheme.FmtAmount(state.ClanSupplyPending.Total)}", CollectSupplies);
                if (pad.Kind == PadKind.Building && pad.Building == BuildingId.SalvageYard && view.Level > 0
                    && state.SalvageStored.Total >= 1000 && top is { } st)
                    _chips.Bubble("salvage", new Vector2(st.x, st.y - 2f), Icon.Crate,
                        $"+{UiTheme.FmtAmount(state.SalvageStored.Total)}", CollectSalvage);
            }
            _chips.End();
            if (!ringShown) _chips.HideRing();
        }

        // ---------- quick actions ----------

        bool IsSelected(PadView view) =>
            (view.Pad.Kind == PadKind.Building && _selected == view.Pad.Building)
            || (view.MineId != null && _selectedMine == view.MineId);

        /// <summary>UPGRADE, INFO and BOOST around the selected building, with what the
        /// upgrade costs (or how long the one running has left).</summary>
        void PlaceRing(Camera cam, GameState state, PadView view, Vector2 padAt, BuildOrder? order)
        {
            var pad = view.Pad;
            int? mineId = pad.Kind == PadKind.Mine ? view.MineId : null;
            int level = view.Level;
            var def = Buildings.Defs[pad.Building];
            var check = mineId is int m ? BuildingSystem.CheckUpgradeMine(state, m) : BuildingSystem.CheckUpgrade(state, pad.Building);
            var cost = new BaseChipLayer.RingCost();
            if (order != null)
                cost.Plain = order.EndsAtTick <= 0 ? $"LV {order.ToLevel} QUEUED"
                    : $"LV {order.ToLevel} IN {UiTheme.FmtDuration(Mathf.Max(0, order.EndsAtTick - state.Tick))}";
            else if (level >= def.MaxLevel) cost.Plain = "MAX LEVEL";
            else
            {
                var c = BuildingSystem.GetUpgradeCost(pad.Building, level + 1);
                cost.Level = level + 1;
                cost.Gold = c.Gold;
                cost.Quartz = c.Quartz;
                cost.Helium = c.Helium;
                cost.GoldShort = state.Resources.Gold < c.Gold;
                cost.QuartzShort = state.Resources.Quartz < c.Quartz;
                cost.HeliumShort = state.Resources.Helium < c.Helium;
                cost.Seconds = BuildingSystem.GetBuildTime(state, pad.Building, level + 1);
            }
            Vector2 artTop = new(padAt.x, padAt.y - 60f), artCenter = new(padAt.x, padAt.y - 30f);
            if (view.ArtQuad != null)
            {
                var t = view.ArtQuad.transform;
                var up = cam.transform.up * (t.lossyScale.y * 0.5f);
                if (_chips!.ToPanel(cam.WorldToScreenPoint(t.position + up)) is { } a) artTop = a;
                if (_chips.ToPanel(cam.WorldToScreenPoint(t.position)) is { } b) artCenter = b;
            }
            _chips!.Ring(artTop, new Vector2(padAt.x, padAt.y + 30f), artCenter,
                level == 0 ? "BUILD" : "UPGRADE", check.Ok, order != null, cost,
                () => RingUpgrade(pad, mineId),
                () => RingInfo(pad, mineId),
                () => RingBoost(pad, mineId));
        }

        string Title(BasePad pad, int? mineId)
        {
            if (mineId is not int m || _ctx?.State == null) return pad.Name;
            var mine = BuildingSystem.GetMine(_ctx.State, m);
            return mine == null ? pad.Name : $"{Buildings.Defs[pad.Building].Name} #{BaseLayout.MineTier(_ctx.State, mine) + 2}";
        }

        void RingUpgrade(BasePad pad, int? mineId)
        {
            var state = _ctx?.State;
            if (state == null) return;
            var res = mineId is int m ? BuildingSystem.StartUpgradeMine(state, m) : BuildingSystem.StartUpgrade(state, pad.Building);
            var ui = UIController.Instance;
            if (res.Ok)
            {
                GameAudio.Feedback(Sfx.Confirm, Haptic.Light);
                ui?.Toast($"{Title(pad, mineId)} upgrade started");
                RefreshAll(state);
            }
            else
            {
                GameAudio.Feedback(Sfx.Error, Haptic.Error);
                ui?.Toast(res.Reason ?? "Can't upgrade right now", Icon.Warning, UiTheme.Bad);
            }
        }

        void RingInfo(BasePad pad, int? mineId)
        {
            if (_ctx == null) return;
            _panelOpen = true;
            BuildingPanel.Open(_ctx, pad.Building, mineId, onClosed: () => { _panelOpen = false; Deselect(); });
        }

        void RingBoost(BasePad pad, int? mineId)
        {
            var state = _ctx?.State;
            if (_ctx == null || state == null) return;
            if (BuildingSystem.FindOrderIndex(state, pad.Building, mineId) < 0)
            {
                UIController.Instance?.Toast("Nothing to speed up here: start an upgrade first", Icon.Bolt, UiTheme.Dim);
                return;
            }
            _panelOpen = true;
            var ctx = _ctx;
            SpeedUpPanel.Open(ctx, Title(pad, mineId), "finish-build",
                remainingSec: () =>
                {
                    int i = BuildingSystem.FindOrderIndex(ctx.State!, pad.Building, mineId);
                    if (i < 0) return 0;
                    var o = ctx.State!.BuildQueue[i];
                    return o.EndsAtTick > 0 ? Mathf.Max(0, o.EndsAtTick - ctx.State!.Tick) : 0;
                },
                apply: cut =>
                {
                    int i = BuildingSystem.FindOrderIndex(ctx.State!, pad.Building, mineId);
                    if (i >= 0) BuildingSystem.SpeedUpBuildOrder(ctx.State!, i, cut == long.MaxValue ? int.MaxValue : (int)cut);
                    RefreshAll(ctx.State!);
                },
                onBack: () => _panelOpen = false); // back on the base, still selected
        }

        /// <summary>From orbit: each district's name and how much of it is built (the
        /// Spaceport: ships docked; the Wilds: sectors charted).</summary>
        void PlaceDistrictLabels(Camera cam, GameState state)
        {
            _chips!.Begin();
            Vector3 centre = _planet!.position;
            Vector3 toCamera = (cam.transform.position - centre).normalized;
            void Label(string key, double lat, double lon, string name, string count)
            {
                var world = _planet.TransformPoint(BaseVisuals.Point(lat, lon, _radius * 1.01f));
                if (Vector3.Dot((world - centre).normalized, toCamera) < 0.05f) return;
                if (_chips.ToPanel(cam.WorldToScreenPoint(world)) is not { } p) return;
                _chips.Place(key, BaseChipLayer.Kind.Online, p, null, name, count);
            }
            foreach (var d in BaseLayout.NorthBand)
            {
                string count;
                if (d == BaseDistrict.Spaceport)
                {
                    int docked = 0;
                    foreach (var n in state.Ships.Values) docked += Mathf.Max(0, n);
                    count = $"{docked:N0} ships";
                }
                else
                {
                    var (built, total) = BaseLayout.Count(state, d);
                    count = $"{built}/{total}";
                }
                Label($"district-{d}", 62, BaseLayout.DistrictLon(d), BaseLayout.DistrictName(d), count);
            }
            // The Wilds' label rides round with the view, over the southern cap.
            var globe = BaseGlobe.Instance;
            Label("district-Wilds", -58, globe != null ? globe.Yaw : 0f, BaseLayout.DistrictName(BaseDistrict.Wilds),
                $"{WildsSystem.ChartedCount(state)}/{WildsLayout.Total}");
            _chips.End();
        }

        static string ChipName(BasePad pad) => pad.Kind == PadKind.Mine
            ? $"{pad.Mine switch { MineType.GoldMine => "Gold", MineType.QuartzExtractor => "Quartz", _ => "Helium" }} #{pad.Tier + 2}"
            : pad.Name;

        /// <summary>The build order for a pad's building (running or queued), if any.</summary>
        static BuildOrder? FindOrder(GameState state, BasePad pad, int? mineId)
        {
            if (pad.Kind is not (PadKind.Building or PadKind.Mine)) return null;
            if (pad.Kind == PadKind.Mine && mineId == null) return null;
            int i = BuildingSystem.FindOrderIndex(state, pad.Building, pad.Kind == PadKind.Mine ? mineId : null);
            return i >= 0 ? state.BuildQueue[i] : null;
        }

        void CollectSalvage()
        {
            var state = _ctx?.State;
            if (state == null) return;
            if (SalvageSystem.Collect(state, out var got).Ok)
            {
                GameAudio.Feedback(Sfx.Coins, Haptic.Success);
                UIController.Instance?.Toast($"Salvage collected: +{UiTheme.FmtAmount(got.Total)}", Icon.Crate, UiTheme.Good);
            }
        }

        void CollectSupplies()
        {
            var state = _ctx?.State;
            if (state == null) return;
            long total = state.ClanSupplyPending.Total;
            if (ClanSystem.CollectSupplies(state).Ok)
            {
                GameAudio.Feedback(Sfx.Coins, Haptic.Success);
                UIController.Instance?.Toast($"Clan supplies collected: +{UiTheme.FmtAmount(total)}", Icon.Pact, UiTheme.Good);
            }
        }

        // ---------- taps ----------

        void HandleTapHit(RaycastHit hit)
        {
            if (!_byCollider.TryGetValue(hit.collider.gameObject, out var view) || _ctx?.State == null) return;
            var ui = UIController.Instance;
            var globe = BaseGlobe.Instance;
            var pad = view.Pad;
            if (globe != null && globe.Zoom > 0.45f)
            {
                globe.FlyTo(pad.District); // from orbit, a tap lands on that district
                return;
            }
            var state = _ctx.State;
            switch (pad.Kind)
            {
                case PadKind.Building when state.Buildings[pad.Building].Level == 0 && !BaseLayout.Unlocked(state, pad):
                    ui?.Toast($"{pad.Name} unlocks at Command Center {pad.UnlockCc}", Icon.Lock, UiTheme.Dim);
                    break;
                case PadKind.Building:
                    SelectBuilding(pad.Building);
                    break;
                case PadKind.Mine:
                {
                    var mine = BaseLayout.MineOn(state, pad);
                    if (mine != null) SelectMine(mine.Id);
                    else if (BaseLayout.Unlocked(state, pad)) BuildMineChooser.Open(_ctx, pad.Tier, pad.Mine);
                    else ui?.Toast($"This pad opens at Command Center {pad.UnlockCc}", Icon.Lock, UiTheme.Dim);
                    break;
                }
                case PadKind.Planned:
                    if (BaseLayout.PlannedOnline(state, pad.Planned)) MarketPanel.Open(_ctx);
                    else if (pad.Planned == PlannedBuilding.ExchangeTerminal)
                        ui?.Toast($"The Exchange Terminal opens at Command Center {pad.UnlockCc} — the Market's new home", Icon.Lock, UiTheme.Dim);
                    else ui?.Toast($"{pad.Name}: coming in a future update", Icon.Lock, UiTheme.Magenta);
                    break;
                default:
                    ui?.Toast("This pad is kept free for a building in a later update", Icon.Question, UiTheme.Dim);
                    break;
            }
        }

        void HandleTapEmpty() => Deselect();

        /// <summary>First tap selects (the quick actions show); a second tap opens INFO.</summary>
        void SelectBuilding(BuildingId id)
        {
            if (_ctx?.State == null) return;
            if (_selected == id) { RingInfo(BaseLayout.BuildingPad(id), null); return; }
            _selected = id;
            _selectedMine = null;
            GameAudio.Play(Sfx.Toggle);
            RefreshAll(_ctx.State);
        }

        void SelectMine(int mineId)
        {
            var state = _ctx?.State;
            if (state == null) return;
            var mine = BuildingSystem.GetMine(state, mineId);
            if (mine == null) return;
            if (_selectedMine == mineId)
            {
                RingInfo(BaseLayout.MinePad(mine.Type, BaseLayout.MineTier(state, mine)), mineId);
                return;
            }
            _selected = null;
            _selectedMine = mineId;
            GameAudio.Play(Sfx.Toggle);
            RefreshAll(state);
        }

        void Deselect()
        {
            if (_selected == null && _selectedMine == null) return;
            _selected = null;
            _selectedMine = null;
            if (_ctx?.State != null) RefreshAll(_ctx.State);
        }

        void OnSimEvent(SimEvent e)
        {
            if (e is BuildingCompleted && _ctx?.State != null) RefreshAll(_ctx.State);
        }

        static void Warn(string msg) => Debug.LogWarning($"[GalaxyRoyale] BuildingMarkers: {msg}. Skipping spawn.");
    }
}
