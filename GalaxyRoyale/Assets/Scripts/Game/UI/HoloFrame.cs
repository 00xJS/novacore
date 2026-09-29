// Neon Hologram chrome (restyle, 2026-09-28): the cut-corner frames and hex
// buttons of the orange Neon Hologram style, painted with Painter2D because UI
// Toolkit has no clip-path. The frame paints on its host, ahead of the host's
// own content (a child element would stop a Button or Label measuring its text,
// so it would collapse to its padding). Holo.Set recolours it: pressed,
// disabled and highlighted buttons, won and lost rows.
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public enum FrameShape
    {
        /// <summary>Top-right and bottom-left corners cut — panels, buttons, cards.</summary>
        Bevel,
        /// <summary>All four corners cut.</summary>
        BevelAll,
        /// <summary>Flat-sided hexagon — the round buttons.</summary>
        Hex,
        Plain,
    }

    public sealed class HoloFrame
    {
        public Color Fill;
        public Color Stroke;
        public float Width = 1f;
        public float Cut = 8f;
        public FrameShape Shape = FrameShape.Bevel;
        /// <summary>Soft outer glow on the stroke (two faint wider passes).</summary>
        public bool Glow;

        readonly VisualElement _host;

        internal HoloFrame(VisualElement host) => _host = host;

        public void MarkDirtyRepaint() => _host.MarkDirtyRepaint();

        public void Set(Color fill, Color stroke)
        {
            if (fill == Fill && stroke == Stroke) return;
            Fill = fill;
            Stroke = stroke;
            _host.MarkDirtyRepaint();
        }

        internal void Paint(MeshGenerationContext mgc)
        {
            // The host's whole box (generated content is in its local space).
            var r = new Rect(0f, 0f, _host.layout.width, _host.layout.height);
            if (float.IsNaN(r.width) || r.width < 2f || r.height < 2f) return;
            var p = mgc.painter2D;
            p.lineJoin = LineJoin.Miter;
            float inset = Width * 0.5f;
            if (Fill.a > 0f)
            {
                Path(p, r, inset);
                p.fillColor = Fill;
                p.Fill();
            }
            if (Stroke.a <= 0f || Width <= 0f) return;
            if (Glow)
            {
                Path(p, r, inset);
                p.strokeColor = new Color(Stroke.r, Stroke.g, Stroke.b, Stroke.a * 0.12f);
                p.lineWidth = Width + 6f;
                p.Stroke();
                p.strokeColor = new Color(Stroke.r, Stroke.g, Stroke.b, Stroke.a * 0.22f);
                p.lineWidth = Width + 3f;
                p.Stroke();
            }
            Path(p, r, inset);
            p.strokeColor = Stroke;
            p.lineWidth = Width;
            p.Stroke();
        }

        void Path(Painter2D p, Rect r, float i)
        {
            float x0 = r.xMin + i, y0 = r.yMin + i, x1 = r.xMax - i, y1 = r.yMax - i;
            float c = Mathf.Min(Cut, (x1 - x0) * 0.45f, (y1 - y0) * 0.45f);
            p.BeginPath();
            switch (Shape)
            {
                case FrameShape.Hex:
                {
                    float k = (x1 - x0) * 0.25f, ym = (y0 + y1) * 0.5f;
                    p.MoveTo(new Vector2(x0 + k, y0));
                    p.LineTo(new Vector2(x1 - k, y0));
                    p.LineTo(new Vector2(x1, ym));
                    p.LineTo(new Vector2(x1 - k, y1));
                    p.LineTo(new Vector2(x0 + k, y1));
                    p.LineTo(new Vector2(x0, ym));
                    break;
                }
                case FrameShape.BevelAll:
                    p.MoveTo(new Vector2(x0 + c, y0));
                    p.LineTo(new Vector2(x1 - c, y0));
                    p.LineTo(new Vector2(x1, y0 + c));
                    p.LineTo(new Vector2(x1, y1 - c));
                    p.LineTo(new Vector2(x1 - c, y1));
                    p.LineTo(new Vector2(x0 + c, y1));
                    p.LineTo(new Vector2(x0, y1 - c));
                    p.LineTo(new Vector2(x0, y0 + c));
                    break;
                case FrameShape.Plain:
                    p.MoveTo(new Vector2(x0, y0));
                    p.LineTo(new Vector2(x1, y0));
                    p.LineTo(new Vector2(x1, y1));
                    p.LineTo(new Vector2(x0, y1));
                    break;
                default:
                    p.MoveTo(new Vector2(x0, y0));
                    p.LineTo(new Vector2(x1 - c, y0));
                    p.LineTo(new Vector2(x1, y0 + c));
                    p.LineTo(new Vector2(x1, y1));
                    p.LineTo(new Vector2(x0 + c, y1));
                    p.LineTo(new Vector2(x0, y1 - c));
                    break;
            }
            p.ClosePath();
        }
    }

    public static class Holo
    {
        static readonly ConditionalWeakTable<VisualElement, HoloFrame> s_frames = new();

        /// <summary>Give <paramref name="host"/> a painted frame (or restyle the one it
        /// has). The host's own background, border and corner radii are cleared.</summary>
        public static HoloFrame Frame(VisualElement host, Color fill, Color stroke, float cut = 8f,
            float width = 1f, FrameShape shape = FrameShape.Bevel, bool glow = false)
        {
            host.style.backgroundColor = Color.clear;
            Widgets.SetBorder(host, Color.clear, 0f);
            host.style.borderTopLeftRadius = 0;
            host.style.borderTopRightRadius = 0;
            host.style.borderBottomLeftRadius = 0;
            host.style.borderBottomRightRadius = 0;
            if (!s_frames.TryGetValue(host, out var frame))
            {
                frame = new HoloFrame(host);
                s_frames.Add(host, frame);
                // Ours first, so the host's text and icons paint on top of the frame.
                host.generateVisualContent = (Action<MeshGenerationContext>)Delegate.Combine(
                    new Action<MeshGenerationContext>(frame.Paint), host.generateVisualContent);
            }
            frame.Fill = fill;
            frame.Stroke = stroke;
            frame.Cut = cut;
            frame.Width = width;
            frame.Shape = shape;
            frame.Glow = glow;
            frame.MarkDirtyRepaint();
            return frame;
        }

        /// <summary>The host's painted frame, if it has one.</summary>
        public static HoloFrame? Of(VisualElement host) => s_frames.TryGetValue(host, out var f) ? f : null;

        /// <summary>Recolour a framed element; plain elements get a background colour.</summary>
        public static void Set(VisualElement host, Color fill, Color stroke)
        {
            var frame = Of(host);
            if (frame != null) frame.Set(fill, stroke);
            else host.style.backgroundColor = fill;
        }

        /// <summary>Recolour only the fill (a won/lost row, a warning card).</summary>
        public static void SetFill(VisualElement host, Color fill)
        {
            var frame = Of(host);
            if (frame != null) frame.Set(fill, frame.Stroke);
            else host.style.backgroundColor = fill;
        }

        /// <summary>Recolour only the stroke.</summary>
        public static void SetStroke(VisualElement host, Color stroke)
        {
            var frame = Of(host);
            if (frame != null) frame.Set(frame.Fill, stroke);
            else Widgets.SetBorder(host, stroke, 1f);
        }
    }
}
