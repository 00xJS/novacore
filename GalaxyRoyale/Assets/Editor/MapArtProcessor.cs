// Keys the baked transparency CHECKERBOARD out of the map art in
// Assets/Resources/Map (fleet.png, probe.png — they arrived with the checker
// still baked in, which renders as a light square around the sprite on the dark
// map). Same border-flood-fill approach as BuildingArtProcessor: remove
// border-connected NEUTRAL + BRIGHT pixels (the checker), stopping at the art's
// darker/coloured outlines. Clean-alpha images are left untouched (their
// transparent border isn't "bright", so the fill never starts).
//
// Run: GalaxyRoyale → Process Map Art (or -executeMethod MapArtProcessor.BatchRun).
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MapArtProcessor
{
    const string Dir = "Assets/Resources/Map";
    const float SaturationTolerance = 0.14f; // ≤ this (max-min)/255 counts as neutral
    const float MinBrightness = 0.5f;        // checker squares (grey ~0.6 / white ~1.0)

    static readonly string[] Files = { "fleet", "probe" };

    [MenuItem("GalaxyRoyale/Process Map Art")]
    public static void Run()
    {
        int ok = 0;
        foreach (var name in Files)
        {
            string path = Path.Combine(Dir, name + ".png");
            if (!File.Exists(path)) { Debug.LogWarning($"[MapArt] missing {path}"); continue; }
            if (KeyChecker(path)) ok++;
        }
        AssetDatabase.Refresh();
        foreach (var name in Files)
        {
            if (AssetImporter.GetAtPath($"{Dir}/{name}.png") is TextureImporter imp)
            {
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
        }
        Debug.Log($"[MapArt] keyed {ok}/{Files.Length} map sprites");
    }

    public static void BatchRun()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (System.Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
    }

    static bool KeyChecker(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(File.ReadAllBytes(path))) { Debug.LogError($"[MapArt] decode failed {path}"); return false; }
        int w = tex.width, h = tex.height;
        var px = tex.GetPixels32();

        bool Removable(Color32 c)
        {
            if (c.a < 8) return true; // already transparent
            int max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            int min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return (max - min) / 255f < SaturationTolerance && max / 255f > MinBrightness;
        }

        var remove = new bool[w * h];
        var q = new Queue<int>();
        void Seed(int x, int y)
        {
            int i = y * w + x;
            if (!remove[i] && Removable(px[i])) { remove[i] = true; q.Enqueue(i); }
        }
        for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
        for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Seed(x - 1, y);
            if (x < w - 1) Seed(x + 1, y);
            if (y > 0) Seed(x, y - 1);
            if (y < h - 1) Seed(x, y + 1);
        }

        int cut = 0;
        for (int i = 0; i < px.Length; i++) if (remove[i]) { px[i] = new Color32(0, 0, 0, 0); cut++; }
        // Feather one kept ring bordering removals (JPG/edge halo).
        var feather = new List<int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (remove[i]) continue;
                bool edge = (x > 0 && remove[i - 1]) || (x < w - 1 && remove[i + 1])
                         || (y > 0 && remove[i - w]) || (y < h - 1 && remove[i + w]);
                if (edge && px[i].a > 180) feather.Add(i);
            }
        foreach (int i in feather) px[i].a = 170;

        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log($"[MapArt] {Path.GetFileName(path)}: removed {cut} px ({100f * cut / px.Length:F0}%)");
        return true;
    }
}
