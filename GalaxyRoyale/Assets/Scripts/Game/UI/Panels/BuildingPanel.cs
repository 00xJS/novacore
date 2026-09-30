// BuildingPanel — faithful UI Toolkit rebuild of v1's BuildingPanel.ts:
//   title "Name  Lv N" · description · effect line (now → next, green) ·
//   UPGRADE COST per-resource lines (resource-colored, red when short) ·
//   time · status reason (red) · UPGRADE, or progress bar + SPEED UP | CANCEL
//   while the order runs. Extra additions over v1: the ⓘ level chart, and
//   contextual helper buttons on the two common blockers (CC gate → jump to
//   the Command Center; busy queue → open Queues).
// `mineId` switches to extra-mine instance mode ("Gold Mine #2", *Mine fns).
//
// BuildMineChooser (bottom of file) is v1's BuildMineChooserPanel: pick which
// producer to place on a tapped expansion-plot ghost.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;
using Position = UnityEngine.UIElements.Position;

namespace GalaxyRoyale.Game.UI
{
    public static class BuildingPanel
    {
        public static void Open(GameContext ctx, BuildingId id, int? mineId = null, Action? onClosed = null)
        {
            var ui = UIController.Instance!;
            var state0 = ctx.State!;
            var def = Buildings.Defs[id];

            string title = def.Name;
            if (mineId is int mid0)
            {
                var mine0 = BuildingSystem.GetMine(state0, mid0);
                if (mine0 == null) { onClosed?.Invoke(); return; }
                // Stable instance number: singleton is #1, extras ordered by id.
                int n = 2;
                foreach (var m in state0.ExtraMines)
                    if (m.Type == mine0.Type && m.Id < mine0.Id) n++;
                title = $"{def.Name} #{n}";
            }

            bool chart = false;
            // The in-progress face's bar, ticked in place by Refresh (see below).
            VisualElement? liveFill = null;
            Label? liveLabel = null;

            void Close() { ui.CloseModal(); onClosed?.Invoke(); }
            var (blocker, content) = Widgets.ModalPanel(title, Close, 62f);

            var body = new VisualElement();
            content.Add(body);

            // ---------- accessors (singleton vs mine instance) ----------

            int Level(GameState s) => mineId is int m
                ? (BuildingSystem.GetMine(s, m)?.Level ?? -1)
                : s.Buildings[id].Level;

            int OrderIdx(GameState s) => BuildingSystem.FindOrderIndex(s, id, mineId);

            SimResult Check(GameState s) => mineId is int m
                ? BuildingSystem.CheckUpgradeMine(s, m)
                : BuildingSystem.CheckUpgrade(s, id);

            SimResult TryStart(GameState s) => mineId is int m
                ? BuildingSystem.StartUpgradeMine(s, m)
                : BuildingSystem.StartUpgrade(s, id);

            void Reopen() => Open(ctx, id, mineId, onClosed);

            // ---------- faces ----------

            void RenderChart(GameState s, int level)
            {
                var head = Widgets.HBox(Justify.SpaceBetween);
                head.style.marginBottom = 6;
                head.Add(Widgets.Text($"Levels 1–{def.MaxLevel}  (current L{level})", 12, UiTheme.Dim));
                head.Add(Widgets.IconButton(Icon.ChevronLeft, "BACK", () => { chart = false; }, 10));
                body.Add(head);

                // Fixed columns so every resource lines up vertically down the chart.
                Label Cell(string text, float pct, UnityEngine.Color color, bool bold = false,
                    TextAnchor anchor = TextAnchor.MiddleRight)
                {
                    var cell = Widgets.Text(text, 11, color, bold);
                    cell.style.width = Length.Percent(pct);
                    cell.style.unityTextAlign = anchor;
                    return cell;
                }

                var header = Widgets.HBox();
                header.style.paddingBottom = 3;
                header.style.borderBottomWidth = 1;
                header.style.borderBottomColor = UiTheme.Stroke;
                // Compacted so the right-most TIME column doesn't run off the panel
                // edge (user feedback). Total < 100% leaves a right margin.
                header.Add(Cell("LV", 7f, UiTheme.Dim, bold: true, TextAnchor.MiddleLeft));
                header.Add(Cell("GOLD", 18f, UiTheme.Gold, bold: true));
                header.Add(Cell("QUARTZ", 18f, UiTheme.Quartz, bold: true));
                header.Add(Cell("HELIUM", 18f, UiTheme.Helium, bold: true));
                header.Add(Cell("TIME", 27f, UiTheme.Dim, bold: true, TextAnchor.MiddleLeft));
                body.Add(header);

                for (int L = 1; L <= def.MaxLevel; L++)
                {
                    var cost = BuildingSystem.GetUpgradeCost(id, L);
                    int seconds = BuildingSystem.GetBuildTime(s, id, L);
                    var color = L == level ? UiTheme.Good : L < level ? UiTheme.Dim : UiTheme.Text;
                    var row = Widgets.HBox();
                    row.style.paddingTop = 3;
                    row.style.paddingBottom = 3;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = UiTheme.Stroke;
                    row.Add(Cell($"L{L}", 7f, color, bold: L == level, TextAnchor.MiddleLeft));
                    row.Add(Cell(UiTheme.FmtAmount(cost.Gold), 18f, color));
                    row.Add(Cell(UiTheme.FmtAmount(cost.Quartz), 18f, color));
                    row.Add(Cell(UiTheme.FmtAmount(cost.Helium), 18f, color));
                    row.Add(Cell(UiTheme.FmtDuration(seconds), 27f, color, anchor: TextAnchor.MiddleLeft));
                    body.Add(row);
                }
            }

            void RenderMain(GameState s, int level)
            {
                bool atMax = level >= def.MaxLevel;
                int toLevel = level + 1;

                // Header row: "Lv N" left, LEVELS chart button right (v1 folds Lv into
                // the title). Painted icon — the old "ⓘ" glyph rendered as a □ box.
                var head = Widgets.HBox(Justify.SpaceBetween);
                head.Add(Widgets.Text(level == 0 ? "Not yet built" : $"Lv {level}", 15, UiTheme.Text, bold: true));
                head.Add(Widgets.IconButton(Icon.Chart, "LEVELS", () => { chart = true; }, 10));
                body.Add(head);

                // Description (v1 def.desc, dim).
                var desc = Widgets.Text(def.Desc, 12, UiTheme.Dim);
                desc.style.whiteSpace = WhiteSpace.Normal;
                desc.style.marginTop = 6;
                body.Add(desc);

                // Effect line (v1 describeEffect, green — "MAX LEVEL" at cap).
                string effect = atMax ? "MAX LEVEL" : DescribeEffect(id, level);
                if (effect.Length > 0)
                {
                    var eff = Widgets.Text(effect, 13, UiTheme.Good);
                    eff.style.whiteSpace = WhiteSpace.Normal;
                    eff.style.marginTop = 10;
                    body.Add(eff);
                }

                // The functional buildings expose their function HERE, next to the
                // upgrade (user feedback: tapping the lab/yard lost their menus).
                if (id == BuildingId.ResearchLab && level >= 1 && mineId == null)
                {
                    int researched = 0, running = s.ResearchQueue.Count;
                    foreach (var kv in s.Research) if (kv.Value > 0) researched++;
                    var summary = Widgets.Text(
                        $"{researched} techs researched · {running} in progress", 11, UiTheme.Dim);
                    summary.style.marginTop = 10;
                    body.Add(summary);
                    var research = Widgets.TextButton("OPEN RESEARCH", () => ui.OpenResearch(), 12);
                    research.name = "tut-open-research";
                    research.style.marginTop = 6;
                    research.style.height = 38;
                    body.Add(research);
                }
                if (id == BuildingId.Shipyard && level >= 1 && mineId == null)
                {
                    var hangarHeader = Widgets.Text("HANGAR", 10, UiTheme.Dim, bold: true);
                    hangarHeader.style.marginTop = 10;
                    body.Add(hangarHeader);
                    // Only ships you actually HAVE (user feedback: don't list a wall
                    // of hulls you can't build yet). Grouped by class.
                    string lastClass = "";
                    bool anyDocked = false;
                    foreach (var hull in Ships.All)
                    {
                        int docked = s.Ships.TryGetValue(hull, out var n) ? n : 0;
                        if (docked <= 0) continue;
                        anyDocked = true;
                        var hdef = Ships.Defs[hull];
                        if (hdef.Class != lastClass)
                        {
                            lastClass = hdef.Class;
                            var cls = Widgets.Text(hdef.Class, 9, UiTheme.Accent, bold: true);
                            cls.style.marginTop = 6;
                            body.Add(cls);
                        }
                        var line = Widgets.HBox(Justify.SpaceBetween);
                        line.style.marginTop = 3;
                        line.Add(Widgets.Text(hdef.Name, 11, UiTheme.Text));
                        line.Add(Widgets.Text($"docked × {docked}", 11, UiTheme.Accent));
                        body.Add(line);
                    }
                    if (!anyDocked)
                    {
                        var empty = Widgets.Text("No ships docked yet — build some below.", 11, UiTheme.Dim);
                        empty.style.marginTop = 4;
                        body.Add(empty);
                    }
                    var fleet = Widgets.TextButton("BUILD SHIPS — FLEET COMMAND", () =>
                        ui.SwitchView(ViewId.Fleet), 12);
                    fleet.style.marginTop = 8;
                    fleet.style.height = 38;
                    body.Add(fleet);
                }

                if (id == BuildingId.SalvageYard && level >= 1 && mineId == null)
                {
                    var stored = s.SalvageStored;
                    long cap = SalvageSystem.CapacityMilli(s);
                    var header = Widgets.Heading("IN THE YARD", 10, UiTheme.Dim, 1.4f);
                    header.style.marginTop = 12;
                    body.Add(header);
                    void Line(string caption, long amount, UnityEngine.Color color)
                    {
                        var line = Widgets.HBox(Justify.SpaceBetween);
                        line.style.marginTop = 3;
                        line.Add(Widgets.Text(caption, 12, color));
                        line.Add(Widgets.Text($"{UiTheme.FmtAmount(amount)} / {UiTheme.FmtAmount(cap)}", 12,
                            amount >= cap ? UiTheme.Bad : UiTheme.Text));
                        body.Add(line);
                    }
                    Line("gold", stored.Gold, UiTheme.Gold);
                    Line("quartz", stored.Quartz, UiTheme.Quartz);
                    Line("helium", stored.Helium, UiTheme.Helium);
                    if (SalvageSystem.Full(s))
                    {
                        var full = Widgets.Text("The yard is full: collect it, or new salvage is lost.", 11, UiTheme.Bad);
                        full.style.whiteSpace = WhiteSpace.Normal;
                        full.style.marginTop = 4;
                        body.Add(full);
                    }
                    var collect = Widgets.Primary(Widgets.TextButton("COLLECT SALVAGE", () =>
                    {
                        if (SalvageSystem.Collect(ctx.State!, out var got).Ok)
                        {
                            GameAudio.Feedback(Sfx.Coins, Haptic.Success);
                            ui.Toast($"Salvage collected: +{UiTheme.FmtAmount(got.Total)}", Icon.Crate, UiTheme.Good);
                        }
                    }, 12));
                    Widgets.SetButtonEnabled(collect, stored.Total > 0);
                    collect.style.marginTop = 8;
                    collect.style.height = 38;
                    body.Add(collect);
                }

                if (id == BuildingId.DroneFactory && mineId == null)
                {
                    var header = Widgets.Heading("IN THE WILDS", 10, UiTheme.Dim, 1.4f);
                    header.style.marginTop = 12;
                    body.Add(header);
                    void Stat(string caption, string value)
                    {
                        var line = Widgets.HBox(Justify.SpaceBetween);
                        line.style.marginTop = 3;
                        line.Add(Widgets.Text(caption, 12, UiTheme.Dim));
                        line.Add(Widgets.Text(value, 12, UiTheme.Text, bold: true));
                        body.Add(line);
                    }
                    int deposits = WildsSystem.ActiveDeposits(s);
                    Stat("Harvester drones", WildsSystem.Drones(s).ToString());
                    Stat("Each carries", $"{WildsSystem.Carry(s):N0} a trip");
                    Stat("Deposits being worked", $"{deposits} of {WildsSystem.ChartedCount(s)} charted sectors");
                    Stat("Brought home an hour", deposits > 0 ? UiTheme.FmtAmount(WildsSystem.HaulPerHourMilli(s)) : "idle: chart a deposit");
                    Stat("Brought home so far", UiTheme.FmtAmount(s.Wilds.Harvested.Total));
                    var go = Widgets.IconButton(Icon.Compass, "GO TO THE WILDS", () =>
                    {
                        ui.CloseModal();
                        BaseGlobe.Instance?.FlyTo(BaseDistrict.Wilds);
                    }, 12);
                    go.style.marginTop = 8;
                    go.style.height = 38;
                    body.Add(go);
                }

                if (mineId == null) FrontierSection(ctx, s, id, level, body);

                int idx = OrderIdx(s);
                if (idx >= 0)
                {
                    RenderInProgress(s, idx);
                    return;
                }
                if (atMax) return;

                // UPGRADE COST — per-resource lines, resource-colored, red when short.
                var costHeader = Widgets.Text("UPGRADE COST", 10, UiTheme.Dim, bold: true);
                costHeader.style.marginTop = 14;
                body.Add(costHeader);

                var cost = BuildingSystem.GetUpgradeCost(id, toLevel);
                void CostLine(string caption, long amount, long have, UnityEngine.Color resColor)
                {
                    if (amount <= 0) return;
                    var line = Widgets.HBox(Justify.SpaceBetween);
                    line.style.marginTop = 4;
                    line.Add(Widgets.Text(caption, 13, have >= amount ? resColor : UiTheme.Bad));
                    line.Add(Widgets.Text(UiTheme.FmtAmount(amount), 13, have >= amount ? resColor : UiTheme.Bad));
                    body.Add(line);
                }
                CostLine("gold", cost.Gold, s.Resources.Gold, UiTheme.Gold);
                CostLine("quartz", cost.Quartz, s.Resources.Quartz, UiTheme.Quartz);
                CostLine("helium", cost.Helium, s.Resources.Helium, UiTheme.Helium);

                var timeLine = Widgets.HBox(Justify.SpaceBetween);
                timeLine.style.marginTop = 8;
                timeLine.Add(Widgets.Text("time", 13, UiTheme.Text));
                timeLine.Add(Widgets.Text(UiTheme.FmtDuration(BuildingSystem.GetBuildTime(s, id, toLevel)), 13, UiTheme.Text));
                body.Add(timeLine);

                // Status reason (v1 statusText) + contextual helper for the two
                // common blockers.
                var check = Check(s);
                if (!check.Ok)
                {
                    var status = Widgets.Text(check.Reason ?? "", 12, UiTheme.Bad);
                    status.style.whiteSpace = WhiteSpace.Normal;
                    status.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                    status.style.marginTop = 12;
                    body.Add(status);

                    if (check.Reason != null && check.Reason.Contains("Command Center"))
                    {
                        var go = Widgets.TextButton("GO TO COMMAND CENTER", () =>
                            Open(ctx, BuildingId.CommandCenter, null, onClosed), 11);
                        go.style.marginTop = 8;
                        body.Add(go);
                    }
                    else if (check.Reason == "Build queue is busy")
                    {
                        var open = Widgets.TextButton("OPEN QUEUES", () => ui.OpenQueues(), 11);
                        open.style.marginTop = 8;
                        body.Add(open);
                    }
                    else if (check.Reason == "Not enough resources")
                    {
                        var shop = Widgets.TextButton("GET RESOURCES IN SHOP", () => ui.OpenShop(), 11);
                        shop.style.marginTop = 8;
                        body.Add(shop);
                    }
                }

                var upgrade = Widgets.TextButton(level == 0 ? "BUILD" : "UPGRADE", () =>
                {
                    var res = TryStart(ctx.State!);
                    if (res.Ok)
                    {
                        ui.Toast($"{title} upgrade queued");
                        GameAudio.Feedback(Sfx.Confirm, Haptic.Light);
                        BuildingMarkers.RequestExtraMineSync();
                    }
                    else GameAudio.Feedback(Sfx.Error, Haptic.Error);
                });
                Widgets.SetButtonEnabled(upgrade, check.Ok);
                upgrade.name = mineId == null ? $"tut-upgrade-{id}" : "tut-upgrade-mine";
                upgrade.style.marginTop = 14;
                upgrade.style.height = 42;
                body.Add(upgrade);
            }

            void RenderInProgress(GameState s, int idx)
            {
                var order = s.BuildQueue[idx];

                if (order.EndsAtTick <= 0)
                {
                    var queued = Widgets.Text("Queued — waiting for a free build slot.", 12, UiTheme.Dim);
                    queued.style.marginTop = 14;
                    body.Add(queued);
                }
                else
                {
                    var (bar, fill, label) = Widgets.ProgressBar();
                    bar.style.marginTop = 16;
                    body.Add(bar);
                    // Kept so Refresh can tick the bar in place — the panel used to
                    // rebuild ALL its buttons every second just to move this bar.
                    liveFill = fill;
                    liveLabel = label;
                    UpdateLiveBar(s);
                }

                // v1: SPEED UP (left) | CANCEL (right), split row.
                var actions = Widgets.HBox(Justify.SpaceBetween);
                actions.style.marginTop = 12;
                var speed = Widgets.TextButton("SPEED UP", () =>
                    SpeedUpPanel.Open(ctx, title, "finish-build",
                        remainingSec: () =>
                        {
                            int i = OrderIdx(ctx.State!);
                            if (i < 0) return 0;
                            var o = ctx.State!.BuildQueue[i];
                            return o.EndsAtTick > 0 ? Math.Max(0, o.EndsAtTick - ctx.State!.Tick) : 0;
                        },
                        apply: cut =>
                        {
                            int i = OrderIdx(ctx.State!);
                            if (i >= 0)
                                BuildingSystem.SpeedUpBuildOrder(ctx.State!, i,
                                    cut == long.MaxValue ? int.MaxValue : (int)cut);
                        },
                        onBack: Reopen));
                speed.name = mineId == null ? $"tut-speedup-{id}" : "tut-speedup-mine";
                speed.style.width = Length.Percent(48f);
                actions.Add(speed);

                var cancel = Widgets.TextButton("CANCEL", () =>
                    ConfirmPanel.Open(
                        "Cancel this upgrade?\nResources will be refunded.",
                        "CANCEL UPGRADE",
                        () =>
                        {
                            int i = OrderIdx(ctx.State!);
                            if (i >= 0) BuildingSystem.CancelUpgrade(ctx.State!, i);
                            BuildingMarkers.RequestExtraMineSync();
                            Reopen();
                        },
                        Reopen));
                cancel.style.width = Length.Percent(48f);
                actions.Add(cancel);
                body.Add(actions);
            }

            // ---------- refresh loop ----------
            //
            // Rebuild only when something the panel SHOWS changes: level, queue
            // state, whether the upgrade is allowed, which cost lines are short,
            // and the lab/shipyard summaries. The old key included raw resources
            // + the tick, so every button was torn down and rebuilt each second
            // (a tap spanning a rebuild could be lost); the progress bar now ticks
            // in place instead.

            void UpdateLiveBar(GameState s)
            {
                if (liveFill == null || liveLabel == null) return;
                int i = OrderIdx(s);
                if (i < 0) return;
                var order = s.BuildQueue[i];
                if (order.EndsAtTick <= 0) return;
                long remain = Math.Max(0, order.EndsAtTick - s.Tick);
                int total = BuildingSystem.GetBuildTime(s, id, order.ToLevel);
                float pct = total > 0 ? 100f * (1f - remain / (float)total) : 0f;
                liveFill.style.width = Length.Percent(Math.Clamp(pct, 0f, 100f));
                liveLabel.text = UiTheme.FmtDuration(remain);
            }

            string cache = "";
            int lastTick = -1;
            void Refresh()
            {
                var s = ctx.State!;
                int level = Level(s);
                if (level < 0) { Close(); return; } // mine was removed (cancelled placement)
                int idx = OrderIdx(s);

                string key;
                if (chart) key = $"c|{level}"; // chart is static per level
                else
                {
                    var check = Check(s);
                    var cost = BuildingSystem.GetUpgradeCost(id, level + 1);
                    bool running = idx >= 0 && s.BuildQueue[idx].EndsAtTick > 0;
                    string extras = "";
                    if (id == BuildingId.ResearchLab)
                    {
                        int researched = 0;
                        foreach (var kv in s.Research) if (kv.Value > 0) researched++;
                        extras = $"{researched}:{s.ResearchQueue.Count}";
                    }
                    else if (id == BuildingId.Shipyard)
                    {
                        long sig = 0; int h = 1;
                        foreach (var hull in Ships.All)
                            sig += (h++) * (long)(s.Ships.TryGetValue(hull, out var n) ? n : 0);
                        extras = sig.ToString();
                    }
                    else if (id == BuildingId.SalvageYard)
                        extras = $"{s.SalvageStored.Gold}:{s.SalvageStored.Quartz}:{s.SalvageStored.Helium}";
                    else if (id == BuildingId.RepairDock)
                        extras = $"{s.DamagedHulls.Count}:{(s.Repair != null ? s.Repair.EndsAtTick - s.Tick : -1)}:{RepairSystem.NextScrapTick(s) / 60}";
                    else if (id == BuildingId.JumpGate)
                        extras = $"{JumpGateSystem.ReloadLeft(s)}:{s.Marches.Count}";
                    else if (id == BuildingId.Observatory && ctx.Bots != null)
                        extras = $"{(ctx.Bots.Boss.NextVisitTick - s.Tick) / 60}:{ctx.Bots.Boss.Active}";
                    // The Citadel (2026-09-30).
                    else if (id == BuildingId.RelicVault)
                    {
                        int relics = 0;
                        foreach (var n in s.Relics.Values) relics += n;
                        extras = relics.ToString();
                    }
                    else if (id == BuildingId.Academy)
                        extras = $"{s.CaptainMarchId}:{AcademySystem.Wounded(s)}:{s.Commander.Level}";
                    else if (id == BuildingId.MissileSilo && ctx.Bots != null)
                        extras = $"{SiloSystem.ReloadLeft(s) / 60}:{SiloSystem.Targets(s, ctx.Bots).Count}:{s.Tick / 30}";
                    else if (id == BuildingId.TradeConsulate)
                        extras = $"{ConsulateSystem.Window(s.Tick)}:{s.ContractsTaken.Count}:{s.Marches.Count}:" +
                                 $"{s.Ships.GetValueOrDefault(HullId.Hauler)}";
                    else if (id == BuildingId.Terraformer)
                        extras = $"{s.Terraform.Path}:{s.Terraform.Stage}:{(s.Terraform.ProjectEndsTick - s.Tick) / 60}";
                    key = $"{level}|{idx}|{running}|{check.Ok}|{check.Reason}|" +
                          $"{s.Resources.Gold >= cost.Gold}{s.Resources.Quartz >= cost.Quartz}{s.Resources.Helium >= cost.Helium}|{extras}";
                }

                if (key != cache)
                {
                    cache = key;
                    liveFill = null;
                    liveLabel = null;
                    body.Clear();
                    if (chart) RenderChart(s, level);
                    else RenderMain(s, level);
                }
                else if (s.Tick != lastTick) UpdateLiveBar(s);
                lastTick = s.Tick;
            }

            ui.OpenModal(blocker, Refresh);
        }

