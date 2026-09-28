// SEND A GARRISON — station a fleet at a clanmate's colony (StrikeSystem). It
// flies there, stands guard, and fights in the defense line of any raid on
// them until you recall it (tap it on the map or in your queues).
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class GarrisonPanel
    {
        public static void Open(GameContext ctx, int hostBotId)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var galaxy = ctx.Bots;
            var host = galaxy?.Find(hostBotId);
            if (galaxy == null || host == null) { ui.Toast("That colony is gone"); return; }

            var (blocker, content, footer) = Widgets.ModalPanelFooter("SEND A GARRISON", ui.CloseModal, 78f);
            var head = Widgets.HBox(Justify.SpaceBetween);
            var title = Widgets.Text(ClanSystem.Tagged(state, galaxy, host.Id, host.Name), 15, UiTheme.Text, bold: true);
            title.style.flexShrink = 1f;
            head.Add(title);
            head.Add(Widgets.Text($"{TileXY.Distance(host.HomeTile, state.HomeTile):N0} tiles", 12, UiTheme.Accent, bold: true));
            content.Add(head);
            content.Add(Note("Your ships fly to their colony and stand guard. They fight any raid there beside " +
                "your clanmate's own fleet, and stay until you recall them."));

            var can = StrikeSystem.CanSendGarrison(state, galaxy, host);
            if (!can.Ok)
            {
                var why = Note(can.Reason ?? "You can't garrison them.");
                why.style.color = UiTheme.Bad;
                content.Add(why);
                ui.OpenModal(blocker);
                return;
            }

            Button? launchBtn = null;
            var preview = Widgets.Text("", 11, UiTheme.Accent);
            preview.style.whiteSpace = WhiteSpace.Normal;
            var status = Widgets.Text("", 11, UiTheme.Bad);
            FleetPicker? picker = null;

            var fleetHeader = Widgets.Text("SELECT THE GARRISON", 10, UiTheme.Dim, bold: true);
            fleetHeader.style.marginTop = 12;
            content.Add(fleetHeader);
            picker = new FleetPicker(content, state, () => Refresh());
            if (picker.Empty) { ui.OpenModal(blocker); return; }
            content.Add(preview);
            content.Add(status);

            void Refresh()
            {
                var fleet = picker!.Fleet();
                if (MarchSystem.FleetCount(fleet) < 1)
                {
                    preview.text = "Select the ships that stand guard.";
                    status.text = "";
                    if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, false);
                    return;
                }
                var p = MarchSystem.PreviewMarch(state, fleet, host.HomeTile);
                preview.text = p.Ok
                    ? $"On guard in {UiTheme.FmtDuration(MarchSystem.FlightSeconds(state, fleet, host.HomeTile))} · helium {UiTheme.FmtAmount(p.HeliumCost)}"
                    : "";
                status.text = p.Ok ? "" : p.Reason ?? "Cannot launch";
                if (launchBtn != null) Widgets.SetButtonEnabled(launchBtn, p.Ok);
            }

            launchBtn = Widgets.IconButton(Icon.Shield, "SEND GARRISON", () =>
            {
                var res = StrikeSystem.SendGarrison(ctx.State!, ctx.Bots!, hostBotId, picker!.Fleet(), out _);
                if (!res.Ok) { ui.Toast(res.Reason ?? "Cannot launch", Icon.Info, UiTheme.Bad); return; }
                GameAudio.Feedback(Sfx.Launch, Haptic.Medium);
                LocalBootstrap.RequestSync();
                ui.Toast($"Garrison on its way to {host.Name}'s colony", Icon.Shield, UiTheme.Good);
                ui.CloseModal();
            }, 14);
            launchBtn.style.height = 42;
            footer.Add(launchBtn);

            Refresh();
            ui.OpenModal(blocker);
        }

        static Label Note(string text)
        {
            var l = Widgets.Text(text, 11, UiTheme.Dim);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = 6;
            return l;
        }
    }
}
