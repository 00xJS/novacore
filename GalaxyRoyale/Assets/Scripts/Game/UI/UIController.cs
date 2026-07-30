// The always-on UI overlay — Unity port of v1's UIScene + ResourceBar +
// BottomNav + ToastManager, rebuilt in UI Toolkit (pure C#, no UXML/USS).
//
// Runtime-only setup: creates its own UIDocument + PanelSettings (theme loaded
// from Resources/GalaxyRoyaleTheme.tss), so the scene needs no UI wiring at all.
// Layout is v1's portrait 390×844 canvas, scaled by width.
//
// Three bands of header (island row / HQ coords / resource strip), bottom nav
// (MAP FLEET BASE ITEMS MAIL), queues FAB with idle badge, stacked toasts, and
// a single-modal host with per-frame Refresh — same architecture as v1.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;
using Position = UnityEngine.UIElements.Position;

namespace GalaxyRoyale.Game.UI
{
    public enum ViewId { Base, Map, Fleet }

    [AddComponentMenu("GalaxyRoyale/UI Controller")]
    [RequireComponent(typeof(GameContext))]
    public sealed class UIController : MonoBehaviour
    {
        public static UIController? Instance { get; private set; }

        GameContext _ctx = null!;
        UIDocument? _doc;
        VisualElement _root = null!;

        // Header
        Label _nameLabel = null!, _mightLabel = null!, _coordsLabel = null!, _shieldLabel = null!;
        string _shieldCache = "";
        // Header refresh gate: ≤4 Hz instead of every frame (see Update).
        int _headerTick = -1;
        float _nextHeaderRefresh;
        readonly Label[] _resAmounts = new Label[3];
        Label _energyLabel = null!, _dmLabel = null!;
        readonly string[] _cache = { "", "", "", "" };
        string _dmCache = "", _mightCache = "", _nameCache = "", _coordsCache = "";
        VisualElement _islandRow = null!;
        VisualElement _headerAvatar = null!;
        int _avatarSeedCache = -1;
        string? _hqOverride;

        /// <summary>Replace the header's HQ line (the map shows viewing coordinates there).</summary>
        public void SetHqOverride(string? text) => _hqOverride = text;

        // Nav + FAB
        readonly Dictionary<ViewId, Button> _navButtons = new();
        Button _mailButton = null!;
        bool _mailBadge;
        Button _queuesFab = null!;
        VisualElement _queuesBadge = null!;
        Label _queuesBadgeLabel = null!;
        int _queuesCountCache = -1;
        float _queuesPingUntil;

        // Toasts
        VisualElement _toastLayer = null!;
        readonly List<VisualElement> _toasts = new();

        // Modal
        VisualElement _modalLayer = null!;
        VisualElement? _modal;
        Action? _modalRefresh;

        // Non-modal node callout (map stays pannable underneath) + map-only search FAB.
        VisualElement _calloutLayer = null!;
        VisualElement? _callout;
        GalaxyRoyale.Sim.Map.MapNode? _calloutNode;
        Button _searchFab = null!;

        // Galaxy-news ticker (the global chat's slot reborn, user spec): a thin
        // strip above the nav showing the latest battle headline; tap → the
        // full GALAXY NEWS wire. Plus the MORE menu (DAILY / RANK).
        const float TickerH = 22f;
        VisualElement _ticker = null!;
        Label _tickerLabel = null!;
        int _tickerNewsCount = -1;
        float _nextDailyGlowPoll;
        Button _moreFab = null!;
        VisualElement _moreMenu = null!;
        bool _moreOpen;
        Button _favoritesFab = null!;
        Button _spinToggle = null!; // base-view auto-rotate (default off)

        public ViewId View { get; private set; } = ViewId.Base;

        void Awake()
        {
            _ctx = GetComponent<GameContext>();
            Instance = this;
        }

        void Start() => EnsureBuilt();