        static string Pct(double share) => $"{Math.Round(share * 1000) / 10:0.#}%";

        /// <summary>The Frontier's four late buildings (2026-09-29): what they're doing now.</summary>
        static void FrontierSection(GameContext ctx, GameState s, BuildingId id, int level, VisualElement body)
        {
            if (level < 1) return;
            var ui = UIController.Instance!;
            void Head(string text)
            {
                var h = Widgets.Heading(text, 10, UiTheme.Dim, 1.4f);
                h.style.marginTop = 12;
                body.Add(h);
            }
            void Stat(string caption, string value, UnityEngine.Color? tint = null)
            {
                var line = Widgets.HBox(Justify.SpaceBetween);
                line.style.marginTop = 3;
                line.Add(Widgets.Text(caption, 12, UiTheme.Dim));
                var v = Widgets.Text(value, 12, tint ?? UiTheme.Text, bold: true);
                v.style.whiteSpace = WhiteSpace.Normal;
                v.style.flexShrink = 1;
                v.style.unityTextAlign = UnityEngine.TextAnchor.MiddleRight;
                line.Add(v);
                body.Add(line);
            }
            Button Action(Button b)
            {
                b.style.marginTop = 8;
                b.style.height = 38;
                body.Add(b);
                return b;
            }
            string Hulls(Dictionary<HullId, int> ships)
            {
                var parts = new List<string>();
                foreach (var hull in Ships.All)
                    if (ships.TryGetValue(hull, out var n) && n > 0) parts.Add($"{Ships.Defs[hull].Name} ×{n:N0}");
                return parts.Count > 0 ? string.Join(" · ", parts) : "none";
            }

            switch (id)
            {
                case BuildingId.RepairDock:
                {
                    Head("IN THE DOCK");
                    Stat("Tows home", $"{Math.Round(RepairSystem.TowShare(level) * 100)}% of ships lost defending home");
                    if (s.Repair is { } job)
                    {
                        Stat("Repairing", Hulls(job.Ships), UiTheme.Quartz);
                        Stat("Back in service in", UiTheme.FmtDuration(Math.Max(0, job.EndsAtTick - s.Tick)));
                        return;
                    }
                    var waiting = RepairSystem.Waiting(s);
                    if (waiting.Count == 0)
                    {
                        Stat("Damaged hulls", "none — ships lost defending your colony will appear here");
                        return;
                    }
                    Stat("Damaged hulls", Hulls(waiting), UiTheme.Energy);
                    Stat("Scrapped in", UiTheme.FmtDuration(Math.Max(0, RepairSystem.NextScrapTick(s) - s.Tick)), UiTheme.Bad);
                    var cost = RepairSystem.Cost(waiting);
                    Stat("Repair costs", $"{UiTheme.FmtAmount(cost.Gold)} gold · {UiTheme.FmtAmount(cost.Quartz)} quartz · " +
                        $"{UiTheme.FmtAmount(cost.Helium)} helium · {UiTheme.FmtDuration(RepairSystem.Seconds(waiting))}");
                    var repair = Action(Widgets.Primary(Widgets.TextButton("REPAIR ALL", () =>
                    {
                        var res = RepairSystem.StartRepair(ctx.State!);
                        if (res.Ok)
                        {
                            GameAudio.Feedback(Sfx.Confirm, Haptic.Success);
                            ui.Toast("Repairs under way", Icon.Shield, UiTheme.Good);
                            LocalBootstrap.RequestSync();
                        }
                        else ui.Toast(res.Reason ?? "Can't repair right now", Icon.Warning, UiTheme.Bad);
                    }, 12)));
                    Widgets.SetButtonEnabled(repair, ResourceSystem.CanAfford(s, cost));
                    return;
                }
                case BuildingId.JumpGate:
                {
                    Head("THE GATE");
                    Stat("Fleets fly", $"+{Math.Min(level, 30)}% faster, on {Math.Min(level, 30)}% less helium");
                    bool precise = JumpGateSystem.Precise(s);
                    Stat("Free jump", precise ? "a Precision Warp — anywhere you pick"
                        : $"a Blind Jump — anywhere you pick from level {JumpGateSystem.PrecisionLevel}");
                    int left = JumpGateSystem.ReloadLeft(s);
                    Stat("Charge", left > 0 ? $"reloading · {UiTheme.FmtDuration(left)}" : "READY", left > 0 ? UiTheme.Dim : UiTheme.Good);
                    if (left > 0) return;
                    if (s.Marches.Count > 0)
                    {
                        Stat("", "Recall all fleets before jumping", UiTheme.Bad);
                        return;
                    }
                    Action(Widgets.Primary(Widgets.IconButton(Icon.Orbit, precise ? "JUMP — PICK A SPOT" : "BLIND JUMP", () =>
                    {
                        if (precise)
                        {
                            ui.CloseModal();
                            ui.SwitchView(ViewId.Map);
                            ctx.GetComponent<MapView>()?.BeginRelocation(viaJumpGate: true);
                            return;
                        }
                        ConfirmPanel.Open("Jump your colony to a random empty spot in the galaxy?\nThe gate recharges in 24 hours.",
                            "JUMP", () =>
                            {
                                var res = JumpGateSystem.JumpRandom(ctx.State!);
                                if (res.Ok)
                                {
                                    GameAudio.Feedback(Sfx.Launch, Haptic.Heavy);
                                    ui.Toast($"Jumped to {ctx.State!.HomeTile.X}, {ctx.State.HomeTile.Y}", Icon.Orbit, UiTheme.Good);
                                    LocalBootstrap.RequestSync();
                                }
                                else ui.Toast(res.Reason ?? "The jump failed", Icon.Warning, UiTheme.Bad);
                                ui.CloseModal();
                            }, () => ui.CloseModal());
                    }, 12)));
                    return;
                }
                case BuildingId.ClanEmbassy:
                {
                    Head("YOUR CLAN");
                    Stat("Wings in your joint strikes", $"up to {EmbassySystem.StrikeWings(s)}");
                    Stat("Supply runs", $"{EmbassySystem.SupplyRunsPerDay(s)} a day");
                    Stat("Clan tribute from the core", Pct(EmbassySystem.ClanTributeShare(s)) + " of your hourly production");
                    if (s.ClanId == 0) Stat("", "Join a clan to put these to work", UiTheme.Energy);
                    return;
                }
                case BuildingId.RelicVault:
                {
                    Head("ON DISPLAY");
                    Stat("Copies shown of each kind", $"up to {level}");
                    foreach (var def in Relics.All)
                    {
                        int have = RelicSystem.Count(s, def.Kind), shown = RelicSystem.OnDisplay(s, def.Kind);
                        Stat($"{def.Name} ×{have}", shown > 0 ? $"+{Math.Round(def.PerCopy * shown * 100)}% {def.Desc}" : "none yet",
                            shown > 0 ? UiTheme.Good : UiTheme.Dim);
                    }
                    Stat("", "Relics come from relic finds in the Wilds.", UiTheme.Dim);
                    return;
                }
                case BuildingId.Academy:
                {
                    Head("YOUR COMMANDER");
                    Stat("Commander XP", $"+{Math.Round(AcademySystem.XpBonus(s) * 100)}%");
                    Stat("Skill resets", $"{Math.Round(AcademySystem.RespecMult(s) * 100)}% of the price");
                    Stat("Leading a fleet", $"+{Math.Round(AcademySystem.CaptainBonus(s) * 100)}% attack and durability");
                    string status = s.CaptainMarchId != 0 ? "out leading a fleet"
                        : AcademySystem.Wounded(s) ? $"recovering · {UiTheme.FmtDuration(s.CaptainWoundedUntilTick - s.Tick)}"
                        : "ready — tick COMMANDER LEADS when you launch a fleet";
                    Stat("Status", status, s.CaptainMarchId != 0 ? UiTheme.Energy : AcademySystem.Wounded(s) ? UiTheme.Bad : UiTheme.Good);
                    return;
                }
                case BuildingId.MissileSilo:
                {
                    Head("THE SILO");
                    Stat("A salvo destroys", $"{Math.Round(SiloSystem.Share(level) * 100)}% of a raiding fleet");
                    Stat("Reload", UiTheme.FmtDuration(SiloSystem.ReloadSec(level)));
                    int left = SiloSystem.ReloadLeft(s);
                    Stat("Status", left > 0 ? $"reloading · {UiTheme.FmtDuration(left)}" : "LOADED", left > 0 ? UiTheme.Dim : UiTheme.Good);
                    if (ctx.Bots == null) return;
                    var targets = SiloSystem.Targets(s, ctx.Bots);
                    if (targets.Count == 0)
                    {
                        Stat("Raids on radar", "none — when your radar picks one up, fire from here", UiTheme.Dim);
                        return;
                    }
                    foreach (var atk in targets)
                    {
                        int n = 0;
                        foreach (var v in atk.Ships.Values) n += v;
                        string who = ctx.Bots.Find(atk.BotId)?.Name ?? "Raiders";
                        var fire = Action(Widgets.Primary(Widgets.IconButton(Icon.Target,
                            $"FIRE AT {who.ToUpperInvariant()} · {n:N0} SHIPS · {UiTheme.FmtDuration(atk.ArrivesAtTick - s.Tick)}", () =>
                        {
                            var res = SiloSystem.Fire(ctx.State!, ctx.Bots!, atk.Id, ctx.Events, out int killed);
                            if (res.Ok) { GameAudio.Feedback(Sfx.Launch, Haptic.Heavy); LocalBootstrap.RequestSync(); }
                            else ui.Toast(res.Reason ?? "Can't fire", Icon.Warning, UiTheme.Bad);
                        }, 11)));
                        Widgets.SetButtonEnabled(fire, left == 0);
                    }
                    return;
                }
                case BuildingId.TradeConsulate:
                {
                    Head("CONTRACTS");
                    Stat("Clients pay", $"{ConsulateSystem.Rate(level):0.00}× what you bring");
                    Stat("New board in", UiTheme.FmtDuration(ConsulateSystem.BoardLeftSec(s)));
                    foreach (var m in s.Marches)
                        if (m.Mission == MarchMission.Trade)
                            Stat($"Delivery to {m.ContractClient}", m.Phase == MarchPhase.Returning ? "paid, flying home"
                                : $"arrives in {UiTheme.FmtDuration(Math.Max(0, m.ArrivesAtTick - s.Tick))}", UiTheme.Energy);
                    if (ctx.Bots == null) return;
                    var board = ConsulateSystem.Board(s, ctx.Bots);
                    if (board.Count == 0) { Stat("", "No clients within reach", UiTheme.Dim); return; }
                    string R(ResourceId r) => r switch { ResourceId.Gold => "gold", ResourceId.Quartz => "quartz", _ => "helium" };
                    foreach (var c in board)
                    {
                        var card = Widgets.Row();
                        card.style.marginTop = 8;
                        card.Add(Widgets.Text(c.ClientName, 12, UiTheme.Text, bold: true));
                        var line = Widgets.Text($"Wants {UiTheme.FmtAmount(c.GiveMilli)} {R(c.Give)} · pays " +
                            $"{UiTheme.FmtAmount(c.GetMilli)} {R(c.Get)} + {c.DarkMatter} DM · " +
                            $"{ConsulateSystem.HaulersNeeded(s, c)} Haulers", 11, UiTheme.Dim);
                        line.style.whiteSpace = WhiteSpace.Normal;
                        card.Add(line);
                        bool taken = ConsulateSystem.Taken(s, c.Code);
                        var can = ConsulateSystem.CanAccept(s, c);
                        var accept = Widgets.TextButton(taken ? "TAKEN" : can.Ok ? "ACCEPT" : can.Reason ?? "—", () =>
                        {
                            var res = ConsulateSystem.Accept(ctx.State!, c, out _);
                            if (res.Ok)
                            {
                                GameAudio.Feedback(Sfx.Launch, Haptic.Medium);
                                ui.Toast($"Haulers away to {c.ClientName} — payment on the way home", Icon.Crate, UiTheme.Good);
                                LocalBootstrap.RequestSync();
                            }
                            else ui.Toast(res.Reason ?? "Can't accept", Icon.Warning, UiTheme.Bad);
                        }, 10);
                        accept.style.marginTop = 6;
                        accept.style.height = 34;
                        Widgets.SetButtonEnabled(accept, !taken && can.Ok);
                        card.Add(accept);
                        body.Add(card);
                    }
                    return;
                }
                case BuildingId.Terraformer:
                {
                    Head("THE PLANET");
                    var t = s.Terraform;
                    Stat("Path", t.Path == TerraformPath.None ? "none chosen yet" : $"{TerraformSystem.Name(t.Path)} · stage {t.Stage}/{TerraformSystem.MaxStages}");
                    Stat("Bonus", TerraformSystem.Bonus(t.Path, t.Stage), t.Stage > 0 ? UiTheme.Good : UiTheme.Dim);
                    Stat("Stages this level allows", TerraformSystem.MaxStage(level).ToString());
                    if (t.ProjectEndsTick > 0)
                    {
                        Stat("Stage under way", $"done in {UiTheme.FmtDuration(t.ProjectEndsTick - s.Tick)}", UiTheme.Energy);
                        return;
                    }
                    foreach (var path in new[] { TerraformPath.Oceanic, TerraformPath.Crystalline, TerraformPath.Metallic, TerraformPath.Temperate })
                    {
                        int stage = t.Path == path ? t.Stage : 0;
                        var cost = TerraformSystem.ProjectCost(s, stage + 1);
                        var check = TerraformSystem.Check(s, path);
                        string label = $"{TerraformSystem.Name(path).ToUpperInvariant()} · STAGE {stage + 1} · " +
                            $"{UiTheme.FmtAmount(cost.Total)} · {UiTheme.FmtDuration(TerraformSystem.ProjectSeconds(stage + 1))}";
                        var p = path;
                        var go = Action(Widgets.TextButton(check.Ok ? label : $"{TerraformSystem.Name(path).ToUpperInvariant()} · {check.Reason}", () =>
                        {
                            void Start()
                            {
                                var res = TerraformSystem.Start(ctx.State!, p);
                                if (res.Ok) { GameAudio.Feedback(Sfx.Confirm, Haptic.Success); LocalBootstrap.RequestSync(); }
                                else ui.Toast(res.Reason ?? "Can't start", Icon.Warning, UiTheme.Bad);
                            }
                            if (t.Path != TerraformPath.None && t.Path != p && t.Stage > 0)
                                ConfirmPanel.Open($"Switch to the {TerraformSystem.Name(p)} path? Your {t.Stage} {TerraformSystem.Name(t.Path)} " +
                                    "stages are undone.", "SWITCH", () => { Start(); ui.CloseModal(); }, () => ui.CloseModal());
                            else Start();
                        }, 10));
                        Widgets.SetButtonEnabled(go, check.Ok);
                    }
                    return;
                }
                case BuildingId.Observatory:
                {
                    Head("THE TELESCOPES");
                    Stat("Wilds surveys", $"{Math.Round((1 - ObservatorySystem.SurveyMult(s)) * 100)}% faster");
                    Stat("Radar warnings", $"{Math.Round((ObservatorySystem.RadarLeadMult(s) - 1) * 100)}% earlier");
                    var galaxy = ctx.Bots;
                    if (galaxy == null) return;
                    if (level < ObservatorySystem.ForecastLevel)
                        Stat("Dreadnought forecast", $"from level {ObservatorySystem.ForecastLevel}");
                    else if (galaxy.Boss.Active)
                        Stat(BossSystem.Name(galaxy.Boss.Variant), $"here now, at {galaxy.Boss.Tile.X}, {galaxy.Boss.Tile.Y}", UiTheme.Bad);
                    else if (ObservatorySystem.Forecast(s, galaxy) is { } f)
                        Stat($"Next: {BossSystem.Name(BossSystem.VariantFor(galaxy.Boss.Visit + 1))}",
                            $"{f.tile.X}, {f.tile.Y} in {UiTheme.FmtDuration(Math.Max(0, f.atTick - s.Tick))}", UiTheme.Energy);
                    return;
                }
            }
        }

