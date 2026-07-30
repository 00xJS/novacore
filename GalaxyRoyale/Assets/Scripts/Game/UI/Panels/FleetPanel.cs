// FLEET COMMAND — UI Toolkit port of v1's FleetScene as a full-screen panel:
// hangar rows (stats, quantity slider, cost/time preview, BUILD), the ship
// build queue (SPEED UP / cancel-with-confirm), and active marches (RECALL).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class FleetPanel
    {
        static string CostBits(ResourceBag cost, int n)
        {
            // Roomier separators (user feedback: the cost figures sat too close
            // together and the row has the horizontal space to breathe).
            string Fmt(long v) => v >= 1000 ? $"{v / 1000.0:0.0}k" : v.ToString();
            var parts = new List<string>();
            if (cost.Gold > 0) parts.Add($"{Fmt(cost.Gold * n)} G");
            if (cost.Quartz > 0) parts.Add($"{Fmt(cost.Quartz * n)} Q");
            if (cost.Helium > 0) parts.Add($"{Fmt(cost.Helium * n)} H");
            return string.Join("    ", parts);
        }

        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            // Shipyard-exclusive builder (user feedback): marches live in the
            // Queues FLEETS tab; this screen is for BUILDING ships.
            var (blocker, content) = Widgets.ModalPanel("SHIPYARD — BUILD SHIPS", ui.CloseModal, 84f);

            content.Add(Widgets.Text("set a quantity, then BUILD · fleets in flight are managed in QUEUES → FLEETS", 10, UiTheme.Dim));

            var refreshers = new List<Action>();

            // ---- hangar rows: COLLAPSIBLE tier sections (user feedback: 23 hulls
            // was one long list). Ships.All is ordered so each ShipDef.Class is
            // contiguous; each class becomes a tap-to-toggle group. Tiers with no
            // unlocked hull start collapsed so you don't scroll past locked ships.
            string lastClass = "";
            VisualElement currentGroup = content;
            foreach (var hull in Ships.All)
            {
                var h = hull;
                var def = Ships.Defs[h];
                if (def.Class != lastClass)
                {
                    lastClass = def.Class;
                    string cls = def.Class;
                    var group = new VisualElement();
                    bool anyUnlocked = Ships.All.Any(x =>
                        Ships.Defs[x].Class == cls && FleetSystem.UnlockBlocker(ctx.State!, x) == null);
                    group.style.display = anyUnlocked ? DisplayStyle.Flex : DisplayStyle.None;
                    var header = Widgets.TextButton(
                        $"{(anyUnlocked ? "▾" : "▸")}  {cls.ToUpper()}", null!, 11);
                    header.style.marginTop = 12;
                    header.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
                    header.clicked += () =>
                    {
                        bool open = group.style.display == DisplayStyle.None;
                        group.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                        header.text = $"{(open ? "▾" : "▸")}  {cls.ToUpper()}";
                    };
                    content.Add(header);
                    content.Add(group);
                    currentGroup = group;
                }
                var row = Widgets.Row();
                row.style.marginTop = 6;

                var head = Widgets.HBox(Justify.SpaceBetween);
                head.Add(Widgets.Text(def.Name, 14, UiTheme.Text, bold: true));
                var owned = Widgets.Text("", 14, UiTheme.Accent, bold: true);
                head.Add(owned);
                row.Add(head);

                string counters = def.Counters is HullId c ? $" · counters {Ships.Defs[c].Name}s" : "";
                string shield = def.Shield > 0 ? $" · shd {def.Shield}" : "";
                row.Add(Widgets.Text(
                    $"atk {def.Atk}{shield} · hp {def.Hp} · spd {def.Speed} · cargo {def.Cargo}{counters}",
                    9, UiTheme.Dim));

                var lockLabel = Widgets.Text("", 11, UiTheme.Bad);
                row.Add(lockLabel);

                var controls = Widgets.HBox();
                controls.style.marginTop = 4;
                var slider = new SliderInt(1, 1) { value = 1 };
                slider.style.flexGrow = 1f;
                slider.style.marginRight = 8;
                var qtyLabel = Widgets.Text("1", 13, UiTheme.Text, bold: true);
                qtyLabel.style.minWidth = 40;
                qtyLabel.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                controls.Add(slider);
                controls.Add(qtyLabel);
                bool userTouched = false;
                var preview = Widgets.Text("", 10, UiTheme.Accent);
                var buildBtn = Widgets.TextButton("BUILD", () =>
                {
                    var res = FleetSystem.QueueShips(ctx.State!, h, slider.value);
                    if (!res.Ok) ui.Toast(res.Reason ?? "Cannot build");
                    else userTouched = false; // next default snaps back to max
                }, 12);
                buildBtn.style.marginLeft = 8;
                buildBtn.style.minWidth = 70;
                controls.Add(buildBtn);
                row.Add(controls);
                row.Add(preview);
                currentGroup.Add(row); // into the collapsible tier group

                void UpdatePreview()
                {
                    qtyLabel.text = slider.value.ToString();
                    int bt = FleetSystem.ShipBuildTime(ctx.State!, h);
                    preview.text = $"×{slider.value}    ·    {CostBits(def.Cost, slider.value)}    ·    {UiTheme.FmtDuration((long)bt * slider.value)}";
                }
                slider.RegisterValueChangedCallback(_ => { userTouched = true; UpdatePreview(); });

                string cache = "";
                refreshers.Add(() =>
                {
                    var state = ctx.State!;
                    int have = state.Ships.TryGetValue(h, out var n) ? n : 0;
                    string? blocker = FleetSystem.UnlockBlocker(state, h);
                    bool locked = blocker != null;
                    int max = FleetSystem.MaxBuildable(state, h);
                    string key = $"{have}|{blocker}|{max}|{(userTouched ? slider.value : -1)}";
                    if (key == cache) return;
                    cache = key;

                    owned.text = $"DOCKED × {have}";
                    bool show = !locked;
                    controls.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                    preview.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                    lockLabel.text = blocker ?? "";
                    lockLabel.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;
                    if (locked) return;

                    slider.highValue = Math.Max(1, max);
                    if (!userTouched) slider.SetValueWithoutNotify(Math.Max(1, max));
                    Widgets.SetButtonEnabled(buildBtn, max >= 1);
                    UpdatePreview();
                });
            }

            // ---- build queue ----
            var queueHeader = Widgets.Text("BUILD QUEUE — idle", 11, UiTheme.Dim, bold: true);
            queueHeader.style.marginTop = 10;
            content.Add(queueHeader);
            var queueList = new VisualElement();
            content.Add(queueList);

            string queueCache = "";
            refreshers.Add(() =>
            {
                var state = ctx.State!;
                var sb = new System.Text.StringBuilder();
                foreach (var o in state.ShipQueue) sb.Append(o.Hull).Append(':').Append(o.Remaining).Append(',');
                sb.Append('|').Append(state.Tick);
                string key = sb.ToString();
                if (key == queueCache) return;
                queueCache = key;

                queueHeader.text = state.ShipQueue.Count > 0 ? "BUILD QUEUE" : "BUILD QUEUE — idle";
                queueList.Clear();
                for (int i = 0; i < state.ShipQueue.Count; i++)
                {
                    var entry = state.ShipQueue[i];
                    var sdef = Ships.Defs[entry.Hull];
                    // The first ShipQueueSlots orders build in PARALLEL lines now.
                    bool active = i < Balance.ShipQueueSlots;
                    string statusText = active && entry.NextDoneAtTick > 0
                        ? $"next in {UiTheme.FmtDuration(entry.NextDoneAtTick - state.Tick)}"
                        : active ? "starting…" : "queued";

                    var qrow = Widgets.Row();
                    var qbox = Widgets.HBox(Justify.SpaceBetween);
                    qbox.Add(Widgets.Text($"{entry.Remaining}× {sdef.Name} — {statusText}", 11, UiTheme.Text));
                    var btns = Widgets.HBox();
                    var speed = Widgets.TextButton("SPEED UP", () => SpeedUpPanel.OpenForShips(ctx, entry), 10);
                    speed.style.marginRight = 4;
                    btns.Add(speed);
                    int idx = i;
                    btns.Add(Widgets.TextButton("×", () =>
                        ConfirmPanel.Open(
                            $"Cancel {entry.Remaining}× {sdef.Name}?\nResources will be refunded.",
                            "CANCEL ORDER",
                            () =>
                            {
                                FleetSystem.CancelShipOrder(ctx.State!, idx);
                                ui.SwitchView(ViewId.Fleet);
                            },
                            () => ui.SwitchView(ViewId.Fleet)), 12));
                    qbox.Add(btns);
                    qrow.Add(qbox);
                    queueList.Add(qrow);
                }
            });

            refresh = () => { foreach (var r in refreshers) r(); };
            refresh();
            return blocker;
        }
    }
}
