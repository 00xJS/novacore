// COMMANDER SKILLS (profile › SKILLS, or the level badge on the header pill):
// level, XP to the next level and what it pays, then the three skill branches
// as tabs. Each tier opens once its branch has enough points; LEARN buys a
// rank. RESET SKILLS hands every point back (first reset free).
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class CommanderPanel
    {
        /// <summary>The branch shown last (the panel reopens on it).</summary>
        static SkillBranch s_branch = SkillBranch.Industry;

        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            if (ctx.State == null) return;
            var (blocker, content, footer) = Widgets.ModalPanelFooter("COMMANDER SKILLS", ui.CloseModal, 84f);

            // ---- level card ----
            var card = Widgets.Row();
            var top = Widgets.HBox(Justify.SpaceBetween);
            var levelLabel = Widgets.Text("", 16, UiTheme.Energy, bold: true);
            top.Add(levelLabel);
            var diffLabel = Widgets.Text("", 10, UiTheme.Dim, bold: true);
            top.Add(diffLabel);
            card.Add(top);
            var (bar, fill, barLabel) = Widgets.ProgressBar(18f);
            bar.style.marginTop = 6;
            card.Add(bar);
            var pointsLabel = Widgets.Text("", 12, UiTheme.Energy, bold: true);
            pointsLabel.style.marginTop = 6;
            pointsLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(pointsLabel);
            var rewardLabel = Note("", 3);
            card.Add(rewardLabel);
            card.Add(Note("XP comes from everything you build, research, fight and win. " +
                "Each level pays Dark Matter and a skill point.", 3));
            content.Add(card);

            // (assigned up front: the tab buttons' handlers reach them through Rebuild)
            var list = new VisualElement();
            Button reset = null!;
            long shownXp = -1;
            int shownLevel = -1, shownSpent = -1;

            // ---- branch tabs ----
            var tabRow = Widgets.HBox(Justify.SpaceBetween);
            tabRow.style.marginTop = 4;
            tabRow.style.marginBottom = 6;
            var tabs = new List<(SkillBranch branch, Button button)>();
            foreach (var branch in CommanderSkills.Branches)
            {
                var b = branch;
                var btn = Widgets.TextButton(CommanderSkills.BranchName(b), () => { s_branch = b; Rebuild(); }, 11);
                btn.style.width = Length.Percent(32.5f);
                tabs.Add((b, btn));
                tabRow.Add(btn);
            }
            content.Add(tabRow);
            content.Add(list);

            // ---- footer ----
            var buttons = Widgets.HBox(Justify.SpaceBetween);
            reset = Widgets.TextButton("", () =>
            {
                var state = ctx.State!;
                int cost = CommanderSystem.RespecCost(state);
                ConfirmPanel.Open(
                    $"Reset your skills?\nAll {CommanderSystem.PointsSpent(state)} points come back to spend again. " +
                    (cost == 0 ? "Your first reset is free." : $"Costs {cost} Dark Matter."),
                    cost == 0 ? "RESET SKILLS" : $"RESET · {cost} DM",
                    () =>
                    {
                        var res = CommanderSystem.Respec(ctx.State!);
                        if (res.Ok)
                        {
                            GameAudio.Feedback(Sfx.Toggle, Haptic.Medium);
                            LocalBootstrap.RequestSync();
                            ui.Toast("Skills reset — every point is back", Icon.Rotate, UiTheme.Good);
                        }
                        else ui.Toast(res.Reason ?? "Can't reset", Icon.Info, UiTheme.Bad);
                        Open(ctx);
                    },
                    () => Open(ctx));
            }, 12);
            reset.style.width = Length.Percent(48f);
            buttons.Add(reset);
            var close = Widgets.TextButton("CLOSE", ui.CloseModal, 12);
            close.style.width = Length.Percent(48f);
            buttons.Add(close);
            footer.Add(buttons);

            void RefreshCard()
            {
                var state = ctx.State!;
                var c = state.Commander;
                int spent = CommanderSystem.PointsSpent(state);
                if (c.Xp == shownXp && c.Level == shownLevel && spent == shownSpent) return;
                bool levelled = shownLevel >= 0 && c.Level != shownLevel;
                shownXp = c.Xp;
                shownLevel = c.Level;
                shownSpent = spent;

                levelLabel.text = $"LEVEL {c.Level} COMMANDER";
                double mult = Difficulties.XpMult(state.Difficulty);
                diffLabel.text = mult > 1 ? $"{Difficulties.Name(state.Difficulty)} · +{(mult - 1) * 100:0}% XP"
                    : Difficulties.Name(state.Difficulty);
                var (into, span) = CommanderSystem.LevelProgress(state);
                if (span > 0)
                {
                    fill.style.width = Length.Percent(Math.Min(100f, 100f * into / span));
                    barLabel.text = $"{into:N0} / {span:N0} XP to level {c.Level + 1}";
                    var (dm, item) = CommanderSystem.LevelReward(c.Level + 1);
                    string itemText = item != null && Shop.ById.TryGetValue(item, out var def) ? $" and a {def.Name}" : "";
                    rewardLabel.text = $"Level {c.Level + 1} pays {dm} Dark Matter{itemText}.";
                }
                else
                {
                    fill.style.width = Length.Percent(100f);
                    barLabel.text = $"{c.Xp:N0} XP · highest level";
                    rewardLabel.text = "You've reached the highest commander level.";
                }
                int free = CommanderSystem.PointsFree(state);
                pointsLabel.text = free > 0
                    ? $"{free} skill point{(free == 1 ? "" : "s")} to spend"
                    : c.Level >= CommanderSystem.MaxLevel ? "Every point earned is spent."
                    : $"No points to spend — the next comes with level {c.Level + 1}.";
                pointsLabel.style.color = free > 0 ? UiTheme.Energy : UiTheme.Dim;
                int cost = CommanderSystem.RespecCost(state);
                Widgets.SetCaption(reset, cost == 0 ? "RESET SKILLS (FREE)" : $"RESET SKILLS · {cost} DM");
                Widgets.SetButtonEnabled(reset, spent > 0);
                if (levelled) Rebuild(); // new points can open LEARN buttons
            }

            void Rebuild()
            {
                var state = ctx.State!;
                foreach (var (b, btn) in tabs)
                {
                    int pts = CommanderSystem.BranchPoints(state, b);
                    Widgets.SetCaption(btn, pts > 0 ? $"{CommanderSkills.BranchName(b)} · {pts}" : CommanderSkills.BranchName(b));
                    Widgets.SetButtonHighlight(btn, b == s_branch);
                }
                list.Clear();
                list.Add(Note(CommanderSkills.BranchPitch(s_branch), 0));
                int branchPoints = CommanderSystem.BranchPoints(state, s_branch);
                for (int tier = 1; tier <= 4; tier++)
                {
                    bool open = CommanderSystem.TierOpen(state, s_branch, tier);
                    int need = CommanderSkills.TierPoints[tier - 1];
                    string title = tier == 4 ? "TIER 4 · CAPSTONE" : $"TIER {tier}";
                    if (!open) title += $" · OPENS AT {need} POINTS IN {CommanderSkills.BranchName(s_branch)} ({branchPoints}/{need})";
                    var header = Widgets.Text(title, 10, open ? UiTheme.Accent : UiTheme.Dim, bold: true);
                    header.style.whiteSpace = WhiteSpace.Normal;
                    header.style.marginTop = 10;
                    header.style.marginBottom = 4;
                    list.Add(header);
                    foreach (var skill in CommanderSkills.All)
                        if (skill.Branch == s_branch && skill.Tier == tier)
                            list.Add(SkillRow(ctx, skill, open, () => { RefreshCard(); Rebuild(); }));
                }
            }

            RefreshCard();
            Rebuild();
            blocker.schedule.Execute(RefreshCard).Every(1000);
            ui.OpenModal(blocker);
        }

        static VisualElement SkillRow(GameContext ctx, SkillDef skill, bool tierOpen, Action changed)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            int rank = CommanderSystem.Rank(state, skill.Id);
            var row = Widgets.Row();
            if (rank > 0) Widgets.SetBorder(row, UiTheme.Energy, 1f);

            var head = Widgets.HBox(Justify.SpaceBetween);
            var nameBox = Widgets.HBox();
            nameBox.Add(Widgets.Text(skill.Name, 13, tierOpen ? UiTheme.Text : UiTheme.Dim, bold: true));
            var pips = Widgets.HBox();
            pips.style.marginLeft = 8;
            for (int i = 0; i < skill.MaxRank; i++) pips.Add(Pip(i < rank));
            nameBox.Add(pips);
            head.Add(nameBox);

            var check = CommanderSystem.CheckLearn(state, skill.Id);
            if (rank < skill.MaxRank)
            {
                var learn = Widgets.TextButton(rank == 0 ? "LEARN" : "RANK UP", () =>
                {
                    var res = CommanderSystem.Learn(ctx.State!, skill.Id);
                    if (!res.Ok) { ui.Toast(res.Reason ?? "Can't learn that yet", Icon.Info, UiTheme.Bad); return; }
                    GameAudio.Feedback(Sfx.Success, Haptic.Light);
                    LocalBootstrap.RequestSync();
                    int now = CommanderSystem.Rank(ctx.State!, skill.Id);
                    ui.Toast($"{skill.Name} rank {now}: {CommanderSkills.Describe(skill, now)}", Icon.Star, UiTheme.Energy);
                    changed();
                }, 11);
                learn.style.height = 28;
                Widgets.SetButtonEnabled(learn, check.Ok);
                head.Add(learn);
            }
            else head.Add(Widgets.Text("MAX", 11, UiTheme.Energy, bold: true));
            row.Add(head);

            if (rank > 0)
            {
                var now = Widgets.Text($"Now: {CommanderSkills.Describe(skill, rank)}", 11, UiTheme.Good);
                now.style.whiteSpace = WhiteSpace.Normal;
                now.style.marginTop = 4;
                row.Add(now);
            }
            if (rank < skill.MaxRank)
            {
                var next = Widgets.Text($"{(rank == 0 ? "Rank 1" : $"Rank {rank + 1}")}: " +
                    CommanderSkills.Describe(skill, rank + 1), 11, tierOpen ? UiTheme.Text : UiTheme.Dim);
                next.style.whiteSpace = WhiteSpace.Normal;
                next.style.marginTop = 4;
                row.Add(next);
            }
            return row;
        }

        static VisualElement Pip(bool on)
        {
            var p = new VisualElement();
            p.style.width = 8;
            p.style.height = 8;
            p.style.marginRight = 3;
            p.style.borderTopLeftRadius = 4;
            p.style.borderTopRightRadius = 4;
            p.style.borderBottomLeftRadius = 4;
            p.style.borderBottomRightRadius = 4;
            Holo.Frame(p, on ? UiTheme.Energy : UiTheme.PanelLight, on ? UiTheme.Energy : UiTheme.Stroke, 9f, 1f);
            return p;
        }

        static Label Note(string text, float marginTop)
        {
            var l = Widgets.Text(text, 11, UiTheme.Dim);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = marginTop;
            return l;
        }
    }
}