        void EnsureBuilt()
        {
            if (_doc != null) return;

            // Must be the editor-created asset, not CreateInstance: runtime-created
            // PanelSettings have no ICU data asset, so UI Toolkit text measurement
            // NREs in device builds (fine in-editor). Regenerate via
            // "GalaxyRoyale → Create Panel Settings Asset" if missing.
            var panelSettings = UnityEngine.Resources.Load<PanelSettings>("GalaxyRoyalePanelSettings");
            if (panelSettings == null)
            {
                Debug.LogWarning("GalaxyRoyalePanelSettings.asset missing from Resources — " +
                    "falling back to runtime PanelSettings (text will break in device builds).");
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            }
            panelSettings.themeStyleSheet = UnityEngine.Resources.Load<ThemeStyleSheet>("GalaxyRoyaleTheme");
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(UiTheme.W, UiTheme.H);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0f; // match width — v1's fixed-390 layout scales by width

            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = panelSettings;

            _root = _doc.rootVisualElement;
            _root.style.position = Position.Absolute;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.style.top = 0;
            _root.style.bottom = 0;
            _root.pickingMode = PickingMode.Ignore;

            BuildHeader();
            BuildBottomNav();
            BuildNewsTicker();
            BuildQueuesFab();
            BuildMoreMenu();

            // Map-only FABs stack on the LEFT above the queues button (user layout):
            // queues (always) → search (map) → favorites (map). MORE lives right.
            Button LeftFab(string label, Action onTap, float bottomOffset, int fontSize = 20)
            {
                var fab = Widgets.TextButton(label, onTap, fontSize);
                fab.style.position = Position.Absolute;
                fab.style.left = 12;
                fab.style.bottom = UiTheme.NavH + TickerH + 16 + bottomOffset;
                fab.style.width = 48;
                fab.style.height = 48;
                fab.style.borderTopLeftRadius = 24;
                fab.style.borderTopRightRadius = 24;
                fab.style.borderBottomLeftRadius = 24;
                fab.style.borderBottomRightRadius = 24;
                Widgets.SetBorder(fab, UiTheme.Accent, 2f);
                fab.style.display = DisplayStyle.None;
                _root.Add(fab);
                return fab;
            }
            // "⌕" rendered as tofu; use a text label instead (user feedback).
            _searchFab = LeftFab("FIND", () => SearchPanel.Open(_ctx), 56f, 12);
            _favoritesFab = LeftFab("★", () => FavoritesPanel.Open(_ctx), 112f);

            // Auto-rotate toggle — top right of the BASE view, default off
            // (replaces the retired smoke-test HUD's only useful button). Plain
            // word label: the runtime font tofu-boxes the ⟳ glyph we used before.
            _spinToggle = Widgets.TextButton("SPIN", ToggleSpin, 12);
            _spinToggle.style.position = Position.Absolute;
            _spinToggle.style.right = 12;
            _spinToggle.style.top = 112; // just under the header
            _spinToggle.style.width = 42;
            _spinToggle.style.height = 42;
            _spinToggle.style.borderTopLeftRadius = 21;
            _spinToggle.style.borderTopRightRadius = 21;
            _spinToggle.style.borderBottomLeftRadius = 21;
            _spinToggle.style.borderBottomRightRadius = 21;
            Widgets.SetBorder(_spinToggle, UiTheme.Stroke, 2f);
            _root.Add(_spinToggle);

            _calloutLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _calloutLayer.style.position = Position.Absolute;
            _calloutLayer.style.left = 10;
            _calloutLayer.style.right = 10;
            _calloutLayer.style.bottom = UiTheme.NavH + TickerH + 10;
            _root.Add(_calloutLayer);

            _toastLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _toastLayer.style.position = Position.Absolute;
            _toastLayer.style.left = 0;
            _toastLayer.style.right = 0;
            _toastLayer.style.bottom = UiTheme.NavH + TickerH + 40;
            _toastLayer.style.alignItems = Align.Center;
            _root.Add(_toastLayer);

            _modalLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _modalLayer.style.position = Position.Absolute;
            _modalLayer.style.left = 0;
            _modalLayer.style.right = 0;
            _modalLayer.style.top = 0;
            _modalLayer.style.bottom = 0;
            _root.Add(_modalLayer);

            _ctx.Events!.Subscribe(OnSimEvent);
        }

        /// <summary>Fresh-commander orientation — LocalBootstrap fires this after
        /// NEW GAME (not at UI build time, which would toast over the title screen).</summary>
        public void ShowRookieHints()
        {
            EnsureBuilt();
            _root.schedule.Execute(() =>
                Toast("Upgrade the Command Center to raise all building caps")).ExecuteLater(2500);
            _root.schedule.Execute(() =>
                Toast("Build a Shipyard, then send fleets from the MAP to gather")).ExecuteLater(6500);
        }

        // ---------- header (v1 ResourceBar) ----------

