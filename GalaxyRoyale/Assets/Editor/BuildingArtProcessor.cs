// One-shot art pipeline for the AI-generated building illustrations in
// Assets/Textures/Buidlings/: they are JPGs with the transparency checkerboard
// BAKED IN. This tool keys the checkerboard (and drop shadows painted over it)
// out to real alpha, crops, downscales, and writes PNGs to
// Assets/Resources/Buildings/<BuildingId>.png where BuildingMarkers loads them.
//
// Keying: flood-fill from the image border through "removable" pixels —
// near-neutral (low saturation) AND reasonably bright. The art style outlines
// every building in near-black, so the fill stops at the silhouette; interior
// grays are safe because they're not connected to the border. Shadows painted
// over the checker are neutral+bright enough to be eaten too.
//
// Run from the menu (GalaxyRoyale → Process Building Art) or batch:
//   Unity -batchmode -executeMethod BuildingArtProcessor.BatchRun
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildingArtProcessor
{
    const string SrcDir = "Assets/Textures/Buidlings"; // (sic — folder arrived with the typo)
    const string DstDir = "Assets/Resources/Buildings";
    const int MaxSize = 512;
    const float SaturationTolerance = 0.12f; // max (max-min)/255 to count as neutral

    // Per-image background-removal threshold. The original 8 have a baked
    // transparency CHECKERBOARD (grey squares ~0.5 bright) → a LOW threshold
    // (0.30) floods through the whole checker. The content-expansion batch has a
    // plain WHITE background instead; a low threshold flood-fills straight through
    // light-grey ART (the radar DISH got eaten), so those use a TIGHT near-white
    // threshold (0.90) that removes only the background and preserves the art.
    const float CheckerBright = 0.30f;
    const float WhiteBgBright = 0.90f;

    static readonly (string src, string dst, float minBright)[] Map =
    {
        ("commandcenter", "CommandCenter",     CheckerBright),
        ("refinery",      "GoldMine",         CheckerBright), // drill rig + ore + ingots
        ("quartz",       "QuartzExtractor",  CheckerBright),
        ("heliummine",       "HeliumRefinery",       CheckerBright), // tanks + green flare stack
        ("solarplant",    "PowerPlant",        CheckerBright),
        ("shipyard",      "Shipyard",          CheckerBright),
        ("storage",       "Warehouse",         CheckerBright),
        ("researchlab",   "ResearchLab",       CheckerBright),
        // Content-expansion buildings (2026-07-05) — white background, tight key.
        ("RadarStation",     "RadarStation",     WhiteBgBright),
        ("ExchangeTerminal", "ExchangeTerminal", WhiteBgBright),
        ("SalvageYard",      "SalvageYard",      WhiteBgBright),
        ("CommandBastion",   "CommandBastion",   WhiteBgBright),
        ("DroneFactory",     "DroneFactory",     WhiteBgBright),
    };

    [MenuItem("GalaxyRoyale/Process Building Art")]
    public static void Run()
    {
        Directory.CreateDirectory(DstDir);
        int ok = 0;
        foreach (var (src, dst, minBright) in Map)
        {
            string srcPath = Path.Combine(SrcDir, src + ".jpg");
            if (!File.Exists(srcPath))
            {
                Debug.LogWarning($"[BuildingArt] Missing {srcPath} — skipped.");
                continue;
            }
            if (Process(srcPath, Path.Combine(DstDir, dst + ".png"), minBright)) ok++;
        }
        AssetDatabase.Refresh();

        // Import settings: real transparency, clamped, mipmapped for the 3D view.
        foreach (var (_, dst, _) in Map)
        {
            string assetPath = $"{DstDir}/{dst}.png";
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter imp)
            {
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
        }
        Debug.Log($"[BuildingArt] Processed {ok}/{Map.Length} building illustrations → {DstDir}");
    }

    public static void BatchRun()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
    }

    static bool Process(string srcPath, string dstPath, float minBright)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(File.ReadAllBytes(srcPath)))
        {
            Debug.LogError($"[BuildingArt] Could not decode {srcPath}");
            return false;
        }
        int w = tex.width, h = tex.height;
        var px = tex.GetPixels32();

        bool Removable(Color32 c)
        {
            int max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            int min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return (max - min) / 255f < SaturationTolerance && max / 255f > minBright;
        }

        // Flood fill from every border pixel through removable pixels.
        var remove = new bool[w * h];
        var queue = new Queue<int>();
        void Seed(int x, int y)
        {
            int i = y * w + x;
            if (!remove[i] && Removable(px[i])) { remove[i] = true; queue.Enqueue(i); }
        }
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Seed(x - 1, y);
            if (x < w - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < h - 1) Seed(x, y + 1);
        }

        // Apply alpha; feather one ring of kept pixels bordering removals (JPG halo).
        for (int i = 0; i < px.Length; i++) if (remove[i]) px[i] = new Color32(0, 0, 0, 0);
        var feathered = new List<int>();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (remove[i]) continue;
                bool edge = (x > 0 && remove[i - 1]) || (x < w - 1 && remove[i + 1])
                         || (y > 0 && remove[i - w]) || (y < h - 1 && remove[i + w]);
                if (edge) feathered.Add(i);
            }
        }
        foreach (int i in feathered) px[i].a = 150;

        // Content bounds + padding.
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 0)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) { Debug.LogError($"[BuildingArt] {srcPath}: everything keyed out?"); return false; }
        const int pad = 6;
        minX = Mathf.Max(0, minX - pad); minY = Mathf.Max(0, minY - pad);
        maxX = Mathf.Min(w - 1, maxX + pad); maxY = Mathf.Min(h - 1, maxY + pad);
        int cw = maxX - minX + 1, ch = maxY - minY + 1;

        // Downscale to MaxSize (longest edge) with box sampling.
        float scale = Mathf.Min(1f, MaxSize / (float)Mathf.Max(cw, ch));
        int ow = Mathf.Max(1, Mathf.RoundToInt(cw * scale));
        int oh = Mathf.Max(1, Mathf.RoundToInt(ch * scale));
        var outPx = new Color32[ow * oh];
        float step = 1f / scale;
        for (int oy = 0; oy < oh; oy++)
        {
            for (int ox = 0; ox < ow; ox++)
            {
                int sx0 = minX + Mathf.FloorToInt(ox * step);
                int sy0 = minY + Mathf.FloorToInt(oy * step);
                int sx1 = Mathf.Min(maxX, minX + Mathf.CeilToInt((ox + 1) * step) - 1);
                int sy1 = Mathf.Min(maxY, minY + Mathf.CeilToInt((oy + 1) * step) - 1);
                float r = 0, g = 0, b = 0, a = 0; int n = 0;
                for (int sy = sy0; sy <= sy1; sy++)
                {
                    for (int sx = sx0; sx <= sx1; sx++)
                    {
                        var c = px[sy * w + sx];
                        // Premultiply so transparent pixels don't bleed dark fringes.
                        float af = c.a / 255f;
                        r += c.r * af; g += c.g * af; b += c.b * af; a += c.a;
                        n++;
                    }
                }
                if (n == 0) continue;
                float am = a / n;
                float unpremul = am > 1f ? 255f / am / n : 0f;
                outPx[oy * ow + ox] = new Color32(
                    (byte)Mathf.Clamp(r * unpremul, 0, 255),
                    (byte)Mathf.Clamp(g * unpremul, 0, 255),
                    (byte)Mathf.Clamp(b * unpremul, 0, 255),
                    (byte)Mathf.Clamp(am, 0, 255));
            }
        }

        var outTex = new Texture2D(ow, oh, TextureFormat.RGBA32, false);
        outTex.SetPixels32(outPx);
        outTex.Apply();
        File.WriteAllBytes(dstPath, outTex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(outTex);
        Debug.Log($"[BuildingArt] {Path.GetFileName(srcPath)} → {dstPath} ({ow}×{oh})");
        return true;
    }
}