        /// <summary>Display names for the internal resource ids (save format keeps gold/quartz/helium).</summary>
        static string ResDisplay(string? resourceId) => resourceId switch
        {
            "gold" => "gold",
            "quartz" => "quartz",
            "helium" => "helium",
            _ => resourceId ?? "",
        };

        /// <summary>v1 describeEffect: what the next level buys, per building kind.</summary>
        static string DescribeEffect(BuildingId id, int level)
        {
            var def = Buildings.Defs[id];
            int next = level + 1;
            switch (def.Kind)
            {
                case BuildingKind.Producer:
                    return $"{ResDisplay(def.Resource)}: {Balance.ProdPerHour(def.BaseProdPerHour, level)}/h → " +
                           $"{Balance.ProdPerHour(def.BaseProdPerHour, next)}/h";
                case BuildingKind.Energy:
                    return $"energy: +{Balance.EnergyOut(level)} → +{Balance.EnergyOut(next)}";
                case BuildingKind.Storage:
                    return $"protects: {Balance.BaseStorage + Balance.StorageBonus(def.BaseStorageBonus, level)} → " +
                           $"{Balance.BaseStorage + Balance.StorageBonus(def.BaseStorageBonus, next)} each (raid shield)";
                case BuildingKind.Command:
                    return $"building level cap: {level} → {next}";
                case BuildingKind.Ship:
                {
                    var unlocked = new List<string>();
                    foreach (var h in Ships.All)
                        if (Ships.Defs[h].ShipyardLevelReq == next) unlocked.Add(Ships.Defs[h].Name);
                    return unlocked.Count > 0
                        ? $"next unlock: {string.Join(", ", unlocked)}"
                        : "higher levels unlock heavier hulls";
                }
                case BuildingKind.Defense:
                    return $"railguns: {Balance.BastionDamage(level):N0} → {Balance.BastionDamage(next):N0} damage a round · " +
                           $"armoured docks: +{level}% → +{next}% armour for ships at home";
                case BuildingKind.Salvage:
                    return $"recovers {Math.Round(Balance.SalvageRate(level) * 100)}% → {Math.Round(Balance.SalvageRate(next) * 100)}% " +
                           $"of wrecks · holds {UiTheme.FmtAmount(Balance.SalvageCapacity(level) * 1000L)} → " +
                           $"{UiTheme.FmtAmount(Balance.SalvageCapacity(next) * 1000L)} each";
                case BuildingKind.Drones:
                    return $"harvester drones: {Balance.WildsDrones(level)} → {Balance.WildsDrones(next)} · " +
                           $"each carries {Balance.DroneCarry(level):N0} → {Balance.DroneCarry(next):N0} a trip";
                case BuildingKind.Repair:
                    return $"tows home {Math.Round(RepairSystem.TowShare(level) * 100)}% → {Math.Round(RepairSystem.TowShare(next) * 100)}% " +
                           "of the ships you lose defending your colony";
                case BuildingKind.Gate:
                    return $"march speed +{Math.Min(level, 30)}% → +{Math.Min(next, 30)}% · helium −{Math.Min(level, 30)}% → −{Math.Min(next, 30)}%" +
                           (next == JumpGateSystem.PrecisionLevel ? " · free jumps go anywhere you pick" : "");
                case BuildingKind.Embassy:
                    return $"strike wings {3 + Math.Min(level, 30) / 10} → {3 + Math.Min(next, 30) / 10} · " +
                           $"supply runs {1 + Math.Min(level, 30) / 10} → {1 + Math.Min(next, 30) / 10} a day · " +
                           $"clan tribute {Pct(0.04 + 0.05 * Math.Min(level, 30) / 30.0)} → {Pct(0.04 + 0.05 * Math.Min(next, 30) / 30.0)}";
                case BuildingKind.Observatory:
                    return $"surveys −{Math.Round(1.5 * Math.Min(level, 30))}% → −{Math.Round(1.5 * Math.Min(next, 30))}% time · " +
                           $"radar warning +{3 * Math.Min(level, 30)}% → +{3 * Math.Min(next, 30)}%" +
                           (next == ObservatorySystem.ForecastLevel ? " · forecasts the Pirate Dreadnought" : "");
                // The Citadel (2026-09-30).
                case BuildingKind.Vault:
                    return $"shows up to {level} → {next} copies of each relic kind";
                case BuildingKind.Academy:
                    return $"commander XP +{Math.Round(AcademySystem.XpPerLevel * level * 100)}% → +{Math.Round(AcademySystem.XpPerLevel * next * 100)}% · " +
                           $"skill resets {Math.Round(Math.Max(AcademySystem.RespecFloor, 1 - AcademySystem.RespecCutPerLevel * level) * 100)}% → " +
                           $"{Math.Round(Math.Max(AcademySystem.RespecFloor, 1 - AcademySystem.RespecCutPerLevel * next) * 100)}% of the price";
                case BuildingKind.Silo:
                    return $"salvo {Math.Round(SiloSystem.Share(level) * 100)}% → {Math.Round(SiloSystem.Share(next) * 100)}% of a raid · " +
                           $"reload {UiTheme.FmtDuration(SiloSystem.ReloadSec(Math.Max(1, level)))} → {UiTheme.FmtDuration(SiloSystem.ReloadSec(next))}";
                case BuildingKind.Consulate:
                    return $"clients pay {ConsulateSystem.Rate(level):0.00}× → {ConsulateSystem.Rate(next):0.00}× · " +
                           $"{Math.Min(4, 2 + level / 10)} → {Math.Min(4, 2 + next / 10)} contracts a board";
                case BuildingKind.Terraform:
                    return $"stages allowed {TerraformSystem.MaxStage(level)} → {TerraformSystem.MaxStage(next)}";
                case BuildingKind.Radar:
                {
                    string lead(int l) => l < 1 ? "no warning"
                        : UiTheme.FmtDuration(GalaxyRoyale.Sim.Systems.RadarSystem.WarnLeadSeconds(l));
                    return $"warning lead: {lead(level)} → {lead(next)} · " +
                           $"probe speed: +{level * (int)(Balance.RadarProbeSpeedPerLevel * 100)}% → " +
                           $"+{next * (int)(Balance.RadarProbeSpeedPerLevel * 100)}%";
                }
                default:
                    return ""; // research lab — v1 shows nothing
            }
        }
    }

