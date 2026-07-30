// Map node UI — ports of v1's NodeCallout.ts + NodePanel.ts.
//
// NodeCallout: lightweight "what did I tap?" strip above the bottom nav with
// NO blocker — the map behind stays pannable while it's open. Camps get
// ATTACK + SPY; resource tiles GATHER; derelicts SALVAGE.
//
// NodeComposer: the modal fleet composer — per-hull sliders, live preview of
// travel / helium / cargo (with research bonuses already baked in), launch.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Map;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class NodeCallout
    {
        /// <summary>Build the callout strip. Hosted by UIController's non-modal callout layer.</summary>
        public static VisualElement Build(GameContext ctx, MapNode node, Action onClose)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            bool isCamp = node.Kind == NodeKind.Camp;
            bool isDerelict = node.Kind == NodeKind.Derelict;

            var panel = new VisualElement();
            panel.style.backgroundColor = new UnityEngine.Color(UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.96f);
            Widgets.SetBorder(panel, UiTheme.Stroke, 1f);
            panel.style.paddingLeft = 14;
            panel.style.paddingRight = 14;
            panel.style.paddingTop = 10;
            panel.style.paddingBottom = 10;
            panel.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());

            var head = Widgets.HBox(Justify.SpaceBetween);
            int displayLv = isCamp ? node.CampLevel : node.Tier + 1;
            string title = isCamp ? $"Pirate Camp  Lv{displayLv}" : $"{Nodes.Defs[node.Kind].Name}  ·  Lv.{displayLv}";
            head.Add(Widgets.Text(title, 15, isCamp ? UiTheme.Bad : UiTheme.Accent, bold: true));
            var close = Widgets.TextButton("×", onClose, 12);
            close.style.width = 32;
            head.Add(close);
            panel.Add(head);

            state.Map.NodeOverrides.TryGetValue(node.Id, out var ov);
            long remaining = ov?.Remaining ?? node.Amount;
            string detail;
            if (isCamp)
            {
                if (MarchSystem.HasSpyIntel(state, node.Tile))
                {
                    var g = MarchSystem.CampGarrison(node);
                    var parts = new List<string>();
                    foreach (var hull in Ships.All)
                        if (g.TryGetValue(hull, out var n) && n > 0) parts.Add($"{n}× {Ships.Defs[hull].Name}");
                    detail = $"garrison: {string.Join(", ", parts)}";
                }
                else detail = "garrison unknown — SPY to reveal (intel arrives in Mail)";
            }
            else if (isDerelict)
            {
                detail = $"salvage: {UiTheme.FmtAmount(remaining)} (50/30/20 split)";
            }
            else
            {
                string resName = node.Kind == NodeKind.DMField ? "dark matter" : node.Resource switch
                {
                    ResourceId.Gold => "gold",
                    ResourceId.Quartz => "quartz",
                    ResourceId.Helium => "helium",
                    _ => "",
                };
                detail = $"{UiTheme.FmtAmount(remaining)} {resName} left  ·  gather ~{UiTheme.FmtRatePerHour(node.RatePerSec * 3600L)}";
            }
            var detailLabel = Widgets.Text(detail, 11, UiTheme.Text);
            detailLabel.style.whiteSpace = WhiteSpace.Normal;
            detailLabel.style.marginTop = 4;
            panel.Add(detailLabel);

            double dist = TileXY.Distance(node.Tile, state.HomeTile);
            panel.Add(Widgets.Text($"{dist:F1} tiles from home", 10, UiTheme.Dim));

            var buttons = Widgets.HBox(Justify.SpaceAround);
            buttons.style.marginTop = 8;
            void Action(string label, Action act, float widthPct)
            {
                var b = Widgets.TextButton(label, act, 14);
                b.style.width = Length.Percent(widthPct);
                b.style.height = 40;
                buttons.Add(b);
            }

            void FavoriteAction(float widthPct) =>
                Action("★", () => ui.Toast(
                    Favorites.Add($"{Nodes.Defs[node.Kind].Name} Lv{displayLv}", node.Tile)
                        ? "Location bookmarked" : "Favorites list is full"), widthPct);

            if (isCamp)
            {
                Action("ATTACK", () => NodeComposer.Open(ctx, node), 34f);
                Action("SPY", () =>
                {
                    var probe = new Dictionary<HullId, int> { [HullId.Probe] = 1 };
                    var res = MarchSystem.SendMarch(state, probe, node.Tile, MarchMission.Spy, out _);
                    if (res.Ok)
                    {
                        ui.Toast("Spy probe en route — intel to your Mailbox");
                        onClose();
                    }
                    else ui.Toast(res.Reason ?? "Cannot spy");
                }, 34f);
                FavoriteAction(20f);
            }
            else
            {
                Action(isDerelict ? "SALVAGE" : "GATHER", () => NodeComposer.Open(ctx, node), 70f);
                FavoriteAction(22f);
            }
            panel.Add(buttons);
            return panel;
        }
    }

    public static class NodeComposer
    {
        public static void Open(GameContext ctx, MapNode node)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            bool isCamp = node.Kind == NodeKind.Camp;
            bool isDerelict = node.Kind == NodeKind.Derelict;

            int displayLv = isCamp ? node.CampLevel : node.Tier + 1;
            string title = isCamp ? $"PIRATE CAMP Lv{displayLv}" : Nodes.Defs[node.Kind].Name.ToUpper();
            var (blocker, content) = Widgets.ModalPanel(title, () =>
            {
                ui.CloseModal();
                ui.ReopenNodeCallout(node);
            }, 76f);

            // Info block.
            state.Map.NodeOverrides.TryGetValue(node.Id, out var ov);
            long remaining = ov?.Remaining ?? node.Amount;
            string infoLine;
            if (isCamp)
            {
                if (MarchSystem.HasSpyIntel(state, node.Tile))
                {
                    var g = MarchSystem.CampGarrison(node);
                    var parts = new List<string>();
                    foreach (var hull in Ships.All)
                        if (g.TryGetValue(hull, out var n) && n > 0) parts.Add($"{n}× {Ships.Defs[hull].Name}");
                    infoLine = $"garrison: {string.Join(", ", parts)}";
                }
                else infoLine = "garrison unknown — send a Spy Probe to reveal it";
            }
            else
            {
                string rate = node.RatePerSec > 0 ? $" · gather ~{UiTheme.FmtRatePerHour(node.RatePerSec * 3600L)}" : "";
                infoLine = $"{UiTheme.FmtAmount(remaining)} {(node.Resource?.ToString().ToLower() ?? "salvage")}{rate}";
            }
            var info = Widgets.Text(
                $"{infoLine}\n{TileXY.Distance(node.Tile, state.HomeTile):F1} tiles from home", 11, UiTheme.Dim);
            info.style.whiteSpace = WhiteSpace.Normal;
            content.Add(info);

            // Research bonus summary (already baked into the preview numbers).
            var bonuses = new List<string>();
            float spd = ResearchSystem.MarchSpeedMult(state) - 1f;
            float cargo = ResearchSystem.CargoMult(state) - 1f;
            float heliumCut = 1f - ResearchSystem.HeliumMult(state);
            if (spd > 0.001f) bonuses.Add($"+{UnityEngine.Mathf.RoundToInt(spd * 100)}% speed");
            if (cargo > 0.001f) bonuses.Add($"+{UnityEngine.Mathf.RoundToInt(cargo * 100)}% cargo");
            if (heliumCut > 0.001f) bonuses.Add($"−{UnityEngine.Mathf.RoundToInt(heliumCut * 100)}% helium");
            if (isCamp)
            {
                float atk = ResearchSystem.AtkMult(state) - 1f;
                float hp = ResearchSystem.HpMult(state) - 1f;
                if (atk > 0.001f) bonuses.Add($"+{UnityEngine.Mathf.RoundToInt(atk * 100)}% atk");
                if (hp > 0.001f) bonuses.Add($"+{UnityEngine.Mathf.RoundToInt(hp * 100)}% hp");
            }
            if (bonuses.Count > 0)
                content.Add(Widgets.Text($"research {string.Join(" · ", bonuses)}", 10, UiTheme.Good));

            // Declared ahead of the slider closures that capture them (definite assignment).
            var preview = Widgets.Text("", 11, UiTheme.Accent);
            preview.style.whiteSpace = WhiteSpace.Normal;
            preview.style.marginTop = 8;
            var status = Widgets.Text("", 11, UiTheme.Bad);
            Button? launchBtn = null;

            // Per-hull sliders — only hulls actually docked (you can't send what you
            // don't own), grouped by tier with a header when the class changes.
            var picks = new Dictionary<HullId, SliderInt>();
            var refreshers = new List<Action>();
            string lastClass = "";
            foreach (var hull in Ships.All)
            {
                var h = hull;
                var def = Ships.Defs[h];
                int docked = state.Ships.TryGetValue(h, out var d) ? d : 0;
                if (docked <= 0) continue;

                if (def.Class != lastClass)
                {
                    lastClass = def.Class;
                    var header = Widgets.Text(def.Class.ToUpper(), 10, UiTheme.Accent, bold: true);
                    header.style.marginTop = 8;
                    content.Add(header);
                }

                var row = Widgets.Row();
                row.style.marginTop = 6;
                var head = Widgets.HBox(Justify.SpaceBetween);
                head.Add(Widgets.Text(def.Name, 12, UiTheme.Text, bold: true));
                var countLabel = Widgets.Text("0", 13, UiTheme.Accent, bold: true);
                head.Add(countLabel);
                row.Add(head);
                row.Add(Widgets.Text(
                    $"docked {docked} · atk {def.Atk} · shd {def.Shield} · cargo {def.Cargo}", 9, UiTheme.Dim));

                var slider = new SliderInt(0, Math.Max(0, docked)) { value = 0 };
                slider.SetEnabled(docked > 0);
                picks[h] = slider;
                row.Add(slider);
                slider.RegisterValueChangedCallback(_ => { countLabel.text = slider.value.ToString(); Refresh(); });
                refreshers.Add(() => countLabel.text = slider.value.ToString());
                content.Add(row);
            }

            var quick = Widgets.HBox(Justify.SpaceAround);
            quick.style.marginTop = 4;
            var maxBtn = Widgets.TextButton("ALL DOCKED", () =>
            {
                foreach (var kv in picks)
                {
                    int docked = state.Ships.TryGetValue(kv.Key, out var d) ? d : 0;
                    kv.Value.SetValueWithoutNotify(docked);
                }
                Refresh();
            }, 10);
            maxBtn.style.width = Length.Percent(47f);
            var clearBtn = Widgets.TextButton("CLEAR", () =>
            {
                foreach (var s in picks.Values) s.SetValueWithoutNotify(0);
                Refresh();
            }, 10);
            clearBtn.style.width = Length.Percent(47f);
            quick.Add(maxBtn);
            quick.Add(clearBtn);
            content.Add(quick);

            content.Add(preview);
            content.Add(status);

            var launchRow = Widgets.HBox(Justify.SpaceAround);
            launchRow.style.marginTop = 8;

            Dictionary<HullId, int> Fleet()
            {
                var fleet = new Dictionary<HullId, int>();
                foreach (var kv in picks)
                    if (kv.Value.value > 0) fleet[kv.Key] = kv.Value.value;
                return fleet;
            }

            void Refresh()
            {
                foreach (var r in refreshers) r();
                var fleet = Fleet();
                var p = MarchSystem.PreviewMarch(state, fleet, node.Tile);
                if (!p.Ok)
                {
                    preview.text = MarchSystem.FleetCount(fleet) < 1 ? "Select ships to preview the march." : "";
                    status.text = MarchSystem.FleetCount(fleet) < 1 ? "" : p.Reason ?? "";
                }
                else
                {
                    status.text = "";
                    preview.text = $"{UiTheme.FmtDuration(p.TravelSec)} travel · {p.HeliumCost / 1000.0:0.#} helium · cargo {UiTheme.FmtCount(p.CargoCap / 1000)}";
                }
                if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, p.Ok);
            }

            void Launch(MarchMission mission)
            {
                var fleet = Fleet();
                // Haulers aboard → offer the load-cargo follow-up screen first
                // (user spec 2026-07-05: evacuate/relocate resources with the fleet).
                if (mission != MarchMission.Spy
                    && fleet.TryGetValue(HullId.Hauler, out var haulers) && haulers > 0)
                {
                    var p = MarchSystem.PreviewMarch(state, fleet, node.Tile);
                    if (!p.Ok) { status.text = p.Reason ?? "Cannot launch"; return; }
                    CargoLoadPanel.Open(ctx, node, fleet, mission);
                    return;
                }
                var res = MarchSystem.SendMarch(state, fleet, node.Tile, mission, out _);
                if (!res.Ok) { status.text = res.Reason ?? "Cannot launch"; return; }
                ui.Toast(mission switch
                {
                    MarchMission.Attack => "Fleet launched — battle report on arrival",
                    MarchMission.Spy => "Spy probe en route — intel to your Mailbox",
                    _ => "Fleet launched — gathering on arrival",
                });
                ui.CloseModal();
                ui.CloseNodeCallout();
            }

            launchBtn = Widgets.TextButton(isCamp ? "ATTACK" : isDerelict ? "SALVAGE" : "GATHER",
                () => Launch(isCamp ? MarchMission.Attack : MarchMission.Gather), 14);
            launchBtn.style.width = Length.Percent(60f);
            launchBtn.style.height = 42;
            launchRow.Add(launchBtn);
            var spyBtn = Widgets.TextButton("SPY", () => Launch(MarchMission.Spy), 12);
            spyBtn.style.width = Length.Percent(34f);
            spyBtn.style.height = 42;
            launchRow.Add(spyBtn);
            content.Add(launchRow);

            Refresh();
            ui.OpenModal(blocker);
        }
    }

    /// <summary>
    /// Load-cargo-at-launch follow-up (user spec 2026-07-05): shown after the
    /// composer when the picked fleet includes haulers. Resources loaded here
    /// leave the stockpile and ride the march — home again on return/recall,
    /// gone if the fleet is wiped. Cargo space used here isn't available for
    /// gathering or loot at the destination.
    /// </summary>
    public static class CargoLoadPanel
    {
        public static void Open(GameContext ctx, MapNode node, Dictionary<HullId, int> fleet, MarchMission mission)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            long capMilli = MarchSystem.EffCargoCap(state, fleet);

            var (blocker, content) = Widgets.ModalPanel("LOAD CARGO", () =>
            {
                ui.CloseModal();
                NodeComposer.Open(ctx, node); // back to the fleet picker
            }, 0);

            var intro = Widgets.Text(
                "Load resources onto the fleet before launch. They fly home with the " +
                "fleet — and are lost with it if it's destroyed.", 10, UiTheme.Dim);
            intro.style.whiteSpace = WhiteSpace.Normal;
            content.Add(intro);

            var capLine = Widgets.Text("", 11, UiTheme.Accent);
            capLine.style.marginTop = 6;
            content.Add(capLine);
            var status = Widgets.Text("", 11, UiTheme.Bad);
            Button? loadBtn = null;

            var picks = new Dictionary<ResourceId, SliderInt>();
            foreach (var (res, label) in new[]
            {
                (ResourceId.Gold, "Gold"), (ResourceId.Quartz, "Quartz"), (ResourceId.Helium, "Helium"),
            })
            {
                var r = res;
                long stockWhole = state.Resources.Get(r) / 1000;
                int max = (int)Math.Min(stockWhole, capMilli / 1000);

                var row = Widgets.Row();
                row.style.marginTop = 6;
                var head = Widgets.HBox(Justify.SpaceBetween);
                head.Add(Widgets.Text(label, 12, UiTheme.Text, bold: true));
                var countLabel = Widgets.Text("0", 13, UiTheme.Accent, bold: true);
                head.Add(countLabel);
                row.Add(head);
                row.Add(Widgets.Text($"in stock {UiTheme.FmtCount(stockWhole)}", 9, UiTheme.Dim));
                var slider = new SliderInt(0, Math.Max(0, max)) { value = 0 };
                slider.SetEnabled(max > 0);
                picks[r] = slider;
                row.Add(slider);
                slider.RegisterValueChangedCallback(_ => { countLabel.text = UiTheme.FmtCount(slider.value); Refresh(); });
                content.Add(row);
            }

            ResourceBag Picked() => new ResourceBag(
                picks[ResourceId.Gold].value, picks[ResourceId.Quartz].value, picks[ResourceId.Helium].value).Milli();

            void Refresh()
            {
                long picked = Picked().Total;
                bool over = picked > capMilli;
                capLine.text = $"cargo {UiTheme.FmtCount(picked / 1000)} / {UiTheme.FmtCount(capMilli / 1000)}";
                status.text = over ? "Over cargo capacity — lighten the load" : "";
                if (loadBtn != null) Widgets.SetButtonEnabled(loadBtn, !over);
            }

            var quick = Widgets.HBox(Justify.SpaceAround);
            quick.style.marginTop = 4;
            var fillBtn = Widgets.TextButton("FILL MAX", () =>
            {
                // Greedy fill: gold → quartz → helium until the hold is full.
                long left = capMilli;
                foreach (var r in new[] { ResourceId.Gold, ResourceId.Quartz, ResourceId.Helium })
                {
                    int amount = (int)Math.Min(state.Resources.Get(r) / 1000, left / 1000);
                    picks[r].SetValueWithoutNotify(Math.Min(amount, picks[r].highValue));
                    left -= (long)picks[r].value * 1000;
                }
                Refresh();
            }, 10);
            fillBtn.style.width = Length.Percent(47f);
            var clearBtn = Widgets.TextButton("CLEAR", () =>
            {
                foreach (var s in picks.Values) s.SetValueWithoutNotify(0);
                Refresh();
            }, 10);
            clearBtn.style.width = Length.Percent(47f);
            quick.Add(fillBtn);
            quick.Add(clearBtn);
            content.Add(quick);
            content.Add(capLine);
            content.Add(status);

            void DoLaunch(bool withCargo)
            {
                var res = MarchSystem.SendMarch(state, fleet, node.Tile, mission, out int marchId);
                if (!res.Ok) { status.text = res.Reason ?? "Cannot launch"; return; }
                var load = Picked();
                if (withCargo && load.Total > 0)
                {
                    var loaded = MarchSystem.LoadCargo(state, marchId, load);
                    if (!loaded.Ok)
                        ui.Toast($"Launched without cargo — {loaded.Reason}");
                    else
                        ui.Toast($"Fleet away with {UiTheme.FmtCount(load.Total / 1000)} cargo aboard");
                }
                else ui.Toast(mission == MarchMission.Attack
                    ? "Fleet launched — battle report on arrival"
                    : "Fleet launched — gathering on arrival");
                ui.CloseModal();
                ui.CloseNodeCallout();
            }

            var buttons = Widgets.HBox(Justify.SpaceAround);
            buttons.style.marginTop = 10;
            var emptyBtn = Widgets.TextButton("LAUNCH EMPTY", () => DoLaunch(false), 11);
            emptyBtn.style.width = Length.Percent(42f);
            emptyBtn.style.height = 42;
            buttons.Add(emptyBtn);
            loadBtn = Widgets.TextButton("LOAD & LAUNCH", () => DoLaunch(true), 12);
            loadBtn.style.width = Length.Percent(52f);
            loadBtn.style.height = 42;
            buttons.Add(loadBtn);
            content.Add(buttons);

            Refresh();
            ui.OpenModal(blocker);
        }
    }
}
