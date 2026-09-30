// UI Toolkit widget factories: buttons, panel chrome with title + close, list
// rows, progress bars. Everything is styled inline from UiTheme — no USS files,
// so the whole look lives in reviewable C#. The look is Neon Hologram with an
// orange accent (restyle, 2026-09-28): cut-corner frames painted by HoloFrame,
// Orbitron for display text (UiFonts), dark violet glass.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public static class Widgets
    {
        /// <summary>v1 TextButton: panel-blue fill, stroke border, active highlight.</summary>
        /// <summary>A font size scaled by the player's text-size setting.</summary>
        public static float Sized(float size) => size * Settings.TextScale;

        /// <summary>Orbitron runs wide: display text drops to this share of the
        /// size a caller asked for, so labels sized for the old font still fit.</summary>
        const float DisplayScale = 0.86f;

        /// <summary>A cut-corner button: orange outline and text on dark glass.
        /// <see cref="Primary"/> fills it for the screen's main action; the "×"
        /// close label becomes a painted cross (Orbitron's × is too thin).</summary>
        public static Button TextButton(string label, Action onTap, int fontSize = 14)
        {
            bool close = label == "×";
            var btn = new Button(onTap) { text = close ? "" : label };
            btn.clicked += GameAudio.Tap; // quiet tick on every button (IconButton/Fab build on this)
            btn.style.fontSize = Sized(fontSize * DisplayScale);
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            UiFonts.Display(btn, 1.1f);
            btn.style.paddingTop = 6;
            btn.style.paddingBottom = 6;
            btn.style.paddingLeft = 10;
            btn.style.paddingRight = 10;
            btn.style.marginLeft = 0;
            btn.style.marginRight = 0;
            btn.style.marginTop = 0;
            btn.style.marginBottom = 0;
            Holo.Frame(btn, UiTheme.Btn, UiTheme.A(UiTheme.Accent, 0.7f), 7f);
            Paint(btn, Look.Normal);
            if (close)
            {
                btn.style.justifyContent = Justify.Center;
                btn.style.alignItems = Align.Center;
                btn.style.paddingLeft = 0;
                btn.style.paddingRight = 0;
                btn.Add(Icons.Make(Icon.Close, fontSize + 2f, UiTheme.Magenta));
                Paint(btn, Look.Close);
            }
            btn.RegisterCallback<PointerDownEvent>(_ => PaintPressed(btn, true), TrickleDown.TrickleDown);
            btn.RegisterCallback<PointerUpEvent>(_ => PaintPressed(btn, false), TrickleDown.TrickleDown);
            btn.RegisterCallback<PointerLeaveEvent>(_ => PaintPressed(btn, false));
            return btn;
        }

        enum Look { Normal, Primary, Highlight, Disabled, Close, Link }

        /// <summary>What each button is when it isn't pressed, disabled or highlighted.</summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, LookBox> s_rest = new();
        sealed class LookBox { public Look Look; public Color Ring; }

        static Look Rest(Button btn) => s_rest.TryGetValue(btn, out var box) ? box.Look : Look.Normal;

        static void Paint(Button btn, Look look)
        {
            if (look is Look.Normal or Look.Primary or Look.Close or Look.Link)
            {
                var tone = look == Look.Primary && s_rest.TryGetValue(btn, out var old) ? old.Ring : default;
                s_rest.AddOrUpdate(btn, new LookBox { Look = look, Ring = tone });
            }
            Color fill, stroke, text;
            switch (look)
            {
                case Look.Primary:
                {
                    var tone = s_rest.TryGetValue(btn, out var box) && box.Ring.a > 0f ? box.Ring : UiTheme.Accent;
                    fill = tone; stroke = tone; text = UiTheme.Ink; break;
                }
                case Look.Highlight:
                    fill = UiTheme.BtnActive; stroke = UiTheme.Accent; text = UiTheme.Accent; break;
                case Look.Disabled:
                    fill = UiTheme.BtnDisabled; stroke = UiTheme.A(UiTheme.Dim, 0.3f); text = UiTheme.A(UiTheme.Dim, 0.8f); break;
                case Look.Close:
                    fill = UiTheme.A(UiTheme.Magenta, 0.08f); stroke = UiTheme.A(UiTheme.Magenta, 0.8f); text = UiTheme.Magenta; break;
                case Look.Link:
                    fill = Color.clear; stroke = Color.clear; text = UiTheme.Magenta; break;
                default:
                    fill = UiTheme.Btn; stroke = UiTheme.A(UiTheme.Accent, 0.7f); text = UiTheme.Accent; break;
            }
            Holo.Set(btn, fill, stroke);
            btn.style.color = text;
            TintIcons(btn, text);
        }

        static void PaintPressed(Button btn, bool down)
        {
            if (!btn.enabledSelf) return;
            bool lit = s_highlighted.TryGetValue(btn, out _);
            var frame = Holo.Of(btn);
            if (frame == null) return;
            if (s_fabFill.TryGetValue(btn, out _))
            {
                if (down) frame.Set(UiTheme.A(UiTheme.Accent, 0.3f), frame.Stroke);
                else PaintFab(btn, lit);
                return;
            }
            if (!down) { Paint(btn, lit ? Look.Highlight : Rest(btn)); return; }
            frame.Set(Rest(btn) == Look.Primary ? Color.Lerp(frame.Fill, UiTheme.Ink, 0.25f) : UiTheme.BtnActive, frame.Stroke);
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, LookBox> s_highlighted = new();

        /// <summary>A frameless magenta text button (‹ BACK and similar).</summary>
        public static Button Link(Button btn)
        {
            Paint(btn, Look.Link);
            return btn;
        }

        /// <summary>Fill a button in the accent colour (or <paramref name="tone"/>): the
        /// screen's main action.</summary>
        public static Button Primary(Button btn, Color? tone = null)
        {
            s_rest.AddOrUpdate(btn, new LookBox { Look = Look.Primary, Ring = tone ?? default });
            if (btn.enabledSelf) Paint(btn, Look.Primary);
            return btn;
        }

        /// <summary>
        /// A real button carrying a painted <see cref="Icon"/> plus an optional
        /// caption. Use this instead of putting symbols in button text: the
        /// runtime font tofu-boxes many of them on device (the old "ⓘ LEVELS"
        /// and "⚔ RAID" buttons read "□ LEVELS" / "□ RAID").
        /// </summary>
        public static Button IconButton(Icon icon, string? caption, Action onTap, int fontSize = 12,
            float iconSize = 0f)
        {
            var btn = TextButton("", onTap, fontSize);
            btn.style.flexDirection = FlexDirection.Row;
            btn.style.justifyContent = Justify.Center;
            btn.style.alignItems = Align.Center;
            btn.Add(Icons.Make(icon, iconSize > 0f ? iconSize : fontSize + 3f, UiTheme.Accent));
            if (!string.IsNullOrEmpty(caption))
            {
                // No explicit color: the caption inherits the button's text color,
                // so SetButtonEnabled / SetButtonHighlight restyle it for free.
                var text = new Label(caption) { name = "caption", pickingMode = PickingMode.Ignore };
                text.style.fontSize = Sized(fontSize * DisplayScale);
                text.style.unityFontStyleAndWeight = FontStyle.Bold;
                text.style.marginLeft = 6;
                text.style.marginRight = 0;
                text.style.marginTop = 0;
                text.style.marginBottom = 0;
                text.style.paddingLeft = 0;
                text.style.paddingRight = 0;
                text.style.paddingTop = 0;
                text.style.paddingBottom = 0;
                btn.Add(text);
            }
            return btn;
        }

        /// <summary>Swap an <see cref="IconButton"/>'s caption text (no-op for plain buttons).</summary>
        public static void SetCaption(Button btn, string caption)
        {
            var label = btn.Q<Label>("caption");
            if (label != null) label.text = caption;
            else btn.text = caption;
        }

        /// <summary>
        /// Hexagonal floating action button: painted icon, optional tiny caption
        /// under it. Zero side padding on purpose — TextButton's 10px padding left
        /// ~24px of room inside a 48px button, so labels like "DAILY" / "FIND"
        /// spilled past the edge.
        /// </summary>
        public static Button Fab(Icon icon, string? caption, Action onTap, float size = 48f,
            Color? ring = null, float ringWidth = 2f)
        {
            var btn = TextButton("", onTap, 9);
            btn.style.width = size * 1.08f; // a flat-sided hexagon reads best a little wide
            btn.style.height = size;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.flexDirection = FlexDirection.Column;
            btn.style.justifyContent = Justify.Center;
            btn.style.alignItems = Align.Center;
            Holo.Frame(btn, UiTheme.A(UiTheme.Bg, 0.8f), ring ?? UiTheme.Accent, 0f,
                Mathf.Max(1.5f, ringWidth * 0.75f), FrameShape.Hex, glow: true);
            s_fabFill.AddOrUpdate(btn, new LookBox { Ring = ring ?? UiTheme.Accent });
            btn.style.color = UiTheme.Accent;

            bool hasCaption = !string.IsNullOrEmpty(caption);
            btn.Add(Icons.Make(icon, size * (hasCaption ? 0.36f : 0.44f), UiTheme.Accent));
            if (hasCaption)
            {
                var text = new Label(caption) { name = "caption", pickingMode = PickingMode.Ignore };
                text.style.fontSize = size >= 46f ? 8 : 7;
                text.style.unityFontStyleAndWeight = FontStyle.Bold;
                text.style.unityTextAlign = TextAnchor.MiddleCenter;
                text.style.marginTop = 1;
                text.style.marginBottom = 0;
                text.style.marginLeft = 0;
                text.style.marginRight = 0;
                text.style.paddingLeft = 0;
                text.style.paddingRight = 0;
                text.style.paddingTop = 0;
                text.style.paddingBottom = 0;
                btn.Add(text);
            }
            return btn;
        }

        /// <summary>Painted icon + label in a row — for inline markers like the
        /// news ticker's swords or the radar warning header.</summary>
        public static VisualElement IconText(Icon icon, string text, int size, Color color, bool bold = false)
        {
            var row = HBox();
            row.pickingMode = PickingMode.Ignore;
            var glyph = Icons.Make(icon, size + 2f, color);
            glyph.style.marginRight = 5;
            row.Add(glyph);
            var label = Text(text, size, color, bold);
            label.name = "text";
            label.style.flexShrink = 1f;
            row.Add(label);
            return row;
        }

        public static void SetButtonEnabled(Button btn, bool on)
        {
            btn.SetEnabled(on);
            if (!on) { Paint(btn, Look.Disabled); return; }
            if (s_fabFill.TryGetValue(btn, out _)) { PaintFab(btn, s_highlighted.TryGetValue(btn, out _)); return; }
            Paint(btn, s_highlighted.TryGetValue(btn, out _) ? Look.Highlight : Rest(btn));
        }

        public static void SetButtonHighlight(Button btn, bool on)
        {
            if (on) s_highlighted.AddOrUpdate(btn, new LookBox());
            else s_highlighted.Remove(btn);
            if (!btn.enabledSelf) return;
            if (s_fabFill.TryGetValue(btn, out _)) { PaintFab(btn, on); return; }
            Paint(btn, on ? Look.Highlight : Rest(btn));
        }

        /// <summary>Hex buttons keep their dark glass; "on" fills them with accent glass.</summary>
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, LookBox> s_fabFill = new();

        static void PaintFab(Button btn, bool on)
        {
            var ring = s_fabFill.TryGetValue(btn, out var box) ? box.Ring : UiTheme.Accent;
            Holo.Set(btn, on ? UiTheme.A(UiTheme.Accent, 0.3f) : UiTheme.A(UiTheme.Bg, 0.8f), on ? UiTheme.Accent : ring);
            btn.style.color = UiTheme.Accent;
            TintIcons(btn, UiTheme.Accent);
        }

        /// <summary>Painted icons don't inherit text color — keep them in step.</summary>
        static void TintIcons(VisualElement root, Color color)
        {
            root.Query<IconElement>().ForEach(i => i.Color = color);
        }

        public static Label Text(string text, int size, Color color, bool bold = false)
        {
            var l = new Label(text);
            l.style.fontSize = Sized(size);
            l.style.color = color;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 0;
            l.style.marginBottom = 0;
            l.style.paddingTop = 0;
            l.style.paddingBottom = 0;
            return l;
        }

        /// <summary>A display label in Orbitron: titles, section headers, captions.</summary>
        public static Label Heading(string text, int size, Color color, float tracking = 1.4f)
        {
            var l = Text(text, 0, color, bold: true);
            l.style.fontSize = Sized(size * DisplayScale);
            UiFonts.Display(l, tracking);
            return l;
        }

        /// <summary>Full-screen modal scaffold: dim blocker + panel with title bar and ×.
        /// heightPct <= 0 → COMPACT: the panel hugs its content (capped at 82%) and
        /// narrows to 82% width — for small pickers/cards with no overhang.</summary>
        public static (VisualElement blocker, VisualElement content) ModalPanel(
            string title, Action onClose, float heightPct = 78f)
        {
            var blocker = new VisualElement();
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0;
            blocker.style.right = 0;
            blocker.style.top = 0;
            blocker.style.bottom = 0;
            blocker.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            blocker.style.justifyContent = Justify.Center;
            blocker.style.alignItems = Align.Center;
            // Panels sit between the header and the nav (as on the Neon Hologram
            // canvas): the resources stay readable and nothing ghosts through a title.
            blocker.style.paddingTop = HeaderBottom() + 6f;
            blocker.style.paddingBottom = UiTheme.NavH + 6f;

            var panel = new VisualElement();
            if (heightPct <= 0f)
            {
                panel.style.width = Length.Percent(82f);
                panel.style.height = StyleKeyword.Auto;
                panel.style.maxHeight = Length.Percent(100f);
            }
            else
            {
                // Callers size panels against the whole screen; the space between
                // the header and the nav is about 3/4 of it.
                panel.style.width = Length.Percent(94f);
                panel.style.height = Length.Percent(Mathf.Min(100f, heightPct * 1.3f));
            }
            Holo.Frame(panel, UiTheme.Panel, UiTheme.Accent, 20f, 1.5f, glow: true);
            // Taps inside the panel must not fall through to the blocker.
            panel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());

            var titleBar = new VisualElement();
            titleBar.style.flexDirection = FlexDirection.Row;
            titleBar.style.justifyContent = Justify.SpaceBetween;
            titleBar.style.alignItems = Align.Center;
            titleBar.style.paddingLeft = 14;
            titleBar.style.paddingRight = 8;
            titleBar.style.paddingTop = 10;
            titleBar.style.paddingBottom = 9;
            titleBar.style.borderBottomWidth = 1;
            titleBar.style.borderBottomColor = UiTheme.Stroke;
            titleBar.style.backgroundColor = UiTheme.A(UiTheme.Accent, 0.07f);
            // Long titles (e.g. radar mail subjects) ellipsize instead of shoving
            // the × off the right edge of the screen (user bug report).
            var titleLabel = Heading(title, 15, UiTheme.Accent, 1.2f);
            titleLabel.style.flexShrink = 1f;
            titleLabel.style.flexGrow = 1f;
            titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            titleLabel.style.overflow = Overflow.Hidden;
            titleLabel.style.textOverflow = TextOverflow.Ellipsis;
            titleBar.Add(titleLabel);

            var close = TextButton("×", onClose, 14);
            close.style.width = 34;
            close.style.height = 32;
            close.style.flexShrink = 0f; // the close button NEVER leaves the screen
            titleBar.Add(close);
            panel.Add(titleBar);

            var content = new ScrollView(ScrollViewMode.Vertical);
            // Touch-scroll only — no visible scrollbars (user feedback: the fat
            // desktop scrollers looked wrong on phone; drag gestures still work).
            content.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            content.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            content.style.flexGrow = 1f;
            content.style.paddingLeft = 12;
            content.style.paddingRight = 12;
            content.style.paddingTop = 10;
            content.style.paddingBottom = 10;
            panel.Add(content);

            blocker.Add(panel);
            return (blocker, content.contentContainer);
        }

        /// <summary>
        /// Modal scaffold with a pinned FOOTER at the bottom of the box (action
        /// buttons that should sit at the bottom of the card, not trail the text —
        /// the scroll body has flexGrow so the footer is always parked at the
        /// bottom edge). Returns the blocker, the scrollable content, and the footer.
        /// </summary>
        public static (VisualElement blocker, VisualElement content, VisualElement footer) ModalPanelFooter(
            string title, Action onClose, float heightPct = 78f)
        {
            var (blocker, content) = ModalPanel(title, onClose, heightPct);
            // content is the ScrollView's contentContainer; walk up to the panel
            // (contentContainer → contentViewport → ScrollView → panel).
            var scroll = content;
            while (scroll != null && scroll is not ScrollView) scroll = scroll.parent;
            var panel = scroll?.parent;

            var footer = new VisualElement();
            footer.style.flexShrink = 0f;
            footer.style.paddingLeft = 12;
            footer.style.paddingRight = 12;
            footer.style.paddingTop = 8;
            footer.style.paddingBottom = 10;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = UiTheme.Stroke;
            panel?.Add(footer); // sibling AFTER the flex-grow scroll → sits at the bottom
            return (blocker, content, footer);
        }

        /// <summary>
        /// Full-screen PAGE scaffold (opaque, full-bleed) — for dedicated screens
        /// that should feel like their own menu rather than a popover: the login
        /// screen today; reuse for any future standalone page (settings, an
        /// onboarding flow, etc.). `onBack` fires from the top-left ‹ BACK; pass
        /// null to omit it (e.g. a first-boot screen with no "behind" to go to).
        /// Returns the blocker (hand to UIController.OpenModal) and the scrollable
        /// content column.
        /// </summary>
        public static (VisualElement blocker, VisualElement content) FullPage(
            string title, Action? onBack)
        {
            var blocker = new VisualElement();
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0;
            blocker.style.right = 0;
            blocker.style.top = 0;
            blocker.style.bottom = 0;
            blocker.style.backgroundColor = UiTheme.Bg; // opaque — a page, not a popup
            blocker.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 8;
            header.style.paddingRight = 8;
            // Push the bar below the dynamic island / notch. Safe-area top inset
            // (screen px) converted to UITK points via the panel's match-width scale.
            header.style.paddingTop = 6 + SafeAreaTopPoints();
            header.style.paddingBottom = 6;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = UiTheme.Accent;
            header.style.backgroundColor = UiTheme.A(UiTheme.Bg, 0.9f);
            if (onBack != null)
            {
                var back = Link(IconButton(Icon.ChevronLeft, "BACK", onBack, 12));
                back.style.minWidth = 74;
                header.Add(back);
            }
            var titleLabel = Heading(title, 16, UiTheme.Accent, 1.6f);
            titleLabel.style.flexGrow = 1f;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            header.Add(titleLabel);
            // Symmetry spacer so the centered title stays centered when BACK shows.
            if (onBack != null) { var spacer = new VisualElement(); spacer.style.minWidth = 74; header.Add(spacer); }
            blocker.Add(header);

            var content = new ScrollView(ScrollViewMode.Vertical);
            content.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            content.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            content.style.flexGrow = 1f;
            content.style.paddingLeft = 18;
            content.style.paddingRight = 18;
            content.style.paddingTop = 16;
            content.style.paddingBottom = 24;
            blocker.Add(content);
            return (blocker, content.contentContainer);
        }

        /// <summary>Where the HUD header ends, in UITK points (UIController sizes the
        /// header the same way: the island row, the HQ line and the resource strip).</summary>
        public static float HeaderBottom() => Mathf.Max(SafeAreaTopPoints() + 4f, 34f) + 20f + 50f;

        /// <summary>Top safe-area inset (dynamic island / notch) in UITK points.
        /// Screen px → points via the panel's match-width reference (UiTheme.W).</summary>
        public static float SafeAreaTopPoints()
        {
            var sa = Screen.safeArea;
            float topPx = Screen.height - (sa.y + sa.height);
            if (topPx <= 0f || Screen.width <= 0) return 0f;
            return topPx * (UiTheme.W / (float)Screen.width);
        }

        /// <summary>A list row card: violet glass in a thin orange cut-corner frame.
        /// Recolour it with <see cref="Holo.SetFill"/> / <see cref="Holo.SetStroke"/>.</summary>
        public static VisualElement Row()
        {
            var row = new VisualElement();
            Holo.Frame(row, UiTheme.PanelLight, UiTheme.A(UiTheme.Accent, 0.28f), 9f);
            row.style.paddingLeft = 10;
            row.style.paddingRight = 10;
            row.style.paddingTop = 8;
            row.style.paddingBottom = 8;
            row.style.marginBottom = 8;
            return row;
        }

        /// <summary>Horizontal flex container.</summary>
        public static VisualElement HBox(Justify justify = Justify.FlexStart, Align align = Align.Center)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Row;
            box.style.justifyContent = justify;
            box.style.alignItems = align;
            return box;
        }

        /// <summary>v1 ProgressBar: light track, active-blue fill, centered label.</summary>
        public static (VisualElement bar, VisualElement fill, Label label) ProgressBar(float height = 18f)
        {
            var bar = new VisualElement();
            bar.style.height = height;
            bar.style.backgroundColor = UiTheme.A(UiTheme.Accent, 0.1f);
            SetBorder(bar, UiTheme.A(UiTheme.Accent, 0.3f), 1f);
            bar.style.justifyContent = Justify.Center;

            var fill = new VisualElement();
            fill.style.position = Position.Absolute;
            fill.style.left = 0;
            fill.style.top = 0;
            fill.style.bottom = 0;
            fill.style.width = Length.Percent(0f);
            fill.style.backgroundColor = UiTheme.A(UiTheme.Accent, 0.55f);
            bar.Add(fill);

            var label = Text("", 11, UiTheme.Text);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            bar.Add(label);
            return (bar, fill, label);
        }

        /// <summary>A round count bubble (queues, district tabs): a true circle with its
        /// number centred on both axes. The label fills the circle and centres its
        /// text itself, so Label padding and font metrics can't push it off-centre.</summary>
        public static VisualElement CountBubble(float size, int fontSize, Color fill, out Label label)
        {
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.style.position = Position.Absolute;
            dot.style.width = size;
            dot.style.height = size;
            float r = size * 0.5f;
            dot.style.borderTopLeftRadius = r;
            dot.style.borderTopRightRadius = r;
            dot.style.borderBottomLeftRadius = r;
            dot.style.borderBottomRightRadius = r;
            dot.style.backgroundColor = fill;
            label = new Label("") { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = 0;
            label.style.right = 0;
            label.style.top = 0;
            label.style.bottom = 0;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.fontSize = fontSize;
            label.style.color = Color.white;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            dot.Add(label);
            return dot;
        }

        /// <summary>Counts over 9 read "9+" so the bubble stays a circle.</summary>
        public static string BubbleCount(int n) => n > 9 ? "9+" : n.ToString();

        /// <summary>A CSS border — or, on an element with a painted frame, the
        /// frame's stroke, so older code colouring a ring still works.</summary>
        public static void SetBorder(VisualElement e, Color color, float width)
        {
            var frame = width > 0f ? Holo.Of(e) : null;
            if (frame != null)
            {
                if (frame.Stroke != color || frame.Width != width)
                {
                    frame.Stroke = color;
                    frame.Width = width;
                    frame.MarkDirtyRepaint();
                }
                return;
            }
            e.style.borderLeftColor = color;
            e.style.borderRightColor = color;
            e.style.borderTopColor = color;
            e.style.borderBottomColor = color;
            e.style.borderLeftWidth = width;
            e.style.borderRightWidth = width;
            e.style.borderTopWidth = width;
            e.style.borderBottomWidth = width;
        }
    }
}