        void BuildHeader()
        {
            var header = new VisualElement();
            header.style.position = Position.Absolute;
            header.style.left = 0;
            header.style.right = 0;
            header.style.top = 0;
            header.style.backgroundColor = UiTheme.Panel;

            // Row 1 — island row: commander pill LEFT, might RIGHT.
            _islandRow = Widgets.HBox(Justify.SpaceBetween);
            _islandRow.style.height = 34;
            _islandRow.style.paddingLeft = 4;
            _islandRow.style.paddingRight = 14;

            var profilePill = Widgets.HBox();
            profilePill.style.backgroundColor = new Color(UiTheme.PanelLight.r, UiTheme.PanelLight.g, UiTheme.PanelLight.b, 0.6f);
            Widgets.SetBorder(profilePill, UiTheme.Stroke, 1f);
            profilePill.style.borderTopLeftRadius = 15;
            profilePill.style.borderTopRightRadius = 15;
            profilePill.style.borderBottomLeftRadius = 15;
            profilePill.style.borderBottomRightRadius = 15;
            profilePill.style.paddingLeft = 4;
            profilePill.style.paddingRight = 8;
            profilePill.style.height = 30;
            profilePill.style.minWidth = 128;
            profilePill.RegisterCallback<PointerUpEvent>(_ => OpenProfile());

            var profile = _ctx.State!.Profile;
            _headerAvatar = Portraits.Avatar(profile.AvatarSeed, profile.Name, 22);
            _avatarSeedCache = profile.AvatarSeed;
            profilePill.Add(_headerAvatar);

            _nameLabel = Widgets.Text(profile.Name, 13, UiTheme.Text);
            _nameLabel.style.marginLeft = 6;
            profilePill.Add(_nameLabel);
            profilePill.Add(Widgets.Text("›", 16, UiTheme.Accent, bold: true));
            _islandRow.Add(profilePill);

            var mightBox = new VisualElement();
            mightBox.style.alignItems = Align.FlexEnd;
            mightBox.Add(Widgets.Text("MIGHT", 8, UiTheme.Dim));
            _mightLabel = Widgets.Text("", 13, UiTheme.Energy, bold: true);
            mightBox.Add(_mightLabel);
            _islandRow.Add(mightBox);
            header.Add(_islandRow);

            // Row 2 — HQ coordinates, centered; Aegis Shield countdown rides beside
            // them while a bubble is up (user spec: the shield needs a visible timer).
            var hqRow = Widgets.HBox(Justify.Center);
            hqRow.style.height = 20;
            _coordsLabel = Widgets.Text("", 11, UiTheme.Accent);
            hqRow.Add(_coordsLabel);
            _shieldLabel = Widgets.Text("", 11, new Color(0.45f, 0.75f, 1f), bold: true);
            _shieldLabel.style.marginLeft = 10;
            _shieldLabel.style.display = DisplayStyle.None;
            hqRow.Add(_shieldLabel);
            header.Add(hqRow);

            // Row 3 — resource strip: GOLD / QUARTZ / HELIUM / ENERGY / D.MATTER.
            var resRow = Widgets.HBox(Justify.SpaceAround, Align.Center);
            resRow.style.height = 50;
            resRow.style.borderTopWidth = 1;
            resRow.style.borderTopColor = UiTheme.Stroke;

            VisualElement Slot(string caption, Color captionColor, out Label amount)
            {
                var slot = new VisualElement();
                slot.style.alignItems = Align.Center;
                slot.style.width = Length.Percent(20f);
                slot.Add(Widgets.Text(caption, 9, captionColor, bold: true));
                amount = Widgets.Text("", 13, UiTheme.Text);
                amount.style.marginTop = 2;
                slot.Add(amount);
                return slot;
            }

            resRow.Add(Slot("GOLD", UiTheme.Gold, out _resAmounts[0]));
            resRow.Add(Slot("QUARTZ", UiTheme.Quartz, out _resAmounts[1]));
            resRow.Add(Slot("HELIUM", UiTheme.Helium, out _resAmounts[2]));
            resRow.Add(Slot("ENERGY", UiTheme.Energy, out _energyLabel));
            resRow.Add(Slot("D.MATTER", UiTheme.DarkMatter, out _dmLabel));
            header.Add(resRow);

            _root.Add(header);
        }

        // ---------- bottom nav (v1 BottomNav) ----------

