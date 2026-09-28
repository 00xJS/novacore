// Queues + Fleets manager — UI Toolkit port of v1's QueuesPanel. The QUEUES tab
// lists every production slot separately (Build 1/2, Research 1/2, Troops) with
// a status dot and a jump action; the FLEETS tab lists marches with RECALL.
// Also exports IdleQueueCount — v1's queueStatus().count — for the FAB badge.
using System;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class QueuesPanel
    {
        /// <summary>
        /// How many *unlocked* queues are idle (0–3). A queue only nags when you can
        /// actually fill it: building always; research needs a Lab; troops a Shipyard.
        /// </summary>
        public static int IdleQueueCount(GameState state)
        {
            bool buildingEmpty = state.BuildQueue.Count < BuildingSystem.BuildSlots(state);
            bool researchEmpty = state.Buildings[BuildingId.ResearchLab].Level >= 1
                && state.ResearchQueue.Count < ResearchSystem.ResearchSlots(state);
            bool troopsEmpty = state.Buildings[BuildingId.Shipyard].Level >= 1
                && state.ShipQueue.Count < Balance.ShipQueueSlots;
            return (buildingEmpty ? 1 : 0) + (researchEmpty ? 1 : 0) + (troopsEmpty ? 1 : 0);
        }

        sealed class SlotRow
        {
            public string Label = "";
            public string Text = "";
            public Color Dot;
            public Color TextColor;
            public string GotoLabel = "";
            public Action GotoAction = () => { };
            /// <summary>Optional second action (e.g. "+24H" to stack another queue buff).</summary>
            public string? ExtraLabel;
            public Action ExtraAction = () => { };
        }

        static int OwnedCount(GameState state, string itemId)
        {
            foreach (var slot in state.Inventory)
                if (slot.ItemId == itemId) return slot.Count;
            return 0;
        }

        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("MANAGE", ui.CloseModal, 70f);

            // Locked slot-2 ACTIVATE: consume an owned queue buff directly instead of
            // detouring through the inventory (user feedback); shop only when broke.
            void ActivateQueueBuff(string itemId, string buffName)
            {
                var state = ctx.State!;
                if (OwnedCount(state, itemId) > 0)
                {
                    var res = ShopSystem.UseItem(state, itemId);
                    ui.Toast(res.Ok ? $"{buffName} active — +24h third queue slot" : res.Reason ?? "Cannot activate");
                }
                else ui.OpenShop();
            }

            bool fleetsTab = false;
            string cache = "";

            var tabRow = Widgets.HBox(Justify.SpaceBetween);
            tabRow.style.marginBottom = 8;
            var queuesBtn = Widgets.TextButton("QUEUES", () => { fleetsTab = false; cache = ""; });
            queuesBtn.style.width = Length.Percent(49f);
            var fleetsBtn = Widgets.TextButton("FLEETS", () => { fleetsTab = true; cache = ""; });
            fleetsBtn.style.width = Length.Percent(49f);
            tabRow.Add(queuesBtn);
            tabRow.Add(fleetsBtn);
            content.Add(tabRow);

            var list = new VisualElement();
            content.Add(list);

            SlotRow[] BuildingRows(GameState state)
            {
                int slots = BuildingSystem.BuildSlots(state);
                int buffLeft = Math.Max(0, state.Buffs.ExtraBuildSlotUntilTick - state.Tick);
                int owned = OwnedCount(state, "buff-buildslot");
                var rows = new SlotRow[Balance.MaxBuildSlots];
                for (int i = 0; i < Balance.MaxBuildSlots; i++)
                {
                    // The buff-unlocked THIRD slot wears its countdown right in the
                    // name (user spec) — "+24H" stacks another Overdrive on top.
                    // (Plain text, no clock glyph: the runtime font tofu-boxed ⏱.)
                    bool buffedSlot = i >= Balance.BaseBuildSlots && buffLeft > 0;
                    string label = buffedSlot
                        ? $"BUILD · SLOT {i + 1} · {UiTheme.FmtDuration(buffLeft)} left"
                        : $"BUILD · SLOT {i + 1}";
                    string? extraLabel = buffedSlot && owned > 0 ? "+24H" : null;
                    Action extraAction = () => ActivateQueueBuff("buff-buildslot", "Construction Overdrive");

                    var order = i < state.BuildQueue.Count ? state.BuildQueue[i] : null;
                    if (order != null)
                    {
                        string name = Buildings.Defs[order.Building].Name + (order.MineId != null ? " #" : "");
                        string t = order.EndsAtTick > 0
                            ? UiTheme.FmtDuration(Math.Max(0, order.EndsAtTick - state.Tick)) : "queued";
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = $"{name} → Lv.{order.ToLevel}   {t}",
                            Dot = UiTheme.Good, TextColor = UiTheme.Text,
                            GotoLabel = "MANAGE",
                            GotoAction = () => BuildingPanel.Open(ctx, order.Building, order.MineId),
                            ExtraLabel = extraLabel, ExtraAction = extraAction,
                        };
                    }
                    else if (i < slots)
                    {
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = "— idle — start the next upgrade",
                            Dot = UiTheme.Bad, TextColor = UiTheme.Bad,
                            GotoLabel = "START",
                            GotoAction = () => BuildingPanel.Open(ctx, BuildingSystem.NextBestUpgrade(ctx.State!)),
                            ExtraLabel = extraLabel, ExtraAction = extraAction,
                        };
                    }
                    else
                    {
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = owned > 0
                                ? $"Construction Overdrive ready — ×{owned} in inventory"
                                : "locked — buy Construction Overdrive",
                            Dot = UiTheme.Stroke, TextColor = UiTheme.Dim,
                            GotoLabel = "ACTIVATE",
                            GotoAction = () => ActivateQueueBuff("buff-buildslot", "Construction Overdrive"),
                        };
                    }
                }
                return rows;
            }

            SlotRow[] ResearchRows(GameState state)
            {
                int lab = state.Buildings[BuildingId.ResearchLab].Level;
                int slots = ResearchSystem.ResearchSlots(state);
                int buffLeft = Math.Max(0, state.Buffs.ExtraResearchSlotUntilTick - state.Tick);
                int owned = OwnedCount(state, "buff-researchslot");
                var rows = new SlotRow[Balance.MaxResearchSlots];
                for (int i = 0; i < Balance.MaxResearchSlots; i++)
                {
                    bool buffedSlot = i >= Balance.BaseResearchSlots && buffLeft > 0;
                    string label = buffedSlot
                        ? $"RESEARCH · SLOT {i + 1} · {UiTheme.FmtDuration(buffLeft)} left"
                        : $"RESEARCH · SLOT {i + 1}";
                    string? extraLabel = buffedSlot && owned > 0 ? "+24H" : null;
                    Action extraAction = () => ActivateQueueBuff("buff-researchslot", "Research Overclock");

                    if (lab < 1)
                    {
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = i == 0 ? "Build a Research Lab to unlock" : "locked — needs a Research Lab",
                            Dot = UiTheme.Stroke, TextColor = UiTheme.Dim,
                            GotoLabel = "BUILD LAB",
                            GotoAction = () => BuildingPanel.Open(ctx, BuildingId.ResearchLab),
                        };
                        continue;
                    }
                    var order = i < state.ResearchQueue.Count ? state.ResearchQueue[i] : null;
                    if (order != null)
                    {
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = $"{Techs.Defs[order.TechId].Name} → Lv.{order.ToLevel}   {UiTheme.FmtDuration(Math.Max(0, order.EndsAtTick - state.Tick))}",
                            Dot = UiTheme.Good, TextColor = UiTheme.Text,
                            GotoLabel = "MANAGE", GotoAction = ui.OpenResearch,
                            ExtraLabel = extraLabel, ExtraAction = extraAction,
                        };
                    }
                    else if (i < slots)
                    {
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = "— idle — start research",
                            Dot = UiTheme.Bad, TextColor = UiTheme.Bad,
                            GotoLabel = "START", GotoAction = ui.OpenResearch,
                            ExtraLabel = extraLabel, ExtraAction = extraAction,
                        };
                    }
                    else
                    {
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = owned > 0
                                ? $"Research Overclock ready — ×{owned} in inventory"
                                : "locked — buy Research Overclock",
                            Dot = UiTheme.Stroke, TextColor = UiTheme.Dim,
                            GotoLabel = "ACTIVATE",
                            GotoAction = () => ActivateQueueBuff("buff-researchslot", "Research Overclock"),
                        };
                    }
                }
                return rows;
            }

            SlotRow[] TroopsRows(GameState state)
            {
                var rows = new SlotRow[Balance.ShipQueueSlots];
                for (int i = 0; i < Balance.ShipQueueSlots; i++)
                {
                    string label = $"TROOPS · SLOT {i + 1}";
                    if (state.Buildings[BuildingId.Shipyard].Level < 1)
                    {
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = i == 0 ? "Build a Shipyard to unlock" : "locked — needs a Shipyard",
                            Dot = UiTheme.Stroke, TextColor = UiTheme.Dim,
                            GotoLabel = "BUILD",
                            GotoAction = () => BuildingPanel.Open(ctx, BuildingId.Shipyard),
                        };
                        continue;
                    }
                    var order = i < state.ShipQueue.Count ? state.ShipQueue[i] : null;
                    if (order != null)
                    {
                        int bt = FleetSystem.ShipBuildTime(state, order.Hull);
                        long cur = order.NextDoneAtTick > 0 ? Math.Max(0, order.NextDoneAtTick - state.Tick) : bt;
                        long total = cur + Math.Max(0, order.Remaining - 1) * (long)bt;
                        // Batches waiting beyond the parallel window tag the LAST line.
                        string more = i == Balance.ShipQueueSlots - 1
                            && state.ShipQueue.Count > Balance.ShipQueueSlots
                                ? $" (+{state.ShipQueue.Count - Balance.ShipQueueSlots} queued)" : "";
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = $"{order.Remaining}× {Ships.Defs[order.Hull].Name}   {UiTheme.FmtDuration(total)}{more}",
                            Dot = UiTheme.Good, TextColor = UiTheme.Text,
                            GotoLabel = "MANAGE", GotoAction = () => ui.SwitchView(ViewId.Fleet),
                        };
                    }
                    else
                    {
                        rows[i] = new SlotRow
                        {
                            Label = label,
                            Text = "— idle — no ships training",
                            Dot = UiTheme.Bad, TextColor = UiTheme.Bad,
                            // Opens the Shipyard, which carries a one-tap bridge into Fleet
                            // Command's hangar (user feedback: START should lead via the yard).
                            GotoLabel = "START",
                            GotoAction = () => BuildingPanel.Open(ctx, BuildingId.Shipyard),
                        };
                    }
                }
                return rows;
            }

            void RenderQueues(GameState state)
            {
                var slotRows = new System.Collections.Generic.List<SlotRow>();
                slotRows.AddRange(BuildingRows(state));
                slotRows.AddRange(ResearchRows(state));
                slotRows.AddRange(TroopsRows(state));

                foreach (var row in slotRows)
                {
                    var card = Widgets.Row();
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    var labelBox = Widgets.HBox();
                    var dot = new VisualElement();
                    dot.style.width = 10;
                    dot.style.height = 10;
                    dot.style.borderTopLeftRadius = 5;
                    dot.style.borderTopRightRadius = 5;
                    dot.style.borderBottomLeftRadius = 5;
                    dot.style.borderBottomRightRadius = 5;
                    dot.style.backgroundColor = row.Dot;
                    dot.style.marginRight = 8;
                    labelBox.Add(dot);
                    labelBox.Add(Widgets.Text(row.Label, 12, UiTheme.Dim, bold: true));
                    head.Add(labelBox);
                    var actions = Widgets.HBox();
                    if (row.ExtraLabel != null)
                    {
                        var extra = Widgets.TextButton(row.ExtraLabel, row.ExtraAction, 10);
                        extra.style.marginRight = 4;
                        actions.Add(extra);
                    }
                    actions.Add(Widgets.TextButton(row.GotoLabel, row.GotoAction, 10));
                    head.Add(actions);
                    card.Add(head);
                    var text = Widgets.Text(row.Text, 11, row.TextColor);
                    text.style.whiteSpace = WhiteSpace.Normal;
                    card.Add(text);
                    list.Add(card);
                }
            }

            void RenderFleets(GameState state)
            {
                // Radar contacts first — hostile fleets the Radar Station has picked up.
                bool anyFleetThreat = false;
                foreach (var threat in RadarService.DetectedThreats)
                {
                    if (!threat.IsFleet) continue; // spy contacts show on the map, not here
                    anyFleetThreat = true;
                    long etaSec = Math.Max(0, threat.ArrivesAtTick - state.Tick);
                    var card = Widgets.Row();
                    card.style.backgroundColor = new UnityEngine.Color(0.165f, 0.1f, 0.1f, 0.9f);
                    card.Add(Widgets.IconText(Icon.Warning, "INCOMING HOSTILE FLEET", 13, UiTheme.Bad, bold: true));
                    string who = string.IsNullOrEmpty(threat.AttackerName) ? "unknown commander" : threat.AttackerName;
                    string size = threat.FleetCount > 0 ? $"{threat.FleetCount} ships · " : "";
                    card.Add(Widgets.Text($"{size}{who} · arrival {UiTheme.FmtDuration(etaSec)}", 10, UiTheme.Dim));
                    list.Add(card);
                }

                if (state.Marches.Count == 0)
                {
                    if (anyFleetThreat) return;
                    var empty = Widgets.Text("No fleets in flight.\nSend one from the map.", 12, UiTheme.Dim);
                    empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                    empty.style.whiteSpace = WhiteSpace.Normal;
                    empty.style.marginTop = 30;
                    list.Add(empty);
                    return;
                }
                foreach (var m in state.Marches)
                {
                    var march = m;
                    var card = Widgets.Row();
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text($"{MarchCallout.MissionLabel(march)} → {march.Node.X},{march.Node.Y}", 13, UiTheme.Text, bold: true));
                    var btns = Widgets.HBox();
                    // Spy probes can be redirected mid-flight (not while gathering intel) — v1 parity with FleetPanel.
                    if (march.Mission == MarchMission.Spy && march.Phase != MarchPhase.Gathering)
                    {
                        var redirect = Widgets.TextButton("REDIRECT", () =>
                        {
                            ui.CloseModal();
                            ui.SwitchView(ViewId.Map);
                            ctx.GetComponent<MapView>()?.BeginProbeRedirect(march.Id);
                        }, 9);
                        redirect.style.marginRight = 4;
                        btns.Add(redirect);
                    }
                    bool canRecall = march.Phase != MarchPhase.Returning;
                    var recall = Widgets.TextButton(canRecall ? "RECALL" : "HOMING", () =>
                    {
                        if (!canRecall) return;
                        MarchSystem.RecallMarch(state, march.Id);
                        cache = "";
                    }, 10);
                    Widgets.SetButtonEnabled(recall, canRecall);
                    btns.Add(recall);
                    head.Add(btns);
                    card.Add(head);

                    long eta = Math.Max(0, march.ArrivesAtTick - state.Tick);
                    string phase = march.ArrivesAtTick == int.MaxValue
                        ? "holding position — recall to bring home"
                        : march.Phase == MarchPhase.Gathering
                            ? $"gathering · {UiTheme.FmtDuration(eta)}"
                            : $"{march.Phase} · {UiTheme.FmtDuration(eta)}";
                    card.Add(Widgets.Text($"{MarchSystem.FleetCount(march.Ships)} ships · {phase}", 10, UiTheme.Dim));
                    list.Add(card);
                }
            }

            int lastRefreshTick = -1;
            refresh = () =>
            {
                var state = ctx.State!;
                // Tick-gated (timers move at 1 Hz); a tab flip clears `cache`
                // for an immediate rebuild, so taps still feel instant.
                if (state.Tick == lastRefreshTick && cache.Length > 0) return;
                lastRefreshTick = state.Tick;
                string key = $"{fleetsTab}|{state.BuildQueue.Count}|{state.ResearchQueue.Count}|{state.ShipQueue.Count}|{state.Marches.Count}|{state.Tick}";
                if (key == cache) return;
                cache = key;

                Widgets.SetButtonHighlight(queuesBtn, !fleetsTab);
                Widgets.SetButtonHighlight(fleetsBtn, fleetsTab);
                list.Clear();
                if (fleetsTab) RenderFleets(state);
                else RenderQueues(state);
            };
            refresh();
            return blocker;
        }
    }
}
