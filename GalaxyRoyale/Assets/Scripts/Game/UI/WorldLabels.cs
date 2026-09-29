// World-anchored text — building names on the home planet, commander names on
// the galaxy map — as retained UI Toolkit labels in a layer BEHIND the HUD.
//
// Replaces the IMGUI OnGUI labels, which:
//  * drew on top of every button (user report: text printed over the round
//    HUD buttons, and rival names over the commander pill in the header),
//  * sized fonts in raw screen PIXELS — "14" renders ~5pt on a 3x iPhone,
//  * piled on top of each other when markers sat close together,
//  * and re-ran IMGUI's layout + repaint passes several times a frame.
// The header / nav / FABs now simply occlude the layer (each layer is inserted
// as the root's first child), sizes are panel points that scale with the
// screen, and Place() declutters: a label that would overlap one already
// placed this frame is skipped — so callers place in priority order.
//
// Neon Hologram tags (2026-09-29): each label is a small dark-glass chip edged
// in its colour, the first line in Orbitron and any further line under it in
// the body font — like the globe base's chips.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Position = UnityEngine.UIElements.Position;

namespace GalaxyRoyale.Game.UI
{
    public sealed class WorldLabelLayer
    {
        sealed class Entry
        {
            public VisualElement Anchor = null!;
            public VisualElement Chip = null!;
            public Label Title = null!;
            public Label Sub = null!;
            public string Text = "";
            public int FontSize;
            public Color Color;
            public bool Above;
            public bool Shown;
        }

        /// <summary>The widest a tag gets (panel points); a longer second line wraps.</summary>
        const float MaxW = 240f;
        const float PadX = 8f, PadY = 3f;
        const float TitleTracking = 1.2f;
        static readonly Color Glass = new(0.02f, 0.01f, 0.06f, 0.8f);

        readonly VisualElement _layer;
        readonly List<Entry> _pool = new();
        readonly List<Rect> _placed = new();
        /// <summary>HUD elements (header, nav, round buttons, callouts) a label
        /// must not touch. The HUD already paints over the layer, but a name
        /// half-tucked behind a button read as clipped text ("War…e" peeking
        /// around the MORE button) — so overlapping labels are skipped outright.</summary>
        readonly IReadOnlyList<VisualElement>? _blockers;
        int _used;

        public WorldLabelLayer(VisualElement root, string name, IReadOnlyList<VisualElement>? blockers = null)
        {
            _blockers = blockers;
            _layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            _layer.style.position = Position.Absolute;
            _layer.style.left = 0;
            _layer.style.right = 0;
            _layer.style.top = 0;
            _layer.style.bottom = 0;
            _layer.style.overflow = Overflow.Hidden;
            root.Insert(0, _layer); // first child → painted under every HUD element
        }

        /// <summary>Start a frame: forget last frame's placements.</summary>
        public void Begin()
        {
            _used = 0;
            _placed.Clear();
        }

        /// <summary>
        /// Place one label at a camera screen point (Camera.WorldToScreenPoint:
        /// pixels, y-up). <paramref name="above"/> puts the label's bottom edge on
        /// the point (planet names); otherwise its top edge (building captions).
        /// Returns false when it was culled (behind camera, off-panel, or it would
        /// collide with a label placed earlier this frame).
        /// </summary>
        public bool Place(Vector3 screenPx, string text, int fontSize, Color color, bool above,
            bool declutter = true)
        {
            if (screenPx.z < 0f || text.Length == 0) return false;
            var panel = _layer.panel;
            if (panel == null) return false;

            var pp = RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(screenPx.x, Screen.height - screenPx.y));
            int lines = 1;
            foreach (char ch in text) if (ch == '\n') lines++;
            float h = lines * fontSize * 1.3f + PadY * 2f + 2f;
            float w = Mathf.Min(MaxW, EstimateWidth(text, fontSize) + PadX * 2f);
            float top = above ? pp.y - h : pp.y;
            var rect = new Rect(pp.x - w * 0.5f, top, w, h);

            var bounds = _layer.layout;
            if (rect.xMax < 0f || rect.xMin > bounds.width || rect.yMax < 0f || rect.yMin > bounds.height)
                return false;
            if (_blockers != null)
            {
                foreach (var b in _blockers)
                {
                    if (b.panel == null || b.resolvedStyle.display == DisplayStyle.None) continue;
                    var wb = b.worldBound; // panel space, same as rect
                    if (wb.width > 0f && wb.height > 0f && wb.Overlaps(rect)) return false;
                }
            }
            if (declutter)
            {
                var padded = new Rect(rect.x - 2f, rect.y - 1f, rect.width + 4f, rect.height + 2f);
                foreach (var r in _placed)
                    if (r.Overlaps(padded)) return false;
            }
            _placed.Add(rect);

