// PROJECTS (2026-09-30; MORE › PROJECTS): the four mega-projects. Each card
// shows its five stages, what the finished ones give, and the next stage's
// cost and build time; one stage builds at a time.
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class MegaprojectsPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("MEGA-PROJECTS", ui.CloseModal, 84f);
            var body = new VisualElement();
            content.Add(body);
            string key = "";

            void Render()
            {
                var s = ctx.State!;
                var m = s.Mega;
                string k = $"{m.Active}|{(m.EndsTick - s.Tick) / 60}|{s.Buildings[BuildingId.CommandCenter].Level}|{s.Resources.Total / 1_000_000}";
                foreach (var kv in m.Stages) k += $"|{kv.Key}:{kv.Value}";
                if (k == key) return;
                key = k;
                body.Clear();
                body.Add(Wrap(Widgets.Text("Colony-wide works for a grown colony. Each is built in five stages, one stage at a time; " +
                    "every finished stage adds its bonus for good.", 11, UiTheme.Dim), 0));

                foreach (var def in Megaprojects.All)
                {
                    int stage = MegaprojectSystem.Stage(s, def.Kind);
                    bool open = MegaprojectSystem.Unlocked(s, def.Kind);
                    bool building = m.Active == (int)def.Kind;
                    bool done = stage >= Megaprojects.Stages;
                    var card = Widgets.Row();
                    card.style.marginTop = 8;
                    Widgets.SetBorder(card, done ? UiTheme.Good : building ? UiTheme.Energy : open ? UiTheme.Accent : UiTheme.Stroke, 1.2f);

                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text(def.Name, 13, open ? UiTheme.Text : UiTheme.Dim, bold: true));
                    head.Add(Pips(stage, building));
                    card.Add(head);
                    card.Add(Wrap(Widgets.Text(def.Blurb, 11, UiTheme.Dim), 3));
                    card.Add(Wrap(Widgets.Text($"Each stage: {def.StageBonus}", 11, UiTheme.Text), 4));
                    if (stage > 0)
                        card.Add(Wrap(Widgets.Text($"Built {stage} of {Megaprojects.Stages}", 11, UiTheme.Good), 2));

                    if (!open)
                        card.Add(Wrap(Widgets.Text($"Opens at Command Center {def.UnlockCc}", 11, UiTheme.Energy, bold: true), 6));
                    else if (building)
                        card.Add(Wrap(Widgets.Text($"Stage {stage + 1} ready in {UiTheme.FmtLong(Math.Max(0, m.EndsTick - s.Tick))}", 12, UiTheme.Energy, bold: true), 6));
                    else if (!done)
                    {
                        var cost = MegaprojectSystem.Cost(s, def.Kind);
                        var kind = def.Kind;
                        var check = MegaprojectSystem.Check(s, kind);
                        var go = Widgets.Primary(Widgets.TextButton(
                            $"BUILD STAGE {stage + 1} · {UiTheme.FmtAmount(cost.Total)} · {UiTheme.FmtLong(MegaprojectSystem.BuildSeconds(s, kind))}", () =>
                        {
                            if (!MegaprojectSystem.Start(ctx.State!, kind).Ok) return;
                            GameAudio.Feedback(Sfx.Confirm, Haptic.Medium);
                            LocalBootstrap.RequestSync();
                            key = "";
                        }, 10));
                        go.style.marginTop = 8;
                        go.style.height = 36;
                        Widgets.SetButtonEnabled(go, check.Ok);
                        card.Add(go);
                        if (!check.Ok) card.Add(Wrap(Widgets.Text(check.Reason ?? "", 10, UiTheme.Dim), 3));
                    }
                    body.Add(card);
                }
            }

            Render();
            blocker.schedule.Execute(Render).Every(1000);
            ui.OpenModal(blocker);
        }

        /// <summary>Five stage pips: built ones filled, the one under way outlined bright.</summary>
        static VisualElement Pips(int built, bool building)
        {
            var row = Widgets.HBox();
            for (int i = 0; i < Megaprojects.Stages; i++)
            {
                var p = new VisualElement();
                p.style.width = 10;
                p.style.height = 10;
                p.style.marginLeft = 4;
                p.style.borderTopLeftRadius = p.style.borderTopRightRadius = p.style.borderBottomLeftRadius = p.style.borderBottomRightRadius = 2;
                bool filled = i < built;
                bool now = building && i == built;
                p.style.backgroundColor = filled ? UiTheme.Good : UnityEngine.Color.clear;
                Widgets.SetBorder(p, filled ? UiTheme.Good : now ? UiTheme.Energy : UiTheme.Stroke, 1.2f);
                row.Add(p);
            }
            return row;
        }

        static Label Wrap(Label l, int top)
        {
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = top;
            return l;
        }
    }
}
