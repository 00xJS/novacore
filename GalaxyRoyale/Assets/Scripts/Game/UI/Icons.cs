// Vector icons painted with UI Toolkit's Painter2D instead of font glyphs.
//
// Why: the runtime font tofu-boxes a lot of symbols on device — the news
// ticker read "□ □ Legacy Codey raided…" because ⚔ has no glyph, and ⓘ / ✓ /
// ⚠ / ☆ are in the same boat. Which symbols survive has been trial-and-error
// (two "tofu sweeps" so far). Painted icons can't tofu, stay crisp at any
// size, and take a live tint, so every icon-on-a-button goes through here.
using UnityEngine;
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public enum Icon
    {
        Swords, Star, StarOutline, Info, Warning, Check,
        ChevronRight, ChevronDown, ChevronLeft, ArrowUp, ArrowDown,
        Close, Menu, More, Search, Rotate, Chart, Dot, Ring, Eye,
        Play, FastForward, Skip,
    }

    /// <summary>A square element that paints one <see cref="Icon"/> in a tint color.</summary>
    public sealed class IconElement : VisualElement
    {
        Icon _icon;
        Color _color;

        public IconElement(Icon icon, Color color)
        {
            _icon = icon;
            _color = color;
            pickingMode = PickingMode.Ignore; // taps go to the owning button
            generateVisualContent += Paint;
        }

        public Icon Icon
        {
            get => _icon;
            set { if (_icon == value) return; _icon = value; MarkDirtyRepaint(); }
        }

        public Color Color
        {
            get => _color;
            set { if (_color == value) return; _color = value; MarkDirtyRepaint(); }
        }

        void Paint(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 1f || r.height < 1f) return;
            float s = Mathf.Min(r.width, r.height);
            var o = new Vector2(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f);
            Icons.Draw(mgc.painter2D, _icon, o, s, _color);
        }
    }

    public static class Icons
    {
        public static IconElement Make(Icon icon, float size, Color color)
        {
            var e = new IconElement(icon, color);
            e.style.width = size;
            e.style.height = size;
            e.style.flexShrink = 0f;
            return e;
        }

        /// <summary>Paints <paramref name="icon"/> into the square at <paramref name="o"/>
        /// with side <paramref name="s"/>. Shapes are authored on a 0..1 grid.</summary>
        public static void Draw(Painter2D p, Icon icon, Vector2 o, float s, Color c)
        {
            Vector2 P(float x, float y) => new(o.x + x * s, o.y + y * s);

            p.strokeColor = c;
            p.fillColor = c;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            float stroke = Mathf.Max(1.2f, s * 0.11f);
            p.lineWidth = stroke;

            void Line(float x0, float y0, float x1, float y1)
            {
                p.BeginPath();
                p.MoveTo(P(x0, y0));
                p.LineTo(P(x1, y1));
                p.Stroke();
            }

            void Path(bool fill, params float[] xy)
            {
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i + 1 < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                if (fill) { p.ClosePath(); p.Fill(); }
                else p.Stroke();
            }

            void Circle(float cx, float cy, float radius, bool fill)
            {
                p.BeginPath();
                p.Arc(P(cx, cy), radius * s, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                if (fill) p.Fill();
                else p.Stroke();
            }

            void Star(bool fill)
            {
                const int points = 5;
                var xy = new float[points * 4];
                for (int i = 0; i < points * 2; i++)
                {
                    float rad = (i % 2 == 0) ? 0.47f : 0.2f;
                    float a = (-90f + i * 36f) * Mathf.Deg2Rad;
                    xy[i * 2] = 0.5f + Mathf.Cos(a) * rad;
                    xy[i * 2 + 1] = 0.54f + Mathf.Sin(a) * rad;
                }
                if (fill) { Path(true, xy); return; }
                p.lineWidth = Mathf.Max(1f, s * 0.08f);
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                p.ClosePath();
                p.Stroke();
            }

            switch (icon)
            {
                case Icon.Swords:
                    p.lineWidth = Mathf.Max(1.2f, s * 0.09f);
                    Line(0.2f, 0.2f, 0.7f, 0.7f);    // blade, top-left to hilt
                    Line(0.58f, 0.78f, 0.78f, 0.58f); // crossguard
                    Line(0.7f, 0.7f, 0.84f, 0.84f);   // grip
                    Line(0.8f, 0.2f, 0.3f, 0.7f);    // blade, top-right to hilt
                    Line(0.42f, 0.78f, 0.22f, 0.58f);
                    Line(0.3f, 0.7f, 0.16f, 0.84f);
                    break;
                case Icon.Star:
                    Star(fill: true);
                    break;
                case Icon.StarOutline:
                    Star(fill: false);
                    break;
                case Icon.Info:
                    p.lineWidth = Mathf.Max(1f, s * 0.08f);
                    Circle(0.5f, 0.5f, 0.42f, fill: false);
                    Circle(0.5f, 0.3f, 0.065f, fill: true);
                    p.lineWidth = Mathf.Max(1.2f, s * 0.1f);
                    Line(0.5f, 0.46f, 0.5f, 0.72f);
                    break;
                case Icon.Warning:
                    p.lineWidth = Mathf.Max(1f, s * 0.08f);
                    p.BeginPath();
                    p.MoveTo(P(0.5f, 0.1f));
                    p.LineTo(P(0.93f, 0.86f));
                    p.LineTo(P(0.07f, 0.86f));
                    p.ClosePath();
                    p.Stroke();
                    p.lineWidth = Mathf.Max(1.2f, s * 0.1f);
                    Line(0.5f, 0.38f, 0.5f, 0.6f);
                    Circle(0.5f, 0.73f, 0.055f, fill: true);
                    break;
                case Icon.Check:
                    p.lineWidth = Mathf.Max(1.4f, s * 0.13f);
                    Path(false, 0.18f, 0.53f, 0.42f, 0.76f, 0.84f, 0.26f);
                    break;
                case Icon.ChevronRight:
                    Path(false, 0.36f, 0.2f, 0.66f, 0.5f, 0.36f, 0.8f);
                    break;
                case Icon.ChevronLeft:
                    Path(false, 0.64f, 0.2f, 0.34f, 0.5f, 0.64f, 0.8f);
                    break;
                case Icon.ChevronDown:
                    Path(false, 0.2f, 0.36f, 0.5f, 0.66f, 0.8f, 0.36f);
                    break;
                case Icon.ArrowUp:
                    Path(true, 0.5f, 0.1f, 0.86f, 0.52f, 0.14f, 0.52f);
                    Line(0.5f, 0.46f, 0.5f, 0.88f);
                    break;
                case Icon.ArrowDown:
                    Path(true, 0.5f, 0.9f, 0.86f, 0.48f, 0.14f, 0.48f);
                    Line(0.5f, 0.54f, 0.5f, 0.12f);
                    break;
                case Icon.Close:
                    Line(0.24f, 0.24f, 0.76f, 0.76f);
                    Line(0.76f, 0.24f, 0.24f, 0.76f);
                    break;
                case Icon.Menu:
                    Line(0.2f, 0.28f, 0.8f, 0.28f);
                    Line(0.2f, 0.5f, 0.8f, 0.5f);
                    Line(0.2f, 0.72f, 0.8f, 0.72f);
                    break;
                case Icon.More:
                    Circle(0.2f, 0.5f, 0.085f, fill: true);
                    Circle(0.5f, 0.5f, 0.085f, fill: true);
                    Circle(0.8f, 0.5f, 0.085f, fill: true);
                    break;
                case Icon.Search:
                    p.lineWidth = Mathf.Max(1.2f, s * 0.1f);
                    Circle(0.43f, 0.43f, 0.26f, fill: false);
                    p.lineWidth = Mathf.Max(1.6f, s * 0.14f);
                    Line(0.63f, 0.63f, 0.85f, 0.85f);
                    break;
                case Icon.Rotate:
                {
                    // 290° arc, clockwise, with an arrowhead at its end.
                    const float r = 0.33f, start = -35f, end = 255f;
                    p.lineWidth = Mathf.Max(1.2f, s * 0.1f);
                    p.lineCap = LineCap.Butt;
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), r * s, Angle.Degrees(start), Angle.Degrees(end));
                    p.Stroke();
                    float a = end * Mathf.Deg2Rad;
                    var tip = new Vector2(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r);
                    var tan = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)); // clockwise travel (y-down)
                    var nrm = new Vector2(tan.y, -tan.x);
                    var head = tip + tan * 0.16f;
                    var b1 = tip + nrm * 0.13f;
                    var b2 = tip - nrm * 0.13f;
                    p.lineCap = LineCap.Round;
                    Path(true, head.x, head.y, b1.x, b1.y, b2.x, b2.y);
                    break;
                }
                case Icon.Chart:
                    Path(true, 0.14f, 0.86f, 0.14f, 0.58f, 0.32f, 0.58f, 0.32f, 0.86f);
                    Path(true, 0.41f, 0.86f, 0.41f, 0.38f, 0.59f, 0.38f, 0.59f, 0.86f);
                    Path(true, 0.68f, 0.86f, 0.68f, 0.16f, 0.86f, 0.16f, 0.86f, 0.86f);
                    break;
                case Icon.Dot:
                    Circle(0.5f, 0.5f, 0.28f, fill: true);
                    break;
                case Icon.Ring:
                    p.lineWidth = Mathf.Max(1f, s * 0.1f);
                    Circle(0.5f, 0.5f, 0.28f, fill: false);
                    break;
                case Icon.Eye:
                    p.lineWidth = Mathf.Max(1f, s * 0.08f);
                    p.BeginPath();
                    p.MoveTo(P(0.06f, 0.5f));
                    p.BezierCurveTo(P(0.3f, 0.14f), P(0.7f, 0.14f), P(0.94f, 0.5f));
                    p.BezierCurveTo(P(0.7f, 0.86f), P(0.3f, 0.86f), P(0.06f, 0.5f));
                    p.ClosePath();
                    p.Stroke();
                    Circle(0.5f, 0.5f, 0.14f, fill: true);
                    break;
                case Icon.Play:
                    Path(true, 0.28f, 0.18f, 0.84f, 0.5f, 0.28f, 0.82f);
                    break;
                case Icon.FastForward:
                    Path(true, 0.1f, 0.22f, 0.5f, 0.5f, 0.1f, 0.78f);
                    Path(true, 0.5f, 0.22f, 0.9f, 0.5f, 0.5f, 0.78f);
                    break;
                case Icon.Skip: // skip to the end: play triangle + bar
                    Path(true, 0.14f, 0.2f, 0.64f, 0.5f, 0.14f, 0.8f);
                    p.lineWidth = Mathf.Max(1.4f, s * 0.13f);
                    Line(0.8f, 0.22f, 0.8f, 0.78f);
                    break;
            }
        }
    }
}
