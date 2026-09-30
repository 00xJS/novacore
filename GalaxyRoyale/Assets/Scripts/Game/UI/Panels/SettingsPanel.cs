// SETTINGS (profile › SETTINGS): sound, haptics and music; text size,
// colour-blind colours and reduced motion; which notifications the game sends
// while it's closed; Game Center; and Report a problem. Everything is saved on
// this device as it changes.
using System;
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public static class SettingsPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content, footer) = Widgets.ModalPanelFooter("SETTINGS", ui.CloseModal, 84f);

            // ---- sound ----
            content.Add(Header("SOUND", 0));
            content.Add(Toggle("Sound effects", () => GameAudio.SoundOn, on =>
            {
                GameAudio.SoundOn = on;
                GameAudio.Play(Sfx.Toggle);
            }));
            content.Add(Toggle("Haptics", () => GameAudio.HapticsOn, on =>
            {
                GameAudio.HapticsOn = on;
                GameAudio.Buzz(Haptic.Medium); // feel it switch on
            }));
            content.Add(Toggle("Music", () => Settings.MusicOn, on =>
            {
                Settings.MusicOn = on;
                Music.Sync();
            }));
            var volRow = Widgets.HBox(Justify.SpaceBetween);
            volRow.style.marginTop = 6;
            volRow.Add(Widgets.Text("Music volume", 12, UiTheme.Dim));
            var volLabel = Widgets.Text("", 12, UiTheme.Accent, bold: true);
            volRow.Add(volLabel);
            content.Add(volRow);
            var volume = new SliderInt(0, 100) { value = (int)Math.Round(Settings.MusicVolume * 100) };
            volLabel.text = $"{volume.value}%";
            volume.RegisterValueChangedCallback(e =>
            {
                Settings.MusicVolume = e.newValue / 100f;
                volLabel.text = $"{e.newValue}%";
                Music.Sync();
            });
            content.Add(volume);
            content.Add(Note("Music and sound follow the silent switch.", 2));

            // ---- display & accessibility ----
            content.Add(Header("DISPLAY & ACCESSIBILITY", 14));
            content.Add(Widgets.Text("Text size", 12, UiTheme.Text));
            var sizeRow = Widgets.HBox(Justify.SpaceBetween);
            sizeRow.style.marginTop = 4;
            var sizeButtons = new System.Collections.Generic.List<(TextSize size, Button button)>();
            void SyncSizes()
            {
                foreach (var (size, button) in sizeButtons) Widgets.SetButtonHighlight(button, size == Settings.TextSize);
            }
            foreach (var (label, size) in new[] { ("NORMAL", TextSize.Normal), ("LARGE", TextSize.Large), ("LARGER", TextSize.Larger) })
            {
                var choice = size;
                var b = Widgets.TextButton(label, () =>
                {
                    if (Settings.TextSize == choice) return;
                    Settings.TextSize = choice;
                    Open(ctx); // rebuild at the new size
                    ui.Toast("Text size set — screens use it as they open; the top bar after a restart", Icon.Info, UiTheme.Accent);
                }, 11);
                b.style.width = Length.Percent(32f);
                sizeButtons.Add((choice, b));
                sizeRow.Add(b);
            }
            SyncSizes();
            content.Add(sizeRow);
            content.Add(Toggle("Colour-blind colours", () => Settings.ColorBlind, on =>
            {
                Settings.ColorBlind = on;
                Open(ctx);
            }, "Blue and orange instead of green and red for good and bad news."));
            content.Add(Toggle("Reduced motion", () => Settings.ReducedMotion, on => Settings.ReducedMotion = on,
                "No pulsing or orbiting on the map; battles open on their report, with the replay one tap away."));

            // ---- notifications ----
            content.Add(Header("NOTIFICATIONS", 14));
            content.Add(Note("Sent while the game is closed.", 0));
            foreach (Settings.Notify kind in Enum.GetValues(typeof(Settings.Notify)))
            {
                var k = kind;
                content.Add(Toggle(Settings.NotifyLabel(k), () => Settings.NotifyOn(k), on => Settings.SetNotify(k, on)));
            }


            // ---- Game Center ----
            content.Add(Header("GAME CENTER", 14));
            content.Add(Toggle("Game Center", () => GameCenter.Enabled, on =>
            {
                GameCenter.Enabled = on;
                if (on) GameCenter.Sync(ctx.State, ok => ui.Toast(ok ? "Signed in to Game Center"
                    : "Game Center sign-in didn't complete — try again later", Icon.Trophy, ok ? UiTheme.Good : UiTheme.Dim));
            }, "Posts your might to the Game Center leaderboard and unlocks your achievements there. " +
               "It signs in to Game Center when you turn it on."));

            // ---- help ----
            content.Add(Header("HELP", 14));
            var handbook = Widgets.IconButton(Icon.Info, "COMMANDER'S HANDBOOK", () => HandbookPanel.Open(ctx), 11);
            handbook.name = "tut-handbook";
            handbook.style.marginTop = 6;
            content.Add(handbook);
            content.Add(Note("Every system in the game, a topic at a time.", 4));
            bool training = ctx.State != null && Sim.Systems.TutorialSystem.Active(ctx.State);
            var train = Widgets.IconButton(Icon.Star, training ? "LEAVE THE TRAINING" : "START THE TRAINING", () =>
            {
                if (ctx.State == null) return;
                if (Sim.Systems.TutorialSystem.Active(ctx.State))
                {
                    Sim.Systems.TutorialSystem.Skip(ctx.State);
                    ui.Toast("Training closed — start it again here whenever you like");
                }
                else
                {
                    Sim.Systems.TutorialSystem.Restart(ctx.State);
                    ui.Toast("Training started: follow the card at the bottom", Icon.Star, UiTheme.Energy);
                }
                LocalBootstrap.RequestSync();
                ui.CloseModal();
            }, 11);
            train.style.marginTop = 6;
            content.Add(train);
            content.Add(Note("A guided run through the controls, one step at a time. Steps you've already done " +
                "pass by themselves.", 4));
            var tour = Widgets.IconButton(Icon.Compass, "REPLAY THE TOUR", () => GlobeTour.Start(), 11);
            tour.style.marginTop = 6;
            content.Add(tour);
            content.Add(Note("Half a minute round your globe: the colony, the Mining Belt, the Frontier, the Spaceport, " +
                "the Wilds and the galaxy.", 4));
            var report = Widgets.IconButton(Icon.Warning, "REPORT A PROBLEM", () =>
            {
                bool shared = ProblemReport.Share(ctx.State);
                if (!shared) ui.Toast("Problem report copied to the clipboard", Icon.Check, UiTheme.Good);
            }, 11);
            report.style.marginTop = 6;
            content.Add(report);
            content.Add(Note("Opens the share sheet with a short report — the game version, your device, the state of " +
                "your galaxy and any recent errors. You read it first and choose where it goes; the game sends nothing.", 4));

            // ---- about (App Store: the privacy policy must be reachable in the app) ----
            content.Add(Header("ABOUT", 14));
            var links = Widgets.HBox(Justify.SpaceBetween);
            links.style.marginTop = 6;
            Button Link(string label, System.Action onTap)
            {
                var b = Widgets.TextButton(label, onTap, 10);
                b.style.width = Length.Percent(32f);
                links.Add(b);
                return b;
            }
            Link("PRIVACY POLICY", () => UnityEngine.Application.OpenURL(Links.Privacy));
            Link("SUPPORT", () => UnityEngine.Application.OpenURL(Links.Support));
            Link("CREDITS", () => CreditsPanel.Open(ctx));
            content.Add(links);
            content.Add(Note($"Galaxy Royale {UnityEngine.Application.version}", 6));

            footer.Add(Widgets.TextButton("DONE", ui.CloseModal, 12));
            ui.OpenModal(blocker);
        }

        /// <summary>A label with an ON / OFF button (and an optional note under it).</summary>
        static VisualElement Toggle(string label, Func<bool> get, Action<bool> set, string? note = null)
        {
            var box = new VisualElement();
            box.style.marginTop = 8;
            var row = Widgets.HBox(Justify.SpaceBetween);
            var text = Widgets.Text(label, 12, UiTheme.Text);
            text.style.flexShrink = 1f;
            text.style.whiteSpace = WhiteSpace.Normal;
            row.Add(text);
            Button? button = null;
            void Sync()
            {
                bool on = get();
                Widgets.SetCaption(button!, on ? "ON" : "OFF");
                Widgets.SetButtonHighlight(button!, on);
            }
            button = Widgets.TextButton("", () => { set(!get()); Sync(); }, 11);
            button.style.width = 64;
            button.style.flexShrink = 0f;
            row.Add(button);
            box.Add(row);
            if (note != null) box.Add(Note(note, 2));
            Sync();
            return box;
        }

        static Label Header(string text, float marginTop)
        {
            var l = Widgets.Text(text, 10, UiTheme.Accent, bold: true);
            l.style.marginTop = marginTop;
            l.style.marginBottom = 2;
            return l;
        }

        static Label Note(string text, float marginTop)
        {
            var l = Widgets.Text(text, 10, UiTheme.Dim);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = marginTop;
            return l;
        }
    }
}
