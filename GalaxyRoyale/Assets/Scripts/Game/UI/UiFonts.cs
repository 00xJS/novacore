// The Neon Hologram type pair (restyle, 2026-09-28): Orbitron for display text
// (titles, buttons, captions, the HUD's labels) and Exo 2 for everything else.
// Both are SIL Open Font License fonts from github.com/google/fonts; their
// licences sit beside them in Resources/Fonts. They're variable fonts, which
// render at their default (Regular) weight; bold is synthesized.
//
// Orbitron has no "·" or "›" and neither font has "→", so each falls back:
// Orbitron → Exo 2 → Unity's built-in runtime font. Without that, those
// characters would draw as empty boxes on device.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public static class UiFonts
    {
        const string Common = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·—–…×•→−";

        static FontDefinition? s_display;
        static FontDefinition? s_body;
        static bool s_loaded;

        static void Load()
        {
            if (s_loaded) return;
            s_loaded = true;
            var display = Resources.Load<Font>("Fonts/Orbitron");
            var body = Resources.Load<Font>("Fonts/Exo2");
            if (display == null || body == null)
            {
                Debug.LogWarning("[UiFonts] Resources/Fonts is missing Orbitron or Exo2 — using the default font");
                return;
            }
            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var system = builtin != null ? FontAsset.CreateFontAsset(builtin) : null;
            var bodyAsset = FontAsset.CreateFontAsset(body);
            var displayAsset = FontAsset.CreateFontAsset(display);
            if (bodyAsset == null || displayAsset == null)
            {
                s_body = FontDefinition.FromFont(body);
                s_display = FontDefinition.FromFont(display);
                return;
            }
            // Load the common glyphs up front: text measured before a glyph is in the
            // atlas came out narrower than it drew, so labels ellipsized early.
            bodyAsset.TryAddCharacters(Common);
            displayAsset.TryAddCharacters(Common);
            bodyAsset.fallbackFontAssetTable = system != null ? new List<FontAsset> { system } : new List<FontAsset>();
            displayAsset.fallbackFontAssetTable = new List<FontAsset> { bodyAsset };
            if (system != null) displayAsset.fallbackFontAssetTable.Add(system);
            s_body = FontDefinition.FromSDFFont(bodyAsset);
            s_display = FontDefinition.FromSDFFont(displayAsset);
        }

        /// <summary>Exo 2 for a whole tree: the font is inherited, so setting it on
        /// the root styles every label under it.</summary>
        public static void ApplyBody(VisualElement root)
        {
            Load();
            if (s_body is { } body) root.style.unityFontDefinition = body;
        }

        /// <summary>Orbitron with a little tracking, for display text.</summary>
        public static T Display<T>(T element, float tracking = 1f) where T : VisualElement
        {
            Load();
            if (s_display is { } display) element.style.unityFontDefinition = display;
            element.style.letterSpacing = tracking;
            return element;
        }
    }
}
