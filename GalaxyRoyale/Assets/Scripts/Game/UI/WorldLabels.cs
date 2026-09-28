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
            public Label Label = null!;
            public string Text = "";
            public int FontSize;
            public Color Color;
            public bool Shown;
        }

        /// <summary>Label box width in panel points; text centers inside it.</summary>
        const float BoxW = 170f;
        static readonly Color Shadow = new(0f, 0f, 0f, 0.95f);

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
            float h = lines * fontSize * 1.22f + 2f;
            float w = Mathf.Min(BoxW, EstimateWidth(text, fontSize) + 6f);
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
            if (e.Text != text) { e.Text = text; e.Label.text = text; }
            if (e.FontSize != fontSize) { e.FontSize = fontSize; e.Label.style.fontSize = fontSize; }
            if (e.Color != color) { e.Color = color; e.Label.style.color = color; }
            // translate = transform only — no layout pass per frame.
            e.Label.style.translate = new Translate(pp.x - BoxW * 0.5f, top);
            if (!e.Shown) { e.Shown = true; e.Label.style.display = DisplayStyle.Flex; }
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
                e.Label.style.display = DisplayStyle.None;
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

            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = 0;
            label.style.top = 0;
            label.style.width = BoxW;
            label.style.unityTextAlign = TextAnchor.UpperCenter;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;
            label.style.paddingLeft = 0;
            label.style.paddingRight = 0;
            label.style.paddingTop = 0;
            label.style.paddingBottom = 0;
            // Dark halo so names read over any planet palette.
            label.style.textShadow = new TextShadow
            {
                offset = new Vector2(0.8f, 0.8f),
                blurRadius = 2.5f,
                color = Shadow,
            };
            _layer.Add(label);

            var entry = new Entry { Label = label, Shown = true };
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
