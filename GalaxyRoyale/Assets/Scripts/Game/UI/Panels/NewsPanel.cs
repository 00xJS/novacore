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
using GalaxyRoyale.Sim.Text;

namespace GalaxyRoyale.Game.UI
{
    public static class NewsPanel
    {
        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("GALAXY NEWS", ui.CloseModal, 78f);

            if (ctx.State != null && ctx.Bots != null) content.Add(Gazette(ctx));

            var body = new VisualElement();
            content.Add(body);

            // Each wire row's "· 5m ago" line, so ages can tick without rebuilding
            // up to 100 rows every few seconds.
            var ageLabels = new List<(Label meta, NewsItem item)>();
            string MetaText(NewsItem n, int tick) => n.IsBulletin
                ? $"{UiTheme.FmtDuration(Math.Max(0, tick - n.Tick))} ago"
                : $"{(n.AttackerWon ? $"looted {UiTheme.FmtAmount(n.LootMilli)}" : "raid repelled")}" +
                  $" · {UiTheme.FmtDuration(Math.Max(0, tick - n.Tick))} ago";

            int lastCount = -1;
            void Render()
            {
                var state = ctx.State;
                var galaxy = ctx.Bots;
                if (state == null || galaxy == null) return;
                body.Clear();
                ageLabels.Clear();

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
                    if (n.IsBulletin || !n.AttackerWon) continue;
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
                        row.style.paddingTop = 3;
                        row.style.paddingBottom = 3;
                        var name = Widgets.Text(
                            $"#{i + 1}  {NameOf(ctx, entry.id)}", 12,
                            entry.id == 0 ? UiTheme.Accent : UiTheme.Text, bold: true);
                        name.style.flexShrink = 1f;
                        row.Add(name);
                        var right = Widgets.HBox();
                        right.Add(Widgets.Text(
                            $"{entry.raids} raid{(entry.raids == 1 ? "" : "s")} · {UiTheme.FmtAmount(entry.loot)} looted",
                            11, UiTheme.Dim));
                        if (entry.id != 0)
                        {
                            // Whole row taps through to the profile; the chevron says so.
                            int botId = entry.id;
                            row.RegisterCallback<PointerUpEvent>(_ =>
                                PlayerProfilePanel.Open(ctx, botId, NameOf(ctx, botId)));
                            var chevron = Icons.Make(Icon.ChevronRight, 12, UiTheme.Accent);
                            chevron.style.marginLeft = 6;
                            right.Add(chevron);
                        }
                        row.Add(right);
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
                    if (n.IsBulletin)
                    {
                        // Clan politics and other galaxy-wide bulletins.
                        var brow = Widgets.Row();
                        Widgets.SetBorder(brow, UiTheme.Energy, 1f);
                        var bline = Widgets.IconText(n.Text!.Contains("war") ? Icon.Swords : Icon.Pact, n.Text!, 12,
                            UiTheme.Energy, bold: true);
                        bline.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                        brow.Add(bline);
                        var bmeta = Widgets.Text(MetaText(n, state.Tick), 10, UiTheme.Dim);
                        bmeta.style.marginLeft = 19;
                        brow.Add(bmeta);
                        ageLabels.Add((bmeta, n));
                        body.Add(brow);
                        continue;
                    }
                    bool involvesMe = n.AttackerId == 0 || n.DefenderId == 0;
                    bool badForMe = (n.DefenderId == 0 && n.AttackerWon)
                                 || (n.AttackerId == 0 && !n.AttackerWon);

                    var row = Widgets.Row();
                    if (involvesMe) Widgets.SetBorder(row, badForMe ? UiTheme.Bad : UiTheme.Accent, 1.2f);
                    var textColor = involvesMe ? (badForMe ? UiTheme.Bad : UiTheme.Text) : UiTheme.Text;

                    var top = Widgets.HBox(Justify.SpaceBetween);
                    var lead = Widgets.HBox(Justify.FlexStart, Align.FlexStart);
                    lead.style.flexShrink = 1f;
                    var swords = Icons.Make(Icon.Swords, 13, n.AttackerWon ? UiTheme.Bad : UiTheme.Good);
                    swords.style.marginRight = 6;
                    swords.style.marginTop = 1;
                    lead.Add(swords);
                    var line = Widgets.Text(Headline(ctx, n), 12, textColor);
                    line.style.whiteSpace = WhiteSpace.Normal;
                    line.style.flexShrink = 1f;
                    lead.Add(line);
                    top.Add(lead);
                    if (n.AttackerId != 0)
                    {
                        int botId = n.AttackerId;
                        row.RegisterCallback<PointerUpEvent>(_ =>
                            PlayerProfilePanel.Open(ctx, botId, NameOf(ctx, botId)));
                        var chevron = Icons.Make(Icon.ChevronRight, 12, UiTheme.Accent);
                        chevron.style.marginLeft = 6;
                        top.Add(chevron);
                    }
                    row.Add(top);

                    var meta = Widgets.Text(MetaText(n, state.Tick), 10, UiTheme.Dim);
                    meta.style.marginLeft = 19; // under the headline, past the icon
                    row.Add(meta);
                    ageLabels.Add((meta, n));
                    body.Add(row);
                }
            }

