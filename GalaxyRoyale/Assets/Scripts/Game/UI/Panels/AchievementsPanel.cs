// AWARDS (MORE → AWARDS, or TITLE on your profile): every achievement with its
// progress, the Dark Matter it pays, and the commander title it unlocks. Tap
// WEAR on an unlocked title to show it on your profile and in the rankings.
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class AchievementsPanel
    {
        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("ACHIEVEMENTS", ui.CloseModal, 80f);
            var body = new VisualElement();
            content.Add(body);

            string key = "";
            int lastTick = -1;

            void Render(GameState state)
            {
                body.Clear();
                int unlocked = 0;
                foreach (var a in Achievements.All) if (AchievementSystem.IsUnlocked(state, a)) unlocked++;

                // ---- summary + worn title ----
                var summary = Widgets.Row();
                var sBox = Widgets.HBox(Justify.SpaceBetween);
                sBox.Add(Widgets.IconText(Icon.Trophy, $"{unlocked} / {Achievements.All.Count} UNLOCKED", 13,
                    UiTheme.Energy, bold: true));
                string? title = AchievementSystem.TitleText(state);
                sBox.Add(Widgets.Text(title != null ? $"Title: {title}" : "No title worn", 11,
                    title != null ? UiTheme.Accent : UiTheme.Dim, bold: title != null));
                summary.Add(sBox);
                summary.Add(EventsPanel.Bar((float)unlocked / Achievements.All.Count, UiTheme.Energy));
                var help = Widgets.Text("Each award pays Dark Matter the moment it unlocks. Most also unlock a " +
                    "commander title — wear one to show it on your profile and in the rankings.", 10, UiTheme.Dim);
                help.style.whiteSpace = WhiteSpace.Normal;
                help.style.marginTop = 6;
                summary.Add(help);
                body.Add(summary);

                // Unlocked first, then the locked ones closest to done; ties keep
                // the definition order (List.Sort isn't stable on its own).
                var list = new System.Collections.Generic.List<AchievementDef>(Achievements.All);
                list.Sort((a, b) =>
                {
                    bool ua = AchievementSystem.IsUnlocked(state, a), ub = AchievementSystem.IsUnlocked(state, b);
                    if (ua != ub) return ua ? -1 : 1;
                    if (ua) return IndexOf(a).CompareTo(IndexOf(b));
                    var (ha, na) = AchievementSystem.Progress(state, a);
                    var (hb, nb) = AchievementSystem.Progress(state, b);
                    int byProgress = ((double)hb / nb).CompareTo((double)ha / na);
                    return byProgress != 0 ? byProgress : IndexOf(a).CompareTo(IndexOf(b));
                });

                foreach (var a in list) body.Add(AchievementRow(ctx, state, a, () => key = ""));
            }

            refresh = () =>
            {
                var state = ctx.State;
                if (state == null || state.Tick == lastTick) return;
                lastTick = state.Tick;
                var sb = new System.Text.StringBuilder(state.Title ?? "-");
                foreach (var a in Achievements.All)
                    sb.Append('|').Append(AchievementSystem.IsUnlocked(state, a) ? "u" : AchievementSystem.Progress(state, a).have.ToString());
                string k = sb.ToString();
                if (k == key) return;
                key = k;
                Render(state);
            };
            refresh();
            return blocker;
        }

        static int IndexOf(AchievementDef a)
        {
            for (int i = 0; i < Achievements.All.Count; i++) if (Achievements.All[i] == a) return i;
            return 0;
        }

        static VisualElement AchievementRow(GameContext ctx, GameState state, AchievementDef a, Action invalidate)
        {
            var ui = UIController.Instance!;
            bool done = AchievementSystem.IsUnlocked(state, a);
            var (have, need) = AchievementSystem.Progress(state, a);

            var row = Widgets.Row();
            if (done) Widgets.SetBorder(row, UiTheme.Energy, 1f);
            var head = Widgets.HBox(Justify.SpaceBetween);
            var left = Widgets.HBox();
            left.style.flexShrink = 1f;
            var cup = Icons.Make(done ? Icon.Trophy : Icon.Ring, 16, done ? UiTheme.Energy : UiTheme.Dim);
            cup.style.marginRight = 8;
            left.Add(cup);
            var col = new VisualElement();
            col.style.flexShrink = 1f;
            col.Add(Widgets.Text(a.Name, 12, UiTheme.Text, bold: true));
            var detail = Widgets.Text(a.Detail, 10, UiTheme.Dim);
            detail.style.whiteSpace = WhiteSpace.Normal;
            col.Add(detail);
            left.Add(col);
            head.Add(left);
            var reward = Widgets.Text(done ? "PAID" : $"+{a.RewardDM} DM", 10,
                done ? UiTheme.Good : UiTheme.DarkMatter, bold: true);
            reward.style.flexShrink = 0f;
            reward.style.marginLeft = 6;
            head.Add(reward);
            row.Add(head);

            if (!done)
            {
                var prog = Widgets.HBox(Justify.SpaceBetween);
                prog.style.marginTop = 6;
                prog.Add(Widgets.Text(a.Goal == AchievementGoal.SeasonTop
                    ? (state.Stats.BestSeasonRank > 0 ? $"Best finish #{state.Stats.BestSeasonRank}" : "No season finished yet")
                    : $"{UiTheme.FmtCount(have)} / {UiTheme.FmtCount(need)}", 10, UiTheme.Dim));
                if (a.Title != null) prog.Add(Widgets.Text($"Title: {a.Title}", 10, UiTheme.Accent));
                row.Add(prog);
                row.Add(EventsPanel.Bar((float)have / need, UiTheme.Energy));
            }
            else if (a.Title != null)
            {
                bool worn = state.Title == a.Id;
                var titleRow = Widgets.HBox(Justify.SpaceBetween);
                titleRow.style.marginTop = 6;
                titleRow.Add(Widgets.Text($"Title: {a.Title}", 11, UiTheme.Accent, bold: true));
                var wear = Widgets.TextButton(worn ? "WORN — TAKE OFF" : "WEAR", () =>
                {
                    var result = AchievementSystem.EquipTitle(ctx.State!, worn ? null : a.Id);
                    if (!result.Ok) { ui.Toast(result.Reason ?? "Can't wear that"); return; }
                    GameAudio.Feedback(Sfx.Confirm, Haptic.Selection);
                    ui.Toast(worn ? "Title removed" : $"You are now {ctx.State!.Profile.Name}, {a.Title}",
                        Icon.Trophy, UiTheme.Energy);
                    LocalBootstrap.RequestSync();
                    invalidate();
                }, 10);
                if (worn) Widgets.SetButtonHighlight(wear, true);
                titleRow.Add(wear);
                row.Add(titleRow);
            }
            return row;
        }
    }
}
