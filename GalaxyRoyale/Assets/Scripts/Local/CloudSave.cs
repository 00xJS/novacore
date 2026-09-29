// iCloud save backup (user request 2026-09-28). After a local save — at most
// every few minutes, and always when the app goes to the background — the save
// is gzipped and mirrored into the app's iCloud key-value store (native side:
// Plugins/iOS/GRCloudSave.mm). A small header rides beside it, so boot can show
// "Commander X · might N · backed up 3h ago" without unpacking the galaxy.
// The store follows the player's Apple ID: a reinstall or a new phone offers
// to restore from it. In the Editor and on other platforms the backup is
// simply unsupported and local saves carry on alone.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GalaxyRoyale.Sim;
using GalaxyRoyale.Sim.Bots;
using GalaxyRoyale.Sim.Save;
using GalaxyRoyale.Sim.Systems;
using UnityEngine;

namespace GalaxyRoyale.Local
{
    public static class CloudSave
    {
        const string HeaderKey = "galaxyroyale.save.header";
        const string DataKey = "galaxyroyale.save.data";
        const string Magic = "GR1";
        const string LastBackupPref = "galaxyroyale.cloud.last_backup_ms";
        /// <summary>Minimum spacing between routine backups (going to the background always backs up).</summary>
        public const int IntervalSec = 300;

        /// <summary>What a backup holds, readable without unpacking it.</summary>
        public sealed class Header
        {
            public long SavedAtMs;
            public int Tick;
            public long Might;
            public string Name = "";
            /// <summary>Length of the packed data it was written with (a half-synced
            /// header/data pair fails this check instead of loading garbage).</summary>
            public int DataLength;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int _GRCloudSignedIn();
        [DllImport("__Internal")] static extern void _GRCloudSync();
        [DllImport("__Internal")] static extern int _GRCloudPut(string headerKey, string header, string dataKey, string data);
        [DllImport("__Internal")] static extern string _GRCloudGet(string key);
        public static bool Supported => true;
#else
        static int _GRCloudSignedIn() => 0;
        static void _GRCloudSync() { }
        static int _GRCloudPut(string headerKey, string header, string dataKey, string data) => 0;
        static string? _GRCloudGet(string key) => null;
        public static bool Supported => false;
#endif

        static readonly object s_lock = new();
        static (string header, string data, long savedAtMs)? s_staged;
        static long s_lastBackupMs = -1;

        public static bool SignedIn
        {
            get { try { return Supported && _GRCloudSignedIn() != 0; } catch (Exception) { return false; } }
        }

        /// <summary>When this device last pushed a backup (0 = never).</summary>
        public static long LastBackupMs
        {
            get
            {
                if (s_lastBackupMs < 0)
                    s_lastBackupMs = long.TryParse(PlayerPrefs.GetString(LastBackupPref, "0"), out var v) ? v : 0;
                return s_lastBackupMs;
            }
            private set
            {
                s_lastBackupMs = value;
                PlayerPrefs.SetString(LastBackupPref, value.ToString());
            }
        }

        /// <summary>Launch: ask the store for the latest values (they land asynchronously).</summary>
        public static void Sync()
        {
            if (!Supported) return;
            try { _GRCloudSync(); } catch (Exception e) { Debug.LogWarning($"[Cloud] sync failed: {e.Message}"); }
        }

        /// <summary>Main thread, before a save: should this one go to iCloud too?</summary>
        public static bool Due(long nowMs, bool force) =>
            Supported && (force || nowMs - LastBackupMs >= IntervalSec * 1000L);

        /// <summary>Main thread: the header for a save of <paramref name="state"/>.</summary>
        public static Header HeaderFor(GameState state, long nowMs) => new()
        {
            SavedAtMs = nowMs,
            Tick = state.Tick,
            Might = PowerSystem.ComputePower(state),
            Name = state.Profile.Name,
        };

