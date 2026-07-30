// Daily objectives panel (MORE → DAILY) — five tasks, progress bars, CLAIM
// buttons paying Dark Matter. Resets at UTC midnight.
using System;
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public static class DailyPanel
    {
        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("DAILY OBJECTIVES", ui.CloseModal, 0f);

            var sub = Widgets.Text("Resets at midnight UTC.", 10, UiTheme.Dim);
            sub.style.marginBottom = 6;
            content.Add(sub);

            var list = new VisualElement();
            content.Add(list);

            string cache = "";
            refresh = () =>
            {
                var state = ctx.State!;
                var sb = new System.Text.StringBuilder();
                foreach (var def in DailyObjectives.Defs)
                    sb.Append(DailyObjectives.Progress(state, def)).Append(':')
                      .Append(DailyObjectives.IsClaimed(def) ? 1 : 0).Append(',');
                string key = sb.ToString();
                if (key == cache) return;
                cache = key;

                list.Clear();
                foreach (var d in DailyObjectives.Defs)
                {
                    var def = d;
                    int progress = DailyObjectives.Progress(state, def);
                    bool done = progress >= def.Target;
                    bool claimed = DailyObjectives.IsClaimed(def);

                    var row = Widgets.Row();
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text(def.Label, 12, claimed ? UiTheme.Dim : UiTheme.Text,
                        bold: !claimed));
                    head.Add(Widgets.Text($"+{def.RewardDM} DM", 11, UiTheme.DarkMatter, bold: true));
                    row.Add(head);

                    var (bar, fill, barLabel) = Widgets.ProgressBar(14f);
                    bar.style.marginTop = 5;
                    fill.style.width = Length.Percent(100f * progress / def.Target);
                    if (claimed) fill.style.backgroundColor = UiTheme.Stroke;
                    barLabel.text = def.Target > 1 ? $"{progress} / {def.Target}" : done ? "done" : "not yet";
                    row.Add(bar);

                    var claim = Widgets.TextButton(
                        claimed ? "✓ CLAIMED" : done ? "CLAIM" : "IN PROGRESS", () =>
                    {
                        if (DailyObjectives.Claim(ctx.State!, def))
                        {
                            ui.Toast($"+{def.RewardDM} Dark Matter — objective complete!");
                            cache = "";
                        }
                    }, 11);
                    Widgets.SetButtonEnabled(claim, done && !claimed);
                    claim.style.marginTop = 6;
                    row.Add(claim);
                    list.Add(row);
                }
            };
            refresh();
            return blocker;
        }
    }
}
