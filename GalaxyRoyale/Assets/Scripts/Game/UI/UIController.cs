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
        /// <summary>Ships finished per hull since that hull's order started (one toast per batch).</summary>
        readonly Dictionary<HullId, int> _shipsBuilt = new();

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
        // MORE-menu shortcuts that light up when something waits to be claimed.
        Button _dailyFab = null!, _eventsFab = null!, _clanFab = null!;
        // Top-left stack on the BASE view: quest tracker, then the event chip.
        VisualElement _leftStack = null!;
        VisualElement _eventChip = null!;
        Label _eventChipTitle = null!, _eventChipInfo = null!, _eventClaimPill = null!;
        string _eventKey = "";
        int _eventSeenInstance = -1, _eventNudgedInstance = -1;
        // Commander's Path tracker (top left of the BASE view).
        VisualElement _questTracker = null!;
        Label _questTitle = null!, _questGoal = null!;
        Label _questClaimPill = null!;
        string _questKey = "";
        int _questNudgedStep = -1;

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
            // Painted icons — font glyphs (⌕, ⟳, ⚔ …) tofu-box on device.
            Button LeftFab(Icon icon, string? caption, Action onTap, float bottomOffset)
            {
                var fab = Widgets.Fab(icon, caption, onTap);
                fab.style.position = Position.Absolute;
                fab.style.left = 12;
                fab.style.bottom = UiTheme.NavH + TickerH + 16 + bottomOffset;
                fab.style.display = DisplayStyle.None;
                _root.Add(fab);
                return fab;
            }
            _searchFab = LeftFab(Icon.Search, "FIND", () => SearchPanel.Open(_ctx), 56f);
            _favoritesFab = LeftFab(Icon.Star, null, () => FavoritesPanel.Open(_ctx), 112f);

            // Auto-rotate toggle — top right of the BASE view, default off.
            _spinToggle = Widgets.Fab(Icon.Rotate, "SPIN", ToggleSpin, 44f, UiTheme.Stroke);
            _spinToggle.style.position = Position.Absolute;
            _spinToggle.style.right = 12;
            _spinToggle.style.top = 112; // just under the header (Update tracks the safe area)
            _root.Add(_spinToggle);

            // Quest tracker + event chip share one column so the chip slides
            // up when the Commander's Path is finished.
            _leftStack = new VisualElement();
            _leftStack.style.position = Position.Absolute;
            _leftStack.style.left = 12;
            _leftStack.style.top = 112;
            _leftStack.style.alignItems = Align.FlexStart;
            _root.Add(_leftStack);
            BuildQuestTracker();
            BuildEventChip();
            BuildCoreChip();

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

            _labelBlockers.AddRange(new[]
            {
                _header, _nav, _ticker, _queuesFab, _moreFab, _moreMenu,
                _spinToggle, _searchFab, _favoritesFab, _calloutLayer, _leftStack,
            });

            _ctx.Events!.Subscribe(OnSimEvent);
        }

        /// <summary>Fresh-commander orientation — LocalBootstrap fires this after
        /// NEW GAME (not at UI build time, which would toast over the title screen).</summary>
        public void ShowRookieHints()
        {
            EnsureBuilt();
            _root.schedule.Execute(() =>
                Toast("Follow the Commander's Path — your first quest is top left", Icon.Star, UiTheme.Energy))
                .ExecuteLater(2500);
            _root.schedule.Execute(() =>
                Toast("Upgrade the Command Center to raise all building caps")).ExecuteLater(6500);
        }

        // ---------- header (v1 ResourceBar) ----------

        void BuildHeader()
        {
            var header = _header = new VisualElement();
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
            var nav = _nav = Widgets.HBox(Justify.SpaceAround, Align.Center);
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

            // Painted swords: the "⚔" glyph rendered as "□" on device (user report).
            var icon = Icons.Make(Icon.Swords, 12, UiTheme.Accent);
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
            _moreFab = Widgets.Fab(Icon.More, null, ToggleMoreMenu);
            _moreFab.style.position = Position.Absolute;
            _moreFab.style.right = 12;
            _moreFab.style.bottom = UiTheme.NavH + TickerH + 16; // search/favorites moved LEFT
            _root.Add(_moreFab);

            _moreMenu = new VisualElement();
            _moreMenu.style.position = Position.Absolute;
            _moreMenu.style.right = 12;
            _moreMenu.style.bottom = UiTheme.NavH + TickerH + 16 + 56;
            _moreMenu.style.alignItems = Align.Center;
            _moreMenu.style.display = DisplayStyle.None;

            Button MiniFab(Icon icon, string label, Action onTap)
            {
                var b = Widgets.Fab(icon, label, () =>
                {
                    ToggleMoreMenu();
                    // A panel that throws while building used to just "do nothing".
                    try { onTap(); }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        Toast($"{label} couldn't open — the error was logged", Icon.Warning, UiTheme.Bad);
                    }
                }, 48f, UiTheme.Stroke);
                b.style.marginBottom = 8;
                _moreMenu.Add(b);
                return b;
            }
            // Stacked bottom-up visually; add in top-down order.
            MiniFab(Icon.Target, "CORE", () => CorePanel.Open(_ctx));
            _clanFab = MiniFab(Icon.Pact, "CLAN", OpenClan);
            MiniFab(Icon.Trophy, "AWARDS", OpenAchievements);
            _eventsFab = MiniFab(Icon.Bolt, "EVENTS", () => OpenEvents());
            _dailyFab = MiniFab(Icon.Check, "DAILY", OpenDaily);
            MiniFab(Icon.Chart, "RANK", () => OpenRankings());
            _root.Add(_moreMenu);
        }

        void ToggleSpin()
        {
            var spin = GameObject.Find("Home Planet")?.GetComponent<PlanetSpin>();
            if (spin == null) return;
            spin.enabled = !spin.enabled;
            Widgets.SetCaption(_spinToggle, spin.enabled ? "STOP" : "SPIN");
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
            _queuesFab = Widgets.Fab(Icon.Menu, null, OpenQueues);
            _queuesFab.style.position = Position.Absolute;
            _queuesFab.style.left = 12;
            _queuesFab.style.bottom = UiTheme.NavH + TickerH + 16;

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
            if (view != View) GameAudio.Feedback(Sfx.Toggle, Haptic.Selection);
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
            // Swapping one panel for another (a report's REPLAY, a rebuilt
            // detail) stays silent; opening over the game gets the whoosh.
            if (_modal == null) GameAudio.Play(Sfx.Open);
            RemoveModal();
            _modal = modal;
            _modalRefresh = refresh;
            _modalLayer.pickingMode = PickingMode.Position;
            _modalLayer.Add(modal);
        }

        public void CloseModal()
        {
            if (_modal != null) GameAudio.Play(Sfx.Close);
            RemoveModal();
        }

        void RemoveModal()
        {
            if (_modal == null) return;
            _modalLayer.Remove(_modal);
            _modalLayer.pickingMode = PickingMode.Ignore;
            _modal = null;
            _modalRefresh = null;
            if (View == ViewId.Fleet) { View = ViewId.Base; HighlightNav(); }
        }

        public bool HasModal => _modal != null;

        /// <summary>A fresh layer for world-anchored labels (building names, map
        /// commander names), painted UNDER the whole HUD so buttons and the header
        /// always occlude it. See <see cref="WorldLabelLayer"/>.</summary>
        public WorldLabelLayer CreateWorldLabelLayer(string name)
        {
            EnsureBuilt();
            return new WorldLabelLayer(_root, name, _labelBlockers);
        }

        /// <summary>HUD pieces world labels must keep clear of (see WorldLabelLayer).</summary>
        readonly List<VisualElement> _labelBlockers = new();
        VisualElement _header = null!, _nav = null!;

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
        public void OpenRankings(bool season = false) => OpenModal(RankingsPanel.Build(_ctx, out var r, season), r);
        public void OpenEvents() => OpenModal(EventsPanel.Build(_ctx, out var r), r);
        public void OpenAchievements() => OpenModal(AchievementsPanel.Build(_ctx, out var r), r);
        public void OpenClan() => OpenModal(ClanPanel.Build(_ctx, out var r), r);
        public void OpenNews() => OpenModal(NewsPanel.Build(_ctx, out var r), r);
        public void OpenDaily() => OpenModal(DailyPanel.Build(_ctx, out var r), r);
        public void OpenResearch() => OpenModal(ResearchPanel.Build(_ctx, out var r), r);
        public void OpenQueues() => OpenModal(QueuesPanel.Build(_ctx, out var r), r);
        public void OpenShop() => OpenModal(ShopPanel.Build(_ctx, out var r), r);
        public void OpenMailbox() => OpenModal(MailboxPanel.Build(_ctx, out var r), r);

        // ---------- toasts (v1 ToastManager: max 3, ~2.6s lifetime) ----------

        public void Toast(string message) => Toast(message, null);

        /// <summary>Toast with an optional painted icon on the left (radar warnings etc.).</summary>
        public void Toast(string message, Icon? icon, Color? iconColor = null)
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
            if (icon is Icon glyph)
            {
                toast.style.flexDirection = FlexDirection.Row;
                toast.style.alignItems = Align.Center;
                var mark = Icons.Make(glyph, 16, iconColor ?? UiTheme.Energy);
                mark.style.marginRight = 8;
                toast.Add(mark);
            }
            var label = Widgets.Text(message, 13, UiTheme.Text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.flexShrink = 1f;
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
                // Completion feedback: these used to only tint the queues FAB for
                // 0.35 s — easy to miss that an upgrade had landed.
                case BuildingCompleted built:
                    _queuesPingUntil = Time.time + 0.35f; // a queue may be idle now — flash the FAB
                    Toast($"{Buildings.Defs[built.Building].Name} reached Lv {built.Level}", Icon.Check, UiTheme.Good);
                    GameAudio.Feedback(Sfx.Success, Haptic.Success);
                    break;
                case ResearchCompleted researched:
                    _queuesPingUntil = Time.time + 0.35f;
                    Toast($"{Techs.Defs[researched.Tech].Name} Lv {researched.Level} researched", Icon.Check, UiTheme.Good);
                    GameAudio.Feedback(Sfx.Success, Haptic.Success);
                    break;
                case ShipsCompleted ships:
                {
                    _queuesPingUntil = Time.time + 0.35f;
                    // Fires per SHIP; toast once when that hull's order runs out
                    // (the finished order is still queued with Remaining 0 here).
                    _shipsBuilt[ships.Hull] = (_shipsBuilt.TryGetValue(ships.Hull, out var soFar) ? soFar : 0) + ships.Count;
                    bool moreComing = false;
                    foreach (var order in _ctx.State!.ShipQueue)
                        if (order.Hull == ships.Hull && order.Remaining > 0) { moreComing = true; break; }
                    if (!moreComing)
                    {
                        int total = _shipsBuilt[ships.Hull];
                        _shipsBuilt.Remove(ships.Hull);
                        Toast($"{total}× {Ships.Defs[ships.Hull].Name} ready in the hangar", Icon.Check, UiTheme.Good);
                        GameAudio.Feedback(Sfx.Success, Haptic.Light);
                    }
                    break;
                }
                case MarchPhaseChanged mpc when mpc.Phase == MarchPhase.Gathering:
                {
                    var held = _ctx.State?.Marches.Find(m => m.Id == mpc.MarchId);
                    // The battle report (or the garrison merge) follows at once.
                    if (held?.Mission == MarchMission.Intercept || held?.Mission == MarchMission.Core) break;
                    Toast(held?.Mission == MarchMission.Garrison
                        ? $"Your garrison is on guard at {_ctx.Bots?.Find(held.GuardEmpireId)?.Name ?? "your clanmate"}'s colony"
                        : "Fleet on station — gathering",
                        held?.Mission == MarchMission.Garrison ? Icon.Shield : null, UiTheme.Good);
                    break;
                }
                case GarrisonFought fought:
                    Toast(fought.Held
                        ? $"Your garrison helped hold {fought.HostName}'s colony against {fought.AttackerName}"
                        : $"{fought.AttackerName} broke through your garrison at {fought.HostName}'s colony",
                        Icon.Shield, fought.Held ? UiTheme.Good : UiTheme.Bad);
                    GameAudio.Feedback(fought.Held ? Sfx.Victory : Sfx.Defeat, fought.Held ? Haptic.Success : Haptic.Warning);
                    break;
                case InterceptMissed missed:
                    Toast($"Intercept missed — {missed.TargetName} changed course. Your fleet is heading home.", Icon.Info, UiTheme.Dim);
                    break;
                case CoreSeized seized when _ctx.State != null && _ctx.Bots != null:
                {
                    string holder = CoreSystem.HolderName(_ctx.State, _ctx.Bots);
                    if (seized.HolderId == 0)
                    {
                        Toast("You hold the Galactic Core — tribute every hour", Icon.Target, UiTheme.Good);
                        GameAudio.Feedback(Sfx.Victory, Haptic.Success);
                    }
                    else if (seized.PreviousHolderId == 0)
                    {
                        Toast(seized.HolderId == CoreSystem.GuardiansId
                            ? "You no longer hold the Galactic Core — the guardians returned"
                            : $"The Galactic Core fell to {holder}", Icon.Target, UiTheme.Bad);
                        GameAudio.Feedback(Sfx.Defeat, Haptic.Warning);
                    }
                    else if (CoreSystem.ClanHolds(_ctx.State, _ctx.Bots))
                        Toast($"Your clanmate {holder} seized the Galactic Core", Icon.Pact, UiTheme.Good);
                    break;
                }
                case CoreTributePaid tribute:
                {
                    var r = tribute.Resources;
                    string dm = tribute.DarkMatter > 0 ? $" · +{tribute.DarkMatter} DM" : "";
                    Toast($"{(tribute.Clan ? "Clan core tribute" : "Core tribute")}: +{UiTheme.FmtAmount(r.Gold)} gold · " +
                        $"+{UiTheme.FmtAmount(r.Quartz)} quartz · +{UiTheme.FmtAmount(r.Helium)} helium{dm}", Icon.Target, UiTheme.Energy);
                    GameAudio.Play(Sfx.Coins, 0.5f);
                    break;
                }
                case CoreUnderAttack attack:
                {
                    string who = _ctx.Bots?.Find(attack.AttackerId)?.Name ?? "A commander";
                    int eta = Math.Max(0, attack.ArrivesAtTick - (_ctx.State?.Tick ?? 0));
                    Toast($"{who} is assaulting your core — lands in {UiTheme.FmtDuration(eta)}", Icon.Warning, UiTheme.Bad);
                    GameAudio.Feedback(Sfx.Alert, Haptic.Warning);
                    break;
                }
                case ClanGarrisonArrived arrived:
                    Toast(arrived.Wings == 1 ? "A clan garrison is on guard at your colony"
                        : $"{arrived.Wings} clan garrisons are on guard at your colony", Icon.Shield, UiTheme.Good);
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
                    // The fight plays out (SKIP-able replay), then the report; a
                    // round-less one goes straight to the report. MarchSystem and
                    // RaidArrivals insert it at Mailbox[0] before emitting.
                    if (_ctx.State!.Mailbox.Count > 0 && _ctx.State.Mailbox[0] is BattleMailReport report)
                        ShowBattle(report);
                    else
                        Toast("Battle resolved — report in your Mailbox");
                    break;
                case SpyReportReceived:
                    Toast("Recon telemetry received — check your Mailbox");
                    break;
                case ColonyRaided raided:
                    // A rival's raid landed on the home colony — pop the report.
                    if (_ctx.State!.Mailbox.Count > 0 && _ctx.State.Mailbox[0] is BattleMailReport cr)
                        ShowBattle(cr);
                    else
                        Toast($"Your colony was raided by {raided.AttackerName} — check Mail");
                    break;
                case AchievementUnlocked unlocked:
                {
                    var a = unlocked.Achievement;
                    Toast(a.Title != null
                        ? $"Achievement: {a.Name} · +{a.RewardDM} DM · title \"{a.Title}\" unlocked"
                        : $"Achievement: {a.Name} · +{a.RewardDM} DM", Icon.Trophy, UiTheme.Energy);
                    GameAudio.Feedback(Sfx.Quest, Haptic.Success);
                    break;
                }
                case SeasonEnded ended:
                {
                    var rec = ended.Record;
                    Toast($"Season {rec.Season} is over — you placed #{rec.Rank} of {rec.Of} · +{rec.RewardDM} DM",
                        Icon.Trophy, UiTheme.Energy);
                    GameAudio.Feedback(Sfx.Victory, Haptic.Success);
                    break;
                }
                case ClanSuppliesArrived:
                    Toast("Your clan's supply run arrived — collect it in MORE › CLAN", Icon.Pact, UiTheme.Good);
                    GameAudio.Play(Sfx.Coins, 0.6f);
                    break;
                case ClanInviteReceived invite:
                    if (_ctx.Bots?.FindClan(invite.ClanId) is { } inviting)
                    {
                        Toast($"{ClanSystem.Label(inviting)} invites you to join — MORE › CLAN", Icon.Pact, UiTheme.Good);
                        GameAudio.Feedback(Sfx.Alert, Haptic.Light);
                    }
                    break;
                case ClanWarDeclared declared:
                {
                    var bots = _ctx.Bots;
                    var other = bots?.FindClan(declared.PlayerClanAttacked ? declared.ClanId : declared.EnemyClanId);
                    if (other == null) break;
                    Toast(declared.PlayerClanAttacked
                        ? $"{ClanSystem.Label(other)} declared war on your clan!"
                        : $"Your clan declared war on {ClanSystem.Label(other)}", Icon.Swords, UiTheme.Bad);
                    GameAudio.Feedback(Sfx.Alert, Haptic.Heavy);
                    break;
                }
                case ClanWarEnded ended:
                {
                    var other = _ctx.Bots?.FindClan(ended.EnemyClanId);
                    string them = other != null ? ClanSystem.Label(other) : "the enemy";
                    Toast(ended.Draw ? $"The war with {them} ended in a draw ({ended.Score}–{ended.EnemyScore})"
                        : ended.Won ? $"Your clan won the war against {them} ({ended.Score}–{ended.EnemyScore}) · +{ClanSystem.WarWinRewardDM} DM"
                        : $"Your clan lost the war against {them} ({ended.Score}–{ended.EnemyScore})",
                        Icon.Swords, ended.Won ? UiTheme.Good : UiTheme.Dim);
                    GameAudio.Feedback(ended.Won ? Sfx.Victory : Sfx.Defeat, ended.Won ? Haptic.Success : Haptic.Warning);
                    break;
                }
            }
        }

        void ShowBattle(BattleMailReport report)
        {
            if (BattleReplayPanel.CanReplay(report)) BattleReplayPanel.Open(_ctx, report);
            else MailboxPanel.OpenReport(_ctx, report);
        }

        // ---------- per-frame refresh (string-cached like v1) ----------

        // ---------- frame pacing ----------
        // Unity on iOS runs at 30 fps unless told otherwise, so pans and pinches
        // felt choppy on 60/120 Hz screens (perf review). 60 fps while a finger is
        // down (plus a beat after, for fling momentum and scroll inertia); idle
        // drops back to the old 30 so battery use at rest is unchanged.
        float _lastTouchTime = -10f;

        /// <summary>Hold 60 fps for a moment, as a touch does — animations (the
        /// battle replay) call this every frame while they play.</summary>
        public void KeepSmooth() => _lastTouchTime = Time.unscaledTime;

        void PaceFrames()
        {
            var pointer = UnityEngine.InputSystem.Pointer.current;
            if (pointer != null && pointer.press.isPressed) _lastTouchTime = Time.unscaledTime;
            int want = Time.unscaledTime - _lastTouchTime < 2.5f ? 60 : 30;
            if (Application.targetFrameRate != want) Application.targetFrameRate = want;
        }

        void Update()
        {
            EnsureBuilt();
            PaceFrames();
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
            _leftStack.style.top = headerH + 10f;

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
            // A bottom callout (node / planet / fleet card) spans the same strip
            // as the corner FABs — they peeked through it, so they step aside.
            // Same for modals: the FABs can't be tapped behind the blocker, and
            // their rings showed under the panel's bottom edge.
            bool calloutUp = _callout != null;
            if (calloutUp && _moreOpen) ToggleMoreMenu();
            var cornerFabDisplay = calloutUp || _modal != null ? DisplayStyle.None : DisplayStyle.Flex;
            _queuesFab.style.display = cornerFabDisplay;
            _moreFab.style.display = cornerFabDisplay;
            var mapFabDisplay = View == ViewId.Map && _modal == null && !calloutUp
                ? DisplayStyle.Flex : DisplayStyle.None;
            _searchFab.style.display = mapFabDisplay;
            _favoritesFab.style.display = mapFabDisplay;
            _spinToggle.style.display = View == ViewId.Base && _modal == null
                ? DisplayStyle.Flex : DisplayStyle.None;
            _leftStack.style.display = View == ViewId.Base && _modal == null && !calloutUp
                ? DisplayStyle.Flex : DisplayStyle.None;
            _questTracker.style.display = QuestSystem.Current(state) != null ? DisplayStyle.Flex : DisplayStyle.None;

            // Slow-cadence chores: ticker headline + daily-reward glow on the MORE FAB.
            if (Time.time >= _nextDailyGlowPoll)
            {
                _nextDailyGlowPoll = Time.time + 5f;
                RefreshTicker();
                bool daily = DailyObjectives.AnyClaimable(state);
                bool eventReady = EventSystem.CanClaim(state);
                bool supplies = state.ClanSupplyRuns > 0 || state.ClanInviteId != 0;
                Widgets.SetBorder(_moreFab, daily || eventReady || supplies ? UiTheme.Good : UiTheme.Accent, 2f);
                Widgets.SetBorder(_dailyFab, daily ? UiTheme.Good : UiTheme.Stroke, 2f);
                Widgets.SetBorder(_eventsFab, eventReady ? UiTheme.Good : UiTheme.Stroke, 2f);
                Widgets.SetBorder(_clanFab, supplies ? UiTheme.Good : UiTheme.Stroke, 2f);
            }

            _modalRefresh?.Invoke();
        }

        /// <summary>Everything in the header + badges that only changes when the
        /// sim ticks or the player acts. Runs at ≤4 Hz (see Update), not 60.</summary>
        // ---------- Commander's Path tracker ----------

        void BuildQuestTracker()
        {
            var t = _questTracker = new VisualElement();
            t.style.maxWidth = 300; // up to the SPIN button
            t.style.flexDirection = FlexDirection.Row;
            t.style.alignItems = Align.Center;
            t.style.paddingLeft = 10;
            t.style.paddingRight = 10;
            t.style.paddingTop = 7;
            t.style.paddingBottom = 7;
            t.style.backgroundColor = new Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.92f);
            Widgets.SetBorder(t, UiTheme.Energy, 1.5f);
            t.style.borderTopLeftRadius = 12;
            t.style.borderTopRightRadius = 12;
            t.style.borderBottomLeftRadius = 12;
            t.style.borderBottomRightRadius = 12;
            var star = Icons.Make(Icon.Star, 16f, UiTheme.Energy);
            star.style.marginRight = 8;
            t.Add(star);
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.style.flexShrink = 1f;
            col.style.overflow = Overflow.Hidden;
            _questTitle = Widgets.Text("", 11, UiTheme.Text, bold: true);
            _questTitle.pickingMode = PickingMode.Ignore;
            // Long titles end in "…" rather than running under the CLAIM pill.
            _questTitle.style.whiteSpace = WhiteSpace.NoWrap;
            _questTitle.style.overflow = Overflow.Hidden;
            _questTitle.style.textOverflow = TextOverflow.Ellipsis;
            _questGoal = Widgets.Text("", 10, UiTheme.Dim);
            _questGoal.pickingMode = PickingMode.Ignore;
            col.Add(_questTitle);
            col.Add(_questGoal);
            t.Add(col);
            _questClaimPill = Widgets.Text("CLAIM", 10, UiTheme.Bg, bold: true);
            _questClaimPill.pickingMode = PickingMode.Ignore;
            _questClaimPill.style.marginLeft = 8;
            _questClaimPill.style.flexShrink = 0f;
            _questClaimPill.style.paddingLeft = 7;
            _questClaimPill.style.paddingRight = 7;
            _questClaimPill.style.paddingTop = 3;
            _questClaimPill.style.paddingBottom = 3;
            _questClaimPill.style.backgroundColor = UiTheme.Good;
            _questClaimPill.style.borderTopLeftRadius = 8;
            _questClaimPill.style.borderTopRightRadius = 8;
            _questClaimPill.style.borderBottomLeftRadius = 8;
            _questClaimPill.style.borderBottomRightRadius = 8;
            _questClaimPill.style.display = DisplayStyle.None;
            t.Add(_questClaimPill);
            t.RegisterCallback<ClickEvent>(_ => QuestPanel.Open(_ctx));
            t.style.display = DisplayStyle.None;
            _leftStack.Add(t);
        }

        // ---------- galaxy event chip (under the quest tracker) ----------

        void BuildEventChip()
        {
            var c = _eventChip = new VisualElement();
            c.style.maxWidth = 300;
            c.style.marginTop = 6;
            c.style.flexDirection = FlexDirection.Row;
            c.style.alignItems = Align.Center;
            c.style.paddingLeft = 10;
            c.style.paddingRight = 10;
            c.style.paddingTop = 5;
            c.style.paddingBottom = 5;
            c.style.backgroundColor = new Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.92f);
            Widgets.SetBorder(c, UiTheme.Stroke, 1.5f);
            c.style.borderTopLeftRadius = 12;
            c.style.borderTopRightRadius = 12;
            c.style.borderBottomLeftRadius = 12;
            c.style.borderBottomRightRadius = 12;
            var bolt = Icons.Make(Icon.Bolt, 14f, UiTheme.Energy);
            bolt.style.marginRight = 7;
            c.Add(bolt);
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.style.flexShrink = 1f;
            _eventChipTitle = Widgets.Text("", 10, UiTheme.Energy, bold: true);
            _eventChipTitle.pickingMode = PickingMode.Ignore;
            _eventChipInfo = Widgets.Text("", 9, UiTheme.Dim);
            _eventChipInfo.pickingMode = PickingMode.Ignore;
            col.Add(_eventChipTitle);
            col.Add(_eventChipInfo);
            c.Add(col);
            _eventClaimPill = Widgets.Text("CLAIM", 9, UiTheme.Bg, bold: true);
            _eventClaimPill.pickingMode = PickingMode.Ignore;
            _eventClaimPill.style.marginLeft = 8;
            _eventClaimPill.style.paddingLeft = 6;
            _eventClaimPill.style.paddingRight = 6;
            _eventClaimPill.style.paddingTop = 2;
            _eventClaimPill.style.paddingBottom = 2;
            _eventClaimPill.style.backgroundColor = UiTheme.Good;
            _eventClaimPill.style.borderTopLeftRadius = 7;
            _eventClaimPill.style.borderTopRightRadius = 7;
            _eventClaimPill.style.borderBottomLeftRadius = 7;
            _eventClaimPill.style.borderBottomRightRadius = 7;
            _eventClaimPill.style.display = DisplayStyle.None;
            c.Add(_eventClaimPill);
            c.RegisterCallback<ClickEvent>(_ => OpenEvents());
            _leftStack.Add(c);
        }

        void RefreshEventChip(GameState state)
        {
            var live = EventSystem.Current(state.Tick);
            var (have, need) = EventSystem.Progress(state);
            bool claimable = EventSystem.CanClaim(state);
            bool claimed = state.EventClaimed && live.Instance == state.EventInstance;
            int left = live.EndTick - state.Tick;
            string key = $"{live.Instance}|{have}|{claimable}|{claimed}|{(left >= 3600 ? left / 3600 : left / 60)}";
            if (key == _eventKey) return;
            bool firstLook = _eventKey.Length == 0;
            _eventKey = key;
            if (EventSystem.IsQuiet(live))
            {
                var first = EventSystem.Next(state.Tick).Def;
                _eventChipTitle.text = $"FIRST GALAXY EVENT IN {UiTheme.FmtLong(left).ToUpperInvariant()}";
                _eventChipInfo.text = $"{first.Name}: {first.Effect}";
            }
            else
            {
                _eventChipTitle.text = $"{live.Def.Name.ToUpperInvariant()} · {UiTheme.FmtLong(left)} left";
                _eventChipInfo.text = claimed ? "Reward claimed — next event soon"
                    : claimable ? "Goal reached — tap to claim"
                    : $"{live.Def.Goal}: {have} / {need}";
            }
            _eventChipInfo.style.color = claimable ? UiTheme.Good : UiTheme.Dim;
            _eventClaimPill.style.display = claimable ? DisplayStyle.Flex : DisplayStyle.None;
            Widgets.SetBorder(_eventChip, claimable ? UiTheme.Good : UiTheme.Stroke, 1.5f);

            // A new event going live mid-session gets announced once.
            if (!firstLook && _eventSeenInstance != live.Instance && !EventSystem.IsQuiet(live))
            {
                Toast($"New galaxy event: {live.Def.Name} — {live.Def.Effect}", Icon.Bolt, UiTheme.Energy);
                GameAudio.Feedback(Sfx.Alert, Haptic.Light);
            }
            _eventSeenInstance = live.Instance;
            if (claimable && !firstLook && _eventNudgedInstance != live.Instance)
            {
                _eventNudgedInstance = live.Instance;
                Toast($"{live.Def.Name} goal reached — tap the event card to claim", Icon.Bolt, UiTheme.Good);
                GameAudio.Feedback(Sfx.Quest, Haptic.Success);
            }
        }

        void RefreshQuestTracker(GameState state)
        {
            var quest = QuestSystem.Current(state);
            if (quest == null) return;
            var (have, need) = QuestSystem.Progress(state, quest);
            bool complete = have >= need;
            string key = $"{state.QuestStep}|{have}|{need}";
            if (key == _questKey) return;
            bool firstLook = _questKey.Length == 0;
            _questKey = key;
            _questTitle.text = $"QUEST {state.QuestStep + 1}/{Quests.Chain.Count} · {quest.Title}";
            _questGoal.text = complete ? "Complete — tap to claim" : QuestPanel.GoalText(quest, have, need);
            _questGoal.style.color = complete ? UiTheme.Good : UiTheme.Dim;
            _questClaimPill.style.display = complete ? DisplayStyle.Flex : DisplayStyle.None;
            Widgets.SetBorder(_questTracker, complete ? UiTheme.Good : UiTheme.Energy, 1.5f);
            // One nudge per step when it completes during play (not on the
            // first look after loading — the tracker already says so).
            if (complete && !firstLook && _questNudgedStep != state.QuestStep)
            {
                _questNudgedStep = state.QuestStep;
                Toast($"Quest complete: {quest.Title} — tap the quest card to claim", Icon.Star, UiTheme.Energy);
                GameAudio.Feedback(Sfx.Quest, Haptic.Success);
            }
        }

        /// <summary>QuestPanel after a successful claim: coins, a tap, and refresh the tracker now.</summary>
        public void OnQuestClaimed()
        {
            GameAudio.Feedback(Sfx.Coins, Haptic.Success);
            _questKey = "";
        }

        // ---------- Galactic Core chip (base view) ----------
        // Shown while you (or a clanmate) hold the core, or an assault is on its way to yours.

        VisualElement _coreChip = null!;
        Label _coreChipTitle = null!, _coreChipInfo = null!;
        string _coreKey = "";

        void BuildCoreChip()
        {
            var c = _coreChip = new VisualElement();
            c.style.maxWidth = 300;
            c.style.marginTop = 6;
            c.style.flexDirection = FlexDirection.Row;
            c.style.alignItems = Align.Center;
            c.style.paddingLeft = 10;
            c.style.paddingRight = 10;
            c.style.paddingTop = 5;
            c.style.paddingBottom = 5;
            c.style.backgroundColor = new Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.92f);
            Widgets.SetBorder(c, UiTheme.Good, 1.5f);
            c.style.borderTopLeftRadius = 12;
            c.style.borderTopRightRadius = 12;
            c.style.borderBottomLeftRadius = 12;
            c.style.borderBottomRightRadius = 12;
            var icon = Icons.Make(Icon.Target, 14f, UiTheme.Energy);
            icon.style.marginRight = 7;
            c.Add(icon);
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.style.flexShrink = 1f;
            _coreChipTitle = Widgets.Text("", 10, UiTheme.Energy, bold: true);
            _coreChipTitle.pickingMode = PickingMode.Ignore;
            _coreChipInfo = Widgets.Text("", 9, UiTheme.Dim);
            _coreChipInfo.pickingMode = PickingMode.Ignore;
            col.Add(_coreChipTitle);
            col.Add(_coreChipInfo);
            c.Add(col);
            c.style.display = DisplayStyle.None;
            c.RegisterCallback<ClickEvent>(_ => CorePanel.Open(_ctx));
            _leftStack.Add(c);
        }

        void RefreshCoreChip(GameState state)
        {
            var galaxy = _ctx.Bots;
            if (galaxy == null) return;
            bool mine = CoreSystem.PlayerHolds(galaxy);
            bool clan = !mine && CoreSystem.ClanHolds(state, galaxy);
            int assault = int.MaxValue;
            if (mine)
                foreach (var m in galaxy.Marches)
                    if (m.Kind == GalaxyRoyale.Sim.Bots.BotMarchKind.CoreAssault && !m.Resolved)
                        assault = Math.Min(assault, m.ArrivesAtTick);
            int tribute = Math.Max(0, galaxy.Core.NextTributeTick - state.Tick);
            string key = $"{mine}|{clan}|{(assault != int.MaxValue ? assault - state.Tick : -1)}|{tribute / 60}";
            if (key == _coreKey) return;
            _coreKey = key;
            _coreChip.style.display = mine || clan ? DisplayStyle.Flex : DisplayStyle.None;
            if (!mine && !clan) return;
            _coreChipTitle.text = mine ? "YOU HOLD THE GALACTIC CORE" : "YOUR CLAN HOLDS THE CORE";
            bool underAttack = assault != int.MaxValue;
            _coreChipInfo.text = underAttack
                ? $"Assault inbound — lands in {UiTheme.FmtDuration(Math.Max(0, assault - state.Tick))}"
                : $"Next tribute in {UiTheme.FmtDuration(tribute)}";
            _coreChipInfo.style.color = underAttack ? UiTheme.Bad : UiTheme.Dim;
            Widgets.SetBorder(_coreChip, underAttack ? UiTheme.Bad : UiTheme.Good, 1.5f);
        }

        void RefreshHeader(GameState state)
        {
            RefreshQuestTracker(state);
            RefreshEventChip(state);
            RefreshCoreChip(state);
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
