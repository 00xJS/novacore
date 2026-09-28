// MAILBOX — UI Toolkit port of v1's MailboxPanel + MailDetailPanel: per-kind
// tabs (ALL / BATTLES / SPY / ★ SAVED), tappable report rows, and a detail
// view with favorite / view-on-map / delete (favorites are delete-protected).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Combat;

namespace GalaxyRoyale.Game.UI
{
    public static class MailboxPanel
    {
        enum Tab { All, Battle, Spy, Fav }

        /// <summary>Did the PLAYER come out on top (either side of the fight)?</summary>
        static bool PlayerWon(BattleMailReport b) =>
            b.Defending ? b.Report.Winner == BattleWinner.Defender : b.Report.Winner == BattleWinner.Attacker;

        /// <summary>Unread row tint by outcome from YOUR side — every battle row used
        /// to be red, even a raid you repelled.</summary>
        static UnityEngine.Color RowColor(MailItem item) => item switch
        {
            BattleMailReport b when PlayerWon(b) => UiTheme.Good,
            BattleMailReport b when b.Report.Winner == BattleWinner.Draw => UiTheme.Energy,
            BattleMailReport => UiTheme.Bad,
            RadarWarning => UiTheme.Bad,
            _ => UiTheme.Text,
        };

        /// <summary>Aegis deflections file a round-less report the defender "won".</summary>
        static bool IsDeflection(BattleMailReport battle) =>
            battle.Defending && battle.Report.Winner == BattleWinner.Defender && battle.Report.Rounds.Count == 0;

        /// <summary>Subjects saved by older builds carry a "⚠ " prefix that
        /// tofu-boxes on device — radar rows get a painted icon instead.</summary>
        static string DisplaySubject(MailItem item) =>
            item.Subject.StartsWith("⚠ ", StringComparison.Ordinal) ? item.Subject.Substring(2) : item.Subject;

        static string IntelLine(GameState state, MailItem item)
        {
            if (item is BattleMailReport { Defending: true } defense)
            {
                // A rival hit YOUR colony: their win is your loss.
                var r = defense.Report;
                if (IsDeflection(defense)) return "Deflected — your Aegis Shield held";
                long lost = r.Loot?.Total ?? 0;
                return r.Winner switch
                {
                    BattleWinner.Attacker => lost > 0 ? $"Raided — lost {UiTheme.FmtAmount(lost)}" : "Raided — defenses broken",
                    BattleWinner.Defender => "Defended — raiders destroyed",
                    _ => "Held — raiders withdrew",
                };
            }
            if (item is BattleMailReport battle)
            {
                var r = battle.Report;
                if (r.Winner == BattleWinner.Attacker)
                {
                    long total = r.Loot?.Total ?? 0;
                    return total > 0 ? $"Victory — looted {UiTheme.FmtAmount(total)}" : "Victory";
                }
                if (r.Winner == BattleWinner.Defender) return "Defeat — fleet destroyed";
                return "Stalemate — fleet withdrew";
            }
            if (item is RadarWarning warn)
            {
                string what = warn.IsFleet is bool f ? (f ? "War fleet" : "Spy probe") : "Unknown contact";
                int dt = warn.ArrivesAtTick - state.Tick;
                return dt > 0 ? $"{what} — arrival in {UiTheme.FmtDuration(dt)}" : $"{what} — arrived";
            }
            if (item is SpyReport spy)
            {
                var i = spy.Intel;
                if (i.Kind == null) return "nothing of note";
                if (i.Kind == NodeKind.Camp)
                {
                    var garrison = i.Garrison ?? new Dictionary<HullId, int>();
                    var parts = garrison.Where(kv => kv.Value > 0)
                        .Select(kv => $"{kv.Value}× {Ships.Defs[kv.Key].Name}").ToList();
                    if (parts.Count == 0) return "garrison: none";
                    // Big commander garrisons list every hull class — summarize in
                    // the LIST row (it was running far off-screen, user report); the
                    // detail view's GARRISON section still shows every line.
                    if (parts.Count > 3)
                    {
                        int total = garrison.Values.Where(v => v > 0).Sum();
                        return $"garrison: {total} ships · {parts.Count} classes";
                    }
                    return $"garrison: {string.Join(", ", parts)}";
                }
                return $"{UiTheme.FmtAmount(i.Remaining ?? 0)} remaining · tier {i.Tier}";
            }
            return "";
        }

        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("MAILBOX", ui.CloseModal, 76f);

