// INTERCEPT — meet a rival fleet in flight (StrikeSystem): a raid heading for
// you or a clanmate, or a raider flying home with its plunder. Opened from a
// raid fleet on the map or an incoming radar contact. Pick your ships (and
// your clan's wings), see where and when you'd meet them, launch.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class InterceptPanel
    {
        public static void Open(GameContext ctx, bool inbound, int fleetId)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var maybeGalaxy = ctx.Bots;
            var first = maybeGalaxy != null ? StrikeSystem.Track(state, maybeGalaxy, inbound, fleetId) : null;
            if (maybeGalaxy == null || first == null)
            {
                ui.Toast("That fleet is out of reach");
                return;
            }
            BotGalaxy galaxy = maybeGalaxy;
            // Re-read every second while the screen is open (the fleet keeps flying).
            StrikeSystem.FleetTrack track = first;
            var enemy = galaxy.Find(track.BotId);
            string enemyName = enemy?.Name ?? "Unknown commander";

            var (blocker, content, footer) = Widgets.ModalPanelFooter("INTERCEPT", ui.CloseModal, 82f);

            // ---- the fleet you'd hunt ----
            var head = Widgets.HBox(Justify.SpaceBetween);
            var title = Widgets.Text($"{ClanSystem.Tagged(state, galaxy, track.BotId, enemyName)}'s fleet", 15, UiTheme.Text, bold: true);
            title.style.flexShrink = 1f;
            title.style.whiteSpace = WhiteSpace.Normal;
            head.Add(title);
            head.Add(Widgets.Text($"{MarchSystem.FleetCount(track.Ships)} SHIPS", 12, UiTheme.Energy, bold: true));
            content.Add(head);

            var victim = track.TargetId > 0 ? galaxy.Find(track.TargetId) : null;
            bool clanmateTarget = victim != null && ClanSystem.SameClanAsPlayer(state, victim);
            string Mission()
            {
                int eta = Math.Max(0, track.ArriveTick - ctx.State!.Tick);
                if (track.Leg == 1)
                    return $"Flying home with {UiTheme.FmtAmount(track.Loot.Total)} plunder · home in {UiTheme.FmtDuration(eta)}";
                if (track.TargetsCore) return $"Assaulting the Galactic Core · lands in {UiTheme.FmtDuration(eta)}";
                if (track.TargetId == 0) return $"Raiding YOUR colony · lands in {UiTheme.FmtDuration(eta)}";
                return $"Raiding {(clanmateTarget ? "your clanmate " : "")}{victim?.Name ?? "a colony"} · lands in {UiTheme.FmtDuration(eta)}";
            }
            var missionRow = Widgets.IconText(track.Leg == 1 ? Icon.Target : Icon.Swords, Mission(), 11,
                track.TargetId == 0 || clanmateTarget ? UiTheme.Bad : UiTheme.Accent, bold: true);
            var missionText = missionRow.Q<Label>("text");
            missionText.style.whiteSpace = WhiteSpace.Normal;
            missionRow.style.marginTop = 4;
            content.Add(missionRow);

            var can = StrikeSystem.CanIntercept(state, galaxy, track);
            if (!can.Ok)
            {
                content.Add(Note(can.Reason ?? "You can't intercept that fleet.", 10));
                ui.OpenModal(blocker);
                return;
            }

            // What you'd face: fleets on the map are in plain sight; an inbound
            // contact only as well as your radar reads it.
            bool known = !inbound || RadarSystem.DetailTier(RadarSystem.Level(state)) >= 4;
            if (known)
            {
                var parts = new List<string>();
                foreach (var hull in Ships.All)
                    if (track.Ships.TryGetValue(hull, out var n) && n > 0) parts.Add($"{Ships.Defs[hull].Name} ×{n}");
                content.Add(Note(string.Join(" · ", parts), 6));
            }
            else content.Add(Note("Your Radar Station can't read their fleet yet — no forecast until it can (Lv 15).", 6));

            content.Add(Note(track.Leg == 0
                ? $"Meet them on their flight path. Wipe them out and the {(track.TargetsCore ? "assault" : "raid")} is off; a close fight still turns them back."
                : "Catch them before they get home. Win, and your ships carry off as much of their plunder as they can hold.", 8));

            // ---- your fleet ----
            bool joint = false;
            var wings = new List<StrikeSystem.Wing>();
            Button? jointBtn = null;
            Label? jointNote = null;
            Button? launchBtn = null;
            var preview = Widgets.Text("", 11, UiTheme.Accent);
            preview.style.whiteSpace = WhiteSpace.Normal;
            var status = Widgets.Text("", 11, UiTheme.Bad);
            status.style.whiteSpace = WhiteSpace.Normal;
            var forecast = new ForecastView();
            FleetPicker? picker = null;

            var fleetHeader = Widgets.Text("SELECT YOUR INTERCEPT FLEET", 10, UiTheme.Dim, bold: true);
            fleetHeader.style.marginTop = 12;
            content.Add(fleetHeader);
            picker = new FleetPicker(content, state, () => Refresh());
            if (picker.Empty) { ui.OpenModal(blocker); return; }

            if (state.ClanId != 0)
            {
                jointBtn = Widgets.IconButton(Icon.Pact, "JOINT STRIKE", () => { joint = !joint; Refresh(); }, 11);
                jointBtn.style.marginTop = 10;
                content.Add(jointBtn);
                jointNote = Widgets.Text("", 10, UiTheme.Dim);
                jointNote.style.whiteSpace = WhiteSpace.Normal;
                jointNote.style.marginTop = 4;
                content.Add(jointNote);
            }
            content.Add(preview);
            content.Add(status);
            content.Add(forecast.Root);

            void Refresh()
            {
                // The hunted fleet keeps flying: re-read it (it may be gone or have turned).
                var live = StrikeSystem.Track(ctx.State!, galaxy, inbound, fleetId);
                if (live == null)
                {
                    missionText.text = "That fleet is gone — landed, broken or out of reach.";
                    preview.text = "";
                    status.text = "";
                    forecast.Hide();
                    if (jointBtn != null) Widgets.SetButtonEnabled(jointBtn, false);
                    if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, false);
                    return;
                }
                track = live;
                missionText.text = Mission();
                var fleet = picker!.Fleet();
                var plan = StrikeSystem.PlanIntercept(state, track, fleet);
                wings = plan.Ok ? StrikeSystem.StrikeWings(state, galaxy, plan.Point, plan.EngageTick, track.BotId)
                    : new List<StrikeSystem.Wing>();
                if (jointBtn != null && jointNote != null)
                {
                    int wingShips = 0;
                    foreach (var w in wings) wingShips += MarchSystem.FleetCount(w.Ships);
                    bool on = joint && wings.Count > 0;
                    Widgets.SetCaption(jointBtn, !plan.Ok ? "JOINT STRIKE"
                        : wings.Count == 0 ? "JOINT STRIKE — NO CLANMATES CAN MAKE IT"
                        : on ? $"JOINT STRIKE ON: {wings.Count} CLANMATE{(wings.Count == 1 ? "" : "S")} · +{wingShips} WARSHIPS"
                        : $"JOINT STRIKE ({wings.Count} ready · +{wingShips} warships)");
                    Widgets.SetButtonHighlight(jointBtn, on);
                    Widgets.SetButtonEnabled(jointBtn, wings.Count > 0);
                    jointNote.text = !plan.Ok ? "Clanmates can join once your fleet can reach the intercept point."
                        : wings.Count > 0 ? $"{string.Join(", ", wings.ConvertAll(w => w.Bot.Name))} can meet the fleet with you."
                        : "Clanmates join when they can reach the intercept point in time.";
                }

                if (MarchSystem.FleetCount(fleet) < 1)
                {
                    preview.text = "Select ships to plan the intercept.";
                    status.text = "";
                    forecast.Hide();
                    if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, false);
                    return;
                }
                if (plan.Ok)
                {
                    status.text = "";
                    var at = StrikeSystem.TileOf(plan.Point);
                    preview.text = $"Intercept in {UiTheme.FmtDuration(plan.EngageTick - state.Tick)} at {at.X}, {at.Y} · helium {UiTheme.FmtAmount(plan.HeliumCost)}";
                }
                else
                {
                    preview.text = "";
                    status.text = plan.Reason ?? "You can't catch them";
                }
                if (known)
                {
                    var attackers = new List<Dictionary<HullId, int>> { fleet };
                    if (joint) foreach (var w in wings) attackers.Add(w.Ships);
                    forecast.Show(BattleForecast.Predict(ClanSystem.Combine(attackers), track.Ships,
                            ResearchSystem.CombatMods(state), enemy != null ? ResearchSystem.CombatMods(enemy.State) : default),
                        "Out in space: no batteries, no home-defense research — fleet against fleet.");
                }
                else forecast.NeedsIntel("Radar Lv 15 reads an inbound fleet — until then, it's a gamble.");
                if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, plan.Ok);
            }

            launchBtn = Widgets.TextButton("LAUNCH INTERCEPT", () =>
            {
                // The fleet may have turned since the screen opened — plan on where it is now.
                var fresh = StrikeSystem.Track(ctx.State!, ctx.Bots!, inbound, fleetId);
                if (fresh == null) { ui.Toast("That fleet is gone"); ui.CloseModal(); return; }
                var res = StrikeSystem.LaunchIntercept(ctx.State!, ctx.Bots!, fresh, picker!.Fleet(),
                    joint && wings.Count > 0, out _, out int flying);
                if (!res.Ok) { ui.Toast(res.Reason ?? "You can't catch them", Icon.Info, UiTheme.Bad); return; }
                GameAudio.Feedback(Sfx.Launch, Haptic.Medium);
                LocalBootstrap.RequestSync();
                ui.Toast("Intercept launched" + (flying > 0 ? $" · {flying} clanmate{(flying == 1 ? "" : "s")} flying with you" : ""));
                ui.CloseModal();
            }, 14);
            launchBtn.style.height = 42;
            footer.Add(launchBtn);

            Refresh();
            // Live: the countdown, the meeting point and who can make it all move with the clock.
            blocker.schedule.Execute(Refresh).Every(1000);
            ui.OpenModal(blocker);
        }

        static Label Note(string text, float marginTop)
        {
            var l = Widgets.Text(text, 11, UiTheme.Dim);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = marginTop;
            return l;
        }
    }
}
