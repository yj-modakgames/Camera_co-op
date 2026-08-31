using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace CameraCoop.Tests
{
    public sealed class CameraDiscoveryProbe : TrackerLauncher
    {
        public Task<CameraDiscoveryResult> ResultTask =
            Task.FromResult(new CameraDiscoveryResult(new int[0], string.Empty, CameraDiscoveryOutcome.Success));
        public int BeginWorkerCalls;

        protected override string ResolveTrackerDir() { return "test-tracker"; }
        protected override string ResolvePython(string dir) { return "test-python"; }

        protected override ICameraDiscoveryWorker CreateDiscoveryWorker(string dir, string python)
        {
            BeginWorkerCalls++;
            return new ProbeWorker(ResultTask);
        }

        private sealed class ProbeWorker : ICameraDiscoveryWorker
        {
            private readonly Task<CameraDiscoveryResult> result;
            public ProbeWorker(Task<CameraDiscoveryResult> result) { this.result = result; }
            public Task<CameraDiscoveryResult> DiscoverAsync(CancellationToken token) { return result; }
        }
    }

    public sealed class FakeCameraDiscoveryProcessFactory : ICameraDiscoveryProcessFactory
    {
        public readonly FakeCameraDiscoveryProcess Process = new FakeCameraDiscoveryProcess();

        public ICameraDiscoveryProcess Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
            Process.Events.Add("process-start");
            return Process;
        }
    }

    public sealed class FakeCameraDiscoveryProcess : ICameraDiscoveryProcess
    {
        public readonly List<string> Events = new List<string>();
        public readonly Queue<bool> WaitResults = new Queue<bool>();
        public CameraDiscoveryReadResult OutputResult = CameraDiscoveryReadResult.Completed("[]");
        public CameraDiscoveryReadResult ErrorResult = CameraDiscoveryReadResult.Completed(string.Empty);
        public int KillFailuresRemaining;
        public int KillCalls;
        public int DisposeCalls;

        public ICameraDiscoveryReader StartStandardOutputRead()
        {
            Events.Add("stdout-start");
            return new FakeCameraDiscoveryReader("stdout", Events, () => OutputResult);
        }

        public ICameraDiscoveryReader StartStandardErrorRead()
        {
            Events.Add("stderr-start");
            return new FakeCameraDiscoveryReader("stderr", Events, () => ErrorResult);
        }

        public bool WaitForExit(int milliseconds)
        {
            Events.Add("wait");
            return WaitResults.Count > 0 && WaitResults.Dequeue();
        }

        public void KillTree()
        {
            KillCalls++;
            Events.Add("kill");
            if (KillFailuresRemaining <= 0) return;
            KillFailuresRemaining--;
            throw new System.InvalidOperationException("kill fault");
        }

        public void Dispose()
        {
            DisposeCalls++;
            Events.Add("dispose");
        }
    }

    public sealed class FakeCameraDiscoveryProcessReaper : ICameraDiscoveryProcessReaper
    {
        public ICameraDiscoveryProcess OwnedProcess;
        public int OwnershipTransfers;

        public void TakeOwnership(ICameraDiscoveryProcess process)
        {
            OwnedProcess = process;
            OwnershipTransfers++;
            ((FakeCameraDiscoveryProcess)process).Events.Add("reaper-own");
        }

        public Task DrainAsync() { return Task.CompletedTask; }

        public void ConfirmExitAndDispose()
        {
            OwnedProcess.KillTree();
            if (!OwnedProcess.WaitForExit(1))
                throw new System.InvalidOperationException("fake process did not exit");
            OwnedProcess.Dispose();
            OwnedProcess = null;
        }
    }

    public sealed class FakeCameraDiscoveryReader : ICameraDiscoveryReader
    {
        private readonly string name;
        private readonly List<string> events;
        private readonly System.Func<CameraDiscoveryReadResult> result;

        public FakeCameraDiscoveryReader(
            string name,
            List<string> events,
            System.Func<CameraDiscoveryReadResult> result)
        {
            this.name = name;
            this.events = events;
            this.result = result;
        }

        public void Close() { events.Add(name + "-close"); }

        public CameraDiscoveryReadResult Observe()
        {
            events.Add(name + "-observe");
            return result();
        }
    }
}
