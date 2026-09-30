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
            var (blocker, content, footer) = Widgets.ModalPanelFooter("THE GALACTIC CORE", ui.CloseModal, 84f);
            bool holds = CoreSystem.PlayerHolds(galaxy);
            bool clanHolds = !holds && CoreSystem.ClanHolds(state, galaxy);

            // ---- who holds it (map redesign, 2026-09-29) ----
            var holder = Widgets.HBox();
            holder.style.paddingLeft = 12;
            holder.style.paddingRight = 12;
            holder.style.paddingTop = 10;
            holder.style.paddingBottom = 10;
            var holderTint = holds || clanHolds ? UiTheme.Helium : UiTheme.Accent;
            Holo.Frame(holder, UiTheme.A(holderTint, 0.08f), UiTheme.A(holderTint, 0.6f), 10f);
            var badge = new VisualElement { pickingMode = PickingMode.Ignore };
            badge.style.width = 40;
            badge.style.height = 40;
            badge.style.marginRight = 12;
            badge.style.justifyContent = Justify.Center;
            badge.style.alignItems = Align.Center;
            Holo.Frame(badge, UiTheme.A(UiTheme.Bg, 0.9f), holderTint, 0f, 1.5f, FrameShape.Hex);
            badge.Add(Icons.Make(holds ? Icon.Shield : Icon.Target, 20, holderTint));
            holder.Add(badge);
            var who = new VisualElement();
            who.style.flexGrow = 1;
            who.style.flexShrink = 1;
            who.Add(Widgets.Text("HELD BY", 9, UiTheme.Dim, bold: true));
            var holderName = Widgets.Text("", 15, UiTheme.Text, bold: true);
            holderName.style.whiteSpace = WhiteSpace.Normal;
            who.Add(holderName);
            var holderSince = Widgets.Text("", 11, UiTheme.Dim);
            who.Add(holderSince);
            holder.Add(who);
            if (holds || clanHolds)
            {
                var you = Widgets.Heading(holds ? "YOU" : "CLAN", 8, UiTheme.Bg, 1f);
                you.style.backgroundColor = UiTheme.Helium;
                you.style.paddingLeft = 7;
                you.style.paddingRight = 7;
                you.style.paddingTop = 3;
                you.style.paddingBottom = 3;
                holder.Add(you);
            }
            content.Add(holder);

            // ---- three stat tiles ----
            var stats = Widgets.HBox(Justify.SpaceBetween);
            stats.style.marginTop = 10;
            Label Tile(string caption, UnityEngine.Color valueColor)
            {
                var tile = new VisualElement();
                tile.style.width = Length.Percent(31.5f);
                tile.style.paddingLeft = 8;
                tile.style.paddingRight = 6;
                tile.style.paddingTop = 7;
                tile.style.paddingBottom = 7;
                Holo.Frame(tile, UiTheme.A(UiTheme.Accent, 0.07f), UiTheme.A(UiTheme.Accent, 0.35f), 6f);
                tile.Add(Widgets.Text(caption, 9, UiTheme.Dim, bold: true));
                var v = Widgets.Heading("", 12, valueColor, 0.4f);
                v.style.marginTop = 3;
                tile.Add(v);
                stats.Add(tile);
                return v;
            }
            var tribute = Tile(galaxy.Core.HolderId == CoreSystem.GuardiansId ? "GUARDIANS" : "NEXT TRIBUTE", UiTheme.Energy);
            var garrison = Tile("GARRISON", UiTheme.Text);
            var threat = Tile("INBOUND", UiTheme.Bad);
            content.Add(stats);

            void RefreshHolder()
            {
                var s = ctx.State!;
                var g = ctx.Bots!;
                var core = g.Core;
                holderName.text = CoreSystem.HolderName(s, g);
                string held = core.HolderId == CoreSystem.GuardiansId ? "Nobody has taken it" :
                    $"Held {UiTheme.FmtLong(Math.Max(0, s.Tick - core.HeldSinceTick))}";
                holderSince.text = $"{held} · seized {core.TimesSeized} time{(core.TimesSeized == 1 ? "" : "s")}";
                tribute.text = core.HolderId == CoreSystem.GuardiansId
                    ? (core.GuardiansRebuildTick > s.Tick ? $"full in {UiTheme.FmtDuration(core.GuardiansRebuildTick - s.Tick)}" : "full strength")
                    : UiTheme.FmtDuration(Math.Max(0, core.NextTributeTick - s.Tick));
                garrison.text = $"{MarchSystem.FleetCount(CoreSystem.GarrisonShips(s, g)):N0} ships";
                int inbound = CoreSystem.InboundAssaults(s, g);
                threat.text = inbound == 0 ? "none" : $"{inbound} assault{(inbound == 1 ? "" : "s")}";
                threat.style.color = inbound == 0 ? UiTheme.Dim : UiTheme.Bad;
            }
            RefreshHolder();
            blocker.schedule.Execute(RefreshHolder).Every(1000);

            // ---- tabs: the action (garrison / assault / clan), BUFFS, HISTORY ----
            var tabBar = Widgets.HBox();
            tabBar.style.marginTop = 12;
            tabBar.style.borderBottomWidth = 1;
            tabBar.style.borderBottomColor = UiTheme.Stroke;
            content.Add(tabBar);
            var action = new VisualElement();
            var buffs = new VisualElement();
            var history = new VisualElement();
            foreach (var pane in new[] { action, buffs, history })
            {
                pane.style.marginTop = 8;
                content.Add(pane);
            }
            var tabs = new List<(Button button, VisualElement pane, bool usesFooter)>();
            void Show(int index)
            {
                for (int k = 0; k < tabs.Count; k++)
                {
                    bool on = k == index;
                    tabs[k].pane.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                    var b = tabs[k].button;
                    b.style.color = on ? UiTheme.Accent : UiTheme.Dim;
                    b.style.borderBottomColor = on ? UiTheme.Accent : UiTheme.A(UiTheme.Accent, 0f);
                    b.style.backgroundColor = on ? UiTheme.A(UiTheme.Accent, 0.12f) : UiTheme.A(UiTheme.Accent, 0f);
                }
                footer.style.display = tabs[index].usesFooter ? DisplayStyle.Flex : DisplayStyle.None;
            }
            void Tab(string label, VisualElement pane, bool usesFooter)
            {
                int index = tabs.Count;
                var b = new Button(() => { GameAudio.Tap(); Show(index); }) { text = label };
                UiFonts.Display(b, 1.6f);
                b.style.fontSize = Widgets.Sized(10);
                b.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
                b.style.flexGrow = 1;
                b.style.flexBasis = 0;
                b.style.height = 36;
                b.style.marginLeft = b.style.marginRight = b.style.marginTop = b.style.marginBottom = 0;
                b.style.borderTopWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
                b.style.borderBottomWidth = 2;
                b.style.borderTopLeftRadius = b.style.borderTopRightRadius = 0;
                b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 0;
                tabBar.Add(b);
                tabs.Add((b, pane, usesFooter));
            }
            Tab(holds ? "GARRISON" : clanHolds ? "CLAN" : "ASSAULT", action, !clanHolds);
            Tab("BUFFS", buffs, false);
            Tab("HISTORY", history, false);

            BuildBuffs(ctx, buffs, holds, clanHolds);
            BuildHistory(ctx, history);

            if (holds) BuildGarrison(ctx, action, footer);
            else if (clanHolds)
            {
                var mine = Widgets.IconText(Icon.Pact, $"Your clan holds the core — you collect {Pct(EmbassySystem.ClanTributeShare(state))} " +
                    "of your production every hour it stays that way.", 12, UiTheme.Good, bold: true);
                mine.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                action.Add(mine);
            }
            else BuildAssault(ctx, action, footer, RefreshHolder);
            Show(0);
            ui.OpenModal(blocker);
        }

        // ---------- buffs ----------

        static void BuildBuffs(GameContext ctx, VisualElement pane, bool holds, bool clanHolds)
        {
            pane.Add(Note(holds ? "Yours while you hold the core:" : "Take and hold the core to earn these:", 0));
            void Buff(Icon icon, UnityEngine.Color tint, string name, string what, bool active, bool isNew = false)
            {
                var row = Widgets.HBox();
                row.style.marginTop = 6;
                row.style.paddingLeft = 10;
                row.style.paddingRight = 10;
                row.style.paddingTop = 7;
                row.style.paddingBottom = 7;
                Holo.Frame(row, UiTheme.A(tint, 0.06f), UiTheme.A(tint, 0.45f), 6f);
                var glyph = Icons.Make(icon, 20, tint);
                glyph.style.marginRight = 10;
                row.Add(glyph);
                var text = new VisualElement();
                text.style.flexGrow = 1;
                text.style.flexShrink = 1;
                text.Add(Widgets.Text(name, 13, UiTheme.Text, bold: true));
                var d = Widgets.Text(what, 11, UiTheme.Dim);
                d.style.whiteSpace = WhiteSpace.Normal;
                text.Add(d);
                row.Add(text);
                var chip = Widgets.Heading(active ? "ACTIVE" : isNew ? "NEW" : "LOCKED", 7,
                    active ? UiTheme.Good : isNew ? UiTheme.Magenta : UiTheme.Dim, 1f);
                chip.style.marginLeft = 8;
                row.Add(chip);
                pane.Add(row);
            }
            Buff(Icon.Coin, UiTheme.Energy, "Core Tribute",
                $"Every hour: {Pct(CoreSystem.TributeShare)} of your hourly production + {CoreSystem.TributeDarkMatter} Dark Matter", holds);
            Buff(Icon.Shield, UiTheme.Quartz, "Core Battery",
                $"The station's guns (Lv {CoreSystem.CoreBattery}) fight for your garrison", holds);
            Buff(Icon.Pact, UiTheme.Helium, "Clan Tribute",
                $"Clanmates earn {Pct(CoreSystem.ClanTributeShare)} of their hourly production while the core is your clan's" +
                (EmbassySystem.Level(ctx.State!) > 0 ? $" (you: {Pct(EmbassySystem.ClanTributeShare(ctx.State!))} with your Clan Embassy)" : ""),
                holds || clanHolds);
            Buff(Icon.Bolt, UiTheme.Magenta, "Galactic Command",
                $"+{Pct(CoreSystem.CommandSpeedMult - 1)} march speed for every fleet you send", holds, isNew: true);
            Buff(Icon.Eye, UiTheme.Magenta, "Core Beacon",
                "Every assault launched at the core is announced the moment it leaves", holds, isNew: true);
        }

        // ---------- history ----------

        static void BuildHistory(GameContext ctx, VisualElement pane)
        {
            var list = new VisualElement();
            pane.Add(list);
            int shown = -1, shownTop = -1;
            void Refresh()
            {
                var g = ctx.Bots!;
                var log = g.Core.History;
                int top = log.Count > 0 ? log[0].AtTick : -1;
                if (log.Count == shown && top == shownTop) return;
                shown = log.Count;
                shownTop = top;
                list.Clear();
                if (log.Count == 0)
                {
                    // Galaxies from before the log (2026-09-29) may already have a holder.
                    list.Add(Note(g.Core.HolderId == CoreSystem.GuardiansId && g.Core.TimesSeized == 0
                        ? "Nothing yet — the Core Guardians have held it since the galaxy began."
                        : $"{CoreSystem.HolderName(ctx.State!, g)} has held the Core for " +
                          $"{UiTheme.FmtLong(Math.Max(0, ctx.State!.Tick - g.Core.HeldSinceTick))}. From now on every seizure, " +
                          "failed assault and guardian rebuild is recorded here.", 4));
                    return;
                }
                int now = ctx.State!.Tick;
                foreach (var e in log)
                {
                    var (text, sub, tint) = Describe(e);
                    var row = Widgets.HBox(Justify.FlexStart, Align.FlexStart);
                    row.style.paddingTop = 6;
                    row.style.paddingBottom = 6;
                    row.style.paddingLeft = 12;
                    row.style.marginLeft = 6;
                    row.style.borderLeftWidth = 2;
                    row.style.borderLeftColor = UiTheme.A(UiTheme.Accent, 0.35f);
                    var dot = Widgets.CountBubble(10f, 1, tint, out _);
                    dot.style.left = -6;
                    dot.style.top = 10;
                    row.Add(dot);
                    var col = new VisualElement();
                    col.style.flexShrink = 1;
                    var t = Widgets.Text(text, 13, UiTheme.Text, bold: true);
                    t.style.whiteSpace = WhiteSpace.Normal;
                    col.Add(t);
                    string ago = $"{UiTheme.FmtLong(Math.Max(0, now - e.AtTick))} ago";
                    col.Add(Widgets.Text(sub.Length > 0 ? $"{ago} · {sub}" : ago, 11, UiTheme.Dim));
                    row.Add(col);
                    list.Add(row);
                }
            }
            Refresh();
            pane.schedule.Execute(Refresh).Every(2000);
        }

        /// <summary>One history line: what happened, the detail under it, and its dot colour.</summary>
        public static (string text, string sub, UnityEngine.Color tint) Describe(CoreLogEntry e)
        {
            bool you = e.ActorId == 0;
            switch (e.Kind)
            {
                case CoreLogKind.Seized:
                    return (you ? $"You seized the Core from {e.Other}" : $"{e.Actor} seized the Core from {e.Other}",
                        e.HeldSec > 0 ? $"they had held it {UiTheme.FmtLong(e.HeldSec)}" : "",
                        you ? UiTheme.Good : UiTheme.Bad);
                case CoreLogKind.Repelled:
                    return (you ? $"Your assault broke on {e.Other}" : $"{e.Actor}'s assault broke on {e.Other}",
                        e.ShipsLost > 0 ? $"{e.ShipsLost:N0} ships lost" : "",
                        you ? UiTheme.Bad : UiTheme.Accent);
                case CoreLogKind.Abandoned:
                    return (you ? "You withdrew from the Core" : $"{e.Actor} left the Core",
                        e.HeldSec > 0 ? $"held {UiTheme.FmtLong(e.HeldSec)} · the guardians returned" : "the guardians returned",
                        UiTheme.Dim);
                default:
                    return ("The Core Guardians rebuilt to full strength", "", UiTheme.DarkMatter);
            }
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
        static string Pct(double share) => $"{Math.Round(share * 1000) / 10:0.#}%";

        static Label Note(string text, float marginTop)
        {
            var l = Widgets.Text(text, 11, UiTheme.Dim);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = marginTop;
            return l;
        }
    }
}
