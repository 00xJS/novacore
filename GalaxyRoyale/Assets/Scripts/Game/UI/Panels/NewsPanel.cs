// GALAXY NEWS — the global chat's slot reborn as a battle wire (user spec):
// every raid in the galaxy posts here, so you can see who's been looting and
// attacking the most and pick your own targets accordingly. The MOST WANTED
// board tallies the busiest raiders in the current feed; tapping a rival's
// line opens their profile.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Game.UI
{
    public static class NewsPanel
    {
        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("GALAXY NEWS", ui.CloseModal, 78f);

            var body = new VisualElement();
            content.Add(body);

            int lastCount = -1;
            void Render()
            {
                var state = ctx.State;
                var galaxy = ctx.Bots;
                if (state == null || galaxy == null) return;
                body.Clear();

                if (galaxy.News.Count == 0)
                {
                    var quiet = Widgets.Text(
                        "All quiet on the galactic front.\nBattle reports from every commander post here.",
                        12, UiTheme.Dim);
                    quiet.style.whiteSpace = WhiteSpace.Normal;
                    quiet.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                    quiet.style.marginTop = 16;
                    body.Add(quiet);
                    return;
                }

                // ---- MOST WANTED: busiest successful raiders in the feed ----
                var wins = new Dictionary<int, (int raids, long loot)>();
                foreach (var n in galaxy.News)
                {
                    if (!n.AttackerWon) continue;
                    var cur = wins.TryGetValue(n.AttackerId, out var w) ? w : (0, 0L);
                    wins[n.AttackerId] = (cur.Item1 + 1, cur.Item2 + n.LootMilli);
                }
                if (wins.Count > 0)
                {
                    var header = Widgets.Text("MOST WANTED", 10, UiTheme.Bad, bold: true);
                    header.style.marginBottom = 4;
                    body.Add(header);
                    var ranked = new List<(int id, int raids, long loot)>();
                    foreach (var kv in wins) ranked.Add((kv.Key, kv.Value.raids, kv.Value.loot));
                    ranked.Sort((a, b) => b.raids != a.raids
                        ? b.raids.CompareTo(a.raids) : b.loot.CompareTo(a.loot));
                    for (int i = 0; i < Math.Min(3, ranked.Count); i++)
                    {
                        var entry = ranked[i];
                        var row = Widgets.HBox(Justify.SpaceBetween);
                        row.style.marginBottom = 2;
                        var name = Widgets.Text(
                            $"#{i + 1}  {NameOf(ctx, entry.id)}", 12,
                            entry.id == 0 ? UiTheme.Accent : UiTheme.Text, bold: true);
                        if (entry.id != 0)
                        {
                            int botId = entry.id;
                            name.RegisterCallback<PointerUpEvent>(_ =>
                                PlayerProfilePanel.Open(ctx, botId, NameOf(ctx, botId)));
                        }
                        row.Add(name);
                        row.Add(Widgets.Text(
                            $"{entry.raids} raids · {UiTheme.FmtAmount(entry.loot)} looted",
                            11, UiTheme.Dim));
                        body.Add(row);
                    }
                    var divider = new VisualElement();
                    divider.style.height = 1;
                    divider.style.backgroundColor = UiTheme.Stroke;
                    divider.style.marginTop = 6;
                    divider.style.marginBottom = 8;
                    body.Add(divider);
                }

                // ---- the wire, newest first ----
                for (int i = galaxy.News.Count - 1; i >= 0; i--)
                {
                    var n = galaxy.News[i];
                    bool involvesMe = n.AttackerId == 0 || n.DefenderId == 0;
                    bool badForMe = (n.DefenderId == 0 && n.AttackerWon)
                                 || (n.AttackerId == 0 && !n.AttackerWon);

                    var row = Widgets.Row();
                    if (involvesMe) Widgets.SetBorder(row, badForMe ? UiTheme.Bad : UiTheme.Accent, 1.2f);
                    var line = Widgets.Text(Headline(ctx, n), 12,
                        involvesMe ? (badForMe ? UiTheme.Bad : UiTheme.Text) : UiTheme.Text);
                    line.style.whiteSpace = WhiteSpace.Normal;
                    row.Add(line);
                    var meta = Widgets.Text(
                        $"{(n.AttackerWon ? $"looted {UiTheme.FmtAmount(n.LootMilli)}" : "raid repelled")}" +
                        $" · {UiTheme.FmtDuration(Math.Max(0, state.Tick - n.Tick))} ago",
                        10, UiTheme.Dim);
                    row.Add(meta);
                    if (n.AttackerId != 0)
                    {
                        int botId = n.AttackerId;
                        row.RegisterCallback<PointerUpEvent>(_ =>
                            PlayerProfilePanel.Open(ctx, botId, NameOf(ctx, botId)));
                    }
                    body.Add(row);
                }
            }

            Render();
            long lastAgeBucket = -1;
            refresh = () =>
            {
                int count = ctx.Bots?.News.Count ?? 0;
                // Re-render every few seconds even without new items so the
                // "· Xm ago" ages keep counting in real time (user report).
                long ageBucket = (ctx.State?.Tick ?? 0) / 5;
                if (count == lastCount && ageBucket == lastAgeBucket) return;
                lastCount = count;
                lastAgeBucket = ageBucket;
                Render();
            };
            return blocker;
        }

        public static string NameOf(GameContext ctx, int empireId) =>
            empireId == 0 ? ctx.State?.Profile.Name ?? "You" : BotNames.NameOf(empireId);

        /// <summary>One-line headline, shared with the ticker strip.</summary>
        public static string Headline(GameContext ctx, NewsItem n)
        {
            string atk = NameOf(ctx, n.AttackerId);
            string def = NameOf(ctx, n.DefenderId);
            return n.AttackerWon
                ? $"⚔ {atk} raided {def}"
                : $"⚔ {def} repelled {atk}";
        }
    }
}
