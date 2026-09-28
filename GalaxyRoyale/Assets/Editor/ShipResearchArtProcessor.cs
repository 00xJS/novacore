// Drop-in art for the ship + research pictures (see Game/UI/ShipArt.cs).
//
// Put raw renders here, named after the hull / tech id (case-insensitive;
// spaces, dashes and underscores ignored):
//   Assets/Textures/Ships/<HullId>.jpg|png       e.g. Leviathan.jpg, probe.png
//   Assets/Textures/Research/<TechId>.jpg|png    e.g. IonThrusters.jpg, ion_thrusters.png
// then run GalaxyRoyale → Process Ship & Research Art (or batch:
//   Unity -batchmode -executeMethod ShipResearchArtProcessor.BatchRun).
//
// Output lands in Assets/Resources/Ships/<HullId>.png and
// Assets/Resources/Research/<TechId>.png, where the game picks it up in place
// of the painted placeholder — no code changes. Keying reuses the building
// pipeline: a baked transparency CHECKERBOARD or a plain WHITE background is
// flood-filled out to real alpha (the threshold is picked per image from its
// corners); images that already have transparency pass through untouched.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using GalaxyRoyale.Data;

public static class ShipResearchArtProcessor
{
    const string ShipSrc = "Assets/Textures/Ships";
    const string ShipDst = "Assets/Resources/Ships";
    const string TechSrc = "Assets/Textures/Research";
    const string TechDst = "Assets/Resources/Research";

    [MenuItem("GalaxyRoyale/Process Ship & Research Art")]
    public static void Run()
    {
        var written = new List<string>();
        int ships = ProcessFolder(ShipSrc, ShipDst, Enum.GetNames(typeof(HullId)), written);
        int techs = ProcessFolder(TechSrc, TechDst, Enum.GetNames(typeof(TechId)), written);
        AssetDatabase.Refresh();

        // UI art: real transparency, clamped, no mips (UI Toolkit draws it 1:1-ish).
        foreach (var path in written)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter imp) continue;
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        Debug.Log($"[ShipResearchArt] {ships} ship + {techs} research images → Resources/Ships, Resources/Research");
    }

    public static void BatchRun()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
    }

    static string Normalize(string s) =>
        s.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();

    static int ProcessFolder(string srcDir, string dstDir, string[] ids, List<string> written)
    {
        if (!Directory.Exists(srcDir))
        {
            Debug.Log($"[ShipResearchArt] {srcDir} not found — nothing to process there.");
            return 0;
        }
        Directory.CreateDirectory(dstDir);
        var byName = new Dictionary<string, string>();
        foreach (var id in ids) byName[Normalize(id)] = id;

        int ok = 0;
        foreach (var file in Directory.GetFiles(srcDir))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png") continue;
            if (!byName.TryGetValue(Normalize(Path.GetFileNameWithoutExtension(file)), out var id))
            {
                Debug.LogWarning($"[ShipResearchArt] {Path.GetFileName(file)}: no hull/tech with that name — skipped.");
                continue;
            }
            string dst = $"{dstDir}/{id}.png";
            if (BuildingArtProcessor.Process(file, dst, PickThreshold(file)))
            {
                written.Add(dst);
                ok++;
            }
        }
        return ok;
    }

    /// <summary>White-background renders need a tight near-white key (a loose one
    /// eats light-grey art); checkerboards need a loose one. Decide from corners.</summary>
    static float PickThreshold(string file)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!tex.LoadImage(File.ReadAllBytes(file))) return BuildingArtProcessor.CheckerBright;
            int w = tex.width, h = tex.height, white = 0, n = 0;
            foreach (var (x, y) in new[] { (2, 2), (w - 3, 2), (2, h - 3), (w - 3, h - 3) })
            {
                var c = tex.GetPixel(Mathf.Clamp(x, 0, w - 1), Mathf.Clamp(y, 0, h - 1));
                n++;
                if (c.a > 0.9f && c.r > 0.93f && c.g > 0.93f && c.b > 0.93f) white++;
            }
            return white == n ? BuildingArtProcessor.WhiteBgBright : BuildingArtProcessor.CheckerBright;
        }
        finally { UnityEngine.Object.DestroyImmediate(tex); }
    }
}