        void BuildBottomNav()
        {
            var nav = Widgets.HBox(Justify.SpaceAround, Align.Center);
            nav.style.position = Position.Absolute;
            nav.style.left = 0;
            nav.style.right = 0;
            nav.style.bottom = 0;
            nav.style.height = UiTheme.NavH;
            nav.style.backgroundColor = UiTheme.Panel;

            Button NavButton(string label, Action onTap)
            {
                var b = Widgets.TextButton(label, onTap, 12);
                b.style.width = Length.Percent(18f);
                b.style.height = UiTheme.NavH - 16;
                nav.Add(b);
                return b;
            }

            _navButtons[ViewId.Map] = NavButton("MAP", () => SwitchView(ViewId.Map));
            _navButtons[ViewId.Fleet] = NavButton("FLEET", () => SwitchView(ViewId.Fleet));
            _navButtons[ViewId.Base] = NavButton("BASE", () => SwitchView(ViewId.Base));
            NavButton("ITEMS", OpenShop);
            _mailButton = NavButton("MAIL", OpenMailbox);

            _root.Add(nav);
            HighlightNav();
        }

        /// <summary>Thin strip above the nav: the latest galaxy-news headline
        /// (every battle in the simulated galaxy posts to the wire).</summary>
        void BuildNewsTicker()
        {
            _ticker = new VisualElement();
            _ticker.style.position = Position.Absolute;
            _ticker.style.left = 0;
            _ticker.style.right = 0;
            _ticker.style.bottom = UiTheme.NavH;
            _ticker.style.height = TickerH;
            _ticker.style.backgroundColor = new Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.92f);
            _ticker.style.borderTopWidth = 1;
            _ticker.style.borderTopColor = UiTheme.Stroke;
            _ticker.style.flexDirection = FlexDirection.Row;
            _ticker.style.alignItems = Align.Center;
            _ticker.style.paddingLeft = 10;
            _ticker.style.paddingRight = 10;
            _ticker.RegisterCallback<PointerUpEvent>(_ => OpenNews());

            var icon = Widgets.Text("⚔", 11, UiTheme.Accent);
            icon.style.marginRight = 6;
            _ticker.Add(icon);
            _tickerLabel = Widgets.Text("Galaxy news — tap for the battle wire", 11, UiTheme.Dim);
            _tickerLabel.style.overflow = Overflow.Hidden;
            _tickerLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _tickerLabel.style.flexShrink = 1f;
            _ticker.Add(_tickerLabel);
            _root.Add(_ticker);
        }

        void RefreshTicker()
        {
            var galaxy = _ctx.Bots;
            var state = _ctx.State;
            if (galaxy == null || state == null || galaxy.News.Count == 0) return;
            var latest = galaxy.News[^1];
            string ago = UiTheme.FmtDuration(Math.Max(0, state.Tick - latest.Tick));
            // Rebuild whenever the headline OR its age string changes — the age used
            // to freeze at whatever it said when the item landed (user report).
            string text = $"{NewsPanel.Headline(_ctx, latest)} · {ago} ago";
            if (galaxy.News.Count == _tickerNewsCount && text == _tickerLabel.text) return;
            _tickerNewsCount = galaxy.News.Count;
            _tickerLabel.text = text;
            bool badForMe = (latest.DefenderId == 0 && latest.AttackerWon)
                         || (latest.AttackerId == 0 && !latest.AttackerWon);
            _tickerLabel.style.color = latest.AttackerId == 0 || latest.DefenderId == 0
                ? (badForMe ? UiTheme.Bad : UiTheme.Accent)
                : UiTheme.Text;
        }

