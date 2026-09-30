// Credits and licences (App Store prep, 2026-09-30): the fonts' SIL Open Font
// Licence notices and the CC BY 4.0 attribution the Solar System Scope planet
// textures (Resources/PlanetRefs) require. Settings › About › CREDITS.
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public static class Links
    {
        public const string Home = "https://00xjs.github.io/novacore/";
        public const string Privacy = Home + "privacy.html";
        public const string Support = Home + "support.html";
    }

    public static class CreditsPanel
    {
        public static void Open(GameContext ctx)
        {
            var ui = UIController.Instance!;
            var (blocker, content) = Widgets.ModalPanel("CREDITS", () => SettingsPanel.Open(ctx), 80f);
            void Section(string title, params string[] lines)
            {
                var h = Widgets.Heading(title, 10, UiTheme.Accent, 1.4f);
                h.style.marginTop = 14;
                content.Add(h);
                foreach (var line in lines)
                {
                    var t = Widgets.Text(line, 12, UiTheme.Text);
                    t.style.whiteSpace = WhiteSpace.Normal;
                    t.style.marginTop = 4;
                    content.Add(t);
                }
            }
            Section("FONTS",
                "Orbitron — Copyright 2018 The Orbitron Project Authors (github.com/theleagueof/orbitron), " +
                "with Reserved Font Name \"Orbitron\".",
                "Exo 2 — Copyright 2013 The Exo 2 Project Authors (github.com/googlefonts/Exo-2.0).",
                "Both are licensed under the SIL Open Font License, Version 1.1 (openfontlicense.org).");
            Section("PLANET TEXTURES",
                "Earth, Mars, Mercury, Saturn and Ceres surface maps by Solar System Scope " +
                "(solarsystemscope.com/textures), licensed under Creative Commons Attribution 4.0 " +
                "(creativecommons.org/licenses/by/4.0). Blended and recoloured for the game.");
            Section("EVERYTHING ELSE",
                "The music and sound are synthesized in the game. Built with Unity.");
            ui.OpenModal(blocker);
        }
    }
}
