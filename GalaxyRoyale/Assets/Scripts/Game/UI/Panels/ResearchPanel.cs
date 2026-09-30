// The tech TREE (redesigned 2026-07-05 per user request): instead of stacked
// text rows, each category renders as skill-tree chains — circular tech nodes
// connected by prerequisite lines, with a detail card for the selected node
// (effect now → next, cost, RESEARCH / SPEED / cancel). Scales naturally as
// the reference-doc techs get added.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class ResearchPanel
    {
        static readonly UnityEngine.Color ResearchAccent = new(0.73f, 0.65f, 1f);
        static readonly UnityEngine.Color Maxed = new(1f, 0.8f, 0.3f);

        static string CatLabel(TechCategory c) => c switch
        {
            TechCategory.Economy => "ECON",
            TechCategory.Logistics => "LOGI",
            TechCategory.Military => "MIL",
            TechCategory.Industry => "IND",
            TechCategory.Defense => "DEF",
            _ => c.ToString().ToUpper(),
        };

        static int PageOf(TechCategory c) =>
            c == TechCategory.Military ? 1 : c == TechCategory.Defense ? 2 : 0;

        static bool IsReduce(TechEffectKind k) =>
            k == TechEffectKind.HeliumReduce || k == TechEffectKind.BuildTimeReduce
            || k == TechEffectKind.ShipTimeReduce || k == TechEffectKind.ResearchTimeReduce;

        static string EffectDesc(TechDef def)
        {
            string scope = def.HullScope is HullId hull ? Ships.Defs[hull].Name : "fleet";
            return def.Effect switch
            {
                TechEffectKind.ProdMultiplier => "resource production",
                TechEffectKind.MarchSpeedMult => "march speed",
                TechEffectKind.CargoMult => "cargo capacity",
                TechEffectKind.HeliumReduce => "march helium cost",
                TechEffectKind.AtkMult => $"{scope} attack",
                TechEffectKind.HpMult => $"{scope} durability",
                TechEffectKind.BuildTimeReduce => "build time",
                TechEffectKind.GatherRateMult => "node gathering speed",
                TechEffectKind.ShieldCapMult => "raid-shielded storage",
                TechEffectKind.ShipTimeReduce => "ship build time",
                TechEffectKind.ResearchTimeReduce => "research time",
                TechEffectKind.ShieldMult => "ship shields",
                TechEffectKind.DefAtkMult => "home defenders' attack",
                TechEffectKind.DefHpMult => "home defenders' durability",
                TechEffectKind.DefShieldMult => "home defenders' shields",
                TechEffectKind.OrbitalBattery => "battery fire per round",
                TechEffectKind.XpMult => "commander XP",
                _ => "",
            };
        }

        static string EffectLabel(TechId id, int level)
        {
            var def = Techs.Defs[id];
            if (def.Effect == TechEffectKind.OrbitalBattery) // flat damage, not a percentage
                return $"{level * Balance.BatteryDamagePerLevel:N0} dmg";
            int pct = (int)Math.Round(level * def.PerLevel * 100);
            return $"{(IsReduce(def.Effect) ? "-" : "+")}{pct}%";
        }

        public static VisualElement Build(GameContext ctx, out Action refresh)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("RESEARCH LAB", ui.CloseModal, 84f);

            var labLine = Widgets.Text("", 11, UiTheme.Dim);
            labLine.style.whiteSpace = WhiteSpace.Normal;
            content.Add(labLine);

            // In-progress strips (slot rows with SPEED / cancel).
            var progressList = new VisualElement();
            progressList.style.marginTop = 6;
            content.Add(progressList);

            // Pages instead of one tab per category (user request): ECONOMY,
            // COMBAT, and DEFENSE (the home-defense branch, 2026-09-27).
            var catRow = Widgets.HBox(Justify.SpaceAround);
            catRow.style.marginTop = 8;
            catRow.style.marginBottom = 10;
            int page = 0; // 0 = ECONOMY · 1 = COMBAT · 2 = DEFENSE
            TechId? selected = null;
            // Three independent change-keys (tree / detail sheet / progress strips).
            // The old single key included the tick + gold, so the WHOLE tree and its
            // buttons were torn down and rebuilt every second — a tap landing across
            // a rebuild could be lost. Now each part rebuilds only when it changes.
            string treeCache = "", detailCache = "", progressCache = "";
            void Invalidate() { treeCache = ""; detailCache = ""; progressCache = ""; }
            var econTab = Widgets.TextButton("ECONOMY", null!, 12);
            var combatTab = Widgets.TextButton("COMBAT", null!, 12);
            var defenseTab = Widgets.TextButton("DEFENSE", null!, 12);
            var tabs = new[] { econTab, combatTab, defenseTab };
            for (int i = 0; i < tabs.Length; i++)
            {
                int p = i;
                tabs[i].name = $"tut-research-tab-{i}";
                tabs[i].style.width = Length.Percent(31.5f);
                tabs[i].clicked += () => { page = p; selected = null; Invalidate(); };
                catRow.Add(tabs[i]);
            }
            content.Add(catRow);

            // The tree canvas fills the scroll; the detail is a BOTTOM SHEET pinned
            // to the modal, so tapping a node always shows its info in view (user
            // feedback: the old detail sat below the fold and was easy to miss).
            var tree = new VisualElement();
            content.Add(tree);

            var detail = new VisualElement();
            detail.style.position = Position.Absolute;
            detail.style.left = Length.Percent(6f);
            detail.style.right = Length.Percent(6f);
            detail.style.bottom = 78f; // sits just inside the modal's bottom edge
            detail.style.maxHeight = Length.Percent(44f);
            Holo.Frame(detail, UiTheme.Panel, ResearchAccent, 11f, 1.5f);
            detail.style.paddingLeft = 12;
            detail.style.paddingRight = 12;
            detail.style.paddingTop = 10;
            detail.style.paddingBottom = 12;
            detail.style.display = DisplayStyle.None;
            blocker.Add(detail); // floats over the tree, not inside the scroll

            // ---------- tree construction ----------

            // A readable tech CARD (redesign — the old tiny circles were hard to
            // parse): name + effect on the left, a level/state badge on the right,
            // state-tinted with a bright accent border when selected.
            VisualElement TechNode(TechId id)
            {
                var state = ctx.State!;
                var def = Techs.Defs[id];
                int level = ResearchSystem.TechLevel(state, id);
                bool researching = ResearchSystem.IsResearching(state, id);
                bool maxed = level >= def.MaxLevel;
                bool locked = !ResearchSystem.PrereqMet(state, id)
                    || state.Buildings[BuildingId.ResearchLab].Level < def.LabLevelReq;
                bool sel = id == selected;

                var color = maxed ? Maxed
                    : researching ? UiTheme.Good
                    : locked ? UiTheme.Stroke
                    : ResearchAccent;

                var card = new VisualElement();
                card.style.width = Length.Percent(100f);
                card.style.flexDirection = FlexDirection.Row;
                card.style.justifyContent = Justify.SpaceBetween;
                card.style.alignItems = Align.Center;
                card.style.paddingLeft = 10;
                card.style.paddingRight = 8;
                card.style.paddingTop = 8;
                card.style.paddingBottom = 8;
                Holo.Frame(card, new UnityEngine.Color(color.r, color.g, color.b, sel ? 0.30f : 0.12f), color, 9f, sel ? 2f : 1f);
                card.RegisterCallback<PointerUpEvent>(_ => { selected = id; Invalidate(); });
                card.name = $"tut-tech-{id}";

                // Tech emblem (drop-in Resources/Research art, else a painted badge).
                var emblem = ResearchArt.Emblem(id, 34f, dim: locked);
                emblem.style.marginRight = 10;
                card.Add(emblem);

                var left = new VisualElement();
                left.style.flexGrow = 1f;
                left.style.flexShrink = 1f;
                // Locked techs get a real LOCKED tag instead of "[locked]" text.
                var nameRow = Widgets.HBox(Justify.FlexStart, Align.Center);
                nameRow.style.flexWrap = Wrap.Wrap;
                var name = Widgets.Text(def.Name, 12, locked ? UiTheme.Dim : UiTheme.Text, bold: true);
                name.style.whiteSpace = WhiteSpace.Normal;
                name.style.flexShrink = 1f;
                nameRow.Add(name);
                if (locked)
                {
                    var tag = Widgets.Heading("LOCKED", 8, UiTheme.Energy, 1f);
                    tag.style.marginLeft = 6;
                    tag.style.paddingLeft = 5;
                    tag.style.paddingRight = 6; // + the tracking measurement misses
                    tag.style.paddingTop = 1;
                    tag.style.paddingBottom = 1;
                    Holo.Frame(tag, UiTheme.A(UiTheme.Energy, 0.1f), UiTheme.Energy, 3f, 1f, FrameShape.BevelAll);
                    nameRow.Add(tag);
                }
                left.Add(nameRow);
                string effLine = maxed ? "fully researched"
                    : $"{EffectDesc(def)}  {EffectLabel(id, level)} › {EffectLabel(id, level + 1)}";
                var effText = Widgets.Text(effLine, 9, locked ? UiTheme.Stroke : color);
                effText.style.whiteSpace = WhiteSpace.Normal;
                effText.style.marginTop = 2;
                left.Add(effText);
                card.Add(left);

                // Level/state badge.
                var badge = new VisualElement();
                badge.style.minWidth = 46;
                badge.style.alignItems = Align.Center;
                string badgeTop = maxed ? "MAX" : $"{level}/{def.MaxLevel}";
                badge.Add(Widgets.Text(badgeTop, 12, color, bold: true));
                // Painted state marks (were "..." / ">" text).
                if (researching) badge.Add(Icons.Make(Icon.Rotate, 13, UiTheme.Good));
                else badge.Add(Icons.Make(Icon.ChevronRight, 12, sel ? color : UiTheme.Stroke));
                card.Add(badge);
                return card;
            }

            // PAGES (user request): ECONOMY (Economy + Logistics + Industry),
            // COMBAT (Military) and DEFENSE. Techs flow DOWN as a vertical list of readable
            // cards — the old side-by-side nest ran out of width, especially on the
            // Military page — GROUPED and sorted by the Research-Lab level that
            // unlocks each one (early → late).
            void RenderTree()
            {
                tree.Clear();
                if (page == 2)
                {
                    var hint = Widgets.Text(
                        "Defense research counts only when your home colony is attacked — " +
                        "military research counts in every battle, attack or defense.", 10, UiTheme.Dim);
                    hint.style.whiteSpace = WhiteSpace.Normal;
                    hint.style.marginBottom = 4;
                    tree.Add(hint);
                }
                var techs = Techs.All
                    .Where(t => PageOf(Techs.Defs[t].Category) == page)
                    .OrderBy(t => Techs.Defs[t].LabLevelReq)
                    .ThenBy(t => Techs.Defs[t].Name)
                    .ToList();

                int lastLab = -1;
                foreach (var id in techs)
                {
                    int lab = Techs.Defs[id].LabLevelReq;
                    if (lab != lastLab)
                    {
                        lastLab = lab;
                        var hdr = Widgets.Text($"UNLOCKS AT RESEARCH LAB {lab}", 9, ResearchAccent, bold: true);
                        hdr.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
                        hdr.style.marginTop = 10;
                        hdr.style.marginBottom = 2;
                        tree.Add(hdr);
                    }
                    var wrap = new VisualElement();
                    wrap.style.marginBottom = 5;
                    wrap.Add(TechNode(id));
                    if (Techs.Defs[id].Requires is { } req)
                    {
                        var need = Widgets.Text($"needs {Techs.Defs[req.Tech].Name} Lv {req.Level}", 8, UiTheme.Dim);
                        need.style.marginLeft = 10;
                        wrap.Add(need);
                    }
                    tree.Add(wrap);
                }
            }

            // ---------- detail card ----------

            void RenderDetail()
            {
                detail.Clear();
                var state = ctx.State!;
                if (selected is not TechId id)
                {
                    detail.style.display = DisplayStyle.None; // no selection → hide the sheet
                    return;
                }
                detail.style.display = DisplayStyle.Flex;

                var def = Techs.Defs[id];
                int level = ResearchSystem.TechLevel(state, id);
                bool maxed = level >= def.MaxLevel;

                // Card content goes straight into the pinned sheet (no nested Row).
                var card = detail;
                var head = Widgets.HBox(Justify.SpaceBetween);
                var headLeft = Widgets.HBox();
                headLeft.style.flexShrink = 1f;
                var bigEmblem = ResearchArt.Emblem(id, 44f);
                bigEmblem.style.marginRight = 10;
                headLeft.Add(bigEmblem);
                var titleCol = new VisualElement();
                titleCol.style.flexShrink = 1f;
                var title = Widgets.Text(def.Name, 14, ResearchAccent, bold: true);
                title.style.whiteSpace = WhiteSpace.Normal;
                titleCol.Add(title);
                titleCol.Add(Widgets.Text(def.Category.ToString().ToUpper(), 9,
                    ResearchArt.CategoryColor(def.Category), bold: true));
                headLeft.Add(titleCol);
                head.Add(headLeft);
                var headRight = Widgets.HBox();
                headRight.Add(Widgets.Text($"Lv {level}/{def.MaxLevel}", 12, UiTheme.Text, bold: true));
                var closeBtn = Widgets.TextButton("×", () => { selected = null; Invalidate(); }, 11);
                closeBtn.style.marginLeft = 8;
                closeBtn.style.minWidth = 30;
                headRight.Add(closeBtn);
                head.Add(headRight);
                card.Add(head);

                string lore = TechLore.DescOf(id);
                if (lore.Length > 0)
                {
                    var flavor = Widgets.Text(lore, 11, UiTheme.Dim);
                    flavor.style.whiteSpace = WhiteSpace.Normal;
                    flavor.style.marginTop = 6;
                    card.Add(flavor);
                }

                string now = EffectLabel(id, level);
                string next = maxed ? "MAX" : EffectLabel(id, level + 1);
                var effectLine = Widgets.Text($"{EffectDesc(def)}: {now} › {next}", 12, UiTheme.Good);
                effectLine.style.marginTop = 6;
                card.Add(effectLine);

                if (def.Requires is { } req && ResearchSystem.TechLevel(state, req.Tech) < req.Level)
                {
                    var lockLine = Widgets.Text(
                        $"Requires {Techs.Defs[req.Tech].Name} Lv {req.Level}", 11, UiTheme.Energy);
                    lockLine.style.marginTop = 4;
                    card.Add(lockLine);
                }
                if (state.Buildings[BuildingId.ResearchLab].Level < def.LabLevelReq)
                {
                    var labLock = Widgets.Text($"Requires Research Lab Lv {def.LabLevelReq}", 11, UiTheme.Energy);
                    labLock.style.marginTop = 2;
                    card.Add(labLock);
                }

                if (!maxed)
                {
                    var cost = ResearchSystem.GetResearchCost(id, level + 1);
                    int time = ResearchSystem.GetResearchTime(state, id, level + 1);
                    // Spread each resource across the line instead of cramming them
                    // together with dead space on the right (user feedback).
                    var costLine = Widgets.HBox(Justify.SpaceBetween);
                    costLine.style.marginTop = 6;
                    if (cost.Gold > 0) costLine.Add(Widgets.Text($"{UiTheme.FmtAmount(cost.Gold)} G", 11, UiTheme.Gold, bold: true));
                    if (cost.Quartz > 0) costLine.Add(Widgets.Text($"{UiTheme.FmtAmount(cost.Quartz)} Q", 11, UiTheme.Quartz, bold: true));
                    if (cost.Helium > 0) costLine.Add(Widgets.Text($"{UiTheme.FmtAmount(cost.Helium)} H", 11, UiTheme.Helium, bold: true));
                    costLine.Add(Widgets.Text(UiTheme.FmtDuration(time), 11, UiTheme.Dim));
                    card.Add(costLine);

                    var check = ResearchSystem.CheckResearch(state, id);
                    if (!check.Ok && check.Reason == "Not enough resources")
                    {
                        var shop = Widgets.TextButton("GET RESOURCES IN SHOP", () => ui.OpenShop(), 10);
                        shop.style.marginTop = 6;
                        card.Add(shop);
                    }
                    // The user hit "the button doesn't work": a disabled RESEARCH with
                    // no visible reason (lab caps the tech level, slot busy, …). Always
                    // show WHY it's blocked, like BuildingPanel's status line.
                    if (!check.Ok && !ResearchSystem.IsResearching(state, id))
                    {
                        var why = Widgets.Text(check.Reason ?? "Cannot research", 11, UiTheme.Bad);
                        why.style.whiteSpace = WhiteSpace.Normal;
                        why.style.marginTop = 6;
                        card.Add(why);
                    }
                    var research = Widgets.TextButton(
                        ResearchSystem.IsResearching(state, id) ? "RESEARCHING…" : "RESEARCH", () =>
                    {
                        var res = ResearchSystem.StartResearch(ctx.State!, id);
                        ui.Toast(res.Ok ? $"Researching {def.Name}…" : res.Reason ?? "Cannot research");
                        if (res.Ok) GameAudio.Feedback(Sfx.Confirm, Haptic.Light);
                        else GameAudio.Feedback(Sfx.Error, Haptic.Error);
                        Invalidate();
                    }, 12);
                    Widgets.SetButtonEnabled(research,
                        check.Ok && !ResearchSystem.IsResearching(state, id));
                    research.name = "tut-research";
                    research.style.marginTop = 8;
                    research.style.height = 38;
                    card.Add(research);
                }
                else
                {
                    var done = Widgets.Text("Fully researched.", 11, Maxed);
                    done.style.marginTop = 6;
                    card.Add(done);
                }
                // card IS the pinned sheet now — nothing more to attach.
            }

            // In-progress strips are rebuilt only when the queue changes; their
            // countdowns are re-texted each tick (no button churn under a finger).
            var progressTimes = new List<(Label label, int index)>();
            string ProgressText(GalaxyRoyale.Sim.GameState s, int i)
            {
                var o = s.ResearchQueue[i];
                return $"{Techs.Defs[o.TechId].Name} › Lv {o.ToLevel} · " +
                       UiTheme.FmtDuration(Math.Max(0, o.EndsAtTick - s.Tick));
            }
            void UpdateProgressTimes()
            {
                var s = ctx.State!;
                foreach (var (label, index) in progressTimes)
                    if (index < s.ResearchQueue.Count) label.text = ProgressText(s, index);
            }

            void RenderProgress()
            {
                progressList.Clear();
                progressTimes.Clear();
                var state = ctx.State!;
                int slots = ResearchSystem.ResearchSlots(state);
                for (int i = 0; i < state.ResearchQueue.Count; i++)
                {
                    int index = i;
                    var strip = Widgets.HBox(Justify.SpaceBetween);
                    strip.style.marginTop = 3;
                    var timeLabel = Widgets.Text(ProgressText(state, i), 11, UiTheme.Good);
                    timeLabel.style.flexShrink = 1f;
                    progressTimes.Add((timeLabel, index));
                    strip.Add(timeLabel);
                    var btns = Widgets.HBox();
                    var speed = Widgets.TextButton("SPEED", () =>
                        SpeedUpPanel.OpenForResearch(ctx, index), 9);
                    speed.style.marginRight = 4;
                    btns.Add(speed);
                    btns.Add(Widgets.TextButton("×", () =>
                        ConfirmPanel.Open("Cancel this research?\nResources will be refunded.",
                            "CANCEL RESEARCH",
                            () => { ResearchSystem.CancelResearch(ctx.State!, index); ui.OpenResearch(); },
                            () => ui.OpenResearch()), 9));
                    strip.Add(btns);
                    progressList.Add(strip);
                }
                if (state.ResearchQueue.Count < slots)
                    progressList.Add(Widgets.Text(
                        $"{slots - state.ResearchQueue.Count} research slot(s) idle", 10, UiTheme.Dim));
            }

            int lastTick = -1;
            refresh = () =>
            {
                var state = ctx.State!;
                int lab = state.Buildings[BuildingId.ResearchLab].Level;

                // What the TREE shows: page, selection, lab gate, levels, what's running.
                var sb = new System.Text.StringBuilder();
                sb.Append(page).Append('|').Append(selected).Append('|').Append(lab).Append('|');
                foreach (var t in Techs.All) sb.Append(ResearchSystem.TechLevel(state, t)).Append(',');
                sb.Append('|');
                foreach (var o in state.ResearchQueue) sb.Append(o.TechId).Append(':').Append(o.ToLevel).Append(',');
                string treeKey = sb.ToString();

                // The DETAIL sheet also cares whether RESEARCH is currently allowed
                // (affordability, free slot) — not the raw resource numbers.
                string detailKey = treeKey;
                if (selected is TechId sel)
                {
                    var check = ResearchSystem.CheckResearch(state, sel);
                    detailKey += $"|{check.Ok}|{check.Reason}";
                }

                // Strips rebuild when the queue's shape changes (start/finish/
                // cancel/speed-up); otherwise the countdowns tick in place.
                sb.Clear();
                foreach (var o in state.ResearchQueue) sb.Append(o.TechId).Append(':').Append(o.EndsAtTick).Append(',');
                sb.Append('|').Append(ResearchSystem.ResearchSlots(state));
                string progressKey = sb.ToString();

                if (treeKey != treeCache)
                {
                    treeCache = treeKey;
                    labLine.text = lab >= 1
                        ? $"Research Lab Lv {lab} — techs cap at your lab level"
                        : "Build a Research Lab to unlock the tech tree";
                    for (int i = 0; i < tabs.Length; i++) Widgets.SetButtonHighlight(tabs[i], i == page);
                    RenderTree();
                }
                if (detailKey != detailCache) { detailCache = detailKey; RenderDetail(); }
                if (progressKey != progressCache) { progressCache = progressKey; RenderProgress(); }
                else if (state.Tick != lastTick) UpdateProgressTimes();
                lastTick = state.Tick;
            };
            refresh();
            return blocker;
        }
    }
}
