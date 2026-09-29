// HAIL (a rival's profile): open a channel and say something — a taunt, an
// offer of peace, clan talk or trade — and hear the commander answer in their
// own voice. Their temper, any grudge and how you compare in might shape the
// reply. It's flavour: the galaxy's rules decide what they actually do.
using System;
using UnityEngine.UIElements;
using GalaxyRoyale.Sim.Text;

namespace GalaxyRoyale.Game.UI
{
    public static class HailPanel
    {
        public static void Open(GameContext ctx, int botId)
        {
            var ui = UIController.Instance!;
            var bot = ctx.Bots?.Find(botId);
            if (ctx.State == null || ctx.Bots == null || bot == null) return;
            string name = bot.Name;
            var (blocker, content) = Widgets.ModalPanel($"HAIL {name.ToUpperInvariant()}", () => PlayerProfilePanel.Open(ctx, botId, name), 0f);

            var intro = Widgets.Text("Open a channel. What do you say?", 11, UiTheme.Dim);
            intro.style.whiteSpace = WhiteSpace.Normal;
            content.Add(intro);

            var reply = Widgets.Row();
            reply.style.marginTop = 10;
            var head = Widgets.HBox();
            var avatar = Portraits.Avatar(bot.State.Profile.AvatarSeed, name, 28);
            avatar.style.marginRight = 8;
            head.Add(avatar);
            head.Add(Widgets.Text(name, 12, UiTheme.Accent, bold: true));
            reply.Add(head);
            var words = Widgets.Text("", 12, UiTheme.Text);
            words.style.whiteSpace = WhiteSpace.Normal;
            words.style.marginTop = 6;
            reply.Add(words);
            var byline = Widgets.Text("", 9, UiTheme.Dim);
            byline.style.marginTop = 4;
            reply.Add(byline);
            reply.style.display = DisplayStyle.None;

            var buttons = new VisualElement();
            buttons.style.marginTop = 10;
            foreach (var (label, intent) in new[]
            {
                ("TAUNT THEM", HailIntent.Taunt), ("OFFER PEACE", HailIntent.Peace),
                ("TALK CLANS", HailIntent.Clan), ("PROPOSE TRADE", HailIntent.Trade),
            })
            {
                var i = intent;
                var b = Widgets.TextButton(label, () => Say(i), 11);
                b.style.marginBottom = 6;
                buttons.Add(b);
            }
            content.Add(buttons);
            content.Add(reply);

            void Say(HailIntent intent)
            {
                var state = ctx.State!;
                var galaxy = ctx.Bots!;
                var target = galaxy.Find(botId);
                if (target == null) return;
                int day = FlavorText.GazetteDay(state);
                int seed = unchecked(botId * 7919 + (int)intent * 131 + day);
                var facts = FlavorText.HailFacts(state, galaxy, target, intent);
                string fallback = FlavorText.HailFallback(facts, seed);
                reply.style.display = DisplayStyle.Flex;
                GameAudio.Play(Sfx.Open);
                bool waiting = AiWriter.Configured && !AiWriter.TryCached($"hail-{botId}-{intent}-{day}", out _);
                if (waiting)
                {
                    words.text = $"{name} is answering...";
                    byline.text = "";
                }
                AiWriter.Write(ctx, "hail", facts, seed, $"hail-{botId}-{intent}-{day}", fallback, (text, ai) =>
                {
                    words.text = AiWriter.Clean(text);
                    byline.text = ai ? "Voiced by the AI" : "";
                    byline.style.display = ai ? DisplayStyle.Flex : DisplayStyle.None;
                });
            }

            ui.OpenModal(blocker);
        }
    }
}
