// Touches the generated Xcode project after every iOS build:
//   * ITSAppUsesNonExemptEncryption = NO — the game uses no encryption of its
//     own, and declaring it spares every TestFlight upload the export-
//     compliance question;
//   * the iCloud key-value store capability for the save backup (CloudSave,
//     Plugins/iOS/GRCloudSave.mm). Automatic signing with
//     -allowProvisioningUpdates enables iCloud on the App ID by itself. Set
//     GR_ICLOUD=0 to build without it (e.g. a team that can't use iCloud);
//   * the Game Center capability (GameCenter.cs — the player opts in from
//     Settings). GR_GAMECENTER=0 builds without it;
//   * the home-screen widget and the raid Live Activity: a WidgetKit
//     extension (iOSWidget/GalaxyWidget.swift, plus the shared
//     Plugins/iOS/RaidAttributes.swift) embedded in the app, and an App Group
//     both share for the game's snapshot (Game/HomeWidget.cs). GR_WIDGET=0
//     builds without them (the app's widget calls then do nothing).
#if UNITY_IOS
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using UnityEngine;

public static class IosPostProcess
{
    const string EntitlementsFile = "Unity-iPhone/GalaxyRoyale.entitlements";
    const string WidgetDir = "GalaxyWidget";
    const string WidgetName = "GalaxyWidget";
    /// <summary>The extension needs Live Activities' ActivityContent API (iOS 16.2).</summary>
    const string WidgetMinIos = "16.2";

    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        bool iCloud = Environment.GetEnvironmentVariable("GR_ICLOUD") != "0";
        bool gameCenter = Environment.GetEnvironmentVariable("GR_GAMECENTER") != "0";
        bool widget = Environment.GetEnvironmentVariable("GR_WIDGET") != "0";
        string bundleId = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS);
        string appGroup = "group." + bundleId;

        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        if (widget)
        {
            plist.root.SetBoolean("NSSupportsLiveActivities", true);
            plist.root.SetString("GRAppGroup", appGroup);
        }
        plist.WriteToFile(plistPath);

        string projPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromFile(projPath);
        string mainGuid = project.GetUnityMainTargetGuid();
        string frameworkGuid = project.GetUnityFrameworkTargetGuid();

        // The app-side widget bridge is Swift (Plugins/iOS/GRWidgetBridge.swift):
        // ActivityKit only exists from iOS 16.1, so it's linked weak for older systems.
        project.SetBuildProperty(frameworkGuid, "SWIFT_VERSION", "5.0");
        project.AddBuildProperty(frameworkGuid, "OTHER_LDFLAGS", "-weak_framework ActivityKit");
        if (widget) AddWidgetExtension(project, path, mainGuid, bundleId, appGroup);
        project.WriteToFile(projPath);

        if (!iCloud && !gameCenter && !widget) return;
        var capabilities = new ProjectCapabilityManager(projPath, EntitlementsFile, null, mainGuid);
        // Key-value storage only: no iCloud Documents, no CloudKit, no containers.
        if (iCloud) capabilities.AddiCloud(true, false, false, false, null);
        if (gameCenter) capabilities.AddGameCenter();
        if (widget) capabilities.AddAppGroups(new[] { appGroup });
        capabilities.WriteToFile();
    }

    /// <summary>The WidgetKit extension: its sources, Info.plist and entitlements
    /// copied into the Xcode project, a target for it, and an embed step in the app.</summary>
    static void AddWidgetExtension(PBXProject project, string path, string mainGuid, string bundleId, string appGroup)
    {
        string dir = Path.Combine(path, WidgetDir);
        Directory.CreateDirectory(dir);
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "iOSWidget", "GalaxyWidget.swift"));
        File.Copy(source, Path.Combine(dir, "GalaxyWidget.swift"), overwrite: true);

        var info = new PlistDocument();
        var root = info.root;
        root.SetString("CFBundleDevelopmentRegion", "en");
        root.SetString("CFBundleDisplayName", "Galaxy Royale");
        root.SetString("CFBundleExecutable", "$(EXECUTABLE_NAME)");
        root.SetString("CFBundleIdentifier", "$(PRODUCT_BUNDLE_IDENTIFIER)");
        root.SetString("CFBundleInfoDictionaryVersion", "6.0");
        root.SetString("CFBundleName", "$(PRODUCT_NAME)");
        root.SetString("CFBundlePackageType", "XPC!");
        // An extension's version must match its app's.
        root.SetString("CFBundleShortVersionString", PlayerSettings.bundleVersion);
        root.SetString("CFBundleVersion", PlayerSettings.iOS.buildNumber);
        root.SetString("GRAppGroup", appGroup);
        root.CreateDict("NSExtension").SetString("NSExtensionPointIdentifier", "com.apple.widgetkit-extension");
        info.WriteToFile(Path.Combine(dir, "Info.plist"));

        var entitlements = new PlistDocument();
        entitlements.root.CreateArray("com.apple.security.application-groups").AddString(appGroup);
        entitlements.WriteToFile(Path.Combine(dir, WidgetName + ".entitlements"));

        string ext = project.AddAppExtension(mainGuid, WidgetName, bundleId + ".widget", WidgetDir + "/Info.plist");
        project.AddSourcesBuildPhase(ext);
        project.AddFrameworksBuildPhase(ext);
        project.AddFileToBuild(ext, project.AddFile(WidgetDir + "/GalaxyWidget.swift", WidgetDir + "/GalaxyWidget.swift"));
        // The Live Activity's attributes: the same file Unity compiled into the app.
        string shared = project.FindFileGuidByProjectPath("Libraries/Plugins/iOS/RaidAttributes.swift");
        if (string.IsNullOrEmpty(shared))
            shared = project.AddFile("Libraries/Plugins/iOS/RaidAttributes.swift", "Libraries/Plugins/iOS/RaidAttributes.swift");
        project.AddFileToBuild(ext, shared);
        project.AddFile(WidgetDir + "/" + WidgetName + ".entitlements", WidgetDir + "/" + WidgetName + ".entitlements");
        project.AddFrameworkToProject(ext, "WidgetKit.framework", false);
        project.AddFrameworkToProject(ext, "SwiftUI.framework", false);
        project.AddFrameworkToProject(ext, "ActivityKit.framework", false);

        project.SetBuildProperty(ext, "SWIFT_VERSION", "5.0");
        project.SetBuildProperty(ext, "IPHONEOS_DEPLOYMENT_TARGET", WidgetMinIos);
        project.SetBuildProperty(ext, "TARGETED_DEVICE_FAMILY", "1");
        project.SetBuildProperty(ext, "ARCHS", "arm64");
        project.SetBuildProperty(ext, "CODE_SIGN_ENTITLEMENTS", WidgetDir + "/" + WidgetName + ".entitlements");
        project.SetBuildProperty(ext, "CODE_SIGN_STYLE", "Automatic");
        project.SetBuildProperty(ext, "DEVELOPMENT_TEAM", PlayerSettings.iOS.appleDeveloperTeamID);
        project.SetBuildProperty(ext, "SKIP_INSTALL", "YES");
        project.SetBuildProperty(ext, "GENERATE_INFOPLIST_FILE", "NO");
        project.SetBuildProperty(ext, "LD_RUNPATH_SEARCH_PATHS",
            "$(inherited) @executable_path/Frameworks @executable_path/../../Frameworks");
    }
}
#endif
