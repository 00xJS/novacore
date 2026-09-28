// EVENTS (MORE → EVENTS, or the event chip on the base): the live galaxy
// event — the rule it bends for every empire, its goal, reward and CLAIM —
// what's next in the weekly rotation, and the current season: your place on
// the might-gained board, the time left, and what each finish pays.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class EventsPanel
    {
        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("EVENTS & SEASON", ui.CloseModal, 80f);
            var body = new VisualElement();
            content.Add(body);

            // Countdown labels tick every second without rebuilding the page.
            Label? eventLeft = null, nextLeft = null, seasonLeft = null;
            string key = "";
            int lastTick = -1;

            void Render()
            {
                var state = ctx.State;
                var galaxy = ctx.Bots;
                if (state == null) return;
                body.Clear();

                // ---- the live event ----
                var live = EventSystem.Current(state.Tick);
                var def = live.Def;
                var (have, need) = EventSystem.Progress(state);
                bool claimable = EventSystem.CanClaim(state);
                bool claimed = state.EventClaimed && live.Instance == state.EventInstance;

                body.Add(Section("GALAXY EVENT"));
                if (EventSystem.IsQuiet(live))
                {
                    // A new galaxy's lead-in: nothing live yet.
                    var quiet = Card(UiTheme.Stroke);
                    var qHead = Widgets.HBox(Justify.SpaceBetween);
                    qHead.Add(Widgets.IconText(Icon.Bolt, "QUIET SKIES", 15, UiTheme.Dim, bold: true));
                    eventLeft = Widgets.Text("", 10, UiTheme.Dim, bold: true);
                    qHead.Add(eventLeft);
                    quiet.Add(qHead);
                    quiet.Add(Wrap(Widgets.Text(
                        $"No galaxy event yet. The first — {EventSystem.Next(state.Tick).Def.Name} — begins soon, " +
                        "then a new one rolls in every few days: each bends a rule for every empire and " +
                        "pays out for hitting its goal.", 11, UiTheme.Text), 6));
                    body.Add(quiet);
                }
                else
                {
                    body.Add(EventCard(ctx, def, have, need, claimable, claimed, () => key = "", out var left));
                    eventLeft = left;
                }

                // ---- next up ----
                var next = EventSystem.Next(state.Tick);
                var nextRow = Widgets.Row();
                var nextBox = Widgets.HBox(Justify.SpaceBetween);
                var nextCol = new VisualElement();
                nextCol.style.flexShrink = 1f;
                nextCol.Add(Widgets.Text("NEXT UP", 9, UiTheme.Dim, bold: true));
                nextCol.Add(Widgets.Text(next.Def.Name, 13, UiTheme.Text, bold: true));
                nextCol.Add(Wrap(Widgets.Text(next.Def.Effect, 10, UiTheme.Dim), 2));
                nextBox.Add(nextCol);
                nextLeft = Widgets.Text("", 10, UiTheme.Accent, bold: true);
                nextLeft.style.flexShrink = 0f;
                nextLeft.style.marginLeft = 8;
                nextBox.Add(nextLeft);
                nextRow.Add(nextBox);
                body.Add(nextRow);

                // ---- the season ----
                body.Add(Section("SEASON"));
                var season = Card(UiTheme.Accent);
                int number = Math.Max(1, state.Season);
                var sHead = Widgets.HBox(Justify.SpaceBetween);
                sHead.Add(Widgets.IconText(Icon.Trophy, $"SEASON {number}", 15, UiTheme.Accent, bold: true));
                seasonLeft = Widgets.Text("", 10, UiTheme.Dim, bold: true);
                sHead.Add(seasonLeft);
                season.Add(sHead);
                season.Add(Wrap(Widgets.Text(
                    "Two-week race: every empire is ranked by the might it GAINS this season, " +
                    "so a young colony can beat the giants.", 11, UiTheme.Text), 6));
                if (galaxy != null)
                {
                    int rank = SeasonSystem.PlayerRank(state, galaxy);
                    int of = galaxy.Bots.Count + 1;
                    var standing = Widgets.HBox(Justify.SpaceBetween);
                    standing.style.marginTop = 10;
                    standing.Add(Widgets.Text($"#{rank}", 22, rank <= 10 ? UiTheme.Energy : UiTheme.Accent, bold: true));
                    var col = new VisualElement();
                    col.style.alignItems = Align.FlexEnd;
                    col.Add(Widgets.Text($"of {of} commanders", 11, UiTheme.Dim));
                    col.Add(Widgets.Text($"+{SeasonSystem.PlayerGain(state):N0} might this season", 12, UiTheme.Text, bold: true));
                    col.Add(Widgets.Text($"Finishing here pays {SeasonSystem.RewardFor(rank)} DM", 11, UiTheme.DarkMatter));
                    standing.Add(col);
                    season.Add(standing);
                }
                season.Add(Wrap(Widgets.Text(
                    $"#1: {SeasonSystem.RewardFor(1):N0} DM · top 3: {SeasonSystem.RewardFor(3)} · top 10: {SeasonSystem.RewardFor(10)} · " +
                    $"top 50: {SeasonSystem.RewardFor(50)} · everyone else: {SeasonSystem.RewardFor(999)}", 10, UiTheme.Dim), 8));
                var board = Widgets.IconButton(Icon.Chart, "SEASON BOARD", () => ui.OpenRankings(season: true), 12);
                board.style.height = 38;
                board.style.marginTop = 10;
                season.Add(board);
                body.Add(season);

                // ---- past seasons ----
                if (state.SeasonHistory.Count > 0)
                {
                    body.Add(Section("PAST SEASONS"));
                    for (int i = state.SeasonHistory.Count - 1; i >= 0 && i >= state.SeasonHistory.Count - 5; i--)
                    {
                        var rec = state.SeasonHistory[i];
                        var row = Widgets.Row();
                        var box = Widgets.HBox(Justify.SpaceBetween);
                        box.Add(Widgets.Text($"Season {rec.Season}", 12, UiTheme.Text, bold: true));
                        box.Add(Widgets.Text($"#{rec.Rank} of {rec.Of} · +{rec.Gain:N0} · {rec.RewardDM} DM", 11,
                            rec.Rank <= 10 ? UiTheme.Energy : UiTheme.Dim));
                        row.Add(box);
                        body.Add(row);
                    }
                }
                UpdateClocks(state);
            }

            void UpdateClocks(GameState state)
            {
                var live = EventSystem.Current(state.Tick);
                if (eventLeft != null)
                    eventLeft.text = $"{(EventSystem.IsQuiet(live) ? "FIRST IN" : "ENDS IN")} {UiTheme.FmtLong(live.EndTick - state.Tick)}";
                if (nextLeft != null) nextLeft.text = $"in {UiTheme.FmtLong(live.EndTick - state.Tick)}";
                int season = Math.Max(1, state.Season);
                if (seasonLeft != null)
                    seasonLeft.text = $"ENDS IN {UiTheme.FmtLong(SeasonSystem.EndTick(season) - state.Tick)}";
            }

            refresh = () =>
            {
                var state = ctx.State;
                if (state == null || state.Tick == lastTick) return;
                lastTick = state.Tick;
                var (have, _) = EventSystem.Progress(state);
                int rank = ctx.Bots != null ? SeasonSystem.PlayerRank(state, ctx.Bots) : 0;
                string k = $"{EventSystem.Current(state.Tick).Instance}|{have}|{state.EventClaimed}|{state.Season}|{rank}|{SeasonSystem.PlayerGain(state)}";
                if (k != key) { key = k; Render(); }
                else UpdateClocks(state);
            };
            refresh();
            return blocker;
        }

        static VisualElement EventCard(GameContext ctx, GalaxyEventDef def, long have, long need,
            bool claimable, bool claimed, Action invalidate, out Label timeLeft)
        {
            var ui = UIController.Instance!;
            var card = Card(claimable ? UiTheme.Good : UiTheme.Energy);
            var head = Widgets.HBox(Justify.SpaceBetween);
            head.Add(Widgets.IconText(Icon.Bolt, def.Name.ToUpperInvariant(), 15, UiTheme.Energy, bold: true));
            timeLeft = Widgets.Text("", 10, UiTheme.Dim, bold: true);
            head.Add(timeLeft);
            card.Add(head);
            card.Add(Wrap(Widgets.Text(def.Effect, 12, UiTheme.Text), 6));
            card.Add(Wrap(Widgets.Text("Every empire in the galaxy feels it — the rivals too.", 10, UiTheme.Dim), 2));

            var goal = Widgets.HBox(Justify.SpaceBetween);
            goal.style.marginTop = 10;
            goal.Add(Widgets.Text($"{def.Goal} during the event", 11, UiTheme.Dim, bold: true));
            goal.Add(Widgets.Text($"{have} / {need}", 11, have >= need ? UiTheme.Good : UiTheme.Text, bold: true));
            card.Add(goal);
            card.Add(Bar(need > 0 ? (float)have / need : 1f, have >= need ? UiTheme.Good : UiTheme.Energy));

            card.Add(Wrap(Widgets.Text($"Reward: {RewardText(def)}", 11, UiTheme.Accent), 8));
            var claim = Widgets.IconButton(Icon.Check,
                claimed ? "REWARD CLAIMED" : claimable ? "CLAIM REWARD" : "IN PROGRESS", () =>
                {
                    var result = EventSystem.Claim(ctx.State!);
                    if (!result.Ok) { ui.Toast(result.Reason ?? "Not yet"); return; }
                    GameAudio.Feedback(Sfx.Coins, Haptic.Success);
                    ui.Toast($"{def.Name} complete — {RewardText(def)}", Icon.Bolt, UiTheme.Energy);
                    LocalBootstrap.RequestSync();
                    invalidate();
                }, 13);
            claim.style.height = 40;
            claim.style.marginTop = 10;
            Widgets.SetButtonEnabled(claim, claimable);
            if (claimable) Widgets.SetBorder(claim, UiTheme.Good, 1.5f);
            card.Add(claim);
            return card;
        }

        public static string RewardText(GalaxyEventDef def)
        {
            var parts = new List<string>();
            if (def.Reward.Gold > 0) parts.Add($"{def.Reward.Gold:N0} gold");
            if (def.Reward.Quartz > 0) parts.Add($"{def.Reward.Quartz:N0} quartz");
            if (def.Reward.Helium > 0) parts.Add($"{def.Reward.Helium:N0} helium");
            if (def.RewardDM > 0) parts.Add($"{def.RewardDM} DM");
            return string.Join(" · ", parts);
        }

        static Label Section(string text)
        {
            var l = Widgets.Text(text, 10, UiTheme.Dim, bold: true);
            l.style.marginTop = 8;
            l.style.marginBottom = 6;
            return l;
        }

        static VisualElement Card(Color border)
        {
            var card = new VisualElement();
            card.style.marginBottom = 8;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 12;
            card.style.backgroundColor = UiTheme.PanelLight;
            Widgets.SetBorder(card, border, 1.5f);
            card.style.borderTopLeftRadius = 10;
            card.style.borderTopRightRadius = 10;
            card.style.borderBottomLeftRadius = 10;
            card.style.borderBottomRightRadius = 10;
            return card;
        }

        static Label Wrap(Label label, float marginTop)
        {
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = marginTop;
            return label;
        }

        /// <summary>Thin progress bar (quest-card style).</summary>
        public static VisualElement Bar(float fraction, Color color)
        {
            var bar = new VisualElement();
            bar.style.height = 5;
            bar.style.marginTop = 4;
            bar.style.backgroundColor = UiTheme.Panel;
            var fill = new VisualElement();
            fill.style.height = Length.Percent(100f);
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            fill.style.backgroundColor = color;
            bar.Add(fill);
            return bar;
        }
    }
}
