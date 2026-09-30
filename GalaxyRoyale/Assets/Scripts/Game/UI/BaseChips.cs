// The globe base's chips (2026-09-28): the approved building states as UI
// Toolkit elements pinned to pads — a name chip with a level badge, a timer and
// progress ring while upgrading, BUILD HERE on an open pad, the Command Center
// level a locked pad waits for, planned and reserved pads, and a bubble for
// clan supplies waiting to be collected. A layer under the HUD, like
// WorldLabelLayer; chips are keyed by pad and only move by translate.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Position = UnityEngine.UIElements.Position;

namespace GalaxyRoyale.Game.UI
{
    public sealed class BaseChipLayer
    {
        /// <summary>Fog: just a "?" hex over an uncharted sector of the Wilds (no label).</summary>
        public enum Kind { Name, Timer, Open, Locked, Planned, Online, Reserved, Fog }

        sealed class Chip
        {
            public VisualElement Anchor = null!;
            public VisualElement Body = null!;
            public VisualElement? Mark;
            public Kind Kind;
            public string Text = "";
            public string Sub = "";
            public Color Rim;
            public bool Used;
            public bool Shown = true;
            public Label? Main;
            public Label? Badge;
            public ProgressRing? Ring;
            /// <summary>Where Place put the chip this frame (its top-centre, panel points).</summary>
            public Vector2 At;
            /// <summary>This frame's fade (low for pads turning over the globe's horizon).</summary>
            public float Alpha = 1f;
            /// <summary>The sideways nudge the declutter pass gave the label (px).</summary>
            public float Dx;
        }

        readonly VisualElement _layer;
        readonly Dictionary<string, Chip> _chips = new();
        readonly IReadOnlyList<VisualElement>? _blockers;
        readonly Dictionary<string, Chip> _bubbles = new();
        readonly Dictionary<string, Action> _bubbleTaps = new();

        /// <summary>Where the quick-action ring lives: over the HUD when given.</summary>
        readonly VisualElement? _front;

        public BaseChipLayer(VisualElement root, IReadOnlyList<VisualElement>? blockers, VisualElement? front = null)
        {
            _blockers = blockers;
            _front = front;
            _layer = new VisualElement { name = "base-chips", pickingMode = PickingMode.Ignore };
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0;
            _layer.style.right = 0;
            _layer.style.top = 0;
            _layer.style.bottom = 0;
            _layer.style.overflow = Overflow.Hidden;
            root.Insert(0, _layer); // under every HUD element
        }

        public IPanel? Panel => _layer.panel;

        /// <summary>Screen pixels (y up) to panel points; null when behind the camera.</summary>
        public Vector2? ToPanel(Vector3 screenPx)
        {
            if (screenPx.z < 0f || _layer.panel == null) return null;
            return RuntimePanelUtils.ScreenToPanel(_layer.panel, new Vector2(screenPx.x, Screen.height - screenPx.y));
        }

        public void Begin()
        {
            foreach (var c in _chips.Values) c.Used = false;
            foreach (var b in _bubbles.Values) b.Used = false;
        }

        public void End()
        {
            foreach (var c in _chips.Values)
                if (!c.Used && c.Shown) { c.Shown = false; c.Anchor.style.display = DisplayStyle.None; }
            Declutter();
            foreach (var b in _bubbles.Values)
                if (!b.Used && b.Shown) { b.Shown = false; b.Anchor.style.display = DisplayStyle.None; }
        }

        public void Clear()
        {
            Begin();
            End();
        }

        bool Blocked(Vector2 p)
        {
            if (_blockers == null) return false;
            foreach (var b in _blockers)
            {
                if (b.panel == null || b.resolvedStyle.display == DisplayStyle.None) continue;
                if (b.worldBound.Contains(p)) return true;
            }
            return false;
        }

