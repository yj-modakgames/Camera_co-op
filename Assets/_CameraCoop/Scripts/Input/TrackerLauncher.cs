using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace CameraCoop
{
    public enum CameraDiscoveryOutcome
    {
        Success,
        Error,
        Timeout,
        Canceled
    }

    public sealed class CameraDiscoveryResult
    {
        public readonly int[] Indices;
        public readonly string Error;
        public readonly CameraDiscoveryOutcome Outcome;

        public CameraDiscoveryResult(int[] indices, string error, CameraDiscoveryOutcome outcome)
        {
            Indices = indices ?? new int[0];
            Error = error ?? string.Empty;
            Outcome = outcome;
        }
    }

    public interface ICameraDiscoveryWorker
    {
        Task<CameraDiscoveryResult> DiscoverAsync(CancellationToken token);
    }

    // 손 추적 tracker 프로세스를 게임 안에서 켜고 끈다. 캠을 잡고 있는 것은 Unity가 아니라
    // 이 Python 프로세스라, 이걸 죽여야 실제로 캠이 꺼진다 (docs/03 §1).
    public class TrackerLauncher : MonoBehaviour
    {
        [SerializeField] private Text buttonLabel;
        [SerializeField] private int cameraIndex;
        [SerializeField] private bool previewEnabled = true;

        private Process proc;
        private bool ownsProcess;
        internal const int DiagnosticLimit = 2048;
        private readonly object stderrLock = new object();
        private readonly StringBuilder stderrTail = new StringBuilder(DiagnosticLimit);
        private Process stderrProcess;
        private bool stderrComplete = true;
        private double exitedAt = -1d;
        private bool reportExitFailure;
        private Task<CameraDiscoveryResult> discoveryTask;
        private CancellationTokenSource discoveryCancellation;
        private int discoveryGeneration;

        protected virtual ICameraDiscoveryWorker CreateDiscoveryWorker(string dir, string python)
        {
            return new ProcessCameraDiscoveryWorker(dir, python);
        }

        public bool IsRunning { get { return IsProcessRunning; } }
        public string LastError { get; private set; } = string.Empty;
        public string LastDiscoveryError { get; private set; } = string.Empty;
        public int CameraIndex { get { return cameraIndex; } }
        public bool PreviewEnabled { get { return previewEnabled; } }

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
            if (ownsProcess)
            {
                StopTracker();
                if (ownsProcess) return false;
            }

            ResetDiagnostics();

            string error;
            if (!TryLaunchProcess(out error))
            {
                LastError = string.IsNullOrEmpty(error) ? "캠: 실행 실패" : error;
                LogFailure();
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
                        EndDiagnosticCapture();
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

        internal void StopTrackerAfterTimeout()
        {
            string detail = DiagnosticDetail();
            StopTracker();
            string stopError = LastError;
            LastError = "첫 packet 대기 시간 초과\n권한·점유·" + SetupScript + " 확인";
            if (!string.IsNullOrEmpty(stopError)) LastError += "\n" + stopError;
            if (!string.IsNullOrEmpty(detail)) LastError += "\n" + detail;
            LogFailure();
            UpdateLabel();
        }

        public void RefreshStatus()
        {
            if (ownsProcess && !IsRunning)
            {
                if (exitedAt == -1d)
                {
                    exitedAt = Time.realtimeSinceStartupAsDouble;
                    reportExitFailure = string.IsNullOrEmpty(LastError);
                }
                if (reportExitFailure) LastError = ProcessExitError();
                bool complete;
                lock (stderrLock) complete = stderrComplete;
                // stderr EOF와 process exit의 순서는 다르므로 main thread를 기다리게 하지 않는다.
                if (!complete && Time.realtimeSinceStartupAsDouble - exitedAt < 0.5d)
                {
                    UpdateLabel();
                    return;
                }
                if (reportExitFailure)
                {
                    LogFailure();
                    reportExitFailure = false;
                }
                try
                {
                    StopProcess();
                    ownsProcess = false;
                    EndDiagnosticCapture();
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
                error = "tracker 폴더 없음\n게임 옆 tracker 폴더를 복원하세요\nEditor에서는 PythonTracker 폴더가 필요합니다.";
                return false;
            }
            if (!File.Exists(Path.Combine(dir, "hand_tracker.py")))
            {
                error = "hand_tracker.py 없음\ntracker 파일을 다시 복사하세요";
                return false;
            }
            string python = ResolvePython(dir);
            if (python == null)
            {
                error = ".venv 없음\n" + SetupScript + " 실행 후 재시도";
                return false;
            }
            try
            {
                var info = new ProcessStartInfo(python, BuildArguments(cameraIndex, previewEnabled))
                {
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                proc = Process.Start(info);
                if (proc == null)
                {
                    error = "tracker 실행 실패\n" + SetupScript + " 확인 후 재시도";
                    return false;
                }
                ownsProcess = true;
                lock (stderrLock)
                {
                    stderrProcess = proc;
                    stderrComplete = false;
                }
                _ = ReadStandardErrorAsync(proc, proc.StandardError);
                return true;
            }
            catch (System.Exception e)
            {
                if (ownsProcess)
                {
                    try
                    {
                        StopProcess();
                        ownsProcess = IsRunning;
                    }
                    catch (System.Exception)
                    {
                        // 종료하지 못한 owned process는 다음 명시적 종료에서 다시 처리한다.
                    }
                }
                string detail = e.Message.Length > DiagnosticLimit ? e.Message.Substring(0, DiagnosticLimit) : e.Message;
                error = "tracker 실행 실패\n" + SetupScript + " 확인 후 재시도\n" + SanitizeDiagnostic(detail);
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
                ProcessTreeTerminator.Kill(proc);
                if (!proc.WaitForExit(3000))
                {
                    return;
                }
            }
            EndDiagnosticCapture();
            proc.StandardError.Dispose();
            proc.Dispose();
            proc = null;
        }

        public bool SetCameraIndex(int index)
        {
            if (index < 0 || IsRunning) return false;
            cameraIndex = index;
            return true;
        }

        public bool SetPreviewEnabled(bool enabled)
        {
            if (IsRunning) return false;
            previewEnabled = enabled;
            return true;
        }

        public static string BuildArguments(int index, bool preview)
        {
            return "hand_tracker.py --camera " + index + (preview ? " --preview" : " --no-preview");
        }

        public bool IsDiscovering { get { return discoveryTask != null && !discoveryTask.IsCompleted; } }

        public bool BeginDiscoverCameras()
        {
            if (IsRunning || IsDiscovering) return false;
            string dir = ResolveTrackerDir();
            string python = dir == null ? null : ResolvePython(dir);
            if (python == null)
            {
                LastDiscoveryError = "카메라 목록 조회를 시작할 수 없습니다";
                return false;
            }
            discoveryCancellation = new CancellationTokenSource();
            int generation = ++discoveryGeneration;
            CancellationToken token = discoveryCancellation.Token;
            ICameraDiscoveryWorker worker;
            try
            {
                worker = CreateDiscoveryWorker(dir, python);
            }
            catch (System.Exception exception)
            {
                discoveryCancellation.Dispose();
                discoveryCancellation = null;
                LastDiscoveryError = SanitizeDiagnostic(
                    "카메라 목록 조회를 시작할 수 없습니다\n" + exception.Message);
                return false;
            }
            generationAtTask = generation;
            LastDiscoveryError = string.Empty;
            try
            {
                discoveryTask = worker.DiscoverAsync(token);
            }
            catch (System.Exception exception)
            {
                discoveryCancellation.Dispose();
                discoveryCancellation = null;
                LastDiscoveryError = SanitizeDiagnostic(
                    "카메라 목록 조회를 시작할 수 없습니다\n" + exception.Message);
                return false;
            }
            return true;
        }

        public void CancelDiscoverCameras()
        {
            discoveryGeneration++;
            if (discoveryCancellation != null) discoveryCancellation.Cancel();
        }

        public bool TryConsumeDiscoveryResult(out int[] indices, out string error)
        {
            indices = new int[0];
            error = string.Empty;
            if (discoveryTask == null || !discoveryTask.IsCompleted) return false;
            CameraDiscoveryResult result;
            try { result = discoveryTask.Result; }
            catch (System.Exception exception)
            {
                discoveryTask = null;
                if (discoveryCancellation != null) discoveryCancellation.Dispose();
                discoveryCancellation = null;
                error = SanitizeDiagnostic("카메라 목록 조회 실패\n" + exception.Message);
                return true;
            }
            discoveryTask = null;
            if (discoveryCancellation != null) discoveryCancellation.Dispose();
            discoveryCancellation = null;
            indices = result.Indices;
            error = SanitizeDiagnostic(result.Error);
            if (result.Outcome == CameraDiscoveryOutcome.Timeout && string.IsNullOrEmpty(error))
                error = "카메라 목록 조회 시간 초과";
            else if (result.Outcome == CameraDiscoveryOutcome.Canceled && string.IsNullOrEmpty(error))
                error = "카메라 목록 조회가 취소되었습니다";
            if (generationAtTask != discoveryGeneration && result.Outcome != CameraDiscoveryOutcome.Canceled)
            {
                indices = new int[0];
                error = "카메라 목록 조회 결과가 만료되었습니다";
            }
            return true;
        }

        private int generationAtTask;

        private async Task ReadStandardErrorAsync(Process source, StreamReader reader)
        {
            var buffer = new char[512];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    CaptureStandardError(source, new string(buffer, 0, count));
            }
            catch (System.ObjectDisposedException) { }
            catch (IOException) { CaptureStandardError(source, "\nstderr 읽기 실패"); }
            finally { CaptureStandardError(source, null); }
        }

        private void CaptureStandardError(Process source, string data)
        {
            lock (stderrLock)
            {
                if (stderrProcess == null || !ReferenceEquals(source, stderrProcess)) return;
                if (data == null)
                {
                    stderrComplete = true;
                    return;
                }
                int start = System.Math.Max(0, data.Length - DiagnosticLimit);
                int retainedLength = data.Length - start;
                int excess = stderrTail.Length + retainedLength - DiagnosticLimit;
                if (excess > 0) stderrTail.Remove(0, excess);
                stderrTail.Append(data, start, retainedLength);
            }
        }

        private void ResetDiagnostics()
        {
            EndDiagnosticCapture();
            lock (stderrLock) stderrTail.Clear();
            exitedAt = -1d;
            reportExitFailure = false;
        }

        private void EndDiagnosticCapture()
        {
            lock (stderrLock)
            {
                stderrProcess = null;
                stderrComplete = true;
            }
        }

        private string DiagnosticDetail()
        {
            string detail;
            lock (stderrLock) detail = stderrTail.ToString();
            return SanitizeDiagnostic(detail).Trim();
        }

        private string ProcessExitError()
        {
            string detail = DiagnosticDetail();
            string title = proc == null ? "tracker 종료됨" : "tracker 종료 (exit code " + proc.ExitCode + ")";
            string summary = "권한·점유·" + SetupScript + " 확인";
            if (!string.IsNullOrEmpty(detail))
            {
                int lastLine = detail.LastIndexOf('\n');
                summary = detail.Substring(lastLine + 1);
                if (summary.Length > 64) summary = summary.Substring(0, 63) + "…";
            }
            return title + "\n" + summary + "\n권한과 다른 camera 사용 프로그램을 확인하세요.\n" +
                SetupScript + " 실행 후 재시도하세요.\n" + detail;
        }

        internal static string SanitizeDiagnostic(string detail)
        {
            string clean = Regex.Replace(detail ?? string.Empty, @"[\x00-\x08\x0B-\x1F\x7F]", string.Empty);
            clean = Regex.Replace(clean, @"(?i)(\b(?:api[_-]?key|access[_-]?token|token|password|passwd|secret|authorization)\b\s*[:=]\s*)[^\r\n]+", "$1[redacted]");
            clean = Regex.Replace(clean, @"(?i)\bBearer\s+[^\s]+", "Bearer [redacted]");
            if (clean.Length > DiagnosticLimit)
            {
                int headLength = DiagnosticLimit / 2;
                int tailLength = DiagnosticLimit - headLength - 1;
                clean = clean.Substring(0, headLength) + "…" +
                    clean.Substring(clean.Length - tailLength, tailLength);
            }
            return clean;
        }

        private void LogFailure()
        {
            LastError = SanitizeDiagnostic(LastError);
            if (LastError.Length > 2560) LastError = LastError.Substring(0, 2559) + "…";
            Debug.LogWarning("[TrackerLauncher] " + LastError, this);
        }

        private static string SetupScript
        {
            get
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                return "setup_tracker.bat";
#else
                return "setup_tracker.sh";
#endif
            }
        }

        // tracker는 자식 프로세스를 하나 더 띄운다 (2026-08-27 실측). 부모만 죽이면 자식이 캠을 계속 잡는다.
        // dataPath에서 위로 훑어 tracker/(배포) 또는 PythonTracker/(개발)를 찾는다.
        // 깊이가 OS마다 다르기 때문이다 — Windows 빌드는 1단계(<exe>/CameraCoop_Data),
        // Editor도 1단계(<root>/Assets), macOS 빌드는 4단계(<app>/Contents/Resources/Data).
        protected virtual string ResolveTrackerDir()
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
        protected virtual string ResolvePython(string dir)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string win = Path.Combine(dir, ".venv", "Scripts", "python.exe");
            return File.Exists(win) ? win : null;
#else
            string nix = Path.Combine(dir, ".venv", "bin", "python");
            return File.Exists(nix) ? nix : null;
#endif
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
            CancelDiscoverCameras();
            _ = DrainDiscoveryProcessesAsync();
            StopTracker();
        }

        private void OnApplicationQuit()
        {
            CancelDiscoverCameras();
            _ = DrainDiscoveryProcessesAsync();
            StopTracker();
        }

        private async Task DrainDiscoveryProcessesAsync()
        {
            Task<CameraDiscoveryResult> activeDiscovery = discoveryTask;
            if (activeDiscovery != null)
            {
                try { await activeDiscovery.ConfigureAwait(false); }
                catch (System.Exception) { }
            }

            try { await CameraDiscoveryProcessReaper.Shared.DrainAsync().ConfigureAwait(false); }
            catch (System.Exception) { }
        }
    }
}
