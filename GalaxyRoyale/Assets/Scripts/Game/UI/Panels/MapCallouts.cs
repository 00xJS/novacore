// Bottom-strip callouts for map taps (same non-modal pattern as NodeCallout —
// the map stays pannable) + the favorites list:
//   RemoteCallout — another commander's planet: ATTACK / SPY / ★ / PROFILE
//   BlankCallout  — empty space: FLY TO (hold) / PORT HERE / ★
//   MarchCallout  — a fleet in flight: status, RECALL, REDIRECT (spy)
//   FavoritesPanel — bookmarked tiles, tap to jump.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    static class CalloutChrome
    {
        /// <summary>NodeCallout-style card: title/sub row + a row of small actions.</summary>
        public static VisualElement Strip(string title, string sub, Action onClose,
            params (string label, Action action, bool enabled)[] actions)
            => Strip(title, sub, onClose, null, actions);

        /// <summary>`subRef` receives the sub Label so callers can live-update it (march ETA).</summary>
        public static VisualElement Strip(string title, string sub, Action onClose,
            Action<Label>? subRef,
            params (string label, Action action, bool enabled)[] actions)
        {
            var card = Widgets.Row();
            card.style.backgroundColor = new UnityEngine.Color(
                UiTheme.Panel.r, UiTheme.Panel.g, UiTheme.Panel.b, 0.96f);
            // Tight strip — no dead space (user feedback).
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.marginBottom = 0;

            var head = Widgets.HBox(Justify.SpaceBetween);
            var titleCol = new VisualElement();
            titleCol.style.flexShrink = 1f;
            titleCol.Add(Widgets.Text(title, 13, UiTheme.Text, bold: true));
            if (sub.Length > 0)
            {
                var subLabel = Widgets.Text(sub, 10, UiTheme.Dim);
                subLabel.style.whiteSpace = WhiteSpace.Normal;
                titleCol.Add(subLabel);
                subRef?.Invoke(subLabel);
            }
            head.Add(titleCol);
            var close = Widgets.TextButton("×", onClose, 13);
            close.style.width = 30;
            close.style.height = 26;
            head.Add(close);
            card.Add(head);

            var row = Widgets.HBox(Justify.SpaceBetween);
            row.style.marginTop = 6;
            float width = actions.Length > 0 ? (100f - 2f * actions.Length) / actions.Length : 100f;
            foreach (var (label, action, enabled) in actions)
            {
                var b = Widgets.TextButton(label, action, 10);
                b.style.width = Length.Percent(width);
                Widgets.SetButtonEnabled(b, enabled);
                row.Add(b);
            }
            card.Add(row);
            return card;
        }

        public static void Close()
        {
            UIController.Instance?.CloseNodeCallout();
        }
    }

    /// <summary>Tapped a rival commander's planet on the map.</summary>
    public static class RemoteCallout
    {
        public static void Open(GameContext ctx, int botId)
        {
            var ui = UIController.Instance!;
            var bot = ctx.Bots?.Find(botId);
            string name = bot?.Name is { Length: > 0 } n ? n : "Unknown commander";
            var tile = bot?.HomeTile ?? default;
            bool shielded = bot != null && RaidService.IsShielded(bot.CachedMight);

            var strip = CalloutChrome.Strip(
                name,
                bot != null
                    ? $"might {bot.CachedMight:N0} · HQ {tile.X},{tile.Y}" + (shielded ? " · SHIELDED" : "")
                    : "no telemetry",
                CalloutChrome.Close,
                ("ATTACK", () => { CalloutChrome.Close(); RaidPanel.Open(ctx, botId); }, !shielded),
                ("SPY", () =>
                {
                    CalloutChrome.Close();
                    if (bot == null) { ui.Toast("No telemetry for that colony"); return; }
                    var (_, message) = RaidService.SpyBot(ctx, BotSystem.SnapshotOf(bot));
                    ui.Toast(message);
                }, bot != null),
                ("★", () =>
                {
                    if (bot == null) return;
                    ui.Toast(Favorites.Add(name, tile) ? $"Bookmarked {name}" : "Favorites list is full");
                }, bot != null),
                ("PROFILE", () => { CalloutChrome.Close(); PlayerProfilePanel.Open(ctx, botId, name); }, true));

            ui.OpenCalloutElement(strip);
        }
    }

    /// <summary>Tapped empty space: stage a fleet there or port the planet over.</summary>
    public static class BlankCallout
    {
        public static void Open(GameContext ctx, TileXY tile)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;

            if (MarchSystem.InCoreExclusion(tile))
            {
                ui.OpenCalloutElement(CalloutChrome.Strip(
                    "FORBIDDEN SPACE", $"{tile.X},{tile.Y} — the supernova core is off-limits",
                    CalloutChrome.Close));
                return;
            }

            // Any docked combat hull available to stage?
            int dockedCombat = 0;
            foreach (var hull in Ships.All)
            {
                if (hull == HullId.Probe) continue;
                dockedCombat += state.Ships.TryGetValue(hull, out var d) ? d : 0;
            }
            bool hasFleet = dockedCombat > 0;
            bool free = Balance.TestMode; // free warps while testing
            bool hasWarp = false;
            foreach (var e in state.Inventory)
                if (e.ItemId == "relocate-target" && e.Count > 0) hasWarp = true;

            var strip = CalloutChrome.Strip(
                $"Empty space  {tile.X}, {tile.Y}",
                "Send a fleet to hold here, or warp your planet over.",
                CalloutChrome.Close,
                // ▲ FLY TO now opens a ship-select composer (user feedback: it used
                // to launch the whole fleet instantly).
                ("▲ FLY TO", () =>
                {
                    CalloutChrome.Close();
                    FlyToComposer.Open(ctx, tile);
                }, hasFleet),
                ("▼ PORT HERE", () =>
                {
                    var s = ctx.State!;
                    if (s.Marches.Count > 0) { ui.Toast("Recall all fleets before relocating"); return; }
                    if (!free && !hasWarp)
                    {
                        // Don't yank the player to the shop (user feedback); just say so.
                        ui.Toast("You need a Precision Warp — buy one in the shop");
                        return;
                    }
                    ConfirmPanel.Open(
                        $"Warp your planet to {tile.X},{tile.Y}?",
                        "WARP",
                        () =>
                        {
                            var s2 = ctx.State!;
                            if (!free && !ShopSystem.ConsumeItem(s2, "relocate-target").Ok)
                            { ui.Toast("No Precision Warp in inventory"); return; }
                            var res = MarchSystem.RelocateHome(s2, tile);
                            ui.Toast(res.Ok ? "Planet relocated!" : res.Reason ?? "Warp failed");
                            if (res.Ok) ctx.GetComponent<MapView>()?.FocusTile(s2.HomeTile);
                        });
                }, true),
                ("★", () =>
                {
                    ui.Toast(Favorites.Add($"Space {tile.X},{tile.Y}", tile)
                        ? "Location bookmarked" : "Favorites list is full");
                }, true));

            ui.OpenCalloutElement(strip);
        }
    }

    /// <summary>Tapped a fleet in flight — the map camera follows it while this is open.</summary>
    public static class MarchCallout
    {
        public static void Open(GameContext ctx, int marchId, Action onClose)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var march = state.Marches.Find(m => m.Id == marchId);
            if (march == null) return;

            bool isSpy = march.Mission == MarchMission.Spy
                || (march.Ships.Count == 1 && march.Ships.ContainsKey(HullId.Probe));

            string StatusFor(March m)
            {
                if (m.ArrivesAtTick == int.MaxValue) return "holding position";
                return $"{m.Phase} · {UiTheme.FmtDuration(Math.Max(0, m.ArrivesAtTick - ctx.State!.Tick))}";
            }

            void Close() { onClose(); CalloutChrome.Close(); }

            var actions = new List<(string, Action, bool)>
            {
                ("RECALL", () =>
                {
                    var res = MarchSystem.RecallMarch(ctx.State!, marchId);
                    if (!res.Ok) ui.Toast(res.Reason ?? "Cannot recall");
                    Close();
                }, march.Phase != MarchPhase.Returning),
            };
            if (isSpy && march.Phase != MarchPhase.Gathering)
                actions.Add(("REDIRECT", () =>
                {
                    Close();
                    ctx.GetComponent<MapView>()?.BeginProbeRedirect(marchId);
                }, true));

            // Live ETA: tick the sub label down each second instead of only on re-tap.
            Label? subLabel = null;
            var strip = CalloutChrome.Strip(
                $"{MarchSystem.FleetCount(march.Ships)} ships → {march.Node.X},{march.Node.Y}",
                $"{march.Mission} · {StatusFor(march)} · following",
                Close,
                lbl => subLabel = lbl,
                actions.ToArray());
            strip.schedule.Execute(() =>
            {
                var m = ctx.State?.Marches.Find(x => x.Id == marchId);
                if (m == null || subLabel == null) return;
                subLabel.text = $"{m.Mission} · {StatusFor(m)} · following";
            }).Every(500);
            ui.OpenCalloutElement(strip);
        }
    }

    /// <summary>
    /// FLY TO an empty tile — pick which combat ships to send (they hold position
    /// there until recalled). User feedback: FLY TO used to launch the whole fleet
    /// instantly with no choice.
    /// </summary>
    public static class FlyToComposer
    {
        public static void Open(GameContext ctx, TileXY tile)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var (blocker, content) = Widgets.ModalPanel($"FLY TO  {tile.X}, {tile.Y}", ui.CloseModal, 0f);

            content.Add(Widgets.Text(
                $"{TileXY.Distance(tile, state.HomeTile):F1} tiles from home · fleet holds position until recalled",
                10, UiTheme.Dim));

            var picks = new Dictionary<HullId, SliderInt>();
            var preview = Widgets.Text("", 11, UiTheme.Accent);
            preview.style.whiteSpace = WhiteSpace.Normal;
            preview.style.marginTop = 8;
            var status = Widgets.Text("", 11, UiTheme.Bad);
            Button? launch = null;

            Dictionary<HullId, int> Fleet()
            {
                var f = new Dictionary<HullId, int>();
                foreach (var kv in picks) if (kv.Value.value > 0) f[kv.Key] = kv.Value.value;
                return f;
            }
            void Refresh()
            {
                var p = MarchSystem.PreviewMarch(state, Fleet(), tile);
                if (!p.Ok)
                {
                    preview.text = MarchSystem.FleetCount(Fleet()) < 1 ? "Select ships to send." : "";
                    status.text = MarchSystem.FleetCount(Fleet()) < 1 ? "" : p.Reason ?? "";
                }
                else
                {
                    status.text = "";
                    preview.text = $"{UiTheme.FmtDuration(p.TravelSec)} travel · {p.HeliumCost / 1000.0:0.#} helium";
                }
                if (launch != null) Widgets.SetButtonEnabled(launch, p.Ok);
            }

            string lastClass = "";
            foreach (var hull in Ships.All)
            {
                if (hull == HullId.Probe) continue; // scouting is its own action
                int docked = state.Ships.TryGetValue(hull, out var d) ? d : 0;
                if (docked <= 0) continue;
                var def = Ships.Defs[hull];
                if (def.Class != lastClass)
                {
                    lastClass = def.Class;
                    var h = Widgets.Text(def.Class.ToUpper(), 10, UiTheme.Accent, bold: true);
                    h.style.marginTop = 8;
                    content.Add(h);
                }
                var row = Widgets.Row();
                row.style.marginTop = 4;
                var head = Widgets.HBox(Justify.SpaceBetween);
                head.Add(Widgets.Text(def.Name, 12, UiTheme.Text, bold: true));
                var count = Widgets.Text("0", 12, UiTheme.Accent, bold: true);
                head.Add(count);
                row.Add(head);
                var slider = new SliderInt(0, docked) { value = 0 };
                picks[hull] = slider;
                slider.RegisterValueChangedCallback(_ => { count.text = slider.value.ToString(); Refresh(); });
                row.Add(slider);
                content.Add(row);
            }

            var quick = Widgets.HBox(Justify.SpaceAround);
            quick.style.marginTop = 6;
            var all = Widgets.TextButton("ALL DOCKED", () =>
            {
                foreach (var kv in picks)
                {
                    int docked = state.Ships.TryGetValue(kv.Key, out var d) ? d : 0;
                    kv.Value.value = docked;
                }
                Refresh();
            }, 10);
            all.style.width = Length.Percent(47f);
            var clear = Widgets.TextButton("CLEAR", () =>
            {
                foreach (var s in picks.Values) s.value = 0;
                Refresh();
            }, 10);
            clear.style.width = Length.Percent(47f);
            quick.Add(all); quick.Add(clear);
            content.Add(quick);
            content.Add(preview);
            content.Add(status);

            launch = Widgets.TextButton("▲ LAUNCH", () =>
            {
                var res = MarchSystem.SendRaidMarch(ctx.State!, Fleet(), tile, out _, MarchMission.Gather);
                if (!res.Ok) { status.text = res.Reason ?? "Cannot launch"; return; }
                ui.CloseModal();
                ui.Toast("Fleet en route — it will hold position until recalled");
            }, 14);
            launch.style.marginTop = 10;
            launch.style.height = 42;
            content.Add(launch);

            Refresh();
            ui.OpenModal(blocker);
        }
    }

    /// <summary>Bookmarked map locations — tap to jump the camera there.</summary>
    public static class FavoritesPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("FAVORITES", ui.CloseModal, 0f);

            var spots = Favorites.All();
            if (spots.Count == 0)
            {
                var empty = Widgets.Text(
                    "No bookmarks yet.\nTap ★ on a planet, node, or empty tile to save it.",
                    12, UiTheme.Dim);
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                empty.style.marginTop = 10;
                content.Add(empty);
            }
            foreach (var s in spots)
            {
                var spot = s;
                var row = Widgets.Row();
                var box = Widgets.HBox(Justify.SpaceBetween);
                var label = Widgets.Text($"★ {spot.Name}  ·  {spot.X},{spot.Y}", 12, UiTheme.Text);
                label.style.flexShrink = 1f;
                void GoThere()
                {
                    ui.CloseModal();
                    ui.SwitchView(ViewId.Map);
                    ctx.GetComponent<MapView>()?.FocusTile(new TileXY(spot.X, spot.Y));
                }
                label.RegisterCallback<PointerUpEvent>(_ => GoThere());
                box.Add(label);
                // Explicit GO (jump to it) + × (remove) so the two are unambiguous.
                var btns = Widgets.HBox();
                var go = Widgets.TextButton("▲ GO", GoThere, 11);
                go.style.marginRight = 6;
                btns.Add(go);
                btns.Add(Widgets.TextButton("×", () =>
                {
                    Favorites.Remove(new TileXY(spot.X, spot.Y));
                    Open(ctx); // rebuild
                }, 12));
                box.Add(btns);
                row.Add(box);
                content.Add(row);
            }

            ui.OpenModal(blocker);
        }
    }
}