        /// <summary>Place (or update) the chip for one pad. <paramref name="chipAt"/> is the
        /// chip's top-centre; <paramref name="markAt"/> centres the pad mark (plus, lock, ?)
        /// or the progress ring. Returns false when the HUD covers the spot.</summary>
        public bool Place(string key, Kind kind, Vector2 chipAt, Vector2? markAt, string text, string sub = "",
            Color? rim = null, float progress = 0f, float alpha = 1f)
        {
            if (Blocked(chipAt)) return false;
            if (!_chips.TryGetValue(key, out var c) || c.Kind != kind)
            {
                if (c != null) _layer.Remove(c.Anchor);
                c = Build(kind);
                _chips[key] = c;
            }
            c.Used = true;
            if (!c.Shown) { c.Shown = true; c.Anchor.style.display = DisplayStyle.Flex; }
            var rimColor = rim ?? UiTheme.Accent;
            if (c.Text != text || c.Sub != sub || c.Rim != rimColor)
            {
                c.Text = text;
                c.Sub = sub;
                c.Rim = rimColor;
                if (c.Main != null) c.Main.text = text;
                if (c.Badge != null)
                {
                    c.Badge.text = sub;
                    if (c.Kind == Kind.Name) c.Badge.style.backgroundColor = rimColor;
                }
            }
            c.Ring?.SetProgress(progress);
            // Neighbours' chips peeking out between the ring's buttons made them hard
            // to read — fade whatever sits under the ring (last frame's layout).
            if (_ringRect is { } rr && c.Anchor.childCount > 0 && c.Anchor[0].worldBound.Overlaps(rr))
                alpha *= 0.2f;
            c.Anchor.style.opacity = alpha;
            c.At = chipAt;
            c.Alpha = alpha;
            c.Anchor.style.translate = new Translate(chipAt.x, chipAt.y);
            if (c.Mark != null && markAt is { } m)
                c.Mark.style.translate = new Translate(m.x - chipAt.x, m.y - chipAt.y);
            return true;
        }

        /// <summary>Tappable "ready to collect" bubbles over buildings (clan supplies at
        /// the Warehouse, salvage at the Salvage Yard), keyed by building.</summary>
        public void Bubble(string key, Vector2 bottomCentre, Icon icon, string text, Action onTap)
        {
            if (!_bubbles.TryGetValue(key, out var b))
            {
                b = new Chip { Kind = Kind.Name };
                var anchor = Anchor();
                var body = new VisualElement { pickingMode = PickingMode.Position };
                body.style.position = Position.Absolute; // sized by its content, not the 0-wide anchor
                body.style.left = 0;
                body.style.top = 0;
                body.style.flexDirection = FlexDirection.Row;
                body.style.alignItems = Align.Center;
                body.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
                body.style.paddingLeft = 6;
                body.style.paddingRight = 9;
                body.style.paddingTop = 4;
                body.style.paddingBottom = 4;
                Holo.Frame(body, UiTheme.Energy, UiTheme.Energy, 6f, 1f, FrameShape.BevelAll, glow: true);
                var glyph = Icons.Make(icon, 13, UiTheme.Ink);
                glyph.name = "bubble-icon";
                body.Add(glyph);
                var label = Widgets.Heading("", 11, UiTheme.Ink, 0.6f);
                label.style.marginLeft = 4;
                label.pickingMode = PickingMode.Ignore;
                body.Add(label);
                body.RegisterCallback<ClickEvent>(_ => { if (_bubbleTaps.TryGetValue(key, out var tap)) tap(); });
                anchor.Add(body);
                b.Anchor = anchor;
                b.Body = body;
                b.Main = label;
                AddUnderRing(anchor);
                _bubbles[key] = b;
            }
            _bubbleTaps[key] = onTap;
            b.Used = true;
            if (!b.Shown) { b.Shown = true; b.Anchor.style.display = DisplayStyle.Flex; }
            if (b.Text != text) { b.Text = text; b.Main!.text = text; }
            b.Body.Q<IconElement>("bubble-icon").Icon = icon;
            b.Anchor.style.translate = new Translate(bottomCentre.x, bottomCentre.y);
        }

