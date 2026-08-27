using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEditor.OSXStandalone;
using UnityEngine;

// CLI: Unity -batchmode -quit -projectPath . -executeMethod MacBuild.Intel64
static class MacBuild
{
    const string OutPath = "Build/macOS/CameraCoop.app";

    [MenuItem("Build/macOS (Intel 64-bit)")]
    public static void Intel64()
    {
        UserBuildSettings.architecture = OSArchitecture.x64;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = OutPath,
            target = BuildTarget.StandaloneOSX,
            targetGroup = BuildTargetGroup.Standalone,
        });

        var summary = report.summary;
        Debug.Log($"[MacBuild] {summary.result} — {summary.totalSize} bytes, {summary.totalErrors} errors");
        if (summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
