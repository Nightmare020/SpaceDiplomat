using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnusedAssetAnalyzer
{
    [MenuItem("Build/Analyze Unused Assets")]
    public static void Analyze()
    {
        // Collect roots: scenes in build
        var scenePaths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
        if (scenePaths.Count == 0)
        {
            Debug.LogError("No scenes in Build Settings. Add your scenes before analyzing.");
            return;
        }

        // Open scenes additive (headless) and collect dependencies
        var prevScene = EditorSceneManager.GetActiveScene().path;
        var openSceneGUIDs = new HashSet<string>();
        try
        {
            var opened = new List<SceneSetup>();
            foreach (var sp in scenePaths)
            {
                var s = EditorSceneManager.OpenScene(sp, OpenSceneMode.Additive);
                opened.Add(new SceneSetup { path = sp, isLoaded = true, isActive = false, isSubScene = false });
            }

            // Assets referenced by scenes
            var deps = AssetDatabase.GetDependencies(scenePaths.ToArray(), true).ToHashSet();

            // + all assets under Resources (they’re always included at runtime if inside Resources)
            var resourcesGUIDs = AssetDatabase.FindAssets("", new[] { "Assets/Resources" }).ToHashSet();
            foreach (var g in resourcesGUIDs)
                deps.Add(AssetDatabase.GUIDToAssetPath(g));

            // + StreamingAssets (raw copy)
            if (Directory.Exists("Assets/StreamingAssets"))
            {
                foreach (var f in Directory.GetFiles("Assets/StreamingAssets", "*", SearchOption.AllDirectories))
                    deps.Add(f.Replace("\\", "/"));
            }

            // Compute unused = project assets – deps – Editor code – non-assets
            var allAssetGUIDs = AssetDatabase.FindAssets(""); // all
            var allPaths = allAssetGUIDs.Select(AssetDatabase.GUIDToAssetPath)
                                        .Where(p => p.StartsWith("Assets/") && !p.Contains("/Editor/"))
                                        .Where(p => !Directory.Exists(p))
                                        .ToHashSet();

            // ignore meta files
            allPaths.RemoveWhere(p => p.EndsWith(".meta"));

            var used = deps.Where(p => p.StartsWith("Assets/") && !p.Contains("/Editor/"))
                           .Select(p => p.Replace("\\", "/")).ToHashSet();

            var unused = allPaths.Except(used).OrderBy(p => p).ToList();

            var outPath = "UnusedAssetsReport.txt";
            File.WriteAllLines(outPath, unused);
            AssetDatabase.Refresh();

            Debug.Log($"UnusedAssetAnalyzer: wrote {unused.Count} paths to {outPath}");
            if (unused.Count > 0)
                Debug.Log("Tip: Review before deleting. Move unused folders under 'Assets/_Archive' first.");
        }
        finally
        {
            // close extra scenes
            for (int i = 1; i < EditorSceneManager.sceneCount; i++)
            {
                var s = EditorSceneManager.GetSceneAt(i);
                if (s.path != prevScene) EditorSceneManager.CloseScene(s, true);
            }
        }
    }
}

