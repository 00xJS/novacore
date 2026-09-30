// The Commander's Handbook (2026-09-30): a list of topics, each opening a short
// article (Data/Handbook). Settings › Help and the training's last step open it.
using UnityEngine.UIElements;
using GalaxyRoyale.Data;

namespace GalaxyRoyale.Game.UI
{
    public static class HandbookPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("COMMANDER'S HANDBOOK", ui.CloseModal, 80f);
            var intro = Widgets.Text("Everything in Galaxy Royale, a topic at a time.", 12, UiTheme.Dim);
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 6;
            content.Add(intro);
            for (int i = 0; i < Handbook.Topics.Count; i++)
            {
                var topic = Handbook.Topics[i];
                int index = i;
                var row = new Button(() => OpenTopic(ctx, index)) { text = "", name = $"handbook-{i}" };
                row.clicked += GameAudio.Tap;
                row.style.marginTop = 6;
                row.style.marginLeft = 0;
                row.style.marginRight = 0;
                row.style.paddingLeft = 12;
                row.style.paddingRight = 10;
                row.style.paddingTop = 9;
                row.style.paddingBottom = 9;
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.justifyContent = Justify.SpaceBetween;
                Holo.Frame(row, UiTheme.A(UiTheme.Accent, 0.06f), UiTheme.A(UiTheme.Accent, 0.4f), 8f);
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.style.flexShrink = 1;
                text.style.flexGrow = 1;
                var title = Widgets.Text(topic.Title, 13, UiTheme.Text, bold: true);
                title.pickingMode = PickingMode.Ignore;
                title.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft; // buttons centre text
                text.Add(title);
                var summary = Widgets.Text(topic.Summary, 11, UiTheme.Dim);
                summary.pickingMode = PickingMode.Ignore;
                summary.style.whiteSpace = WhiteSpace.Normal;
                summary.style.unityTextAlign = UnityEngine.TextAnchor.MiddleLeft;
                text.Add(summary);
                row.Add(text);
                var chevron = Icons.Make(Icon.ChevronRight, 16f, UiTheme.Accent);
                chevron.style.marginLeft = 8;
                row.Add(chevron);
                content.Add(row);
            }
            ui.OpenModal(blocker);
        }

        static void OpenTopic(GameContext ctx, int index)
        {
            var ui = UIController.Instance!;
            var topic = Handbook.Topics[index];
            var (blocker, content, footer) = Widgets.ModalPanelFooter(topic.Title.ToUpperInvariant(), () => Open(ctx), 80f);
            foreach (var paragraph in topic.Paragraphs)
            {
                var p = Widgets.Text(paragraph, 13, UiTheme.Text);
                p.style.whiteSpace = WhiteSpace.Normal;
                p.style.marginBottom = 10;
                content.Add(p);
            }
            var nav = Widgets.HBox(Justify.SpaceBetween);
            var back = Widgets.TextButton("ALL TOPICS", () => Open(ctx), 11);
            back.style.width = Length.Percent(48f);
            back.style.height = 38;
            nav.Add(back);
            if (index + 1 < Handbook.Topics.Count)
            {
                var next = Widgets.Primary(Widgets.TextButton("NEXT TOPIC", () => OpenTopic(ctx, index + 1), 11));
                next.style.width = Length.Percent(48f);
                next.style.height = 38;
                nav.Add(next);
            }
            footer.Add(nav);
            ui.OpenModal(blocker);
        }
    }
}
