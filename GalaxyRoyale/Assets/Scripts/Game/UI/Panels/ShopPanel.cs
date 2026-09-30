// GALACTIC EXCHANGE — UI Toolkit port of v1's ShopPanel. ITEMS tab (inventory,
// USE) is the default; the SHOP tab sells with Dark Matter, filtered by the
// four category sub-tabs. Skins show BUY → APPLY → ACTIVE. DARK MATTER
// (2026-09-30) sells packs for real money through the App Store (StoreService),
// priced in the player's own currency by the App Store itself.
using System;
using System.Linq;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class ShopPanel
    {
        public static VisualElement Build(GameContext ctx, out Action refresh, int startTab = 0)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("GALACTIC EXCHANGE", ui.CloseModal, 84f);

            var dmLine = Widgets.Text("", 12, UiTheme.DarkMatter, bold: true);
            dmLine.style.unityTextAlign = UnityEngine.TextAnchor.MiddleRight;
            content.Add(dmLine);

            // 0 ITEMS (the nav entry is "Items"), 1 SHOP, 2 DARK MATTER.
            int tab = startTab;
            bool shopTab = false;
            var category = ShopCategory.Resources;
            string cache = "";

            var tabRow = Widgets.HBox(Justify.SpaceBetween);
            tabRow.style.marginTop = 6;
            var itemsBtn = Widgets.TextButton("ITEMS", () => { tab = 0; cache = ""; }, 11);
            itemsBtn.style.width = Length.Percent(32f);
            var shopBtn = Widgets.TextButton("SHOP", () => { tab = 1; cache = ""; }, 11);
            shopBtn.style.width = Length.Percent(32f);
            var dmBtn = Widgets.TextButton("DARK MATTER", () => { tab = 2; cache = ""; }, 11);
            dmBtn.name = "shop-darkmatter";
            dmBtn.style.width = Length.Percent(32f);
            tabRow.Add(itemsBtn);
            tabRow.Add(shopBtn);
            tabRow.Add(dmBtn);
            content.Add(tabRow);

            var catRow = Widgets.HBox(Justify.SpaceBetween);
            catRow.style.marginTop = 6;
            var catButtons = new System.Collections.Generic.Dictionary<ShopCategory, Button>();
            foreach (var (cat, label) in new[]
            {
                (ShopCategory.Resources, "RESOURCES"), (ShopCategory.Speedups, "SPEED-UPS"),
                (ShopCategory.Buffs, "BUFFS"), (ShopCategory.Skins, "SKINS"),
            })
            {
                var c = cat;
                var b = Widgets.TextButton(label, () => { category = c; cache = ""; }, 9);
                b.style.width = Length.Percent(24f);
                catButtons[c] = b;
                catRow.Add(b);
            }
            content.Add(catRow);

            var list = new VisualElement();
            list.style.marginTop = 8;
            content.Add(list);

            void ItemRow(string name, string desc, string actionLabel, bool enabled, Action action)
            {
                var row = Widgets.Row();
                var box = Widgets.HBox(Justify.SpaceBetween, Align.Center);
                var textCol = new VisualElement();
                textCol.style.flexShrink = 1f;
                textCol.style.marginRight = 8;
                textCol.Add(Widgets.Text(name, 12, UiTheme.Text, bold: true));
                var d = Widgets.Text(desc, 10, UiTheme.Dim);
                d.style.whiteSpace = WhiteSpace.Normal;
                textCol.Add(d);
                box.Add(textCol);
                var btn = Widgets.TextButton(actionLabel, action, 10);
                btn.style.minWidth = 78;
                Widgets.SetButtonEnabled(btn, enabled);
                box.Add(btn);
                row.Add(box);
                list.Add(row);
            }

            void RenderItems()
            {
                var state = ctx.State!;
                if (state.Inventory.Count == 0)
                {
                    var empty = Widgets.Text("Inventory is empty.\nBuy consumables in the SHOP tab.", 12, UiTheme.Dim);
                    empty.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                    empty.style.whiteSpace = WhiteSpace.Normal;
                    empty.style.marginTop = 24;
                    list.Add(empty);
                    return;
                }
                foreach (var slot in state.Inventory.ToArray())
                {
                    if (!Shop.ById.TryGetValue(slot.ItemId, out var item)) continue;
                    ItemRow($"{item.Name}  ×{slot.Count}", item.Description, "USE", true, () =>
                    {
                        if (item.Effect == ShopEffect.RelocateTarget)
                        {
                            // Map targeting flow — the item is consumed on WARP confirm, not here.
                            ui.CloseModal();
                            ui.SwitchView(ViewId.Map);
                            ctx.GetComponent<MapView>()?.BeginRelocation();
                            return;
                        }
                        var res = ShopSystem.UseItem(state, item.Id);
                        ui.Toast(res.Ok ? $"{item.Name} used" : res.Reason ?? "Cannot use");
                        cache = "";
                    });
                }
            }

            void RenderShop()
            {
                var state = ctx.State!;
                foreach (var item in Shop.Items)
                {
                    if (item.Category != category) continue;

                    if (item.IsSkin)
                    {
                        bool owned = state.Skins.Owned.Contains(item.Id);
                        bool active = state.Skins.ActivePlanet == item.Id;
                        string label = active ? "ACTIVE" : owned ? "APPLY" : $"{item.PriceDM} DM";
                        ItemRow(item.Name, item.Description, label, !active, () =>
                        {
                            var res = owned
                                ? ShopSystem.ApplySkin(state, item.Id)
                                : ShopSystem.Buy(state, item.Id, ctx.Events!);
                            ui.Toast(res.Ok
                                ? owned ? $"{item.Name} equipped" : $"{item.Name} unlocked"
                                : res.Reason ?? "Cannot buy");
                            cache = "";
                        });
                        continue;
                    }

                    bool affordable = state.Premium.DarkMatter >= item.PriceDM;
                    ItemRow(item.Name, item.Description, $"{item.PriceDM} DM", affordable, () =>
                    {
                        var res = ShopSystem.Buy(state, item.Id, ctx.Events!);
                        ui.Toast(res.Ok ? $"{item.Name} → inventory" : res.Reason ?? "Cannot buy");
                        cache = "";
                    });
                }
            }

            void RenderDarkMatter()
            {
                var store = StoreService.Instance;
                var intro = Widgets.Text("Dark Matter buys speed-ups, resource packs, shields and skins in the SHOP. " +
                                         "You also earn it from quests, events, achievements and the Galactic Core.", 11, UiTheme.Dim);
                intro.style.whiteSpace = WhiteSpace.Normal;
                intro.style.marginBottom = 4;
                list.Add(intro);
                if (store == null || store.State != StoreService.Status.Ready)
                {
                    string msg = store == null ? "The App Store isn't available."
                        : store.State == StoreService.Status.Loading ? "Contacting the App Store…"
                        : store.Error + ".";
                    var note = Widgets.Text(msg, 12, UiTheme.Dim);
                    note.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                    note.style.whiteSpace = WhiteSpace.Normal;
                    note.style.marginTop = 24;
                    list.Add(note);
                    if (store != null && store.State == StoreService.Status.Unavailable)
                    {
                        var retry = Widgets.TextButton("TRY AGAIN", () => { store.Load(); cache = ""; }, 11);
                        retry.style.alignSelf = Align.Center;
                        retry.style.marginTop = 10;
                        retry.style.minWidth = 120;
                        list.Add(retry);
                    }
                    return;
                }
                foreach (var pack in DarkMatterPacks.All)
                {
                    if (!store.Prices.TryGetValue(pack.ProductId, out var price)) continue;
                    string desc = pack.Bonus > 0
                        ? $"{pack.DarkMatter:N0} Dark Matter  ·  +{pack.Bonus * 100f:0}% bonus"
                        : $"{pack.DarkMatter:N0} Dark Matter";
                    var p = pack;
                    ItemRow(pack.Name, desc, store.Busy ? "…" : price, !store.Busy, () => { store.Buy(p); cache = ""; });
                }
                var small = Widgets.Text("Purchases are charged to your Apple Account and can't be undone in the game. " +
                                         "Dark Matter is kept in your save and your iCloud backup.", 10, UiTheme.Dim);
                small.style.whiteSpace = WhiteSpace.Normal;
                small.style.marginTop = 10;
                list.Add(small);
            }

            refresh = () =>
            {
                var state = ctx.State!;
                shopTab = tab == 1;
                var store = StoreService.Instance;
                var sb = new System.Text.StringBuilder();
                sb.Append(tab).Append('|');
                if (tab == 2 && store != null)
                    sb.Append(store.State).Append(store.Busy).Append(store.Prices.Count).Append(store.Error).Append('|');
                sb.Append(shopTab).Append('|').Append(category).Append('|')
                  .Append(state.Premium.DarkMatter).Append('|')
                  .Append(state.Skins.Owned.Count).Append('|').Append(state.Skins.ActivePlanet).Append('|');
                foreach (var e in state.Inventory) sb.Append(e.ItemId).Append(':').Append(e.Count).Append(',');
                string key = sb.ToString();
                if (key == cache) return;
                cache = key;

                dmLine.text = $"{state.Premium.DarkMatter:N0} DM";
                Widgets.SetButtonHighlight(itemsBtn, tab == 0);
                Widgets.SetButtonHighlight(shopBtn, tab == 1);
                Widgets.SetButtonHighlight(dmBtn, tab == 2);
                catRow.style.display = shopTab ? DisplayStyle.Flex : DisplayStyle.None;
                foreach (var kv in catButtons)
                    Widgets.SetButtonHighlight(kv.Value, kv.Key == category);

                list.Clear();
                if (tab == 2) RenderDarkMatter();
                else if (shopTab) RenderShop();
                else RenderItems();
            };
            refresh();
            return blocker;
        }
    }
}
