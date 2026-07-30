// Creates the PanelSettings ASSET that UIController loads from Resources.
//
// Why an asset instead of ScriptableObject.CreateInstance at runtime: Unity 6's
// UI Toolkit text engine needs an ICU data asset for text measurement. The
// editor auto-assigns it to PanelSettings assets on import, but PanelSettings
// created at runtime never get one — the UI then NullReferenceExceptions on
// every text layout in device builds (works in-editor, dies on iPhone).
//
// Run via menu "GalaxyRoyale → Create Panel Settings Asset" or
// -batchmode -executeMethod PanelSettingsFactory.BatchRun
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using GalaxyRoyale.Game.UI;

public static class PanelSettingsFactory
{
    const string AssetPath = "Assets/Resources/GalaxyRoyalePanelSettings.asset";

    [MenuItem("GalaxyRoyale/Create Panel Settings Asset")]
    public static void Create()
    {
        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(AssetPath);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
        }

        settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
            "Assets/Resources/GalaxyRoyaleTheme.tss");
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(UiTheme.W, UiTheme.H);
        settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        settings.match = 0f; // match width — v1's fixed-390 layout scales by width

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"PanelSettingsFactory: wrote {AssetPath}");
    }

    public static void BatchRun()
    {
        Create();
        EditorApplication.Exit(0);
    }
}
