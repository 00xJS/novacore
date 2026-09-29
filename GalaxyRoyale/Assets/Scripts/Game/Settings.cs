// Player settings (build-all plan, 2026-09-28): accessibility, music, and which
// notifications to send. Device-local (PlayerPrefs), like sound and haptics —
// they belong to this phone and this player, not to the galaxy in the save.
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public enum TextSize { Normal, Large, Larger }

    public static class Settings
    {
        const string Prefix = "galaxyroyale.";

        static int Get(string key, int fallback) => PlayerPrefs.GetInt(Prefix + key, fallback);

        static void Put(string key, int value)
        {
            PlayerPrefs.SetInt(Prefix + key, value);
            PlayerPrefs.Save();
        }

        // ---------- display & accessibility ----------

        static TextSize? s_textSize;

        /// <summary>Text size for every screen built from now on (the HUD after a restart).</summary>
        public static TextSize TextSize
        {
            get => s_textSize ??= (TextSize)Mathf.Clamp(Get("textSize", 0), 0, 2);
            set { s_textSize = value; Put("textSize", (int)value); }
        }

        public static float TextScale => TextSize switch
        {
            TextSize.Large => 1.15f,
            TextSize.Larger => 1.3f,
            _ => 1f,
        };

        static bool? s_colorBlind;

        /// <summary>Blue / orange instead of green / red for good and bad news.</summary>
        public static bool ColorBlind
        {
            get => s_colorBlind ??= Get("colorBlind", 0) == 1;
            set
            {
                s_colorBlind = value;
                Put("colorBlind", value ? 1 : 0);
                UI.UiTheme.ApplyPalette(value);
            }
        }

        static bool? s_reducedMotion;

        /// <summary>No pulsing or orbiting on the map, and battles open on their report
        /// instead of the round-by-round replay (it's one tap away).</summary>
        public static bool ReducedMotion
        {
            get => s_reducedMotion ??= Get("reducedMotion", 0) == 1;
            set { s_reducedMotion = value; Put("reducedMotion", value ? 1 : 0); }
        }

        // ---------- music ----------

        static bool? s_musicOn;

        public static bool MusicOn
        {
            get => s_musicOn ??= Get("music", 1) == 1;
            set { s_musicOn = value; Put("music", value ? 1 : 0); }
        }

        static float? s_musicVolume;

        public static float MusicVolume
        {
            get => s_musicVolume ??= Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "musicVolume", 0.6f));
            set
            {
                s_musicVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(Prefix + "musicVolume", s_musicVolume.Value);
                PlayerPrefs.Save();
            }
        }

        // ---------- notifications (sent while the app is closed) ----------

        public enum Notify { Timers, Raids, Galaxy, Clan }

        public static bool NotifyOn(Notify kind) => Get("notify." + kind, 1) == 1;

        public static void SetNotify(Notify kind, bool on) => Put("notify." + kind, on ? 1 : 0);

        public static string NotifyLabel(Notify kind) => kind switch
        {
            Notify.Timers => "Fleets, construction and research",
            Notify.Raids => "Radar warnings, your core and your shield",
            Notify.Galaxy => "Galaxy events, seasons and the dreadnought",
            _ => "Clan supply runs",
        };
    }
}
