// Creates the template material assets that runtime code clones (see
// MapVisuals.LitOpaque / UnlitTransparent / SpriteDefault).
//
// Why assets: shaders only ship in a device build when a built asset references
// them. All GalaxyRoyale materials are created procedurally at runtime via
// Shader.Find, which therefore returned null on iOS (magenta planet, missing
// starfields). These Resources/ templates guarantee the shaders — with the
// exact keyword variants we use — get into every build.
//
// Run via menu "GalaxyRoyale → Create Material Templates" or
// -batchmode -executeMethod MaterialFactory.BatchRun
using UnityEditor;
using UnityEngine;
using GalaxyRoyale.Game;

public static class MaterialFactory
{
    [MenuItem("GalaxyRoyale/Create Material Templates")]
    public static void Create()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Materials"))
            AssetDatabase.CreateFolder("Assets/Resources", "Materials");

        Write("Assets/Resources/Materials/NovaLit.mat", () =>
            new Material(Shader.Find("Universal Render Pipeline/Lit")));

        Write("Assets/Resources/Materials/NovaUnlitTransparent.mat", () =>
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            MapVisuals.ConfigureTransparent(mat); // bake the transparent variant keywords
            return mat;
        });

        Write("Assets/Resources/Materials/NovaSprites.mat", () =>
            new Material(Shader.Find("Sprites/Default")));

        AssetDatabase.SaveAssets();
        Debug.Log("MaterialFactory: template materials present in Assets/Resources/Materials");
    }

    static void Write(string path, System.Func<Material> make)
    {
        // Existing assets are left alone so their GUIDs stay stable; delete the
        // .mat and rerun to regenerate.
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        AssetDatabase.CreateAsset(make(), path);
    }

    public static void BatchRun()
    {
        Create();
        EditorApplication.Exit(0);
    }
}
