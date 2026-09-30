// The globe base's controls (2026-09-28): district tabs above the news ticker
// (COMMAND, MINES, FRONTIER, PORT and WILDS, each with its count and a dot for
// something waiting there) and the button at the top right that goes to ORBIT,
// back HOME to the Command district, or LANDs from orbit. BaseGlobe does the
// flying. Over the Wilds (2026-09-29) a header names them, counts the charted
// sectors and the drones' haul, and SURVEY NEXT starts the nearest survey.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;
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
        /// <summary>Over the Wilds: their name, the charted count and SURVEY NEXT.</summary>
        public readonly VisualElement WildsHud;
        readonly GameContext _ctx;
        readonly Dictionary<BaseDistrict, Tab> _tabs = new();
        readonly Label _wildsSub;
        readonly Button _survey;
        string _key = "";
        string _fabKey = "";
        string _wildsKey = "";
        bool _visible = true;

        static readonly Color Idle = new(0.435f, 0.498f, 0.659f);

        public DistrictHud(VisualElement root, float bottom, GameContext ctx)
        {
            _ctx = ctx;
            Bar = new VisualElement { name = "tut-districts" };
            Bar.style.position = Position.Absolute;
            Bar.style.left = 66;
            Bar.style.right = 66;
            Bar.style.bottom = bottom;
            Bar.style.height = 46;
            Bar.style.flexDirection = FlexDirection.Row;
            Holo.Frame(Bar, UiTheme.A(UiTheme.Bg, 0.88f), UiTheme.A(UiTheme.Accent, 0.55f), 9f);
            var tabs = new[]
            {
                (BaseDistrict.Command, "COMMAND"), (BaseDistrict.MiningBelt, "MINES"), (BaseDistrict.Frontier, "FRONTIER"),
                (BaseDistrict.Spaceport, "PORT"), (BaseDistrict.Wilds, "WILDS"),
            };
            foreach (var (district, label) in tabs)
            {
                var d = district;
                var tab = new Tab();
                var b = new Button(() => BaseGlobe.Instance?.FlyTo(d)) { text = "", name = $"tut-district-{d}" };
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
                // Five tabs share the bar: a smaller, tighter label than the old three.
                tab.Name = Widgets.Heading(label, 8, Idle, 0.5f);
                tab.Name.pickingMode = PickingMode.Ignore;
                b.Add(tab.Name);
                tab.Count = Widgets.Text("", 10, Idle, bold: true);
                tab.Count.pickingMode = PickingMode.Ignore;
                tab.Count.style.marginTop = 1;
                b.Add(tab.Count);
                tab.Dot = Widgets.CountBubble(15f, 9, UiTheme.Magenta, out tab.DotLabel);
                tab.Dot.style.top = 3;
                tab.Dot.style.right = 3;
                tab.Dot.style.display = DisplayStyle.None;
                b.Add(tab.Dot);
                if (district != BaseDistrict.Wilds)
                {
                    b.style.borderRightWidth = 1;
                    b.style.borderRightColor = UiTheme.A(UiTheme.Accent, 0.2f);
                }
                tab.Button = b;
                _tabs[district] = tab;
                Bar.Add(b);
            }
            root.Add(Bar);

            WildsHud = new VisualElement();
            WildsHud.style.position = Position.Absolute;
            WildsHud.style.left = 20;
            WildsHud.style.right = 20;
            WildsHud.style.bottom = bottom + 46 + 12;
            WildsHud.style.alignItems = Align.Center;
            WildsHud.pickingMode = PickingMode.Ignore;
            var title = Widgets.Heading("THE WILDS", 12, UiTheme.Accent, 4f);
            title.pickingMode = PickingMode.Ignore;
            title.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 8f, color = UiTheme.A(UiTheme.Accent, 0.8f) };
            WildsHud.Add(title);
            _wildsSub = Widgets.Text("", 11, UiTheme.Dim);
            _wildsSub.pickingMode = PickingMode.Ignore;
            _wildsSub.style.marginTop = 2;
            _wildsSub.style.unityTextAlign = TextAnchor.MiddleCenter;
            WildsHud.Add(_wildsSub);
            _survey = Widgets.Primary(Widgets.IconButton(Icon.Compass, "SURVEY NEXT SECTOR", OnSurvey, 11), UiTheme.Magenta);
            _survey.style.marginTop = 8;
            _survey.style.height = 38;
            _survey.style.paddingLeft = 16;
            _survey.style.paddingRight = 16;
            WildsHud.Add(_survey);
            WildsHud.style.display = DisplayStyle.None;
            root.Add(WildsHud);

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

        /// <summary>SURVEY NEXT: open the survey under way, or start the nearest one
        /// the teams can reach and turn it to the front.</summary>
        void OnSurvey()
        {
            var state = _ctx.State;
            var globe = BaseGlobe.Instance;
            var ui = UIController.Instance;
            if (state == null || ui == null) return;
            if (state.Wilds.Surveying >= 0)
            {
                globe?.FlyToSouth(WildsLayout.Place(state.Wilds.Surveying).Lon);
                SectorPanel.Open(_ctx, state.Wilds.Surveying);
                return;
            }
            int next = WildsSystem.NextSurvey(state, globe != null ? globe.Yaw : 0f);
            if (next < 0) { ui.Toast("Every sector the teams can reach is charted", Icon.Check, UiTheme.Good); return; }
            var res = WildsSystem.StartSurvey(state, next);
            if (!res.Ok)
            {
                GameAudio.Feedback(Sfx.Error, Haptic.Error);
                ui.Toast(res.Reason ?? "Can't survey right now", Icon.Warning, UiTheme.Bad);
                return;
            }
            GameAudio.Feedback(Sfx.Confirm, Haptic.Light);
            globe?.FlyToSouth(WildsLayout.Place(next).Lon);
            ui.Toast($"Surveying {WildsLayout.Name(next)} · {UiTheme.FmtDuration(WildsSystem.SurveySeconds(state, next))}",
                Icon.Compass, UiTheme.Magenta);
        }

        /// <summary>The bar (and the Wilds header) show on the base screen with nothing over it.</summary>
        public void SetVisible(bool on)
        {
            _visible = on;
            Bar.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) WildsHud.style.display = DisplayStyle.None;
        }

        public void Refresh(GameState state)
        {
            var globe = BaseGlobe.Instance;
            var active = globe != null && !globe.InOrbit ? globe.District : (BaseDistrict?)null;
            var (c1, t1) = BaseLayout.Count(state, BaseDistrict.Command);
            var (c2, t2) = BaseLayout.Count(state, BaseDistrict.MiningBelt);
            var (c3, t3) = BaseLayout.Count(state, BaseDistrict.Frontier);
            int open = BaseLayout.OpenPads(state, BaseDistrict.MiningBelt);
            int docked = 0;
            foreach (var n in state.Ships.Values) docked += Math.Max(0, n);
            int charted = WildsSystem.ChartedCount(state);
            int finds = 0;
            foreach (var s in state.Wilds.Sectors.Values)
                if (s.Find is WildsFind.Cache or WildsFind.Relic && !s.Claimed) finds++;
            string key = $"{active}|{c1}/{t1}|{c2}/{t2}|{c3}/{t3}|{open}|{docked}|{charted}|{finds}";
            if (key != _key)
            {
                _key = key;
                Paint(BaseDistrict.Command, active, $"{c1}/{t1}", 0);
                Paint(BaseDistrict.MiningBelt, active, $"{c2}/{t2}", open);
                Paint(BaseDistrict.Frontier, active, $"{c3}/{t3}", 0);
                Paint(BaseDistrict.Spaceport, active, docked >= 10_000 ? $"{docked / 1000}K" : docked.ToString("N0"), 0);
                Paint(BaseDistrict.Wilds, active, $"{charted}/{WildsLayout.Total}", finds);
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

            bool wilds = _visible && active == BaseDistrict.Wilds;
            WildsHud.style.display = wilds ? DisplayStyle.Flex : DisplayStyle.None;
            if (!wilds) return;
            int left = WildsSystem.SurveyLeft(state);
            int next = state.Wilds.Surveying >= 0 ? state.Wilds.Surveying : WildsSystem.NextSurvey(state, globe!.Yaw);
            string wkey = $"{charted}|{state.Wilds.Surveying}|{left}|{next}|{WildsSystem.Drones(state)}|{WildsSystem.ActiveDeposits(state)}";
            if (wkey == _wildsKey) return;
            _wildsKey = wkey;
            long haul = WildsSystem.ActiveDeposits(state) > 0 ? WildsSystem.HaulPerHourMilli(state) : 0;
            int drones = WildsSystem.Drones(state);
            _wildsSub.text = charted == 0
                ? "Uncharted land under survey fog · survey a sector to begin"
                : $"{charted} of {WildsLayout.Total} sectors charted · {drones} drone{(drones == 1 ? "" : "s")}" +
                  (haul > 0 ? $" bring home {UiTheme.FmtAmount(haul)}/h" : " idle");
            if (state.Wilds.Surveying >= 0)
                Widgets.SetCaption(_survey, $"SURVEYING {WildsLayout.Name(state.Wilds.Surveying)} · {UiTheme.FmtDuration(left)}");
            else if (next >= 0)
                Widgets.SetCaption(_survey, $"SURVEY NEXT SECTOR · {UiTheme.FmtDuration(WildsSystem.SurveySeconds(state, next)).ToUpperInvariant()}");
            else Widgets.SetCaption(_survey, "ALL REACHABLE SECTORS CHARTED");
        }

        void Paint(BaseDistrict d, BaseDistrict? active, string count, int dot)
        {
            var tab = _tabs[d];
            bool on = active == d;
            var color = on ? UiTheme.Accent : Idle;
            Holo.Set(tab.Button, on ? UiTheme.A(UiTheme.Accent, 0.16f) : Color.clear, Color.clear);
            tab.Name.style.color = color;
            tab.Count.text = count;
            tab.Count.style.color = on ? UiTheme.Text : Idle;
            tab.Dot.style.display = dot > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            tab.DotLabel.text = Widgets.BubbleCount(dot);
        }
    }
}
