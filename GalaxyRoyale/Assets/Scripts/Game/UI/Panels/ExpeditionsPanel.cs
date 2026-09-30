// EXPEDITIONS (2026-09-30; MORE › EXPLORE): send a fleet beyond the charted
// galaxy, make the halfway call, read how it went. ExpeditionSystem runs them.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class ExpeditionsPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("EXPEDITIONS", ui.CloseModal, 84f);
            var body = new VisualElement();
            content.Add(body);
            string key = "";

            void Render()
            {
                var s = ctx.State!;
                string k = $"{s.Expeditions.Count}|{ExpeditionSystem.Window(s.Tick)}|{s.ExpeditionLog.Count}|{s.ExpeditionsTaken.Count}|";
                foreach (var e in s.Expeditions) k += $"{e.Id}:{e.Choice}:{s.Tick >= e.MidTick}:{(e.EndTick - s.Tick) / 60};";
                if (k == key) return;
                key = k;
                body.Clear();
                body.Add(Wrap(Widgets.Text("Send a fleet beyond the charted galaxy for a few hours. Halfway there you'll make a call: " +
                    "the bold one pays double and can turn up relics, if it comes off. Stronger fleets, and your commander leading, " +
                    $"improve the odds. Up to {ExpeditionSystem.MaxActive} at once.", 11, UiTheme.Dim), 0));

                if (s.Expeditions.Count > 0) body.Add(Section("OUT NOW"));
                foreach (var e in s.Expeditions)
                {
                    var def = Expeditions.Def(e.Kind);
                    var card = Card(UiTheme.Accent);
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text(def.Name, 13, UiTheme.Text, bold: true));
                    head.Add(Widgets.Text($"home in {UiTheme.FmtDuration(Math.Max(0, e.EndTick - s.Tick))}", 11, UiTheme.Accent, bold: true));
                    card.Add(head);
                    if (e.Led) card.Add(Widgets.Text("Your commander leads it", 10, UiTheme.Energy));
                    if (s.Tick < e.MidTick)
                        card.Add(Wrap(Widgets.Text($"On the way. Something will need your call in {UiTheme.FmtDuration(e.MidTick - s.Tick)}.", 11, UiTheme.Dim), 4));
                    else if (e.Choice < 0)
                    {
                        card.Add(Wrap(Widgets.Text(def.Moment, 12, UiTheme.Text), 4));
                        var row = Widgets.HBox(Justify.SpaceBetween);
                        row.style.marginTop = 8;
                        int id = e.Id;
                        var bold = Widgets.Primary(Widgets.TextButton($"{def.BoldChoice} · {Math.Round(ExpeditionSystem.BoldOdds(s, e) * 100)}%", () =>
                        {
                            ExpeditionSystem.Decide(ctx.State!, id, true);
                            GameAudio.Feedback(Sfx.Confirm, Haptic.Medium);
                            key = "";
                        }, 10));
                        bold.style.width = Length.Percent(49f);
                        var safe = Widgets.TextButton(def.SafeChoice, () =>
                        {
                            ExpeditionSystem.Decide(ctx.State!, id, false);
                            GameAudio.Feedback(Sfx.Confirm, Haptic.Light);
                            key = "";
                        }, 10);
                        safe.style.width = Length.Percent(49f);
                        row.Add(bold);
                        row.Add(safe);
                        card.Add(row);
                        card.Add(Wrap(Widgets.Text("No answer by the time it's due home counts as the careful choice.", 10, UiTheme.Dim), 4));
                    }
                    else card.Add(Wrap(Widgets.Text($"You chose: {(e.Choice == 0 ? def.BoldChoice : def.SafeChoice)}.", 11, UiTheme.Dim), 4));
                    body.Add(card);
                }

                var boardHead = Widgets.HBox(Justify.SpaceBetween);
                boardHead.Add(Section("DESTINATIONS"));
                boardHead.Add(Widgets.Text($"new in {UiTheme.FmtDuration(ExpeditionSystem.BoardLeftSec(s))}", 10, UiTheme.Dim));
                body.Add(boardHead);
                foreach (var offer in ExpeditionSystem.Board(s))
                {
                    var def = Expeditions.Def(offer.Kind);
                    var card = Card(UiTheme.Stroke);
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text(def.Name, 13, UiTheme.Text, bold: true));
                    head.Add(Widgets.Text($"{offer.Hours} h", 11, UiTheme.Accent, bold: true));
                    card.Add(head);
                    card.Add(Wrap(Widgets.Text(def.Blurb, 11, UiTheme.Dim), 2));
                    card.Add(Widgets.Text($"calls for {offer.Recommended:N0} might of ships", 10, UiTheme.Text));
                    bool taken = ExpeditionSystem.Taken(s, offer.Code);
                    bool full = s.Expeditions.Count >= ExpeditionSystem.MaxActive;
                    var o = offer;
                    var send = Widgets.TextButton(taken ? "SENT" : full ? "ALL FLEETS OUT" : "SEND A FLEET", () => Send(ctx, o), 11);
                    send.style.marginTop = 6;
                    send.style.height = 34;
                    Widgets.SetButtonEnabled(send, !taken && !full);
                    card.Add(send);
                    body.Add(card);
                }

                if (s.ExpeditionLog.Count > 0) body.Add(Section("LAST RESULTS"));
                foreach (var log in s.ExpeditionLog)
                {
                    var def = Expeditions.Def(log.Kind);
                    var card = Card(log.Bold && !log.Won ? UiTheme.Bad : log.Bold ? UiTheme.Good : UiTheme.Stroke);
                    card.Add(Widgets.Text(def.Name, 12, UiTheme.Text, bold: true));
                    card.Add(Wrap(Widgets.Text(log.Story, 11, UiTheme.Text), 2));
                    var parts = new List<string> { $"+{UiTheme.FmtAmount(log.LootMilli.Total)}", $"+{log.DarkMatter} DM" };
                    if (log.Relic is { } r) parts.Add($"a {Relics.Def(r).Name}");
                    if (log.ShipsLost > 0) parts.Add($"{log.ShipsLost:N0} ships lost");
                    card.Add(Wrap(Widgets.Text(string.Join(" · ", parts), 11, log.ShipsLost > 0 ? UiTheme.Bad : UiTheme.Good), 4));
                    body.Add(card);
                }
            }

            Render();
            blocker.schedule.Execute(Render).Every(1000);
            ui.OpenModal(blocker);
        }

        static void Send(GameContext ctx, ExpeditionOffer offer)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var def = Expeditions.Def(offer.Kind);
            var (blocker, content, footer) = Widgets.ModalPanelFooter(def.Name.ToUpperInvariant(), () => { ui.CloseModal(); Open(ctx); }, 84f);
            content.Add(Wrap(Widgets.Text($"{def.Blurb} {offer.Hours} hours there and back. It calls for {offer.Recommended:N0} might of ships.", 11, UiTheme.Dim), 0));
            var preview = Widgets.Text("", 11, UiTheme.Accent);
            preview.style.whiteSpace = WhiteSpace.Normal;
            preview.style.marginTop = 6;
            FleetPicker? picker = null;
            Button? go = null;
            void Refresh()
            {
                if (picker == null) return;
                var fleet = picker.Fleet();
                long might = ExpeditionSystem.Might(fleet);
                var probe = new Expedition { Ships = fleet, Recommended = offer.Recommended, Led = CaptainToggle.Want };
                preview.text = might < 1 ? "Pick the ships to send."
                    : $"{might:N0} of {offer.Recommended:N0} might · a bold call comes off {Math.Round(ExpeditionSystem.BoldOdds(state, probe) * 100)}% of the time";
                if (go != null) Widgets.SetButtonEnabled(go, ExpeditionSystem.CanSend(state, offer, fleet).Ok);
            }
            picker = new FleetPicker(content, state, Refresh);
            content.Add(preview);
            if (CaptainToggle.Build(state) is { } lead)
            {
                lead.RegisterCallback<ClickEvent>(_ => Refresh());
                content.Add(lead);
            }
            go = Widgets.Primary(Widgets.TextButton("SEND THE EXPEDITION", () =>
            {
                var res = ExpeditionSystem.Send(ctx.State!, offer, picker!.Fleet(), CaptainToggle.Want, out _);
                if (!res.Ok) { ui.Toast(res.Reason ?? "Can't send", Icon.Warning, UiTheme.Bad); return; }
                GameAudio.Feedback(Sfx.Launch, Haptic.Medium);
                ui.Toast($"Expedition away — back in {offer.Hours} hours", Icon.Compass, UiTheme.Good);
                LocalBootstrap.RequestSync();
                ui.CloseModal();
                Open(ctx);
            }, 13));
            go.style.height = 42;
            footer.Add(go);
            Refresh();
            ui.OpenModal(blocker);
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

        static Label Wrap(Label l, int top)
        {
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = top;
            return l;
        }
    }
}
