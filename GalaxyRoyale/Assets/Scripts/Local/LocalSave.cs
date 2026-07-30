// On-device persistence — the ONLY persistence in Galaxy Royale (the cloud
// layer retired with the single-player pivot). The v17 envelope (player state
// + the 99-bot simulated galaxy) lives as a JSON file under persistentDataPath.
//
// Durability rules ("save locally and keep it backed up" — user spec):
//   - Writes are atomic: encode → write to a .tmp file → move over the live
//     file, so a crash mid-write can't torch the save.
//   - Before each overwrite, the previous good save rotates to a .bak file.
//   - Load falls back to the .bak automatically if the main file is missing
//     or corrupt — the empire survives anything short of losing both files.
//   - persistentDataPath is included in iOS device backups (iCloud/iTunes) by
//     default, so the OS-level backup story rides along for free.
using System;
using System.IO;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using UnityEngine;

namespace GalaxyRoyale.Local
{
    public static class LocalSave
    {
        static string Dir => Application.persistentDataPath;
        static string PathMain => Path.Combine(Dir, "galaxy-royale-save.json");
        static string PathBackup => Path.Combine(Dir, "galaxy-royale-save.bak.json");
        static string PathTemp => Path.Combine(Dir, "galaxy-royale-save.tmp.json");

        public static void Save(GameState state, BotGalaxy? bots, long nowMs)
        {
            try
            {
                string json = SaveCodec.Encode(SaveManager.Wrap(state, nowMs, bots));
                File.WriteAllText(PathTemp, json);
                // Rotate: current good save becomes the backup, temp becomes current.
                if (File.Exists(PathMain))
                {
                    if (File.Exists(PathBackup)) File.Delete(PathBackup);
                    File.Move(PathMain, PathBackup);
                }
                File.Move(PathTemp, PathMain);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] LocalSave.Save failed: {e.Message}");
            }
        }

        public static (GameState state, long savedAtMs, BotGalaxy? bots)? Load()
        {
            var main = TryLoad(PathMain);
            if (main != null) return main;
            var backup = TryLoad(PathBackup);
            if (backup != null)
                Debug.LogWarning("[Save] Main save unreadable — restored from backup.");
            return backup;
        }

        static (GameState state, long savedAtMs, BotGalaxy? bots)? TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var file = SaveCodec.Decode(File.ReadAllText(path));
                return SaveManager.Unwrap(file);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Corrupt save ignored at {Path.GetFileName(path)} ({e.Message})");
                return null;
            }
        }

        /// <summary>Full wipe — main, backup, and temp. Used by RESET EMPIRE only.</summary>
        public static void Delete()
        {
            foreach (var p in new[] { PathMain, PathBackup, PathTemp })
            {
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception) { /* nothing sensible to do */ }
            }
        }
    }
}
