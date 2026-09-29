// Saves are gzipped on disk (2026-09-29). The envelope's JSON is ~600 KB of
// very repetitive text (every bot's buildings, fleets and reports), and gzip
// packs it to about a tenth — a far smaller write every 30 s, and the same
// bytes feed the iCloud backup. Reading sniffs gzip's magic number, so a
// plain-JSON save (every save before this, or one a tool wrote) still loads.
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GalaxyRoyale.Sim.Save
{
    public static class SaveCompression
    {
        /// <summary>Gzip the save's JSON text (UTF-8).</summary>
        public static byte[] Pack(string json)
        {
            byte[] raw = Encoding.UTF8.GetBytes(json);
            using var buffer = new MemoryStream(raw.Length / 8 + 64);
            using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(raw, 0, raw.Length);
            return buffer.ToArray();
        }

        /// <summary>Starts with gzip's magic number (1F 8B)?</summary>
        public static bool IsPacked(byte[] bytes) => bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;

        /// <summary>The save's JSON text, from gzip or from plain UTF-8 JSON.</summary>
        public static string Unpack(byte[] bytes)
        {
            if (!IsPacked(bytes))
            {
                // Plain text; File.WriteAllText never wrote a BOM, but an editor might.
                int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                return Encoding.UTF8.GetString(bytes, skip, bytes.Length - skip);
            }
            using var input = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var output = new MemoryStream(bytes.Length * 10);
            input.CopyTo(output);
            return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
        }
    }
}
