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
            // Commander title (earned in AWARDS).
            string? title = AchievementSystem.TitleText(state);
            nameCol.Add(Widgets.Text(title ?? "No title yet", 10, title != null ? UiTheme.Energy : UiTheme.Dim,
                bold: title != null));
            var myClan = ctx.Bots?.FindClan(state.ClanId);
            nameCol.Add(Widgets.Text(myClan != null ? ClanSystem.Label(myClan) : "No clan", 10,
                myClan != null ? UiTheme.Good : UiTheme.Dim, bold: myClan != null));
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
            var idButtons = Widgets.HBox(Justify.SpaceBetween);
            idButtons.style.marginTop = 8;
            var rename = Widgets.TextButton($"RENAME ({renameCost} DM)", () => OpenRename(ctx), 10);
            rename.style.width = Length.Percent(49f);
            idButtons.Add(rename);
            var titles = Widgets.IconButton(Icon.Trophy, "TITLES & AWARDS", ui.OpenAchievements, 10);
            titles.style.width = Length.Percent(49f);
            idButtons.Add(titles);
            card.Add(idButtons);

            // Commander level, XP and the way into the skill tree.
            var cmdBox = Widgets.HBox(Justify.SpaceBetween);
            cmdBox.style.marginTop = 8;
            var cmdCol = new VisualElement();
            cmdCol.style.flexGrow = 1f;
            cmdCol.style.flexShrink = 1f;
            cmdCol.style.marginRight = 8;
            var cmdLabel = Widgets.Text("", 11, UiTheme.Energy, bold: true);
            cmdCol.Add(cmdLabel);
            var (xpBar, xpFill, xpText) = Widgets.ProgressBar(14f);
            xpBar.style.marginTop = 3;
            xpText.style.fontSize = 9;
            cmdCol.Add(xpBar);
            cmdBox.Add(cmdCol);
            var skills = Widgets.IconButton(Icon.Star, "SKILLS", ui.OpenCommander, 10);
            skills.style.width = Length.Percent(34f);
            cmdBox.Add(skills);
            card.Add(cmdBox);
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
            {
                "Battles won", "Battles lost", "Camps cleared", "Raids won", "Raids repelled", "Marches sent",
                "Ships built", "Ships docked", "Tech levels", "Achievements", "Best season", "Dark Matter",
            })
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

            // ---- difficulty (changeable any time; NEW GAME picks the first) ----
            content.Add(SectionHeader("DIFFICULTY"));
            var diffRow = Widgets.HBox(Justify.SpaceBetween);
            foreach (var d in Difficulties.All)
            {
                var choice = d;
                var b = Widgets.TextButton(Difficulties.Name(d), () =>
                {
                    if (ctx.State!.Difficulty == choice) return;
                    ConfirmPanel.Open(
                        $"Switch to {Difficulties.Name(choice)}?\n{Difficulties.Pitch(choice)}\n" +
                        "It applies from now on, and you can change it again any time.",
                        $"SWITCH TO {Difficulties.Name(choice)}",
                        () =>
                        {
                            ctx.State!.Difficulty = choice;
                            LocalBootstrap.RequestSync();
                            ui.Toast($"Difficulty: {Difficulties.Name(choice)}", Icon.Shield, UiTheme.Accent);
                            ui.OpenProfile();
                        },
                        () => ui.OpenProfile());
                }, 11);
                b.style.width = Length.Percent(32f);
                Widgets.SetButtonHighlight(b, state.Difficulty == d);
                diffRow.Add(b);
            }
            content.Add(diffRow);
            var diffNote = Widgets.Text(Difficulties.Pitch(state.Difficulty), 10, UiTheme.Dim);
            diffNote.style.whiteSpace = WhiteSpace.Normal;
            diffNote.style.marginTop = 4;
            content.Add(diffNote);

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
                int freePoints = CommanderSystem.PointsFree(state);
                string key = $"{state.Profile.Name}|{state.Resources.Total}|{state.Stats.MarchesSent}|{state.Stats.BattlesWon}|{state.Stats.BattlesLost}|{state.Premium.DarkMatter}|{state.Stats.ShipsBuilt}|{state.Achievements.Count}|{state.Commander.Xp}|{freePoints}";
                if (key == cache) return;
                cache = key;

                var cmd = state.Commander;
                cmdLabel.text = freePoints > 0
                    ? $"LEVEL {cmd.Level} COMMANDER · {freePoints} POINT{(freePoints == 1 ? "" : "S")} TO SPEND"
                    : $"LEVEL {cmd.Level} COMMANDER";
                var (into, span) = CommanderSystem.LevelProgress(state);
                xpFill.style.width = Length.Percent(span > 0 ? Math.Min(100f, 100f * into / span) : 100f);
                xpText.text = span > 0 ? $"{into:N0} / {span:N0} XP" : "highest level";
                Widgets.SetCaption(skills, freePoints > 0 ? $"SKILLS ({freePoints})" : "SKILLS");
                Widgets.SetButtonHighlight(skills, freePoints > 0);

                nameLabel.text = state.Profile.Name;
                mightLabel.text = PowerSystem.ComputePower(state).ToString("N0");
                resLabels[0].text = UiTheme.FmtAmount(state.Resources.Gold);
                resLabels[1].text = UiTheme.FmtAmount(state.Resources.Quartz);
                resLabels[2].text = UiTheme.FmtAmount(state.Resources.Helium);
                statLabels["Battles won"].text = state.Stats.BattlesWon.ToString();
                statLabels["Battles lost"].text = state.Stats.BattlesLost.ToString();
                statLabels["Camps cleared"].text = state.Stats.CampsCleared.ToString();
                statLabels["Raids won"].text = state.Stats.RaidsWon.ToString();
                statLabels["Raids repelled"].text = state.Stats.DefensesWon.ToString();
                statLabels["Ships built"].text = state.Stats.ShipsBuilt.ToString("N0");
                statLabels["Achievements"].text = $"{state.Achievements.Count} / {Achievements.All.Count}";
                statLabels["Best season"].text = state.Stats.BestSeasonRank > 0 ? $"#{state.Stats.BestSeasonRank}" : "—";
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

            // iCloud backup (CloudSave): status + BACK UP NOW.
            if (GalaxyRoyale.Local.CloudSave.Supported)
            {
                long last = GalaxyRoyale.Local.CloudSave.LastBackupMs;
                long agoSec = last > 0 ? Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - last) / 1000) : -1;
                var status = Widgets.IconText(Icon.Rotate,
                    agoSec < 0 ? "iCloud backup: not backed up yet"
                        : $"iCloud backup: last backed up {UiTheme.FmtLong(agoSec)} ago", 11,
                    agoSec >= 0 ? UiTheme.Good : UiTheme.Dim);
                status.style.marginTop = 6;
                row.Add(status);
                if (!GalaxyRoyale.Local.CloudSave.SignedIn)
                    AddText("Not signed in to iCloud on this device — backups sync once you are " +
                            "(Settings › your name › iCloud).", UiTheme.Dim);
                var backup = Widgets.IconButton(Icon.Rotate, "BACK UP NOW", () =>
                {
                    LocalBootstrap.Instance?.BackUpNow();
                    bool done = GalaxyRoyale.Local.CloudSave.LastBackupMs > last;
                    GameAudio.Feedback(done ? Sfx.Success : Sfx.Error, done ? Haptic.Success : Haptic.Error);
                    ui.Toast(done ? "Empire backed up to iCloud" : "iCloud didn't take the backup — try again later",
                        Icon.Rotate, done ? UiTheme.Good : UiTheme.Bad);
                    ui.OpenProfile();
                }, 11);
                backup.style.marginTop = 6;
                row.Add(backup);
            }

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
                        () => NewGamePanel.Open((test, difficulty) =>
                        {
                            LocalBootstrap.Instance?.ResetEmpire(test, difficulty);
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
