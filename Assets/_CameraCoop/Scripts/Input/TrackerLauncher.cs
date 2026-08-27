using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace CameraCoop
{
    // 손 추적 tracker 프로세스를 게임 안에서 켜고 끈다. 캠을 잡고 있는 것은 Unity가 아니라
    // 이 Python 프로세스라, 이걸 죽여야 실제로 캠이 꺼진다 (docs/03 §1).
    public class TrackerLauncher : MonoBehaviour
    {
        [SerializeField] private Text buttonLabel;

        private Process proc;

        public bool IsRunning { get { return proc != null && !proc.HasExited; } }

        private void Start()
        {
            UpdateLabel();
        }

        public void OnClickToggle()
        {
            if (IsRunning)
            {
                StopTracker();
            }
            else
            {
                StartTracker();
            }
            UpdateLabel();
        }

        private void StartTracker()
        {
            string dir = ResolveTrackerDir();
            if (dir == null)
            {
                SetLabel("캠: tracker 폴더 없음");
                Debug.LogWarning("[TrackerLauncher] tracker 폴더를 찾지 못했습니다. 빌드는 exe 옆 tracker/, Editor는 PythonTracker/ 를 씁니다.");
                return;
            }
            string python = ResolvePython(dir);
            if (python == null)
            {
                SetLabel("캠: setup 먼저 실행");
                Debug.LogWarning("[TrackerLauncher] venv를 찾지 못했습니다. tracker/setup_tracker.bat 을 먼저 실행하세요: " + dir);
                return;
            }
            try
            {
                var info = new ProcessStartInfo(python, "hand_tracker.py")
                {
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                proc = Process.Start(info);
            }
            catch (System.Exception e)
            {
                proc = null;
                SetLabel("캠: 실행 실패");
                Debug.LogWarning("[TrackerLauncher] tracker 실행 실패: " + e.Message);
            }
        }

        private void StopTracker()
        {
            if (proc == null)
            {
                return;
            }
            try
            {
                if (!proc.HasExited)
                {
                    KillTree(proc);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TrackerLauncher] tracker 종료 실패: " + e.Message);
            }
            finally
            {
                proc.Dispose();
                proc = null;
            }
        }

        // tracker는 자식 프로세스를 하나 더 띄운다 (2026-08-27 실측). 부모만 죽이면 자식이 캠을 계속 잡는다.
        private static void KillTree(Process target)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var kill = new ProcessStartInfo("taskkill", "/PID " + target.Id + " /T /F")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (Process p = Process.Start(kill))
            {
                p.WaitForExit(3000);
            }
#else
            target.Kill();
#endif
        }

        // 빌드: <exe 폴더>/tracker, Editor: <프로젝트 루트>/PythonTracker
        private static string ResolveTrackerDir()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string built = Path.Combine(root, "tracker");
            if (Directory.Exists(built))
            {
                return built;
            }
            string dev = Path.Combine(root, "PythonTracker");
            return Directory.Exists(dev) ? dev : null;
        }

        // venv가 없으면 실행하지 않는다 — PATH의 python으로 띄워봐야 mediapipe가 없어 조용히 죽는다.
        private static string ResolvePython(string dir)
        {
            string win = Path.Combine(dir, ".venv", "Scripts", "python.exe");
            if (File.Exists(win))
            {
                return win;
            }
            string nix = Path.Combine(dir, ".venv", "bin", "python");
            return File.Exists(nix) ? nix : null;
        }

        private void UpdateLabel()
        {
            SetLabel(IsRunning ? "캠 끄기" : "캠 켜기");
        }

        private void SetLabel(string text)
        {
            if (buttonLabel != null)
            {
                buttonLabel.text = text;
            }
        }

        // 게임을 닫으면 캠도 꺼져야 한다 — 남은 tracker가 웹캠을 계속 점유하지 않게.
        private void OnDestroy()
        {
            StopTracker();
        }

        private void OnApplicationQuit()
        {
            StopTracker();
        }
    }
}