            var e = Next();
            if (e.Text != text)
            {
                e.Text = text;
                int cut = text.IndexOf('\n');
                e.Title.text = cut < 0 ? text : text.Substring(0, cut);
                // Text measurement leaves out letter-spacing, so the title is
                // padded by its tracking: the chip leaves room for the tracked
                // line instead of wrapping it ("Commande / r").
                float track = TitleTracking * e.Title.text.Length * 0.5f;
                e.Title.style.paddingLeft = track;
                e.Title.style.paddingRight = track;
                e.Sub.text = cut < 0 ? "" : text.Substring(cut + 1);
                e.Sub.style.display = cut < 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }
            if (e.FontSize != fontSize)
            {
                e.FontSize = fontSize;
                e.Title.style.fontSize = Widgets.Sized(fontSize * 0.86f);
                e.Sub.style.fontSize = Widgets.Sized(fontSize - 1);
            }
            if (e.Color != color)
            {
                e.Color = color;
                e.Title.style.color = color;
                Holo.SetStroke(e.Chip, new Color(color.r, color.g, color.b, 0.8f));
            }
            if (e.Above != above)
            {
                e.Above = above;
                e.Chip.style.translate = new Translate(Length.Percent(-50), above ? Length.Percent(-100) : 0);
            }
            // translate = transform only — no layout pass per frame.
            e.Anchor.style.translate = new Translate(pp.x, pp.y);
            if (!e.Shown) { e.Shown = true; e.Anchor.style.display = DisplayStyle.Flex; }
            return true;
        }

        /// <summary>Finish a frame: hide pooled labels that weren't placed.</summary>
        public void End()
        {
            for (int i = _used; i < _pool.Count; i++)
            {
                var e = _pool[i];
                if (!e.Shown) continue;
                e.Shown = false;
                e.Anchor.style.display = DisplayStyle.None;
            }
        }

        /// <summary>Hide everything (view switched away, labels off).</summary>
        public void Clear()
        {
            Begin();
            End();
        }

        Entry Next()
        {
            if (_used < _pool.Count) return _pool[_used++];

            var anchor = new VisualElement { pickingMode = PickingMode.Ignore };
            anchor.style.position = Position.Absolute;
            anchor.style.left = 0;
            anchor.style.top = 0;
            var chip = new VisualElement { pickingMode = PickingMode.Ignore };
            chip.style.position = Position.Absolute; // sized by its text, not the 0-wide anchor
            chip.style.left = 0;
            chip.style.top = 0;
            chip.style.maxWidth = MaxW;
            chip.style.alignItems = Align.Stretch; // lines span the chip, centred
            chip.style.paddingLeft = PadX;
            chip.style.paddingRight = PadX;
            chip.style.paddingTop = PadY;
            chip.style.paddingBottom = PadY;
            chip.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100));
            Holo.Frame(chip, Glass, UiTheme.A(UiTheme.Accent, 0.8f), 5f, 1f, FrameShape.BevelAll);
            var title = Widgets.Heading("", 10, UiTheme.Text, TitleTracking);
            title.pickingMode = PickingMode.Ignore;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            chip.Add(title);
            var sub = Widgets.Text("", 9, UiTheme.Dim, bold: true);
            sub.pickingMode = PickingMode.Ignore;
            sub.style.whiteSpace = WhiteSpace.Normal;
            sub.style.unityTextAlign = TextAnchor.MiddleCenter;
            sub.style.marginTop = 1;
            chip.Add(sub);
            anchor.Add(chip);
            _layer.Add(anchor);

            var entry = new Entry { Anchor = anchor, Chip = chip, Title = title, Sub = sub, Shown = true, Above = true };
            _pool.Add(entry);
            _used++;
            return entry;
        }

        /// <summary>Cheap width estimate (bold UI font averages ~0.58em per glyph).</summary>
        static float EstimateWidth(string text, int fontSize)
        {
            int longest = 0, current = 0;
            foreach (char ch in text)
            {
                if (ch == '\n') { if (current > longest) longest = current; current = 0; }
                else current++;
            }
            if (current > longest) longest = current;
            return longest * fontSize * 0.58f;
        }
    }
}