            var tab = Tab.All;
            string cache = "";

            var tabRow = Widgets.HBox(Justify.SpaceBetween);
            tabRow.style.marginBottom = 8;
            var tabButtons = new Dictionary<Tab, Button>();
            foreach (var (t, label) in new[]
            {
                (Tab.All, "ALL"), (Tab.Battle, "BATTLES"), (Tab.Spy, "SPY"), (Tab.Fav, "SAVED"),
            })
            {
                var tt = t;
                void Select() { tab = tt; cache = ""; }
                var b = tt == Tab.Fav
                    ? Widgets.IconButton(Icon.Star, label, Select, 10, 11f)
                    : Widgets.TextButton(label, Select, 10);
                b.style.paddingLeft = 4;
                b.style.paddingRight = 4;
                b.style.width = Length.Percent(24f);
                tabButtons[tt] = b;
                tabRow.Add(b);
            }
            content.Add(tabRow);

            var list = new VisualElement();
            content.Add(list);

            List<MailItem> Filtered(GameState state) => tab switch
            {
                Tab.Battle => state.Mailbox.Where(m => m is BattleMailReport).ToList(),
                Tab.Spy => state.Mailbox.Where(m => m is SpyReport).ToList(),
                Tab.Fav => state.Mailbox.Where(m => m.Favorite).ToList(),
                _ => state.Mailbox.ToList(),
            };

            refresh = () =>
            {
                var state = ctx.State!;
                var items = Filtered(state);
                string key = $"{tab}|{items.Count}|{(items.Count > 0 ? items[0].Id : 0)}|{state.Mailbox.Count(r => !r.Read)}|{state.Mailbox.Count(r => r.Favorite)}";
                if (key == cache) return;
                cache = key;

                foreach (var kv in tabButtons)
                    Widgets.SetButtonHighlight(kv.Value, kv.Key == tab);
                list.Clear();

                if (items.Count == 0)
                {
                    string msg = tab switch
                    {
                        Tab.Battle => "No battle reports yet.\nAttack a pirate camp to log one.",
                        Tab.Spy => "No spy reports yet.\nSend a Spy Probe from any map node.",
                        Tab.Fav => "No saved reports yet.\nTap SAVE on any report to keep it.",
                        _ => "No reports yet.\nSend a Spy Probe or attack a camp.",
                    };
                    var empty = Widgets.Text(msg, 12, UiTheme.Dim);
                    empty.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                    empty.style.whiteSpace = WhiteSpace.Normal;
                    empty.style.marginTop = 40;
                    list.Add(empty);
                    return;
                }

                foreach (var report in items)
                {
                    var r = report;
                    bool isBattle = r is BattleMailReport || r is RadarWarning;
                    bool won = r is BattleMailReport br && PlayerWon(br);
                    var row = Widgets.Row();
                    if (won) row.style.backgroundColor = new UnityEngine.Color(0.1f, 0.16f, 0.12f, r.Read ? 0.5f : 0.9f);
                    else if (isBattle) row.style.backgroundColor = new UnityEngine.Color(0.165f, 0.1f, 0.1f, r.Read ? 0.5f : 0.9f);
                    else if (r.Read) row.style.backgroundColor = new UnityEngine.Color(UiTheme.PanelLight.r, UiTheme.PanelLight.g, UiTheme.PanelLight.b, 0.4f);
                    row.RegisterCallback<PointerUpEvent>(_ => OpenDetail(ctx, r));

                    // Unread dot / saved star / radar warning are painted icons —
                    // text glyphs are at the mercy of the runtime font.
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    var lead = Widgets.HBox();
                    lead.style.flexShrink = 1f;
                    void Mark(Icon icon, float size, UnityEngine.Color color)
                    {
                        var mark = Icons.Make(icon, size, color);
                        mark.style.marginRight = 5;
                        lead.Add(mark);
                    }
                    if (!r.Read) Mark(Icon.Dot, 10f, UiTheme.Accent);
                    if (r.Favorite) Mark(Icon.Star, 12f, UiTheme.Energy);
                    if (r is RadarWarning) Mark(Icon.Warning, 13f, UiTheme.Bad);
                    var subject = Widgets.Text(DisplaySubject(r), 12,
                        r.Read ? UiTheme.Dim : RowColor(r), bold: !r.Read);
                    subject.style.flexShrink = 1f;
                    lead.Add(subject);
                    head.Add(lead);
                    head.Add(Icons.Make(Icon.ChevronRight, 14, UiTheme.Accent));
                    row.Add(head);
                    var meta = Widgets.Text(
                        $"{IntelLine(state, r)} · {UiTheme.FmtDuration(state.Tick - r.AtTick)} ago",
                        10, UiTheme.Dim);
                    meta.style.whiteSpace = WhiteSpace.Normal; // wrap, never bleed off-screen
                    row.Add(meta);
                    list.Add(row);
                }
            };
            refresh();
            return blocker;
        }

