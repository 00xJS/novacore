// Rankings (MORE → RANK) — the galaxy leaderboard: you versus the 99
// simulated commanders, ranked by might. Your row is highlighted; tapping a
// rival opens their public profile. All local — no fetch, no spinner.
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

        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("GALAXY RANKINGS", ui.CloseModal, 78f);

            var body = new VisualElement();
            content.Add(body);

            int lastSeenRank = UnityEngine.PlayerPrefs.GetInt(LastRankKey, 0);
            bool rankStored = false;
            long lastPlayerMight = -1;
            void Render()
            {
                var state = ctx.State;
                var galaxy = ctx.Bots;
                if (state == null || galaxy == null) return;

                body.Clear();
                long playerMight = PowerSystem.ComputePower(state);

                var rows = new List<(int botId, string name, int avatarSeed, long might)>
                {
                    (0, state.Profile.Name, state.Profile.AvatarSeed, playerMight),
                };
                foreach (var bot in galaxy.Bots)
                    rows.Add((bot.Id, bot.Name, bot.State.Profile.AvatarSeed, bot.CachedMight));
                rows.Sort((a, b) => b.might.CompareTo(a.might));

                // Pinned YOU card: the board is 250 rows, you shouldn't have to hunt
                // for yourself — plus how far you've moved since you last looked.
                int myRank = rows.FindIndex(r => r.botId == 0) + 1;
                var pin = Widgets.Row();
                pin.style.backgroundColor = new UnityEngine.Color(
                    UiTheme.Accent.r * 0.18f, UiTheme.Accent.g * 0.18f, UiTheme.Accent.b * 0.18f, 1f);
                Widgets.SetBorder(pin, UiTheme.Accent, 1.5f);
                pin.style.marginBottom = 12;
                var pinBox = Widgets.HBox(Justify.SpaceBetween);
                var pinLeft = Widgets.HBox();
                var pinRank = Widgets.Text($"#{myRank}", 18, UiTheme.Accent, bold: true);
                pinRank.style.minWidth = 56;
                pinLeft.Add(pinRank);
                var pinCol = new VisualElement();
                pinCol.Add(Widgets.Text("YOUR RANK", 9, UiTheme.Dim, bold: true));
                pinCol.Add(Widgets.Text($"of {rows.Count} commanders · might {playerMight:N0}", 11, UiTheme.Text));
                pinLeft.Add(pinCol);
                pinBox.Add(pinLeft);
                if (lastSeenRank > 0 && lastSeenRank != myRank)
                {
                    bool up = myRank < lastSeenRank;
                    pinBox.Add(Widgets.IconText(up ? Icon.ArrowUp : Icon.ArrowDown,
                        $"{Math.Abs(lastSeenRank - myRank)} since last look", 11, up ? UiTheme.Good : UiTheme.Bad));
                }
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
                    var name = Widgets.Text(entry.name, 12,
                        me ? UiTheme.Accent : UiTheme.Text, bold: me);
                    left.Add(name);
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

            Render();
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

        static Label RankLabel(int rank)
        {
            var label = Widgets.Text($"#{rank}", 12,
                rank <= 3 ? UiTheme.Energy : UiTheme.Dim, bold: true);
            label.style.width = 34;
            return label;
        }
    }
}
