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

        public static void Open(GameContext ctx, int plot)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("EXPANSION PLOT", ui.CloseModal, 52f);

            var intro = Widgets.Text("Construct an additional producer on this plot.", 12, UiTheme.Dim);
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 8;
            content.Add(intro);

            var list = new VisualElement();
            content.Add(list);

            string cache = "";
            void Refresh()
            {
                var s = ctx.State!;
                string key = s.Tick.ToString();
                if (key == cache) return;
                cache = key;

                list.Clear();
                int allowed = BuildingSystem.AllowedMinesForType(s);
                foreach (var type in Types)
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

                    var build = Widgets.TextButton(
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
                    }, 11);
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
