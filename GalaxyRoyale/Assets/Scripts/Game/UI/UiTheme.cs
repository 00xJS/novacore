// v1's palette + layout constants + display formatters, ported from
// `src/ui/theme.ts` and `src/ui/format.ts`. All UI code colors from here.
using UnityEngine;

namespace GalaxyRoyale.Game.UI
{
    public static class UiTheme
    {
        // Portrait design canvas (v1's 390×844; PanelSettings match-width scales it).
        public const int W = 390;
        public const int H = 844;
        public const int NavH = 72; // was 64 — user wants a slightly taller nav

        // Neon Hologram with an orange accent — the player's pick (2026-09-28).
        // Dark violet glass, orange strokes and titles, magenta as the second colour.
        public static readonly Color Bg          = Rgb(0x05030e);
        public static readonly Color Panel       = Rgba(0x0b0621, 0.985f); // panels overlap the header: keep it from ghosting through
        public static readonly Color PanelLight  = Rgba(0x1e1046, 0.62f);
        public static readonly Color Stroke      = Rgba(0xff9a3d, 0.35f);
        public static readonly Color Btn         = Rgba(0xff9a3d, 0.09f);
        public static readonly Color BtnActive   = Rgba(0xff9a3d, 0.26f);
        public static readonly Color BtnDisabled = Rgba(0x9fb3d9, 0.05f);

        public static readonly Color Text   = Rgb(0xe8f7ff);
        public static readonly Color Dim    = Rgb(0x9fb3d9);
        public static readonly Color Accent = Rgb(0xff9a3d);
        /// <summary>The style's second colour: quests, badges, unread dots, close buttons.</summary>
        public static readonly Color Magenta = Rgb(0xff3dd8);
        /// <summary>Dark text on accent-filled buttons and badges.</summary>
        public static readonly Color Ink = Rgb(0x05030e);
        // Good / bad news: green / red, or blue / orange with colour-blind colours on
        // (Settings.ColorBlind — screens pick it up as they're built).
        public static Color Good = Rgb(0x3dffa0);
        public static Color Bad  = Rgb(0xff4d6d);

        static UiTheme()
        {
            if (Settings.ColorBlind) ApplyPalette(true);
        }

        public static void ApplyPalette(bool colorBlind)
        {
            Good = colorBlind ? Rgb(0x5eb8ff) : Rgb(0x3dffa0);
            Bad = colorBlind ? Rgb(0xffa347) : Rgb(0xff4d6d);
        }

        /// <summary>A dark wash of a colour for a card's background (won / lost rows).</summary>
        public static Color Wash(Color c, float alpha) => new(c.r * 0.17f, c.g * 0.17f, c.b * 0.17f, alpha);

        public static readonly Color Gold   = Rgb(0xd6e0ec);
        public static readonly Color Quartz = Rgb(0x3df5ff);
        public static readonly Color Helium     = Rgb(0x3dffa0);
        public static readonly Color Energy  = Rgb(0xffe14d);
        public static readonly Color DarkMatter = Rgb(0xd58cff);

        /// <summary>A colour at a new opacity.</summary>
        public static Color A(Color c, float alpha) => new(c.r, c.g, c.b, alpha);

        /// <summary>Avatar/faction palette, indexed by profile.AvatarSeed.</summary>
        /// <summary>Premade commander portraits in Resources/Avatars (avatar-0..9):
        /// 5 women then 5 men. avatarSeed indexes into them everywhere.</summary>
        public const int AvatarCount = 10;

        public static readonly Color[] AvatarColors =
        {
            Rgb(0x7fd4ff), Rgb(0x7fe08a), Rgb(0xffd166), Rgb(0xff9f7a),
            Rgb(0xc9a1e8), Rgb(0x6fd3e8), Rgb(0xf07a9a), Rgb(0x9ab0ff),
        };

        public static Color ResourceColor(GalaxyRoyale.Data.ResourceId id) => id switch
        {
            GalaxyRoyale.Data.ResourceId.Gold   => Gold,
            GalaxyRoyale.Data.ResourceId.Quartz => Quartz,
            GalaxyRoyale.Data.ResourceId.Helium     => Helium,
            _ => Text,
        };

        static Color Rgb(int hex) => new(
            ((hex >> 16) & 0xff) / 255f,
            ((hex >> 8) & 0xff) / 255f,
            (hex & 0xff) / 255f);

        static Color Rgba(int hex, float alpha) => A(Rgb(hex), alpha);

        // ---------- format.ts ----------

        /// <summary>Milli-units in, human string out (whole-unit based): K / M / B / T.</summary>
        public static string FmtAmount(long milli)
        {
            long whole = milli / 1000;
            if (whole >= 1_000_000_000_000) return $"{whole / 1_000_000_000_000.0:0.0}T";
            if (whole >= 1_000_000_000) return $"{whole / 1_000_000_000.0:0.0}B";
            if (whole >= 1_000_000) return $"{whole / 1_000_000.0:0.0}M";
            if (whole >= 10_000) return $"{whole / 1000.0:0.0}K";
            return whole.ToString("N0");
        }

        public static string FmtRatePerHour(long milliPerHour) => $"{milliPerHour / 1000}/h";

        /// <summary>Whole-number formatting: 10000→10k, 1200000→1.2M.</summary>
        public static string FmtCount(long n)
        {
            if (n >= 1_000_000) return n % 1_000_000 != 0 ? $"{n / 1_000_000.0:0.0}M" : $"{n / 1_000_000}M";
            if (n >= 1000) return n % 1000 != 0 ? $"{n / 1000.0:0.0}k" : $"{n / 1000}k";
            return n.ToString();
        }

        public static string FmtDuration(double seconds)
        {
            long s = (long)System.Math.Max(0, System.Math.Ceiling(seconds));
            long h = s / 3600;
            long m = (s % 3600) / 60;
            long sec = s % 60;
            if (h > 0) return $"{h}h {m}m";
            if (m > 0) return $"{m}m {sec}s";
            return $"{sec}s";
        }

        /// <summary>Like FmtDuration, but counts days for long spans ("9d 4h").</summary>
        public static string FmtLong(double seconds)
        {
            long s = (long)System.Math.Max(0, System.Math.Ceiling(seconds));
            if (s < 86400) return FmtDuration(s);
            return $"{s / 86400}d {s % 86400 / 3600}h";
        }
    }
}
