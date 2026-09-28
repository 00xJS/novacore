// Public profile of a rival (simulated) commander — opened from rankings or
// their planet on the map. Shows only PUBLIC intel (name, might, HQ, presence,
// and whether they're out for revenge) — resources and fleets stay hidden
// until you spy (v1 rule, kept).
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;

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
            idCol.Add(Widgets.Text(ClanSystem.Tagged(ctx.State!, ctx.Bots!, bot.Id, name), 16, UiTheme.Text, bold: true));
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
            Line("COMMANDER LEVEL", CommanderSystem.RivalLevel(bot.State).ToString(), UiTheme.Energy);
            Line("HQ", $"{bot.HomeTile.X}, {bot.HomeTile.Y}", UiTheme.Accent);
            Line("DISTANCE", $"{GalaxyRoyale.Data.TileXY.Distance(bot.HomeTile, ctx.State!.HomeTile):N0} tiles", UiTheme.Text);
            Line("BATTLES", $"{bot.State.Stats.BattlesWon}W · {bot.State.Stats.BattlesLost}L", UiTheme.Text);
            // Your won raid marked them (BotSystem.ApplyPlayerRaid): the counter-
            // raid comes once they can win it, unless the grudge lapses first.
            if (BotSystem.HoldsGrudge(bot, ctx.State!.Tick))
            {
                var grudge = Widgets.IconText(Icon.Warning,
                    "OUT FOR REVENGE — they'll counter-raid you once they can win", 11, UiTheme.Bad, bold: true);
                grudge.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                grudge.style.marginTop = 10;
                body.Add(grudge);
            }

            // ---- clan ----
            var state = ctx.State!;
            var galaxy = ctx.Bots!;
            var theirClan = galaxy.FindClan(bot.ClanId);
            bool clanmate = ClanSystem.SameClanAsPlayer(state, bot);
            if (theirClan != null)
            {
                bool war = ClanSystem.AtWarWith(galaxy, state.ClanId, theirClan.Id);
                bool covers = TileXY.Distance(bot.HomeTile, state.HomeTile) <= ClanSystem.ReinforceRange;
                string text = clanmate
                    ? $"YOUR CLANMATE in {ClanSystem.Label(theirClan)}" + (covers ? " — covers your colony when raiders strike" : "")
                    : war ? $"{ClanSystem.Label(theirClan)} — AT WAR WITH YOUR CLAN"
                          : $"Member of {ClanSystem.Label(theirClan)}" + (theirClan.LeaderId == bot.Id ? " (leader)" : "");
                var clanRow = Widgets.IconText(war ? Icon.Swords : Icon.Pact, text, 11,
                    clanmate ? UiTheme.Good : war ? UiTheme.Bad : UiTheme.Accent, bold: true);
                clanRow.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                clanRow.style.marginTop = 10;
                body.Add(clanRow);
            }
            else
            {
                var free = Widgets.IconText(Icon.Ring, ClanSystem.IsLoneWolf(state.Seed, bot)
                    ? "Lone wolf — never joins a clan" : "Independent — in no clan", 11, UiTheme.Dim);
                free.style.marginTop = 10;
                body.Add(free);
            }

            Button? clanBtn = null;
            if (clanmate) clanBtn = Widgets.IconButton(Icon.Pact, "YOUR CLAN", ui.OpenClan, 10);
            else if (theirClan == null && ClanSystem.PlayerLeads(state, galaxy))
            {
                var verdict = ClanSystem.CanInvite(state, galaxy, bot);
                clanBtn = Widgets.IconButton(Icon.Pact, "INVITE TO YOUR CLAN", () =>
                {
                    var result = ClanSystem.Invite(ctx.State!, ctx.Bots!, botId);
                    if (!result.Ok) { ui.Toast(result.Reason ?? "They declined", Icon.Info, UiTheme.Bad); return; }
                    GameAudio.Feedback(Sfx.Quest, Haptic.Success);
                    ui.Toast($"{name} joined your clan", Icon.Pact, UiTheme.Good);
                    LocalBootstrap.RequestSync();
                    Open(ctx, botId, fallbackName);
                }, 10);
                if (!verdict.Ok)
                {
                    var why = Widgets.Text(verdict.Reason ?? "", 10, UiTheme.Dim);
                    why.style.whiteSpace = WhiteSpace.Normal;
                    why.style.marginTop = 6;
                    body.Add(why);
                    Widgets.SetButtonEnabled(clanBtn, false);
                }
            }
            else if (theirClan != null)
            {
                int clanId = theirClan.Id;
                clanBtn = Widgets.IconButton(Icon.Pact, $"VIEW [{theirClan.Tag}]", () => ClanPanel.OpenProfile(ctx, clanId), 10);
            }
            if (clanBtn != null)
            {
                clanBtn.style.marginTop = 8;
                body.Add(clanBtn);
            }

            var actions = Widgets.HBox(Justify.SpaceBetween);
            actions.style.marginTop = 12;
            var map = Widgets.TextButton("VIEW ON MAP", () =>
            {
                ui.CloseModal();
                ui.SwitchView(ViewId.Map);
                ctx.GetComponent<MapView>()?.FocusTile(bot.HomeTile);
            }, 10);
            map.style.width = Length.Percent(48f);
            actions.Add(map);
            // Painted swords — "⚔ RAID" rendered as "□ RAID" on device. Clanmates
            // never raid each other; for them the slot sends a garrison instead.
            var raid = clanmate
                ? Widgets.IconButton(Icon.Shield, "GARRISON", () =>
                {
                    ui.CloseModal();
                    GarrisonPanel.Open(ctx, botId);
                }, 10)
                : Widgets.IconButton(Icon.Swords, "RAID", () =>
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
