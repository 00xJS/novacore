// THE GALACTIC CORE (tap the core on the map). Who holds it, what holding it
// pays, and either the assault composer (your fleet plus your clan's wings,
// with a forecast) or — while you hold it — your garrison: reinforce or withdraw.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class CorePanel
    {
        /// <summary>The map card for the core (replaces the old "forbidden space" strip).</summary>
        public static void OpenCallout(GameContext ctx)
        {
            var ui = UIController.Instance!;
            if (ctx.State == null || ctx.Bots == null) return;
            string Status()
            {
                var state = ctx.State!;
                var galaxy = ctx.Bots!;
                var core = galaxy.Core;
                int ships = MarchSystem.FleetCount(CoreSystem.GarrisonShips(state, galaxy));
                string held = core.HolderId == CoreSystem.GuardiansId ? "" :
                    $" · held {UiTheme.FmtLong(Math.Max(0, state.Tick - core.HeldSinceTick))}";
                return $"Held by {CoreSystem.HolderName(state, galaxy)} · {ships:N0} ships{held}";
            }
            Label? sub = null;
            var strip = CalloutChrome.Strip("THE GALACTIC CORE", Status(), CalloutChrome.Close, l => sub = l,
                CalloutChrome.Act(CoreSystem.PlayerHolds(ctx.Bots) ? "YOUR GARRISON" : "DETAILS",
                    () => { CalloutChrome.Close(); Open(ctx); }, icon: Icon.Target));
            strip.schedule.Execute(() => { if (sub != null && ctx.State != null) sub.text = Status(); }).Every(1000);
            ui.OpenCalloutElement(strip);
        }

        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var galaxy = ctx.Bots;
            if (galaxy == null) return;
            var core = galaxy.Core;
            var (blocker, content, footer) = Widgets.ModalPanelFooter("THE GALACTIC CORE", ui.CloseModal, 84f);

            // ---- who holds it ----
            var holderRow = Widgets.IconText(Icon.Target, "", 13, UiTheme.Energy, bold: true);
            var holderText = holderRow.Q<Label>("text");
            holderText.style.whiteSpace = WhiteSpace.Normal;
            content.Add(holderRow);
            var garrisonLine = Note("", 4);
            content.Add(garrisonLine);
            void RefreshHolder()
            {
                var g = ctx.Bots!;
                string held = g.Core.HolderId == CoreSystem.GuardiansId ? "" :
                    $" · held {UiTheme.FmtLong(Math.Max(0, ctx.State!.Tick - g.Core.HeldSinceTick))}";
                holderText.text = $"Held by {CoreSystem.HolderName(ctx.State!, g)}{held}";
                garrisonLine.text = $"Garrison: {Composition(CoreSystem.GarrisonShips(ctx.State!, g))}. " +
                    $"The station's guns (Lv {CoreSystem.CoreBattery}) fight for whoever holds it.";
            }
            RefreshHolder();

            content.Add(Note($"Whoever holds the core earns tribute every hour: {Pct(CoreSystem.TributeShare)} of their " +
                $"hourly production (plus {CoreSystem.TributeDarkMatter} Dark Matter for you). While a clanmate holds it, " +
                $"you get {Pct(CoreSystem.ClanTributeShare)}. The holder's clanmates near the core help defend it.", 8));

            if (CoreSystem.PlayerHolds(galaxy))
            {
                BuildGarrison(ctx, content, footer);
                blocker.schedule.Execute(RefreshHolder).Every(1000);
                ui.OpenModal(blocker);
                return;
            }
            if (CoreSystem.ClanHolds(state, galaxy))
            {
                var mine = Widgets.IconText(Icon.Pact, $"Your clan holds the core — you collect {Pct(CoreSystem.ClanTributeShare)} " +
                    "of your production every hour it stays that way.", 12, UiTheme.Good, bold: true);
                mine.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                mine.style.marginTop = 12;
                content.Add(mine);
                blocker.schedule.Execute(RefreshHolder).Every(1000);
                ui.OpenModal(blocker);
                return;
            }
            BuildAssault(ctx, content, footer, RefreshHolder);
            ui.OpenModal(blocker);
        }

        // ---------- you hold it ----------

        static void BuildGarrison(GameContext ctx, VisualElement content, VisualElement footer)
        {
            var ui = UIController.Instance!;
            var card = Widgets.Row();
            card.style.marginTop = 12;
            Widgets.SetBorder(card, UiTheme.Good, 1.5f);
            card.Add(Widgets.IconText(Icon.Shield, "YOU HOLD THE CORE", 12, UiTheme.Good, bold: true));
            var status = Note("", 4);
            status.style.color = UiTheme.Text;
            card.Add(status);
            content.Add(card);
            void RefreshStatus()
            {
                var state = ctx.State!;
                var galaxy = ctx.Bots!;
                var garrison = CoreSystem.PlayerGarrison(state);
                int next = Math.Max(0, galaxy.Core.NextTributeTick - state.Tick);
                status.text = $"Your garrison: {Composition(garrison?.Ships ?? new())}. Next tribute in " +
                    $"{UiTheme.FmtDuration(next)} · {state.Stats.CoreHoursHeld} hours of tribute so far. " +
                    "Commanders will try to take it back.";
            }
            RefreshStatus();
            card.schedule.Execute(RefreshStatus).Every(1000);

            var header = Widgets.Text("REINFORCE THE GARRISON", 10, UiTheme.Dim, bold: true);
            header.style.marginTop = 12;
            content.Add(header);
            Button? send = null;
            var preview = Note("", 4);
            FleetPicker? picker = null;
            picker = new FleetPicker(content, ctx.State!, () =>
            {
                var fleet = picker!.Fleet();
                var p = MarchSystem.PreviewMarch(ctx.State!, fleet, CoreSystem.CoreTile);
                preview.text = MarchSystem.FleetCount(fleet) < 1 ? "Pick ships to send to the core."
                    : p.Ok ? $"Joins the garrison in {UiTheme.FmtDuration(MarchSystem.FlightSeconds(ctx.State!, fleet, CoreSystem.CoreTile))} · helium {UiTheme.FmtAmount(p.HeliumCost)}"
                    : p.Reason ?? "Cannot launch";
                if (send != null) Widgets.SetButtonEnabled(send, p.Ok && MarchSystem.FleetCount(fleet) > 0);
            });
            content.Add(preview);

            var row = Widgets.HBox(Justify.SpaceBetween);
            send = Widgets.IconButton(Icon.Shield, "SEND REINFORCEMENTS", () =>
            {
                var res = CoreSystem.SendToCore(ctx.State!, ctx.Bots!, picker!.Fleet(), false, out _, out _);
                if (!res.Ok) { ui.Toast(res.Reason ?? "Cannot launch", Icon.Info, UiTheme.Bad); return; }
                GameAudio.Feedback(Sfx.Launch, Haptic.Medium);
                LocalBootstrap.RequestSync();
                ui.Toast("Reinforcements on the way to the core", Icon.Shield, UiTheme.Good);
                ui.CloseModal();
            }, 12);
            send.style.width = Length.Percent(58f);
            Widgets.SetButtonEnabled(send, false);
            row.Add(send);
            var withdraw = Widgets.TextButton("WITHDRAW", () =>
                ConfirmPanel.Open("Withdraw your garrison?\nThe core falls back to the guardians and your tribute stops.",
                    "WITHDRAW", () =>
                    {
                        if (CoreSystem.PlayerGarrison(ctx.State!) is { } garrison)
                            MarchSystem.RecallMarch(ctx.State!, garrison.Id);
                        LocalBootstrap.RequestSync();
                        ui.Toast("Your garrison is flying home");
                        ui.CloseModal();
                    }, () => Open(ctx)), 12);
            withdraw.style.width = Length.Percent(38f);
            row.Add(withdraw);
            footer.Add(row);
        }

        // ---------- assault ----------

        static void BuildAssault(GameContext ctx, VisualElement content, VisualElement footer, Action refreshHolder)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var galaxy = ctx.Bots!;
            var header = Widgets.Text("SELECT YOUR ASSAULT FLEET", 10, UiTheme.Dim, bold: true);
            header.style.marginTop = 12;
            content.Add(header);

            bool joint = false;
            var wings = new List<StrikeSystem.Wing>();
            Button? jointBtn = null;
            Label? jointNote = null;
            Button? launchBtn = null;
            var preview = Note("", 6);
            preview.style.color = UiTheme.Accent;
            var status = Note("", 2);
            status.style.color = UiTheme.Bad;
            var forecast = new ForecastView();
            FleetPicker? picker = null;
            picker = new FleetPicker(content, state, () => Refresh());
            if (picker.Empty) { footer.Add(Widgets.TextButton("CLOSE", ui.CloseModal, 12)); return; }

            if (state.ClanId != 0)
            {
                jointBtn = Widgets.IconButton(Icon.Pact, "JOINT STRIKE", () => { joint = !joint; Refresh(); }, 11);
                jointBtn.style.marginTop = 10;
                content.Add(jointBtn);
                jointNote = Note("", 4);
                content.Add(jointNote);
            }
            content.Add(preview);
            content.Add(status);
            content.Add(forecast.Root);

            void Refresh()
            {
                refreshHolder();
                var s = ctx.State!;
                var g = ctx.Bots!;
                if (CoreSystem.PlayerHolds(g) || CoreSystem.ClanHolds(s, g))
                {
                    status.text = "The core changed hands — reopen it to see your options.";
                    if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, false);
                    return;
                }
                var fleet = picker!.Fleet();
                int count = MarchSystem.FleetCount(fleet);
                var p = MarchSystem.PreviewMarch(s, fleet, CoreSystem.CoreTile);
                int arrive = s.Tick + MarchSystem.FlightSeconds(s, fleet, CoreSystem.CoreTile);
                int exclude = g.Core.HolderId > 0 ? g.Core.HolderId : 0;
                wings = count > 0 && p.Ok ? StrikeSystem.StrikeWings(s, g, CoreSystem.CoreTile, arrive, exclude)
                    : new List<StrikeSystem.Wing>();
                if (jointBtn != null && jointNote != null)
                {
                    int wingShips = 0;
                    foreach (var w in wings) wingShips += MarchSystem.FleetCount(w.Ships);
                    bool on = joint && wings.Count > 0;
                    Widgets.SetCaption(jointBtn, count < 1 ? "JOINT STRIKE"
                        : wings.Count == 0 ? "JOINT STRIKE — NO CLANMATES CAN MAKE IT"
                        : on ? $"JOINT STRIKE ON: {wings.Count} CLANMATE{(wings.Count == 1 ? "" : "S")} · +{wingShips} WARSHIPS"
                        : $"JOINT STRIKE ({wings.Count} ready · +{wingShips} warships)");
                    Widgets.SetButtonHighlight(jointBtn, on);
                    Widgets.SetButtonEnabled(jointBtn, wings.Count > 0);
                    jointNote.text = wings.Count > 0
                        ? $"{string.Join(", ", wings.ConvertAll(w => w.Bot.Name))} can land with you."
                        : $"Clanmates join when they can reach the core with you (within {StrikeSystem.StrikeRange:N0} tiles, rested).";
                }
                if (count < 1)
                {
                    preview.text = "Select ships to plan the assault.";
                    status.text = "";
                    forecast.Hide();
                    if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, false);
                    return;
                }
                preview.text = p.Ok ? $"Arrives in {UiTheme.FmtDuration(arrive - s.Tick)} · helium {UiTheme.FmtAmount(p.HeliumCost)}" : "";
                status.text = p.Ok ? "" : p.Reason ?? "Cannot launch";
                var defence = CoreSystem.DefenceOf(s, g);
                var attackers = new List<Dictionary<HullId, int>> { fleet };
                if (joint) foreach (var w in wings) attackers.Add(w.Ships);
                forecast.Show(BattleForecast.Predict(ClanSystem.Combine(attackers), ClanSystem.Combine(defence.Lines),
                        ResearchSystem.CombatMods(s), defence.Mods),
                    "Win and your survivors stay as the core's garrison.");
                if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, p.Ok);
            }

            launchBtn = Widgets.IconButton(Icon.Swords, "LAUNCH ASSAULT", () =>
            {
                var res = CoreSystem.SendToCore(ctx.State!, ctx.Bots!, picker!.Fleet(), joint && wings.Count > 0,
                    out _, out int flying);
                if (!res.Ok) { ui.Toast(res.Reason ?? "Cannot launch", Icon.Info, UiTheme.Bad); return; }
                GameAudio.Feedback(Sfx.Launch, Haptic.Medium);
                LocalBootstrap.RequestSync();
                ui.Toast("Assault on the core launched" + (flying > 0 ? $" · {flying} clanmate{(flying == 1 ? "" : "s")} flying with you" : ""),
                    Icon.Swords, UiTheme.Energy);
                ui.CloseModal();
            }, 14);
            launchBtn.style.height = 42;
            footer.Add(launchBtn);
            Refresh();
            footer.schedule.Execute(Refresh).Every(1000);
        }

        // ---------- bits ----------

        static string Composition(Dictionary<HullId, int> fleet)
        {
            var parts = new List<string>();
            foreach (var hull in Ships.All)
                if (fleet.TryGetValue(hull, out var n) && n > 0) parts.Add($"{Ships.Defs[hull].Name} ×{n:N0}");
            return parts.Count > 0 ? string.Join(" · ", parts) : "none";
        }

        /// <summary>"12%" — the P0 format adds a culture-dependent space ("12 %").</summary>
        static string Pct(double share) => $"{Math.Round(share * 100):0}%";

        static Label Note(string text, float marginTop)
        {
            var l = Widgets.Text(text, 11, UiTheme.Dim);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = marginTop;
            return l;
        }
    }
}
