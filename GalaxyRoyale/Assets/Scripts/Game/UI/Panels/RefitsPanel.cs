// REFITS (2026-09-30; MORE › REFITS): one module per warship class, and the
// blueprints found so far. Pirate Lords carry the blueprints (each lord the
// same one every time you beat them, so a rematch raises its Mk).
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class RefitsPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("REFITS", ui.CloseModal, 84f);
            var body = new VisualElement();
            content.Add(body);
            string? choosing = null; // the class whose module list is open

            void Render()
            {
                var s = ctx.State!;
                body.Clear();
                body.Add(Wrap(Widgets.Text("Each warship class takes one module: a trade between firepower and staying power. " +
                    "Refits are free and instant, so fit for the fight ahead.", 11, UiTheme.Dim), 0));

                body.Add(Section("FITTED"));
                foreach (var cls in Modules.Classes)
                {
                    var c = cls;
                    var fitted = ModuleSystem.FittedTo(s, c);
                    var row = Widgets.Row();
                    row.style.marginTop = 5;
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text(c, 12, UiTheme.Text, bold: true));
                    var change = Widgets.TextButton(choosing == c ? "DONE" : "CHANGE", () =>
                    {
                        choosing = choosing == c ? null : c;
                        GameAudio.Tap();
                        Render();
                    }, 10);
                    change.style.width = 90;
                    head.Add(change);
                    row.Add(head);
                    row.Add(Wrap(Widgets.Text(fitted is { } f ? Describe(s, f) : "No module fitted",
                        11, fitted != null ? UiTheme.Accent : UiTheme.Dim), 2));
                    if (choosing == c)
                    {
                        void Pick(ModuleKind? kind)
                        {
                            if (!ModuleSystem.Fit(ctx.State!, c, kind).Ok) return;
                            GameAudio.Feedback(Sfx.Confirm, Haptic.Light);
                            choosing = null;
                            LocalBootstrap.RequestSync();
                            Render();
                        }
                        var none = Widgets.TextButton("NO MODULE", () => Pick(null), 10);
                        none.style.marginTop = 6;
                        row.Add(none);
                        foreach (var def in Modules.All)
                        {
                            if (!ModuleSystem.Owned(s, def.Kind)) continue;
                            var k = def.Kind;
                            var b = Widgets.TextButton(Describe(s, k), () => Pick(k), 10);
                            b.style.marginTop = 4;
                            row.Add(b);
                        }
                    }
                    body.Add(row);
                }

                body.Add(Section("BLUEPRINTS"));
                foreach (var def in Modules.All)
                {
                    int mark = ModuleSystem.Mark(s, def.Kind);
                    var row = Widgets.Row();
                    row.style.marginTop = 5;
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text(def.Name, 12, mark > 0 ? UiTheme.Text : UiTheme.Dim, bold: true));
                    head.Add(Widgets.Text(mark > 0 ? Modules.MarkName(mark) : "NOT FOUND", 10, mark > 0 ? UiTheme.Good : UiTheme.Dim, bold: true));
                    row.Add(head);
                    row.Add(Wrap(Widgets.Text($"{def.Blurb} {Effect(def, Math.Max(1, mark))}", 11, UiTheme.Dim), 2));
                    if (mark < Modules.MaxMark)
                    {
                        string carrier = "";
                        foreach (var lord in PirateLords.All)
                            if (lord.Index % Modules.All.Count == (int)def.Kind) carrier += (carrier.Length > 0 ? " or " : "") + lord.Name;
                        row.Add(Wrap(Widgets.Text(mark == 0 ? $"Carried by {carrier}" : $"Another copy from {carrier} raises it to {Modules.MarkName(mark + 1)}",
                            10, UiTheme.Energy), 2));
                    }
                    body.Add(row);
                }
            }

            Render();
            ui.OpenModal(blocker);
        }

        static string Describe(GameState s, ModuleKind kind)
        {
            var def = Modules.Def(kind);
            return $"{def.Name} {Modules.MarkName(ModuleSystem.Mark(s, kind))} · {Effect(def, ModuleSystem.Mark(s, kind))}";
        }

        static string Effect(ModuleDef def, int mark)
        {
            float k = Modules.MarkScale(mark);
            string Pct(float v) => $"{(v >= 0 ? "+" : "−")}{Math.Abs(Math.Round(v * k * 100))}%";
            var parts = new System.Collections.Generic.List<string>();
            if (def.Atk != 0) parts.Add($"{Pct(def.Atk)} attack");
            if (def.Hp != 0) parts.Add($"{Pct(def.Hp)} hull");
            return string.Join(", ", parts);
        }

        static Label Section(string text)
        {
            var l = Widgets.Text(text, 10, UiTheme.Dim, bold: true);
            l.style.marginTop = 12;
            l.style.marginBottom = 4;
            return l;
        }

        static Label Wrap(Label l, int top)
        {
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = top;
            return l;
        }
    }
}
