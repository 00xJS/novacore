// Shared small modals: ConfirmPanel (yes/no for destructive actions) and
// SpeedUpPanel (targeted timer cuts from inventory items) — ports of v1's
// ConfirmPanel.ts and SpeedUpPanel.ts.
using System;
using System.Linq;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class ConfirmPanel
    {
        /// <summary>Generic yes/no confirmation. Both paths close the modal first.</summary>
        public static void Open(string message, string confirmLabel, Action onConfirm, Action? onCancel = null)
        {
            var ui = UIController.Instance!;
            // heightPct 0 → a compact content-hugging card (user feedback: the
            // fixed-height confirm felt oversized for a one-line question).
            var (blocker, content) = Widgets.ModalPanel("CONFIRM", () =>
            {
                ui.CloseModal();
                onCancel?.Invoke();
            }, 0f);

            var text = Widgets.Text(message, 13, UiTheme.Text);
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
            text.style.marginTop = 6;
            content.Add(text);

            // A long confirm label ("RESET — LOSE EVERYTHING") spilled out of a
            // half-width button, so long ones stack full width instead.
            bool stack = confirmLabel.Length > 14;
            var buttons = stack ? new VisualElement() : Widgets.HBox(Justify.SpaceAround);
            buttons.style.marginTop = 14;
            var confirm = Widgets.TextButton(confirmLabel, () => { ui.CloseModal(); onConfirm(); });
            confirm.style.width = Length.Percent(stack ? 100f : 45f);
            var cancel = Widgets.TextButton("BACK", () => { ui.CloseModal(); onCancel?.Invoke(); });
            cancel.style.width = Length.Percent(stack ? 100f : 45f);
            if (stack) cancel.style.marginTop = 8;
            buttons.Add(confirm);
            buttons.Add(cancel);
            content.Add(buttons);

            ui.OpenModal(blocker);
        }
    }

    /// <summary>
    /// Two-option decision modal — for real forks in the road (e.g. keep device
    /// empire vs load cloud), where ConfirmPanel's confirm/back framing would
    /// make one option feel like a cancel.
    /// </summary>
    public static class ChoicePanel
    {
        /// <param name="onDismiss">What the × does besides closing (default: nothing).</param>
        public static void Open(string title, string message,
            (string label, Action action) first, (string label, Action action) second,
            Action? onDismiss = null)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel(title, () => { ui.CloseModal(); onDismiss?.Invoke(); }, 42f);

            var text = Widgets.Text(message, 13, UiTheme.Text);
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
            text.style.marginTop = 10;
            content.Add(text);

            Button Option((string label, Action action) opt)
            {
                var b = Widgets.TextButton(opt.label, () => { ui.CloseModal(); opt.action(); });
                b.style.marginTop = 12;
                b.style.height = 40;
                return b;
            }
            content.Add(Option(first));
            content.Add(Option(second));

            ui.OpenModal(blocker);
        }
    }

    /// <summary>
    /// Targeted speed-up picker: shows the timer's remaining time, one button per
    /// owned speed-up token (consume → shave), the matching finisher item, and a
    /// GET MORE jump to the shop. `apply(long.MaxValue)` means "finish now".
    /// </summary>
    public static class SpeedUpPanel
    {
        public static void Open(GameContext ctx, string title, string finishItemId,
            Func<long> remainingSec, Action<long> apply, Action onBack)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel($"SPEED UP — {title}", () =>
            {
                ui.CloseModal();
                onBack();
            }, 62f);

            var remainLabel = Widgets.Text("", 14, UiTheme.Accent, bold: true);
            remainLabel.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
            remainLabel.style.marginBottom = 8;
            content.Add(remainLabel);

            var list = new VisualElement();
            content.Add(list);

            var getMore = Widgets.TextButton("GET MORE IN SHOP", () => ui.OpenShop());
            getMore.style.marginTop = 10;
            content.Add(getMore);

            string cache = "";
            void Refresh()
            {
                var state = ctx.State!;
                long remain = remainingSec();
                if (remain <= 0)
                {
                    ui.CloseModal();
                    onBack();
                    return;
                }
                remainLabel.text = $"{UiTheme.FmtDuration(remain)} remaining";

                var sb = new System.Text.StringBuilder();
                foreach (var e in state.Inventory) sb.Append(e.ItemId).Append(':').Append(e.Count).Append(',');
                string key = sb.ToString();
                if (key == cache) return;
                cache = key;

                list.Clear();
                bool any = false;

                foreach (var item in Shop.Items)
                {
                    bool isFinisher = item.Id == finishItemId;
                    bool isSpeedToken = item.Effect == ShopEffect.Speedup;
                    if (!isFinisher && !isSpeedToken) continue;
                    int count = state.Inventory.FirstOrDefault(i => i.ItemId == item.Id)?.Count ?? 0;
                    if (count < 1) continue;
                    any = true;

                    var row = Widgets.Row();
                    var box = Widgets.HBox(Justify.SpaceBetween);
                    box.Add(Widgets.Text($"{item.Name}  ×{count}", 12, UiTheme.Text));
                    box.Add(Widgets.TextButton(isFinisher ? "FINISH" : "USE", () =>
                    {
                        if (!ShopSystem.ConsumeItem(ctx.State!, item.Id).Ok) return;
                        apply(isFinisher ? long.MaxValue : item.SpeedupSec ?? 0);
                        cache = "";
                    }, 10));
                    row.Add(box);
                    list.Add(row);
                }

                if (!any)
                {
                    var none = Widgets.Text("No speed-up items in your inventory.", 12, UiTheme.Dim);
                    none.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                    none.style.marginTop = 12;
                    list.Add(none);
                }
            }

            ui.OpenModal(blocker, Refresh);
        }

        /// <summary>Speed-up for an in-progress research slot; back returns to the tech tree.</summary>
        public static void OpenForResearch(GameContext ctx, int index)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            string title = index < state.ResearchQueue.Count
                ? $"{Techs.Defs[state.ResearchQueue[index].TechId].Name} research"
                : "Research";
            Open(ctx, title, "finish-research",
                remainingSec: () =>
                {
                    var q = index < state.ResearchQueue.Count ? state.ResearchQueue[index] : null;
                    return q != null ? Math.Max(0, q.EndsAtTick - state.Tick) : 0;
                },
                apply: cut =>
                {
                    var q = index < state.ResearchQueue.Count ? state.ResearchQueue[index] : null;
                    if (q == null) return;
                    q.EndsAtTick = cut == long.MaxValue
                        ? state.Tick
                        : (int)Math.Max(state.Tick, q.EndsAtTick - cut);
                },
                onBack: ui.OpenResearch);
        }

        /// <summary>Speed-up for a specific ship order (cascades through the batch).</summary>
        public static void OpenForShips(GameContext ctx, GalaxyRoyale.Sim.ShipOrder entry)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var def = Ships.Defs[entry.Hull];
            Open(ctx, $"{entry.Remaining}× {def.Name}", "finish-ships",
                remainingSec: () =>
                {
                    int idx = state.ShipQueue.IndexOf(entry);
                    if (idx < 0) return 0;
                    int bt = FleetSystem.ShipBuildTime(state, entry.Hull);
                    long cur = entry.NextDoneAtTick == 0 ? bt : Math.Max(0, entry.NextDoneAtTick - state.Tick);
                    return cur + Math.Max(0, entry.Remaining - 1) * (long)bt;
                },
                apply: cut =>
                {
                    int idx = state.ShipQueue.IndexOf(entry);
                    if (idx < 0) return;
                    FleetSystem.SpeedUpShipOrder(state, idx, cut == long.MaxValue ? int.MaxValue : (int)cut);
                },
                onBack: () => ui.SwitchView(ViewId.Fleet));
        }
    }
}
