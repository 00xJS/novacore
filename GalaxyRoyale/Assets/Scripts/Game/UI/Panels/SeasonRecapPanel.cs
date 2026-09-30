// The season recap (2026-09-30): when a week's season is settled, a card with
// where you finished, what it paid, who won it and what you did along the way.
// It opens by itself when a season ends mid-session; EVENTS & SEASON › PAST
// SEASONS reopens any of them.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Sim;

namespace GalaxyRoyale.Game.UI
{
    public static class SeasonRecapPanel
    {
        /// <summary>SeasonSystem.Tally keys, in the order the recap lists them.</summary>
        static readonly (string key, Icon icon, string one, string many)[] Lines =
        {
            ("raids", Icon.Swords, "raid won", "raids won"),
            ("defenses", Icon.Shield, "defense held", "defenses held"),
            ("camps", Icon.Target, "pirate camp cleared", "pirate camps cleared"),
            ("loot", Icon.Crate, "resources plundered", "resources plundered"),
            ("gathered", Icon.Drop, "resources gathered", "resources gathered"),
            ("ships", Icon.Ship, "ship built", "ships built"),
            ("upgrades", Icon.Chart, "upgrade finished", "upgrades finished"),
            ("research", Icon.Bolt, "research finished", "research finished"),
            ("events", Icon.Star, "galaxy event won", "galaxy events won"),
            ("boss", Icon.Warning, "dreadnought strike", "dreadnought strikes"),
            ("cores", Icon.Trophy, "Core seizure", "Core seizures"),
            ("expeditions", Icon.Compass, "expedition home", "expeditions home"),
            ("nemeses", Icon.Swords, "nemesis broken", "nemeses broken"),
        };

        public static void Open(GameContext ctx, SeasonRecord rec, bool justEnded = false)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel($"SEASON {rec.Season} RECAP", ui.CloseModal, 0f);

            // ---- the finish ----
            Color medal = rec.Rank switch
            {
                1 => UiTheme.Energy,
                <= 3 => UiTheme.Accent,
                <= 10 => UiTheme.Good,
                _ => UiTheme.Text,
            };
            var place = Widgets.Heading($"#{rec.Rank}", 40, medal, 2f);
            place.style.alignSelf = Align.Center;
            place.style.marginTop = 4;
            content.Add(place);
            var of = Widgets.Text($"of {rec.Of:N0} commanders", 12, UiTheme.Dim);
            of.style.alignSelf = Align.Center;
            content.Add(of);
            var verdict = Widgets.Text(rec.Rank switch
            {
                1 => "Season champion. The whole galaxy watched you rise.",
                <= 3 => "On the podium. The champion felt you breathing down their neck.",
                <= 10 => "Top ten. The big names know yours now.",
                <= 50 => "Top fifty, out of the whole galaxy.",
                _ => "Every season is a fresh race. The next one starts now.",
            }, 12, UiTheme.Text);
            verdict.style.whiteSpace = WhiteSpace.Normal;
            verdict.style.unityTextAlign = TextAnchor.MiddleCenter;
            verdict.style.marginTop = 8;
            content.Add(verdict);

            var pay = Widgets.Row();
            pay.style.marginTop = 12;
            var payBox = Widgets.HBox(Justify.SpaceBetween);
            payBox.Add(Widgets.Text($"Might gained: +{rec.Gain:N0}", 12, UiTheme.Text, bold: true));
            payBox.Add(Widgets.Text($"+{rec.RewardDM:N0} DM", 12, UiTheme.DarkMatter, bold: true));
            pay.Add(payBox);
            content.Add(pay);

            if (rec.Champion.Length > 0 && rec.Rank != 1)
            {
                var champ = Widgets.Text($"Champion: {rec.Champion} (+{rec.ChampionGain:N0} might)", 11, UiTheme.Dim);
                champ.style.marginTop = 6;
                champ.style.whiteSpace = WhiteSpace.Normal;
                content.Add(champ);
            }

            // ---- what you did ----
            var shown = new List<VisualElement>();
            foreach (var (key, icon, one, many) in Lines)
            {
                if (!rec.Highlights.TryGetValue(key, out long n) || n <= 0) continue;
                var row = Widgets.HBox();
                row.style.marginTop = 5;
                var ic = new IconElement(icon, UiTheme.Accent);
                ic.style.width = 14;
                ic.style.height = 14;
                ic.style.marginRight = 8;
                row.Add(ic);
                string count = key is "loot" or "gathered" ? UiTheme.FmtCount(n) : n.ToString("N0");
                row.Add(Widgets.Text($"{count} {(n == 1 ? one : many)}", 12, UiTheme.Text));
                shown.Add(row);
            }
            if (shown.Count > 0)
            {
                var h = Widgets.Heading("YOUR SEASON", 10, UiTheme.Accent, 1.4f);
                h.style.marginTop = 14;
                content.Add(h);
                foreach (var row in shown) content.Add(row);
            }

            var buttons = Widgets.HBox(Justify.SpaceBetween);
            buttons.style.marginTop = 16;
            var board = Widgets.IconButton(Icon.Chart, justEnded ? "NEW SEASON" : "SEASON BOARD",
                () => ui.OpenRankings(season: true), 12);
            board.style.flexGrow = 1;
            board.style.height = 38;
            board.style.marginRight = 8;
            buttons.Add(board);
            var done = Widgets.TextButton(justEnded ? "ONWARD" : "CLOSE", ui.CloseModal, 12);
            done.style.flexGrow = 1;
            done.style.height = 38;
            buttons.Add(done);
            content.Add(buttons);

            ui.OpenModal(blocker);
            if (justEnded) GameAudio.Feedback(rec.Rank <= 10 ? Sfx.Victory : Sfx.Success, Haptic.Success);
        }
    }
}
