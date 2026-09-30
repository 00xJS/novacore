// The Commander's Path (user request 2026-09-27): the guided quest chain, in
// both STANDARD and TESTING games. Done steps are ticked off, the current one
// shows its goal, progress and reward with CLAIM, and later steps show only
// their titles.
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class QuestPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("COMMANDER'S PATH", ui.CloseModal, 80f);
            var body = new VisualElement();
            content.Add(body);

            string lastKey = "";
            void Render()
            {
                var state = ctx.State;
                if (state == null) return;
                var current = QuestSystem.Current(state);
                string key = $"{state.QuestStep}|{(current != null ? QuestSystem.Progress(state, current).ToString() : "")}";
                if (key == lastKey) return;
                lastKey = key;
                body.Clear();

                var head = Widgets.HBox(Justify.SpaceBetween);
                head.Add(Widgets.Text(current != null
                    ? $"QUEST {state.QuestStep + 1} OF {Quests.Chain.Count}"
                    : "ALL QUESTS COMPLETE", 10, UiTheme.Dim, bold: true));
                head.Add(Widgets.Text(state.TestMode ? "TESTING GALAXY" : "STANDARD GALAXY", 10,
                    state.TestMode ? UiTheme.Energy : UiTheme.Accent, bold: true));
                body.Add(head);

                for (int i = 0; i < Quests.Chain.Count; i++)
                {
                    var quest = Quests.Chain[i];
                    if (i < state.QuestStep) body.Add(DoneRow(quest));
                    else if (i == state.QuestStep) body.Add(CurrentCard(ctx, state, quest, Render));
                    else body.Add(LockedRow(quest, i + 1));
                }
                if (current == null)
                {
                    var done = Widgets.Text("The Commander's Path is complete — the galaxy is yours to take.",
                        12, UiTheme.Good);
                    done.style.whiteSpace = WhiteSpace.Normal;
                    done.style.marginTop = 12;
                    body.Add(done);
                }
            }

            Render();
            ui.OpenModal(blocker, Render); // progress updates live while the panel is open
        }

        static VisualElement DoneRow(QuestDef quest)
        {
            var row = Widgets.IconText(Icon.Check, quest.Title, 11, UiTheme.Dim);
            row.style.marginTop = 6;
            return row;
        }

        static VisualElement LockedRow(QuestDef quest, int number)
        {
            var label = Widgets.Text($"{number}. {quest.Title}", 11,
                new UnityEngine.Color(UiTheme.Dim.r, UiTheme.Dim.g, UiTheme.Dim.b, 0.7f));
            label.style.marginTop = 6;
            return label;
        }

        static VisualElement CurrentCard(GameContext ctx, GameState state, QuestDef quest, System.Action rerender)
        {
            var ui = UIController.Instance!;
            var (have, need) = QuestSystem.Progress(state, quest);
            bool complete = have >= need;

            var card = new VisualElement();
            card.style.marginTop = 10;
            card.style.marginBottom = 4;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 12;
            Holo.Frame(card, UiTheme.PanelLight, complete ? UiTheme.Good : UiTheme.Energy, 11f, 1.5f);

            card.Add(Widgets.IconText(Icon.Star, quest.Title, 14, UiTheme.Energy, bold: true));
            var detail = Widgets.Text(quest.Detail, 11, UiTheme.Text);
            detail.style.whiteSpace = WhiteSpace.Normal;
            detail.style.marginTop = 4;
            card.Add(detail);

            var goal = Widgets.HBox(Justify.SpaceBetween);
            goal.style.marginTop = 8;
            goal.Add(Widgets.Text(GoalText(quest, have, need), 11, complete ? UiTheme.Good : UiTheme.Dim, bold: true));
            card.Add(goal);
            var bar = new VisualElement();
            bar.style.height = 5;
            bar.style.marginTop = 4;
            bar.style.backgroundColor = UiTheme.Panel;
            var fill = new VisualElement();
            fill.style.height = Length.Percent(100f);
            fill.style.width = Length.Percent(need > 0 ? 100f * have / need : 100f);
            fill.style.backgroundColor = complete ? UiTheme.Good : UiTheme.Energy;
            bar.Add(fill);
            card.Add(bar);

            var reward = Widgets.Text($"Reward: {RewardText(quest)}", 11, UiTheme.Accent);
            reward.style.whiteSpace = WhiteSpace.Normal;
            reward.style.marginTop = 8;
            card.Add(reward);

            var claim = Widgets.IconButton(Icon.Check, complete ? "CLAIM REWARD" : "IN PROGRESS", () =>
            {
                var result = QuestSystem.Claim(state);
                if (!result.Ok) { ui.Toast(result.Reason ?? "Not yet"); return; }
                ui.Toast($"Quest complete: {quest.Title} — {RewardText(quest)}", Icon.Star, UiTheme.Energy);
                ui.OnQuestClaimed();
                rerender();
            }, 13);
            claim.name = "tut-claim";
            claim.style.height = 42;
            claim.style.marginTop = 10;
            Widgets.SetButtonEnabled(claim, complete);
            if (complete) Widgets.SetBorder(claim, UiTheme.Good, 1.5f);
            card.Add(claim);
            return card;
        }

        /// <summary>"Command Center Lv 1 / 2", "Battles won 0 / 1", …</summary>
        public static string GoalText(QuestDef quest, int have, int need) => quest.Goal switch
        {
            QuestGoal.BuildingLevel => $"{Buildings.Defs[quest.Building].Name} Lv {have} / {need}",
            QuestGoal.ResearchLevel => $"{Techs.Defs[quest.Tech].Name} Lv {have} / {need}",
            QuestGoal.BattlesWon => $"Battles won {have} / {need}",
            QuestGoal.CampScouted => have >= need ? "Pirate camp scanned" : "No camp scanned yet",
            _ => "",
        };

        /// <summary>"400 gold · 200 quartz · 10 Fighters".</summary>
        public static string RewardText(QuestDef quest)
        {
            var parts = new List<string>();
            if (quest.Reward.Gold > 0) parts.Add($"{quest.Reward.Gold:N0} gold");
            if (quest.Reward.Quartz > 0) parts.Add($"{quest.Reward.Quartz:N0} quartz");
            if (quest.Reward.Helium > 0) parts.Add($"{quest.Reward.Helium:N0} helium");
            if (quest.RewardShips != null)
                foreach (var hull in Ships.All)
                    if (quest.RewardShips.TryGetValue(hull, out var n) && n > 0)
                        parts.Add($"{n} {Ships.Defs[hull].Name}{(n == 1 ? "" : "s")}");
            return string.Join(" · ", parts);
        }
    }
}
