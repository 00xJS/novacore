// WHILE YOU WERE AWAY — the morning-after report. Waking up to last night's
// wars is the game's pitch, but the catch-up used to surface only a resources
// toast. LocalBootstrap opens this after a cold start or a background resume
// when something actually happened (see OfflineDebrief).
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class DebriefPanel
    {
        public static void Open(GameContext ctx, OfflineDebrief d)
        {
            var ui = UIController.Instance!;
            var (blocker, content, footer) = Widgets.ModalPanelFooter("WHILE YOU WERE AWAY", ui.CloseModal, 0f);

            var span = Widgets.Text($"{UiTheme.FmtDuration(d.ElapsedSec)} of galaxy time passed.", 11, UiTheme.Dim);
            span.style.whiteSpace = WhiteSpace.Normal;
            content.Add(span);

            void Section(string title)
            {
                var header = Widgets.Text(title, 10, UiTheme.Dim, bold: true);
                header.style.marginTop = 12;
                content.Add(header);
            }

            void Line(Icon icon, string text, Color color)
            {
                var row = Widgets.IconText(icon, text, 12, color);
                row.style.marginTop = 5;
                var label = row.Q<Label>("text");
                if (label != null) label.style.whiteSpace = WhiteSpace.Normal;
                content.Add(row);
            }

            static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

            if (d.RaidsSuffered + d.RaidsRepelled + d.RaidsDeflected + d.SpyScans > 0)
            {
                Section("YOUR COLONY");
                if (d.RaidsSuffered > 0)
                    Line(Icon.Swords, d.LootLostMilli > 0
                        ? $"Raided {d.RaidsSuffered}× — lost {UiTheme.FmtAmount(d.LootLostMilli)}"
                        : $"Raided {d.RaidsSuffered}×", UiTheme.Bad);
                if (d.RaidsRepelled > 0) Line(Icon.Check, $"{N(d.RaidsRepelled, "raid", "raids")} repelled", UiTheme.Good);
                if (d.RaidsDeflected > 0)
                    Line(Icon.Ring, $"{N(d.RaidsDeflected, "raid", "raids")} broke on your Aegis Shield", UiTheme.Accent);
                if (d.SpyScans > 0) Line(Icon.Eye, $"Spy probes swept your colony {d.SpyScans}×", UiTheme.DarkMatter);
            }

            if (d.FleetsHome + d.BattlesWon + d.BattlesLost + d.SpyReports + d.BossStrikes > 0)
            {
                Section("YOUR FLEETS");
                if (d.BossStrikes > 0)
                    Line(Icon.Warning, $"{N(d.BossStrikes, "strike", "strikes")} on the Pirate Dreadnought — {d.BossDamage:N0} damage",
                        UiTheme.Energy);
                if (d.FleetsHome > 0)
                    Line(Icon.ArrowDown, d.CargoHomeMilli > 0
                        ? $"{N(d.FleetsHome, "fleet", "fleets")} home with {UiTheme.FmtAmount(d.CargoHomeMilli)} cargo"
                        : $"{N(d.FleetsHome, "fleet", "fleets")} home", UiTheme.Text);
                if (d.BattlesWon > 0) Line(Icon.Swords, $"{N(d.BattlesWon, "battle", "battles")} won", UiTheme.Good);
                if (d.BattlesLost > 0) Line(Icon.Swords, $"{N(d.BattlesLost, "battle", "battles")} lost", UiTheme.Bad);
                if (d.SpyReports > 0) Line(Icon.Eye, $"{N(d.SpyReports, "spy report", "spy reports")} in", UiTheme.Accent);
            }

            if (d.Upgrades + d.Research + d.ShipsBuilt > 0)
            {
                Section("COLONY WORK");
                if (d.Upgrades > 0) Line(Icon.Check, $"{N(d.Upgrades, "upgrade", "upgrades")} finished", UiTheme.Text);
                if (d.Research > 0) Line(Icon.Check, $"{N(d.Research, "research level", "research levels")} completed", UiTheme.Text);
                if (d.ShipsBuilt > 0) Line(Icon.Check, $"{N(d.ShipsBuilt, "ship", "ships")} built", UiTheme.Text);
            }

            if (d.Achievements.Count + d.SeasonsEnded.Count + d.SupplyRuns + d.ClanWarsWon + d.LevelsGained > 0
                || d.BossResult != null)
            {
                Section("PROGRESS");
                if (d.BossResult is { } boss)
                    Line(Icon.Trophy, $"The Pirate Dreadnought {(boss.Killed ? "was destroyed" : "escaped")} — you placed " +
                        $"#{boss.Rank} of {boss.Of} (+{boss.RewardDM} DM)", UiTheme.Energy);
                if (d.LevelsGained > 0)
                {
                    int dm = 0;
                    for (int l = d.LevelAfter - d.LevelsGained + 1; l <= d.LevelAfter; l++)
                        dm += CommanderSystem.LevelReward(l).darkMatter;
                    Line(Icon.Star, $"Commander level {d.LevelAfter} (+{N(d.LevelsGained, "skill point", "skill points")}, " +
                        $"+{dm} DM) — spend points in your profile › SKILLS", UiTheme.Energy);
                }
                foreach (var rec in d.SeasonsEnded)
                    Line(Icon.Trophy, $"Season {rec.Season} ended — you placed #{rec.Rank} of {rec.Of} (+{rec.RewardDM} DM)",
                        UiTheme.Energy);
                foreach (var a in d.Achievements)
                    Line(Icon.Trophy, a.Title != null
                        ? $"Achievement: {a.Name} (+{a.RewardDM} DM, title \"{a.Title}\")"
                        : $"Achievement: {a.Name} (+{a.RewardDM} DM)", UiTheme.Energy);
                if (d.ClanWarsWon > 0)
                    Line(Icon.Swords, $"Your clan won {N(d.ClanWarsWon, "war", "wars")} (+{d.ClanWarsWon * ClanSystem.WarWinRewardDM} DM)",
                        UiTheme.Good);
                if (d.SupplyRuns > 0)
                    Line(Icon.Pact, $"{N(d.SupplyRuns, "supply run", "supply runs")} from your clan — collect in MORE › CLAN",
                        UiTheme.Good);
            }

            var gained = d.Gained;
            if (gained.Gold > 0 || gained.Quartz > 0 || gained.Helium > 0)
            {
                Section("PRODUCTION");
                var row = Widgets.HBox(Justify.SpaceBetween);
                row.style.marginTop = 5;
                if (gained.Gold > 0) row.Add(Widgets.Text($"+{UiTheme.FmtAmount(gained.Gold)} gold", 12, UiTheme.Gold, bold: true));
                if (gained.Quartz > 0) row.Add(Widgets.Text($"+{UiTheme.FmtAmount(gained.Quartz)} quartz", 12, UiTheme.Quartz, bold: true));
                if (gained.Helium > 0) row.Add(Widgets.Text($"+{UiTheme.FmtAmount(gained.Helium)} helium", 12, UiTheme.Helium, bold: true));
                content.Add(row);
            }

            Section("GALAXY RANK");
            if (d.RankAfter < d.RankBefore)
                Line(Icon.ArrowUp, $"#{d.RankBefore} → #{d.RankAfter}  (up {d.RankBefore - d.RankAfter})", UiTheme.Good);
            else if (d.RankAfter > d.RankBefore)
                Line(Icon.ArrowDown, $"#{d.RankBefore} → #{d.RankAfter}  (down {d.RankAfter - d.RankBefore})", UiTheme.Bad);
            else
                Line(Icon.Chart, $"Holding at #{d.RankAfter}", UiTheme.Dim);

            var buttons = Widgets.HBox(Justify.SpaceBetween);
            if (d.NewMail > 0)
            {
                var mail = Widgets.TextButton($"MAIL ({d.NewMail} NEW)", () => ui.OpenMailbox(), 12);
                mail.style.width = Length.Percent(48f);
                buttons.Add(mail);
            }
            var ok = Widgets.TextButton("CONTINUE", ui.CloseModal, 12);
            Widgets.Primary(ok);
            ok.style.width = Length.Percent(d.NewMail > 0 ? 48f : 100f);
            Widgets.SetBorder(ok, UiTheme.Accent, 1.5f);
            buttons.Add(ok);
            footer.Add(buttons);

            ui.OpenModal(blocker);
        }
    }
}