        // ---------- detail view (v1 MailDetailPanel) ----------

        /// <summary>Public entry — also used by UIController to pop fresh battle reports.</summary>
        public static void OpenReport(GameContext ctx, MailItem report) => OpenDetail(ctx, report);

        static void OpenDetail(GameContext ctx, MailItem report)
        {
            var ui = UIController.Instance!;
            report.Read = true;

            var (blocker, content, footer) = Widgets.ModalPanelFooter(DisplaySubject(report), ui.OpenMailbox, 70f);
            var state = ctx.State!;

            content.Add(Widgets.Text(
                $"Target {report.Target.X},{report.Target.Y} · {UiTheme.FmtDuration(state.Tick - report.AtTick)} ago",
                11, UiTheme.Dim));

            if (report is BattleMailReport battle)
            {
                var r = battle.Report;
                // Perspective: on a DEFENSE report the rival is the Attacker side,
                // so their win is your defeat (this used to read as your VICTORY).
                bool defending = battle.Defending;
                bool deflected = IsDeflection(battle);
                bool draw = r.Winner == BattleWinner.Draw;
                bool playerWon = defending ? r.Winner == BattleWinner.Defender : r.Winner == BattleWinner.Attacker;
                string outcome = deflected ? "DEFLECTED" : draw ? "STALEMATE" : playerWon ? "VICTORY" : "DEFEAT";
                var head = Widgets.Text(outcome, 18,
                    draw ? UiTheme.Energy : playerWon ? UiTheme.Good : UiTheme.Bad, bold: true);
                head.style.marginTop = 8;
                content.Add(head);
                if (deflected)
                {
                    var note = Widgets.Text("The raid broke on your Aegis Shield — no battle, nothing lost.", 11, UiTheme.Dim);
                    note.style.whiteSpace = WhiteSpace.Normal;
                    content.Add(note);
                }

                // Fleet breakdown: a labeled section with ONE hull per line (user
                // spec: don't crunch every ship type onto one run-on line).
                void FleetSection(string label, Dictionary<HullId, int> fleet)
                {
                    var header = Widgets.Text(label, 10, UiTheme.Accent, bold: true);
                    header.style.marginTop = 10;
                    content.Add(header);
                    bool any = false;
                    foreach (var hull in Ships.All)
                    {
                        if (!fleet.TryGetValue(hull, out int n) || n <= 0) continue;
                        any = true;
                        var line = Widgets.HBox(Justify.SpaceBetween);
                        line.style.marginTop = 2;
                        line.Add(Widgets.Text(Ships.Defs[hull].Name, 11, UiTheme.Dim));
                        line.Add(Widgets.Text($"× {n}", 11, UiTheme.Text, bold: true));
                        content.Add(line);
                    }
                    if (!any) content.Add(Widgets.Text("none", 11, UiTheme.Dim));
                }

                if (defending)
                {
                    FleetSection("YOUR DEFENDERS", r.Defender);
                    if (!deflected) FleetSection("YOUR SURVIVORS", r.DefenderSurvivors);
                    FleetSection("RAIDERS", r.Attacker);
                }
                else
                {
                    FleetSection("YOUR FLEET", r.Attacker);
                    FleetSection("SURVIVORS", r.AttackerSurvivors);
                    FleetSection("DEFENDERS", r.Defender);
                }

                var roundsLine = Widgets.HBox(Justify.SpaceBetween);
                roundsLine.style.marginTop = 10;
                roundsLine.Add(Widgets.Text("Rounds", 11, UiTheme.Dim));
                roundsLine.Add(Widgets.Text(r.Rounds.Count.ToString(), 11, UiTheme.Text));
                content.Add(roundsLine);

                if (r.Loot is { Total: > 0 } loot)
                {
                    var lootLine = Widgets.HBox(Justify.SpaceBetween);
                    lootLine.style.marginTop = 6;
                    lootLine.Add(Widgets.Text(defending ? "Plundered from you" : "Loot", 11, UiTheme.Dim));
                    lootLine.Add(Widgets.Text(
                        $"{UiTheme.FmtAmount(loot.Gold)} G  {UiTheme.FmtAmount(loot.Quartz)} Q  {UiTheme.FmtAmount(loot.Helium)} H",
                        11, defending ? UiTheme.Bad : UiTheme.Good));
                    content.Add(lootLine);
                }
            }
            else if (report is SpyReport spy)
            {
                // Rival-commander recon renders structured sections (GARRISON /
                // RESOURCES / BUILDINGS / RESEARCH, all the same row format —
                // user spec); camp/node scans keep the compact one-liner.
                bool commanderRecon = spy.Intel.Buildings != null || spy.Intel.LootableMilli != null;

                if (!commanderRecon)
                {
                    var intel = Widgets.Text(IntelLine(state, spy), 13, UiTheme.Text);
                    intel.style.whiteSpace = WhiteSpace.Normal;
                    intel.style.marginTop = 10;
                    content.Add(intel);
                    if (spy.Intel.CampLevel is int cl && cl > 0)
                        content.Add(Widgets.Text($"Camp level {cl}", 11, UiTheme.Dim));
                }

                VisualElement Section(string title)
                {
                    var header = Widgets.Text(title, 10, UiTheme.Accent, bold: true);
                    header.style.marginTop = 12;
                    content.Add(header);
                    return header;
                }
                void SectionRow(string label, string value)
                {
                    var row = Widgets.HBox(Justify.SpaceBetween);
                    row.style.marginTop = 2;
                    row.Add(Widgets.Text(label, 11, UiTheme.Dim));
                    row.Add(Widgets.Text(value, 11, UiTheme.Text, bold: true));
                    content.Add(row);
                }

                if (commanderRecon && spy.Intel.Garrison is { } garrison)
                {
                    Section("GARRISON");
                    bool anyShips = false;
                    foreach (var hull in Ships.All)
                    {
                        if (!garrison.TryGetValue(hull, out int n) || n <= 0) continue;
                        anyShips = true;
                        SectionRow(Ships.Defs[hull].Name, $"× {n}");
                    }
                    if (!anyShips)
                        content.Add(Widgets.Text("No ships docked.", 11, UiTheme.Dim));
                }

                if (spy.Intel.LootableMilli is { } lootable)
                {
                    Section("RESOURCES — LOOTABLE / SHIELDED");
                    var shielded = spy.Intel.ProtectedMilli ?? new ResourceBag();
                    SectionRow("Gold",
                        $"{UiTheme.FmtAmount(lootable.Gold)} / {UiTheme.FmtAmount(shielded.Gold)}");
                    SectionRow("Quartz",
                        $"{UiTheme.FmtAmount(lootable.Quartz)} / {UiTheme.FmtAmount(shielded.Quartz)}");
                    SectionRow("Helium",
                        $"{UiTheme.FmtAmount(lootable.Helium)} / {UiTheme.FmtAmount(shielded.Helium)}");
                }

                // Rival-commander recon: the full base + tech stack (user spec).
                if (spy.Intel.Buildings is { Count: > 0 } spiedBuildings)
                {
                    var header = Widgets.Text("BUILDINGS", 10, UiTheme.Accent, bold: true);
                    header.style.marginTop = 12;
                    content.Add(header);
                    foreach (var id in Buildings.All)
                    {
                        if (!spiedBuildings.TryGetValue(id, out int lvl) || lvl <= 0) continue;
                        var row = Widgets.HBox(Justify.SpaceBetween);
                        row.style.marginTop = 2;
                        row.Add(Widgets.Text(Buildings.Defs[id].Name, 11, UiTheme.Dim));
                        row.Add(Widgets.Text($"L{lvl}", 11, UiTheme.Text, bold: true));
                        content.Add(row);
                    }
                }
                if (spy.Intel.Research is { } spiedResearch)
                {
                    var header = Widgets.Text("RESEARCH", 10, UiTheme.Accent, bold: true);
                    header.style.marginTop = 12;
                    content.Add(header);
                    bool anyTech = false;
                    foreach (var id in Techs.All)
                    {
                        if (!spiedResearch.TryGetValue(id, out int lvl) || lvl <= 0) continue;
                        anyTech = true;
                        var row = Widgets.HBox(Justify.SpaceBetween);
                        row.style.marginTop = 2;
                        row.Add(Widgets.Text(Techs.Defs[id].Name, 11, UiTheme.Dim));
                        row.Add(Widgets.Text($"L{lvl}", 11, UiTheme.Text, bold: true));
                        content.Add(row);
                    }
                    if (!anyTech)
                        content.Add(Widgets.Text("No research completed yet.", 11, UiTheme.Dim));
                }
            }
            else if (report is RadarWarning warn)
            {
                var headline = Widgets.Text(IntelLine(state, warn), 14, UiTheme.Bad, bold: true);
                headline.style.whiteSpace = WhiteSpace.Normal;
                headline.style.marginTop = 10;
                content.Add(headline);
                if (warn.AttackerName != null)
                    content.Add(Widgets.Text($"Commander: {warn.AttackerName}", 12, UiTheme.Text));
                if (warn.FleetCount is int fc)
                    content.Add(Widgets.Text($"Contact size: {fc} ships", 12, UiTheme.Text));
                if (warn.FleetComp != null && warn.FleetComp.Count > 0)
                {
                    string compLine = string.Join("  ", warn.FleetComp
                        .Where(kv => kv.Value > 0)
                        .Select(kv => $"{kv.Value}× {Ships.Defs[kv.Key].Name}"));
                    content.Add(Widgets.Text($"Composition: {compLine}", 12, UiTheme.Text));
                }
                var hint = Widgets.Text(
                    "Upgrade your Radar Station for earlier warnings and sharper contact detail.",
                    10, UiTheme.Dim);
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.marginTop = 6;
                content.Add(hint);
            }

            // Actions live in the pinned footer — always at the bottom of the box,
            // not trailing the (variable-length) report text (user spec).
            var buttons = Widgets.HBox(Justify.SpaceAround);

            // Painted star — "☆ SAVE" was outside the runtime font's glyph set.
            var favBtn = Widgets.IconButton(report.Favorite ? Icon.Star : Icon.StarOutline,
                report.Favorite ? "SAVED" : "SAVE", () =>
            {
                report.Favorite = !report.Favorite;
                OpenDetail(ctx, report); // rebuild with the new star state
            }, 11);
            buttons.Add(favBtn);

            buttons.Add(Widgets.TextButton("VIEW ON MAP", () =>
            {
                ui.CloseModal();
                ui.SwitchView(ViewId.Map);
                GetMapView(ctx)?.FocusTile(report.Target);
            }, 11));

            buttons.Add(Widgets.TextButton("DELETE", () =>
            {
                if (report.Favorite)
                {
                    ui.Toast("Un-favorite before deleting");
                    return;
                }
                state.Mailbox.RemoveAll(m => m.Id == report.Id);
                ui.OpenMailbox();
            }, 11));

            footer.Add(buttons);
            ui.OpenModal(blocker);
        }

        static MapView? GetMapView(GameContext ctx) => ctx.GetComponent<MapView>();
    }
}
