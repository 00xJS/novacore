// Batch/menu build entry points. iOS: generates the Xcode project at
// Builds/iOS (device) or Builds/iOS-Sim (iOS Simulator, arm64), with
// automatic signing.
//
// SIGNING: the committed bundle id / team are placeholders (this is a public
// repo). Supply your own through the environment for device builds instead
// of editing this file:
//
//   GR_BUNDLE_ID=com.you.galaxyroyale GR_TEAM_ID=ABCDE12345 \
//   Unity -batchmode -buildTarget iOS -executeMethod BuildScript.BuildIos
//
// The overrides only live for the duration of the build — the committed
// values are restored afterwards, so ProjectSettings.asset never picks up a
// real identity. (Without GR_TEAM_ID, pick your team in Xcode instead.)
//
// Release builds (scripts/testflight.sh) also pass GR_BUILD_NUMBER — every
// TestFlight upload needs a higher one — and optionally GR_VERSION.
//
//   Unity -batchmode -buildTarget iOS -executeMethod BuildScript.BuildIosSimulator
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class BuildScript
{
    const string PlaceholderBundleId = "com.example.galaxyroyale";

    static string BundleId => Env("GR_BUNDLE_ID") ?? PlaceholderBundleId;
    static string TeamId => Env("GR_TEAM_ID") ?? ""; // empty = choose the team in Xcode

    static string? Env(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [MenuItem("GalaxyRoyale/Build iOS (Xcode project)")]
    public static void BuildIosMenu() => BuildIosInternal(simulator: false, exitOnDone: false);

    [MenuItem("GalaxyRoyale/Build iOS Simulator (Xcode project)")]
    public static void BuildIosSimulatorMenu() => BuildIosInternal(simulator: true, exitOnDone: false);

    public static void BuildIos() => BuildIosInternal(simulator: false, exitOnDone: true);

    public static void BuildIosSimulator() => BuildIosInternal(simulator: true, exitOnDone: true);

    static void BuildIosInternal(bool simulator, bool exitOnDone)
    {
        // Snapshot what's committed so the build leaves ProjectSettings.asset as it was.
        string committedId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
        string committedTeam = PlayerSettings.iOS.appleDeveloperTeamID;
        string committedVersion = PlayerSettings.bundleVersion;
        string committedBuild = PlayerSettings.iOS.buildNumber;
        var committedSdk = PlayerSettings.iOS.sdkVersion;
        var committedSimArch = PlayerSettings.iOS.simulatorSdkArchitecture;
        bool ok = false;
        try
        {
            PlayerSettings.productName = "Galaxy Royale";
            PlayerSettings.companyName = "Galaxy Royale";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.bundleVersion = Env("GR_VERSION") ?? "1.2";
            // Rises with every build (UTC yymmddHHMM, as scripts/testflight.sh stamps them): TestFlight needs a new one per
            // upload, and iOS keys its cached icons (the notification banner's among them)
            // by it — a fixed "3" kept the old icon on notifications after the new one
            // shipped (user report 2026-09-29).
            PlayerSettings.iOS.buildNumber = Env("GR_BUILD_NUMBER")
                ?? System.DateTime.UtcNow.ToString("yyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);
            PlayerSettings.iOS.appleDeveloperTeamID = TeamId;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
            PlayerSettings.iOS.targetOSVersionString = "13.0"; // the editor clamps to its own floor
            PlayerSettings.iOS.sdkVersion = simulator ? iOSSdkVersion.SimulatorSDK : iOSSdkVersion.DeviceSDK;
            if (simulator) PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;

            // iPhone only: the layout is a portrait phone design (an iPad build would
            // need its own screenshots and review on iPad).
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;

            // Portrait-locked, like v1's shell.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Boot.unity" },
                locationPathName = simulator ? "Builds/iOS-Sim" : "Builds/iOS",
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            Debug.Log($"[Build] iOS{(simulator ? " Simulator" : "")}: {report.summary.result}, " +
                      $"{report.summary.totalErrors} errors, output: {report.summary.outputPath}");
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, committedId);
            PlayerSettings.iOS.appleDeveloperTeamID = committedTeam;
            PlayerSettings.bundleVersion = committedVersion;
            PlayerSettings.iOS.buildNumber = committedBuild;
            PlayerSettings.iOS.sdkVersion = committedSdk;
            PlayerSettings.iOS.simulatorSdkArchitecture = committedSimArch;
            AssetDatabase.SaveAssets();
        }
        if (exitOnDone) EditorApplication.Exit(ok ? 0 : 1);
    }
}
