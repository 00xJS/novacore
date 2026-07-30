// Commander portraits (extracted from the retired social layer's SocialGate —
// the avatar renderer outlived the chat panels it was born in). Used by the
// header pill, the profile panels, rankings rows, and rival-commander cards.
using UnityEngine;
using UnityEngine.UIElements;

namespace GalaxyRoyale.Game.UI
{
    public static class Portraits
    {
        // Premade portrait cache (Resources/Avatars/avatar-0..9 — 5 women, 5 men).
        static readonly Texture2D?[] _avatarTex = new Texture2D?[UiTheme.AvatarCount];
        static readonly bool[] _avatarTried = new bool[UiTheme.AvatarCount];

        public static Texture2D? AvatarTexture(int avatarSeed)
        {
            int i = Mathf.Abs(avatarSeed) % UiTheme.AvatarCount;
            if (!_avatarTried[i])
            {
                _avatarTried[i] = true;
                _avatarTex[i] = UnityEngine.Resources.Load<Texture2D>($"Avatars/avatar-{i}");
            }
            return _avatarTex[i];
        }

        /// <summary>Commander portrait for profiles / rankings / the header —
        /// premade image when available, initial-disc fallback otherwise.</summary>
        public static VisualElement Avatar(int avatarSeed, string name, int size)
        {
            var disc = new VisualElement();
            disc.style.width = size;
            disc.style.height = size;
            disc.style.borderTopLeftRadius = size / 2f;
            disc.style.borderTopRightRadius = size / 2f;
            disc.style.borderBottomLeftRadius = size / 2f;
            disc.style.borderBottomRightRadius = size / 2f;

            var tex = AvatarTexture(avatarSeed);
            if (tex != null)
            {
                disc.style.backgroundImage = new StyleBackground(tex);
                return disc;
            }

            var color = UiTheme.AvatarColors[Mathf.Abs(avatarSeed) % UiTheme.AvatarColors.Length];
            disc.style.backgroundColor = new Color(color.r, color.g, color.b, 0.35f);
            Widgets.SetBorder(disc, color, 1.5f);
            disc.style.justifyContent = Justify.Center;
            disc.style.alignItems = Align.Center;
            disc.Add(Widgets.Text(name.Length > 0 ? name.Substring(0, 1).ToUpper() : "?",
                Mathf.Max(9, size / 2), UiTheme.Text, bold: true));
            return disc;
        }
    }
}
