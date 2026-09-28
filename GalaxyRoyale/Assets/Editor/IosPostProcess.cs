// Touches the generated Xcode project after every iOS build:
//   * ITSAppUsesNonExemptEncryption = NO — the game uses no encryption of its
//     own, and declaring it spares every TestFlight upload the export-
//     compliance question;
//   * the iCloud key-value store capability for the save backup (CloudSave,
//     Plugins/iOS/GRCloudSave.mm). Automatic signing with
//     -allowProvisioningUpdates enables iCloud on the App ID by itself. Set
//     GR_ICLOUD=0 to build without it (e.g. a team that can't use iCloud).
#if UNITY_IOS
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class IosPostProcess
{
    const string EntitlementsFile = "Unity-iPhone/GalaxyRoyale.entitlements";

    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        plist.WriteToFile(plistPath);

        if (Environment.GetEnvironmentVariable("GR_ICLOUD") == "0") return;
        string projPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromFile(projPath);
        var capabilities = new ProjectCapabilityManager(projPath, EntitlementsFile, null,
            project.GetUnityMainTargetGuid());
        // Key-value storage only: no iCloud Documents, no CloudKit, no containers.
        capabilities.AddiCloud(true, false, false, false, null);
        capabilities.WriteToFile();
    }
}
#endif
