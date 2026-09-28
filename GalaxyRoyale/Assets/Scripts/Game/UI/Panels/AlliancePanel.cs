// ALLIES (MORE → ALLIES): your pacts with simulated commanders — supplies
// waiting to be collected, who's close enough to reinforce your colony, and
// the commanders open to a pact right now. See AllianceSystem for the rules.
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class AlliancePanel
    {
        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("ALLIANCES", ui.CloseModal, 80f);
            var body = new VisualElement();
            content.Add(body);

            string key = "";
            int lastTick = -1;

            void Render(GameState state, BotGalaxy galaxy)
            {
                body.Clear();

                var intro = Widgets.Text(
                    $"Hold up to {AllianceSystem.MaxAllies} pacts. Allies never raid you, send a supply run every day, " +
                    $"and — if their colony is within {AllianceSystem.ReinforceRange:N0} tiles of yours — commit " +
                    $"{AllianceSystem.ReinforceFraction:P0} of their warships when raiders hit you.", 10, UiTheme.Dim);
                intro.style.whiteSpace = WhiteSpace.Normal;
                body.Add(intro);

                // ---- supplies ----
                var pending = AllianceSystem.PendingTotal(state);
                if (pending.Total > 0)
                {
                    var supplies = Widgets.Row();
                    Widgets.SetBorder(supplies, UiTheme.Good, 1.5f);
                    supplies.style.marginTop = 10;
                    supplies.Add(Widgets.IconText(Icon.Pact, "SUPPLIES WAITING", 12, UiTheme.Good, bold: true));
                    var amounts = Widgets.HBox(Justify.SpaceAround);
                    amounts.style.marginTop = 6;
                    if (pending.Gold > 0) amounts.Add(Widgets.Text($"+{UiTheme.FmtAmount(pending.Gold)} gold", 12, UiTheme.Gold, bold: true));
                    if (pending.Quartz > 0) amounts.Add(Widgets.Text($"+{UiTheme.FmtAmount(pending.Quartz)} quartz", 12, UiTheme.Quartz, bold: true));
                    if (pending.Helium > 0) amounts.Add(Widgets.Text($"+{UiTheme.FmtAmount(pending.Helium)} helium", 12, UiTheme.Helium, bold: true));
                    supplies.Add(amounts);
                    var collect = Widgets.IconButton(Icon.Check, "COLLECT SUPPLIES", () =>
                    {
                        var result = AllianceSystem.Collect(ctx.State!);
                        if (!result.Ok) { ui.Toast(result.Reason ?? "Nothing to collect"); return; }
                        GameAudio.Feedback(Sfx.Coins, Haptic.Success);
                        ui.Toast("Allied supplies banked", Icon.Pact, UiTheme.Good);
                        LocalBootstrap.RequestSync();
                        key = "";
                    }, 13);
                    collect.style.height = 40;
                    collect.style.marginTop = 8;
                    Widgets.SetBorder(collect, UiTheme.Good, 1.5f);
                    supplies.Add(collect);
                    body.Add(supplies);
                }

                // ---- your allies ----
                body.Add(Section($"YOUR ALLIES  {state.Allies.Count}/{AllianceSystem.MaxAllies}"));
                if (state.Allies.Count == 0)
                    body.Add(Note("No pacts yet — pick a commander below, or PROPOSE from anyone's profile."));
                foreach (var pact in state.Allies)
                {
                    var ally = galaxy.Find(pact.BotId);
                    if (ally == null) continue;
                    body.Add(AllyRow(ctx, state, ally, pact));
                }

                // ---- candidates ----
                if (state.Allies.Count < AllianceSystem.MaxAllies)
                {
                    body.Add(Section("OPEN TO A PACT"));
                    var candidates = AllianceSystem.Candidates(state, galaxy, 6);
                    if (candidates.Count == 0)
                    {
                        long mine = PowerSystem.ComputePower(state);
                        body.Add(Note(mine < BotSystem.PlayerShieldMight
                            ? $"Commanders only ally with established empires — reach {BotSystem.PlayerShieldMight:N0} might."
                            : "Nobody is looking for a pact right now. Grow stronger — giants won't ally with a small colony."));
                    }
                    foreach (var bot in candidates)
                        body.Add(CandidateRow(ctx, state, galaxy, bot, () => key = ""));
                }
            }

            refresh = () =>
            {
                var state = ctx.State;
                var galaxy = ctx.Bots;
                if (state == null || galaxy == null || state.Tick == lastTick) return;
                lastTick = state.Tick;
                // Rebuild when pacts or supplies change, and once a minute for the
                // countdowns and the candidate pool (might moves slowly).
                var sb = new System.Text.StringBuilder();
                foreach (var pact in state.Allies)
                    sb.Append(pact.BotId).Append(':').Append(pact.PendingRuns).Append(':')
                      .Append(pact.NextAidTick).Append(',');
                sb.Append('|').Append(state.Tick / 60);
                string k = sb.ToString();
                if (k == key) return;
                key = k;
                Render(state, galaxy);
            };
            refresh();
            return blocker;
        }

        static VisualElement AllyRow(GameContext ctx, GameState state, BotEmpire ally, Alliance pact)
        {
            var ui = UIController.Instance!;
            var row = Widgets.Row();
            Widgets.SetBorder(row, UiTheme.Accent, 1f);
            var head = Widgets.HBox(Justify.SpaceBetween);
            var left = Widgets.HBox();
            left.style.flexShrink = 1f;
            var avatar = Portraits.Avatar(ally.State.Profile.AvatarSeed, ally.Name, 28);
            avatar.style.marginRight = 8;
            left.Add(avatar);
            var col = new VisualElement();
            col.style.flexShrink = 1f;
            col.Add(Widgets.Text(ally.Name, 13, UiTheme.Text, bold: true));
            col.Add(Widgets.Text($"Might {ally.CachedMight:N0} · allied {UiTheme.FmtLong(state.Tick - pact.SinceTick)}", 10, UiTheme.Dim));
            left.Add(col);
            head.Add(left);
            row.Add(head);

            double dist = TileXY.Distance(ally.HomeTile, state.HomeTile);
            bool inRange = AllianceSystem.InReinforceRange(state, ally);
            var range = Widgets.IconText(inRange ? Icon.Shield : Icon.Ring,
                inRange ? $"In range ({dist:N0} tiles) — reinforces your colony"
                        : $"{dist:N0} tiles away — too far to reinforce", 10,
                inRange ? UiTheme.Good : UiTheme.Dim);
            range.style.marginTop = 6;
            row.Add(range);
            var aid = Widgets.IconText(Icon.Clock, pact.PendingRuns >= AllianceSystem.MaxStoredRuns
                    ? $"{pact.PendingRuns} supply runs waiting — collect to receive more"
                    : pact.PendingRuns > 0
                        ? $"{pact.PendingRuns} supply run{(pact.PendingRuns == 1 ? "" : "s")} waiting · next in {Eta(pact.NextAidTick - state.Tick)}"
                        : $"Next supply run in {Eta(pact.NextAidTick - state.Tick)}", 10,
                pact.PendingRuns > 0 ? UiTheme.Good : UiTheme.Dim);
            aid.style.marginTop = 3;
            row.Add(aid);

            var buttons = Widgets.HBox(Justify.SpaceBetween);
            buttons.style.marginTop = 8;
            var profile = Widgets.TextButton("PROFILE", () => PlayerProfilePanel.Open(ctx, ally.Id, ally.Name), 10);
            profile.style.width = Length.Percent(48f);
            buttons.Add(profile);
            var breakBtn = Widgets.TextButton("BREAK PACT", () =>
                ConfirmPanel.Open(
                    $"Break your pact with {ally.Name}?\nThey'll take it personally — expect them to hold a grudge " +
                    "(and maybe come raiding). Supplies already sent are yours to keep.",
                    "BREAK PACT",
                    () =>
                    {
                        var result = AllianceSystem.Break(ctx.State!, ctx.Bots!, ally.Id);
                        GameAudio.Feedback(Sfx.Alert, Haptic.Warning);
                        ui.Toast(result.Ok ? $"Pact with {ally.Name} broken" : result.Reason ?? "Couldn't break it",
                            Icon.Warning, UiTheme.Bad);
                        LocalBootstrap.RequestSync();
                        ui.OpenAlliances();
                    },
                    () => ui.OpenAlliances()), 10);
            breakBtn.style.width = Length.Percent(48f);
            buttons.Add(breakBtn);
            row.Add(buttons);
            return row;
        }

        static VisualElement CandidateRow(GameContext ctx, GameState state, BotGalaxy galaxy, BotEmpire bot, Action invalidate)
        {
            var ui = UIController.Instance!;
            var row = Widgets.Row();
            var box = Widgets.HBox(Justify.SpaceBetween);
            var left = Widgets.HBox();
            left.style.flexShrink = 1f;
            var avatar = Portraits.Avatar(bot.State.Profile.AvatarSeed, bot.Name, 24);
            avatar.style.marginRight = 8;
            left.Add(avatar);
            var col = new VisualElement();
            col.style.flexShrink = 1f;
            col.Add(Widgets.Text(bot.Name, 12, UiTheme.Text, bold: true));
            double dist = TileXY.Distance(bot.HomeTile, state.HomeTile);
            bool inRange = AllianceSystem.InReinforceRange(state, bot);
            col.Add(Widgets.Text($"Might {bot.CachedMight:N0} · {dist:N0} tiles{(inRange ? " · in range" : "")}", 10,
                inRange ? UiTheme.Good : UiTheme.Dim));
            left.Add(col);
            box.Add(left);
            var propose = Widgets.IconButton(Icon.Pact, "PROPOSE", () => Propose(ctx, bot.Id, invalidate), 10);
            propose.style.flexShrink = 0f;
            propose.style.marginLeft = 6;
            box.Add(propose);
            row.Add(box);
            return row;
        }

        /// <summary>Shared by this panel and rival profiles.</summary>
        public static bool Propose(GameContext ctx, int botId, Action? after = null)
        {
            var ui = UIController.Instance!;
            var state = ctx.State;
            var galaxy = ctx.Bots;
            if (state == null || galaxy == null) return false;
            var result = AllianceSystem.Propose(state, galaxy, botId);
            string name = galaxy.Find(botId)?.Name ?? "They";
            if (!result.Ok)
            {
                GameAudio.Feedback(Sfx.Error, Haptic.Error);
                ui.Toast(result.Reason ?? $"{name} declined", Icon.Info, UiTheme.Bad);
                return false;
            }
            GameAudio.Feedback(Sfx.Quest, Haptic.Success);
            ui.Toast($"{name} accepted — you're allies now", Icon.Pact, UiTheme.Good);
            LocalBootstrap.RequestSync();
            after?.Invoke();
            return true;
        }

        /// <summary>Minute-grained countdown (the panel re-renders once a minute).</summary>
        static string Eta(int seconds)
        {
            int minutes = Math.Max(1, (seconds + 59) / 60);
            return minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
        }

        static Label Section(string text)
        {
            var l = Widgets.Text(text, 10, UiTheme.Dim, bold: true);
            l.style.marginTop = 12;
            l.style.marginBottom = 6;
            return l;
        }

        static Label Note(string text)
        {
            var l = Widgets.Text(text, 11, UiTheme.Dim);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 6;
            return l;
        }
    }
}