    /// <summary>
    /// v1 BuildMineChooserPanel: tapped an empty expansion plot — pick which of
    /// the three producers to place there (count n/allowed, level-1 cost/time).
    /// </summary>
    public static class BuildMineChooser
    {
        static readonly MineType[] Types =
            { MineType.GoldMine, MineType.QuartzExtractor, MineType.HeliumRefinery };

        /// <summary>An open Mining Belt pad: each pad takes one resource's mine
        /// (its lane), so only that type is offered.</summary>
        public static void Open(GameContext ctx, int plot, MineType only)
        {
            var name = Buildings.Defs[MineTypes.ToBuildingId(only)].Name;
            Open(ctx, plot, new[] { only }, "BUILD HERE", $"This pad is on the {name.ToLowerInvariant()} lane: build another {name} here.");
        }

        public static void Open(GameContext ctx, int plot) =>
            Open(ctx, plot, Types, "EXPANSION PLOT", "Construct an additional producer on this plot.");

        static void Open(GameContext ctx, int plot, MineType[] types, string title, string blurb)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel(title, ui.CloseModal, types.Length == 1 ? 0f : 52f);

            var intro = Widgets.Text(blurb, 12, UiTheme.Dim);
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 8;
            content.Add(intro);

            var list = new VisualElement();
            content.Add(list);

