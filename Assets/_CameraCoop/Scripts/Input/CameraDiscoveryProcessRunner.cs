using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CameraCoop
{
    public sealed class CameraDiscoveryReadResult
    {
        public readonly string Content;
        public readonly Exception Error;

        private CameraDiscoveryReadResult(string content, Exception error)
        {
            Content = content ?? string.Empty;
            Error = error;
        }

        public static CameraDiscoveryReadResult Completed(string content)
        {
            return new CameraDiscoveryReadResult(content, null);
        }

        public static CameraDiscoveryReadResult Faulted(Exception error)
        {
            return new CameraDiscoveryReadResult(string.Empty, error ?? new IOException("reader fault"));
        }
    }

    public interface ICameraDiscoveryReader
    {
        void Close();
        CameraDiscoveryReadResult Observe();
    }

    public interface ICameraDiscoveryProcess : IDisposable
    {
        ICameraDiscoveryReader StartStandardOutputRead();
        ICameraDiscoveryReader StartStandardErrorRead();
        bool WaitForExit(int milliseconds);
        void KillTree();
    }

    public interface ICameraDiscoveryProcessFactory
    {
        ICameraDiscoveryProcess Start(ProcessStartInfo startInfo);
    }

    public interface ICameraDiscoveryProcessReaper
    {
        void TakeOwnership(ICameraDiscoveryProcess process);
        Task DrainAsync();
    }

    public sealed class CameraDiscoveryProcessRunner
    {
        private readonly ICameraDiscoveryProcessFactory processFactory;
        private readonly ICameraDiscoveryProcessReaper processReaper;
        private readonly int normalWaitAttempts;
        private readonly int waitSliceMilliseconds;
        private readonly int killWaitMilliseconds;

        public CameraDiscoveryProcessRunner()
            : this(
                new SystemCameraDiscoveryProcessFactory(),
                CameraDiscoveryProcessReaper.Shared,
                100,
                50,
                1000)
        {
        }

        public CameraDiscoveryProcessRunner(
            ICameraDiscoveryProcessFactory processFactory,
            ICameraDiscoveryProcessReaper processReaper,
            int normalWaitAttempts,
            int waitSliceMilliseconds,
            int killWaitMilliseconds)
        {
            if (processFactory == null) throw new ArgumentNullException(nameof(processFactory));
            if (processReaper == null) throw new ArgumentNullException(nameof(processReaper));
            if (normalWaitAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(normalWaitAttempts));
            if (waitSliceMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(waitSliceMilliseconds));
            if (killWaitMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(killWaitMilliseconds));
            this.processFactory = processFactory;
            this.processReaper = processReaper;
            this.normalWaitAttempts = normalWaitAttempts;
            this.waitSliceMilliseconds = waitSliceMilliseconds;
            this.killWaitMilliseconds = killWaitMilliseconds;
        }

        public CameraDiscoveryResult Run(string workingDirectory, string python, CancellationToken token)
        {
            ICameraDiscoveryProcess process = null;
            ICameraDiscoveryReader stdout = null;
            ICameraDiscoveryReader stderr = null;
            bool readersObserved = false;
            try
            {
                process = processFactory.Start(CreateStartInfo(workingDirectory, python));
                if (process == null)
                    return Error("카메라 목록 프로세스를 시작하지 못했습니다");

                stdout = process.StartStandardOutputRead();
                stderr = process.StartStandardErrorRead();

                bool exited = false;
                for (int i = 0; i < normalWaitAttempts && !token.IsCancellationRequested; i++)
                {
                    if (!process.WaitForExit(waitSliceMilliseconds)) continue;
                    exited = true;
                    break;
                }

                if (exited)
                {
                    ReaderPair readers = ObserveReaders(stdout, stderr);
                    readersObserved = true;
                    if (readers.HasFault)
                        return Error("카메라 목록 출력 읽기 실패" + readers.FaultDiagnostics());

                    int[] indices;
                    string error;
                    bool success = CameraDeviceCatalog.TryParseIndices(readers.Output.Content, out indices, out error);
                    return new CameraDiscoveryResult(
                        indices,
                        error,
                        success ? CameraDiscoveryOutcome.Success : CameraDiscoveryOutcome.Error);
                }

                bool canceled = token.IsCancellationRequested;
                Exception terminationError = null;
                bool killConfirmed = false;
                try
                {
                    process.KillTree();
                    killConfirmed = process.WaitForExit(killWaitMilliseconds);
                }
                catch (Exception exception)
                {
                    terminationError = exception;
                }

                if (!killConfirmed)
                {
                    CloseReader(stdout);
                    CloseReader(stderr);
                    ReaderPair readers = ObserveReaders(stdout, stderr);
                    readersObserved = true;
                    processReaper.TakeOwnership(process);
                    process = null;
                    return Error(
                        "카메라 목록 프로세스 종료를 확인하지 못했습니다" +
                        ExceptionDiagnostic(terminationError) +
                        readers.FaultDiagnostics());
                }

                ReaderPair completedReaders = ObserveReaders(stdout, stderr);
                readersObserved = true;
                string message = canceled
                    ? "카메라 목록 조회가 취소되었습니다"
                    : "카메라 목록 조회 시간 초과";
                return new CameraDiscoveryResult(
                    new int[0],
                    message + completedReaders.FaultDiagnostics(),
                    canceled ? CameraDiscoveryOutcome.Canceled : CameraDiscoveryOutcome.Timeout);
            }
            catch (Exception exception)
            {
                if (!readersObserved)
                {
                    CloseReader(stdout);
                    CloseReader(stderr);
                    ReaderPair readers = ObserveReaders(stdout, stderr);
                    readersObserved = true;
                    return Error(
                        "카메라 목록 조회 실패" +
                        ExceptionDiagnostic(exception) +
                        readers.FaultDiagnostics());
                }
                return Error("카메라 목록 조회 실패" + ExceptionDiagnostic(exception));
            }
            finally
            {
                if (process != null)
                {
                    try { process.Dispose(); }
                    catch (Exception) { }
                }
            }
        }

        private static ProcessStartInfo CreateStartInfo(string workingDirectory, string python)
        {
            return new ProcessStartInfo(python, "hand_tracker.py --list-cameras")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
        }

        private static ReaderPair ObserveReaders(ICameraDiscoveryReader stdout, ICameraDiscoveryReader stderr)
        {
            return new ReaderPair(ObserveReader(stdout), ObserveReader(stderr));
        }

        private static CameraDiscoveryReadResult ObserveReader(ICameraDiscoveryReader reader)
        {
            if (reader == null) return CameraDiscoveryReadResult.Completed(string.Empty);
            try
            {
                return reader.Observe() ?? CameraDiscoveryReadResult.Faulted(
                    new InvalidOperationException("reader observation returned no result"));
            }
            catch (Exception exception)
            {
                return CameraDiscoveryReadResult.Faulted(exception);
            }
        }

        private static void CloseReader(ICameraDiscoveryReader reader)
        {
            if (reader == null) return;
            try { reader.Close(); }
            catch (Exception) { }
        }

        private static string ExceptionDiagnostic(Exception exception)
        {
            return exception == null
                ? string.Empty
                : "\n" + TrackerLauncher.SanitizeDiagnostic(exception.Message);
        }

        private static CameraDiscoveryResult Error(string message)
        {
            return new CameraDiscoveryResult(
                new int[0],
                TrackerLauncher.SanitizeDiagnostic(message),
                CameraDiscoveryOutcome.Error);
        }

        private sealed class ReaderPair
        {
            public readonly CameraDiscoveryReadResult Output;
            public readonly CameraDiscoveryReadResult Error;

            public ReaderPair(CameraDiscoveryReadResult output, CameraDiscoveryReadResult error)
            {
                Output = output;
                Error = error;
            }

            public bool HasFault { get { return Output.Error != null || Error.Error != null; } }

            public string FaultDiagnostics()
            {
                return ExceptionDiagnostic(Output.Error) + ExceptionDiagnostic(Error.Error);
            }
        }
    }

    public sealed class CameraDiscoveryProcessReaper : ICameraDiscoveryProcessReaper
    {
        private readonly object gate = new object();
        private readonly Dictionary<long, TrackedProcess> pending =
            new Dictionary<long, TrackedProcess>();
        private readonly int waitMilliseconds;
        private readonly int errorBackoffMilliseconds;
        private long nextId;
        private Exception lastError;

        public static CameraDiscoveryProcessReaper Shared { get; } =
            new CameraDiscoveryProcessReaper(1000, 100);

        public CameraDiscoveryProcessReaper(int waitMilliseconds, int errorBackoffMilliseconds)
        {
            if (waitMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
            if (errorBackoffMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(errorBackoffMilliseconds));
            this.waitMilliseconds = waitMilliseconds;
            this.errorBackoffMilliseconds = errorBackoffMilliseconds;
        }

        public int PendingCount
        {
            get { lock (gate) return pending.Count; }
        }

        public Exception LastError
        {
            get { lock (gate) return lastError; }
        }

        public void TakeOwnership(ICameraDiscoveryProcess process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            lock (gate)
            {
                long id = ++nextId;
                var tracked = new TrackedProcess(id, process);
                pending.Add(id, tracked);
                tracked.Task = Task.Run(() => Reap(tracked));
            }
        }

        public Task DrainAsync()
        {
            lock (gate)
            {
                if (pending.Count == 0) return Task.CompletedTask;
                var tasks = new Task[pending.Count];
                int index = 0;
                foreach (TrackedProcess tracked in pending.Values)
                    tasks[index++] = tracked.Task;
                return Task.WhenAll(tasks);
            }
        }

        private void Reap(TrackedProcess tracked)
        {
            try
            {
                bool exited = false;
                while (!exited)
                {
                    try { tracked.Process.KillTree(); }
                    catch (Exception exception) { Observe(exception); }

                    try { exited = tracked.Process.WaitForExit(waitMilliseconds); }
                    catch (Exception exception) { Observe(exception); }

                    if (!exited && errorBackoffMilliseconds > 0)
                        Thread.Sleep(errorBackoffMilliseconds);
                }
            }
            catch (Exception exception)
            {
                Observe(exception);
            }
            finally
            {
                try { tracked.Process.Dispose(); }
                catch (Exception exception) { Observe(exception); }
                lock (gate) pending.Remove(tracked.Id);
            }
        }

        private void Observe(Exception exception)
        {
            lock (gate) lastError = exception;
        }

        private sealed class TrackedProcess
        {
            public readonly long Id;
            public readonly ICameraDiscoveryProcess Process;
            public Task Task;

            public TrackedProcess(long id, ICameraDiscoveryProcess process)
            {
                Id = id;
                Process = process;
            }
        }
    }

    internal sealed class ProcessCameraDiscoveryWorker : ICameraDiscoveryWorker
    {
        private readonly string workingDirectory;
        private readonly string python;
        private readonly CameraDiscoveryProcessRunner runner;

        public ProcessCameraDiscoveryWorker(string workingDirectory, string python)
        {
            this.workingDirectory = workingDirectory;
            this.python = python;
            runner = new CameraDiscoveryProcessRunner();
        }

        public Task<CameraDiscoveryResult> DiscoverAsync(CancellationToken token)
        {
            return Task.Run(() => runner.Run(workingDirectory, python, token));
        }
    }

    internal sealed class SystemCameraDiscoveryProcessFactory : ICameraDiscoveryProcessFactory
    {
        public ICameraDiscoveryProcess Start(ProcessStartInfo startInfo)
        {
            Process process = Process.Start(startInfo);
            return process == null ? null : new SystemCameraDiscoveryProcess(process);
        }
    }

    internal sealed class SystemCameraDiscoveryProcess : ICameraDiscoveryProcess
    {
        private readonly Process process;

        public SystemCameraDiscoveryProcess(Process process)
        {
            this.process = process;
        }

        public ICameraDiscoveryReader StartStandardOutputRead()
        {
            return new SystemCameraDiscoveryReader(process.StandardOutput);
        }

        public ICameraDiscoveryReader StartStandardErrorRead()
        {
            return new SystemCameraDiscoveryReader(process.StandardError);
        }

        public bool WaitForExit(int milliseconds) { return process.WaitForExit(milliseconds); }
        public void KillTree() { ProcessTreeTerminator.Kill(process); }
        public void Dispose() { process.Dispose(); }
    }

    internal sealed class SystemCameraDiscoveryReader : ICameraDiscoveryReader
    {
        private readonly StreamReader reader;
        private readonly Task<string> completion;

        public SystemCameraDiscoveryReader(StreamReader reader)
        {
            this.reader = reader;
            completion = reader.ReadToEndAsync();
        }

        public void Close() { reader.Close(); }

        public CameraDiscoveryReadResult Observe()
        {
            try { return CameraDiscoveryReadResult.Completed(completion.GetAwaiter().GetResult()); }
            catch (Exception exception) { return CameraDiscoveryReadResult.Faulted(exception); }
        }
    }

    internal static class ProcessTreeTerminator
    {
        public static void Kill(Process target)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            RunSilent("taskkill", "/PID " + target.Id + " /T /F");
#else
            RunSilent("/usr/bin/pkill", "-P " + target.Id);
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
            using (Process process = Process.Start(info))
            {
                if (process == null)
                    throw new InvalidOperationException(file + " 프로세스를 시작하지 못했습니다.");
                if (!process.WaitForExit(3000))
                    throw new TimeoutException(file + " 프로세스 종료 대기 시간이 초과되었습니다.");
            }
        }
    }
}