            Render();
            long lastAgeBucket = -1;
            refresh = () =>
            {
                int count = ctx.Bots?.News.Count ?? 0;
                int tick = ctx.State?.Tick ?? 0;
                if (count != lastCount)
                {
                    lastCount = count;
                    Render();
                    return;
                }
                // Keep "· Xm ago" counting in real time (user report) by retexting
                // the age lines — no full rebuild.
                long ageBucket = tick / 5;
                if (ageBucket == lastAgeBucket) return;
                lastAgeBucket = ageBucket;
                foreach (var (meta, item) in ageLabels) meta.text = MetaText(item, tick);
            };
            return blocker;
        }

        /// <summary>THE GALACTIC GAZETTE: today's front page. The game's own edition at once;
        /// with the AI writers set up, the AI's edition replaces it when it arrives, and is
        /// kept for the rest of the galaxy day (on this device) so it's written once.</summary>
        static VisualElement Gazette(GameContext ctx)
        {
            var state = ctx.State!;
            var galaxy = ctx.Bots!;
            int day = FlavorText.GazetteDay(state);
            string cacheKey = $"gazette-{day}";
            // An edition from earlier today (an earlier session) is reused as it was.
            if (AiWriter.SavedGazette(day) is { } saved) AiWriter.Remember(cacheKey, saved);

            var card = Widgets.Row();
            Widgets.SetBorder(card, UiTheme.Energy, 1f);
            card.style.marginBottom = 10;
            var masthead = Widgets.HBox(Justify.SpaceBetween);
            masthead.Add(Widgets.Text("THE GALACTIC GAZETTE", 11, UiTheme.Energy, bold: true));
            masthead.Add(Widgets.Text($"DAY {day}", 10, UiTheme.Dim, bold: true));
            card.Add(masthead);
            var headline = Widgets.Text("", 14, UiTheme.Text, bold: true);
            headline.style.whiteSpace = WhiteSpace.Normal;
            headline.style.marginTop = 6;
            card.Add(headline);
            var items = Widgets.Text("", 11, UiTheme.Text);
            items.style.whiteSpace = WhiteSpace.Normal;
            items.style.marginTop = 4;
            card.Add(items);
            var byline = Widgets.Text("", 9, UiTheme.Dim);
            byline.style.marginTop = 6;
            card.Add(byline);

            void Show(string text, bool ai)
            {
                string clean = AiWriter.Clean(text);
                int nl = clean.IndexOf('\n');
                headline.text = nl < 0 ? clean : clean.Substring(0, nl).Trim();
                items.text = nl < 0 ? "" : clean.Substring(nl + 1).Trim();
                byline.text = ai ? "Written by the Gazette's AI desk" : "From the Gazette's wire desk";
            }

            string fallback = FlavorText.GazetteFallback(state, galaxy);
            Show(fallback, false);
            if (AiWriter.Configured && !AiWriter.TryCached(cacheKey, out _)) byline.text = "The AI desk is writing today's edition...";
            AiWriter.Write(ctx, "gazette", FlavorText.GazetteFacts(state, galaxy), day, cacheKey, fallback, (text, ai) =>
            {
                Show(text, ai);
                if (ai) AiWriter.SaveGazette(day, text);
            });
            return card;
        }

        public static string NameOf(GameContext ctx, int empireId) =>
            empireId == 0 ? ctx.State?.Profile.Name ?? "You" : BotNames.NameOf(empireId);

        /// <summary>"[VOID] Moon Moon" for clan members.</summary>
        public static string TaggedName(GameContext ctx, int empireId)
        {
            string name = NameOf(ctx, empireId);
            return ctx.State != null && ctx.Bots != null
                ? GalaxyRoyale.Sim.Systems.ClanSystem.Tagged(ctx.State, ctx.Bots, empireId, name)
                : name;
        }

        /// <summary>One-line headline, shared with the ticker strip. No leading ⚔:
        /// that glyph tofu-boxed on device ("□ □ X raided Y") — callers paint
        /// an Icon.Swords beside it instead.</summary>
        public static string Headline(GameContext ctx, NewsItem n)
        {
            if (n.IsBulletin) return n.Text!;
            string atk = TaggedName(ctx, n.AttackerId);
            string def = TaggedName(ctx, n.DefenderId);
            return n.AttackerWon
                ? $"{atk} raided {def}"
                : $"{def} repelled {atk}";
        }
    }
}
