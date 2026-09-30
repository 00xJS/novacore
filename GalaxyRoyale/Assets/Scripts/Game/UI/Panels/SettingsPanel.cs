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

            // ---- AI writers ----
            content.Add(Header("AI WRITERS", 14));
            content.Add(Note("Optional. Point this at your own Galaxy Royale AI proxy (server/ai-proxy in the game's " +
                "repository) and Claude writes the Galactic Gazette, battle recaps and commanders' replies. Without it, " +
                "the game writes them itself. Only game facts are sent: names, battle numbers and news.", 0));
            var url = new TextField { value = AiWriter.ProxyUrl, maxLength = 200 };
            url.style.marginTop = 6;
            url.textEdition.placeholder = "https://galaxy-royale-ai.your-name.workers.dev";
            url.textEdition.hidePlaceholderOnFocus = true;
            content.Add(url);
            var aiRow = Widgets.HBox(Justify.SpaceBetween);
            aiRow.style.marginTop = 6;
            var aiStatus = Note(AiWriter.Configured ? "Set — the AI writes when it's reachable." : "Not set — the game writes its own.", 0);
            var saveUrl = Widgets.TextButton("SAVE", () =>
            {
                AiWriter.ProxyUrl = url.value;
                aiStatus.text = AiWriter.Configured ? "Saved — the AI writes when it's reachable."
                    : string.IsNullOrWhiteSpace(url.value) ? "Cleared — the game writes its own." : "That isn't a web address (https://…)";
            }, 11);
            saveUrl.style.width = Length.Percent(48f);
            var test = Widgets.TextButton("TEST", () =>
            {
                AiWriter.ProxyUrl = url.value;
                aiStatus.text = "Checking...";
                AiWriter.Check(ctx, (ok, message) =>
                {
                    aiStatus.text = message;
                    aiStatus.style.color = ok ? UiTheme.Good : UiTheme.Bad;
                });
            }, 11);
            test.style.width = Length.Percent(48f);
            aiRow.Add(saveUrl);
            aiRow.Add(test);
            content.Add(aiRow);
            aiStatus.style.marginTop = 4;
            content.Add(aiStatus);

            // ---- Game Center ----
            content.Add(Header("GAME CENTER", 14));
            content.Add(Toggle("Game Center", () => GameCenter.Enabled, on =>
            {
                GameCenter.Enabled = on;
                if (on) GameCenter.Sync(ctx.State, ok => ui.Toast(ok ? "Signed in to Game Center"
                    : "Game Center sign-in didn't complete — try again later", Icon.Trophy, ok ? UiTheme.Good : UiTheme.Dim));
            }, "Posts your might to the Game Center leaderboard and unlocks your achievements there. " +
               "It signs in to Game Center when you turn it on."));

            // ---- redeem a code ----
            content.Add(Header("REDEEM A CODE", 14));
            content.Add(Note("Got a code from the developer? Each code works once per game.", 2));
            var redeemRow = Widgets.HBox();
            redeemRow.style.marginTop = 6;
            var codeField = new TextField { maxLength = 32 };
            codeField.style.flexGrow = 1;
            codeField.style.marginRight = 8;
            codeField.style.marginLeft = 0;
            redeemRow.Add(codeField);
            var redeemStatus = Note("", 4);
            var redeem = Widgets.TextButton("REDEEM", () =>
            {
                var res = Sim.Systems.RedeemSystem.Redeem(ctx.State!, codeField.value ?? "", out var paid);
                if (!res.Ok)
                {
                    redeemStatus.text = res.Reason ?? "";
                    redeemStatus.style.color = UiTheme.Bad;
                    GameAudio.Feedback(Sfx.Error, Haptic.Error);
                    return;
                }
                var r = paid!.Resources;
                var parts = new System.Collections.Generic.List<string>();
                if (r.Gold > 0) parts.Add($"{UiTheme.FmtAmount(r.Gold * 1000)} gold");
                if (r.Quartz > 0) parts.Add($"{UiTheme.FmtAmount(r.Quartz * 1000)} quartz");
                if (r.Helium > 0) parts.Add($"{UiTheme.FmtAmount(r.Helium * 1000)} helium");
                if (paid.DarkMatter > 0) parts.Add($"{paid.DarkMatter} Dark Matter");
                string got = string.Join(" · ", parts);
                redeemStatus.text = $"{paid.Title} redeemed: {got}";
                redeemStatus.style.color = UiTheme.Good;
                codeField.value = "";
                GameAudio.Feedback(Sfx.Confirm, Haptic.Success);
                LocalBootstrap.RequestSync(); // save it right away
                ui.Toast($"{paid.Title}: {got}", Icon.Crate, UiTheme.Good);
            }, 11);
            redeemRow.Add(redeem);
            content.Add(redeemRow);
            content.Add(redeemStatus);

            // ---- help ----
            content.Add(Header("HELP", 14));
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