        /// <summary>Right-side MORE FAB — pops a reverse-L column of shortcuts.</summary>
        void BuildMoreMenu()
        {
            _moreFab = Widgets.TextButton("⋯", ToggleMoreMenu, 20);
            _moreFab.style.position = Position.Absolute;
            _moreFab.style.right = 12;
            _moreFab.style.bottom = UiTheme.NavH + TickerH + 16; // search/favorites moved LEFT
            _moreFab.style.width = 48;
            _moreFab.style.height = 48;
            _moreFab.style.borderTopLeftRadius = 24;
            _moreFab.style.borderTopRightRadius = 24;
            _moreFab.style.borderBottomLeftRadius = 24;
            _moreFab.style.borderBottomRightRadius = 24;
            Widgets.SetBorder(_moreFab, UiTheme.Accent, 2f);
            _root.Add(_moreFab);

            _moreMenu = new VisualElement();
            _moreMenu.style.position = Position.Absolute;
            _moreMenu.style.right = 12;
            _moreMenu.style.bottom = UiTheme.NavH + TickerH + 16 + 56;
            _moreMenu.style.alignItems = Align.Center;
            _moreMenu.style.display = DisplayStyle.None;

            Button MiniFab(string label, Action onTap)
            {
                var b = Widgets.TextButton(label, () => { ToggleMoreMenu(); onTap(); }, 9);
                b.style.width = 48;
                b.style.height = 44;
                b.style.marginBottom = 8;
                b.style.borderTopLeftRadius = 22;
                b.style.borderTopRightRadius = 22;
                b.style.borderBottomLeftRadius = 22;
                b.style.borderBottomRightRadius = 22;
                Widgets.SetBorder(b, UiTheme.Stroke, 2f);
                _moreMenu.Add(b);
                return b;
            }
            // Stacked bottom-up visually; add in top-down order.
            MiniFab("DAILY", OpenDaily);
            MiniFab("RANK", OpenRankings);
            _root.Add(_moreMenu);
        }

        void ToggleSpin()
        {
            var spin = GameObject.Find("Home Planet")?.GetComponent<PlanetSpin>();
            if (spin == null) return;
            spin.enabled = !spin.enabled;
            _spinToggle.text = spin.enabled ? "STOP" : "SPIN";
            Widgets.SetButtonHighlight(_spinToggle, spin.enabled);
            Toast(spin.enabled ? "Auto-rotate on — sit back and watch your world" : "Auto-rotate off");
        }

        void ToggleMoreMenu()
        {
            _moreOpen = !_moreOpen;
            _moreMenu.style.display = _moreOpen ? DisplayStyle.Flex : DisplayStyle.None;
            Widgets.SetButtonHighlight(_moreFab, _moreOpen);
        }

        void BuildQueuesFab()
        {
            _queuesFab = Widgets.TextButton("≡", OpenQueues, 20);
            _queuesFab.style.position = Position.Absolute;
            _queuesFab.style.left = 12;
            _queuesFab.style.bottom = UiTheme.NavH + TickerH + 16;
            _queuesFab.style.width = 48;
            _queuesFab.style.height = 48;
            _queuesFab.style.borderTopLeftRadius = 24;
            _queuesFab.style.borderTopRightRadius = 24;
            _queuesFab.style.borderBottomLeftRadius = 24;
            _queuesFab.style.borderBottomRightRadius = 24;
            Widgets.SetBorder(_queuesFab, UiTheme.Accent, 2f);

            _queuesBadge = new VisualElement { pickingMode = PickingMode.Ignore };
            _queuesBadge.style.position = Position.Absolute;
            _queuesBadge.style.right = -4;
            _queuesBadge.style.top = -4;
            _queuesBadge.style.width = 18;
            _queuesBadge.style.height = 18;
            _queuesBadge.style.borderTopLeftRadius = 9;
            _queuesBadge.style.borderTopRightRadius = 9;
            _queuesBadge.style.borderBottomLeftRadius = 9;
            _queuesBadge.style.borderBottomRightRadius = 9;
            _queuesBadge.style.backgroundColor = UiTheme.Bad;
            _queuesBadge.style.justifyContent = Justify.Center;
            _queuesBadge.style.alignItems = Align.Center;
            _queuesBadge.style.display = DisplayStyle.None;
            _queuesBadgeLabel = Widgets.Text("", 11, Color.white, bold: true);
            _queuesBadge.Add(_queuesBadgeLabel);
            _queuesFab.Add(_queuesBadge);

            _root.Add(_queuesFab);
        }

        // ---------- view switching ----------

        public void SwitchView(ViewId view)
        {
            CloseModal();
            CloseNodeCallout();
            var mapView = GetComponent<MapView>();
            if (mapView != null)
            {
                mapView.CancelTargeting();
                mapView.ClearSelection();
            }

            // Re-tapping MAP while already on the map flies you home (v1 behavior).
            if (view == ViewId.Map && View == ViewId.Map && mapView != null && mapView.IsActive)
            {
                mapView.FocusTile(_ctx.State!.HomeTile);
                return;
            }

            if (view == ViewId.Fleet)
            {
                // Fleet is a full-screen panel over whichever world view is behind.
                View = ViewId.Fleet;
                HighlightNav();
                OpenModal(FleetPanel.Build(_ctx, out var refresh), refresh);
                return;
            }

            if (view == ViewId.Map)
            {
                if (mapView == null) mapView = gameObject.AddComponent<MapView>();
                if (!mapView.IsActive) mapView.EnterMap();
            }
            else if (mapView != null && mapView.IsActive)
            {
                mapView.ExitMap();
            }
            View = view;
            HighlightNav();
        }

