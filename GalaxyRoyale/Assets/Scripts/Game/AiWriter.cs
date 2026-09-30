// The AI writers (build-all plan, 2026-09-28): the Galactic Gazette, battle
// recaps and hail replies, written by Claude through the player's own proxy
// (server/ai-proxy — a Cloudflare Worker that holds the API key; the key never
// ships in the app). The player-facing setting was removed for the App Store
// release (2026-09-30): only the GR_AI developer launch hook sets the proxy,
// for this session, and an address saved by an older build is ignored. Without one
// — or offline, or when the proxy answers anything but 200 — every feature
// uses the game's own text (Sim/Text/FlavorText), so nothing depends on it.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using GalaxyRoyale.Sim.Save;

namespace GalaxyRoyale.Game
{
    public static class AiWriter
    {
        const string UrlKey = "galaxyroyale.aiProxy";
        const string GazetteKey = "galaxyroyale.gazette";
        const int TimeoutSec = 25;
        /// <summary>Texts written this session, so reopening a report doesn't pay twice.</summary>
        static readonly Dictionary<string, string> s_cache = new();
        /// <summary>Requests in flight, so a double tap doesn't send two.</summary>
        static readonly HashSet<string> s_pending = new();

        static string s_proxy = "";

        /// <summary>The proxy for this session (GR_AI only; never saved).</summary>
        public static string ProxyUrl
        {
            get => s_proxy;
            set
            {
                string url = (value ?? "").Trim();
                if (url == s_proxy) return;
                s_proxy = url;
                s_cache.Clear();
            }
        }

        /// <summary>Forget what an older build saved: its proxy address and its AI Gazette.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ForgetSaved()
        {
            if (!PlayerPrefs.HasKey(UrlKey) && !PlayerPrefs.HasKey(GazetteKey)) return;
            PlayerPrefs.DeleteKey(UrlKey);
            PlayerPrefs.DeleteKey(GazetteKey);
            PlayerPrefs.Save();
        }

        /// <summary>Today's AI Gazette, written in an earlier session on this device (null if none).</summary>
        public static string? SavedGazette(int day)
        {
            string saved = PlayerPrefs.GetString(GazetteKey, "");
            int bar = saved.IndexOf('|');
            return bar > 0 && saved.Substring(0, bar) == day.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ? saved.Substring(bar + 1) : null;
        }

        /// <summary>Keep today's AI Gazette, so it's written once a galaxy day.</summary>
        public static void SaveGazette(int day, string text)
        {
            PlayerPrefs.SetString(GazetteKey, $"{day.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{text}");
            PlayerPrefs.Save();
        }

        /// <summary>A usable proxy URL is set (http or https).</summary>
        public static bool Configured =>
            Uri.TryCreate(ProxyUrl, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

        /// <summary>Text for <paramref name="kind"/> from these facts: the proxy's when it answers,
        /// otherwise <paramref name="fallback"/>. <paramref name="done"/> gets the text and whether the
        /// AI wrote it; it runs at once for the fallback or a cached text, later for a fresh one.</summary>
        public static void Write(MonoBehaviour host, string kind, Dictionary<string, object?> facts, int seed,
            string cacheKey, string fallback, Action<string, bool> done)
        {
            if (s_cache.TryGetValue(cacheKey, out var cached)) { done(cached, true); return; }
            if (!Configured || !s_pending.Add(cacheKey)) { done(fallback, false); return; }
            host.StartCoroutine(Post(kind, facts, seed, text =>
            {
                s_pending.Remove(cacheKey);
                if (text == null) { done(fallback, false); return; }
                s_cache[cacheKey] = text;
                done(text, true);
            }));
        }

        /// <summary>Remember a text written in an earlier session (the Gazette keeps its edition).</summary>
        public static void Remember(string cacheKey, string text) => s_cache[cacheKey] = text;

        public static bool TryCached(string cacheKey, out string text) => s_cache.TryGetValue(cacheKey, out text!);

        static IEnumerator Post(string kind, Dictionary<string, object?> facts, int seed, Action<string?> done)
        {
            string body = Json.Write(new Dictionary<string, object?>
            {
                ["kind"] = kind,
                ["facts"] = facts,
                ["seed"] = (long)seed,
            });
            using var req = new UnityWebRequest(ProxyUrl.TrimEnd('/') + "/v1/generate", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSec,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            yield return req.SendWebRequest();
            string? text = null;
            if (req.result == UnityWebRequest.Result.Success && req.responseCode == 200)
            {
                try
                {
                    if (Json.Parse(req.downloadHandler.text) is Dictionary<string, object?> d
                        && d.TryGetValue("text", out var t) && t is string s && s.Trim().Length > 0)
                        text = Clean(s);
                }
                catch (Exception) { /* a garbled reply is just no reply */ }
            }
            else Debug.Log($"[AiWriter] {kind}: {req.responseCode} {req.error}"); // not an error: the game's own text stands in
            done(text);
        }

        /// <summary>GET /health — for Settings' TEST button.</summary>
        public static void Check(MonoBehaviour host, Action<bool, string> done)
        {
            if (!Configured) { done(false, "Enter the proxy's address first (https://…)"); return; }
            host.StartCoroutine(Health(done));
        }

        static IEnumerator Health(Action<bool, string> done)
        {
            using var req = UnityWebRequest.Get(ProxyUrl.TrimEnd('/') + "/health");
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success && req.responseCode == 200
                && req.downloadHandler.text.Contains("\"ok\":true"))
                done(true, "Connected — the AI writers are ready");
            else
                done(false, req.responseCode > 0 ? $"The proxy answered {req.responseCode}" : $"Couldn't reach the proxy ({req.error})");
        }

        /// <summary>Tidy a reply for the game's font: the Gazette's "•" bullets become "·"
        /// (drawn everywhere else in the game), curly quotes and dashes go plain.</summary>
        public static string Clean(string s)
        {
            var sb = new StringBuilder(s.Trim());
            sb.Replace("• ", "· ").Replace('•', '·')
              .Replace('‘', '\'').Replace('’', '\'').Replace('“', '"').Replace('”', '"')
              .Replace('–', '-').Replace("…", "...");
            return sb.ToString();
        }
    }
}
