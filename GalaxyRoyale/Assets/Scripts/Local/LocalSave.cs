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
//   - The 30 s autosave snapshots the state on the main thread, then builds
//     the JSON text and writes the file on a worker (the text is over half the
//     encode cost); pause/quit/new-game saves stay synchronous so they're on
//     disk before iOS suspends the app.
using System;
using System.IO;
using System.Threading.Tasks;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using UnityEngine;

namespace GalaxyRoyale.Local
{
    public static class LocalSave
    {
        // persistentDataPath is main-thread-only in Unity — cached for the writer.
        static string? s_dir;
        static string Dir => s_dir ??= Application.persistentDataPath;
        static string PathMain => Path.Combine(Dir, "galaxy-royale-save.json");
        static string PathBackup => Path.Combine(Dir, "galaxy-royale-save.bak.json");
        static string PathTemp => Path.Combine(Dir, "galaxy-royale-save.tmp.json");

        // Every snapshot takes the next number (main thread); the writer skips a
        // snapshot older than the newest one already on disk, so a slow worker
        // can never roll the save back.
        static readonly object s_writeLock = new();
        static long s_lastSeq;
        static long s_writtenSeq;

        /// <summary>Encode and write right now — for pause, quit and new game.</summary>
        public static void Save(GameState state, BotGalaxy? bots, long nowMs)
        {
            try
            {
                string json = SaveCodec.Encode(SaveManager.Wrap(state, nowMs, bots));
                WriteAtomic(json, ++s_lastSeq);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] LocalSave.Save failed: {e.Message}");
            }
        }

        /// <summary>Autosave: snapshot now (the sim mutates state on this thread),
        /// write on a worker.</summary>
        public static void SaveInBackground(GameState state, BotGalaxy? bots, long nowMs)
        {
            System.Collections.Generic.Dictionary<string, object?> tree;
            try
            {
                _ = Dir; // resolve the path here, on the main thread
                tree = SaveCodec.EncodeTree(SaveManager.Wrap(state, nowMs, bots));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] LocalSave snapshot failed: {e.Message}");
                return;
            }
            long seq = ++s_lastSeq;
            Task.Run(() =>
            {
                try { WriteAtomic(Json.Write(tree), seq); }
                catch (Exception e) { Debug.LogWarning($"[Save] Background save failed: {e.Message}"); }
            });
        }

        static void WriteAtomic(string json, long seq)
        {
            lock (s_writeLock)
            {
                if (seq < s_writtenSeq) return; // a newer snapshot already landed
                File.WriteAllText(PathTemp, json);
                // Rotate: current good save becomes the backup, temp becomes current.
                if (File.Exists(PathMain))
                {
                    if (File.Exists(PathBackup)) File.Delete(PathBackup);
                    File.Move(PathMain, PathBackup);
                }
                File.Move(PathTemp, PathMain);
                s_writtenSeq = seq;
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

        /// <summary>
        /// Wipe for RESET EMPIRE / NEW GAME. A save that still DECODES is deleted
        /// (the player double-confirmed). One that doesn't — corrupt, or written by
        /// a newer build before a downgrade — is renamed aside instead: the title
        /// screen can't see it, so NEW GAME used to erase it with no confirmation.
        /// </summary>
        public static void Delete()
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            // Holding the write lock with a fresh number waits out an in-flight
            // autosave and voids any older one, so the old galaxy can't land
            // back on disk after the wipe.
            lock (s_writeLock)
            {
                s_writtenSeq = ++s_lastSeq;
                foreach (var p in new[] { PathMain, PathBackup })
                {
                    try
                    {
                        if (!File.Exists(p)) continue;
                        if (TryLoad(p) != null) { File.Delete(p); continue; }
                        string kept = Path.Combine(Dir,
                            $"{Path.GetFileNameWithoutExtension(p)}.unreadable-{stamp}.json");
                        File.Move(p, kept);
                        Debug.LogWarning($"[Save] Unreadable save kept as {Path.GetFileName(kept)}");
                    }
                    catch (Exception) { /* nothing sensible to do */ }
                }
                try { if (File.Exists(PathTemp)) File.Delete(PathTemp); }
                catch (Exception) { }
            }
        }
    }
}