        /// <summary>MapView calls this when the player taps their home planet (or ◀ Base).</summary>
        public void OnMapExited()
        {
            if (View == ViewId.Map)
            {
                View = ViewId.Base;
                HighlightNav();
            }
        }

        void HighlightNav()
        {
            foreach (var kv in _navButtons)
                Widgets.SetButtonHighlight(kv.Value, kv.Key == View);
        }

        // ---------- modal host ----------

        public void OpenModal(VisualElement modal, Action? refresh = null)
        {
            EnsureBuilt(); // boot code can open panels before our Start runs
            CloseModal();
            _modal = modal;
            _modalRefresh = refresh;
            _modalLayer.pickingMode = PickingMode.Position;
            _modalLayer.Add(modal);
        }

        public void CloseModal()
        {
            if (_modal == null) return;
            _modalLayer.Remove(_modal);
            _modalLayer.pickingMode = PickingMode.Ignore;
            _modal = null;
            _modalRefresh = null;
            if (View == ViewId.Fleet) { View = ViewId.Base; HighlightNav(); }
        }

        public bool HasModal => _modal != null;

        // ---------- panel launchers ----------

        // ---------- node callout (non-modal — the map stays live underneath) ----------

        public void OpenNodeCallout(GalaxyRoyale.Sim.Map.MapNode node)
        {
            CloseNodeCallout();
            _calloutNode = node;
            _callout = NodeCallout.Build(_ctx, node, () =>
            {
                CloseNodeCallout();
                GetComponent<MapView>()?.ClearSelection();
            });
            _calloutLayer.Add(_callout);
        }

        public void CloseNodeCallout()
        {
            if (_callout == null) return;
            _calloutLayer.Remove(_callout);
            _callout = null;
            _calloutNode = null;
        }

        /// <summary>Composer's × returns here so the callout survives a peek into the composer.</summary>
        public void ReopenNodeCallout(GalaxyRoyale.Sim.Map.MapNode node) => OpenNodeCallout(node);

        /// <summary>Host an arbitrary bottom-strip callout (remote planets, empty
        /// space, marches) in the same non-modal layer as node callouts.</summary>
        public void OpenCalloutElement(VisualElement element)
        {
            CloseNodeCallout();
            _callout = element;
            _calloutLayer.Add(element);
        }

        public void OpenProfile() => OpenModal(ProfilePanel.Build(_ctx, out var r), r);
        public void OpenRankings() => OpenModal(RankingsPanel.Build(_ctx, out var r), r);
        public void OpenNews() => OpenModal(NewsPanel.Build(_ctx, out var r), r);
        public void OpenDaily() => OpenModal(DailyPanel.Build(_ctx, out var r), r);
        public void OpenResearch() => OpenModal(ResearchPanel.Build(_ctx, out var r), r);
        public void OpenQueues() => OpenModal(QueuesPanel.Build(_ctx, out var r), r);
        public void OpenShop() => OpenModal(ShopPanel.Build(_ctx, out var r), r);
        public void OpenMailbox() => OpenModal(MailboxPanel.Build(_ctx, out var r), r);

        // ---------- toasts (v1 ToastManager: max 3, ~2.6s lifetime) ----------

