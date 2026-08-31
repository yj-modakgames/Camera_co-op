using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CameraCoop.Tests
{
    public class CameraDiscoveryTests
    {
        [Test]
        public void Catalog_ParsesAvailableIndicesAndSortsThem()
        {
            int[] indices;
            string error;

            bool found = CameraDeviceCatalog.TryParseIndices(
                "[{\"index\":3,\"available\":true},{\"index\":1,\"available\":false},{\"index\":0,\"available\":true}]",
                out indices, out error);

            Assert.IsTrue(found);
            CollectionAssert.AreEqual(new[] { 0, 3 }, indices);
            Assert.IsEmpty(error);
        }

        [Test]
        public void Catalog_RemovesDuplicateAvailableIndices()
        {
            int[] indices;
            string error;

            bool found = CameraDeviceCatalog.TryParseIndices(
                "[{\"index\":2,\"available\":true},{\"index\":2,\"available\":true}]",
                out indices, out error);

            Assert.IsTrue(found);
            CollectionAssert.AreEqual(new[] { 2 }, indices);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[{\"index\":4,\"available\":false}]")]
        public void Catalog_ReportsMalformedOrEmptyDeviceList(string json)
        {
            int[] indices;
            string error;

            bool found = CameraDeviceCatalog.TryParseIndices(json, out indices, out error);

            Assert.IsFalse(found);
            Assert.IsEmpty(indices);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void Catalog_CyclesOnlyThroughDiscoveredIndices()
        {
            int[] indices = { 0, 3, 7 };

            Assert.AreEqual(3, CameraDeviceCatalog.NextIndex(indices, 0, 1));
            Assert.AreEqual(7, CameraDeviceCatalog.NextIndex(indices, 0, -1));
            Assert.AreEqual(0, CameraDeviceCatalog.NextIndex(indices, 7, 1));
            Assert.AreEqual(-1, CameraDeviceCatalog.NextIndex(indices, 0, 0));
        }

        [Test]
        public void Launcher_BuildArgumentsPreservesExplicitPreviewChoice()
        {
            Assert.AreEqual("hand_tracker.py --camera 3 --preview", TrackerLauncher.BuildArguments(3, true));
            Assert.AreEqual("hand_tracker.py --camera 3 --no-preview", TrackerLauncher.BuildArguments(3, false));
        }

        [Test]
        public void Launcher_BeginDiscoveryReturnsPromptlyAndConsumesSuccess()
        {
            var rig = new GameObject("camera discovery probe");
            try
            {
                var launcher = rig.AddComponent<CameraDiscoveryProbe>();
                launcher.ResultTask = Task.FromResult(new CameraDiscoveryResult(new[] { 0, 2 }, "", CameraDiscoveryOutcome.Success));

                Assert.IsTrue(launcher.BeginDiscoverCameras());
                int[] indices; string error = string.Empty;
                Assert.IsTrue(launcher.TryConsumeDiscoveryResult(out indices, out error));
                CollectionAssert.AreEqual(new[] { 0, 2 }, indices);
                Assert.IsEmpty(error);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }

        [Test]
        public void Launcher_ConsumesTypedErrorAndDiagnostics()
        {
            var rig = new GameObject("camera discovery probe");
            try
            {
                var launcher = rig.AddComponent<CameraDiscoveryProbe>();
                launcher.ResultTask = Task.FromResult(new CameraDiscoveryResult(new int[0], "stderr: camera busy", CameraDiscoveryOutcome.Error));
                Assert.IsTrue(launcher.BeginDiscoverCameras());
                int[] indices; string error;
                Assert.IsTrue(launcher.TryConsumeDiscoveryResult(out indices, out error));
                Assert.IsEmpty(indices);
                StringAssert.Contains("camera busy", error);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }

        [Test]
        public void Launcher_ConsumptionSanitizesSensitiveControlAndOversizeDiagnostics()
        {
            var rig = new GameObject("camera discovery probe");
            try
            {
                var launcher = rig.AddComponent<CameraDiscoveryProbe>();
                string oversizedPath = new string('p', 4096);
                launcher.ResultTask = Task.FromResult(new CameraDiscoveryResult(
                    new int[0],
                    "camera busy\0\ntoken=private-test-value\npath=" + oversizedPath,
                    CameraDiscoveryOutcome.Error));

                Assert.IsTrue(launcher.BeginDiscoverCameras());
                int[] indices; string error;
                Assert.IsTrue(launcher.TryConsumeDiscoveryResult(out indices, out error));

                StringAssert.Contains("camera busy", error);
                StringAssert.DoesNotContain("private-test-value", error);
                StringAssert.DoesNotContain("\0", error);
                Assert.LessOrEqual(error.Length, 2048);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }

        [Test]
        public void Launcher_LastDiscoveryErrorSanitizesWorkerCreationFailure()
        {
            var rig = new GameObject("camera discovery throwing probe");
            try
            {
                var launcher = rig.AddComponent<ThrowingWorkerLauncher>();
                Assert.IsFalse(launcher.BeginDiscoverCameras());

                StringAssert.Contains("camera discovery backend", launcher.LastDiscoveryError);
                StringAssert.DoesNotContain("private-test-value", launcher.LastDiscoveryError);
                StringAssert.DoesNotContain("\0", launcher.LastDiscoveryError);
                Assert.LessOrEqual(launcher.LastDiscoveryError.Length, 2048);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }

        [TestCase(CameraDiscoveryOutcome.Timeout, "카메라 목록 조회 시간 초과")]
        [TestCase(CameraDiscoveryOutcome.Canceled, "카메라 목록 조회가 취소되었습니다")]
        public void Launcher_ConsumesTimeoutAndCanceledOutcomes(CameraDiscoveryOutcome outcome, string expected)
        {
            var rig = new GameObject("camera discovery probe");
            try
            {
                var launcher = rig.AddComponent<CameraDiscoveryProbe>();
                launcher.ResultTask = Task.FromResult(new CameraDiscoveryResult(new int[0], "", outcome));
                Assert.IsTrue(launcher.BeginDiscoverCameras());
                if (outcome == CameraDiscoveryOutcome.Canceled) launcher.CancelDiscoverCameras();
                int[] indices; string error;
                Assert.IsTrue(launcher.TryConsumeDiscoveryResult(out indices, out error));
                StringAssert.Contains(expected, error);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }

        [Test]
        public void Launcher_StaleSuccessIsDiscardedAfterCancel()
        {
            var rig = new GameObject("camera discovery probe");
            try
            {
                var launcher = rig.AddComponent<CameraDiscoveryProbe>();
                launcher.ResultTask = Task.FromResult(new CameraDiscoveryResult(new[] { 4 }, "", CameraDiscoveryOutcome.Success));
                Assert.IsTrue(launcher.BeginDiscoverCameras());
                launcher.CancelDiscoverCameras();
                int[] indices; string error;
                Assert.IsTrue(launcher.TryConsumeDiscoveryResult(out indices, out error));
                Assert.IsEmpty(indices);
                StringAssert.Contains("만료", error);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }

        [Test]
        public void Launcher_FaultedDiscoveryTaskIsConsumedAsError()
        {
            var rig = new GameObject("camera discovery probe");
            try
            {
                var launcher = rig.AddComponent<CameraDiscoveryProbe>();
                launcher.ResultTask = Task.FromException<CameraDiscoveryResult>(new InvalidOperationException("worker fault"));
                Assert.IsTrue(launcher.BeginDiscoverCameras());
                int[] indices; string error = string.Empty;
                Assert.DoesNotThrow(() => Assert.IsTrue(launcher.TryConsumeDiscoveryResult(out indices, out error)));
                StringAssert.Contains("worker fault", error);
            }
            finally { UnityEngine.Object.DestroyImmediate(rig); }
        }

        [Test]
        public void ProcessRunner_ObservesBothReadersBeforeDisposeOnSuccess()
        {
            var factory = new FakeCameraDiscoveryProcessFactory();
            factory.Process.OutputResult = CameraDiscoveryReadResult.Completed("[{\"index\":2,\"available\":true}]");
            factory.Process.WaitResults.Enqueue(true);
            var reaper = new FakeCameraDiscoveryProcessReaper();
            var runner = new CameraDiscoveryProcessRunner(factory, reaper, 1, 1, 1);

            CameraDiscoveryResult result = runner.Run("test-dir", "test-python", CancellationToken.None);

            Assert.AreEqual(CameraDiscoveryOutcome.Success, result.Outcome);
            CollectionAssert.AreEqual(new[] { 2 }, result.Indices);
            CollectionAssert.DoesNotContain(factory.Process.Events, "kill");
            Assert.AreEqual(0, reaper.OwnershipTransfers);
            AssertLifecycleOrder(factory.Process, closeReaders: false);
        }

        [Test]
        public void ProcessRunner_ObservesBothReadersBeforeDisposeOnTimeout()
        {
            var factory = new FakeCameraDiscoveryProcessFactory();
            factory.Process.WaitResults.Enqueue(false);
            factory.Process.WaitResults.Enqueue(true);
            var reaper = new FakeCameraDiscoveryProcessReaper();
            var runner = new CameraDiscoveryProcessRunner(factory, reaper, 1, 1, 1);

            CameraDiscoveryResult result = runner.Run("test-dir", "test-python", CancellationToken.None);

            Assert.AreEqual(CameraDiscoveryOutcome.Timeout, result.Outcome);
            CollectionAssert.Contains(factory.Process.Events, "kill");
            Assert.AreEqual(0, reaper.OwnershipTransfers);
            AssertLifecycleOrder(factory.Process, closeReaders: false);
        }

        [Test]
        public void ProcessRunner_ObservesBothReadersBeforeDisposeOnCancel()
        {
            var factory = new FakeCameraDiscoveryProcessFactory();
            factory.Process.WaitResults.Enqueue(true);
            var reaper = new FakeCameraDiscoveryProcessReaper();
            var runner = new CameraDiscoveryProcessRunner(factory, reaper, 1, 1, 1);
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            CameraDiscoveryResult result = runner.Run("test-dir", "test-python", cancellation.Token);

            Assert.AreEqual(CameraDiscoveryOutcome.Canceled, result.Outcome);
            CollectionAssert.Contains(factory.Process.Events, "kill");
            Assert.AreEqual(0, reaper.OwnershipTransfers);
            AssertLifecycleOrder(factory.Process, closeReaders: false);
        }

        [Test]
        public void ProcessRunner_TransfersOwnershipAfterClosingAndObservingReadersWhenKillIsNotConfirmed()
        {
            var factory = new FakeCameraDiscoveryProcessFactory();
            factory.Process.WaitResults.Enqueue(false);
            factory.Process.WaitResults.Enqueue(false);
            factory.Process.WaitResults.Enqueue(true);
            factory.Process.OutputResult = CameraDiscoveryReadResult.Faulted(new InvalidOperationException("stdout fault"));
            factory.Process.ErrorResult = CameraDiscoveryReadResult.Faulted(new InvalidOperationException("stderr fault"));
            var reaper = new FakeCameraDiscoveryProcessReaper();
            var runner = new CameraDiscoveryProcessRunner(factory, reaper, 1, 1, 1);

            CameraDiscoveryResult result = null;
            Assert.DoesNotThrow(() => result = runner.Run("test-dir", "test-python", CancellationToken.None));

            Assert.AreEqual(CameraDiscoveryOutcome.Error, result.Outcome);
            StringAssert.Contains("종료를 확인하지 못했습니다", result.Error);
            StringAssert.Contains("stdout fault", result.Error);
            StringAssert.Contains("stderr fault", result.Error);
            CollectionAssert.Contains(factory.Process.Events, "kill");
            Assert.AreSame(factory.Process, reaper.OwnedProcess);
            Assert.AreEqual(1, reaper.OwnershipTransfers);
            Assert.AreEqual(0, factory.Process.DisposeCalls);
            AssertReadersClosedAndObservedBeforeOwnershipTransfer(factory.Process);

            reaper.ConfirmExitAndDispose();

            Assert.IsNull(reaper.OwnedProcess);
            Assert.AreEqual(1, factory.Process.DisposeCalls);
            Assert.Greater(
                factory.Process.Events.IndexOf("dispose"),
                factory.Process.Events.IndexOf("reaper-own"));
        }

        [Test]
        public void ProcessRunner_ExceptionDiagnosticsAreSanitizedAndCapped()
        {
            string oversizedPath = new string('p', 4096);
            var factory = new ThrowingCameraDiscoveryProcessFactory(
                new InvalidOperationException("camera backend\0\ntoken=private-test-value\npath=" + oversizedPath));
            var runner = new CameraDiscoveryProcessRunner(
                factory,
                new FakeCameraDiscoveryProcessReaper(),
                1,
                1,
                1);

            CameraDiscoveryResult result = runner.Run("test-dir", "test-python", CancellationToken.None);

            Assert.AreEqual(CameraDiscoveryOutcome.Error, result.Outcome);
            StringAssert.Contains("camera backend", result.Error);
            StringAssert.DoesNotContain("private-test-value", result.Error);
            StringAssert.DoesNotContain("\0", result.Error);
            Assert.LessOrEqual(result.Error.Length, 2048);
        }

        private sealed class ThrowingCameraDiscoveryProcessFactory : ICameraDiscoveryProcessFactory
        {
            private readonly Exception error;

            public ThrowingCameraDiscoveryProcessFactory(Exception error)
            {
                this.error = error;
            }

            public ICameraDiscoveryProcess Start(System.Diagnostics.ProcessStartInfo startInfo)
            {
                throw error;
            }
        }

        private sealed class ThrowingWorkerLauncher : TrackerLauncher
        {
            protected override string ResolveTrackerDir() { return "test-tracker"; }
            protected override string ResolvePython(string dir) { return "test-python"; }

            protected override ICameraDiscoveryWorker CreateDiscoveryWorker(string dir, string python)
            {
                throw new InvalidOperationException(
                    "camera discovery backend\0\ntoken=private-test-value\npath=" + new string('p', 4096));
            }
        }

        [Test]
        public void ProcessReaper_RetainsOwnershipAcrossKillFaultUntilExitAndDisposesOnce()
        {
            var process = new FakeCameraDiscoveryProcess();
            process.KillFailuresRemaining = 1;
            process.WaitResults.Enqueue(false);
            process.WaitResults.Enqueue(true);
            var reaper = new CameraDiscoveryProcessReaper(1, 0);

            reaper.TakeOwnership(process);
            Assert.DoesNotThrow(() => reaper.DrainAsync().GetAwaiter().GetResult());

            Assert.AreEqual(2, process.KillCalls);
            Assert.AreEqual(1, process.DisposeCalls);
            Assert.AreEqual(0, reaper.PendingCount);
            StringAssert.Contains("kill fault", reaper.LastError.Message);
        }

        private static void AssertLifecycleOrder(FakeCameraDiscoveryProcess process, bool closeReaders)
        {
            int firstWait = process.Events.IndexOf("wait");
            Assert.Greater(firstWait, process.Events.IndexOf("stdout-start"));
            Assert.Greater(firstWait, process.Events.IndexOf("stderr-start"));

            int dispose = process.Events.IndexOf("dispose");
            Assert.Greater(dispose, process.Events.LastIndexOf("stdout-observe"));
            Assert.Greater(dispose, process.Events.LastIndexOf("stderr-observe"));
            Assert.AreEqual(1, process.Events.FindAll(item => item == "stdout-observe").Count);
            Assert.AreEqual(1, process.Events.FindAll(item => item == "stderr-observe").Count);
            Assert.AreEqual(1, process.DisposeCalls);

            if (!closeReaders)
            {
                CollectionAssert.DoesNotContain(process.Events, "stdout-close");
                CollectionAssert.DoesNotContain(process.Events, "stderr-close");
                return;
            }
            Assert.Greater(process.Events.IndexOf("stdout-observe"), process.Events.IndexOf("stdout-close"));
            Assert.Greater(process.Events.IndexOf("stderr-observe"), process.Events.IndexOf("stderr-close"));
        }

        private static void AssertReadersClosedAndObservedBeforeOwnershipTransfer(
            FakeCameraDiscoveryProcess process)
        {
            int transfer = process.Events.IndexOf("reaper-own");
            Assert.Greater(transfer, process.Events.IndexOf("stdout-close"));
            Assert.Greater(transfer, process.Events.IndexOf("stderr-close"));
            Assert.Greater(transfer, process.Events.IndexOf("stdout-observe"));
            Assert.Greater(transfer, process.Events.IndexOf("stderr-observe"));
            Assert.AreEqual(1, process.Events.FindAll(item => item == "stdout-observe").Count);
            Assert.AreEqual(1, process.Events.FindAll(item => item == "stderr-observe").Count);
        }
    }
}
