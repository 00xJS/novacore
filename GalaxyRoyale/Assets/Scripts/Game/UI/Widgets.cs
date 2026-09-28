// UI Toolkit widget factories mirroring v1's `src/ui/widgets.ts` look:
// flat dark-blue buttons with 1px strokes, panel chrome with title + close,
// list rows, progress bars. Everything is styled inline from UiTheme — no USS
// files, so the whole look lives in reviewable C#.
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public static class Widgets
    {
        /// <summary>v1 TextButton: panel-blue fill, stroke border, active highlight.</summary>
        public static Button TextButton(string label, Action onTap, int fontSize = 14)
        {
            var btn = new Button(onTap) { text = label };
            btn.clicked += GameAudio.Tap; // quiet tick on every button (IconButton/Fab build on this)
            btn.style.backgroundColor = UiTheme.Btn;
            btn.style.color = UiTheme.Text;
            btn.style.fontSize = fontSize;
            btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            SetBorder(btn, UiTheme.Stroke, 1f);
            btn.style.borderTopLeftRadius = 3;
            btn.style.borderTopRightRadius = 3;
            btn.style.borderBottomLeftRadius = 3;
            btn.style.borderBottomRightRadius = 3;
            btn.style.paddingTop = 6;
            btn.style.paddingBottom = 6;
            btn.style.paddingLeft = 10;
            btn.style.paddingRight = 10;
            btn.style.marginLeft = 0;
            btn.style.marginRight = 0;
            btn.style.marginTop = 0;
            btn.style.marginBottom = 0;
            btn.RegisterCallback<PointerDownEvent>(_ => btn.style.backgroundColor = UiTheme.BtnActive,
                TrickleDown.TrickleDown);
            btn.RegisterCallback<PointerUpEvent>(_ => btn.style.backgroundColor =
                btn.enabledSelf ? UiTheme.Btn : UiTheme.BtnDisabled, TrickleDown.TrickleDown);
            btn.RegisterCallback<PointerLeaveEvent>(_ => btn.style.backgroundColor =
                btn.enabledSelf ? UiTheme.Btn : UiTheme.BtnDisabled);
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
            btn.Add(Icons.Make(icon, iconSize > 0f ? iconSize : fontSize + 3f, UiTheme.Text));
            if (!string.IsNullOrEmpty(caption))
            {
                // No explicit color: the caption inherits the button's text color,
                // so SetButtonEnabled / SetButtonHighlight restyle it for free.
                var text = new Label(caption) { name = "caption", pickingMode = PickingMode.Ignore };
                text.style.fontSize = fontSize;
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
        /// Round floating action button: painted icon, optional tiny caption under
        /// it. Zero side padding on purpose — TextButton's 10px padding left ~24px
        /// of room inside a 48px circle, so labels like "DAILY" / "FIND" spilled
        /// past the ring.
        /// </summary>
        public static Button Fab(Icon icon, string? caption, Action onTap, float size = 48f,
            Color? ring = null, float ringWidth = 2f)
        {
            var btn = TextButton("", onTap, 9);
            btn.style.width = size;
            btn.style.height = size;
            float radius = size * 0.5f;
            btn.style.borderTopLeftRadius = radius;
            btn.style.borderTopRightRadius = radius;
            btn.style.borderBottomLeftRadius = radius;
            btn.style.borderBottomRightRadius = radius;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.flexDirection = FlexDirection.Column;
            btn.style.justifyContent = Justify.Center;
            btn.style.alignItems = Align.Center;
            SetBorder(btn, ring ?? UiTheme.Accent, ringWidth);

            bool hasCaption = !string.IsNullOrEmpty(caption);
            btn.Add(Icons.Make(icon, size * (hasCaption ? 0.36f : 0.44f), UiTheme.Text));
            if (hasCaption)
            {
                var text = new Label(caption) { name = "caption", pickingMode = PickingMode.Ignore };
                text.style.fontSize = size >= 46f ? 9 : 8;
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
            btn.style.backgroundColor = on ? UiTheme.Btn : UiTheme.BtnDisabled;
            btn.style.color = on ? UiTheme.Text : UiTheme.Dim;
            TintIcons(btn, on ? UiTheme.Text : UiTheme.Dim);
        }

        public static void SetButtonHighlight(Button btn, bool on)
        {
            btn.style.backgroundColor = on ? UiTheme.BtnActive : UiTheme.Btn;
            btn.style.color = on ? UiTheme.Accent : UiTheme.Text;
            TintIcons(btn, on ? UiTheme.Accent : UiTheme.Text);
        }

        /// <summary>Painted icons don't inherit text color — keep them in step.</summary>
        static void TintIcons(VisualElement root, Color color)
        {
            root.Query<IconElement>().ForEach(i => i.Color = color);
        }

        public static Label Text(string text, int size, Color color, bool bold = false)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 0;
            l.style.marginBottom = 0;
            l.style.paddingTop = 0;
            l.style.paddingBottom = 0;
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

            var panel = new VisualElement();
            if (heightPct <= 0f)
            {
                panel.style.width = Length.Percent(82f);
                panel.style.height = StyleKeyword.Auto;
                panel.style.maxHeight = Length.Percent(82f);
            }
            else
            {
                panel.style.width = Length.Percent(94f);
                panel.style.height = Length.Percent(heightPct);
            }
            panel.style.backgroundColor = UiTheme.Panel;
            SetBorder(panel, UiTheme.Stroke, 1f);
            panel.style.borderTopLeftRadius = 6;
            panel.style.borderTopRightRadius = 6;
            panel.style.borderBottomLeftRadius = 6;
            panel.style.borderBottomRightRadius = 6;
            // Taps inside the panel must not fall through to the blocker.
            panel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());

            var titleBar = new VisualElement();
            titleBar.style.flexDirection = FlexDirection.Row;
            titleBar.style.justifyContent = Justify.SpaceBetween;
            titleBar.style.alignItems = Align.Center;
            titleBar.style.paddingLeft = 14;
            titleBar.style.paddingRight = 8;
            titleBar.style.paddingTop = 8;
            titleBar.style.paddingBottom = 8;
            titleBar.style.borderBottomWidth = 1;
            titleBar.style.borderBottomColor = UiTheme.Stroke;
            // Long titles (e.g. radar mail subjects) ellipsize instead of shoving
            // the × off the right edge of the screen (user bug report).
            var titleLabel = Text(title, 16, UiTheme.Accent, bold: true);
            titleLabel.style.flexShrink = 1f;
            titleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            titleLabel.style.overflow = Overflow.Hidden;
            titleLabel.style.textOverflow = TextOverflow.Ellipsis;
            titleBar.Add(titleLabel);

            var close = TextButton("×", onClose, 14);
            close.style.width = 34;
            close.style.height = 30;
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
            header.style.borderBottomColor = UiTheme.Stroke;
            if (onBack != null)
            {
                var back = TextButton("‹ BACK", onBack, 12);
                back.style.minWidth = 74;
                header.Add(back);
            }
            var titleLabel = Text(title, 16, UiTheme.Accent, bold: true);
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

        /// <summary>Top safe-area inset (dynamic island / notch) in UITK points.
        /// Screen px → points via the panel's match-width reference (UiTheme.W).</summary>
        public static float SafeAreaTopPoints()
        {
            var sa = Screen.safeArea;
            float topPx = Screen.height - (sa.y + sa.height);
            if (topPx <= 0f || Screen.width <= 0) return 0f;
            return topPx * (UiTheme.W / (float)Screen.width);
        }

        /// <summary>A list row card (panelLight fill, stroke border) — v1's panel rows.</summary>
        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.backgroundColor = UiTheme.PanelLight;
            SetBorder(row, UiTheme.Stroke, 1f);
            row.style.borderTopLeftRadius = 4;
            row.style.borderTopRightRadius = 4;
            row.style.borderBottomLeftRadius = 4;
            row.style.borderBottomRightRadius = 4;
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
            bar.style.backgroundColor = UiTheme.PanelLight;
            SetBorder(bar, UiTheme.Stroke, 1f);
            bar.style.justifyContent = Justify.Center;

            var fill = new VisualElement();
            fill.style.position = Position.Absolute;
            fill.style.left = 0;
            fill.style.top = 0;
            fill.style.bottom = 0;
            fill.style.width = Length.Percent(0f);
            fill.style.backgroundColor = UiTheme.BtnActive;
            bar.Add(fill);

            var label = Text("", 11, UiTheme.Text);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            bar.Add(label);
            return (bar, fill, label);
        }

        public static void SetBorder(VisualElement e, Color color, float width)
        {
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