        // ---------- quick actions (approved state: a tap shows a small ring) ----------

        public enum RingSpot { Above, Below, Over }

        /// <summary>What the ring says under its buttons: an upgrade's price and time,
        /// or a plain line (a running upgrade, max level).</summary>
        public struct RingCost
        {
            public string? Plain;
            public int Level;
            public long Gold, Quartz, Helium;
            public bool GoldShort, QuartzShort, HeliumShort;
            public int Seconds;
        }

        VisualElement? _ring, _costRow;
        /// <summary>Where the ring was last placed (panel points), null while it's hidden.</summary>
        Rect? _ringRect;
        Button? _upgrade, _info, _boost;
        Label? _upgradeCaption, _costPlain, _costLevel, _costGold, _costQuartz, _costHelium, _costTime;
        VisualElement? _costParts;
        Action? _onUpgrade, _onInfo, _onBoost;
        const float RingW = 184f, RingH = 64f, CostH = 20f, Edge = 8f;

        /// <summary>Show the ring for a selected building. It sits above the art, or below
        /// the name chip, or over the art: the first spot the HUD doesn't cover.</summary>
        public void Ring(Vector2 artTop, Vector2 chipBottom, Vector2 artCenter, string upgradeCaption, bool canUpgrade,
            bool canBoost, RingCost cost, Action onUpgrade, Action onInfo, Action onBoost)
        {
            BuildRing();
            _onUpgrade = onUpgrade;
            _onInfo = onInfo;
            _onBoost = onBoost;
            float width = _layer.layout.width > 0 ? _layer.layout.width : UiTheme.W;
            const float block = RingH + CostH;
            Vector2 TopLeft(Vector2 topCentre) => new(
                Mathf.Clamp(topCentre.x, RingW * 0.5f + Edge, width - RingW * 0.5f - Edge) - RingW * 0.5f, topCentre.y);
            var spots = new[]
            {
                TopLeft(new Vector2(artTop.x, artTop.y - block - 2f)),
                TopLeft(new Vector2(chipBottom.x, chipBottom.y + 4f)),
                TopLeft(new Vector2(artCenter.x, artCenter.y - block * 0.5f)),
            };
            var at = spots[0];
            foreach (var spot in spots)
                if (!BlockedRect(new Rect(spot.x, spot.y, RingW, block))) { at = spot; break; }
            _ringRect = new Rect(at.x, at.y, RingW, block);
            _ring!.style.translate = new Translate(at.x, at.y);
            _ring.style.display = DisplayStyle.Flex;
            if (_upgradeCaption!.text != upgradeCaption) _upgradeCaption.text = upgradeCaption;
            RingLook(_upgrade!, canUpgrade);
            RingLook(_boost!, canBoost);
            RingLook(_info!, true);

            // The cost row: centred under the buttons, kept on screen (its width is last frame's).
            bool plain = cost.Plain != null;
            _costPlain!.style.display = plain ? DisplayStyle.Flex : DisplayStyle.None;
            _costParts!.style.display = plain ? DisplayStyle.None : DisplayStyle.Flex;
            if (plain) SetText(_costPlain, cost.Plain!, UiTheme.Text);
            else
            {
                SetText(_costLevel!, $"LV {cost.Level}", UiTheme.Accent);
                SetPart(_costGold!, cost.Gold, cost.GoldShort);
                SetPart(_costQuartz!, cost.Quartz, cost.QuartzShort);
                SetPart(_costHelium!, cost.Helium, cost.HeliumShort);
                SetText(_costTime!, UiTheme.FmtDuration(cost.Seconds), UiTheme.Text);
            }
            float w = _costRow!.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0f) w = 200f;
            float cx = at.x + RingW * 0.5f;
            float left = Mathf.Clamp(cx - w * 0.5f, Edge, Mathf.Max(Edge, width - w - Edge));
            _costRow.style.translate = new Translate(left, at.y + RingH);
            _costRow.style.display = DisplayStyle.Flex;
        }

