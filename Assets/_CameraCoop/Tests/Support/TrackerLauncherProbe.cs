using System;

namespace CameraCoop.Tests
{
    public sealed class TrackerLauncherProbe : TrackerLauncher
    {
        public bool running;
        public bool failStart;
        public string startError = "캠: 실행 실패";
        public int startCalls;
        public int stopCalls;
        public bool failStop;
        public bool throwOnStop;

        protected override bool IsProcessRunning => running;

        protected override bool TryLaunchProcess(out string error)
        {
            startCalls++;
            error = failStart ? startError : string.Empty;
            running = !failStart;
            return !failStart;
        }

        protected override void StopProcess()
        {
            stopCalls++;
            if (throwOnStop)
            {
                throw new InvalidOperationException("Simulated tracker stop failure.");
            }
            if (!failStop)
            {
                running = false;
            }
        }
    }
}