        public void Toast(string message)
        {
            EnsureBuilt();
            if (_toasts.Count >= 3)
            {
                var oldest = _toasts[0];
                _toasts.RemoveAt(0);
                _toastLayer.Remove(oldest);
            }
            var toast = new VisualElement { pickingMode = PickingMode.Ignore };
            toast.style.backgroundColor = new Color(UiTheme.PanelLight.r, UiTheme.PanelLight.g, UiTheme.PanelLight.b, 0.95f);
            Widgets.SetBorder(toast, UiTheme.Stroke, 1f);
            toast.style.paddingLeft = 14;
            toast.style.paddingRight = 14;
            toast.style.paddingTop = 8;
            toast.style.paddingBottom = 8;
            toast.style.marginTop = 6;
            toast.style.maxWidth = UiTheme.W - 80;
            var label = Widgets.Text(message, 13, UiTheme.Text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            toast.Add(label);
            _toastLayer.Add(toast);
            _toasts.Add(toast);

            toast.schedule.Execute(() =>
            {
                if (_toasts.Remove(toast) && _toastLayer.Contains(toast))
                    _toastLayer.Remove(toast);
            }).ExecuteLater(2600);
        }

        /// <summary>True when the screen-space point (Input System coords, y-up) hits any UI element.</summary>
        public static bool IsPointerOverUI(Vector2 screenPos)
        {
            var inst = Instance;
            if (inst?._doc?.rootVisualElement?.panel is not IPanel panel) return false;
            var panelPos = RuntimePanelUtils.ScreenToPanel(
                panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            return panel.Pick(panelPos) != null;
        }

        // ---------- sim events (v1 UIScene.create subscription) ----------

        void OnSimEvent(SimEvent e)
        {
            switch (e)
            {
                case BuildingCompleted:
                case ShipsCompleted:
                case ResearchCompleted:
                    _queuesPingUntil = Time.time + 0.35f; // a queue may be idle now — flash the FAB
                    break;
                case MarchPhaseChanged mpc when mpc.Phase == MarchPhase.Gathering:
                    Toast("Fleet on station — gathering");
                    break;
                case MarchReturned mr:
                {
                    var parts = new List<string>();
                    if (mr.Cargo.Gold > 0) parts.Add($"+{UiTheme.FmtAmount(mr.Cargo.Gold)} gold");
                    if (mr.Cargo.Quartz > 0) parts.Add($"+{UiTheme.FmtAmount(mr.Cargo.Quartz)} quartz");
                    if (mr.Cargo.Helium > 0) parts.Add($"+{UiTheme.FmtAmount(mr.Cargo.Helium)} helium");
                    if (mr.CargoDm > 0) parts.Add($"+{mr.CargoDm / 1000:N0} dark matter");
                    Toast(parts.Count > 0 ? $"Fleet home: {string.Join(", ", parts)}" : "Fleet returned home");
                    break;
                }
                case BattleResolved:
                    // v1 pops the report immediately; MarchSystem inserts it at Mailbox[0].
                    if (_ctx.State!.Mailbox.Count > 0 && _ctx.State.Mailbox[0] is BattleMailReport report)
                        MailboxPanel.OpenReport(_ctx, report);
                    else
                        Toast("Battle resolved — report in your Mailbox");
                    break;
                case SpyReportReceived:
                    Toast("Recon telemetry received — check your Mailbox");
                    break;
                case ColonyRaided raided:
                    // A rival's raid landed on the home colony — pop the report.
                    if (_ctx.State!.Mailbox.Count > 0 && _ctx.State.Mailbox[0] is BattleMailReport cr)
                        MailboxPanel.OpenReport(_ctx, cr);
                    else
                        Toast($"Your colony was raided by {raided.AttackerName} — check Mail");
                    break;
            }
        }

        // ---------- per-frame refresh (string-cached like v1) ----------

        void Update()
        {
            EnsureBuilt();
            var state = _ctx.State;
            if (state == null) return;

            // Safe-area top inset (Dynamic Island) in panel units — v1's ISLAND_ROW_H.
            float scale = Screen.width / (float)UiTheme.W;
            float topInset = scale > 0f ? (Screen.height - Screen.safeArea.yMax) / scale : 0f;
            _islandRow.style.height = Mathf.Max(topInset + 4f, 34f);
            _islandRow.style.paddingTop = Mathf.Max(0f, topInset - 26f);
            // Park the auto-rotate button just below the (safe-area-aware) header
            // so it never overlaps the Dark Matter box on notched devices.
            float headerH = Mathf.Max(topInset + 4f, 34f) + 20f + 50f;
            _spinToggle.style.top = headerH + 10f;

            // Header numbers only move when the sim ticks (1 Hz) or the player
            // acts — refreshing them 60×/frame was ComputePower + string-format
            // GC churn for nothing. 4 Hz keeps button-driven changes (purchases,
            // queue starts) feeling instant without the per-frame cost.
            if (state.Tick != _headerTick || Time.time >= _nextHeaderRefresh)
            {
                _headerTick = state.Tick;
                _nextHeaderRefresh = Time.time + 0.25f;
                RefreshHeader(state);
            }

            Widgets.SetBorder(_queuesFab, Time.time < _queuesPingUntil ? UiTheme.Energy : UiTheme.Accent, 2f);
            var mapFabDisplay = View == ViewId.Map && _modal == null
                ? DisplayStyle.Flex : DisplayStyle.None;
            _searchFab.style.display = mapFabDisplay;
            _favoritesFab.style.display = mapFabDisplay;
            _spinToggle.style.display = View == ViewId.Base && _modal == null
                ? DisplayStyle.Flex : DisplayStyle.None;

            // Slow-cadence chores: ticker headline + daily-reward glow on the MORE FAB.
            if (Time.time >= _nextDailyGlowPoll)
            {
                _nextDailyGlowPoll = Time.time + 5f;
                RefreshTicker();
                Widgets.SetBorder(_moreFab,
                    DailyObjectives.AnyClaimable(state) ? UiTheme.Good : UiTheme.Accent, 2f);
            }

            _modalRefresh?.Invoke();
        }

        /// <summary>Everything in the header + badges that only changes when the
        /// sim ticks or the player acts. Runs at ≤4 Hz (see Update), not 60.</summary>
        void RefreshHeader(GameState state)
        {
            for (int i = 0; i < 3; i++)
            {
                var res = Resources_All[i];
                string s = UiTheme.FmtAmount(state.Resources.Get(res));
                if (s != _cache[i]) { _cache[i] = s; _resAmounts[i].text = s; }
            }

            var energy = ResourceSystem.GetEnergyBalance(state);
            string es = $"{Mathf.RoundToInt(energy.Factor * 100)}%";
            if (es != _cache[3])
            {
                _cache[3] = es;
                _energyLabel.text = es;
                _energyLabel.style.color = energy.Factor < 1f ? UiTheme.Bad : UiTheme.Text;
            }

            string dm = state.Premium.DarkMatter.ToString("N0");
            if (dm != _dmCache) { _dmCache = dm; _dmLabel.text = dm; }

            string might = PowerSystem.ComputePower(state).ToString("N0");
            if (might != _mightCache) { _mightCache = might; _mightLabel.text = might; }

            if (state.Profile.Name != _nameCache)
            {
                _nameCache = state.Profile.Name;
                _nameLabel.text = state.Profile.Name;
            }

            if (state.Profile.AvatarSeed != _avatarSeedCache)
            {
                _avatarSeedCache = state.Profile.AvatarSeed;
                var fresh = Portraits.Avatar(_avatarSeedCache, state.Profile.Name, 22);
                var parent = _headerAvatar.parent;
                int index = parent.IndexOf(_headerAvatar);
                parent.RemoveAt(index);
                parent.Insert(index, fresh);
                _headerAvatar = fresh;
            }

            string coords = _hqOverride ?? $"HQ {state.HomeTile.X}, {state.HomeTile.Y}";
            if (coords != _coordsCache) { _coordsCache = coords; _coordsLabel.text = coords; }

            // Aegis Shield countdown — ticks down live next to the HQ coords.
            // (Plain text label: the runtime font tofu-boxes non-BMP emoji.)
            long shieldLeft = state.Buffs.ShieldUntilTick - state.Tick;
            string shield = shieldLeft > 0 ? $"SHIELD {UiTheme.FmtDuration(shieldLeft)}" : "";
            if (shield != _shieldCache)
            {
                _shieldCache = shield;
                _shieldLabel.text = shield;
                _shieldLabel.style.display = shieldLeft > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            bool unread = false;
            foreach (var item in state.Mailbox)
                if (!item.Read) { unread = true; break; }
            if (unread != _mailBadge)
            {
                _mailBadge = unread;
                _mailButton.text = unread ? "MAIL ●" : "MAIL";
                _mailButton.style.color = unread ? UiTheme.Accent : UiTheme.Text;
            }

            int idleCount = QueuesPanel.IdleQueueCount(state);
            if (idleCount != _queuesCountCache)
            {
                _queuesCountCache = idleCount;
                bool show = idleCount > 0;
                _queuesBadge.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                _queuesBadgeLabel.text = show ? idleCount.ToString() : "";
            }
        }

        static readonly ResourceId[] Resources_All = { ResourceId.Gold, ResourceId.Quartz, ResourceId.Helium };
    }
}
