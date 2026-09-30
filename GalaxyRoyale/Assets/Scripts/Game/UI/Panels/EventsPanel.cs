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
        /// <summary>Fly the map to a map event's spot and open its node, if it has one.</summary>
        public static void ShowOnMap(GameContext ctx, TileXY tile, Sim.Map.MapNode? node)
        {
            var ui = UIController.Instance!;
            ui.CloseModal();
            ui.SwitchView(ViewId.Map);
            ctx.GetComponent<MapView>()?.FocusTile(tile);
            if (node != null) ui.OpenNodeCallout(node);
        }

        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("EVENTS & SEASON", ui.CloseModal, 80f);
            var body = new VisualElement();
            content.Add(body);

            // Countdown labels tick every second without rebuilding the page.
            Label? eventLeft = null, nextLeft = null, seasonLeft = null, twistLeft = null;
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
                    // Rival events: who's marked.
                    if (def.Kind == GalaxyEventKind.BountyBoard && state.BountyTargetId != 0)
                    {
                        var (pay, dm) = BountySystem.Reward(state);
                        body.Add(Wrap(Widgets.Text(state.BountyClaimed
                            ? $"The bounty on {state.BountyTargetName} has been collected."
                            : $"Wanted: {state.BountyTargetName} at {state.BountyTile.X}, {state.BountyTile.Y}. " +
                              $"Win a raid on them for {UiTheme.FmtAmount(pay.Total)} and {dm} DM.", 12, UiTheme.Energy), 6));
                    }
                    // Map events (2026-09-30): take the player to it.
                    if (EventSites.Focus(state) is { } spot)
                    {
                        var show = Widgets.Primary(Widgets.TextButton("SHOW ON MAP", () => ShowOnMap(ctx, spot.tile, spot.node), 12));
                        show.name = "event-show";
                        show.style.marginTop = 6;
                        show.style.height = 38;
                        body.Add(show);
                    }
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

                // ---- the week's twist (2026-09-30) ----
                var twist = TwistSystem.At(state.Tick);
                var nextTwist = TwistSystem.Next(state.Tick);
                body.Add(Section("THIS WEEK'S TWIST"));
                var tw = Card(twist.Kind == TwistKind.None ? UiTheme.Stroke : UiTheme.Accent);
                var twHead = Widgets.HBox(Justify.SpaceBetween);
                twHead.Add(Widgets.IconText(Icon.Bolt, twist.Name.ToUpperInvariant(), 13, UiTheme.Accent, bold: true));
                twistLeft = Widgets.Text("", 10, UiTheme.Accent, bold: true);
                twHead.Add(twistLeft);
                tw.Add(twHead);
                tw.Add(Wrap(Widgets.Text(twist.Effect, 12, UiTheme.Text), 4));
                if (twist.Flavor.Length > 0) tw.Add(Wrap(Widgets.Text(twist.Flavor, 11, UiTheme.Dim), 3));
                tw.Add(Wrap(Widgets.Text($"Next week: {nextTwist.Name} · {nextTwist.Effect}", 10, UiTheme.Dim), 6));
                body.Add(tw);

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
                    "One-week race: every empire is ranked by the might it GAINS this season, " +
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
                        // Tap a past season for its recap.
                        row.RegisterCallback<ClickEvent>(_ => SeasonRecapPanel.Open(ctx, rec));
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
                if (twistLeft != null) twistLeft.text = $"NEW IN {UiTheme.FmtLong(TwistSystem.LeftSec(state.Tick))}";
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

            card.Add(Wrap(Widgets.Text($"Reward: {RewardText(ctx.State!, def)}", 11, UiTheme.Accent), 8));
            var claim = Widgets.IconButton(Icon.Check,
                claimed ? "REWARD CLAIMED" : claimable ? "CLAIM REWARD" : "IN PROGRESS", () =>
                {
                    var result = EventSystem.Claim(ctx.State!);
                    if (!result.Ok) { ui.Toast(result.Reason ?? "Not yet"); return; }
                    GameAudio.Feedback(Sfx.Coins, Haptic.Success);
                    ui.Toast($"{def.Name} complete — {RewardText(ctx.State!, def)}", Icon.Bolt, UiTheme.Energy);
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

        public static string RewardText(GameState state, GalaxyEventDef def)
        {
            // Scaled to the colony (EventSystem.RewardMilli), so it keeps pace as you grow.
            var r = EventSystem.RewardMilli(state, def);
            var parts = new List<string>();
            if (r.Gold > 0) parts.Add($"{UiTheme.FmtAmount(r.Gold)} gold");
            if (r.Quartz > 0) parts.Add($"{UiTheme.FmtAmount(r.Quartz)} quartz");
            if (r.Helium > 0) parts.Add($"{UiTheme.FmtAmount(r.Helium)} helium");
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
            Holo.Frame(card, UiTheme.PanelLight, border, 11f, 1.5f);
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
