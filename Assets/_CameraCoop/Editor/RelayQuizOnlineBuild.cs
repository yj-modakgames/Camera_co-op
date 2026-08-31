using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CameraCoop.EditorTools
{
    public static class RelayQuizOnlineBuild
    {
        public const string ScenePath = "Assets/_CameraCoop/Scenes/RelayQuizOnline.unity";
        public const string WindowsOutputPath = "Builds/RelayQuizOnline/CameraCoopRelayOnline.exe";
        public const string MacOutputPath = "Builds/RelayQuizOnlineMac/CameraCoopRelayOnline.app";
        private const string ArchitectureSetting = "Architecture";
        private const string CameraUsageDescription =
            "RelayQuiz uses the camera to track your hands for drawing and game controls.";

        [MenuItem("Camera Co-op/RelayQuiz Online/Build Windows x64")]
        public static void BuildWindows64()
        {
            Build(BuildTarget.StandaloneWindows64, WindowsOutputPath);
        }

        [MenuItem("Camera Co-op/RelayQuiz Online/Build Intel Mac x64")]
        public static void BuildIntelMac64()
        {
            Build(BuildTarget.StandaloneOSX, MacOutputPath);
        }

        private static void Build(BuildTarget target, string relativeOutputPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            {
                throw new BuildFailedException("RelayQuiz Online requires an idle Editor in EditMode.");
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                throw new BuildFailedException("RelayQuiz Online cannot build " + target
                    + ": its Build Support module is unavailable in Unity " + Application.unityVersion
                    + ". Installing a module requires separate approval; no installation was attempted.");
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                throw new BuildFailedException("Required RelayQuiz Online scene is missing: " + ScenePath);
            }
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty)
                {
                    throw new BuildFailedException("Save or preserve the unsaved scene before building: "
                        + scene.path + ". The build helper does not save scenes.");
                }
            }

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputPath = Path.Combine(root, relativeOutputPath);
            string platformName = BuildPipeline.GetBuildTargetName(target);
            string previousArchitecture = EditorUserBuildSettings.GetPlatformSettings(
                platformName, ArchitectureSetting);
            bool mac = target == BuildTarget.StandaloneOSX;
            string previousCameraUsage = mac ? PlayerSettings.macOS.cameraUsageDescription : null;

            try
            {
                // Unity 6000.3 DesktopStandaloneBuildWindowExtension uses this public API and enum.
                EditorUserBuildSettings.SetPlatformSettings(platformName, ArchitectureSetting,
                    OSArchitecture.x64.ToString().ToLowerInvariant());
                if (mac)
                    PlayerSettings.macOS.cameraUsageDescription = CameraUsageDescription;

                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    target = target,
                    targetGroup = BuildTargetGroup.Standalone,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    locationPathName = outputPath,
                    options = BuildOptions.None,
                });
                if (report == null)
                    throw new BuildFailedException("RelayQuiz Online build returned no BuildReport.");

                BuildSummary summary = report.summary;
                string result = "[RelayQuizOnlineBuild] " + summary.result + " -> " + outputPath
                    + " (errors " + summary.totalErrors + ", warnings " + summary.totalWarnings + ")";
                if (summary.result != BuildResult.Succeeded || summary.totalErrors != 0)
                    throw new BuildFailedException(result);
                if (summary.totalWarnings != 0)
                    Debug.LogWarning(result);
                else
                    Debug.Log(result);
            }
            finally
            {
                EditorUserBuildSettings.SetPlatformSettings(platformName, ArchitectureSetting,
                    previousArchitecture);
                if (mac)
                    PlayerSettings.macOS.cameraUsageDescription = previousCameraUsage;
            }
        }
    }
}
