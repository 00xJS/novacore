// MAILBOX — UI Toolkit port of v1's MailboxPanel + MailDetailPanel: per-kind
// tabs (ALL / BATTLES / SPY / ★ SAVED), tappable report rows, and a detail
// view with favorite / view-on-map / delete (favorites are delete-protected),
// plus STRIKE BACK / RAID / SPY shortcuts when the report is about a rival.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Combat;
using GalaxyRoyale.Sim.Systems;

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
            BossReport { Kind: BossReportKind.Missed } => UiTheme.Dim,
            BossReport => UiTheme.Energy,
            _ => UiTheme.Text,
        };

        /// <summary>Aegis deflections file a round-less report the defender "won".</summary>
        static bool IsDeflection(BattleMailReport battle) =>
            battle.Defending && battle.Report.Winner == BattleWinner.Defender && battle.Report.Rounds.Count == 0;

        /// <summary>Subjects saved by older builds carry a "⚠ " prefix that
        /// tofu-boxes on device — radar rows get a painted icon instead.</summary>
        static string DisplaySubject(MailItem item) =>
            item.Subject.StartsWith("⚠ ", StringComparison.Ordinal) ? item.Subject.Substring(2) : item.Subject;

        /// <summary>
        /// The rival commander a report is about: whoever raided you (stored id,
        /// or the name in an older report's subject), the commander you raided or
        /// scanned, or a radar contact your station identified. Null for pirate
        /// camps and unidentified contacts.
        /// </summary>
        static BotEmpire? RivalOf(GameContext ctx, MailItem item)
        {
            var galaxy = ctx.Bots;
            if (galaxy == null) return null;
            string? name = null;
            switch (item)
            {
                case BattleMailReport { Defending: true } defense:
                    if (defense.AttackerBotId > 0) return galaxy.Find(defense.AttackerBotId);
                    name = NameAfter(defense.Subject, "Colony raided by ")
                        ?? NameAfter(defense.Subject, "Raid repelled — ")
                        ?? NameAfter(defense.Subject, "Raid deflected — ", " hit your Aegis Shield");
                    break;
                case BattleMailReport battle: // camps are "Pirate camp LvN" — never a commander
                    name = battle.Report.DefenderName;
                    break;
                case SpyReport spy when spy.Intel.Buildings != null || spy.Intel.LootableMilli != null:
                    name = NameAfter(spy.Subject, "Recon: ");
                    break;
                case RadarWarning warn:
                    name = warn.AttackerName;
                    break;
            }
            if (string.IsNullOrEmpty(name)) return null;
            return galaxy.Bots.Find(b => b.Name == name);
        }

        /// <summary>Who you fought: the defender you hit, or the rival who raided you.</summary>
        internal static string EnemyNameOf(GameContext ctx, BattleMailReport mail) =>
            mail.Defending
                ? RivalOf(ctx, mail)?.Name ?? "Raiders"
                : mail.Report.DefenderName ?? "Defenders";

        static string? NameAfter(string subject, string prefix, string suffix = "")
        {
            if (!subject.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string rest = subject.Substring(prefix.Length);
            if (suffix.Length == 0) return rest;
            int cut = rest.IndexOf(suffix, StringComparison.Ordinal);
            return cut < 0 ? null : rest.Substring(0, cut);
        }

        static string IntelLine(GameState state, MailItem item)
        {
            if (item is BossReport boss)
                return boss.Kind switch
                {
                    BossReportKind.Missed => "It was gone before your fleet arrived",
                    BossReportKind.Result => $"{(boss.Killed ? "Destroyed" : "Escaped")} · you dealt {boss.YourDamage:N0} · +{boss.RewardDM} DM",
                    _ => $"{boss.Damage:N0} damage · {HullLeft(boss)} hull left · lost {Lost(boss)}",
                };
            if (item is BattleMailReport { Defending: true, GuardedBotId: CoreSystem.CoreGuardId } core)
                return core.Report.Winner == BattleWinner.Attacker
                    ? "Core lost — your garrison fell"
                    : "Core held — the assault was beaten off";
            if (item is BattleMailReport { Defending: true, GuardedBotId: > 0 } guard)
                // Your garrison at a clanmate's colony — their loss isn't yours.
                return guard.Report.Winner == BattleWinner.Attacker
                    ? "Garrison overrun — the raiders broke through"
                    : "Garrison held — the raid was beaten off";
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
                Tab.Battle => state.Mailbox.Where(m => m is BattleMailReport || m is BossReport).ToList(),
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
                    bool won = (r is BattleMailReport br && PlayerWon(br))
                        || r is BossReport { Kind: not BossReportKind.Missed };
                    var row = Widgets.Row();
                    if (won) row.style.backgroundColor = UiTheme.Wash(UiTheme.Good, r.Read ? 0.5f : 0.9f);
                    else if (isBattle) row.style.backgroundColor = UiTheme.Wash(UiTheme.Bad, r.Read ? 0.5f : 0.9f);
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
            var rival = RivalOf(ctx, report);

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
                if (BattleReplayPanel.CanReplay(battle))
                {
                    var watch = Widgets.IconButton(Icon.Play, "WATCH REPLAY", () =>
                        BattleReplayPanel.Open(ctx, battle, () => OpenDetail(ctx, battle)), 12);
                    watch.style.marginTop = 8;
                    watch.style.height = 38;
                    content.Add(watch);
                }
                if (!deflected && r.Rounds.Count > 0) content.Add(Correspondent(ctx, battle));

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

                // Clanmates who fought in either line (their ships are counted in
                // the fleets below).
                void SupportLine(Dictionary<HullId, int>? ships, string? names, string label, UnityEngine.Color color)
                {
                    if (ships == null || names == null) return;
                    int n = 0;
                    foreach (var kv in ships) n += kv.Value;
                    var help = Widgets.IconText(Icon.Pact, $"{label} — {n} warship{(n == 1 ? "" : "s")} from {names}",
                        11, color, bold: true);
                    help.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                    help.style.marginTop = 10;
                    content.Add(help);
                }

                if (defending)
                {
                    SupportLine(battle.AllyShips, battle.AllyNames, "CLAN REINFORCEMENTS", UiTheme.Good);
                    FleetSection(battle.AllyShips != null ? "DEFENDERS (YOURS + CLAN)" : "YOUR DEFENDERS", r.Defender);
                    if (!deflected) FleetSection("SURVIVORS", r.DefenderSurvivors);
                    FleetSection("RAIDERS", r.Attacker);
                }
                else
                {
                    SupportLine(battle.AllyShips, battle.AllyNames, battle.AllyLootMilli > 0
                        ? $"CLAN SUPPORT (carried home {UiTheme.FmtAmount(battle.AllyLootMilli)})" : "CLAN SUPPORT", UiTheme.Good);
                    SupportLine(battle.EnemyAllyShips, battle.EnemyAllyNames, "THEIR CLAN DEFENDED", UiTheme.Bad);
                    FleetSection(battle.AllyShips != null ? "YOUR FLEET (+ CLAN)" : "YOUR FLEET", r.Attacker);
                    FleetSection("SURVIVORS", r.AttackerSurvivors);
                    FleetSection(battle.EnemyAllyShips != null ? "DEFENDERS (+ THEIR CLAN)" : "DEFENDERS", r.Defender);
                }

                var roundsLine = Widgets.HBox(Justify.SpaceBetween);
                roundsLine.style.marginTop = 10;
                roundsLine.Add(Widgets.Text("Rounds", 11, UiTheme.Dim));
                roundsLine.Add(Widgets.Text(r.Rounds.Count.ToString(), 11, UiTheme.Text));
                content.Add(roundsLine);
                if (r.DefenderBattery > 0)
                {
                    var battery = Widgets.HBox(Justify.SpaceBetween);
                    battery.style.marginTop = 6;
                    battery.Add(Widgets.IconText(Icon.Target,
                        defending ? "Your Orbital Batteries" : "Their Orbital Batteries", 11, UiTheme.Dim));
                    battery.Add(Widgets.Text($"Lv {r.DefenderBattery}", 11, UiTheme.Text, bold: true));
                    content.Add(battery);
                }

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

                // The grudge a won raid earns (BotSystem.ApplyPlayerRaid marks it).
                if (!defending && playerWon && rival != null && BotSystem.HoldsGrudge(rival, state.Tick))
                {
                    var grudge = Widgets.IconText(Icon.Warning,
                        $"{rival.Name} wants revenge — expect a counter-raid once they can win.", 11, UiTheme.Bad);
                    grudge.Q<Label>("text").style.whiteSpace = WhiteSpace.Normal;
                    grudge.style.marginTop = 10;
                    content.Add(grudge);
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
            else if (report is BossReport boss)
            {
                void Line(string label, string value, UnityEngine.Color color)
                {
                    var row = Widgets.HBox(Justify.SpaceBetween);
                    row.style.marginTop = 3;
                    row.Add(Widgets.Text(label, 11, UiTheme.Dim));
                    row.Add(Widgets.Text(value, 11, color, bold: true));
                    content.Add(row);
                }
                string title = boss.Kind switch
                {
                    BossReportKind.Missed => "NOTHING THERE",
                    BossReportKind.Result => boss.Killed ? "DESTROYED" : "ESCAPED",
                    _ => boss.FinalBlow ? "FINAL BLOW" : "STRIKE LANDED",
                };
                var head = Widgets.Text(title, 18, boss.Kind == BossReportKind.Missed ? UiTheme.Dim : UiTheme.Energy, bold: true);
                head.style.marginTop = 8;
                content.Add(head);
                if (boss.Kind == BossReportKind.Missed)
                {
                    var note = Widgets.Text("The Pirate Dreadnought had broken apart or jumped away before your fleet arrived. " +
                        "Your ships are on their way home.", 11, UiTheme.Dim);
                    note.style.whiteSpace = WhiteSpace.Normal;
                    content.Add(note);
                }
                else if (boss.Kind == BossReportKind.Result)
                {
                    Line("Your damage", $"{boss.YourDamage:N0}", UiTheme.Text);
                    Line("Everyone's damage", $"{boss.TotalDamage:N0}", UiTheme.Text);
                    Line("Your place", $"#{boss.Rank} of {boss.Of}", UiTheme.Text);
                    if (boss.TopClan != null) Line("Top clan", boss.TopClan, UiTheme.Text);
                    Line("Your reward", $"+{boss.RewardDM} Dark Matter", UiTheme.Good);
                }
                else
                {
                    Line("Damage dealt", $"{boss.Damage:N0}", UiTheme.Text);
                    Line("Its hull", $"{boss.HpBefore:N0} → {boss.HpAfter:N0} ({HullLeft(boss)})", UiTheme.Text);
                    Line("Rounds", boss.Rounds.ToString(), UiTheme.Text);
                    if (boss.FinalBlow) Line("Final blow", $"+{BossSystem.FinalBlowDM} Dark Matter", UiTheme.Good);
                    if (boss.Salvage.Total > 0)
                        Line("Salvage", $"{UiTheme.FmtAmount(boss.Salvage.Gold)} G  {UiTheme.FmtAmount(boss.Salvage.Quartz)} Q  " +
                            $"{UiTheme.FmtAmount(boss.Salvage.Helium)} H", UiTheme.Good);
                    var fleetHead = Widgets.Text("YOUR FLEET", 10, UiTheme.Accent, bold: true);
                    fleetHead.style.marginTop = 10;
                    content.Add(fleetHead);
                    foreach (var hull in Ships.All)
                    {
                        if (!boss.Fleet.TryGetValue(hull, out int sent) || sent <= 0) continue;
                        int back = boss.Survivors.TryGetValue(hull, out var sv) ? sv : 0;
                        Line(Ships.Defs[hull].Name, $"{back:N0} / {sent:N0}", back < sent ? UiTheme.Bad : UiTheme.Text);
                    }
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
            if (rival != null)
            {
                // Answer a raid (or follow up your own) without hunting the
                // commander down on the map or in the rankings. A radar contact
                // hasn't landed yet — hitting their colony while the fleet is
                // out is a counter-raid.
                bool struck = report is BattleMailReport { Defending: true };
                bool incoming = report is RadarWarning;
                bool recon = report is SpyReport;
                // You raided their colony where it stands (an intercept met their
                // fleet out in space — that was no raid on them).
                bool raidedThere = report is BattleMailReport { Defending: false } own && own.Target.Equals(rival.HomeTile);
                int rivalId = rival.Id;
                string rivalName = rival.Name;
                var rivalRow = Widgets.HBox(Justify.SpaceAround);
                rivalRow.style.marginBottom = 8;
                var raidBtn = Widgets.IconButton(Icon.Swords,
                    struck ? "STRIKE BACK" : incoming ? "COUNTER-RAID" : raidedThere ? "RAID AGAIN" : "RAID", () =>
                {
                    ui.CloseModal();
                    RaidPanel.Open(ctx, rivalId);
                }, 11);
                raidBtn.style.width = Length.Percent(48f);
                rivalRow.Add(raidBtn);
                var second = recon
                    ? Widgets.TextButton("PROFILE", () =>
                    {
                        ui.CloseModal();
                        PlayerProfilePanel.Open(ctx, rivalId, rivalName);
                    }, 11)
                    : Widgets.IconButton(Icon.Eye, "SPY", () =>
                    {
                        var target = ctx.Bots?.Find(rivalId);
                        if (target == null) { ui.Toast("No telemetry on that commander"); return; }
                        ui.Toast(RaidService.SpyBot(ctx, BotSystem.SnapshotOf(target)).message);
                    }, 11);
                second.style.width = Length.Percent(48f);
                rivalRow.Add(second);
                footer.Add(rivalRow);
            }

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

        /// <summary>WAR CORRESPONDENT: the battle told as a story — the game's own telling at
        /// once; with the AI writers set up, the AI's (once per report per session).</summary>
        static VisualElement Correspondent(GameContext ctx, BattleMailReport battle)
        {
            var box = new VisualElement();
            box.style.marginTop = 10;
            var head = Widgets.Text("WAR CORRESPONDENT", 10, UiTheme.Accent, bold: true);
            box.Add(head);
            var story = Widgets.Text("", 11, UiTheme.Text);
            story.style.whiteSpace = WhiteSpace.Normal;
            story.style.marginTop = 3;
            story.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Italic;
            box.Add(story);
            var byline = Widgets.Text("", 9, UiTheme.Dim);
            byline.style.marginTop = 3;
            box.Add(byline);

            var facts = GalaxyRoyale.Sim.Text.FlavorText.RecapFacts(ctx.State!, ctx.Bots, battle);
            string fallback = GalaxyRoyale.Sim.Text.FlavorText.RecapFallback(facts, battle.Id);
            void Show(string text, bool ai)
            {
                story.text = AiWriter.Clean(text);
                byline.text = ai ? "Filed by the AI correspondent" : "";
                byline.style.display = ai ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Show(fallback, false);
            AiWriter.Write(ctx, "recap", facts, battle.Id, $"recap-{battle.Id}", fallback, Show);
            return box;
        }

        static string HullLeft(BossReport b) => b.MaxHp > 0 ? $"{Math.Round(b.HpAfter * 100.0 / b.MaxHp):0}%" : "?";

        static string Lost(BossReport b)
        {
            int sent = 0, back = 0;
            foreach (var v in b.Fleet.Values) sent += v;
            foreach (var v in b.Survivors.Values) back += v;
            return $"{sent - back:N0} of {sent:N0}";
        }
    }
}
