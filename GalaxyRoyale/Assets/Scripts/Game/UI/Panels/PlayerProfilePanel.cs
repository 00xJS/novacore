// Public profile of a rival (simulated) commander — opened from rankings or
// their planet on the map. Shows only PUBLIC intel (name, might, HQ, presence)
// — resources and fleets stay hidden until you spy (v1 rule, kept).
using UnityEngine.UIElements;
using GalaxyRoyale.Sim.Bots;

namespace GalaxyRoyale.Game.UI
{
    public static class PlayerProfilePanel
    {
        public static void Open(GameContext ctx, int botId, string fallbackName)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("COMMANDER PROFILE", ui.CloseModal, 0f); // compact card

            var body = new VisualElement();
            content.Add(body);
            ui.OpenModal(blocker);

            var bot = ctx.Bots?.Find(botId);
            if (bot == null)
            {
                var unknown = Widgets.Text("No telemetry on that commander.", 11, UiTheme.Dim);
                unknown.style.whiteSpace = WhiteSpace.Normal;
                body.Add(unknown);
                return;
            }

            string name = bot.Name.Length > 0 ? bot.Name : fallbackName;
            var personality = BotSystem.PersonalityOf(ctx.State!.Seed, bot.Id);

            var head = Widgets.HBox();
            var avatar = Portraits.Avatar(bot.State.Profile.AvatarSeed, name, 40);
            avatar.style.marginRight = 10;
            head.Add(avatar);
            var idCol = new VisualElement();
            idCol.Add(Widgets.Text(name, 16, UiTheme.Text, bold: true));
            // Presence mirrors the bot's activity model — the same schedule that
            // gates its decisions, so "online" rivals really are the busy ones.
            bool online = IsOnlineNow(ctx, bot, personality);
            idCol.Add(Widgets.IconText(online ? Icon.Dot : Icon.Ring, online ? "online" : "offline", 10,
                online ? UiTheme.Good : UiTheme.Dim));
            head.Add(idCol);
            body.Add(head);

            void Line(string label, string value, UnityEngine.Color color)
            {
                var row = Widgets.HBox(Justify.SpaceBetween);
                row.style.marginTop = 6;
                row.Add(Widgets.Text(label, 11, UiTheme.Dim));
                row.Add(Widgets.Text(value, 12, color));
                body.Add(row);
            }

            Line("MIGHT", bot.CachedMight.ToString("N0"), UiTheme.Energy);
            Line("HQ", $"{bot.HomeTile.X}, {bot.HomeTile.Y}", UiTheme.Accent);
            Line("BATTLES", $"{bot.State.Stats.BattlesWon}W · {bot.State.Stats.BattlesLost}L", UiTheme.Text);

            var actions = Widgets.HBox(Justify.SpaceBetween);
            actions.style.marginTop = 14;
            var map = Widgets.TextButton("VIEW ON MAP", () =>
            {
                ui.CloseModal();
                ui.SwitchView(ViewId.Map);
                ctx.GetComponent<MapView>()?.FocusTile(bot.HomeTile);
            }, 10);
            map.style.width = Length.Percent(48f);
            actions.Add(map);
            // Painted swords — "⚔ RAID" rendered as "□ RAID" on device.
            var raid = Widgets.IconButton(Icon.Swords, "RAID", () =>
            {
                ui.CloseModal();
                RaidPanel.Open(ctx, botId);
            }, 10);
            raid.style.width = Length.Percent(48f);
            actions.Add(raid);
            body.Add(actions);
        }

        static bool IsOnlineNow(GameContext ctx, BotEmpire bot, BotPersonality personality)
        {
            int hour = (ctx.State?.Tick ?? 0) / 3600;
            return GalaxyRoyale.Sim.Rng.Hash2d(
                unchecked((uint)ctx.State!.Seed * 31u + (uint)bot.Id), hour, 7919)
                < personality.Activity;
        }
    }
}
