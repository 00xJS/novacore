// Forecast block under the fleet sliders (raid launcher + camp composer): the
// predicted outcome of the picked fleet against the garrison the player has
// intel on. Combat is deterministic, so the only uncertainty is what changes
// before the fleet arrives.
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Combat;

namespace GalaxyRoyale.Game.UI
{
    public sealed class ForecastView
    {
        public readonly VisualElement Root = new();
        readonly VisualElement _head = new();
        readonly Label _detail;
        readonly Label _note;

        public ForecastView()
        {
            Root.style.marginTop = 8;
            Root.style.paddingLeft = 10;
            Root.style.paddingRight = 10;
            Root.style.paddingTop = 6;
            Root.style.paddingBottom = 6;
            Root.style.backgroundColor = UiTheme.PanelLight;
            Widgets.SetBorder(Root, UiTheme.Stroke, 1f);
            _detail = Widgets.Text("", 11, UiTheme.Text);
            _detail.style.whiteSpace = WhiteSpace.Normal;
            _detail.style.marginTop = 2;
            _note = Widgets.Text("", 10, UiTheme.Dim);
            _note.style.whiteSpace = WhiteSpace.Normal;
            _note.style.marginTop = 2;
            Root.Add(_head);
            Root.Add(_detail);
            Root.Add(_note);
        }

        public void Hide() => Root.style.display = DisplayStyle.None;

        /// <summary>No garrison intel yet — say how to get it instead of guessing.</summary>
        public void NeedsIntel(string hint)
        {
            Root.style.display = DisplayStyle.Flex;
            SetHead(Icon.Eye, "FORECAST UNAVAILABLE", UiTheme.Dim);
            _detail.text = hint;
            _detail.style.color = UiTheme.Dim;
            _note.style.display = DisplayStyle.None;
        }

        public void Show(BattleForecast f, string? note)
        {
            Root.style.display = DisplayStyle.Flex;
            _detail.style.color = UiTheme.Text;
            string rounds = f.Rounds == 1 ? "1 round" : $"{f.Rounds} rounds";
            string yourLosses = f.YourLosses == 0
                ? "no losses expected"
                : $"you lose {f.YourLosses} of {f.YourShips}: {LossList(f.YourLossesByHull)}";

            if (f.Unopposed)
            {
                SetHead(Icon.Check, "FORECAST · UNOPPOSED", UiTheme.Good);
                _detail.text = "No ships docked to defend — the raid lands without a fight.";
            }
            else if (f.Winner == BattleWinner.Attacker)
            {
                SetHead(Icon.Swords, "FORECAST · VICTORY", UiTheme.Good);
                _detail.text = $"Their fleet is destroyed in {rounds} · {yourLosses}";
            }
            else if (f.Winner == BattleWinner.Defender)
            {
                SetHead(Icon.Warning, "FORECAST · DEFEAT", UiTheme.Bad);
                _detail.text = $"Your whole fleet is lost in {rounds} · they lose {f.EnemyLosses} of {f.EnemyShips}";
            }
            else if (f.Wiped && f.EnemyLosses >= f.EnemyShips)
            {
                SetHead(Icon.Warning, "FORECAST · MUTUAL DESTRUCTION", UiTheme.Energy);
                _detail.text = $"Both fleets are destroyed in {rounds} — nothing comes home.";
            }
            else
            {
                SetHead(Icon.Warning, "FORECAST · STALEMATE", UiTheme.Energy);
                _detail.text = $"Neither side breaks after {rounds} · {yourLosses} · they lose {f.EnemyLosses} of {f.EnemyShips}";
            }

            _note.text = note ?? "";
            _note.style.display = string.IsNullOrEmpty(note) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void SetHead(Icon icon, string text, UnityEngine.Color color)
        {
            _head.Clear();
            _head.Add(Widgets.IconText(icon, text, 11, color, bold: true));
        }

        static string LossList(Dictionary<HullId, int> byHull)
        {
            var parts = new List<string>();
            foreach (var hull in Ships.All)
                if (byHull.TryGetValue(hull, out var n) && n > 0)
                    parts.Add($"{n}×\u00A0{Ships.Defs[hull].Name}"); // no break inside "2× Fighter"
            return string.Join(", ", parts);
        }
    }
}
