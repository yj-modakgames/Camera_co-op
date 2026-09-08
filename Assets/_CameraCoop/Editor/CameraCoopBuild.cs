using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CameraCoop.EditorTools
{
    // 빌드 조건의 단일 출처는 docs/10_build.md. 이 파일은 그 문서를 실행 가능한 형태로 옮긴 것이다.
    public static class CameraCoopBuild
    {
        public const string ScenePath = "Assets/_CameraCoop/Scenes/NetplayTest.unity";
        private const string OutputDir = "Builds/CameraCoop";

        // 현재 Editor가 도는 OS에 맞춰 빌드한다. CLI에서는:
        //   unity cmd build --target <StandaloneWindows64|StandaloneOSX> --outputPath <경로> --confirm true
        [MenuItem("Camera Co-op/Build for This OS")]
        public static void BuildForThisOS()
        {
            bool mac = Application.platform == RuntimePlatform.OSXEditor;
            BuildTarget target = mac ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneWindows64;
            string output = Path.Combine(OutputDir, mac ? "CameraCoop.app" : "CameraCoop.exe");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                locationPathName = output,
                options = BuildOptions.None,
            };
            BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(options);
            Debug.Log("[CameraCoopBuild] " + report.summary.result + " -> " + output
                + " (errors " + report.summary.totalErrors + ")");
        }
    }

    // 빌드 산출물 옆에 tracker/Steam 부속 파일을 OS에 맞게 깔아준다.
    // 메뉴 빌드든 CLI 빌드든 항상 실행되므로, 손으로 복사하는 단계가 없다.
    public class CameraCoopBuildPayload : IPostprocessBuildWithReport
    {
        private const string AutomaticGraphicsSwitchingKey = "NSSupportsAutomaticGraphicsSwitching";

        public int callbackOrder { get { return 0; } }

        // 프로젝트 루트 기준 원본 경로
        private const string TrackerSrc = "PythonTracker";
        private const string DistSrc = "PythonTracker/dist";

        public void OnPostprocessBuild(BuildReport report)
        {
            // Windows: <...>/CameraCoop.exe, macOS: <...>/CameraCoop.app — 양쪽 다 부모 폴더가 배포 폴더다.
            string dest = Path.GetDirectoryName(report.summary.outputPath);
            if (string.IsNullOrEmpty(dest))
            {
                Debug.LogWarning("[CameraCoopBuild] 산출물 경로를 해석하지 못해 payload를 건너뜁니다: " + report.summary.outputPath);
                return;
            }
            bool mac = report.summary.platform == BuildTarget.StandaloneOSX;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            ApplyMacPlayerSettings(report.summary.platform, report.summary.outputPath);

            string appId = Path.Combine(root, "steam_appid.txt");
            foreach (string target in SteamAppIdDestinations(mac, dest, report.summary.outputPath)) Copy(appId, target);
            Copy(Path.Combine(root, TrackerSrc, "fake_hand.py"), Path.Combine(dest, "fake_hand.py"));
            Copy(Path.Combine(root, DistSrc, "README_FIRST.txt"), Path.Combine(dest, "README_FIRST.txt"));

            string tracker = Path.Combine(dest, "tracker");
            Directory.CreateDirectory(Path.Combine(tracker, "models"));
            Copy(Path.Combine(root, TrackerSrc, "hand_tracker.py"), Path.Combine(tracker, "hand_tracker.py"));
            Copy(Path.Combine(root, TrackerSrc, "camera_utils.py"), Path.Combine(tracker, "camera_utils.py"));
            Copy(Path.Combine(root, TrackerSrc, "config.py"), Path.Combine(tracker, "config.py"));
            Copy(Path.Combine(root, TrackerSrc, "one_euro_filter.py"), Path.Combine(tracker, "one_euro_filter.py"));
            Copy(Path.Combine(root, TrackerSrc, "models", "hand_landmarker.task"), Path.Combine(tracker, "models", "hand_landmarker.task"));

            // Intel Mac은 mediapipe 0.10.21 (1.0.1은 arm64 wheel만 있다). docs/10 §2 참조.
            string req = mac ? "requirements-intel-mac.txt" : "requirements.txt";
            Copy(Path.Combine(root, TrackerSrc, req), Path.Combine(tracker, "requirements.txt"));

            string setup = mac ? "setup_tracker.sh" : "setup_tracker.bat";
            string run = mac ? "run_tracker.sh" : "run_tracker.bat";
            Copy(Path.Combine(root, DistSrc, setup), Path.Combine(tracker, setup));
            Copy(Path.Combine(root, DistSrc, run), Path.Combine(tracker, run));
            if (mac)
            {
                MakeExecutable(Path.Combine(tracker, setup));
                MakeExecutable(Path.Combine(tracker, run));
            }

            Debug.Log("[CameraCoopBuild] payload 배치 완료 (" + (mac ? "macOS" : "Windows") + "): " + dest);
        }

        // Steam은 cwd에서 steam_appid.txt를 찾는다. macOS는 Finder로 .app을 열면 cwd가 "/"라
        // .app 옆 파일을 못 읽으므로, 실행 파일과 같은 Contents/MacOS에도 함께 둔다.
        private static string[] SteamAppIdDestinations(bool mac, string dest, string outputPath)
        {
            string beside = Path.Combine(dest, "steam_appid.txt");
            if (!mac || string.IsNullOrEmpty(outputPath)) return new[] { beside };
            return new[] { beside, Path.Combine(outputPath, "Contents", "MacOS", "steam_appid.txt") };
        }

        private static void ApplyMacPlayerSettings(BuildTarget platform, string outputPath)
        {
            if (platform != BuildTarget.StandaloneOSX)
            {
                return;
            }

            string plistPath = Path.Combine(outputPath, "Contents", "Info.plist");
            XDocument plist = XDocument.Load(plistPath, LoadOptions.PreserveWhitespace);
            XElement dict = plist.Root.Element("dict");
            XElement key = dict.Elements("key").FirstOrDefault(element => element.Value == AutomaticGraphicsSwitchingKey);
            if (key == null)
            {
                dict.Add(new XElement("key", AutomaticGraphicsSwitchingKey), new XElement("true"));
            }
            else
            {
                key.ElementsAfterSelf().First().ReplaceWith(new XElement("true"));
            }
            plist.Save(plistPath, SaveOptions.DisableFormatting);
        }

        private static void Copy(string from, string to)
        {
            if (!File.Exists(from))
            {
                Debug.LogWarning("[CameraCoopBuild] 원본이 없어 건너뜁니다: " + from);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
        }

        // .NET Standard 2.1에는 File.SetUnixFileMode가 없어 chmod를 직접 부른다.
        private static void MakeExecutable(string path)
        {
            if (!File.Exists(path) || Application.platform != RuntimePlatform.OSXEditor)
            {
                return;
            }
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("chmod", "+x \"" + path + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(info))
                {
                    p.WaitForExit(3000);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CameraCoopBuild] chmod 실패 (" + path + "): " + e.Message);
            }
        }
    }
}