        static void SetText(Label l, string text, Color color)
        {
            if (l.text != text) l.text = text;
            l.style.color = color;
        }

        static void SetPart(Label l, long milli, bool isShort)
        {
            var part = l.parent;
            part.style.display = milli > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            SetText(l, Compact(milli), isShort ? UiTheme.Bad : UiTheme.Text);
        }

        /// <summary>Milli-units as a short amount: 656, 3.3K, 1.2M.</summary>
        /// <summary>Labels that overlap side by side are pushed apart, and any that would
        /// cross the screen's edge are pulled back in (user 2026-09-29: names ran into
        /// each other in the Command district and the Frontier). Works from last
        /// frame's label widths, so it settles within a frame of a label changing;
        /// each frame starts again from the pads' own positions, so nothing drifts.</summary>
        void Declutter()
        {
            _declutter.Clear();
            foreach (var c in _chips.Values)
            {
                if (!c.Used || !c.Shown || c.Kind == Kind.Fog) continue;
                // Pads over the horizon (faded) or off the screen keep their own place:
                // pulling them in would drag half-hidden labels into view.
                if (c.Alpha < 0.6f) { Reset(c); continue; }
                var size = c.Body.layout;
                if (float.IsNaN(size.width) || size.width <= 0f) continue;
                _declutter.Add((c, c.At.x, size.width, size.height));
            }
            int n = _declutter.Count;
            if (n == 0) return;
            var x = new float[n];
            for (int i = 0; i < n; i++) x[i] = _declutter[i].x;
            const float Gap = 4f;
            for (int pass = 0; pass < 6; pass++)
            {
                bool moved = false;
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        var a = _declutter[i];
                        var b = _declutter[j];
                        // Only labels sharing a line: their heights overlap.
                        if (a.c.At.y >= b.c.At.y + b.h || b.c.At.y >= a.c.At.y + a.h) continue;
                        bool aLeft = x[i] <= x[j];
                        float need = (a.w + b.w) * 0.5f + Gap - Mathf.Abs(x[j] - x[i]);
                        if (need <= 0f) continue;
                        float half = need * 0.5f;
                        x[i] += aLeft ? -half : half;
                        x[j] += aLeft ? half : -half;
                        moved = true;
                    }
                if (!moved) break;
            }
            float width = _layer.layout.width > 0 ? _layer.layout.width : UiTheme.W;
            for (int i = 0; i < n; i++)
            {
                var (c, x0, w, _) = _declutter[i];
                float half = w * 0.5f;
                bool onScreen = x0 >= 0f && x0 <= width;
                float cx = onScreen ? Mathf.Clamp(x[i], half + Edge, Mathf.Max(half + Edge, width - half - Edge)) : x[i];
                float dx = Mathf.Round(cx - x0);
                if (Mathf.Abs(dx - c.Dx) < 0.5f) continue;
                c.Dx = dx;
                c.Body.style.left = dx;
            }
        }

        readonly List<(Chip c, float x, float w, float h)> _declutter = new();

        static void Reset(Chip c)
        {
            if (c.Dx == 0f) return;
            c.Dx = 0f;
            c.Body.style.left = 0;
        }

        static string Compact(long milli)
        {
            long whole = milli / 1000;
            if (whole < 1000) return whole.ToString();
            if (whole < 1_000_000) return $"{whole / 1000.0:0.#}K";
            if (whole < 1_000_000_000) return $"{whole / 1_000_000.0:0.#}M";
            return $"{whole / 1_000_000_000.0:0.#}B";
        }

        public void HideRing()
        {
            _ringRect = null;
            if (_ring != null && _ring.style.display != DisplayStyle.None) _ring.style.display = DisplayStyle.None;
            if (_costRow != null && _costRow.style.display != DisplayStyle.None) _costRow.style.display = DisplayStyle.None;
        }

        bool BlockedRect(Rect r)
        {
            if (_blockers == null) return false;
            foreach (var b in _blockers)
            {
                if (b.panel == null || b.resolvedStyle.display == DisplayStyle.None) continue;
                var wb = b.worldBound;
                if (wb.width > 0f && wb.height > 0f && wb.Overlaps(r)) return true;
            }
            return false;
        }

        void BuildRing()
        {
            if (_ring != null) return;
            _ring = new VisualElement { name = "quick-actions", pickingMode = PickingMode.Ignore };
            _ring.style.position = Position.Absolute;
            _ring.style.left = 0;
            _ring.style.top = 0;
            _ring.style.width = RingW;
            _ring.style.height = RingH;
            // A gentle arc: the middle button stands a little higher.
            _upgrade = RingButton(Icon.ArrowUp, "UPGRADE", 2f, 14f, () => _onUpgrade?.Invoke(), out _upgradeCaption);
            _info = RingButton(Icon.Info, "INFO", RingW * 0.5f - 29f, 0f, () => _onInfo?.Invoke(), out _);
            _boost = RingButton(Icon.Bolt, "BOOST", RingW - 60f, 14f, () => _onBoost?.Invoke(), out _);
            _ring.style.display = DisplayStyle.None;
            (_front ?? _layer).Add(_ring);

            // Price and time: level, then gold, quartz and helium with their icons, then time.
            _costRow = new VisualElement { name = "quick-cost", pickingMode = PickingMode.Ignore };
            _costRow.style.position = Position.Absolute;
            _costRow.style.left = 0;
            _costRow.style.top = 0;
            _costRow.style.height = CostH;
            _costRow.style.flexDirection = FlexDirection.Row;
            _costRow.style.alignItems = Align.Center;
            _costRow.style.paddingLeft = 7;
            _costRow.style.paddingRight = 8;
            Holo.Frame(_costRow, RingFill, UiTheme.A(UiTheme.Accent, 0.5f), 5f);
            Label CostLabel(VisualElement parent, int size, Color color, bool display)
            {
                var l = display ? Widgets.Heading("", size, color, 0.6f) : Widgets.Text("", size, color, bold: true);
                l.pickingMode = PickingMode.Ignore;
                l.style.whiteSpace = WhiteSpace.NoWrap;
                parent.Add(l);
                return l;
            }
            _costPlain = CostLabel(_costRow, 10, UiTheme.Text, true);
            _costParts = new VisualElement { pickingMode = PickingMode.Ignore };
            _costParts.style.flexDirection = FlexDirection.Row;
            _costParts.style.alignItems = Align.Center;
            _costRow.Add(_costParts);
            _costLevel = CostLabel(_costParts, 10, UiTheme.Accent, true);
            Label Part(Icon icon, Color tint)
            {
                var part = new VisualElement { pickingMode = PickingMode.Ignore };
                part.style.flexDirection = FlexDirection.Row;
                part.style.alignItems = Align.Center;
                part.style.marginLeft = 7;
                var glyph = Icons.Make(icon, 11, tint);
                glyph.style.marginRight = 2;
                part.Add(glyph);
                _costParts!.Add(part);
                return CostLabel(part, 11, UiTheme.Text, false);
            }
            _costGold = Part(Icon.Coin, UiTheme.Gold);
            _costQuartz = Part(Icon.Crystal, UiTheme.Quartz);
            _costHelium = Part(Icon.Drop, UiTheme.Helium);
            _costTime = Part(Icon.Clock, UiTheme.Dim);
            _costRow.style.display = DisplayStyle.None;
            (_front ?? _layer).Add(_costRow);
        }

        /// <summary>Chips and bubbles made after the quick-action ring (a pad turning into a
        /// timer, a district rotated into view) used to be appended over it, so
        /// UPGRADE / INFO / BOOST hid behind neighbouring name chips. New ones go under it.</summary>
        void AddUnderRing(VisualElement anchor)
        {
            if (_ring != null && _ring.parent == _layer) _layer.Insert(_layer.IndexOf(_ring), anchor);
            else _layer.Add(anchor);
        }

        Button RingButton(Icon icon, string caption, float left, float top, Action onTap, out Label captionLabel)
        {
            var b = new Button(onTap) { text = "" };
            b.clicked += GameAudio.Tap;
            b.style.position = Position.Absolute;
            b.style.left = left;
            b.style.top = top;
            b.style.width = 58;
            b.style.height = 52;
            b.style.marginLeft = 0;
            b.style.marginRight = 0;
            b.style.marginTop = 0;
            b.style.marginBottom = 0;
            b.style.paddingLeft = 0;
            b.style.paddingRight = 0;
            b.style.paddingTop = 0;
            b.style.paddingBottom = 0;
            b.style.flexDirection = FlexDirection.Column;
            b.style.justifyContent = Justify.Center;
            b.style.alignItems = Align.Center;
            Holo.Frame(b, RingFill, UiTheme.Accent, 0f, 1.5f, FrameShape.Hex, glow: true);
            b.Add(Icons.Make(icon, 17, UiTheme.Accent));
            captionLabel = Widgets.Heading(caption, 7, UiTheme.Accent, 0.2f);
            captionLabel.name = "caption";
            captionLabel.pickingMode = PickingMode.Ignore;
            captionLabel.style.marginTop = 1;
            b.Add(captionLabel);
            _ring!.Add(b);
            return b;
        }

        static readonly Color RingDim = new(0.62f, 0.7f, 0.85f, 0.7f);
        // Solid fills: at 80–90% the art and name chips behind showed through the
        // buttons, which sit over the row of buildings behind the selection.
        static readonly Color RingFill = new(0.05f, 0.03f, 0.12f, 1f);
        static readonly Color RingFillDim = new(0.04f, 0.03f, 0.08f, 1f);

        /// <summary>Dim, but still tappable: a dim UPGRADE or BOOST says why it can't.</summary>
        static void RingLook(Button b, bool active)
        {
            var c = active ? UiTheme.Accent : RingDim;
            Holo.Set(b, active ? RingFill : RingFillDim, c);
            b.Query<IconElement>().ForEach(i => i.Color = c);
            var label = b.Q<Label>("caption");
            if (label != null) label.style.color = c;
        }

        VisualElement Anchor()
        {
            var anchor = new VisualElement { pickingMode = PickingMode.Ignore };
            anchor.style.position = Position.Absolute;
            anchor.style.left = 0;
            anchor.style.top = 0;
            anchor.style.width = 0;
            anchor.style.height = 0;
            anchor.style.overflow = Overflow.Visible;
            return anchor;
        }

        Chip Build(Kind kind)
        {
            var c = new Chip { Kind = kind };
            c.Anchor = Anchor();
            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.style.position = Position.Absolute;
            body.style.flexDirection = FlexDirection.Row;
            body.style.alignItems = Align.Center;
            body.style.translate = new Translate(Length.Percent(-50), 0);
            body.style.left = 0;
            body.style.top = 0;
            body.style.paddingTop = 2;
            body.style.paddingBottom = 2;
            body.style.paddingLeft = 3;
            body.style.paddingRight = 8;
            c.Body = body;

            Label Main(int size, Color color, bool display)
            {
                var l = display ? Widgets.Heading("", size, color, 1.2f) : Widgets.Text("", size, color, bold: true);
                l.pickingMode = PickingMode.Ignore;
                l.style.whiteSpace = WhiteSpace.NoWrap;
                return l;
            }

            switch (kind)
            {
                case Kind.Name:
                {
                    Holo.Frame(body, new Color(0.03f, 0.016f, 0.09f, 0.86f), UiTheme.A(UiTheme.Accent, 0.55f), 5f);
                    var badge = Widgets.Heading("", 10, UiTheme.Ink, 0f);
                    badge.pickingMode = PickingMode.Ignore;
                    badge.style.minWidth = 17;
                    badge.style.height = 16;
                    badge.style.paddingLeft = 3;
                    badge.style.paddingRight = 3;
                    badge.style.unityTextAlign = TextAnchor.MiddleCenter;
                    badge.style.marginRight = 5;
                    body.Add(badge);
                    c.Badge = badge;
                    c.Main = Main(10, UiTheme.Text, false);
                    body.Add(c.Main);
                    break;
                }
                case Kind.Timer:
                {
                    body.style.paddingLeft = 7;
                    Holo.Frame(body, new Color(0.03f, 0.016f, 0.09f, 0.9f), UiTheme.Accent, 5f);
                    var clock = Icons.Make(Icon.Clock, 12, UiTheme.Accent);
                    clock.style.marginRight = 4;
                    body.Add(clock);
                    c.Main = Main(10, UiTheme.Accent, true);
                    body.Add(c.Main);
                    var ring = new ProgressRing(44f);
                    ring.style.position = Position.Absolute;
                    ring.style.left = -22;
                    ring.style.top = -22;
                    c.Ring = ring;
                    c.Mark = ring;
                    break;
                }
                case Kind.Open:
                {
                    body.style.paddingLeft = 8;
                    Holo.Frame(body, UiTheme.A(UiTheme.Accent, 0.2f), UiTheme.Accent, 0f, 1f, FrameShape.Plain);
                    c.Main = Main(9, UiTheme.Accent, true);
                    body.Add(c.Main);
                    var plus = new VisualElement { pickingMode = PickingMode.Ignore };
                    plus.style.position = Position.Absolute;
                    plus.style.width = 30;
                    plus.style.height = 30;
                    plus.style.left = -15;
                    plus.style.top = -15;
                    plus.style.justifyContent = Justify.Center;
                    plus.style.alignItems = Align.Center;
                    Holo.Frame(plus, new Color(0.03f, 0.016f, 0.09f, 0.6f), UiTheme.Accent, 0f, 1.5f, FrameShape.Hex, glow: true);
                    plus.Add(Icons.Make(Icon.Plus, 16, UiTheme.Accent));
                    c.Mark = plus;
                    break;
                }
                case Kind.Locked:
                {
                    body.style.paddingLeft = 6;
                    Holo.Frame(body, new Color(0.02f, 0.01f, 0.05f, 0.85f), new Color(0.31f, 0.35f, 0.5f), 5f);
                    var lockIcon = Icons.Make(Icon.Lock, 11, new Color(0.54f, 0.59f, 0.72f));
                    lockIcon.style.marginRight = 4;
                    body.Add(lockIcon);
                    c.Main = Main(9, new Color(0.54f, 0.59f, 0.72f), true);
                    body.Add(c.Main);
                    var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                    mark.style.position = Position.Absolute;
                    mark.style.width = 24;
                    mark.style.height = 22;
                    mark.style.left = -12;
                    mark.style.top = -11;
                    mark.style.justifyContent = Justify.Center;
                    mark.style.alignItems = Align.Center;
                    Holo.Frame(mark, new Color(0.02f, 0.01f, 0.05f, 0.8f), Color.clear, 0f, 0f, FrameShape.Hex);
                    mark.Add(Icons.Make(Icon.Lock, 13, new Color(0.54f, 0.59f, 0.72f)));
                    c.Mark = mark;
                    break;
                }
                case Kind.Planned:
                case Kind.Online:
                {
                    bool online = kind == Kind.Online;
                    var tone = online ? UiTheme.Accent : UiTheme.Magenta;
                    body.style.paddingLeft = 6;
                    Holo.Frame(body, new Color(0.03f, 0.016f, 0.09f, 0.88f), UiTheme.A(tone, 0.75f), 5f);
                    c.Main = Main(10, UiTheme.Text, false);
                    body.Add(c.Main);
                    var tag = Widgets.Heading("", 9, tone, 0.8f);
                    tag.pickingMode = PickingMode.Ignore;
                    tag.style.marginLeft = 5;
                    body.Add(tag);
                    c.Badge = tag; // the tag text rides in Sub
                    break;
                }
                case Kind.Fog:
                {
                    body.style.display = DisplayStyle.None;
                    var fog = new VisualElement { pickingMode = PickingMode.Ignore };
                    fog.style.position = Position.Absolute;
                    fog.style.width = 26;
                    fog.style.height = 24;
                    fog.style.left = -13;
                    fog.style.top = -12;
                    fog.style.justifyContent = Justify.Center;
                    fog.style.alignItems = Align.Center;
                    Holo.Frame(fog, new Color(0.02f, 0.01f, 0.05f, 0.55f), new Color(0.44f, 0.5f, 0.66f, 0.5f), 0f, 1f, FrameShape.Hex);
                    fog.Add(Icons.Make(Icon.Question, 13, new Color(0.54f, 0.6f, 0.76f)));
                    c.Mark = fog;
                    break;
                }
                case Kind.Reserved:
                {
                    body.style.paddingLeft = 8;
                    Holo.Frame(body, new Color(0.02f, 0.01f, 0.05f, 0.7f), new Color(0.31f, 0.35f, 0.5f, 0.8f), 0f, 1f, FrameShape.Plain);
                    c.Main = Main(8, new Color(0.54f, 0.59f, 0.72f), true);
                    body.Add(c.Main);
                    var q = new VisualElement { pickingMode = PickingMode.Ignore };
                    q.style.position = Position.Absolute;
                    q.style.width = 24;
                    q.style.height = 22;
                    q.style.left = -12;
                    q.style.top = -11;
                    q.style.justifyContent = Justify.Center;
                    q.style.alignItems = Align.Center;
                    Holo.Frame(q, new Color(0.02f, 0.01f, 0.05f, 0.6f), Color.clear, 0f, 0f, FrameShape.Hex);
                    q.Add(Icons.Make(Icon.Question, 14, new Color(0.44f, 0.5f, 0.66f)));
                    c.Mark = q;
                    break;
                }
            }
            c.Anchor.Add(body);
            if (c.Mark != null) c.Anchor.Add(c.Mark);
            AddUnderRing(c.Anchor);
            return c;
        }
    }

    /// <summary>A painted progress ring (upgrading buildings).</summary>
    public sealed class ProgressRing : VisualElement
    {
        float _progress;
        readonly Label _label;

        public ProgressRing(float size)
        {
            pickingMode = PickingMode.Ignore;
            style.width = size;
            style.height = size;
            style.justifyContent = Justify.Center;
            style.alignItems = Align.Center;
            _label = Widgets.Heading("", 10, UiTheme.Text, 0f);
            _label.pickingMode = PickingMode.Ignore;
            Add(_label);
            generateVisualContent += Paint;
        }

        public void SetProgress(float p)
        {
            p = Mathf.Clamp01(p);
            if (Mathf.Abs(p - _progress) < 0.004f && _label.text.Length > 0) return;
            _progress = p;
            _label.text = $"{Mathf.RoundToInt(p * 100)}%";
            MarkDirtyRepaint();
        }

        void Paint(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 4f) return;
            var p = mgc.painter2D;
            var c = r.center;
            float radius = Mathf.Min(r.width, r.height) * 0.5f;
            p.fillColor = new Color(0.03f, 0.016f, 0.09f, 0.85f);
            p.BeginPath();
            p.Arc(c, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
            float ringR = radius - 5f;
            p.lineWidth = 3.5f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.14f);
            p.BeginPath();
            p.Arc(c, ringR, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
            if (_progress <= 0f) return;
            p.lineCap = LineCap.Round;
            p.strokeColor = UiTheme.Accent;
            p.BeginPath();
            p.Arc(c, ringR, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * _progress));
            p.Stroke();
        }
    }
}
