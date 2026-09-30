// THE PIRATE DREADNOUGHT (MORE › BOSS, the base-screen chip, or tap it on the
// map): its hull and time left, the clan damage race, what it pays, and the
// strike planner — pick ships, see the exact damage and losses, launch. While
// no dreadnought is in the galaxy it says when the next one comes and how the
// last one ended.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class BossPanel
    {
        /// <summary>"64% hull · leaves in 13h" (or when the next one comes).</summary>
        public static string Status(GameState state, Sim.Bots.BotGalaxy galaxy)
        {
            var boss = galaxy.Boss;
            if (!boss.Active)
                return $"Next one in {UiTheme.FmtLong(Math.Max(0, boss.NextVisitTick - state.Tick))}";
            if (!BossSystem.Revealed(state, galaxy))
                return $"cloaked for {UiTheme.FmtLong(boss.ArrivedTick + BossSystem.StealthCloakSec - state.Tick)} · leaves in " +
                       UiTheme.FmtLong(Math.Max(0, boss.LeavesTick - state.Tick));
            return $"{Pct(BossSystem.HullShare(boss))} hull · leaves in {UiTheme.FmtLong(Math.Max(0, boss.LeavesTick - state.Tick))}";
        }

        public static void OpenCallout(GameContext ctx)
        {
            var ui = UIController.Instance!;
            if (ctx.State == null || ctx.Bots == null) return;
            Label? sub = null;
            var strip = CalloutChrome.Strip(BossSystem.Name(ctx.Bots.Boss.Variant).ToUpperInvariant(), Status(ctx.State, ctx.Bots), CalloutChrome.Close, l => sub = l,
                CalloutChrome.Act("STRIKE", () => { CalloutChrome.Close(); Open(ctx); }, icon: Icon.Swords));
            strip.schedule.Execute(() => { if (sub != null && ctx.State != null && ctx.Bots != null) sub.text = Status(ctx.State, ctx.Bots); }).Every(1000);
            ui.OpenCalloutElement(strip);
        }

        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            if (ctx.State == null || ctx.Bots == null) return;
            var state = ctx.State;
            var galaxy = ctx.Bots;
            var boss = galaxy.Boss;
            var (blocker, content, footer) = Widgets.ModalPanelFooter(
                boss.Active ? BossSystem.Name(boss.Variant).ToUpperInvariant() : "PIRATE DREADNOUGHT", ui.CloseModal, 84f);
            if (boss.Active)
            {
                // The visit's variant (rival events, 2026-09-30).
                var trait = Note(BossSystem.Trait(boss.Variant), 0);
                trait.style.marginBottom = 8;
                content.Add(trait);
            }

            if (!boss.Active)
            {
                var none = Widgets.IconText(Icon.Warning, "No dreadnought in the galaxy right now", 13, UiTheme.Dim, bold: true);
                content.Add(none);
                var next = Note("", 6);
                content.Add(next);
                void RefreshNext() => next.text =
                    $"The next one drops out of hyperspace in {UiTheme.FmtLong(Math.Max(0, boss.NextVisitTick - ctx.State!.Tick))}.";
                RefreshNext();
                blocker.schedule.Execute(RefreshNext).Every(1000);
                if (boss.Visit > 0)
                {
                    var head = Widgets.Text("LAST VISIT", 10, UiTheme.Dim, bold: true);
                    head.style.marginTop = 12;
                    content.Add(head);
                    string how = boss.LastKilled ? "It was destroyed" : "It escaped";
                    string top = boss.LastTopClan != null ? $" — {boss.LastTopClan} dealt the most damage" : "";
                    content.Add(Note($"{how}{top}.", 4));
                    content.Add(Note(boss.LastYourDamage > 0
                        ? $"You dealt {boss.LastYourDamage:N0} damage (#{boss.LastYourRank}) and earned {boss.LastRewardDM} Dark Matter."
                        : "You didn't strike it.", 4));
                }
                content.Add(HowItWorks());
                footer.Add(Widgets.TextButton("CLOSE", ui.CloseModal, 12));
                ui.OpenModal(blocker);
                return;
            }

            // ---- the hull ----
            var hullHead = Widgets.HBox(Justify.SpaceBetween);
            var hullText = Widgets.Text("", 13, UiTheme.Bad, bold: true);
            hullHead.Add(hullText);
            var leaves = Widgets.Text("", 11, UiTheme.Dim);
            hullHead.Add(leaves);
            content.Add(hullHead);
            var (bar, fill, barLabel) = Widgets.ProgressBar(16f);
            bar.style.marginTop = 4;
            fill.style.backgroundColor = UiTheme.Bad;
            content.Add(bar);
            double dist = TileXY.Distance(state.HomeTile, boss.Tile);
            content.Add(Note($"At {boss.Tile.X}, {boss.Tile.Y} · {dist:N0} tiles from your colony. Its guns fire {boss.Cannon:N0} " +
                "a round at a striking fleet — up to half as much again as its hull fails — straight through ship shields.", 6));

            // ---- the race ----
            var raceHead = Widgets.Text("CLAN DAMAGE RACE", 10, UiTheme.Dim, bold: true);
            raceHead.style.marginTop = 12;
            content.Add(raceHead);
            var raceBox = new VisualElement();
            content.Add(raceBox);
            var yours = Widgets.Text("", 11, UiTheme.Accent, bold: true);
            yours.style.marginTop = 4;
            yours.style.whiteSpace = WhiteSpace.Normal;
            content.Add(yours);
            content.Add(Note($"When it breaks apart, {BossSystem.PoolDMKilled:N0} Dark Matter is shared out by damage — the final blow " +
                $"earns {BossSystem.FinalBlowDM} more, and the top clan's members {BossSystem.TopClanDM} more. If it escapes, " +
                $"{BossSystem.PoolDMEscaped}. Survivors bring back salvage for the damage they did.", 6));

            string raceKey = "";
            void RefreshBoss()
            {
                var s = ctx.State!;
                var g = ctx.Bots!;
                var b = g.Boss;
                if (!b.Active) { hullText.text = "IT'S GONE"; return; }
                double share = BossSystem.HullShare(b);
                hullText.text = $"HULL {Pct(share)}";
                leaves.text = $"leaves in {UiTheme.FmtLong(Math.Max(0, b.LeavesTick - s.Tick))}";
                fill.style.width = Length.Percent((float)(share * 100));
                barLabel.text = $"{b.Hp:N0} / {b.MaxHp:N0}";

                long total = 0;
                foreach (var v in b.Damage.Values) total += v;
                string key = $"{total}|{s.ClanId}";
                if (key == raceKey) return;
                raceKey = key;
                raceBox.Clear();
                var race = BossSystem.ClanRace(s, g);
                if (race.Count == 0) raceBox.Add(Note("Nobody has struck it yet.", 4));
                for (int i = 0; i < Math.Min(5, race.Count); i++)
                {
                    var r = race[i];
                    var line = Widgets.HBox(Justify.SpaceBetween);
                    line.style.marginTop = 3;
                    var name = Widgets.Text($"#{i + 1} {r.Name}", 11, r.Yours ? UiTheme.Good : UiTheme.Text, bold: r.Yours);
                    name.style.flexShrink = 1f;
                    line.Add(name);
                    line.Add(Widgets.Text($"{UiTheme.FmtCount(r.Damage)} ({Pct(total > 0 ? r.Damage / (double)total : 0)})", 11,
                        r.Yours ? UiTheme.Good : UiTheme.Dim));
                    raceBox.Add(line);
                }
                var (mine, rank, of) = BossSystem.YourStanding(g);
                yours.text = mine > 0
                    ? $"You: {mine:N0} damage · #{rank} of {of} · {Pct(total > 0 ? mine / (double)total : 0)} of the reward so far"
                    : "You haven't struck it yet.";
            }
            RefreshBoss();

            // ---- the strike ----
            var strikeHead = Widgets.Text("SELECT YOUR STRIKE FLEET", 10, UiTheme.Dim, bold: true);
            strikeHead.style.marginTop = 12;
            content.Add(strikeHead);
            var preview = Note("", 6);
            preview.style.color = UiTheme.Accent;
            var status = Note("", 4);
            status.style.color = UiTheme.Bad;
            var forecast = new VisualElement();
            forecast.style.marginTop = 8;
            forecast.style.paddingLeft = 10;
            forecast.style.paddingRight = 10;
            forecast.style.paddingTop = 6;
            forecast.style.paddingBottom = 6;
            Holo.Frame(forecast, UiTheme.PanelLight, UiTheme.Stroke, 9f, 1f);
            var fcHead = Widgets.IconText(Icon.Swords, "FORECAST", 12, UiTheme.Energy, bold: true);
            var fcLine = Note("", 2);
            fcLine.style.color = UiTheme.Text;
            forecast.Add(fcHead);
            forecast.Add(fcLine);
            Button? launch = null;
            FleetPicker? picker = null;
            picker = new FleetPicker(content, state, () => Refresh());
            if (picker.Empty)
            {
                footer.Add(Widgets.TextButton("CLOSE", ui.CloseModal, 12));
                blocker.schedule.Execute(RefreshBoss).Every(1000);
                ui.OpenModal(blocker);
                return;
            }
            content.Add(preview);
            content.Add(status);
            content.Add(forecast);

            void Refresh()
            {
                var s = ctx.State!;
                var g = ctx.Bots!;
                var fleet = picker!.Fleet();
                int count = MarchSystem.FleetCount(fleet);
                if (count < 1)
                {
                    preview.text = "Select ships to plan the strike.";
                    status.text = "";
                    forecast.style.display = DisplayStyle.None;
                    if (launch != null) Widgets.SetButtonEnabled(launch, false);
                    return;
                }
                var can = BossSystem.CanStrike(s, g, fleet);
                var p = MarchSystem.PreviewMarch(s, fleet, g.Boss.Tile);
                int flight = MarchSystem.FlightSeconds(s, fleet, g.Boss.Tile);
                preview.text = p.Ok ? $"Arrives in {UiTheme.FmtDuration(flight)} · helium {UiTheme.FmtAmount(p.HeliumCost)}" : "";
                status.text = !can.Ok ? can.Reason ?? "" : p.Ok ? "" : p.Reason ?? "Cannot launch";
                var f = BossSystem.Forecast(s, g, fleet);
                forecast.style.display = DisplayStyle.Flex;
                int lost = 0;
                foreach (var v in f.Losses.Values) lost += v;
                string losses = lost == 0 ? "no losses expected" : $"you lose {lost:N0} of {count:N0}";
                fcLine.text = f.Killed
                    ? $"Breaks it apart! Deals the last {f.Damage:N0} damage · {losses}. (If others strike first, less will be left.)"
                    : $"Deals {f.Damage:N0} damage ({Pct(g.Boss.MaxHp > 0 ? f.Damage / (double)g.Boss.MaxHp : 0)} of its hull) in " +
                      $"{f.RoundsFought} round{(f.RoundsFought == 1 ? "" : "s")} · {losses}.";
                if (launch != null) Widgets.SetButtonEnabled(launch, can.Ok && p.Ok);
            }

            launch = Widgets.IconButton(Icon.Swords, "LAUNCH STRIKE", () =>
            {
                var res = BossSystem.SendStrike(ctx.State!, ctx.Bots!, picker!.Fleet(), out _);
                if (!res.Ok) { ui.Toast(res.Reason ?? "Cannot launch", Icon.Info, UiTheme.Bad); return; }
                GameAudio.Feedback(Sfx.Launch, Haptic.Medium);
                LocalBootstrap.RequestSync();
                ui.Toast("Strike on the dreadnought launched", Icon.Swords, UiTheme.Energy);
                ui.CloseModal();
            }, 14);
            launch.style.height = 42;
            footer.Add(launch);
            Refresh();
            blocker.schedule.Execute(() => { RefreshBoss(); Refresh(); }).Every(1000);
            ui.OpenModal(blocker);
        }

        static VisualElement HowItWorks()
        {
            var box = new VisualElement();
            var head = Widgets.Text("HOW IT WORKS", 10, UiTheme.Dim, bold: true);
            head.style.marginTop = 12;
            box.Add(head);
            box.Add(Note("Every few days a Pirate Dreadnought drops in somewhere in the middle rings and stays for a day. " +
                "Its hull is shared by the whole galaxy: every commander's strike wears it down, and the clans race to deal " +
                $"the most damage. When it breaks apart, {BossSystem.PoolDMKilled:N0} Dark Matter is shared out by damage. " +
                "They come in turn as the classic Dreadnought, a Carrier (harder-hitting, lighter hull), a Siege Dreadnought " +
                "(shells nearby colonies every 4 hours) and a Stealth Dreadnought (hidden for its first 8 hours).", 4));
            return box;
        }

        /// <summary>"64%" (the P0 format adds a culture-dependent space).</summary>
        static string Pct(double share) => $"{Math.Round(share * 100):0}%";

        static Label Note(string text, float marginTop)
        {
            var l = Widgets.Text(text, 11, UiTheme.Dim);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = marginTop;
            return l;
        }
    }
}
