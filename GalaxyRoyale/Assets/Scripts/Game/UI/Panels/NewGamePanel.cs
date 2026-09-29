// NEW GAME setup (user request 2026-09-27): choose STANDARD — the real climb
// from a small colony — or TESTING — the playtest kit (500K of everything,
// 1M Dark Matter, speed-ups, free warps). The Commander's Path quests run in
// both. The difficulty (EASY / STANDARD / BRUTAL) applies to either mode and
// can be changed later in the profile. Nothing is erased until START is tapped.
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Game.UI
{
    public static class NewGamePanel
    {
        /// <param name="onStart">Called with testMode = true for TESTING, and the difficulty picked.</param>
        /// <param name="onBack">Back out without starting anything.</param>
        public static void Open(Action<bool, Difficulty> onStart, Action onBack)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.FullPage("NEW GALAXY", onBack);

            var intro = Widgets.Text(
                "Choose how you start. The Commander's Path quests guide your first steps either way.",
                12, UiTheme.Dim);
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
            intro.style.marginTop = 12;
            intro.style.marginBottom = 18;
            intro.style.paddingLeft = 12;
            intro.style.paddingRight = 12;
            content.Add(intro);

            // ---- difficulty ----
            var difficulty = Difficulty.Standard;
            var diffBox = new VisualElement();
            diffBox.style.marginLeft = 16;
            diffBox.style.marginRight = 16;
            diffBox.style.marginBottom = 16;
            diffBox.Add(Widgets.Text("DIFFICULTY", 11, UiTheme.Dim, bold: true));
            var diffRow = Widgets.HBox(Justify.SpaceBetween);
            diffRow.style.marginTop = 6;
            var pitch = Widgets.Text("", 11, UiTheme.Dim);
            pitch.style.whiteSpace = WhiteSpace.Normal;
            pitch.style.marginTop = 6;
            var diffButtons = new System.Collections.Generic.List<(Difficulty d, Button b)>();
            void Sync()
            {
                foreach (var (d, b) in diffButtons) Widgets.SetButtonHighlight(b, d == difficulty);
                pitch.text = Difficulties.Pitch(difficulty);
            }
            foreach (var d in Difficulties.All)
            {
                var choice = d;
                var b = Widgets.TextButton(Difficulties.Name(d), () => { difficulty = choice; Sync(); }, 12);
                b.style.width = Length.Percent(32f);
                diffButtons.Add((d, b));
                diffRow.Add(b);
            }
            diffBox.Add(diffRow);
            diffBox.Add(pitch);
            content.Add(diffBox);
            Sync();

            var start = Balance.StartResources();
            content.Add(ModeCard("STANDARD", UiTheme.Accent,
                "The real climb — build a small colony into a power the galaxy fears.",
                new[]
                {
                    $"{start.Gold} gold · {start.Quartz} quartz · {start.Helium} helium",
                    "No Dark Matter, no free speed-ups",
                    "Quest rewards carry you through the early game",
                },
                "START STANDARD", () => onStart(false, difficulty)));

            content.Add(ModeCard("TESTING", UiTheme.Magenta, // magenta, so it never reads as a second orange
                "A sandbox for playtesting every system without waiting.",
                new[]
                {
                    $"{Balance.TestModeResources / 1000}K of every resource",
                    $"{Balance.TestModeDarkMatter / 1_000_000}M Dark Matter · {Balance.TestModeSpeedupCount}× every speed-up",
                    "Free warps on the map",
                },
                "START TESTING", () => onStart(true, difficulty)));

            ui.OpenModal(blocker);
        }

        static VisualElement ModeCard(string title, UnityEngine.Color color, string pitch,
            string[] perks, string buttonLabel, Action onTap)
        {
            var card = new VisualElement();
            card.style.marginLeft = 16;
            card.style.marginRight = 16;
            card.style.marginBottom = 16;
            card.style.paddingLeft = 16;
            card.style.paddingRight = 16;
            card.style.paddingTop = 14;
            card.style.paddingBottom = 14;
            Holo.Frame(card, UiTheme.Panel, color, 11f, 1.5f);

            card.Add(Widgets.Text(title, 18, color, bold: true));
            var pitchLabel = Widgets.Text(pitch, 12, UiTheme.Text);
            pitchLabel.style.whiteSpace = WhiteSpace.Normal;
            pitchLabel.style.marginTop = 4;
            pitchLabel.style.marginBottom = 8;
            card.Add(pitchLabel);
            foreach (var perk in perks)
            {
                var line = Widgets.IconText(Icon.Check, perk, 11, UiTheme.Dim);
                line.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                line.style.marginTop = 3;
                card.Add(line);
            }
            var button = Widgets.Primary(Widgets.TextButton(buttonLabel, onTap, 14), color);
            button.style.height = 46;
            button.style.marginTop = 12;
            card.Add(button);
            return card;
        }
    }
}
