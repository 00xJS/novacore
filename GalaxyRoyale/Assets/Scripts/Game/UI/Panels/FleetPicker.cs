// Per-hull sliders for "which ships go" (user spec: choose what you send, not
// an auto all-docked fleet) — shared by the intercept and garrison screens.
// Probes stay home; ALL DOCKED / CLEAR fill or empty every slider.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;

namespace GalaxyRoyale.Game.UI
{
    public sealed class FleetPicker
    {
        readonly Dictionary<HullId, SliderInt> _picks = new();
        readonly Dictionary<HullId, Label> _counts = new();

        /// <summary>True when there's nothing docked to send.</summary>
        public bool Empty => _picks.Count == 0;

        /// <param name="changed">Runs after every slider move or quick fill.</param>
        public FleetPicker(VisualElement content, GameState state, Action changed)
        {
            string lastClass = "";
            foreach (var hull in Ships.All)
            {
                var h = hull;
                if (h == HullId.Probe) continue;
                int docked = state.Ships.TryGetValue(h, out var d) ? d : 0;
                if (docked <= 0) continue;
                var def = Ships.Defs[h];
                if (def.Class != lastClass)
                {
                    lastClass = def.Class;
                    var classHeader = Widgets.Text(def.Class.ToUpper(), 10, UiTheme.Accent, bold: true);
                    classHeader.style.marginTop = 8;
                    content.Add(classHeader);
                }
                var row = Widgets.Row();
                row.style.marginTop = 6;
                var rowHead = Widgets.HBox(Justify.SpaceBetween);
                rowHead.Add(Widgets.Text(def.Name, 12, UiTheme.Text, bold: true));
                var count = Widgets.Text("0", 13, UiTheme.Accent, bold: true);
                rowHead.Add(count);
                row.Add(rowHead);
                row.Add(Widgets.Text($"docked {docked} · atk {def.Atk} · shd {def.Shield} · speed {def.Speed}", 9, UiTheme.Dim));
                var slider = new SliderInt(0, docked) { value = 0 };
                slider.RegisterValueChangedCallback(_ => { count.text = slider.value.ToString(); changed(); });
                row.Add(slider);
                content.Add(row);
                _picks[h] = slider;
                _counts[h] = count;
            }
            if (_picks.Count == 0)
            {
                var none = Widgets.Text("No combat ships docked — build some in the Shipyard first.", 12, UiTheme.Bad);
                none.style.whiteSpace = WhiteSpace.Normal;
                none.style.marginTop = 6;
                content.Add(none);
                return;
            }

            var quick = Widgets.HBox(Justify.SpaceAround);
            quick.style.marginTop = 6;
            var all = Widgets.TextButton("ALL DOCKED", () =>
            {
                foreach (var kv in _picks)
                    kv.Value.SetValueWithoutNotify(state.Ships.TryGetValue(kv.Key, out var d) ? d : 0);
                Sync();
                changed();
            }, 10);
            all.style.width = Length.Percent(47f);
            var clear = Widgets.TextButton("CLEAR", () =>
            {
                foreach (var s in _picks.Values) s.SetValueWithoutNotify(0);
                Sync();
                changed();
            }, 10);
            clear.style.width = Length.Percent(47f);
            quick.Add(all);
            quick.Add(clear);
            content.Add(quick);
        }

        void Sync()
        {
            foreach (var kv in _picks) _counts[kv.Key].text = kv.Value.value.ToString();
        }

        public Dictionary<HullId, int> Fleet()
        {
            var fleet = new Dictionary<HullId, int>();
            foreach (var kv in _picks)
                if (kv.Value.value > 0) fleet[kv.Key] = kv.Value.value;
            return fleet;
        }
    }
}
