// On-device persistence — the ONLY persistence in Galaxy Royale (the cloud
// layer retired with the single-player pivot). The v17 envelope (player state
// + the 99-bot simulated galaxy) lives as gzipped JSON under persistentDataPath
// (galaxy-royale-save.json.gz, 2026-09-29: ~60 KB instead of ~600 KB; builds
// before that wrote plain galaxy-royale-save.json, which still loads).
//
// Durability rules ("save locally and keep it backed up" — user spec):
//   - Writes are atomic: encode → write to a .tmp file → move over the live
//     file, so a crash mid-write can't torch the save.
//   - Before each overwrite, the previous good save rotates to a .bak file.
//   - Load falls back to the .bak automatically if the main file is missing
//     or corrupt — the empire survives anything short of losing both files.
//   - persistentDataPath is included in iOS device backups (iCloud/iTunes) by
//     default, so the OS-level backup story rides along for free — and every
//     few minutes the save is also mirrored to the app's own iCloud store
//     (CloudSave), which a reinstall or a new phone can restore from.
//   - The 30 s autosave snapshots the state on the main thread, then builds
//     the JSON text, gzips it and writes the file on a worker (the text is over
//     half the encode cost); pause/quit/new-game saves stay synchronous so
//     they're on disk before iOS suspends the app.
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
        static string PathMain => Path.Combine(Dir, "galaxy-royale-save.json.gz");
        static string PathBackup => Path.Combine(Dir, "galaxy-royale-save.bak.json.gz");
        static string PathTemp => Path.Combine(Dir, "galaxy-royale-save.tmp.json.gz");
        // The plain-JSON files of older builds: read when there's no .gz save yet,
        // deleted once the new pair (save + backup) is on disk.
        static string LegacyMain => Path.Combine(Dir, "galaxy-royale-save.json");
        static string LegacyBackup => Path.Combine(Dir, "galaxy-royale-save.bak.json");

        // Every snapshot takes the next number (main thread); the writer skips a
        // snapshot older than the newest one already on disk, so a slow worker
        // can never roll the save back.
        static readonly object s_writeLock = new();
        static long s_lastSeq;
        static long s_writtenSeq;

        /// <summary>Encode and write right now — for pause, quit and new game.
        /// <paramref name="cloud"/>: also stage it for the iCloud backup.</summary>
        public static void Save(GameState state, BotGalaxy? bots, long nowMs, bool cloud = false)
        {
            try
            {
                byte[] bytes = Pack(SaveCodec.Encode(SaveManager.Wrap(state, nowMs, bots)));
                if (WriteAtomic(bytes, ++s_lastSeq) && cloud)
                    CloudSave.Stage(bytes, CloudSave.HeaderFor(state, nowMs));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] LocalSave.Save failed: {e.Message}");
            }
        }

        /// <summary>Autosave: snapshot now (the sim mutates state on this thread),
        /// write on a worker — and pack the iCloud backup there too when
        /// <paramref name="cloud"/> asks for one.</summary>
        public static void SaveInBackground(GameState state, BotGalaxy? bots, long nowMs, bool cloud = false)
        {
            System.Collections.Generic.Dictionary<string, object?> tree;
            CloudSave.Header? header;
            try
            {
                _ = Dir; // resolve the path here, on the main thread
                tree = SaveCodec.EncodeTree(SaveManager.Wrap(state, nowMs, bots));
                header = cloud ? CloudSave.HeaderFor(state, nowMs) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] LocalSave snapshot failed: {e.Message}");
                return;
            }
            long seq = ++s_lastSeq;
            Task.Run(() =>
            {
                try
                {
                    byte[] bytes = Pack(Json.Write(tree));
                    if (WriteAtomic(bytes, seq) && header != null) CloudSave.Stage(bytes, header);
                }
                catch (Exception e) { Debug.LogWarning($"[Save] Background save failed: {e.Message}"); }
            });
        }

        /// <summary>The file's bytes: gzipped, or the plain text if gzip fails.</summary>
        static byte[] Pack(string json)
        {
            try { return SaveCompression.Pack(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] gzip unavailable ({e.Message}) — writing the save uncompressed");
                return System.Text.Encoding.UTF8.GetBytes(json);
            }
        }

        /// <summary>False when a newer snapshot already landed (nothing written).</summary>
        static bool WriteAtomic(byte[] bytes, long seq)
        {
            lock (s_writeLock)
            {
                if (seq < s_writtenSeq) return false; // a newer snapshot already landed
                File.WriteAllBytes(PathTemp, bytes);
                // Rotate: current good save becomes the backup, temp becomes current.
                if (File.Exists(PathMain))
                {
                    if (File.Exists(PathBackup)) File.Delete(PathBackup);
                    File.Move(PathMain, PathBackup);
                }
                File.Move(PathTemp, PathMain);
                s_writtenSeq = seq;
                // Both .gz files stand now, so the old plain-JSON pair has nothing left to cover.
                if (File.Exists(PathBackup))
                    foreach (var legacy in new[] { LegacyMain, LegacyBackup })
                        if (File.Exists(legacy)) File.Delete(legacy);
                return true;
            }
        }

        public static (GameState state, long savedAtMs, BotGalaxy? bots)? Load()
        {
            bool packed = File.Exists(PathMain) || File.Exists(PathBackup);
            string main = packed ? PathMain : LegacyMain, backup = packed ? PathBackup : LegacyBackup;
            var loaded = TryLoad(main);
            if (loaded != null) return loaded;
            loaded = TryLoad(backup);
            if (loaded != null)
                Debug.LogWarning("[Save] Main save unreadable — restored from backup.");
            return loaded;
        }

        static (GameState state, long savedAtMs, BotGalaxy? bots)? TryLoad(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var file = SaveCodec.Decode(SaveCompression.Unpack(File.ReadAllBytes(path)));
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
                foreach (var p in new[] { PathMain, PathBackup, LegacyMain, LegacyBackup })
                {
                    try
                    {
                        if (!File.Exists(p)) continue;
                        if (TryLoad(p) != null) { File.Delete(p); continue; }
                        string kept = Path.Combine(Dir, $"{Path.GetFileName(p)}.unreadable-{stamp}");
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
