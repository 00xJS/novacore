// Game Center (build-all plan, 2026-09-28) — optional: off until the player
// turns it on in Settings, which signs in to Game Center. Then the game posts
// its might to the leaderboard below and unlocks the matching Game Center
// achievement whenever an achievement unlocks in the game. The leaderboard and
// achievements have to exist in App Store Connect under exactly these ids
// (store/README.md); anything missing there is skipped quietly.
using System.Collections.Generic;
using UnityEngine;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game
{
    public static class GameCenter
    {
        public const string MightLeaderboard = "galaxyroyale.might";

        /// <summary>"galaxyroyale.core_breacher" for the in-game achievement "core-breacher"
        /// (Game Center ids allow letters, digits, periods and underscores).</summary>
        public static string AchievementId(string id) => "galaxyroyale." + id.Replace('-', '_');

        const string EnabledKey = "galaxyroyale.gameCenter";
        static bool s_signedIn, s_signingIn;
        static long s_lastMight = -1;
        static readonly HashSet<string> s_reported = new();

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 0) == 1;
            set { PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool SignedIn => s_signedIn;

        /// <summary>Sign in if the player opted in, then post everything (boot, the Settings switch).</summary>
        public static void Sync(GameState? state, System.Action<bool>? done = null)
        {
            if (!Enabled || Application.isEditor || state == null) { done?.Invoke(false); return; }
            if (s_signedIn) { PostAll(state); done?.Invoke(true); return; }
            if (s_signingIn) return;
            s_signingIn = true;
            Social.localUser.Authenticate(ok =>
            {
                s_signingIn = false;
                s_signedIn = ok;
                if (ok) PostAll(state);
                done?.Invoke(ok);
            });
        }

        /// <summary>Might to the leaderboard (when it changed) and every unlocked achievement not yet posted.</summary>
        public static void PostAll(GameState state)
        {
            if (!Enabled || !s_signedIn) return;
            long might = PowerSystem.ComputePower(state);
            if (might != s_lastMight)
            {
                s_lastMight = might;
                Social.ReportScore(might, MightLeaderboard, _ => { });
            }
            foreach (var id in state.Achievements) PostAchievement(id);
        }

        public static void PostAchievement(string id)
        {
            if (!Enabled || !s_signedIn || !s_reported.Add(id)) return;
            Social.ReportProgress(AchievementId(id), 100.0, _ => { });
        }
    }
}
