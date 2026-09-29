// SETTINGS (profile › SETTINGS): sound, haptics and music; text size,
// colour-blind colours and reduced motion; and which notifications the game
// sends while it's closed. Everything is saved on this device as it changes.
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
