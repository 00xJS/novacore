// A sector of the Wilds (2026-09-29): what's there and what to do about it —
// survey it (the cost and time, or why the teams can't reach it yet), watch
// the survey under way and speed it up, claim a supply cache or a relic, or
// see a deposit's stock, what the drones have brought home and when it refills.
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class SectorPanel
    {
        /// <summary>What a find is called, its glyph and its colour.</summary>
        public static (string name, Icon icon, UnityEngine.Color color) Look(WildsSector? sector)
        {
            if (sector == null || sector.Find == WildsFind.None) return ("Uncharted sector", Icon.Question, UiTheme.Dim);
            return sector.Find switch
            {
                WildsFind.Cache => ("Supply cache", Icon.Crate, UiTheme.Accent),
                WildsFind.Relic => ("Relic", Icon.Star, UiTheme.DarkMatter),
                _ => sector.Resource switch
                {
                    ResourceId.Gold => ("Gold seam", Icon.Coin, UiTheme.Gold),
                    ResourceId.Quartz => ("Crystal field", Icon.Crystal, UiTheme.Quartz),
                    _ => ("Helium vent", Icon.Drop, UiTheme.Helium),
                },
            };
        }

        public static void Open(GameContext ctx, int index)
        {
            var ui = UIController.Instance!;
            if (!WildsLayout.Valid(index)) return;
            var (blocker, content, footer) = Widgets.ModalPanelFooter($"SECTOR {WildsLayout.Name(index)}", ui.CloseModal, 0f);

            var head = Widgets.HBox();
            head.style.marginTop = 4;
            var glyph = Icons.Make(Icon.Question, 40f, UiTheme.Dim);
            head.Add(glyph);
            var names = new VisualElement();
            names.style.marginLeft = 12;
            names.style.flexShrink = 1f;
            var title = Widgets.Heading("", 14, UiTheme.Text, 1.6f);
            names.Add(title);
            var where = Widgets.Text("", 11, UiTheme.Dim);
            where.style.marginTop = 2;
            names.Add(where);
            head.Add(names);
            content.Add(head);

            var body = new VisualElement();
            content.Add(body);

            string cache = "";
            void Refresh()
            {
                var state = ctx.State!;
                var sector = WildsSystem.Sector(state, index);
                bool surveying = state.Wilds.Surveying == index;
                int left = WildsSystem.SurveyLeft(state);
                long stock = sector?.StockMilli ?? 0;
                string key = $"{sector?.Find}|{sector?.Claimed}|{surveying}|{(surveying ? left : 0)}|{stock / 1000}" +
                             $"|{sector?.RefillTick}|{state.Wilds.Surveying}|{WildsSystem.Reachable(state, index)}";
                if (key == cache) return;
                cache = key;

                var (name, icon, color) = Look(sector);
                glyph.Icon = surveying ? Icon.Compass : icon;
                glyph.Color = surveying ? UiTheme.Magenta : color;
                title.text = (surveying ? "Surveying…" : name).ToUpperInvariant();
                title.style.color = surveying ? UiTheme.Magenta : color;
                var (lat, lon, _) = WildsLayout.Place(index);
                int ring = WildsLayout.RingOf(index);
                where.text = $"Ring {(char)('A' + ring)} · {Math.Abs(lat):0}° south, {Math.Abs(lon):0}° {(lon < 0 ? "west" : "east")}";

                body.Clear();
                footer.Clear();
                if (surveying) ShowSurvey(ctx, index, body, footer, left);
                else if (sector == null || sector.Find == WildsFind.None) ShowFog(ctx, index, body, footer);
                else if (sector.Find == WildsFind.Deposit) ShowDeposit(state, sector, body, footer);
                else ShowFind(ctx, sector, body, footer);
            }

            Refresh();
            ui.OpenModal(blocker, Refresh);
        }

        static Label Line(VisualElement parent, string text, int size, UnityEngine.Color color, float marginTop = 8)
        {
            var label = Widgets.Text(text, size, color);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = marginTop;
            parent.Add(label);
            return label;
        }

        static VisualElement Amounts(ResourceBag milli, int darkMatter = 0)
        {
            var row = Widgets.HBox();
            row.style.marginTop = 6;
            row.style.flexWrap = Wrap.Wrap;
            void Part(Icon icon, long amount, UnityEngine.Color color)
            {
                if (amount <= 0) return;
                var part = Widgets.IconText(icon, UiTheme.FmtAmount(amount), 13, color, bold: true);
                part.style.marginRight = 14;
                row.Add(part);
            }
            Part(Icon.Coin, milli.Gold, UiTheme.Gold);
            Part(Icon.Crystal, milli.Quartz, UiTheme.Quartz);
            Part(Icon.Drop, milli.Helium, UiTheme.Helium);
            if (darkMatter > 0)
            {
                var dm = Widgets.IconText(Icon.Star, $"{darkMatter:N0} Dark Matter", 13, UiTheme.DarkMatter, bold: true);
                row.Add(dm);
            }
            return row;
        }

        static Button FooterButton(VisualElement footer, Button b)
        {
            b.style.height = 42;
            footer.Add(b);
            return b;
        }

        static void ShowFog(GameContext ctx, int index, VisualElement body, VisualElement footer)
        {
            var state = ctx.State!;
            var ui = UIController.Instance!;
            Line(body, "Under survey fog. A survey finds a deposit for the drones to harvest, a supply cache or a relic.",
                12, UiTheme.Text);
            if (!WildsSystem.Reachable(state, index))
            {
                Line(body, "Out of reach: chart a sector next to it, in the ring to the north, first.", 12, UiTheme.Bad);
                FooterButton(footer, Widgets.TextButton("CLOSE", ui.CloseModal, 12));
                return;
            }
            var cost = WildsSystem.SurveyCost(index);
            var costHead = Widgets.Heading("SURVEY", 9, UiTheme.Magenta, 2f);
            costHead.style.marginTop = 12;
            body.Add(costHead);
            body.Add(Amounts(cost));
            Line(body, $"Takes {UiTheme.FmtDuration(WildsSystem.SurveySeconds(ctx.State!, index))}", 11, UiTheme.Dim, 4);
            var check = WildsSystem.CheckSurvey(state, index);
            if (!check.Ok) Line(body, check.Reason ?? "", 11, UiTheme.Bad, 6);
            var go = FooterButton(footer, Widgets.Primary(Widgets.IconButton(Icon.Compass, "SURVEY", () =>
            {
                var res = WildsSystem.StartSurvey(ctx.State!, index);
                if (res.Ok)
                {
                    GameAudio.Feedback(Sfx.Confirm, Haptic.Light);
                    ui.Toast($"Surveying {WildsLayout.Name(index)} · {UiTheme.FmtDuration(WildsSystem.SurveySeconds(ctx.State!, index))}",
                        Icon.Compass, UiTheme.Magenta);
                }
                else
                {
                    GameAudio.Feedback(Sfx.Error, Haptic.Error);
                    ui.Toast(res.Reason ?? "Can't survey right now", Icon.Warning, UiTheme.Bad);
                }
            }, 12), UiTheme.Magenta));
            Widgets.SetButtonEnabled(go, check.Ok);
        }

        static void ShowSurvey(GameContext ctx, int index, VisualElement body, VisualElement footer, int left)
        {
            var ui = UIController.Instance!;
            int total = Math.Max(1, WildsSystem.SurveySeconds(ctx.State!, index));
            var (bar, fill, label) = Widgets.ProgressBar(18f);
            bar.style.marginTop = 12;
            fill.style.width = Length.Percent(100f * (1f - left / (float)total));
            fill.style.backgroundColor = UiTheme.Magenta;
            label.text = $"{UiTheme.FmtDuration(left)} left";
            body.Add(bar);
            Line(body, "The survey team is charting it. You'll hear what they find.", 11, UiTheme.Dim);
            FooterButton(footer, Widgets.IconButton(Icon.Bolt, "SPEED UP", () =>
                SpeedUpPanel.Open(ctx, $"Survey {WildsLayout.Name(index)}", "",
                    remainingSec: () => WildsSystem.SurveyLeft(ctx.State!),
                    apply: cut => WildsSystem.SpeedUpSurvey(ctx.State!, cut >= int.MaxValue ? int.MaxValue : (int)cut),
                    onBack: () => Open(ctx, index)), 12));
        }

        static void ShowDeposit(GameState state, WildsSector sector, VisualElement body, VisualElement footer)
        {
            var ui = UIController.Instance!;
            var (_, _, color) = Look(sector);
            string res = sector.Resource switch { ResourceId.Gold => "gold", ResourceId.Quartz => "quartz", _ => "helium" };
            var (bar, fill, label) = Widgets.ProgressBar(18f);
            bar.style.marginTop = 12;
            float pct = sector.MaxMilli > 0 ? sector.StockMilli / (float)sector.MaxMilli : 0f;
            fill.style.width = Length.Percent(100f * pct);
            fill.style.backgroundColor = color;
            label.text = $"{UiTheme.FmtAmount(sector.StockMilli)} of {UiTheme.FmtAmount(sector.MaxMilli)} {res}";
            body.Add(bar);
            if (sector.StockMilli <= 0)
                Line(body, $"Dry. It fills back up in {UiTheme.FmtDuration(Math.Max(0, sector.RefillTick - state.Tick))}.",
                    12, UiTheme.Energy);
            else
            {
                int deposits = Math.Max(1, WildsSystem.ActiveDeposits(state));
                long share = WildsSystem.HaulPerHourMilli(state) / deposits;
                int drones = WildsSystem.Drones(state);
                Line(body, $"Your {drones} harvester drone{(drones == 1 ? "" : "s")} work{(drones == 1 ? "s" : "")} " +
                           $"{deposits} deposit{(deposits == 1 ? "" : "s")}: about {UiTheme.FmtAmount(share)} {res} an hour from here.",
                    12, UiTheme.Text);
            }
            if (sector.HarvestedMilli > 0)
                Line(body, $"Brought home so far: {UiTheme.FmtAmount(sector.HarvestedMilli)} {res}", 11, UiTheme.Dim, 4);
            if (WildsSystem.FactoryLevel(state) < 1)
                Line(body, "More drones, with bigger holds, come from the Drone Factory (the Frontier, Command Center 10).",
                    11, UiTheme.Dim);
            FooterButton(footer, Widgets.TextButton("CLOSE", ui.CloseModal, 12));
        }

        static void ShowFind(GameContext ctx, WildsSector sector, VisualElement body, VisualElement footer)
        {
            var state = ctx.State!;
            var ui = UIController.Instance!;
            bool relic = sector.Find == WildsFind.Relic;
            Line(body, relic
                    ? "A relic of whoever was here before: a Dark Matter core, still humming."
                    : "A supply cache, left by a survey crew or a wreck long ago.",
                12, UiTheme.Text);
            if (sector.Claimed)
            {
                Line(body, $"Claimed. The Wilds shift here in {UiTheme.FmtDuration(Math.Max(0, sector.FogTick - state.Tick))}: " +
                           "the fog rolls back in, and a new survey finds something new.", 12, UiTheme.Dim);
                FooterButton(footer, Widgets.TextButton("CLOSE", ui.CloseModal, 12));
                return;
            }
            body.Add(Amounts(sector.Reward, sector.RewardDM));
            int index = sector.Index;
            FooterButton(footer, Widgets.Primary(Widgets.IconButton(Icon.Check, "CLAIM", () =>
            {
                var s = ctx.State!;
                var reward = sector.Reward.Clone();
                int dm = sector.RewardDM;
                if (!WildsSystem.Claim(s, index, out var found).Ok) return;
                GameAudio.Feedback(Sfx.Coins, Haptic.Success);
                // The Citadel (2026-09-30): the relic itself comes home for the Relic Vault.
                string piece = found is { } k ? $" and a {Relics.Def(k).Name} for your Relic Vault" : "";
                ui.Toast(dm > 0 ? $"Relic claimed: +{dm} Dark Matter{piece}" : $"Cache claimed: +{UiTheme.FmtAmount(reward.Total)}",
                    relic ? Icon.Star : Icon.Crate, UiTheme.Good);
            }, 12)));
        }
    }
}
