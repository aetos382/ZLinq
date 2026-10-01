using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Usage: Unity.exe -batchmode -quit -projectPath <path> -executeMethod PlayerBuilder.Build
//          -outDir <dir> [-backend IL2CPP|Mono]
// Assets/Plugins/ZLinq/ZLinq.dll and Assets/StreamingAssets/variant.txt must be placed beforehand.
public static class PlayerBuilder
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    public static void Build()
    {
        var outDir = GetArgument("-outDir") ?? throw new ArgumentException("-outDir is required.");
        var backend = GetArgument("-backend") ?? "IL2CPP";

        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,
            backend == "Mono" ? ScriptingImplementation.Mono2x : ScriptingImplementation.IL2CPP);
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone, Il2CppCompilerConfiguration.Release);
        PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 320;
        PlayerSettings.defaultScreenHeight = 240;
        PlayerSettings.runInBackground = true;
        PlayerSettings.usePlayerLog = true;

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(outDir, "Bench.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"Build result: {report.summary.result}, errors: {report.summary.totalErrors}");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    static string GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
