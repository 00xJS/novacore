// Batch/menu build entry points. iOS: generates the Xcode project at
// Builds/iOS, then enables automatic signing.
//
// SET THESE BEFORE BUILDING TO A DEVICE: BundleId and TeamId below are
// placeholders. Use your own reverse-domain bundle identifier and your Apple
// Developer team id (or leave TeamId empty and pick your team in Xcode after
// opening the generated project).
//
//   Unity -batchmode -buildTarget iOS -executeMethod BuildScript.BuildIos
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class BuildScript
{
    // Placeholders — replace with your own before a device build (see header).
    const string BundleId = "com.example.galaxyroyale";
    const string TeamId = ""; // empty = choose the team in Xcode

    [MenuItem("GalaxyRoyale/Build iOS (Xcode project)")]
    public static void BuildIosMenu() => BuildIosInternal(exitOnDone: false);

    public static void BuildIos() => BuildIosInternal(exitOnDone: true);

    static void BuildIosInternal(bool exitOnDone)
    {
        PlayerSettings.productName = "Galaxy Royale";
        PlayerSettings.companyName = "Galaxy Royale";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
        PlayerSettings.bundleVersion = "1.1";
        PlayerSettings.iOS.buildNumber = "2"; // prototype shipped as 1 — must increase
        PlayerSettings.iOS.appleDeveloperTeamID = TeamId;
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        PlayerSettings.iOS.targetOSVersionString = "13.0";

        // Portrait-locked, like v1's shell.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = false;
        PlayerSettings.allowedAutorotateToLandscapeRight = false;

        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Boot.unity" },
            locationPathName = "Builds/iOS",
            target = BuildTarget.iOS,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
        Debug.Log($"[Build] iOS: {report.summary.result}, {report.summary.totalErrors} errors, " +
                  $"output: {report.summary.outputPath}");
        if (exitOnDone) EditorApplication.Exit(ok ? 0 : 1);
    }
}
