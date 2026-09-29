// Ship & research pictures (user request: "photo/placeholders" for every ship
// and research, for another layer of detail).
//
// Same drop-in pattern as Resources/Buildings, Map and Avatars: put a PNG at
//   Resources/Ships/<HullId>.png      e.g. Resources/Ships/Leviathan.png
//   Resources/Research/<TechId>.png   e.g. Resources/Research/IonThrusters.png
// and it replaces the placeholder everywhere, zero code changes. (Raw renders
// with a baked checkerboard can go through GalaxyRoyale → Process Ship &
// Research Art first.) All 23 hulls have renders now (the art upgrade,
// 2026-09-29: lit metal hulls in the Neon Hologram palette, top-down, nose up);
// a hull without one falls back to a painted top-down SCHEMATIC generated from
// a per-hull shape table, and every tech without art gets a category emblem.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Game.UI
{
    public static class ShipArt
    {
        static readonly Dictionary<HullId, Texture2D?> _photos = new();

        /// <summary>Drop-in render for a hull, or null → schematic placeholder.</summary>
        public static Texture2D? Photo(HullId hull)
        {
            if (!_photos.TryGetValue(hull, out var tex))
            {
                tex = UnityEngine.Resources.Load<Texture2D>($"Ships/{hull}");
                _photos[hull] = tex;
            }
            return tex;
        }

        /// <summary>Enemy ships in reports and replays: the same art, warmed red.</summary>
        public static readonly Color EnemyTint = new(1f, 0.74f, 0.76f);

        /// <summary>How big a hull draws beside the others: a fighter is 0.85, the
        /// heaviest destroyers 1.8 (it follows hull points).</summary>
        public static float Bulk(HullId hull) =>
            Mathf.Clamp(0.85f + 0.22f * Mathf.Log(Ships.Defs[hull].Hp / 60f, 2f), 0.75f, 1.8f);

        /// <summary>
        /// The hull's art on its own, no card: the render when there is one,
        /// otherwise the schematic. Enemy ships face down and are warmed red.
        /// <paramref name="size"/> 0 leaves the size to the caller.
        /// </summary>
        public static VisualElement Sprite(HullId hull, float size, bool enemy = false)
        {
            var photo = Photo(hull);
            VisualElement e;
            if (photo != null)
            {
                e = new VisualElement();
                e.style.backgroundImage = new StyleBackground(photo);
                e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                if (enemy) e.style.unityBackgroundImageTintColor = EnemyTint;
            }
            else e = new ShipSchematic(hull);
            e.pickingMode = PickingMode.Ignore;
            e.style.flexShrink = 0f;
            if (size > 0f)
            {
                e.style.width = size;
                e.style.height = size;
            }
            if (enemy) e.style.rotate = new StyleRotate(new Rotate(Angle.Degrees(180f)));
            return e;
        }

        /// <summary>Role tint used for frames, stripes and shield rings.</summary>
        public static Color RoleColor(HullId hull) => ShipLore.RoleOf(hull) switch
        {
            "Striker" => new Color(1f, 0.52f, 0.42f),
            "Guardian" => new Color(0.45f, 0.72f, 1f),
            "Skirmisher" => new Color(0.5f, 0.92f, 0.6f),
            "Recon" => new Color(0.78f, 0.64f, 1f),
            _ => new Color(1f, 0.8f, 0.4f), // support: freighters, frigates, salvage
        };

        /// <summary>
        /// A framed picture of the hull: the drop-in render when there is one,
        /// otherwise the painted schematic on a blueprint grid.
        /// </summary>
        public static VisualElement Card(HullId hull, float width, float height)
        {
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            // width <= 0 → stretch to the container (detail-card hero image).
            if (width > 0f) card.style.width = width;
            else card.style.width = Length.Percent(100f);
            card.style.height = height;
            card.style.flexShrink = 0f;
            card.style.overflow = Overflow.Hidden;
            var role = RoleColor(hull);
            // Neon Hologram (2026-09-29): a cut-corner frame edged in the hull's role
            // colour, a soft pool of that colour behind the ship, the art on top.
            float side = width > 0f ? Mathf.Min(width, height) : height;
            Holo.Frame(card, new Color(0.03f, 0.02f, 0.08f, 0.92f), new Color(role.r, role.g, role.b, 0.75f),
                Mathf.Clamp(side * 0.14f, 4f, 14f), 1f, FrameShape.BevelAll);
            card.Add(Holo.Glow(new Color(role.r, role.g, role.b, 0.3f), 8f, 10f, 84f, 84f));

            var photo = Photo(hull);
            if (photo != null)
            {
                var art = Sprite(hull, 0f);
                art.style.position = Position.Absolute;
                art.style.left = Length.Percent(6f);
                art.style.right = Length.Percent(6f);
                art.style.top = Length.Percent(6f);
                art.style.bottom = Length.Percent(6f);
                card.Add(art);
                return card;
            }

            var schematic = new ShipSchematic(hull, grid: true) { pickingMode = PickingMode.Ignore };
            schematic.style.position = Position.Absolute;
            schematic.style.left = 0;
            schematic.style.right = 0;
            schematic.style.top = 0;
            schematic.style.bottom = 0;
            card.Add(schematic);
            return card;
        }
    }

    /// <summary>Painted top-down silhouette of a hull (placeholder art).</summary>
    public sealed class ShipSchematic : VisualElement
    {
        /// <summary>
        /// Silhouette recipe on a unit box, nose up. Len = hull length; Body =
        /// hull half-width; Span = wingtip half-span; WingAt / Chord = where the
        /// wing root starts / how long it runs (fraction of Len); Sweep = how far
        /// back the tip sits (negative = forward-swept); Nose = nose-cone length.
        /// </summary>
        readonly struct Shape
        {
            public readonly float Len, Body, Span, WingAt, Sweep, Chord, Nose;
            public readonly int Engines;
            public readonly bool Pods, Ring, Claws, Containers, Orb;

            public Shape(float len, float body, float span, float wingAt, float sweep, float chord, float nose,
                int engines, bool pods = false, bool ring = false, bool claws = false,
                bool containers = false, bool orb = false)
            {
                Len = len; Body = body; Span = span; WingAt = wingAt; Sweep = sweep; Chord = chord; Nose = nose;
                Engines = engines; Pods = pods; Ring = ring; Claws = claws; Containers = containers; Orb = orb;
            }
        }

        static readonly Dictionary<HullId, Shape> Shapes = new()
        {
            [HullId.Fighter]   = new(0.60f, 0.070f, 0.30f, 0.55f, 0.16f, 0.22f, 0.30f, 1),
            [HullId.Bomber]    = new(0.64f, 0.100f, 0.40f, 0.40f, 0.10f, 0.34f, 0.18f, 2),
            [HullId.Cruiser]   = new(0.80f, 0.120f, 0.24f, 0.50f, 0.06f, 0.30f, 0.20f, 3, pods: true),
            [HullId.Talon]     = new(0.72f, 0.070f, 0.36f, 0.62f, -0.16f, 0.16f, 0.34f, 1),
            [HullId.Sentinel]  = new(0.62f, 0.110f, 0.26f, 0.45f, 0.04f, 0.30f, 0.16f, 2, ring: true),
            [HullId.Harrier]   = new(0.82f, 0.060f, 0.22f, 0.62f, 0.20f, 0.14f, 0.36f, 2),
            [HullId.Vanguard]  = new(0.86f, 0.160f, 0.30f, 0.45f, 0.08f, 0.36f, 0.22f, 3, pods: true),
            [HullId.Rampart]   = new(0.76f, 0.200f, 0.30f, 0.38f, 0.02f, 0.44f, 0.14f, 3, ring: true),
            [HullId.Corsair]   = new(0.88f, 0.130f, 0.34f, 0.50f, 0.20f, 0.26f, 0.28f, 2),
            [HullId.Lancer]    = new(0.80f, 0.080f, 0.20f, 0.30f, 0.02f, 0.50f, 0.30f, 1, pods: true),
            [HullId.Bulwark]   = new(0.70f, 0.150f, 0.26f, 0.40f, 0.04f, 0.40f, 0.14f, 2, ring: true),
            [HullId.Javelin]   = new(0.86f, 0.070f, 0.26f, 0.58f, 0.20f, 0.18f, 0.38f, 2),
            [HullId.Behemoth]  = new(0.90f, 0.220f, 0.36f, 0.40f, 0.06f, 0.40f, 0.12f, 4, pods: true),
            [HullId.Leviathan] = new(0.90f, 0.240f, 0.30f, 0.30f, 0.02f, 0.56f, 0.10f, 4, ring: true),
            [HullId.Nomad]     = new(0.92f, 0.180f, 0.38f, 0.52f, 0.20f, 0.30f, 0.24f, 3),
            [HullId.Reaper]    = new(0.92f, 0.140f, 0.40f, 0.55f, 0.24f, 0.26f, 0.34f, 3, pods: true),
            [HullId.Warden]    = new(0.86f, 0.180f, 0.32f, 0.42f, 0.06f, 0.40f, 0.18f, 3, ring: true),
            [HullId.Wraith]    = new(0.94f, 0.120f, 0.42f, 0.58f, 0.28f, 0.22f, 0.40f, 2),
            [HullId.Hauler]    = new(0.66f, 0.160f, 0.20f, 0.55f, 0.00f, 0.24f, 0.10f, 2, containers: true),
            [HullId.Atlas]     = new(0.88f, 0.200f, 0.26f, 0.60f, 0.00f, 0.22f, 0.10f, 3, containers: true),
            [HullId.Aegis]     = new(0.66f, 0.120f, 0.24f, 0.48f, 0.08f, 0.28f, 0.18f, 2, ring: true),
            [HullId.Scavenger] = new(0.70f, 0.130f, 0.22f, 0.55f, 0.06f, 0.24f, 0.12f, 2, claws: true),
            [HullId.Probe]     = new(0.50f, 0.000f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0, orb: true),
        };

        static readonly Color HullFill = new(0.74f, 0.8f, 0.88f);
        static readonly Color HullShade = new(0.52f, 0.58f, 0.68f);
        static readonly Color Outline = new(0.06f, 0.08f, 0.14f);
        static readonly Color EngineGlow = new(0.5f, 0.85f, 1f);
        static readonly Color Cargo = new(0.96f, 0.72f, 0.3f);

        readonly HullId _hull;
        readonly bool _grid;

        public ShipSchematic(HullId hull, bool grid = false)
        {
            _hull = hull;
            _grid = grid;
            generateVisualContent += Paint;
        }

        void Paint(MeshGenerationContext mgc)
        {
            var rect = contentRect;
            if (rect.width < 4f || rect.height < 4f) return;
            var p = mgc.painter2D;
            float s = Mathf.Min(rect.width, rect.height);
            var o = new Vector2(rect.x + (rect.width - s) * 0.5f, rect.y + (rect.height - s) * 0.5f);
            Vector2 P(float x, float y) => new(o.x + x * s, o.y + y * s);
            var role = ShipArt.RoleColor(_hull);

            if (_grid)
            {
                // Faint blueprint grid behind the hull.
                p.lineWidth = 1f;
                p.strokeColor = new Color(role.r, role.g, role.b, 0.08f);
                for (int i = 1; i < 8; i++)
                {
                    float t = i / 8f;
                    p.BeginPath(); p.MoveTo(new Vector2(rect.x + t * rect.width, rect.y));
                    p.LineTo(new Vector2(rect.x + t * rect.width, rect.yMax)); p.Stroke();
                    p.BeginPath(); p.MoveTo(new Vector2(rect.x, rect.y + t * rect.height));
                    p.LineTo(new Vector2(rect.xMax, rect.y + t * rect.height)); p.Stroke();
                }
            }

            if (!Shapes.TryGetValue(_hull, out var sh)) return;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

            void Fill(Color c, params Vector2[] pts)
            {
                p.fillColor = c;
                p.BeginPath();
                p.MoveTo(pts[0]);
                for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
                p.ClosePath();
                p.Fill();
            }
            void Circle(Vector2 c, float radius, Color color, bool fill, float width = 1f)
            {
                p.BeginPath();
                p.Arc(c, radius, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                if (fill) { p.fillColor = color; p.Fill(); }
                else { p.strokeColor = color; p.lineWidth = width; p.Stroke(); }
            }

            if (sh.Orb)
            {
                // Recon probe: sensor orb, three antennae, a scan ring.
                p.strokeColor = HullShade;
                p.lineWidth = Mathf.Max(1f, s * 0.02f);
                foreach (float a in new[] { -90f, 30f, 150f })
                {
                    float rad = a * Mathf.Deg2Rad;
                    p.BeginPath();
                    p.MoveTo(P(0.5f + Mathf.Cos(rad) * 0.12f, 0.5f + Mathf.Sin(rad) * 0.12f));
                    p.LineTo(P(0.5f + Mathf.Cos(rad) * 0.32f, 0.5f + Mathf.Sin(rad) * 0.32f));
                    p.Stroke();
                    Circle(P(0.5f + Mathf.Cos(rad) * 0.33f, 0.5f + Mathf.Sin(rad) * 0.33f), s * 0.03f, role, fill: true);
                }
                Circle(P(0.5f, 0.5f), s * 0.14f, HullFill, fill: true);
                Circle(P(0.5f, 0.5f), s * 0.14f, Outline, fill: false, Mathf.Max(1f, s * 0.015f));
                Circle(P(0.5f, 0.5f), s * 0.06f, role, fill: true);
                Circle(P(0.5f, 0.5f), s * 0.42f, new Color(role.r, role.g, role.b, 0.35f), fill: false,
                    Mathf.Max(1f, s * 0.01f));
                return;
            }

            float top = 0.5f - sh.Len * 0.5f;
            float Y(float t) => top + Mathf.Clamp01(t) * sh.Len;
            const float cx = 0.5f;

            if (sh.Ring) // shield bubble behind the hull
            {
                Circle(P(cx, 0.5f), s * (sh.Len * 0.56f), new Color(role.r, role.g, role.b, 0.10f), fill: true);
                Circle(P(cx, 0.5f), s * (sh.Len * 0.56f), new Color(role.r, role.g, role.b, 0.55f), fill: false,
                    Mathf.Max(1f, s * 0.012f));
            }

            // Engine glow first so the hull overlaps its inner half.
            for (int i = 0; i < sh.Engines; i++)
            {
                float t = sh.Engines == 1 ? 0f : i / (float)(sh.Engines - 1) * 2f - 1f;
                var e = P(cx + t * sh.Body * 0.55f, Y(1f) - 0.005f);
                Circle(e, s * 0.045f, new Color(EngineGlow.r, EngineGlow.g, EngineGlow.b, 0.25f), fill: true);
                Circle(e, s * 0.022f, EngineGlow, fill: true);
            }

            // Wings (one polygon per side), then the hull on top.
            float wingTipFront = sh.WingAt + sh.Sweep;
            float wingTipBack = wingTipFront + sh.Chord * 0.45f;
            for (int side = -1; side <= 1; side += 2)
            {
                Fill(HullShade,
                    P(cx + side * sh.Body * 0.9f, Y(sh.WingAt)),
                    P(cx + side * sh.Span, Y(wingTipFront)),
                    P(cx + side * sh.Span, Y(wingTipBack)),
                    P(cx + side * sh.Body * 0.9f, Y(sh.WingAt + sh.Chord)));
                // Role-colored wingtip lights.
                Circle(P(cx + side * sh.Span, Y((wingTipFront + wingTipBack) * 0.5f)), s * 0.014f, role, fill: true);
            }

            var hull = new[]
            {
                P(cx, Y(0f)),                                   // nose tip
                P(cx + sh.Body * 0.55f, Y(sh.Nose)),            // nose shoulder
                P(cx + sh.Body, Y(sh.Nose + 0.12f)),
                P(cx + sh.Body, Y(0.86f)),
                P(cx + sh.Body * 0.8f, Y(1f)),                  // tail
                P(cx - sh.Body * 0.8f, Y(1f)),
                P(cx - sh.Body, Y(0.86f)),
                P(cx - sh.Body, Y(sh.Nose + 0.12f)),
                P(cx - sh.Body * 0.55f, Y(sh.Nose)),
            };
            Fill(HullFill, hull);
            p.strokeColor = Outline;
            p.lineWidth = Mathf.Max(1f, s * 0.012f);
            p.BeginPath();
            p.MoveTo(hull[0]);
            for (int i = 1; i < hull.Length; i++) p.LineTo(hull[i]);
            p.ClosePath();
            p.Stroke();

            if (sh.Pods) // weapon / missile pods flanking the hull
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float x0 = cx + side * (sh.Body + 0.015f), x1 = cx + side * (sh.Body + 0.055f);
                    Fill(HullShade, P(x0, Y(0.3f)), P(x1, Y(0.34f)), P(x1, Y(0.62f)), P(x0, Y(0.62f)));
                }
            }
            if (sh.Containers) // freighter cargo modules down the spine
            {
                for (int i = 0; i < 3; i++)
                {
                    float t0 = 0.28f + i * 0.2f, t1 = t0 + 0.15f;
                    Fill(Cargo, P(cx - sh.Body * 0.7f, Y(t0)), P(cx + sh.Body * 0.7f, Y(t0)),
                        P(cx + sh.Body * 0.7f, Y(t1)), P(cx - sh.Body * 0.7f, Y(t1)));
                }
            }
            if (sh.Claws) // salvage grapples at the bow
            {
                p.strokeColor = HullShade;
                p.lineWidth = Mathf.Max(1.2f, s * 0.025f);
                for (int side = -1; side <= 1; side += 2)
                {
                    p.BeginPath();
                    p.MoveTo(P(cx + side * sh.Body * 0.6f, Y(0.2f)));
                    p.LineTo(P(cx + side * sh.Body * 1.2f, Y(-0.05f)));
                    p.LineTo(P(cx + side * sh.Body * 0.5f, Y(-0.12f)));
                    p.Stroke();
                }
            }

            // Role stripe down the spine + cockpit.
            p.strokeColor = new Color(role.r, role.g, role.b, 0.9f);
            p.lineWidth = Mathf.Max(1f, s * 0.018f);
            p.BeginPath();
            p.MoveTo(P(cx, Y(sh.Nose + 0.14f)));
            p.LineTo(P(cx, Y(0.84f)));
            p.Stroke();
            Circle(P(cx, Y(sh.Nose + 0.06f)), s * Mathf.Max(0.018f, sh.Body * 0.3f), new Color(0.15f, 0.3f, 0.45f), fill: true);
        }
    }

    public static class ResearchArt
    {
        static readonly Dictionary<TechId, Texture2D?> _photos = new();

        public static Texture2D? Photo(TechId tech)
        {
            if (!_photos.TryGetValue(tech, out var tex))
            {
                tex = UnityEngine.Resources.Load<Texture2D>($"Research/{tech}");
                _photos[tech] = tex;
            }
            return tex;
        }

        public static Color CategoryColor(TechCategory c) => c switch
        {
            TechCategory.Economy => UiTheme.Energy,
            TechCategory.Logistics => UiTheme.Quartz,
            TechCategory.Military => UiTheme.Bad,
            TechCategory.Defense => UiTheme.Good,
            _ => UiTheme.DarkMatter, // Industry
        };

        /// <summary>Hex tech emblem (Neon Hologram, 2026-09-29; it was round): the drop-in
        /// art, or a painted badge — the effect's glyph (or, for hull-scoped techs, that
        /// hull's art) on a glass hex edged in the category's colour.</summary>
        public static VisualElement Emblem(TechId tech, float size, bool dim = false)
        {
            var def = Techs.Defs[tech];
            var color = CategoryColor(def.Category);
            if (dim) color = Color.Lerp(color, UiTheme.Stroke, 0.65f);

            var badge = new VisualElement { pickingMode = PickingMode.Ignore };
            badge.style.width = size * 1.08f; // a flat-sided hex reads best a little wide
            badge.style.height = size;
            badge.style.flexShrink = 0f;
            badge.style.justifyContent = Justify.Center;
            badge.style.alignItems = Align.Center;
            Holo.Frame(badge, new Color(color.r * 0.2f, color.g * 0.2f, color.b * 0.2f, 0.95f), color, 0f,
                Mathf.Max(1f, size * 0.04f), FrameShape.Hex, glow: !dim);

            var photo = Photo(tech);
            if (photo != null)
            {
                var art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.style.width = size * 0.78f;
                art.style.height = size * 0.78f;
                art.style.backgroundImage = new StyleBackground(photo);
                art.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                badge.Add(art);
                return badge;
            }

            if (def.HullScope is HullId hull)
            {
                badge.Add(ShipArt.Sprite(hull, size * 0.74f));
                return badge;
            }

            badge.Add(Icons.Make(GlyphFor(def.Effect), size * 0.5f, color));
            return badge;
        }

        static Icon GlyphFor(TechEffectKind kind) => kind switch
        {
            TechEffectKind.ProdMultiplier => Icon.Chart,
            TechEffectKind.GatherRateMult => Icon.ArrowDown,
            TechEffectKind.MarchSpeedMult => Icon.ArrowUp,
            TechEffectKind.CargoMult => Icon.Menu,
            TechEffectKind.HeliumReduce => Icon.Dot,
            TechEffectKind.AtkMult => Icon.Swords,
            TechEffectKind.HpMult => Icon.Check,
            TechEffectKind.ShieldMult => Icon.Ring,
            TechEffectKind.ShieldCapMult => Icon.Star,
            TechEffectKind.BuildTimeReduce => Icon.Rotate,
            TechEffectKind.ShipTimeReduce => Icon.Rotate,
            TechEffectKind.ResearchTimeReduce => Icon.Eye,
            TechEffectKind.DefAtkMult => Icon.Swords,
            TechEffectKind.DefHpMult => Icon.Shield,
            TechEffectKind.DefShieldMult => Icon.Ring,
            TechEffectKind.OrbitalBattery => Icon.Target,
            _ => Icon.Info,
        };
    }
}