            string cache = "";
            void Refresh()
            {
                var s = ctx.State!;
                // Rebuild only when something shown changes: rebuilding every tick
                // could swallow a tap that landed between a button's press and release.
                var key = new System.Text.StringBuilder().Append(BuildingSystem.AllowedMinesForType(s));
                foreach (var type in types)
                {
                    var id = MineTypes.ToBuildingId(type);
                    key.Append('|').Append(BuildingSystem.MineCountOfType(s, type))
                       .Append(ResourceSystem.CanAfford(s, BuildingSystem.GetUpgradeCost(id, 1)) ? 'y' : 'n')
                       .Append(BuildingSystem.GetBuildTime(s, id, 1));
                }
                if (key.ToString() == cache) return;
                cache = key.ToString();

                list.Clear();
                int allowed = BuildingSystem.AllowedMinesForType(s);
                foreach (var type in types)
                {
                    var bid = MineTypes.ToBuildingId(type);
                    var def = Buildings.Defs[bid];
                    int count = BuildingSystem.MineCountOfType(s, type);
                    var cost = BuildingSystem.GetUpgradeCost(bid, 1);
                    int seconds = BuildingSystem.GetBuildTime(s, bid, 1);
                    bool maxed = count >= allowed;
                    bool canAfford = ResourceSystem.CanAfford(s, cost);

                    var row = Widgets.Row();
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text($"{def.Name}", 13, UiTheme.Text, bold: true));
                    head.Add(Widgets.Text($"{count}/{allowed} built", 11,
                        maxed ? UiTheme.Dim : UiTheme.Accent));
                    row.Add(head);

                    row.Add(Widgets.Text(
                        $"G {UiTheme.FmtAmount(cost.Gold)} · Q {UiTheme.FmtAmount(cost.Quartz)} · " +
                        $"H {UiTheme.FmtAmount(cost.Helium)} · {UiTheme.FmtDuration(seconds)}",
                        11, canAfford ? UiTheme.Dim : UiTheme.Bad));

                    var build = Widgets.Primary(Widgets.TextButton(
                        maxed ? "CC UNLOCKS MORE" : "BUILD", () =>
                    {
                        var res = BuildingSystem.BuildMine(ctx.State!, type, plot, out int newMineId);
                        if (res.Ok)
                        {
                            ui.CloseModal();
                            ui.Toast($"{def.Name} construction started");
                            BuildingMarkers.RequestExtraMineSync();
                        }
                        else
                        {
                            ui.Toast(res.Reason ?? "Cannot build");
                        }
                    }, 11));
                    Widgets.SetButtonEnabled(build, !maxed && canAfford);
                    build.style.marginTop = 6;
                    row.Add(build);
                    list.Add(row);
                }
            }

            ui.OpenModal(blocker, Refresh);
        }
    }
}
