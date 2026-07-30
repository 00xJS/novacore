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

        public static readonly Color Bg          = Rgb(0x070b14);
        public static readonly Color Panel       = Rgb(0x111a2c);
        public static readonly Color PanelLight  = Rgb(0x1a2740);
        public static readonly Color Stroke      = Rgb(0x2a3b5c);
        public static readonly Color Btn         = Rgb(0x1f3a5c);
        public static readonly Color BtnActive   = Rgb(0x2a4a72);
        public static readonly Color BtnDisabled = Rgb(0x141d2c);

        public static readonly Color Text   = Rgb(0xdbe4f0);
        public static readonly Color Dim    = Rgb(0x8892a6);
        public static readonly Color Accent = Rgb(0x7fd4ff);
        public static readonly Color Good   = Rgb(0x7fe08a);
        public static readonly Color Bad    = Rgb(0xff7a7a);

        public static readonly Color Gold   = Rgb(0xaab4c0);
        public static readonly Color Quartz = Rgb(0x6fd3e8);
        public static readonly Color Helium     = Rgb(0x86e08a);
        public static readonly Color Energy  = Rgb(0xffd166);
        public static readonly Color DarkMatter = Rgb(0xc9a1e8);

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
    }
}
