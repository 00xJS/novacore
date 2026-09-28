// Commander profile — UI Toolkit port of v1's ProfilePanel: identity card
// (avatar, name, HQ, might), resource totals, empire stats, rename flow, and
// an account section (placeholder until Supabase lands in Phase B.5).
using System;
using System.Linq;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class ProfilePanel
    {
        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("COMMANDER", ui.CloseModal, 0f); // compact card
            var state = ctx.State!;

            // ---- identity card ----
            var card = Widgets.Row();
            var cardBox = Widgets.HBox(Justify.SpaceBetween);
            var idBox = Widgets.HBox();
            // Premade portrait; tap to pick another of the 10 styles.
            var avatar = Portraits.Avatar(state.Profile.AvatarSeed, state.Profile.Name, 40);
            avatar.style.marginRight = 10;
            avatar.RegisterCallback<PointerUpEvent>(_ => OpenAvatarPicker(ctx));
            idBox.Add(avatar);

            var nameCol = new VisualElement();
            var nameLabel = Widgets.Text(state.Profile.Name, 15, UiTheme.Text, bold: true);
            nameCol.Add(nameLabel);
            nameCol.Add(Widgets.Text($"HQ {state.HomeTile.X}, {state.HomeTile.Y}", 11, UiTheme.Accent));
            idBox.Add(nameCol);
            cardBox.Add(idBox);

            var mightCol = new VisualElement();
            mightCol.style.alignItems = Align.FlexEnd;
            mightCol.Add(Widgets.Text("MIGHT", 9, UiTheme.Dim));
            var mightLabel = Widgets.Text("", 15, UiTheme.Energy, bold: true);
            mightCol.Add(mightLabel);
            cardBox.Add(mightCol);
            card.Add(cardBox);

            int renameCost = Shop.ById["name-change"].PriceDM;
            var rename = Widgets.TextButton($"RENAME ({renameCost} DM)", () => OpenRename(ctx), 10);
            rename.style.marginTop = 8;
            card.Add(rename);
            content.Add(card);

            // ---- resources ----
            content.Add(SectionHeader("RESOURCES"));
            var resRow = Widgets.Row();
            var resBox = Widgets.HBox(Justify.SpaceAround);
            var resLabels = new Label[3];
            var resDefs = new[] { ("GOLD", UiTheme.Gold), ("QUARTZ", UiTheme.Quartz), ("HELIUM", UiTheme.Helium) };
            for (int i = 0; i < 3; i++)
            {
                var col = new VisualElement();
                col.style.alignItems = Align.Center;
                col.Add(Widgets.Text(resDefs[i].Item1, 9, resDefs[i].Item2, bold: true));
                resLabels[i] = Widgets.Text("", 13, UiTheme.Text);
                col.Add(resLabels[i]);
                resBox.Add(col);
            }
            resRow.Add(resBox);
            content.Add(resRow);

            // ---- empire stats ----
            content.Add(SectionHeader("EMPIRE"));
            var empireRow = Widgets.Row();
            var statLabels = new System.Collections.Generic.Dictionary<string, Label>();
            foreach (var statName in new[]
                { "Battles won", "Battles lost", "Marches sent", "Ships docked", "Tech levels", "Dark Matter" })
            {
                var line = Widgets.HBox(Justify.SpaceBetween);
                line.style.marginTop = 3;
                line.Add(Widgets.Text(statName, 11, UiTheme.Dim));
                var v = Widgets.Text("", 11, UiTheme.Text);
                statLabels[statName] = v;
                line.Add(v);
                empireRow.Add(line);
            }
            content.Add(empireRow);

            // ---- account (signed in / pending verification / guest) ----
            // ---- sound & haptics (user request 2026-09-27) ----
            content.Add(SectionHeader("SOUND & HAPTICS"));
            var fxRow = Widgets.HBox(Justify.SpaceBetween);
            Button? soundBtn = null, hapticsBtn = null;
            void SyncFx()
            {
                Widgets.SetCaption(soundBtn!, GameAudio.SoundOn ? "SOUND: ON" : "SOUND: OFF");
                Widgets.SetButtonHighlight(soundBtn!, GameAudio.SoundOn);
                Widgets.SetCaption(hapticsBtn!, GameAudio.HapticsOn ? "HAPTICS: ON" : "HAPTICS: OFF");
                Widgets.SetButtonHighlight(hapticsBtn!, GameAudio.HapticsOn);
            }
            soundBtn = Widgets.TextButton("", () =>
            {
                GameAudio.SoundOn = !GameAudio.SoundOn;
                GameAudio.Play(Sfx.Toggle);
                SyncFx();
            }, 11);
            hapticsBtn = Widgets.TextButton("", () =>
            {
                GameAudio.HapticsOn = !GameAudio.HapticsOn;
                GameAudio.Buzz(Haptic.Medium); // feel it switch on
                SyncFx();
            }, 11);
            soundBtn.style.width = Length.Percent(48f);
            hapticsBtn.style.width = Length.Percent(48f);
            fxRow.Add(soundBtn);
            fxRow.Add(hapticsBtn);
            content.Add(fxRow);
            SyncFx();

            content.Add(SectionHeader("ACCOUNT"));
            var accountRow = Widgets.Row();
            BuildAccountSection(ctx, accountRow);
            content.Add(accountRow);

            string cache = "";
            int lastRefreshTick = -1;
            refresh = () =>
            {
                // Everything shown moves at sim-tick rate — skip the per-frame
                // cache-key string build (it allocated every frame while open).
                if (state.Tick == lastRefreshTick) return;
                lastRefreshTick = state.Tick;
                string key = $"{state.Profile.Name}|{state.Resources.Total}|{state.Stats.MarchesSent}|{state.Stats.BattlesWon}|{state.Stats.BattlesLost}|{state.Premium.DarkMatter}";
                if (key == cache) return;
                cache = key;

                nameLabel.text = state.Profile.Name;
                mightLabel.text = PowerSystem.ComputePower(state).ToString("N0");
                resLabels[0].text = UiTheme.FmtAmount(state.Resources.Gold);
                resLabels[1].text = UiTheme.FmtAmount(state.Resources.Quartz);
                resLabels[2].text = UiTheme.FmtAmount(state.Resources.Helium);
                statLabels["Battles won"].text = state.Stats.BattlesWon.ToString();
                statLabels["Battles lost"].text = state.Stats.BattlesLost.ToString();
                statLabels["Marches sent"].text = state.Stats.MarchesSent.ToString();
                statLabels["Ships docked"].text = state.Ships.Values.Sum().ToString();
                statLabels["Tech levels"].text = state.Research.Values.Sum().ToString();
                statLabels["Dark Matter"].text = state.Premium.DarkMatter.ToString("N0");
            };
            refresh();
            return blocker;
        }

        static void BuildAccountSection(GameContext ctx, VisualElement row)
        {
            var ui = UIController.Instance!;

            void AddText(string text, UnityEngine.Color color)
            {
                var l = Widgets.Text(text, 11, color);
                l.style.whiteSpace = WhiteSpace.Normal;
                row.Add(l);
            }

            if (ctx.State is { } st)
                AddText(st.TestMode
                    ? "Galaxy mode: TESTING — playtest economy (chosen at NEW GAME)."
                    : "Galaxy mode: STANDARD (chosen at NEW GAME).", st.TestMode ? UiTheme.Energy : UiTheme.Accent);
            AddText("Your empire saves to this device automatically (every 30s + on exit), " +
                    "with a rolling backup copy.", UiTheme.Dim);

            // Starting over is double-confirmed so it can't be fat-fingered — it
            // also refounds the simulated galaxy (every rival starts fresh).
            var reset = Widgets.TextButton("RESET EMPIRE", () =>
                ConfirmPanel.Open(
                    "Reset this galaxy?\nYour empire AND all " +
                    $"{GalaxyRoyale.Sim.Bots.BotSystem.BotCount} rival commanders start over from scratch.",
                    "CONTINUE",
                    () => ConfirmPanel.Open(
                        "FINAL WARNING\nALL progression will be LOST forever. There is no undo.",
                        "RESET — LOSE EVERYTHING",
                        () => NewGamePanel.Open(test =>
                        {
                            LocalBootstrap.Instance?.ResetEmpire(test);
                            ui.CloseModal();
                            ui.Toast(test ? "A new TESTING galaxy is born" : "A new galaxy is born — good luck, Commander");
                        }, () => ui.OpenProfile()),
                        () => ui.OpenProfile()),
                    () => ui.OpenProfile()), 11);
            reset.style.marginTop = 8;
            row.Add(reset);
        }

        static VisualElement SectionHeader(string text)
        {
            var l = Widgets.Text(text, 11, UiTheme.Accent, bold: true);
            l.style.marginTop = 10;
            l.style.marginBottom = 4;
            return l;
        }

        /// <summary>Preset avatar styles (the 8 AvatarColors). Syncs everywhere —
        /// header pill, chat rows, rankings — via avatarSeed in the save + positions.</summary>
        static void OpenAvatarPicker(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("AVATAR STYLE", ui.OpenProfile, 40f);

            content.Add(Widgets.Text("Pick your commander's portrait.", 11, UiTheme.Dim));

            var grid = Widgets.HBox(Justify.SpaceAround);
            grid.style.marginTop = 12;
            grid.style.flexWrap = Wrap.Wrap;
            string name = ctx.State!.Profile.Name;
            for (int i = 0; i < UiTheme.AvatarCount; i++)
            {
                int seed = i;
                var option = Portraits.Avatar(seed, name, 56);
                option.style.marginBottom = 10;
                option.style.marginLeft = 4;
                option.style.marginRight = 4;
                if (ctx.State!.Profile.AvatarSeed == seed)
                    Widgets.SetBorder(option, UnityEngine.Color.white, 2.5f);
                option.RegisterCallback<PointerUpEvent>(_ =>
                {
                    ctx.State!.Profile.AvatarSeed = seed;
                    LocalBootstrap.RequestSync(); // save the new style right away
                    ui.Toast("Avatar updated");
                    ui.OpenProfile();
                });
                grid.Add(option);
            }
            content.Add(grid);

            var hint = Widgets.Text("5 women · 5 men — your portrait shows in rankings and your profile.", 10, UiTheme.Dim);
            hint.style.marginTop = 6;
            hint.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
            content.Add(hint);

            ui.OpenModal(blocker);
        }

        static void OpenRename(GameContext ctx)
        {
            var ui = UIController.Instance!;
            int cost = Shop.ById["name-change"].PriceDM;
            var (blocker, content) = Widgets.ModalPanel("RENAME COMMANDER", ui.OpenProfile, 36f);

            content.Add(Widgets.Text($"Costs {cost} Dark Matter. 2–16 characters.", 11, UiTheme.Dim));
            var field = new TextField { value = ctx.State!.Profile.Name, maxLength = 16 };
            field.style.marginTop = 8;
            content.Add(field);

            var status = Widgets.Text("", 11, UiTheme.Bad);
            status.style.marginTop = 4;
            content.Add(status);

            var confirm = Widgets.TextButton("CONFIRM RENAME", () =>
            {
                var res = ShopSystem.ChangeCommanderName(ctx.State!, field.value);
                if (res.Ok)
                {
                    ui.Toast($"You are now Commander {ctx.State!.Profile.Name}");
                    LocalBootstrap.RequestSync(); // save the new name right away
                    ui.OpenProfile();
                }
                else status.text = res.Reason ?? "";
            });
            confirm.style.marginTop = 10;
            content.Add(confirm);

            ui.OpenModal(blocker);
        }
    }
}
