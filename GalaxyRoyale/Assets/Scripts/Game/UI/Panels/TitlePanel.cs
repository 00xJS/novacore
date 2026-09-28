// The boot screen — the old login page's slot, rebranded for single-player
// (user spec): CONTINUE GAME resumes the saved galaxy; NEW GAME founds a
// fresh one (STANDARD or TESTING, picked in NewGamePanel), double-confirmed
// when a save would be erased. Opaque full-bleed
// page — the placeholder sim idles hidden behind it until a choice is made.
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public static class TitlePanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var boot = LocalBootstrap.Instance;
            var (blocker, content) = Widgets.FullPage("GALAXY ROYALE", onBack: null);

            // Center the body in the page (user spec — buttons were hugging the top;
            // the header bar stays put). FullPage's content is a ScrollView whose
            // content container hugs its children, so stretch it to the viewport
            // first or the flex centering has nothing to center within.
            if (content is ScrollView scroll)
            {
                scroll.contentContainer.style.flexGrow = 1f;
                scroll.contentContainer.style.minHeight = Length.Percent(100f);
            }

            var body = new VisualElement();
            body.style.flexGrow = 1f;
            body.style.justifyContent = Justify.Center;
            body.style.paddingLeft = 28;
            body.style.paddingRight = 28;
            body.style.paddingBottom = 48; // optical center sits a touch above true center

            var tagline = Widgets.Text(
                $"One galaxy. {GalaxyRoyale.Sim.Bots.BotSystem.BotCount + 1} commanders. Only one throne.",
                13, UiTheme.Dim);
            tagline.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
            tagline.style.whiteSpace = WhiteSpace.Normal;
            tagline.style.marginBottom = 36;
            body.Add(tagline);

            bool hasSave = boot != null && boot.HasSave;
            if (hasSave)
            {
                var cont = Widgets.TextButton("CONTINUE GAME", () => boot!.ContinueGame(), 15);
                cont.style.height = 52;
                Widgets.SetBorder(cont, UiTheme.Accent, 2f);
                body.Add(cont);

                var summary = Widgets.Text(boot!.SaveSummary, 11, UiTheme.Dim);
                summary.style.unityTextAlign = UnityEngine.TextAnchor.MiddleCenter;
                summary.style.marginTop = 6;
                summary.style.marginBottom = 22;
                body.Add(summary);
            }

            var fresh = Widgets.TextButton("NEW GAME", () =>
            {
                // Mode picker (STANDARD / TESTING); backing out returns here and
                // nothing is erased until START is tapped.
                void ChooseMode() => NewGamePanel.Open(test => boot?.NewGame(test), () => Open(ctx));
                if (!hasSave)
                {
                    ChooseMode();
                    return;
                }
                // Secondary confirmation (user spec): starting over erases the
                // saved empire AND all its rivals — never one tap away.
                ConfirmPanel.Open(
                    "Start a NEW galaxy?\nYour current empire and all " +
                    $"{GalaxyRoyale.Sim.Bots.BotSystem.BotCount} rival commanders will be erased forever.",
                    "NEW GAME — ERASE SAVE",
                    ChooseMode,
                    () => Open(ctx)); // cancel → back to the title screen
            }, hasSave ? 12 : 15);
            fresh.style.height = hasSave ? 44 : 52;
            if (!hasSave) Widgets.SetBorder(fresh, UiTheme.Accent, 2f);
            body.Add(fresh);

            content.Add(body);
            ui.OpenModal(blocker);
        }
    }
}
