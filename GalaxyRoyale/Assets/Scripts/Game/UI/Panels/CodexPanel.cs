// CODEX (2026-09-30; Profile › CODEX & COLLECTIONS): nine collections of what
// the commander has met. Each shows found/total and every entry (unfound ones
// as "???"); a full collection earns a title and, for four, a planet skin
// that can only be earned, never bought. Earned skins are worn from here.
using UnityEngine.UIElements;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game.UI
{
    public static class CodexPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("CODEX", ui.OpenProfile, 86f);
            var body = new VisualElement();
            content.Add(body);
            string? open = null;
            string key = "";

            void Render()
            {
                var s = ctx.State!;
                string k = $"{s.Codex.Count}|{s.Skins.ActivePlanet}|{s.Skins.Owned.Count}|{open}";
                if (k == key) return;
                key = k;
                body.Clear();
                int found = 0, total = 0;
                foreach (var cat in Codex.Categories)
                {
                    var (f, t) = CodexSystem.Progress(s, cat);
                    found += f;
                    total += t;
                }
                body.Add(Wrap(Widgets.Text($"{found} of {total} entries. Fill a collection for a title; four of them also earn a planet skin " +
                    "you can't buy.", 11, UiTheme.Dim), 0));

                foreach (var cat in Codex.Categories)
                {
                    var c = cat;
                    var (f, t) = CodexSystem.Progress(s, cat);
                    bool full = f >= t;
                    var card = Widgets.Row();
                    card.style.marginTop = 6;
                    Widgets.SetBorder(card, full ? UiTheme.Good : UiTheme.Stroke, 1.2f);
                    var head = Widgets.HBox(Justify.SpaceBetween);
                    head.Add(Widgets.Text(cat.Name, 13, UiTheme.Text, bold: true));
                    head.Add(Widgets.Text($"{f} / {t}", 12, full ? UiTheme.Good : UiTheme.Accent, bold: true));
                    card.Add(head);
                    card.Add(Wrap(Widgets.Text(cat.How, 10, UiTheme.Dim), 2));
                    var (bar, fill, label) = Widgets.ProgressBar(8f);
                    bar.style.marginTop = 5;
                    fill.style.width = Length.Percent(t > 0 ? 100f * f / t : 0f);
                    label.text = "";
                    card.Add(bar);
                    if (cat.SkinId != null)
                    {
                        bool owned = s.Skins.Owned.Contains(cat.SkinId);
                        bool worn = s.Skins.ActivePlanet == cat.SkinId;
                        var skinRow = Widgets.HBox(Justify.SpaceBetween);
                        skinRow.style.marginTop = 5;
                        skinRow.Add(Widgets.Text($"Reward: {cat.SkinName} planet skin", 10, owned ? UiTheme.Good : UiTheme.Energy));
                        if (owned)
                        {
                            var wear = Widgets.TextButton(worn ? "WORN" : "WEAR", () =>
                            {
                                ShopSystem.ApplySkin(ctx.State!, c.SkinId!);
                                GameAudio.Feedback(Sfx.Confirm, Haptic.Light);
                                LocalBootstrap.RequestSync();
                                key = "";
                                Render();
                            }, 9);
                            wear.style.width = 70;
                            Widgets.SetButtonEnabled(wear, !worn);
                            skinRow.Add(wear);
                        }
                        card.Add(skinRow);
                    }
                    var toggle = Widgets.TextButton(open == c.Id ? "HIDE ENTRIES" : "SHOW ENTRIES", () =>
                    {
                        open = open == c.Id ? null : c.Id;
                        GameAudio.Tap();
                        key = "";
                        Render();
                    }, 9);
                    toggle.style.marginTop = 6;
                    toggle.style.height = 28;
                    card.Add(toggle);
                    if (open == c.Id)
                        foreach (var e in cat.Entries)
                        {
                            bool has = CodexSystem.Has(s, e.Key);
                            var line = Widgets.Text(has ? e.Label : "???", 11, has ? UiTheme.Text : UiTheme.Dim);
                            line.style.marginTop = 3;
                            card.Add(line);
                        }
                    body.Add(card);
                }

                // Festival skins (2026-09-30): earned at the seasonal festivals, worn from here too.
                var fests = new System.Collections.Generic.List<FestivalDef>();
                foreach (var f in Festivals.All) if (s.Skins.Owned.Contains(f.SkinId)) fests.Add(f);
                if (fests.Count > 0)
                {
                    var h = Widgets.Text("FESTIVAL SKINS", 10, UiTheme.Dim, bold: true);
                    h.style.marginTop = 12;
                    body.Add(h);
                    foreach (var f in fests)
                    {
                        var skin = f.SkinId;
                        var row = Widgets.HBox(Justify.SpaceBetween);
                        row.style.marginTop = 5;
                        row.Add(Widgets.Text($"{f.SkinName} · {f.Name}", 11, UiTheme.Text));
                        bool worn = s.Skins.ActivePlanet == skin;
                        var wear = Widgets.TextButton(worn ? "WORN" : "WEAR", () =>
                        {
                            ShopSystem.ApplySkin(ctx.State!, skin);
                            GameAudio.Feedback(Sfx.Confirm, Haptic.Light);
                            LocalBootstrap.RequestSync();
                            key = "";
                            Render();
                        }, 9);
                        wear.style.width = 70;
                        Widgets.SetButtonEnabled(wear, !worn);
                        row.Add(wear);
                        body.Add(row);
                    }
                }
            }

            Render();
            blocker.schedule.Execute(Render).Every(2000); // entries land once a minute
            ui.OpenModal(blocker);
        }

        static Label Wrap(Label l, int top)
        {
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = top;
            return l;
        }
    }
}
