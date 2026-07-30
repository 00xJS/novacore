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
        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("GALAXY RANKINGS", ui.CloseModal, 78f);

            var body = new VisualElement();
            content.Add(body);

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
                    if (!me)
                    {
                        int botId = entry.botId;
                        string botName = entry.name;
                        name.RegisterCallback<PointerUpEvent>(_ =>
                            PlayerProfilePanel.Open(ctx, botId, botName));
                        avatar.RegisterCallback<PointerUpEvent>(_ =>
                            PlayerProfilePanel.Open(ctx, botId, botName));
                    }
                    left.Add(name);
                    box.Add(left);
                    box.Add(Widgets.Text($"{entry.might:N0}", 12, UiTheme.Energy));
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
