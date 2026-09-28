// CLAN (MORE → CLAN): clans of up to 15 commanders (ClanSystem). Out of a
// clan: a standing invitation, founding your own, and the clans that would
// take you. In one: its standing, the war it's fighting, the daily supply run,
// its members (the leader can invite and remove), and leaving. Tapping a clan
// anywhere opens its public profile (OpenProfile).
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class ClanPanel
    {
        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("CLAN", ui.CloseModal, 82f);
            var body = new VisualElement();
            content.Add(body);

            string key = "";
            int lastTick = -1;

            void Render(GameState state, BotGalaxy galaxy)
            {
                body.Clear();
                var clan = ClanSystem.PlayerClan(state, galaxy);
                if (clan == null) RenderOutside(ctx, state, galaxy, body, () => key = "");
                else RenderInside(ctx, state, galaxy, clan, body, () => key = "");
            }

            refresh = () =>
            {
                var state = ctx.State;
                var galaxy = ctx.Bots;
                if (state == null || galaxy == null || state.Tick == lastTick) return;
                lastTick = state.Tick;
                var clan = ClanSystem.PlayerClan(state, galaxy);
                string k = $"{state.ClanId}|{state.ClanInviteId}|{state.ClanSupplyRuns}|{state.Tick / 60}"
                    + (clan != null ? $"|{ClanSystem.MemberCount(state, galaxy, clan.Id)}|{clan.WarWithClanId}|{clan.WarScore}|{clan.LeaderId}" : "");
                if (k == key) return;
                key = k;
                Render(state, galaxy);
            };
            refresh();
            return blocker;
        }

        // ---------- not in a clan ----------

        static void RenderOutside(GameContext ctx, GameState state, BotGalaxy galaxy, VisualElement body, Action invalidate)
        {
            var ui = UIController.Instance!;
            body.Add(Note($"A clan is up to {ClanSystem.MaxMembers} commanders. Clanmates never raid each other, send " +
                $"warships when one is raided (up to {ClanSystem.MaxReinforcers} within {ClanSystem.ReinforceRange:N0} tiles), " +
                "fly along on each other's raids, and share a daily supply run. Clans go to war with each other."));

            // ---- a standing invitation ----
            if (galaxy.FindClan(state.ClanInviteId) is { } inviting)
            {
                var card = Card(UiTheme.Good);
                card.Add(Widgets.IconText(Icon.Pact, $"{ClanSystem.Label(inviting)} INVITES YOU", 13, UiTheme.Good, bold: true));
                card.Add(Wrap(Widgets.Text($"{ClanSystem.MemberCount(state, galaxy, inviting.Id)}/{ClanSystem.MaxMembers} members · " +
                    $"might {ClanSystem.ClanMight(state, galaxy, inviting.Id):N0} · nearest member " +
                    $"{ClanSystem.NearestMember(state, galaxy, inviting.Id):N0} tiles · expires in " +
                    $"{UiTheme.FmtLong(Math.Max(0, state.ClanInviteExpiresTick - state.Tick))}", 11, UiTheme.Text), 4));
                var row = Widgets.HBox(Justify.SpaceBetween);
                row.style.marginTop = 8;
                var accept = Widgets.IconButton(Icon.Check, "ACCEPT", () =>
                {
                    if (Act(ClanSystem.Join(ctx.State!, ctx.Bots!, inviting.Id), $"Welcome to {ClanSystem.Label(inviting)}"))
                        invalidate();
                }, 12);
                accept.style.width = Length.Percent(48f);
                Widgets.SetBorder(accept, UiTheme.Good, 1.5f);
                row.Add(accept);
                var decline = Widgets.TextButton("DECLINE", () =>
                {
                    ClanSystem.DeclineInvite(ctx.State!);
                    ui.Toast($"Declined {ClanSystem.Label(inviting)}");
                    invalidate();
                }, 12);
                decline.style.width = Length.Percent(48f);
                row.Add(decline);
                card.Add(row);
                body.Add(card);
            }

            // ---- found your own ----
            var found = Card(UiTheme.Accent);
            found.Add(Widgets.IconText(Icon.Pact, "FOUND A CLAN", 13, UiTheme.Accent, bold: true));
            found.Add(Wrap(Widgets.Text($"Lead your own: pick a name and a 2–4 letter tag. Costs {ClanSystem.FoundCost.Gold:N0} gold " +
                $"and {ClanSystem.FoundCost.Quartz:N0} quartz; then invite commanders from the clan screen or their profiles.",
                11, UiTheme.Text), 4));
            var foundBtn = Widgets.TextButton("FOUND A CLAN", () => OpenFound(ctx), 12);
            foundBtn.style.marginTop = 8;
            found.Add(foundBtn);
            body.Add(found);

            // ---- clans that would take you ----
            body.Add(Section("CLANS NEAR YOU"));
            var clans = new List<(Clan clan, double dist)>();
            foreach (var clan in galaxy.Clans)
                if (clan.LeaderId != 0) clans.Add((clan, ClanSystem.NearestMember(state, galaxy, clan.Id)));
            clans.Sort((a, b) => a.dist.CompareTo(b.dist));
            if (clans.Count == 0) body.Add(Note("No clans have formed yet — check back after the next politics round."));
            for (int i = 0; i < Math.Min(8, clans.Count); i++)
                body.Add(ClanRow(ctx, state, galaxy, clans[i].clan, clans[i].dist, invalidate));
        }

        static VisualElement ClanRow(GameContext ctx, GameState state, BotGalaxy galaxy, Clan clan, double dist, Action invalidate)
        {
            var row = Widgets.Row();
            var head = Widgets.HBox(Justify.SpaceBetween);
            var name = Widgets.Text(ClanSystem.Label(clan), 12, UiTheme.Text, bold: true);
            name.style.flexShrink = 1f;
            head.Add(name);
            if (clan.WarWithClanId != 0) head.Add(Widgets.Text("AT WAR", 10, UiTheme.Bad, bold: true));
            row.Add(head);
            row.Add(Widgets.Text($"{ClanSystem.MemberCount(state, galaxy, clan.Id)}/{ClanSystem.MaxMembers} members · " +
                $"might {ClanSystem.ClanMight(state, galaxy, clan.Id):N0} · nearest {dist:N0} tiles", 10, UiTheme.Dim));
            var verdict = ClanSystem.CanJoin(state, galaxy, clan);
            var actions = Widgets.HBox(Justify.SpaceBetween);
            actions.style.marginTop = 6;
            var view = Widgets.TextButton("VIEW", () => OpenProfile(ctx, clan.Id), 10);
            view.style.width = Length.Percent(32f);
            actions.Add(view);
            var join = Widgets.TextButton(verdict.Ok ? "ASK TO JOIN" : "CAN'T JOIN YET", () =>
            {
                if (Act(ClanSystem.Join(ctx.State!, ctx.Bots!, clan.Id), $"Welcome to {ClanSystem.Label(clan)}"))
                    invalidate();
            }, 10);
            join.style.width = Length.Percent(64f);
            Widgets.SetButtonEnabled(join, verdict.Ok);
            actions.Add(join);
            row.Add(actions);
            if (!verdict.Ok) row.Add(Wrap(Widgets.Text(verdict.Reason ?? "", 10, UiTheme.Dim), 3));
            return row;
        }

        // ---------- in a clan ----------

        static void RenderInside(GameContext ctx, GameState state, BotGalaxy galaxy, Clan clan, VisualElement body, Action invalidate)
        {
            var ui = UIController.Instance!;
            bool leader = clan.LeaderId == 0;
            int count = ClanSystem.MemberCount(state, galaxy, clan.Id);

            var head = Card(UiTheme.Accent);
            head.Add(Widgets.IconText(Icon.Pact, ClanSystem.Label(clan).ToUpperInvariant(), 15, UiTheme.Accent, bold: true));
            string leaderName = leader ? "You lead it" : $"Led by {galaxy.Find(clan.LeaderId)?.Name ?? "—"}";
            head.Add(Wrap(Widgets.Text($"{leaderName} · {count}/{ClanSystem.MaxMembers} members · might " +
                $"{ClanSystem.ClanMight(state, galaxy, clan.Id):N0}", 11, UiTheme.Text), 4));
            body.Add(head);

            // ---- war ----
            var war = Card(clan.WarWithClanId != 0 ? UiTheme.Bad : UiTheme.Stroke);
            if (galaxy.FindClan(clan.WarWithClanId) is { } enemy)
            {
                war.Add(Widgets.IconText(Icon.Swords, $"AT WAR WITH {ClanSystem.Label(enemy).ToUpperInvariant()}", 12, UiTheme.Bad, bold: true));
                war.Add(Wrap(Widgets.Text($"Score {clan.WarScore}–{enemy.WarScore} · ends in " +
                    $"{UiTheme.FmtLong(Math.Max(0, clan.WarEndsTick - state.Tick))}. Every battle won against their members " +
                    $"counts; the winning clan's members get {ClanSystem.WarWinRewardDM} DM.", 11, UiTheme.Text), 4));
            }
            else
            {
                war.Add(Widgets.IconText(Icon.Swords, "AT PEACE", 12, UiTheme.Dim, bold: true));
                bool resting = state.Tick < clan.WarCooldownUntilTick;
                war.Add(Wrap(Widgets.Text(resting
                    ? $"Recovering from the last war — no new war for {UiTheme.FmtLong(clan.WarCooldownUntilTick - state.Tick)}."
                    : leader ? "As leader you can declare a three-day war on another clan."
                             : "Rival clans may declare war on yours at any time.", 11, UiTheme.Dim), 4));
                if (leader && !resting)
                {
                    var declare = Widgets.IconButton(Icon.Swords, "DECLARE WAR", () => OpenWarPicker(ctx), 11);
                    declare.style.marginTop = 8;
                    war.Add(declare);
                }
            }
            body.Add(war);

            // ---- supplies ----
            var supplies = Card(state.ClanSupplyRuns > 0 ? UiTheme.Good : UiTheme.Stroke);
            var pending = state.ClanSupplyPending;
            if (state.ClanSupplyRuns > 0)
            {
                supplies.Add(Widgets.IconText(Icon.Pact, $"SUPPLIES WAITING ({state.ClanSupplyRuns} RUN{(state.ClanSupplyRuns == 1 ? "" : "S")})", 12, UiTheme.Good, bold: true));
                supplies.Add(Wrap(Widgets.Text($"+{UiTheme.FmtAmount(pending.Gold)} gold · +{UiTheme.FmtAmount(pending.Quartz)} quartz · " +
                    $"+{UiTheme.FmtAmount(pending.Helium)} helium", 12, UiTheme.Text, bold: true), 4));
                var collect = Widgets.IconButton(Icon.Check, "COLLECT", () =>
                {
                    if (ClanSystem.CollectSupplies(ctx.State!).Ok)
                    {
                        GameAudio.Feedback(Sfx.Coins, Haptic.Success);
                        ui.Toast("Clan supplies banked", Icon.Pact, UiTheme.Good);
                        LocalBootstrap.RequestSync();
                        invalidate();
                    }
                }, 12);
                collect.style.marginTop = 8;
                Widgets.SetBorder(collect, UiTheme.Good, 1.5f);
                supplies.Add(collect);
            }
            else
            {
                supplies.Add(Widgets.IconText(Icon.Clock,
                    $"Next supply run in {UiTheme.FmtLong(Math.Max(0, state.ClanSupplyNextTick - state.Tick))}", 11, UiTheme.Dim));
                supplies.Add(Wrap(Widgets.Text("Every member chips in a little each day; runs wait here (up to " +
                    $"{ClanSystem.MaxStoredRuns}) until you collect.", 10, UiTheme.Dim), 3));
            }
            body.Add(supplies);

            // ---- members ----
            body.Add(Section($"MEMBERS {count}/{ClanSystem.MaxMembers}"));
            var members = ClanSystem.BotMembers(galaxy, clan.Id);
            members.Sort((a, b) => b.CachedMight.CompareTo(a.CachedMight));
            body.Add(MemberRow(ctx, state, null, clan, leader, invalidate));
            foreach (var bot in members) body.Add(MemberRow(ctx, state, bot, clan, leader, invalidate));

            // ---- recruit ----
            if (leader && count < ClanSystem.MaxMembers)
            {
                body.Add(Section("INVITE COMMANDERS"));
                var candidates = ClanSystem.InviteCandidates(state, galaxy, 8);
                if (candidates.Count == 0)
                    body.Add(Note("Nobody nearby would join right now — grow stronger, or invite from a commander's profile."));
                foreach (var bot in candidates)
                {
                    var row = Widgets.Row();
                    var box = Widgets.HBox(Justify.SpaceBetween);
                    var left = Widgets.HBox();
                    left.style.flexShrink = 1f;
                    var avatar = Portraits.Avatar(bot.State.Profile.AvatarSeed, bot.Name, 22);
                    avatar.style.marginRight = 8;
                    left.Add(avatar);
                    var col = new VisualElement();
                    col.style.flexShrink = 1f;
                    col.Add(Widgets.Text(bot.Name, 12, UiTheme.Text, bold: true));
                    col.Add(Widgets.Text($"Might {bot.CachedMight:N0} · {TileXY.Distance(bot.HomeTile, state.HomeTile):N0} tiles",
                        10, UiTheme.Dim));
                    left.Add(col);
                    box.Add(left);
                    int botId = bot.Id;
                    var invite = Widgets.IconButton(Icon.Pact, "INVITE", () =>
                    {
                        if (Act(ClanSystem.Invite(ctx.State!, ctx.Bots!, botId), $"{bot.Name} joined {ClanSystem.Label(clan)}"))
                            invalidate();
                    }, 10);
                    invite.style.flexShrink = 0f;
                    box.Add(invite);
                    row.Add(box);
                    body.Add(row);
                }
            }

            // ---- leave ----
            var leave = Widgets.TextButton("LEAVE CLAN", () =>
                ConfirmPanel.Open(
                    leader && members.Count > 0
                        ? $"Leave {ClanSystem.Label(clan)}?\nLeadership passes to {members[0].Name}. Supplies waiting are banked."
                        : leader
                            ? $"Leave {ClanSystem.Label(clan)}?\nYou're its only member — the clan disbands."
                            : $"Leave {ClanSystem.Label(clan)}?\nYou lose its protection. Supplies waiting are banked.",
                    "LEAVE CLAN",
                    () =>
                    {
                        Act(ClanSystem.Leave(ctx.State!, ctx.Bots!), $"You left {ClanSystem.Label(clan)}");
                        ui.OpenClan();
                    },
                    () => ui.OpenClan()), 11);
            leave.style.marginTop = 12;
            body.Add(leave);
        }

        static VisualElement MemberRow(GameContext ctx, GameState state, BotEmpire? bot, Clan clan, bool playerLeads, Action invalidate)
        {
            var ui = UIController.Instance!;
            var row = Widgets.Row();
            if (bot == null) Widgets.SetBorder(row, UiTheme.Accent, 1f);
            var box = Widgets.HBox(Justify.SpaceBetween);
            var left = Widgets.HBox();
            left.style.flexShrink = 1f;
            string name = bot?.Name ?? state.Profile.Name;
            var avatar = Portraits.Avatar(bot?.State.Profile.AvatarSeed ?? state.Profile.AvatarSeed, name, 24);
            avatar.style.marginRight = 8;
            left.Add(avatar);
            var col = new VisualElement();
            col.style.flexShrink = 1f;
            bool isLeader = bot == null ? clan.LeaderId == 0 : clan.LeaderId == bot.Id;
            col.Add(Widgets.Text((isLeader ? "LEADER · " : "") + name + (bot == null ? " (you)" : ""), 12,
                bot == null ? UiTheme.Accent : UiTheme.Text, bold: true));
            if (bot != null)
            {
                double d = TileXY.Distance(bot.HomeTile, state.HomeTile);
                bool inRange = d <= ClanSystem.ReinforceRange;
                col.Add(Widgets.Text($"Might {bot.CachedMight:N0} · {d:N0} tiles" + (inRange ? " · covers you" : ""), 10,
                    inRange ? UiTheme.Good : UiTheme.Dim));
                int id = bot.Id;
                row.RegisterCallback<PointerUpEvent>(_ => PlayerProfilePanel.Open(ctx, id, bot.Name));
            }
            else col.Add(Widgets.Text($"Might {PowerSystem.ComputePower(state):N0}", 10, UiTheme.Dim));
            left.Add(col);
            box.Add(left);
            if (bot != null && playerLeads)
            {
                int id = bot.Id;
                var kick = Widgets.TextButton("REMOVE", () =>
                    ConfirmPanel.Open($"Remove {bot.Name} from {ClanSystem.Label(clan)}?\nThey'll take it personally — expect a grudge.",
                        "REMOVE", () =>
                        {
                            Act(ClanSystem.Kick(ctx.State!, ctx.Bots!, id), $"{bot.Name} was removed");
                            ui.OpenClan();
                        }, () => ui.OpenClan()), 10);
                kick.style.flexShrink = 0f;
                box.Add(kick);
            }
            row.Add(box);
            return row;
        }

        // ---------- sub-screens ----------

        static void OpenFound(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("FOUND A CLAN", ui.OpenClan, 0f);
            content.Add(Wrap(Widgets.Text($"Costs {ClanSystem.FoundCost.Gold:N0} gold and {ClanSystem.FoundCost.Quartz:N0} quartz. " +
                "Name: 3–24 letters, digits or spaces. Tag: 2–4 letters or digits.", 11, UiTheme.Dim), 0));
            // Our own labels: the default runtime theme paints a TextField's
            // built-in label near-black (unreadable on the dark panel) and
            // reserves a wide column for it.
            content.Add(Section("CLAN NAME"));
            var name = new TextField { maxLength = 24 };
            content.Add(name);
            content.Add(Section("TAG"));
            var tag = new TextField { maxLength = 4 };
            content.Add(tag);
            var status = Widgets.Text("", 11, UiTheme.Bad);
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.marginTop = 4;
            content.Add(status);
            var go = Widgets.IconButton(Icon.Pact, "FOUND IT", () =>
            {
                var result = ClanSystem.Found(ctx.State!, ctx.Bots!, name.value ?? "", tag.value ?? "");
                if (!result.Ok) { status.text = result.Reason ?? ""; GameAudio.Feedback(Sfx.Error, Haptic.Error); return; }
                GameAudio.Feedback(Sfx.Quest, Haptic.Success);
                ui.Toast($"You founded [{(tag.value ?? "").Trim().ToUpperInvariant()}] {(name.value ?? "").Trim()}", Icon.Pact, UiTheme.Good);
                LocalBootstrap.RequestSync();
                ui.OpenClan();
            }, 13);
            go.style.marginTop = 10;
            go.style.height = 40;
            content.Add(go);
            ui.OpenModal(blocker);
        }

        static void OpenWarPicker(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var galaxy = ctx.Bots!;
            var (blocker, content) = Widgets.ModalPanel("DECLARE WAR", ui.OpenClan, 70f);
            content.Add(Note($"A war lasts {ClanSystem.WarDurationSec / 86400} days. Every battle won against the enemy's members " +
                $"scores; the winners get {ClanSystem.WarWinRewardDM} DM. The enemy clan will come for you too."));
            foreach (var (clan, members, might) in ClanSystem.Standings(state, galaxy))
            {
                if (clan.Id == state.ClanId) continue;
                var verdict = ClanSystem.CanDeclareWar(state, galaxy, clan);
                var row = Widgets.Row();
                row.Add(Widgets.Text(ClanSystem.Label(clan), 12, UiTheme.Text, bold: true));
                row.Add(Widgets.Text($"{members}/{ClanSystem.MaxMembers} members · might {might:N0} · nearest " +
                    $"{ClanSystem.NearestMember(state, galaxy, clan.Id):N0} tiles", 10, UiTheme.Dim));
                int id = clan.Id;
                var btn = Widgets.IconButton(Icon.Swords, verdict.Ok ? "DECLARE WAR" : (verdict.Reason ?? "Unavailable"), () =>
                    ConfirmPanel.Open($"Declare war on {ClanSystem.Label(clan)}?\nThree days of open season between your clans.",
                        "DECLARE WAR", () =>
                        {
                            if (Act(ClanSystem.DeclareWar(ctx.State!, ctx.Bots!, id), $"War declared on {ClanSystem.Label(clan)}"))
                                GameAudio.Feedback(Sfx.Alert, Haptic.Heavy);
                            ui.OpenClan();
                        }, () => ui.OpenClan()), 10);
                btn.style.marginTop = 6;
                Widgets.SetButtonEnabled(btn, verdict.Ok);
                row.Add(btn);
                content.Add(row);
            }
            ui.OpenModal(blocker);
        }

        /// <summary>A clan's public profile: members, war, and JOIN / DECLARE WAR.</summary>
        public static void OpenProfile(GameContext ctx, int clanId)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var galaxy = ctx.Bots!;
            var clan = galaxy.FindClan(clanId);
            var (blocker, content) = Widgets.ModalPanel(clan != null ? ClanSystem.Label(clan).ToUpperInvariant() : "CLAN", ui.CloseModal, 78f);
            ui.OpenModal(blocker);
            if (clan == null) { content.Add(Note("That clan has disbanded.")); return; }

            int count = ClanSystem.MemberCount(state, galaxy, clan.Id);
            string leaderName = clan.LeaderId == 0 ? state.Profile.Name : galaxy.Find(clan.LeaderId)?.Name ?? "—";
            content.Add(Widgets.Text($"Led by {leaderName} · {count}/{ClanSystem.MaxMembers} members · might " +
                $"{ClanSystem.ClanMight(state, galaxy, clan.Id):N0}", 11, UiTheme.Text));
            if (galaxy.FindClan(clan.WarWithClanId) is { } enemy)
            {
                var war = Widgets.IconText(Icon.Swords, $"At war with {ClanSystem.Label(enemy)} · {clan.WarScore}–{enemy.WarScore}", 11,
                    UiTheme.Bad, bold: true);
                war.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                war.style.marginTop = 4;
                content.Add(war);
            }

            var actions = Widgets.HBox(Justify.SpaceBetween);
            actions.style.marginTop = 10;
            if (state.ClanId == 0)
            {
                var verdict = ClanSystem.CanJoin(state, galaxy, clan);
                var join = Widgets.IconButton(Icon.Pact, verdict.Ok ? "ASK TO JOIN" : "CAN'T JOIN YET", () =>
                {
                    if (Act(ClanSystem.Join(ctx.State!, ctx.Bots!, clan.Id), $"Welcome to {ClanSystem.Label(clan)}")) ui.OpenClan();
                }, 11);
                join.style.width = Length.Percent(100f);
                Widgets.SetButtonEnabled(join, verdict.Ok);
                actions.Add(join);
                content.Add(actions);
                if (!verdict.Ok) content.Add(Wrap(Widgets.Text(verdict.Reason ?? "", 10, UiTheme.Dim), 3));
            }
            else if (state.ClanId != clan.Id && ClanSystem.PlayerLeads(state, galaxy))
            {
                var verdict = ClanSystem.CanDeclareWar(state, galaxy, clan);
                var declare = Widgets.IconButton(Icon.Swords, "DECLARE WAR", () =>
                    ConfirmPanel.Open($"Declare war on {ClanSystem.Label(clan)}?\nThree days of open season between your clans.",
                        "DECLARE WAR", () =>
                        {
                            Act(ClanSystem.DeclareWar(ctx.State!, ctx.Bots!, clan.Id), $"War declared on {ClanSystem.Label(clan)}");
                            ui.OpenClan();
                        }, () => OpenProfile(ctx, clanId)), 11);
                declare.style.width = Length.Percent(100f);
                Widgets.SetButtonEnabled(declare, verdict.Ok);
                actions.Add(declare);
                content.Add(actions);
                if (!verdict.Ok) content.Add(Wrap(Widgets.Text(verdict.Reason ?? "", 10, UiTheme.Dim), 3));
            }

            content.Add(Section("MEMBERS"));
            if (state.ClanId == clan.Id)
                content.Add(Widgets.Text($"{state.Profile.Name} (you){(clan.LeaderId == 0 ? " · LEADER" : "")}", 12, UiTheme.Accent, bold: true));
            var members = ClanSystem.BotMembers(galaxy, clan.Id);
            members.Sort((a, b) => b.CachedMight.CompareTo(a.CachedMight));
            foreach (var bot in members)
            {
                var row = Widgets.Row();
                var box = Widgets.HBox(Justify.SpaceBetween);
                box.Add(Widgets.Text((clan.LeaderId == bot.Id ? "LEADER · " : "") + bot.Name, 12, UiTheme.Text, bold: true));
                box.Add(Widgets.Text($"{bot.CachedMight:N0}", 11, UiTheme.Energy));
                row.Add(box);
                int id = bot.Id;
                row.RegisterCallback<PointerUpEvent>(_ => PlayerProfilePanel.Open(ctx, id, bot.Name));
                content.Add(row);
            }
        }

        // ---------- bits ----------

        /// <summary>Toast the outcome; true on success.</summary>
        static bool Act(SimResult result, string success)
        {
            var ui = UIController.Instance!;
            if (!result.Ok)
            {
                GameAudio.Feedback(Sfx.Error, Haptic.Error);
                ui.Toast(result.Reason ?? "Not possible right now", Icon.Info, UiTheme.Bad);
                return false;
            }
            GameAudio.Feedback(Sfx.Confirm, Haptic.Success);
            ui.Toast(success, Icon.Pact, UiTheme.Good);
            LocalBootstrap.RequestSync();
            return true;
        }

        static VisualElement Card(UnityEngine.Color border)
        {
            var card = new VisualElement();
            card.style.marginTop = 8;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.paddingTop = 10;
            card.style.paddingBottom = 12;
            card.style.backgroundColor = UiTheme.PanelLight;
            Widgets.SetBorder(card, border, 1.5f);
            card.style.borderTopLeftRadius = 10;
            card.style.borderTopRightRadius = 10;
            card.style.borderBottomLeftRadius = 10;
            card.style.borderBottomRightRadius = 10;
            return card;
        }

        static Label Section(string text)
        {
            var l = Widgets.Text(text, 10, UiTheme.Dim, bold: true);
            l.style.marginTop = 12;
            l.style.marginBottom = 6;
            return l;
        }

        static Label Note(string text) => Wrap(Widgets.Text(text, 11, UiTheme.Dim), 0);

        static Label Wrap(Label label, float marginTop)
        {
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = marginTop;
            return label;
        }
    }
}