        /// <summary>Any thread, once the save is on disk: stage the file's bytes (gzipped
        /// already, or plain JSON if gzip failed) for the upload.</summary>
        public static void Stage(byte[] file, Header header)
        {
            try
            {
                string data = (SaveCompression.IsPacked(file) ? "z" : "r") + Convert.ToBase64String(file);
                header.DataLength = data.Length;
                lock (s_lock) s_staged = (WriteHeader(header), data, header.SavedAtMs);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cloud] packing the backup failed: {e.Message}");
            }
        }

        /// <summary>Main thread (every frame, and right after a background save):
        /// push a staged backup to the store.</summary>
        public static void Flush()
        {
            (string header, string data, long savedAtMs) staged;
            lock (s_lock)
            {
                if (s_staged is not { } s) return;
                staged = s;
                s_staged = null;
            }
            try
            {
                if (_GRCloudPut(HeaderKey, staged.header, DataKey, staged.data) != 0)
                    LastBackupMs = staged.savedAtMs;
                else
                    Debug.LogWarning("[Cloud] the iCloud store didn't take the backup (it retries on the next save)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cloud] backup failed: {e.Message}");
            }
        }

        /// <summary>The backup's header, or null when there's none (or no iCloud).</summary>
        public static Header? Peek()
        {
            if (!Supported) return null;
            try { return ReadHeader(_GRCloudGet(HeaderKey)); }
            catch (Exception) { return null; }
        }

        /// <summary>Unpack and decode the backup (null when missing or damaged).</summary>
        public static (GameState state, long savedAtMs, BotGalaxy? bots)? FetchSave()
        {
            if (!Supported) return null;
            try
            {
                var header = ReadHeader(_GRCloudGet(HeaderKey));
                string? data = _GRCloudGet(DataKey);
                if (header == null || data == null || data.Length != header.DataLength) return null;
                return SaveManager.Unwrap(SaveCodec.Decode(Unpack(data)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cloud] the iCloud backup couldn't be read: {e.Message}");
                return null;
            }
        }

        // ---------- packing: gzip → base64 (a raw fallback if gzip is unavailable) ----------

        public static string Pack(string json)
        {
            try { return "z" + Convert.ToBase64String(SaveCompression.Pack(json)); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Cloud] gzip unavailable ({e.Message}) — storing the backup uncompressed");
                return "r" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            }
        }

        public static string Unpack(string data)
        {
            if (data.Length == 0) throw new InvalidDataException("empty backup");
            byte[] bytes = Convert.FromBase64String(data.Substring(1));
            if (data[0] == 'r') return Encoding.UTF8.GetString(bytes);
            if (data[0] != 'z') throw new InvalidDataException("unknown backup format");
            return SaveCompression.Unpack(bytes);
        }

        // "GR1|savedAtMs|tick|might|base64(name)|dataLength" — base64 keeps a "|"
        // in a commander's name from splitting the fields.
        static string WriteHeader(Header h) =>
            $"{Magic}|{h.SavedAtMs}|{h.Tick}|{h.Might}|{Convert.ToBase64String(Encoding.UTF8.GetBytes(h.Name))}|{h.DataLength}";

        public static Header? ReadHeader(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var parts = text!.Split('|');
            if (parts.Length != 6 || parts[0] != Magic) return null;
            if (!long.TryParse(parts[1], out var savedAt) || !int.TryParse(parts[2], out var tick)
                || !long.TryParse(parts[3], out var might) || !int.TryParse(parts[5], out var length))
                return null;
            string name;
            try { name = Encoding.UTF8.GetString(Convert.FromBase64String(parts[4])); }
            catch (FormatException) { return null; }
            return new Header { SavedAtMs = savedAt, Tick = tick, Might = might, Name = name, DataLength = length };
        }

        /// <summary>Test hook: the header text a backup would carry.</summary>
        public static string HeaderText(Header h) => WriteHeader(h);
    }
}
