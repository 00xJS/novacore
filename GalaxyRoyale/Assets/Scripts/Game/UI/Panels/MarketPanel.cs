// GALACTIC MARKET (MORE › MARKET): today's prices with their six-hour trend,
// then a trade composer — what to sell, what to buy, how much — with an exact
// quote (fee and price movement included) before you commit.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class MarketPanel
    {
        static ResourceId s_sell = ResourceId.Gold, s_buy = ResourceId.Helium;

        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            if (ctx.State == null) return;
            var (blocker, content, footer) = Widgets.ModalPanelFooter("GALACTIC MARKET", ui.CloseModal, 84f);

            // ---- prices ----
            var priceHead = Widgets.Text("PRICES", 10, UiTheme.Dim, bold: true);
            content.Add(priceHead);
            var priceRows = new Dictionary<ResourceId, (Label price, VisualElement trend, Label change)>();
            var priceCard = Widgets.Row();
            priceCard.style.marginTop = 4;
            foreach (var r in Resources.All)
            {
                var line = Widgets.HBox(Justify.SpaceBetween);
                line.style.marginTop = 3;
                line.Add(Widgets.Text(MarketSystem.Name(r).ToUpperInvariant(), 12, Tint(r), bold: true));
                var right = Widgets.HBox();
                var price = Widgets.Text("", 12, UiTheme.Text, bold: true);
                right.Add(price);
                var trendBox = new VisualElement();
                trendBox.style.marginLeft = 8;
                right.Add(trendBox);
                var change = Widgets.Text("", 10, UiTheme.Dim);
                change.style.marginLeft = 3;
                change.style.minWidth = 34;
                right.Add(change);
                line.Add(right);
                priceCard.Add(line);
                priceRows[r] = (price, trendBox, change);
            }
            var priceNote = Note("Credits per unit. Prices wander over the day; your own big trades move them too, " +
                "and that wears off over a few hours.", 4);
            priceCard.Add(priceNote);
            content.Add(priceCard);

            // ---- the trade ----
            var sellHead = Widgets.Text("SELL", 10, UiTheme.Dim, bold: true);
            sellHead.style.marginTop = 10;
            content.Add(sellHead);
            var sellRow = Widgets.HBox(Justify.SpaceBetween);
            sellRow.style.marginTop = 4;
            content.Add(sellRow);
            var buyHead = Widgets.Text("BUY", 10, UiTheme.Dim, bold: true);
            buyHead.style.marginTop = 10;
            content.Add(buyHead);
            var buyRow = Widgets.HBox(Justify.SpaceBetween);
            buyRow.style.marginTop = 4;
            content.Add(buyRow);

            var amountHead = Widgets.HBox(Justify.SpaceBetween);
            amountHead.style.marginTop = 12;
            amountHead.Add(Widgets.Text("AMOUNT", 10, UiTheme.Dim, bold: true));
            var amountLabel = Widgets.Text("0", 13, UiTheme.Accent, bold: true);
            amountHead.Add(amountLabel);
            content.Add(amountHead);
            var slider = new SliderInt(0, 1) { value = 0 };
            content.Add(slider);
            var quick = Widgets.HBox(Justify.SpaceBetween);
            quick.style.marginTop = 4;
            content.Add(quick);
            var maxNote = Note("", 2);
            content.Add(maxNote);
            var quoteBox = Widgets.Row();
            quoteBox.style.marginTop = 10;
            var quoteHead = Widgets.Text("", 13, UiTheme.Good, bold: true);
            quoteHead.style.whiteSpace = WhiteSpace.Normal;
            var quoteLine = Note("", 3);
            quoteBox.Add(quoteHead);
            quoteBox.Add(quoteLine);
            content.Add(quoteBox);

            var sellButtons = new List<(ResourceId r, Button b)>();
            var buyButtons = new List<(ResourceId r, Button b)>();
            Button? trade = null;
            long amount = 0;

            foreach (var res in Resources.All)
            {
                var r = res;
                var sb = Widgets.TextButton(MarketSystem.Name(r).ToUpperInvariant(), () =>
                {
                    s_sell = r;
                    if (s_buy == r) s_buy = Next(r);
                    amount = 0;
                    Sync();
                }, 11);
                sb.style.width = Length.Percent(32f);
                sellButtons.Add((r, sb));
                sellRow.Add(sb);
                var bb = Widgets.TextButton(MarketSystem.Name(r).ToUpperInvariant(), () =>
                {
                    s_buy = r;
                    if (s_sell == r) { s_sell = Next(r); amount = 0; }
                    Sync();
                }, 11);
                bb.style.width = Length.Percent(32f);
                buyButtons.Add((r, bb));
                buyRow.Add(bb);
            }
            foreach (var (label, share) in new[] { ("10%", 0.1), ("25%", 0.25), ("50%", 0.5), ("MAX", 1.0) })
            {
                double part = share;
                var q = Widgets.TextButton(label, () =>
                {
                    amount = (long)(MarketSystem.MaxSell(ctx.State!, s_sell) * part);
                    Sync();
                }, 10);
                q.style.width = Length.Percent(23.5f);
                quick.Add(q);
            }
            slider.RegisterValueChangedCallback(e => { amount = e.newValue; SyncQuote(); });

            void Sync()
            {
                var s = ctx.State!;
                foreach (var (r, b) in sellButtons) Widgets.SetButtonHighlight(b, r == s_sell);
                foreach (var (r, b) in buyButtons) Widgets.SetButtonHighlight(b, r == s_buy);
                long max = Math.Max(0, MarketSystem.MaxSell(s, s_sell));
                amount = Math.Max(0, Math.Min(amount, max));
                slider.highValue = (int)Math.Min(int.MaxValue, Math.Max(1, max));
                slider.SetValueWithoutNotify((int)Math.Min(int.MaxValue, amount));
                slider.SetEnabled(max > 0);
                long wallet = s.Resources.Get(s_sell) / 1000;
                maxNote.text = wallet > max
                    ? $"One trade moves at most {max:N0} {MarketSystem.Name(s_sell)} (the market's depth); you have {wallet:N0}."
                    : $"You have {wallet:N0} {MarketSystem.Name(s_sell)}.";
                SyncQuote();
            }

            void SyncQuote()
            {
                var s = ctx.State!;
                amountLabel.text = amount.ToString("N0");
                if (amount <= 0)
                {
                    quoteHead.text = "Choose how much to sell";
                    quoteHead.style.color = UiTheme.Dim;
                    quoteLine.text = $"Fee {Math.Round(MarketSystem.Fee * 100):0}% on every trade.";
                    if (trade != null) Widgets.SetButtonEnabled(trade, false);
                    return;
                }
                var q = MarketSystem.GetQuote(s, s_sell, s_buy, amount);
                if (!q.Ok)
                {
                    quoteHead.text = q.Reason ?? "Can't trade";
                    quoteHead.style.color = UiTheme.Bad;
                    quoteLine.text = "";
                    if (trade != null) Widgets.SetButtonEnabled(trade, false);
                    return;
                }
                double posted = MarketSystem.Rate(s, s_sell, s_buy);
                double got = q.BuyWhole / (double)q.SellWhole;
                quoteHead.text = $"You get {q.BuyWhole:N0} {MarketSystem.Name(s_buy)}";
                quoteHead.style.color = UiTheme.Good;
                quoteLine.text = $"{got:0.###} {MarketSystem.Name(s_buy)} per {MarketSystem.Name(s_sell)} " +
                    $"(posted {posted:0.###}, less the {Math.Round(MarketSystem.Fee * 100):0}% fee" +
                    (got < posted * (1 - MarketSystem.Fee) * 0.995 ? " and the price your trade moves)" : ")");
                if (trade != null) Widgets.SetButtonEnabled(trade, true);
            }

            string priceKey = "";
            void RefreshPrices()
            {
                var s = ctx.State!;
                string key = $"{s.Tick / 60}|{s.Market.ImpactTick}|{s.Stats.MarketTrades}";
                if (key == priceKey) return;
                priceKey = key;
                foreach (var r in Resources.All)
                {
                    var (price, trendBox, change) = priceRows[r];
                    double now = MarketSystem.Price(s, r);
                    double before = MarketSystem.BaseValue(r) * MarketSystem.Drift(s.Seed, r, s.Tick - MarketSystem.DriftStepSec);
                    double delta = before > 0 ? now / before - 1 : 0;
                    price.text = now.ToString("0.000");
                    trendBox.Clear();
                    bool up = delta >= 0;
                    trendBox.Add(Icons.Make(up ? Icon.ArrowUp : Icon.ArrowDown, 11f, up ? UiTheme.Good : UiTheme.Bad));
                    change.text = $"{Math.Abs(Math.Round(delta * 100)):0}%";
                    change.style.color = up ? UiTheme.Good : UiTheme.Bad;
                }
            }

            trade = Widgets.IconButton(Icon.Rotate, "TRADE", () =>
            {
                var s = ctx.State!;
                var q = MarketSystem.GetQuote(s, s_sell, s_buy, amount);
                var res = MarketSystem.Trade(s, s_sell, s_buy, amount);
                if (!res.Ok) { ui.Toast(res.Reason ?? "Can't trade", Icon.Info, UiTheme.Bad); return; }
                GameAudio.Feedback(Sfx.Coins, Haptic.Light);
                LocalBootstrap.RequestSync();
                ui.Toast($"Sold {q.SellWhole:N0} {MarketSystem.Name(s_sell)} for {q.BuyWhole:N0} {MarketSystem.Name(s_buy)}",
                    Icon.Rotate, UiTheme.Good);
                amount = 0;
                priceKey = "";
                RefreshPrices();
                Sync();
            }, 14);
            trade.style.height = 42;
            footer.Add(trade);

            RefreshPrices();
            Sync();
            blocker.schedule.Execute(() => { RefreshPrices(); SyncQuote(); }).Every(1000);
            ui.OpenModal(blocker);
        }

        static ResourceId Next(ResourceId r) => r == ResourceId.Gold ? ResourceId.Quartz
            : r == ResourceId.Quartz ? ResourceId.Helium : ResourceId.Gold;

        static UnityEngine.Color Tint(ResourceId r) => r switch
        {
            ResourceId.Quartz => UiTheme.Quartz,
            ResourceId.Helium => UiTheme.Helium,
            _ => UiTheme.Gold,
        };

        static Label Note(string text, float marginTop)
        {
            var l = Widgets.Text(text, 11, UiTheme.Dim);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = marginTop;
            return l;
        }
    }
}
