// Bottom-strip callouts for map taps (same non-modal pattern as NodeCallout —
// the map stays pannable) + the favorites list:
//   RemoteCallout — another commander's planet: ATTACK / SPY / ★ / PROFILE
//   BlankCallout  — empty space: FLY TO (hold) / PORT HERE / ★
//   MarchCallout  — your fleet in flight: status, RECALL, REDIRECT (spy)
//   RivalFlightCallout — someone else's flight (raid, probe, gather run, or a
//                   radar-tracked hostile inbound to you); the camera follows it
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
    /// <summary>One callout action button, with an optional painted icon.</summary>
    readonly struct CalloutAction
    {
        public readonly string Label;
        public readonly Action OnTap;
        public readonly bool Enabled;
        public readonly Icon? Glyph;

        public CalloutAction(string label, Action onTap, bool enabled, Icon? glyph)
        {
            Label = label;
            OnTap = onTap;
            Enabled = enabled;
            Glyph = glyph;
        }
    }

    static class CalloutChrome
    {
        public static CalloutAction Act(string label, Action onTap, bool enabled = true, Icon? icon = null)
            => new(label, onTap, enabled, icon);

        /// <summary>NodeCallout-style card: title/sub row + a row of small actions.</summary>
        public static VisualElement Strip(string title, string sub, Action onClose,
            params CalloutAction[] actions)
            => Strip(title, sub, onClose, null, actions);

        /// <summary>`subRef` receives the sub Label so callers can live-update it (march ETA).</summary>
        public static VisualElement Strip(string title, string sub, Action onClose,
            Action<Label>? subRef,
            params CalloutAction[] actions)
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
            foreach (var a in actions)
            {
                // Symbols are painted icons — "★" / "▲" as button text is at the
                // mercy of the runtime font (other glyphs tofu-boxed on device).
                var b = a.Glyph is Icon icon
                    ? Widgets.IconButton(icon, a.Label.Length > 0 ? a.Label : null, a.OnTap, 10, 12f)
                    : Widgets.TextButton(a.Label, a.OnTap, 10);
                b.style.width = Length.Percent(width);
                b.style.paddingLeft = 4;
                b.style.paddingRight = 4;
                Widgets.SetButtonEnabled(b, a.Enabled);
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
                CalloutChrome.Act("ATTACK", () => { CalloutChrome.Close(); RaidPanel.Open(ctx, botId); }, !shielded),
                CalloutChrome.Act("SPY", () =>
                {
                    CalloutChrome.Close();
                    if (bot == null) { ui.Toast("No telemetry for that colony"); return; }
                    var (_, message) = RaidService.SpyBot(ctx, BotSystem.SnapshotOf(bot));
                    ui.Toast(message);
                }, bot != null),
                CalloutChrome.Act("", () =>
                {
                    if (bot == null) return;
                    ui.Toast(Favorites.Add(name, tile) ? $"Bookmarked {name}" : "Favorites list is full");
                }, bot != null, Icon.Star),
                CalloutChrome.Act("PROFILE", () => { CalloutChrome.Close(); PlayerProfilePanel.Open(ctx, botId, name); }));

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
                // FLY TO opens a ship-select composer (user feedback: it used
                // to launch the whole fleet instantly).
                CalloutChrome.Act("FLY TO", () =>
                {
                    CalloutChrome.Close();
                    FlyToComposer.Open(ctx, tile);
                }, hasFleet, Icon.ArrowUp),
                CalloutChrome.Act("PORT HERE", () =>
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
                }, true, Icon.ArrowDown),
                CalloutChrome.Act("", () =>
                {
                    ui.Toast(Favorites.Add($"Space {tile.X},{tile.Y}", tile)
                        ? "Location bookmarked" : "Favorites list is full");
                }, true, Icon.Star));

            ui.OpenCalloutElement(strip);
        }
    }

    /// <summary>Tapped a fleet in flight — the map camera follows it while this is open.</summary>
    public static class MarchCallout
    {
        /// <summary>Mission as the player thinks of it: a lone probe sent at a
        /// rival flies as an Attack march (RaidArrivals files its intel), but it
        /// is a spy run — it used to be listed as "Attack".</summary>
        public static string MissionLabel(March m) =>
            m.Mission == MarchMission.Attack && m.Ships.Count == 1 && m.Ships.ContainsKey(HullId.Probe)
                ? "Spy" : m.Mission.ToString();

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

            var actions = new List<CalloutAction>
            {
                CalloutChrome.Act("RECALL", () =>
                {
                    var res = MarchSystem.RecallMarch(ctx.State!, marchId);
                    if (!res.Ok) ui.Toast(res.Reason ?? "Cannot recall");
                    Close();
                }, march.Phase != MarchPhase.Returning),
            };
            if (isSpy && march.Phase != MarchPhase.Gathering)
                actions.Add(CalloutChrome.Act("REDIRECT", () =>
                {
                    Close();
                    ctx.GetComponent<MapView>()?.BeginProbeRedirect(marchId);
                }));

            // Live ETA: tick the sub label down each second instead of only on re-tap.
            Label? subLabel = null;
            var strip = CalloutChrome.Strip(
                $"{MarchSystem.FleetCount(march.Ships)} ships · target {march.Node.X}, {march.Node.Y}",
                $"{MissionLabel(march)} · {StatusFor(march)} · following",
                Close,
                lbl => subLabel = lbl,
                actions.ToArray());
            strip.schedule.Execute(() =>
            {
                var m = ctx.State?.Marches.Find(x => x.Id == marchId);
                if (m == null || subLabel == null) return;
                subLabel.text = $"{MissionLabel(m)} · {StatusFor(m)} · following";
            }).Every(500);
            ui.OpenCalloutElement(strip);
        }
    }

    /// <summary>
    /// Tapped another commander's flight on the map — a raid fleet, spy probe,
    /// gather run, or a radar-tracked hostile inbound to you. The map camera
    /// follows it while this is open (user request: only your OWN fleets could
    /// be tapped and followed before). MapView owns the follow; onClose ends it.
    /// </summary>
    public static class RivalFlightCallout
    {
        static string NameOf(GameContext ctx, int botId) =>
            ctx.Bots?.Find(botId)?.Name is { Length: > 0 } n ? n : "Unknown commander";

        /// <summary>A real bot-vs-bot flight: raid (combat wing) or recon probe.</summary>
        public static void OpenRaid(GameContext ctx, int marchId, Action onClose)
        {
            var ui = UIController.Instance!;
            BotMarch? Find()
            {
                var galaxy = ctx.Bots;
                if (galaxy == null) return null;
                foreach (var m in galaxy.Marches)
                    if (m.Id == marchId) return m;
                return null;
            }
            var march = Find();
            if (march == null) return;

            int attackerId = march.BotId, targetId = march.TargetBotId;
            string attacker = NameOf(ctx, attackerId);
            string target = NameOf(ctx, targetId);
            int ships = 0;
            foreach (var kv in march.Ships) ships += kv.Value;

            string Status(BotMarch m)
            {
                int now = ctx.State?.Tick ?? 0;
                if (!m.Resolved)
                    return $"{(m.IsSpy ? "scouting" : "raiding")} {target} · arrives in " +
                           UiTheme.FmtDuration(Math.Max(0, m.ArrivesAtTick - now));
                long loot = m.LootMilli.Gold + m.LootMilli.Quartz + m.LootMilli.Helium;
                return (loot > 0 ? $"homeward with {UiTheme.FmtAmount(loot)} loot" : "homeward, empty-handed") +
                       $" · {UiTheme.FmtDuration(Math.Max(0, m.ReturnsAtTick - now))}";
            }

            void Close() { onClose(); CalloutChrome.Close(); }
            Label? sub = null;
            var strip = CalloutChrome.Strip(
                march.IsSpy ? $"{attacker}'s spy probe" : $"{attacker}'s raid fleet · {ships} ships",
                $"{Status(march)} · following",
                Close,
                l => sub = l,
                CalloutChrome.Act("ATTACKER", () => { Close(); PlayerProfilePanel.Open(ctx, attackerId, attacker); },
                    icon: Icon.Swords),
                CalloutChrome.Act("TARGET", () => { Close(); PlayerProfilePanel.Open(ctx, targetId, target); }));
            strip.schedule.Execute(() =>
            {
                var m = Find();
                if (m != null && sub != null) sub.text = $"{Status(m)} · following";
            }).Every(500);
            ui.OpenCalloutElement(strip);
        }

        /// <summary>A rival's gather shuttle (cosmetic traffic around their colony).</summary>
        public static void OpenGatherRun(GameContext ctx, int botId, Action onClose)
        {
            var ui = UIController.Instance!;
            string name = NameOf(ctx, botId);
            void Close() { onClose(); CalloutChrome.Close(); }
            ui.OpenCalloutElement(CalloutChrome.Strip(
                $"{name}'s gatherers",
                "Harvest run near their colony · following",
                Close,
                CalloutChrome.Act("COMMANDER", () => { Close(); PlayerProfilePanel.Open(ctx, botId, name); })));
        }

        /// <summary>A hostile inbound to YOU, as far as the Radar Station can see —
        /// RadarContact is already sanitized to the radar's detail tier.</summary>
        public static void OpenContact(GameContext ctx, int contactId, Action onClose)
        {
            var ui = UIController.Instance!;
            RadarContact? Find()
            {
                foreach (var c in RadarService.DetectedThreats)
                    if (c.Id == contactId) return c;
                return null;
            }
            var contact = Find();
            if (contact == null) return;

            string who = contact.AttackerName.Length > 0 ? contact.AttackerName : "Unknown commander";
            string what = !contact.IsFleet ? "spy probe"
                : contact.FleetCount > 0 ? $"{contact.FleetCount} ships" : "hostile fleet";
            string Status(RadarContact c) =>
                $"{what} · impact in {UiTheme.FmtDuration(Math.Max(0, c.ArrivesAtTick - (ctx.State?.Tick ?? 0)))} · following";

            // Names are unique across the roster, so the radar's name resolves the bot.
            int attackerId = -1;
            if (contact.AttackerName.Length > 0 && ctx.Bots != null)
                foreach (var b in ctx.Bots.Bots)
                    if (b.Name == contact.AttackerName) { attackerId = b.Id; break; }

            void Close() { onClose(); CalloutChrome.Close(); }
            var actions = new List<CalloutAction>
            {
                CalloutChrome.Act("DEFENSES", () => { Close(); ui.OpenQueues(); }, icon: Icon.Warning),
            };
            if (attackerId >= 0)
                actions.Add(CalloutChrome.Act("ATTACKER", () => { Close(); PlayerProfilePanel.Open(ctx, attackerId, who); }));

            Label? sub = null;
            var strip = CalloutChrome.Strip($"INCOMING — {who}", Status(contact), Close, l => sub = l,
                actions.ToArray());
            strip.schedule.Execute(() =>
            {
                var c = Find();
                if (c != null && sub != null) sub.text = Status(c);
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

            launch = Widgets.IconButton(Icon.ArrowUp, "LAUNCH", () =>
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
                    "No bookmarks yet.\nTap the star on a planet, node, or empty tile to save it.",
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
                var label = Widgets.IconText(Icon.Star, $"{spot.Name}  ·  {spot.X},{spot.Y}", 12, UiTheme.Text);
                label.style.flexShrink = 1f;
                label.pickingMode = PickingMode.Position; // the name row is tappable too
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
                var go = Widgets.IconButton(Icon.ChevronRight, "GO", GoThere, 11);
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
