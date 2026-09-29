// The globe base's controls (2026-09-28): district tabs above the news ticker
// (COMMAND, MINES and FRONTIER, each with its built count and a dot counting
// open pads) and the button at the top right that goes to ORBIT, back HOME to
// the Command district, or LANDs from orbit. BaseGlobe does the flying.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Sim;
using Position = UnityEngine.UIElements.Position;

namespace GalaxyRoyale.Game.UI
{
    public sealed class DistrictHud
    {
        sealed class Tab
        {
            public Button Button = null!;
            public Label Name = null!;
            public Label Count = null!;
            public VisualElement Dot = null!;
            public Label DotLabel = null!;
        }

        public readonly VisualElement Bar;
        public readonly Button Fab;
        readonly Dictionary<BaseDistrict, Tab> _tabs = new();
        string _key = "";
        string _fabKey = "";

        static readonly Color Idle = new(0.435f, 0.498f, 0.659f);

        public DistrictHud(VisualElement root, float bottom)
        {
            Bar = new VisualElement();
            Bar.style.position = Position.Absolute;
            Bar.style.left = 72;
            Bar.style.right = 72;
            Bar.style.bottom = bottom;
            Bar.style.height = 46;
            Bar.style.flexDirection = FlexDirection.Row;
            Holo.Frame(Bar, UiTheme.A(UiTheme.Bg, 0.88f), UiTheme.A(UiTheme.Accent, 0.55f), 9f);
            foreach (var (district, label) in new[]
            {
                (BaseDistrict.Command, "COMMAND"), (BaseDistrict.MiningBelt, "MINES"), (BaseDistrict.Frontier, "FRONTIER"),
            })
            {
                var d = district;
                var tab = new Tab();
                var b = new Button(() => BaseGlobe.Instance?.FlyTo(d)) { text = "" };
                b.clicked += GameAudio.Tap;
                b.style.flexGrow = 1;
                b.style.flexBasis = 0;
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
                Holo.Frame(b, Color.clear, Color.clear, 0f, 0f, FrameShape.Plain);
                tab.Name = Widgets.Heading(label, 10, Idle, 1.4f);
                tab.Name.pickingMode = PickingMode.Ignore;
                b.Add(tab.Name);
                tab.Count = Widgets.Text("", 11, Idle, bold: true);
                tab.Count.pickingMode = PickingMode.Ignore;
                tab.Count.style.marginTop = 1;
                b.Add(tab.Count);
                tab.Dot = new VisualElement { pickingMode = PickingMode.Ignore };
                tab.Dot.style.position = Position.Absolute;
                tab.Dot.style.top = 4;
                tab.Dot.style.right = 6;
                tab.Dot.style.minWidth = 14;
                tab.Dot.style.height = 14;
                tab.Dot.style.borderTopLeftRadius = 7;
                tab.Dot.style.borderTopRightRadius = 7;
                tab.Dot.style.borderBottomLeftRadius = 7;
                tab.Dot.style.borderBottomRightRadius = 7;
                tab.Dot.style.backgroundColor = UiTheme.Magenta;
                tab.Dot.style.justifyContent = Justify.Center;
                tab.Dot.style.alignItems = Align.Center;
                tab.DotLabel = Widgets.Text("", 9, Color.white, bold: true);
                tab.DotLabel.pickingMode = PickingMode.Ignore;
                tab.Dot.Add(tab.DotLabel);
                tab.Dot.style.display = DisplayStyle.None;
                b.Add(tab.Dot);
                if (district != BaseDistrict.Frontier)
                {
                    b.style.borderRightWidth = 1;
                    b.style.borderRightColor = UiTheme.A(UiTheme.Accent, 0.2f);
                }
                tab.Button = b;
                _tabs[district] = tab;
                Bar.Add(b);
            }
            root.Add(Bar);

            Fab = Widgets.Fab(Icon.Orbit, "ORBIT", OnFab, 46f);
            Fab.style.position = Position.Absolute;
            Fab.style.right = 12;
            Fab.style.top = 112;
            root.Add(Fab);
        }

        static void OnFab()
        {
            var globe = BaseGlobe.Instance;
            if (globe == null) return;
            if (globe.InOrbit) globe.Land();
            else if (globe.District == BaseDistrict.Command) globe.Orbit();
            else globe.FlyTo(BaseDistrict.Command);
        }

        public void Refresh(GameState state)
        {
            var globe = BaseGlobe.Instance;
            var active = globe != null && !globe.InOrbit ? globe.District : (BaseDistrict?)null;
            var (c1, t1) = BaseLayout.Count(state, BaseDistrict.Command);
            var (c2, t2) = BaseLayout.Count(state, BaseDistrict.MiningBelt);
            var (c3, t3) = BaseLayout.Count(state, BaseDistrict.Frontier);
            int open = BaseLayout.OpenPads(state, BaseDistrict.MiningBelt);
            string key = $"{active}|{c1}/{t1}|{c2}/{t2}|{c3}/{t3}|{open}";
            if (key != _key)
            {
                _key = key;
                Paint(BaseDistrict.Command, active, $"{c1}/{t1}", 0);
                Paint(BaseDistrict.MiningBelt, active, $"{c2}/{t2}", open);
                Paint(BaseDistrict.Frontier, active, $"{c3}/{t3}", 0);
            }

            var (icon, caption) = globe == null || !globe.InOrbit
                ? globe != null && globe.District != BaseDistrict.Command ? (Icon.Home, "HOME") : (Icon.Orbit, "ORBIT")
                : (Icon.Land, "LAND");
            if (caption != _fabKey)
            {
                _fabKey = caption;
                Widgets.SetCaption(Fab, caption);
                Fab.Query<IconElement>().ForEach(i => i.Icon = icon);
            }
        }

        void Paint(BaseDistrict d, BaseDistrict? active, string count, int open)
        {
            var tab = _tabs[d];
            bool on = active == d;
            var color = on ? UiTheme.Accent : Idle;
            Holo.Set(tab.Button, on ? UiTheme.A(UiTheme.Accent, 0.16f) : Color.clear, Color.clear);
            tab.Name.style.color = color;
            tab.Count.text = count;
            tab.Count.style.color = on ? UiTheme.Text : Idle;
            tab.Dot.style.display = open > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            tab.DotLabel.text = open.ToString();
        }
    }
}
