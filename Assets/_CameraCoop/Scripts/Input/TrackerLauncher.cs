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
        private bool ownsProcess;

        public bool IsRunning { get { return IsProcessRunning; } }
        public string LastError { get; private set; } = string.Empty;

        protected virtual bool IsProcessRunning { get { return proc != null && !proc.HasExited; } }

        private void Start()
        {
            RefreshStatus();
        }

        private void Update()
        {
            RefreshStatus();
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
        }

        public bool StartTracker()
        {
            RefreshStatus();
            LastError = string.Empty;
            if (IsRunning)
            {
                UpdateLabel();
                return true;
            }

            string error;
            if (!TryLaunchProcess(out error))
            {
                LastError = string.IsNullOrEmpty(error) ? "캠: 실행 실패" : error;
                UpdateLabel();
                return false;
            }

            ownsProcess = true;
            RefreshStatus();
            return IsRunning;
        }

        public void StopTracker()
        {
            LastError = string.Empty;
            if (ownsProcess)
            {
                try
                {
                    StopProcess();
                    if (IsRunning)
                    {
                        LastError = "캠: 종료 실패";
                    }
                    else
                    {
                        ownsProcess = false;
                    }
                }
                catch (System.Exception e)
                {
                    LastError = "캠: 종료 실패";
                    Debug.LogWarning("[TrackerLauncher] tracker 종료 실패: " + e.Message);
                }
            }
            UpdateLabel();
        }

        public void RefreshStatus()
        {
            if (ownsProcess && !IsRunning)
            {
                if (string.IsNullOrEmpty(LastError))
                {
                    LastError = "캠: tracker 종료됨";
                }
                try
                {
                    StopProcess();
                    ownsProcess = false;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[TrackerLauncher] 종료된 tracker 정리 실패: " + e.Message);
                }
            }
            UpdateLabel();
        }

        protected virtual bool TryLaunchProcess(out string error)
        {
            error = string.Empty;
            string dir = ResolveTrackerDir();
            if (dir == null)
            {
                error = "캠: tracker 폴더 없음";
                Debug.LogWarning("[TrackerLauncher] tracker 폴더를 찾지 못했습니다. 빌드는 exe 옆 tracker/, Editor는 PythonTracker/ 를 씁니다.");
                return false;
            }
            string python = ResolvePython(dir);
            if (python == null)
            {
                error = "캠: setup 먼저 실행";
                Debug.LogWarning("[TrackerLauncher] venv를 찾지 못했습니다. tracker/setup_tracker.bat 을 먼저 실행하세요: " + dir);
                return false;
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
                if (proc == null)
                {
                    error = "캠: 실행 실패";
                    Debug.LogWarning("[TrackerLauncher] tracker 실행 실패: 프로세스가 생성되지 않았습니다.");
                    return false;
                }
                return true;
            }
            catch (System.Exception e)
            {
                proc = null;
                error = "캠: 실행 실패";
                Debug.LogWarning("[TrackerLauncher] tracker 실행 실패: " + e.Message);
                return false;
            }
        }

        protected virtual void StopProcess()
        {
            if (proc == null)
            {
                return;
            }
            if (!proc.HasExited)
            {
                KillTree(proc);
                if (!proc.WaitForExit(3000))
                {
                    return;
                }
            }
            proc.Dispose();
            proc = null;
        }

        // tracker는 자식 프로세스를 하나 더 띄운다 (2026-08-27 실측). 부모만 죽이면 자식이 캠을 계속 잡는다.
        private static void KillTree(Process target)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            RunSilent("taskkill", "/PID " + target.Id + " /T /F");
#else
            RunSilent("/usr/bin/pkill", "-P " + target.Id); // 자식 먼저 — 부모를 먼저 죽이면 고아가 캠을 물고 남는다
            target.Kill();
#endif
        }

        private static void RunSilent(string file, string args)
        {
            var info = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (Process p = Process.Start(info))
            {
                if (p == null)
                {
                    throw new System.InvalidOperationException(file + " 프로세스를 시작하지 못했습니다.");
                }
                if (!p.WaitForExit(3000))
                {
                    throw new System.TimeoutException(file + " 프로세스 종료 대기 시간이 초과되었습니다.");
                }
            }
        }

        // dataPath에서 위로 훑어 tracker/(배포) 또는 PythonTracker/(개발)를 찾는다.
        // 깊이가 OS마다 다르기 때문이다 — Windows 빌드는 1단계(<exe>/CameraCoop_Data),
        // Editor도 1단계(<root>/Assets), macOS 빌드는 4단계(<app>/Contents/Resources/Data).
        private static string ResolveTrackerDir()
        {
            string dir = Application.dataPath;
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(dir); i++)
            {
                string built = Path.Combine(dir, "tracker");
                if (Directory.Exists(built))
                {
                    return built;
                }
                string dev = Path.Combine(dir, "PythonTracker");
                if (Directory.Exists(dev))
                {
                    return dev;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
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
            SetLabel(!string.IsNullOrEmpty(LastError) ? LastError : IsRunning ? "캠 끄기" : "캠 켜기");
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
