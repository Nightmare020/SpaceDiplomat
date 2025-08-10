using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class SmartBuilder
{
    [MenuItem("Build/Make Release (Win64)")]
    public static void MakeReleaseWin64()
    {
        // Safety checks
        var baseUrl = Resources.Load<ServerSettings>("ServerSettings")?.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl) || !baseUrl.StartsWith("https://"))
        {
            if (!EditorUtility.DisplayDialog("Base URL check",
                "ServerSettings.BaseUrl is missing or not HTTPS.\n\nContinue anyway?", "Build", "Cancel"))
                return;
        }

        if (EditorUserBuildSettings.development)
        {
            if (!EditorUtility.DisplayDialog("Development Build is ON",
                "Development Build is enabled. For distribution, turn it OFF.\n\nContinue anyway?", "Build", "Cancel"))
                return;
        }

        // Player Settings (minimal, adjust to taste)
        PlayerSettings.stripEngineCode = true;
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Medium);
        PlayerSettings.SplashScreen.showUnityLogo = false;

        // Make output folder
        var outDir = "Builds/Windows";
        Directory.CreateDirectory(outDir);

        // Scenes from Build Settings
        var scenes = System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled);
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("No scenes", "Add scenes to Build Settings.", "OK");
            return;
        }

        // Build
        var buildPlayerOptions = new BuildPlayerOptions
        {
            target = BuildTarget.StandaloneWindows64,
            locationPathName = Path.Combine(outDir, "SpaceDiplomat.exe"),
            scenes = System.Array.ConvertAll(scenes, s => s.path),
            options = BuildOptions.CleanBuildCache
        };

        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        var summary = report.summary;
        var reportTxt = Path.Combine(outDir, "BuildReport.txt");
        File.WriteAllText(reportTxt,
            $"Result: {summary.result}\n" +
            $"Size: {summary.totalSize / (1024 * 1024f):0.0} MB\n" +
            $"Time: {summary.totalTime}\n" +
            $"Errors: {summary.totalErrors}  Warnings: {summary.totalWarnings}\n");
        Debug.Log($"Build finished: {summary.result}. Report at {reportTxt}");

        if (summary.result != BuildResult.Succeeded)
            EditorUtility.RevealInFinder(reportTxt);
    }
}