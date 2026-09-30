// Find-nearest search — port of v1's SearchPanel: pick a node kind and an
// optional level (camps: garrison level; others: tier+1), then jump the map
// to the closest live match.
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class SearchPanel
    {
        /// <summary>The kind picked in the open panel (the training reads it).</summary>
        public static NodeKind Picked { get; private set; } = NodeKind.Asteroid;

        public static void Open(GameContext ctx)
        {
            Picked = NodeKind.Asteroid;
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("FIND NEAREST", ui.CloseModal, 0f); // compact

            var kind = NodeKind.Asteroid;
            int level = 0; // 0 = any
            var kindButtons = new System.Collections.Generic.Dictionary<NodeKind, Button>();
            var levelButtons = new System.Collections.Generic.Dictionary<int, Button>();

            // A live map event first (2026-09-30).
            if (ctx.State != null && EventSites.Focus(ctx.State) is { } spot)
            {
                var ev = Widgets.Primary(Widgets.TextButton(
                    $"{EventSystem.Current(ctx.State.Tick).Def.Name.ToUpperInvariant()}: SHOW ON MAP",
                    () => EventsPanel.ShowOnMap(ctx, spot.tile, spot.node), 11));
                ev.style.marginBottom = 10;
                ev.style.height = 38;
                content.Add(ev);
            }
            content.Add(Widgets.Text("WHAT", 10, UiTheme.Dim, bold: true));
            var kindList = new VisualElement();
            foreach (var k in Nodes.All)
            {
                var kk = k;
                var b = Widgets.TextButton(Nodes.Defs[kk].Name, () => { kind = kk; Picked = kk; Highlight(); }, 11);
                b.name = $"tut-search-{kk}";
                b.style.marginTop = 4;
                kindButtons[kk] = b;
                kindList.Add(b);
            }
            content.Add(kindList);

            content.Add(Widgets.Text("LEVEL", 10, UiTheme.Dim, bold: true));
            var levelRow = Widgets.HBox(Justify.SpaceBetween);
            levelRow.style.marginTop = 4;
            for (int l = 0; l <= 5; l++)
            {
                int ll = l;
                var b = Widgets.TextButton(ll == 0 ? "ANY" : ll.ToString(), () => { level = ll; Highlight(); }, 10);
                b.style.width = Length.Percent(15f);
                levelButtons[ll] = b;
                levelRow.Add(b);
            }
            content.Add(levelRow);

            var find = Widgets.TextButton("FIND", () =>
            {
                ui.CloseModal();
                var mapView = ctx.GetComponent<MapView>();
                if (mapView == null || !mapView.SearchNearest(kind, level))
                    ui.Toast(level > 0 ? $"No Lv{level} found nearby" : "None of that found nearby");
            }, 14);
            find.name = "tut-search-find";
            find.style.marginTop = 12;
            find.style.height = 40;
            content.Add(find);

            // Jump straight to coordinates instead of scrolling (user request).
            var jumpHeader = Widgets.Text("JUMP TO COORDINATES", 10, UiTheme.Dim, bold: true);
            jumpHeader.style.marginTop = 14;
            content.Add(jumpHeader);
            var jumpRow = Widgets.HBox(Justify.SpaceBetween);
            jumpRow.style.marginTop = 4;
            var xField = new TextField { maxLength = 4 };
            xField.style.width = Length.Percent(30f);
            var yField = new TextField { maxLength = 4 };
            yField.style.width = Length.Percent(30f);
            jumpRow.Add(xField);
            jumpRow.Add(yField);
            jumpRow.Add(Widgets.TextButton("GO", () =>
            {
                if (!int.TryParse(xField.value?.Trim(), out int x)
                    || !int.TryParse(yField.value?.Trim(), out int y)
                    || x < 0 || y < 0 || x >= Balance.SectorSize || y >= Balance.SectorSize)
                {
                    ui.Toast($"Enter coordinates 0–{Balance.SectorSize - 1}");
                    return;
                }
                ui.CloseModal();
                ctx.GetComponent<MapView>()?.FocusTile(new TileXY(x, y));
            }, 12));
            content.Add(jumpRow);
            content.Add(Widgets.Text("x above left · y above right", 9, UiTheme.Dim));

            void Highlight()
            {
                foreach (var kv in kindButtons) Widgets.SetButtonHighlight(kv.Value, kv.Key == kind);
                foreach (var kv in levelButtons) Widgets.SetButtonHighlight(kv.Value, kv.Key == level);
            }
            Highlight();

            ui.OpenModal(blocker);
        }
    }
}
