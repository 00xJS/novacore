// The first-run tour (2026-09-29): half a minute round the globe for a new
// commander — the colony, the Mining Belt, the Frontier, the Spaceport, the
// Wilds and the galaxy from orbit — with a line on what each is for. The globe
// flies itself from stop to stop; NEXT moves on sooner and SKIP ends it. It
// runs after NEW GAME (the rookie hints follow it) and again from Settings.
using System;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Sim;
using Position = UnityEngine.UIElements.Position;

namespace GalaxyRoyale.Game.UI
{
    public static class GlobeTour
    {
        readonly struct Stop
        {
            public readonly BaseDistrict District;
            public readonly bool Orbit;
            public readonly string Title, Text;

            public Stop(BaseDistrict district, bool orbit, string title, string text)
            {
                District = district; Orbit = orbit; Title = title; Text = text;
            }
        }

        const float SecondsPerStop = 5.5f;

        static readonly Stop[] Stops =
        {
            new(BaseDistrict.Command, false, "YOUR COLONY",
                "Every building stands on its own pad. Tap one to upgrade it. The Command Center caps the rest, so raise it first."),
            new(BaseDistrict.MiningBelt, false, "THE MINING BELT",
                "A pad for every extra mine. More of them open each time the Command Center levels up."),
            new(BaseDistrict.Frontier, false, "THE FRONTIER",
                "Buildings for later: the Bastion's railguns, the Salvage Yard and the Drone Factory. The Exchange Terminal is your market."),
            new(BaseDistrict.Spaceport, false, "THE SPACEPORT",
                "Your docked fleet parks here. Tap the field to build ships and send them out."),
            new(BaseDistrict.Wilds, false, "THE WILDS",
                "Pull the planet up to tip it south. Survey the fog for deposits your drones harvest, supply caches and relics."),
            new(BaseDistrict.Command, true, "YOUR GALAXY",
                "249 rival commanders are out there, and they don't wait for you. Tap MAP to find them. Good luck, Commander."),
        };

        static VisualElement? s_card;

        public static bool Running => s_card != null;

        public static void Start(Action? onDone = null)
        {
            var ui = UIController.Instance;
            if (ui == null || Running) return;
            ui.CloseModal();
            ui.SwitchView(ViewId.Base);

            var card = new VisualElement { name = "globe-tour" };
            card.style.position = Position.Absolute;
            card.style.left = 16;
            card.style.right = 16;
            card.style.bottom = UiTheme.NavH + 22 + 16 + 46 + 14;
            card.style.paddingLeft = 16;
            card.style.paddingRight = 16;
            card.style.paddingTop = 14;
            card.style.paddingBottom = 14;
            Holo.Frame(card, UiTheme.A(UiTheme.Bg, 0.94f), UiTheme.Accent, 12f, 1.5f, FrameShape.Bevel, glow: true);

            var step = Widgets.Heading("", 9, UiTheme.Magenta, 2f);
            card.Add(step);
            var title = Widgets.Heading("", 16, UiTheme.Accent, 3f);
            title.style.marginTop = 4;
            title.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 8f, color = UiTheme.A(UiTheme.Accent, 0.7f) };
            card.Add(title);
            var text = Widgets.Text("", 13, UiTheme.Text);
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.marginTop = 6;
            card.Add(text);

            var (bar, fill, _) = Widgets.ProgressBar(4f);
            bar.style.marginTop = 12;
            fill.style.backgroundColor = UiTheme.Accent;
            card.Add(bar);

            var buttons = Widgets.HBox(Justify.SpaceBetween);
            buttons.style.marginTop = 12;
            int index = -1;
            float until = 0f;
            IVisualElementScheduledItem? loop = null;

            void End()
            {
                loop?.Pause();
                card.RemoveFromHierarchy();
                s_card = null;
                BaseGlobe.Instance?.FlyTo(BaseDistrict.Command);
                onDone?.Invoke();
            }

            void Go(int i)
            {
                if (i >= Stops.Length) { End(); return; }
                index = i;
                var s = Stops[i];
                var globe = BaseGlobe.Instance;
                if (globe != null)
                {
                    globe.FlyTo(s.District);
                    if (s.Orbit) globe.Orbit();
                }
                step.text = $"TOUR · {i + 1} OF {Stops.Length}";
                title.text = s.Title;
                text.text = s.Text;
                until = Time.unscaledTime + SecondsPerStop;
                GameAudio.Play(Sfx.Toggle);
            }

            var skip = Widgets.TextButton("SKIP TOUR", End, 11);
            skip.style.width = Length.Percent(40f);
            skip.style.height = 38;
            buttons.Add(skip);
            var next = Widgets.Primary(Widgets.TextButton("NEXT", () => Go(index + 1), 11));
            next.style.width = Length.Percent(40f);
            next.style.height = 38;
            buttons.Add(next);
            card.Add(buttons);

            s_card = card;
            ui.ShowOverlay(card);
            Go(0);
            loop = card.schedule.Execute(() =>
            {
                float left = until - Time.unscaledTime;
                fill.style.width = Length.Percent(Mathf.Clamp01(1f - left / SecondsPerStop) * 100f);
                if (left <= 0f) Go(index + 1);
            }).Every(50);
        }
    }
}
