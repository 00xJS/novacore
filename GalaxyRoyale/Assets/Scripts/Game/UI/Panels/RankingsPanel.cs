// Rankings (MORE → RANK) — the galaxy leaderboard: you versus the simulated
// commanders, ranked by might — on the SEASON tab by might GAINED this season
// (SeasonSystem), and on the CLANS tab clan by clan. Your row is highlighted,
// names carry their clan tag, rivals out for revenge a red marker, clanmates a
// green one; tapping a row opens that commander's (or clan's) profile. All
// local — no fetch, no spinner.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class RankingsPanel
    {
        /// <summary>Your rank the last time you opened the board (device-local).</summary>
        const string LastRankKey = "galaxyroyale.last_rank";

        /// <summary>A new galaxy starts the "since last look" delta fresh.</summary>
        public static void ForgetLastRank() => UnityEngine.PlayerPrefs.DeleteKey(LastRankKey);

        public static VisualElement Build(GameContext ctx, out Action refresh, bool season = false)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("GALAXY RANKINGS", ui.CloseModal, 78f);

            int lastSeenRank = UnityEngine.PlayerPrefs.GetInt(LastRankKey, 0);
            bool rankStored = false;
            long lastPlayerMight = -1;

            // MIGHT | SEASON | CLANS tabs.
            int tab = season ? 1 : 0;
            var tabs = Widgets.HBox(Justify.SpaceBetween);
            tabs.style.marginBottom = 10;
            var tabButtons = new Button[3];
            var body = new VisualElement();
            void SelectTab(int to)
            {
                tab = to;
                for (int t = 0; t < tabButtons.Length; t++) Widgets.SetButtonHighlight(tabButtons[t], t == tab);
                Render();
            }
            tabButtons[0] = Widgets.IconButton(Icon.Chart, "MIGHT", () => SelectTab(0), 11);
            tabButtons[1] = Widgets.IconButton(Icon.Trophy, "SEASON", () => SelectTab(1), 11);
            tabButtons[2] = Widgets.IconButton(Icon.Pact, "CLANS", () => SelectTab(2), 11);
            foreach (var b in tabButtons)
            {
                b.style.width = Length.Percent(32f);
                tabs.Add(b);
            }
            content.Add(tabs);
            content.Add(body);

            void Render()
            {
                var state = ctx.State;
                var galaxy = ctx.Bots;
                if (state == null || galaxy == null) return;

                body.Clear();
                long playerMight = PowerSystem.ComputePower(state);
                if (tab == 1) { RenderSeason(ctx, state, galaxy, body); return; }
                if (tab == 2) { RenderClans(ctx, state, galaxy, body); return; }

                var rows = new List<(int botId, string name, int avatarSeed, long might, bool grudge)>
                {
                    (0, state.Profile.Name, state.Profile.AvatarSeed, playerMight, false),
                };
                foreach (var bot in galaxy.Bots)
                    rows.Add((bot.Id, bot.Name, bot.State.Profile.AvatarSeed, bot.CachedMight,
                        BotSystem.HoldsGrudge(bot, state.Tick)));
                rows.Sort((a, b) => b.might.CompareTo(a.might));

                // Pinned YOU card: the board is 250 rows, you shouldn't have to hunt
                // for yourself — plus how far you've moved since you last looked.
                int myRank = rows.FindIndex(r => r.botId == 0) + 1;
                var pin = Widgets.Row();
                pin.style.backgroundColor = new UnityEngine.Color(
                    UiTheme.Accent.r * 0.18f, UiTheme.Accent.g * 0.18f, UiTheme.Accent.b * 0.18f, 1f);
                Widgets.SetBorder(pin, UiTheme.Accent, 1.5f);
                pin.style.marginBottom = 12;
                var pinBox = Widgets.HBox();
                var pinRank = Widgets.Text($"#{myRank}", 18, UiTheme.Accent, bold: true);
                pinRank.style.minWidth = 56;
                pinRank.style.flexShrink = 0f;
                pinBox.Add(pinRank);
                var pinCol = new VisualElement();
                pinCol.style.flexShrink = 1f;
                pinCol.Add(Widgets.Text("YOUR RANK", 9, UiTheme.Dim, bold: true));
                var standing = Widgets.Text($"of {rows.Count} commanders · might {playerMight:N0}", 11, UiTheme.Text);
                standing.style.whiteSpace = WhiteSpace.Normal;
                pinCol.Add(standing);
                // The movement gets its own line — sharing the row with the might
                // line, the two ran into each other on phone-width cards.
                if (lastSeenRank > 0 && lastSeenRank != myRank)
                {
                    bool up = myRank < lastSeenRank;
                    int moved = Math.Abs(lastSeenRank - myRank);
                    var delta = Widgets.IconText(up ? Icon.ArrowUp : Icon.ArrowDown,
                        $"{(up ? "up" : "down")} {moved} {(moved == 1 ? "place" : "places")} since last look",
                        11, up ? UiTheme.Good : UiTheme.Bad, bold: true);
                    delta.style.marginTop = 3;
                    pinCol.Add(delta);
                }
                pinBox.Add(pinCol);
                pin.Add(pinBox);
                body.Add(pin);
                if (!rankStored)
                {
                    rankStored = true; // the delta compares visits, not ticks within one
                    UnityEngine.PlayerPrefs.SetInt(LastRankKey, myRank);
                }

                int rank = 0;
                foreach (var entry in rows)
                {
                    rank++;
                    bool me = entry.botId == 0;
                    var row = Widgets.Row();
                    if (me) Widgets.SetBorder(row, UiTheme.Accent, 1.5f);
                    var box = Widgets.HBox(Justify.SpaceBetween);
                    var left = Widgets.HBox();
                    left.Add(RankLabel(rank));
                    var avatar = Portraits.Avatar(entry.avatarSeed, entry.name, 20);
                    avatar.style.marginRight = 6;
                    left.Add(avatar);
                    left.Add(NameBlock(state, galaxy, entry.botId, entry.name));
                    if (entry.grudge)
                    {
                        var mark = Icons.Make(Icon.Swords, 12, UiTheme.Bad);
                        mark.style.marginLeft = 6;
                        left.Add(mark);
                    }
                    if (!me && galaxy.Find(entry.botId) is { } rival && ClanSystem.SameClanAsPlayer(state, rival))
                    {
                        var mark = Icons.Make(Icon.Pact, 13, UiTheme.Good);
                        mark.style.marginLeft = 6;
                        left.Add(mark);
                    }
                    box.Add(left);
                    var right = Widgets.HBox();
                    right.Add(Widgets.Text($"{entry.might:N0}", 12, UiTheme.Energy));
                    if (!me)
                    {
                        // The WHOLE card opens the profile — only the name text and
                        // avatar used to, so taps on the rank / might / padding did
                        // nothing even though the row looked like one big button.
                        int botId = entry.botId;
                        string botName = entry.name;
                        row.RegisterCallback<PointerUpEvent>(_ =>
                            PlayerProfilePanel.Open(ctx, botId, botName));
                        var chevron = Icons.Make(Icon.ChevronRight, 12, UiTheme.Accent);
                        chevron.style.marginLeft = 8;
                        right.Add(chevron);
                    }
                    box.Add(right);
                    row.Add(box);
                    body.Add(row);
                }
            }

            SelectTab(tab);
            int lastCheckTick = -1;
            refresh = () =>
            {
                // Might only moves when the sim ticks — check 1×/s, not per frame
                // (ComputePower walks every building level + hull; it's not free).
                var state = ctx.State;
                if (state == null || state.Tick == lastCheckTick) return;
                lastCheckTick = state.Tick;
                long m = PowerSystem.ComputePower(state);
                if (m == lastPlayerMight) return;
                lastPlayerMight = m;
                Render();
            };
            return blocker;
        }

        /// <summary>Clan tag + name, plus the worn title under YOUR name.</summary>
        static VisualElement NameBlock(GalaxyRoyale.Sim.GameState state, BotGalaxy galaxy, int botId, string name)
        {
            bool me = botId == 0;
            var label = Widgets.Text(ClanSystem.Tagged(state, galaxy, botId, name), 12,
                me ? UiTheme.Accent : UiTheme.Text, bold: me);
            label.style.flexShrink = 1f;
            string? title = me ? AchievementSystem.TitleText(state) : null;
            if (title == null) return label;
            var col = new VisualElement();
            col.Add(label);
            col.Add(Widgets.Text(title, 9, UiTheme.Energy, bold: true));
            return col;
        }

        /// <summary>CLANS tab: every clan by total might; tap one for its profile.</summary>
        static void RenderClans(GameContext ctx, GalaxyRoyale.Sim.GameState state, BotGalaxy galaxy, VisualElement body)
        {
            var rows = ClanSystem.Standings(state, galaxy);
            if (rows.Count == 0)
            {
                var none = Widgets.Text("No clans yet — they form as the galaxy's politics play out.", 11, UiTheme.Dim);
                none.style.whiteSpace = WhiteSpace.Normal;
                body.Add(none);
                return;
            }
            int rank = 0;
            foreach (var (clan, members, might) in rows)
            {
                rank++;
                bool mine = clan.Id == state.ClanId;
                var row = Widgets.Row();
                if (mine) Widgets.SetBorder(row, UiTheme.Accent, 1.5f);
                var box = Widgets.HBox(Justify.SpaceBetween);
                var left = Widgets.HBox();
                left.style.flexShrink = 1f;
                left.Add(RankLabel(rank));
                var col = new VisualElement();
                col.style.flexShrink = 1f;
                col.Add(Widgets.Text(ClanSystem.Label(clan), 12, mine ? UiTheme.Accent : UiTheme.Text, bold: true));
                string status = $"{members}/{ClanSystem.MaxMembers} members";
                if (galaxy.FindClan(clan.WarWithClanId) is { } enemy) status += $" · at war with [{enemy.Tag}]";
                col.Add(Widgets.Text(status, 10, clan.WarWithClanId != 0 ? UiTheme.Bad : UiTheme.Dim));
                left.Add(col);
                box.Add(left);
                var right = Widgets.HBox();
                right.Add(Widgets.Text($"{might:N0}", 12, UiTheme.Energy));
                var chevron = Icons.Make(Icon.ChevronRight, 12, UiTheme.Accent);
                chevron.style.marginLeft = 8;
                right.Add(chevron);
                box.Add(right);
                row.Add(box);
                int clanId = clan.Id;
                row.RegisterCallback<PointerUpEvent>(_ => ClanPanel.OpenProfile(ctx, clanId));
                body.Add(row);
            }
        }

        /// <summary>SEASON tab: ranked by might gained since the season began.</summary>
        static void RenderSeason(GameContext ctx, GalaxyRoyale.Sim.GameState state, BotGalaxy galaxy, VisualElement body)
        {
            var rows = SeasonSystem.Standings(state, galaxy);
            int myRank = rows.FindIndex(r => r.id == 0) + 1;
            int number = Math.Max(1, state.Season);

            var pin = Widgets.Row();
            pin.style.backgroundColor = new UnityEngine.Color(
                UiTheme.Energy.r * 0.16f, UiTheme.Energy.g * 0.16f, UiTheme.Energy.b * 0.16f, 1f);
            Widgets.SetBorder(pin, UiTheme.Energy, 1.5f);
            pin.style.marginBottom = 12;
            var pinBox = Widgets.HBox();
            var pinRank = Widgets.Text($"#{myRank}", 18, UiTheme.Energy, bold: true);
            pinRank.style.minWidth = 56;
            pinRank.style.flexShrink = 0f;
            pinBox.Add(pinRank);
            var pinCol = new VisualElement();
            pinCol.style.flexShrink = 1f;
            pinCol.Add(Widgets.Text($"SEASON {number} · ENDS IN {UiTheme.FmtLong(SeasonSystem.EndTick(number) - state.Tick).ToUpperInvariant()}",
                9, UiTheme.Dim, bold: true));
            var standing = Widgets.Text(
                $"+{SeasonSystem.PlayerGain(state):N0} might gained · finishing here pays {SeasonSystem.RewardFor(myRank)} DM",
                11, UiTheme.Text);
            standing.style.whiteSpace = WhiteSpace.Normal;
            pinCol.Add(standing);
            pinBox.Add(pinCol);
            pin.Add(pinBox);
            body.Add(pin);

            int rank = 0;
            foreach (var entry in rows)
            {
                rank++;
                bool me = entry.id == 0;
                var row = Widgets.Row();
                if (me) Widgets.SetBorder(row, UiTheme.Energy, 1.5f);
                var box = Widgets.HBox(Justify.SpaceBetween);
                var left = Widgets.HBox();
                left.Add(RankLabel(rank));
                int avatarSeed = me ? state.Profile.AvatarSeed : galaxy.Find(entry.id)?.State.Profile.AvatarSeed ?? 0;
                var avatar = Portraits.Avatar(avatarSeed, entry.name, 20);
                avatar.style.marginRight = 6;
                left.Add(avatar);
                left.Add(NameBlock(state, galaxy, entry.id, entry.name));
                if (!me && galaxy.Find(entry.id) is { } rival && ClanSystem.SameClanAsPlayer(state, rival))
                {
                    var mark = Icons.Make(Icon.Pact, 13, UiTheme.Good);
                    mark.style.marginLeft = 6;
                    left.Add(mark);
                }
                box.Add(left);
                var right = Widgets.HBox();
                right.Add(Widgets.Text(entry.gain >= 0 ? $"+{entry.gain:N0}" : $"{entry.gain:N0}", 12,
                    entry.gain > 0 ? UiTheme.Good : UiTheme.Dim));
                if (!me)
                {
                    int botId = entry.id;
                    string botName = entry.name;
                    row.RegisterCallback<PointerUpEvent>(_ => PlayerProfilePanel.Open(ctx, botId, botName));
                    var chevron = Icons.Make(Icon.ChevronRight, 12, UiTheme.Accent);
                    chevron.style.marginLeft = 8;
                    right.Add(chevron);
                }
                box.Add(right);
                row.Add(box);
                body.Add(row);
            }
        }

        static Label RankLabel(int rank)
        {
            var label = Widgets.Text($"#{rank}", 12,
                rank <= 3 ? UiTheme.Energy : UiTheme.Dim, bold: true);
            label.style.width = 34;
            return label;
        }
    }
}
