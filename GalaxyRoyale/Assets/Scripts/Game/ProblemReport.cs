// Report a problem (Settings): a plain-text report — version, device, the
// state of the galaxy and the most recent errors from ErrorLog — handed to the
// iOS share sheet (Plugins/iOS/GRShare.mm), so the player reads it first and
// chooses where it goes (Mail, Messages, Notes…). Nothing is sent by the game.
// No names or identifiers: the commander's name and the save stay out of it.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using GalaxyRoyale.Data;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Systems;

namespace GalaxyRoyale.Game
{
    public static class ProblemReport
    {
        const int ErrorLines = 80;

        public static string Compose(GameState? state)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Galaxy Royale — problem report");
            sb.AppendLine($"Version {Application.version} · {SystemInfo.operatingSystem} · {SystemInfo.deviceModel}");
            if (state != null)
            {
                sb.AppendLine($"Galaxy tick {state.Tick:N0} · commander level {state.Commander.Level} · " +
                    $"might {PowerSystem.ComputePower(state):N0} · {Difficulties.Name(state.Difficulty)} · " +
                    $"{(state.TestMode ? "testing" : "standard")} mode · {(state.ClanId != 0 ? "in a clan" : "no clan")}");
            }
            sb.AppendLine();
            sb.AppendLine("What happened (and what you expected):");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("---- recent errors ----");
            sb.AppendLine(RecentErrors());
            return sb.ToString();
        }

        static string RecentErrors()
        {
            try
            {
                var path = ErrorLog.PathOrNull;
                if (path == null || !File.Exists(path)) return "(none logged)";
                var lines = File.ReadAllLines(path);
                int from = Math.Max(0, lines.Length - ErrorLines);
                return string.Join("\n", lines, from, lines.Length - from);
            }
            catch (Exception e)
            {
                return $"(couldn't read the error log: {e.Message})";
            }
        }

        /// <summary>Open the share sheet with the report (elsewhere: copy it to the clipboard).</summary>
        /// <returns>True when the share sheet opened; false when it went to the clipboard.</returns>
        public static bool Share(GameState? state)
        {
            string text = Compose(state);
#if UNITY_IOS && !UNITY_EDITOR
            _GRShareText(text);
            return true;
#else
            GUIUtility.systemCopyBuffer = text;
            return false;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void _GRShareText(string text);
#endif
    }
}
