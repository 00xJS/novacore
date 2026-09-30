// STORY (2026-09-30; MORE › STORY): the campaign, "The Long Night". The open
// chapter's briefing from HALCYON, its objectives, the Pirate Lord at the end
// of it (portrait, doctrine, their lair on the map), CLAIM when it's done, a
// lord who has come back for a rematch, and the chapters already told.
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class CampaignPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel(Campaign.Name.ToUpperInvariant(), ui.CloseModal, 84f);
            var body = new VisualElement();
            content.Add(body);
            string key = "";

            void Render()
            {
                var s = ctx.State!;
                var c = s.Campaign;
                var ch = CampaignSystem.Current(s);
                string k = $"{c.Chapter}|{c.Open}|{c.LairId}|{c.RematchId}|{CampaignSystem.Blocker(s)}";
                if (ch != null && c.Open)
                    for (int i = 0; i < ch.Objectives.Count; i++) k += $"|{CampaignSystem.Progress(s, i).have}";
                if (k == key) return;
                key = k;
                body.Clear();

                if (ch == null)
                {
                    body.Add(Wrap(Widgets.Heading("THE LONG NIGHT HAS ENDED", 14, UiTheme.Energy, 1.4f), 0));
                    body.Add(Wrap(Widgets.Text("Every lord of the Court has fallen. They still rise now and then with new fleets: " +
                        "the galaxy doesn't stay quiet for long.", 12, UiTheme.Text), 6));
                }
                else
                {
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text($"CHAPTER {ch.Number} OF {Campaign.Chapters.Count}", 10, UiTheme.Dim, bold: true));
                    head.Add(Widgets.Text(c.Open ? "IN PROGRESS" : "COMING UP", 10, c.Open ? UiTheme.Accent : UiTheme.Dim, bold: true));
                    body.Add(head);
                    body.Add(Wrap(Widgets.Heading(ch.Title.ToUpperInvariant(), 16, UiTheme.Accent, 1.4f), 2));

                    if (!c.Open)
                    {
                        body.Add(Wrap(Widgets.Text(CampaignSystem.Blocker(s) ?? "Opening…", 12, UiTheme.Energy, bold: true), 8));
                        body.Add(Wrap(Widgets.Text("HALCYON is still piecing together where the Court went next. " +
                            "Keep building: the chapter opens when your colony is ready.", 11, UiTheme.Dim), 4));
                    }
                    else
                    {
                        var brief = Card(UiTheme.Stroke);
                        brief.Add(Widgets.Text($"{Campaign.Narrator}, your ship-mind", 10, UiTheme.Accent, bold: true));
                        brief.Add(Wrap(Widgets.Text(ch.Intro, 12, UiTheme.Text), 4));
                        body.Add(brief);

                        body.Add(Section("OBJECTIVES"));
                        for (int i = 0; i < ch.Objectives.Count; i++)
                        {
                            var (have, need) = CampaignSystem.Progress(s, i);
                            bool done = have >= need;
                            var row = Widgets.HBox(Justify.SpaceBetween);
                            row.style.marginTop = 5;
                            var left = Widgets.HBox();
                            var tick = Icons.Make(done ? Icon.Check : Icon.Dot, 13f, done ? UiTheme.Good : UiTheme.Dim);
                            tick.style.marginRight = 8;
                            left.Add(tick);
                            var t = Widgets.Text(ch.Objectives[i].Text, 12, done ? UiTheme.Good : UiTheme.Text);
                            t.style.whiteSpace = WhiteSpace.Normal;
                            t.style.flexShrink = 1;
                            left.Add(t);
                            left.style.flexShrink = 1;
                            row.Add(left);
                            if (need > 1) row.Add(Widgets.Text($"{UiTheme.FmtCount(have)} / {UiTheme.FmtCount(need)}", 11, done ? UiTheme.Good : UiTheme.Dim));
                            body.Add(row);
                        }

                        var lord = PirateLords.Def(ch.Lord);
                        body.Add(Section("THE LORD"));
                        body.Add(LordCard(ctx, lord, LairSystem.Find(s, c.LairId), beaten: LairSystem.Wins(s, ch.Lord) > 0 && c.LairId.Length == 0));

                        var (pay, dm) = CampaignSystem.Reward(s);
                        bool ready = CampaignSystem.CanClaim(s);
                        var claim = Widgets.Primary(Widgets.TextButton(ready
                            ? $"CLAIM · +{UiTheme.FmtAmount(pay.Total)} · +{dm} DM"
                            : $"FINISH THE CHAPTER · +{UiTheme.FmtAmount(pay.Total)} · +{dm} DM", () =>
                        {
                            var st = ctx.State!;
                            string outro = Campaign.Chapters[st.Campaign.Chapter].Outro;
                            if (!CampaignSystem.Claim(st).Ok) return;
                            GameAudio.Feedback(Sfx.Victory, Haptic.Success);
                            ui.Toast($"Chapter complete: {outro}", Icon.Star, UiTheme.Energy);
                            LocalBootstrap.RequestSync();
                            key = "";
                        }, 11));
                        claim.style.marginTop = 12;
                        claim.style.height = 40;
                        Widgets.SetButtonEnabled(claim, ready);
                        body.Add(claim);
                    }
                }

                // A lord back for a rematch.
                if (LairSystem.Find(s, c.RematchId) is { } rematch)
                {
                    var (lordIx, tier, _) = LairSystem.Parse(rematch);
                    body.Add(Section($"RETURNED · TIER {tier + 1}"));
                    body.Add(LordCard(ctx, PirateLords.Def(lordIx), rematch, beaten: false));
                }

                // The story so far.
                if (c.Chapter > 0)
                {
                    body.Add(Section("THE STORY SO FAR"));
                    for (int i = Math.Min(c.Chapter, Campaign.Chapters.Count) - 1; i >= 0; i--)
                    {
                        var past = Campaign.Chapters[i];
                        var card = Card(UiTheme.Stroke);
                        card.Add(Widgets.Text($"{past.Number}. {past.Title}", 12, UiTheme.Text, bold: true));
                        card.Add(Wrap(Widgets.Text(past.Outro, 11, UiTheme.Dim), 3));
                        body.Add(card);
                    }
                }
            }

            Render();
            blocker.schedule.Execute(Render).Every(1000);
            ui.OpenModal(blocker);
        }

        static VisualElement LordCard(GameContext ctx, PirateLordDef lord, Sim.Map.MapNode? lair, bool beaten)
        {
            var s = ctx.State!;
            var card = Card(beaten ? UiTheme.Good : UiTheme.Bad);
            var top = Widgets.HBox();
            var face = Portraits.Avatar(lord.Face, lord.Name, 44);
            Widgets.SetBorder(face, beaten ? UiTheme.Good : UiTheme.Bad, 1.5f);
            face.style.marginRight = 10;
            top.Add(face);
            var col = new VisualElement();
            col.style.flexShrink = 1;
            col.Add(Widgets.Text(lord.Name, 13, UiTheme.Text, bold: true));
            col.Add(Widgets.Text(lord.Epithet, 11, beaten ? UiTheme.Good : UiTheme.Bad));
            top.Add(col);
            card.Add(top);
            card.Add(Wrap(Widgets.Text(beaten ? $"\"{lord.Last}\"" : $"\"{lord.Boast}\"", 11, UiTheme.Text), 6));
            card.Add(Wrap(Widgets.Text($"Doctrine: {lord.Doctrine}.", 11, UiTheme.Dim), 4));
            if (beaten)
            {
                card.Add(Wrap(Widgets.Text("Defeated.", 11, UiTheme.Good, bold: true), 4));
                return card;
            }
            if (lair == null) return card;
            long ships = 0;
            foreach (var kv in MarchSystem.CampGarrison(lair)) ships += kv.Value;
            double dist = TileXY.Distance(lair.Tile, s.HomeTile);
            card.Add(Wrap(Widgets.Text($"Lair at {lair.Tile.X}, {lair.Tile.Y} · {dist:0} tiles away · {ships:N0} ships", 11, UiTheme.Energy), 4));
            var (pay, dm) = LairSystem.Reward(s, LairSystem.Parse(lair).lord, LairSystem.Parse(lair).tier);
            card.Add(Wrap(Widgets.Text($"Beating them pays +{UiTheme.FmtAmount(pay.Total)} · +{dm} DM" +
                (LairSystem.Parse(lair).tier == 0 ? " · a relic" : ""), 11, UiTheme.Good), 2));
            var show = Widgets.TextButton("SHOW THE LAIR", () => EventsPanel.ShowOnMap(ctx, lair.Tile, lair), 11);
            show.style.marginTop = 8;
            show.style.height = 34;
            card.Add(show);
            return card;
        }

        static Label Section(string text)
        {
            var l = Widgets.Text(text, 10, UiTheme.Dim, bold: true);
            l.style.marginTop = 12;
            l.style.marginBottom = 4;
            return l;
        }

        static VisualElement Card(UnityEngine.Color border)
        {
            var c = Widgets.Row();
            c.style.marginTop = 6;
            Widgets.SetBorder(c, border, 1.2f);
            return c;
        }

        static T Wrap<T>(T l, int top) where T : VisualElement
        {
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = top;
            return l;
        }
    }
}
