// Raid launcher — opened by tapping a rival commander's planet on the galaxy
// map (or RAID on their profile). Shows the target's public intel (name,
// might, coordinates, shield state), a FLEET SELECTION SCREEN (per-hull
// sliders — user spec: choose what you send, not an auto all-docked fleet),
// the travel/fuel preview, a battle forecast, and a LAUNCH button pinned to
// the box footer. The defense readout — and with it the forecast — stays
// CLASSIFIED until a spy probe has visited.
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
    public static class RaidPanel
    {
        public static void Open(GameContext ctx, int targetBotId)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;

            var bot = ctx.Bots?.Find(targetBotId);
            if (bot == null)
            {
                var (b, c) = Widgets.ModalPanel("RAID TARGET", ui.CloseModal, 0);
                c.Add(Widgets.Text("No telemetry on that colony.", 12, UiTheme.Dim));
                ui.OpenModal(b);
                return;
            }
            var target = BotSystem.SnapshotOf(bot);
            string targetName = target.CommanderName.Length > 0 ? target.CommanderName : "Unknown commander";
            var tile = new TileXY(target.HomeX, target.HomeY);

            var (blocker, content, footer) = Widgets.ModalPanelFooter("RAID TARGET", ui.CloseModal, 82f);

            // ---- target intel (compact) ----
            var galaxy = ctx.Bots!;
            var headRow = Widgets.HBox(Justify.SpaceBetween);
            headRow.Add(Widgets.Text(ClanSystem.Tagged(state, galaxy, bot.Id, targetName), 15, UiTheme.Text, bold: true));
            headRow.Add(Widgets.Text($"MIGHT {target.Might:N0}", 12, UiTheme.Energy, bold: true));
            content.Add(headRow);
            content.Add(Widgets.Text($"HQ {target.HomeX}, {target.HomeY}", 11, UiTheme.Accent));
            if (BotSystem.HoldsGrudge(bot, state.Tick))
            {
                var grudge = Widgets.IconText(Icon.Warning,
                    "Out for revenge — they'll counter-raid once they can win", 11, UiTheme.Bad);
                grudge.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                grudge.style.marginTop = 4;
                content.Add(grudge);
            }

            bool hasIntel = MarchSystem.HasSpyIntel(state, tile);
            if (hasIntel)
            {
                int garrison = MarchSystem.FleetCount(target.Ships);
                var g = Widgets.Text(
                    garrison > 0 ? $"Garrison: ~{garrison} ships docked" : "Garrison: no ships docked",
                    11, garrison > 0 ? UiTheme.Bad : UiTheme.Good);
                g.style.marginTop = 4;
                content.Add(g);
                content.Add(Widgets.Text(
                    $"Unshielded loot: ~{UiTheme.FmtAmount(target.LootableMilli.Total)}", 11, UiTheme.Dim));
                int battery = ResearchSystem.BatteryLevel(bot.State);
                if (battery > 0)
                {
                    var guns = Widgets.IconText(Icon.Target,
                        $"Orbital Batteries Lv {battery} — they fire on raiders every round", 11, UiTheme.Bad);
                    guns.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                    content.Add(guns);
                }
            }
            else
            {
                var unknown = Widgets.Text("Defenses unknown — spy first to reveal garrison & loot.", 11, UiTheme.Energy);
                unknown.style.whiteSpace = WhiteSpace.Normal;
                unknown.style.marginTop = 4;
                content.Add(unknown);
            }

            // ---- intel actions ----
            var intelRow = Widgets.HBox(Justify.SpaceBetween);
            intelRow.style.marginTop = 8;
            var profileBtn = Widgets.TextButton("PROFILE", () =>
            {
                ui.CloseModal();
                PlayerProfilePanel.Open(ctx, targetBotId, targetName);
            }, 10);
            profileBtn.style.width = Length.Percent(48f);
            intelRow.Add(profileBtn);
            var spyBtn = Widgets.TextButton("SEND SPY PROBE", () =>
            {
                var (ok, message) = RaidService.SpyBot(ctx, target);
                ui.Toast(message);
                if (ok) ui.CloseModal();
            }, 10);
            spyBtn.style.width = Length.Percent(48f);
            intelRow.Add(spyBtn);
            content.Add(intelRow);

            if (ClanSystem.SameClanAsPlayer(state, bot))
            {
                var mate = Widgets.IconText(Icon.Pact, "Your clanmate — clanmates never raid each other.", 12, UiTheme.Good, bold: true);
                mate.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                mate.style.marginTop = 12;
                content.Add(mate);
                ui.OpenModal(blocker);
                return;
            }

            // Their clan answers the call: clanmates in range reinforce the garrison.
            var theirHelpers = ClanSystem.DefenseHelpers(state, galaxy, bot.Id, 0);
            if (galaxy.FindClan(bot.ClanId) is { } theirClan)
            {
                int helperShips = 0;
                foreach (var (_, sent) in theirHelpers) helperShips += MarchSystem.FleetCount(sent);
                bool enemy = ClanSystem.AtWarWith(galaxy, state.ClanId, theirClan.Id);
                var clanLine = Widgets.IconText(Icon.Shield, theirHelpers.Count > 0
                        ? $"{ClanSystem.Label(theirClan)}: {theirHelpers.Count} clanmate{(theirHelpers.Count == 1 ? "" : "s")} in range will send ~{helperShips} warships to defend"
                        : $"{ClanSystem.Label(theirClan)}: no clanmates close enough to help",
                    11, theirHelpers.Count > 0 ? UiTheme.Bad : UiTheme.Dim);
                clanLine.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                clanLine.style.marginTop = 6;
                content.Add(clanLine);
                if (enemy)
                {
                    var war = Widgets.IconText(Icon.Swords, "At war with your clan — a win scores for your side", 11, UiTheme.Energy, bold: true);
                    war.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                    content.Add(war);
                }
            }

            if (RaidService.IsShielded(target.Might))
            {
                var shield = Widgets.Text(
                    $"Protected by the new-commander shield (under {RaidService.NewPlayerShieldMight:N0} might).",
                    12, UiTheme.Energy);
                shield.style.whiteSpace = WhiteSpace.Normal;
                shield.style.marginTop = 12;
                content.Add(shield);
                ui.OpenModal(blocker);
                return;
            }

            // Clanmates who'd land with your fleet (the JOINT STRIKE toggle below) —
            // who can make it depends on your fleet's speed, so Refresh recomputes
            // them. Declared before the sliders: their callbacks run Refresh.
            bool joint = false;
            var wings = new List<StrikeSystem.Wing>();
            Button? jointBtn = null;
            Label? jointNote = null;

            // ---- fleet selection (per-hull sliders; probes stay home) ----
            var fleetHeader = Widgets.Text("SELECT YOUR RAIDING FLEET", 10, UiTheme.Dim, bold: true);
            fleetHeader.style.marginTop = 12;
            content.Add(fleetHeader);

            var preview = Widgets.Text("", 11, UiTheme.Accent);
            preview.style.whiteSpace = WhiteSpace.Normal;
            var status = Widgets.Text("", 11, UiTheme.Bad);
            var forecast = new ForecastView();
            Button? launchBtn = null;

            var picks = new Dictionary<HullId, SliderInt>();
            var refreshers = new List<Action>();
            string lastClass = "";
            foreach (var hull in Ships.All)
            {
                var h = hull;
                if (h == HullId.Probe) continue; // recon can't raid
                var def = Ships.Defs[h];
                int docked = state.Ships.TryGetValue(h, out var d) ? d : 0;
                if (docked <= 0) continue;

                if (def.Class != lastClass)
                {
                    lastClass = def.Class;
                    var classHeader = Widgets.Text(def.Class.ToUpper(), 10, UiTheme.Accent, bold: true);
                    classHeader.style.marginTop = 8;
                    content.Add(classHeader);
                }

                var row = Widgets.Row();
                row.style.marginTop = 6;
                var rowHead = Widgets.HBox(Justify.SpaceBetween);
                rowHead.Add(Widgets.Text(def.Name, 12, UiTheme.Text, bold: true));
                var countLabel = Widgets.Text("0", 13, UiTheme.Accent, bold: true);
                rowHead.Add(countLabel);
                row.Add(rowHead);
                row.Add(Widgets.Text($"docked {docked} · atk {def.Atk} · shd {def.Shield} · cargo {UiTheme.FmtCount(def.Cargo / 1000)}", 9, UiTheme.Dim));

                var slider = new SliderInt(0, Math.Max(0, docked)) { value = 0 };
                picks[h] = slider;
                row.Add(slider);
                slider.RegisterValueChangedCallback(_ => { countLabel.text = slider.value.ToString(); Refresh(); });
                refreshers.Add(() => countLabel.text = slider.value.ToString());
                content.Add(row);
            }

            if (picks.Count == 0)
            {
                var none = Widgets.Text("No combat ships docked — build some in the Shipyard first.", 12, UiTheme.Bad);
                none.style.whiteSpace = WhiteSpace.Normal;
                none.style.marginTop = 6;
                content.Add(none);
                ui.OpenModal(blocker);
                return;
            }

            var quick = Widgets.HBox(Justify.SpaceAround);
            quick.style.marginTop = 6;
            var allBtn = Widgets.TextButton("ALL DOCKED", () =>
            {
                foreach (var kv in picks)
                {
                    int docked = state.Ships.TryGetValue(kv.Key, out var d) ? d : 0;
                    kv.Value.SetValueWithoutNotify(docked);
                }
                Refresh();
            }, 10);
            allBtn.style.width = Length.Percent(47f);
            var clearBtn = Widgets.TextButton("CLEAR", () =>
            {
                foreach (var s in picks.Values) s.SetValueWithoutNotify(0);
                Refresh();
            }, 10);
            clearBtn.style.width = Length.Percent(47f);
            quick.Add(allBtn);
            quick.Add(clearBtn);
            content.Add(quick);

            // ---- joint strike ----
            if (state.ClanId != 0)
            {
                jointBtn = Widgets.IconButton(Icon.Pact, "JOINT STRIKE", () =>
                {
                    joint = !joint;
                    Refresh();
                }, 11);
                jointBtn.style.marginTop = 10;
                content.Add(jointBtn);
                jointNote = Widgets.Text("", 10, UiTheme.Dim);
                jointNote.style.whiteSpace = WhiteSpace.Normal;
                jointNote.style.marginTop = 4;
                content.Add(jointNote);
            }

            void RefreshJoint(int arriveTick)
            {
                if (jointBtn == null || jointNote == null) return;
                wings = arriveTick > 0 ? StrikeSystem.StrikeWings(state, galaxy, tile, arriveTick, targetBotId) : new List<StrikeSystem.Wing>();
                int wingShips = 0;
                foreach (var w in wings) wingShips += MarchSystem.FleetCount(w.Ships);
                bool on = joint && wings.Count > 0;
                Widgets.SetCaption(jointBtn, arriveTick <= 0 ? "JOINT STRIKE — PICK YOUR FLEET FIRST"
                    : wings.Count == 0 ? "JOINT STRIKE — NO CLANMATES CAN MAKE IT"
                    : on ? $"JOINT STRIKE ON: {wings.Count} CLANMATE{(wings.Count == 1 ? "" : "S")} · +{wingShips} WARSHIPS"
                    : $"JOINT STRIKE ({wings.Count} ready · +{wingShips} warships)");
                Widgets.SetButtonHighlight(jointBtn, on);
                Widgets.SetButtonEnabled(jointBtn, wings.Count > 0);
                jointNote.text = wings.Count > 0
                    ? $"{string.Join(", ", wings.ConvertAll(w => w.Bot.Name))} would fly from their own colonies and land with your fleet, each bringing a quarter of their warships. They keep a share of the plunder."
                    : $"Clanmates join when they can land with your fleet: within {StrikeSystem.StrikeRange:N0} tiles of the target, rested, with warships docked.";
            }

            content.Add(preview);
            content.Add(status);
            content.Add(forecast.Root);
            if (CaptainToggle.Build(state) is { } lead) content.Add(lead); // the Academy (2026-09-30)

            Dictionary<HullId, int> Fleet()
            {
                var fleet = new Dictionary<HullId, int>();
                foreach (var kv in picks)
                    if (kv.Value.value > 0) fleet[kv.Key] = kv.Value.value;
                return fleet;
            }

            void Refresh()
            {
                foreach (var rr in refreshers) rr();
                var fleet = Fleet();
                if (MarchSystem.FleetCount(fleet) < 1)
                {
                    preview.text = "Select ships to preview the raid.";
                    status.text = "";
                    forecast.Hide();
                    RefreshJoint(0);
                    if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, false);
                    return;
                }
                var p = MarchSystem.PreviewMarch(state, fleet, tile);
                RefreshJoint(p.Ok ? state.Tick + p.TravelSec : 0);
                if (hasIntel)
                {
                    // Same lines the arrival battle uses (StrikeSystem.ResolveRaid).
                    var attackers = new List<Dictionary<HullId, int>> { fleet };
                    if (joint) foreach (var w in wings) attackers.Add(w.Ships);
                    var defenders = new List<Dictionary<HullId, int>> { target.Ships };
                    foreach (var (_, sent) in theirHelpers) defenders.Add(sent);
                    forecast.Show(BattleForecast.Predict(ClanSystem.Combine(attackers), ClanSystem.Combine(defenders),
                            ResearchSystem.CombatMods(state), ResearchSystem.DefenseMods(bot.State)),
                        "Rivals keep building — their garrison can grow before you arrive.");
                }
                else
                    forecast.NeedsIntel("Send a spy probe first — the forecast needs their garrison.");
                if (p.Ok)
                {
                    status.text = "";
                    preview.text = $"Travel {UiTheme.FmtDuration(p.TravelSec)} each way · helium {UiTheme.FmtAmount(p.HeliumCost)} · cargo {UiTheme.FmtAmount(p.CargoCap)}";
                }
                else
                {
                    preview.text = "";
                    status.text = p.Reason ?? "Cannot launch";
                }
                if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, p.Ok);
            }

            launchBtn = Widgets.TextButton("LAUNCH RAID", () =>
            {
                var fleet = Fleet();
                var (ok, message) = RaidService.LaunchRaid(ctx, target, fleet, joint && wings.Count > 0);
                ui.Toast(message);
                if (ok) ui.CloseModal();
            }, 14);
            Widgets.Primary(launchBtn);
            launchBtn.style.height = 42;
            footer.Add(launchBtn);

            Refresh();
            // Live: clanmates come and go (a fleet docks, a wing rests) while the screen is open.
            if (state.ClanId != 0) blocker.schedule.Execute(Refresh).Every(1000);
            ui.OpenModal(blocker);
        }
    }
}
