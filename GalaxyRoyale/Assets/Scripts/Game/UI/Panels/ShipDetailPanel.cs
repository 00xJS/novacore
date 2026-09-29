// Ship spec card — tap a hull's picture in the Shipyard (user request: a
// photo/placeholder per ship and "another layer of detail"). Hero picture,
// class + role, flavor text, stat bars scaled against the whole roster, the
// counter triangle BOTH ways (tap to hop between hulls), unlock requirements,
// and the hull-specific research lines that buff it.
using System;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class ShipDetailPanel
    {
        public static void Open(GameContext ctx, HullId hull, Action onBack)
        {
            var ui = UIController.Instance!;
            var state = ctx.State!;
            var def = Ships.Defs[hull];
            var role = ShipArt.RoleColor(hull);
            var (blocker, content) = Widgets.ModalPanel(def.Name.ToUpper(), onBack, 86f);

            content.Add(ShipArt.Card(hull, 0f, 176f));

            var tags = Widgets.HBox(Justify.SpaceBetween);
            tags.style.marginTop = 8;
            string roleName = ShipLore.RoleOf(hull);
            tags.Add(Widgets.Text(roleName.Length > 0 ? $"{def.Class.ToUpper()} · {roleName.ToUpper()}" : def.Class.ToUpper(),
                11, role, bold: true));
            int docked = state.Ships.TryGetValue(hull, out var n) ? n : 0;
            tags.Add(Widgets.Text($"DOCKED × {docked}", 11, UiTheme.Accent, bold: true));
            content.Add(tags);

            var desc = Widgets.Text(ShipLore.DescOf(hull), 12, UiTheme.Text);
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.marginTop = 6;
            content.Add(desc);

            void Section(string title)
            {
                var header = Widgets.Text(title, 10, UiTheme.Dim, bold: true);
                header.style.marginTop = 14;
                header.style.marginBottom = 2;
                content.Add(header);
            }

            // ---- stats, each bar scaled to the strongest hull in the roster ----
            Section("COMBAT PROFILE");
            int maxAtk = 1, maxShd = 1, maxHp = 1, maxSpd = 1, maxCargo = 1;
            foreach (var h in Ships.All)
            {
                var d = Ships.Defs[h];
                maxAtk = Math.Max(maxAtk, d.Atk);
                maxShd = Math.Max(maxShd, d.Shield);
                maxHp = Math.Max(maxHp, d.Hp);
                maxSpd = Math.Max(maxSpd, d.Speed);
                maxCargo = Math.Max(maxCargo, d.Cargo);
            }
            void StatBar(string label, int value, int max, string shown, Color color)
            {
                var row = Widgets.HBox();
                row.style.marginTop = 4;
                var name = Widgets.Text(label, 11, UiTheme.Dim);
                name.style.width = Length.Percent(24f);
                row.Add(name);
                // A segmented hologram gauge: tinted track, lit fill, ten cells.
                var track = new VisualElement();
                track.style.flexGrow = 1f;
                track.style.height = 9;
                track.style.backgroundColor = UiTheme.A(color, 0.1f);
                Widgets.SetBorder(track, UiTheme.A(color, 0.35f), 1f);
                var fill = new VisualElement();
                fill.style.height = Length.Percent(100f);
                fill.style.width = Length.Percent(Mathf.Clamp(100f * value / max, value > 0 ? 3f : 0f, 100f));
                fill.style.backgroundColor = UiTheme.A(color, 0.85f);
                track.Add(fill);
                for (int cell = 1; cell < 10; cell++)
                {
                    var gap = new VisualElement { pickingMode = PickingMode.Ignore };
                    gap.style.position = Position.Absolute;
                    gap.style.left = Length.Percent(cell * 10f);
                    gap.style.top = 0;
                    gap.style.bottom = 0;
                    gap.style.width = 2;
                    gap.style.backgroundColor = UiTheme.A(UiTheme.Bg, 0.9f);
                    track.Add(gap);
                }
                row.Add(track);
                var val = Widgets.Text(shown, 11, UiTheme.Text, bold: true);
                val.style.width = Length.Percent(20f);
                val.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Add(val);
                content.Add(row);
            }
            StatBar("Attack", def.Atk, maxAtk, def.Atk.ToString(), UiTheme.Bad);
            StatBar("Shield", def.Shield, maxShd, def.Shield.ToString(), UiTheme.Accent);
            StatBar("Hull", def.Hp, maxHp, UiTheme.FmtCount(def.Hp), UiTheme.Good);
            StatBar("Speed", def.Speed, maxSpd, $"{def.Speed}/min", UiTheme.Energy);
            StatBar("Cargo", def.Cargo, maxCargo, UiTheme.FmtCount(def.Cargo / 1000), UiTheme.Gold);

            void Line(string label, string value, Color color)
            {
                var row = Widgets.HBox(Justify.SpaceBetween);
                row.style.marginTop = 5;
                row.Add(Widgets.Text(label, 11, UiTheme.Dim));
                row.Add(Widgets.Text(value, 11, color, bold: true));
                content.Add(row);
            }
            var cost = def.Cost;
            var costParts = new System.Collections.Generic.List<string>();
            if (cost.Gold > 0) costParts.Add($"{UiTheme.FmtCount(cost.Gold)} G");
            if (cost.Quartz > 0) costParts.Add($"{UiTheme.FmtCount(cost.Quartz)} Q");
            if (cost.Helium > 0) costParts.Add($"{UiTheme.FmtCount(cost.Helium)} H");
            Line("Cost per ship", costParts.Count > 0 ? string.Join(" · ", costParts) : "free", UiTheme.Text);
            Line("Build time", UiTheme.FmtDuration(FleetSystem.ShipBuildTime(state, hull)), UiTheme.Text);

            // ---- the counter triangle, both directions (tap to hop hulls) ----
            HullId? weakTo = null;
            foreach (var h in Ships.All)
                if (Ships.Defs[h].Counters == hull) { weakTo = h; break; }
            if (def.Counters != null || weakTo != null)
            {
                Section("MATCHUPS");
                void Matchup(string caption, HullId other, Color color)
                {
                    var btn = Widgets.TextButton("", () => Open(ctx, other, onBack), 11);
                    btn.style.flexDirection = FlexDirection.Row;
                    btn.style.justifyContent = Justify.SpaceBetween;
                    btn.style.alignItems = Align.Center;
                    btn.style.marginTop = 4;
                    btn.style.paddingTop = 4;
                    btn.style.paddingBottom = 4;
                    var left = Widgets.HBox();
                    left.pickingMode = PickingMode.Ignore;
                    left.Add(ShipArt.Card(other, 34f, 34f));
                    var text = Widgets.Text($"{caption} {Ships.Defs[other].Name}", 12, color, bold: true);
                    text.pickingMode = PickingMode.Ignore;
                    text.style.marginLeft = 8;
                    left.Add(text);
                    btn.Add(left);
                    btn.Add(Icons.Make(Icon.ChevronRight, 12, UiTheme.Accent));
                    content.Add(btn);
                }
                if (def.Counters is HullId strong) Matchup("Deals 2× damage to", strong, UiTheme.Good);
                if (weakTo is HullId weak) Matchup("Takes 2× damage from", weak, UiTheme.Bad);
            }

            // ---- unlock path ----
            Section("REQUIREMENTS");
            string? blockerText = FleetSystem.UnlockBlocker(state, hull);
            bool anyUnmet = false;
            int yard = state.Buildings[BuildingId.Shipyard].Level;
            anyUnmet |= yard < def.ShipyardLevelReq;
            Line($"Shipyard Lv {def.ShipyardLevelReq}", yard >= def.ShipyardLevelReq ? "met" : $"you have Lv {yard}",
                yard >= def.ShipyardLevelReq ? UiTheme.Good : UiTheme.Bad);
            foreach (var req in def.TechReqs)
            {
                int have = state.Research.TryGetValue(req.Tech, out var lvl) ? lvl : 0;
                anyUnmet |= have < req.Level;
                Line($"{Techs.Defs[req.Tech].Name} Lv {req.Level}", have >= req.Level ? "met" : $"you have Lv {have}",
                    have >= req.Level ? UiTheme.Good : UiTheme.Bad);
            }
            // The rows above already say what's missing; only add a line when the
            // hull is ready, or blocked by something the rows don't cover.
            if (blockerText == null || !anyUnmet)
            {
                var status = Widgets.Text(blockerText ?? "Ready to build in the Shipyard.", 11,
                    blockerText == null ? UiTheme.Good : UiTheme.Bad);
                status.style.whiteSpace = WhiteSpace.Normal;
                status.style.marginTop = 6;
                content.Add(status);
            }

            // ---- research that buffs THIS hull ----
            bool anyScoped = false;
            foreach (var tech in Techs.All)
            {
                var tdef = Techs.Defs[tech];
                if (tdef.HullScope != hull) continue;
                if (!anyScoped) { Section("HULL RESEARCH"); anyScoped = true; }
                int lvl = state.Research.TryGetValue(tech, out var l) ? l : 0;
                var row = Widgets.HBox();
                row.style.marginTop = 4;
                row.Add(ResearchArt.Emblem(tech, 26f, dim: lvl == 0));
                var name = Widgets.Text(tdef.Name, 11, lvl > 0 ? UiTheme.Text : UiTheme.Dim);
                name.style.marginLeft = 8;
                name.style.flexGrow = 1f;
                row.Add(name);
                row.Add(Widgets.Text($"Lv {lvl}/{tdef.MaxLevel}", 11, UiTheme.Accent, bold: true));
                content.Add(row);
            }

            ui.OpenModal(blocker);
        }
    }
}
