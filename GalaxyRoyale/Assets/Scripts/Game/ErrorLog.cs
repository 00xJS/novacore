// Keeps the last errors and exceptions in a small file beside the save
// (persistentDataPath/galaxy-royale-errors.log). On iOS, Unity's log only goes
// to stdout, which a phone or a TestFlight build can't show anyone, so
// without this a UI handler that throws just "does nothing".
using System;
using System.IO;
using UnityEngine;

namespace GalaxyRoyale.Game
{
    public static class ErrorLog
    {
        const long MaxBytes = 64 * 1024;
        static string? s_path;
        static readonly object s_lock = new();

        public static string? PathOrNull => s_path;

        /// <summary>Start capturing (idempotent; call from the main thread).</summary>
        public static void Install()
        {
            if (s_path != null) return;
            s_path = Path.Combine(Application.persistentDataPath, "galaxy-royale-errors.log");
            Application.logMessageReceivedThreaded += OnLog;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            var path = s_path;
            if (path == null) return;
            try
            {
                lock (s_lock)
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string old = path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path,
                        $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z {type}: {condition}\n{stackTrace}\n");
                }
            }
            catch (Exception)
            {
                // Logging must never take the game down.
            }
        }
    }
}
