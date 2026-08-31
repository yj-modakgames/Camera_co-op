using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CameraCoop.Tests
{
    public class CameraControlTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<GameObject> roots = new List<GameObject>();
        private MonoBehaviour panel;
        private TrackerLauncher launcher;
        private InputModeManager modes;
        private UdpHandReceiver receiver;
        private HandInputRouter router;
        private Button button;
        private Text buttonLabel;
        private Text statusLabel;
        private Vector2 inside;
        private Vector2 outside;

        [SetUp]
        public void SetUp()
        {
            InputFocus.IsTyping = false;
            Type panelType = typeof(TrackerLauncher).Assembly.GetType("CameraCoop.CameraControlPanel");
            Assert.IsNotNull(panelType, "The approved camera-only control panel is missing.");
            Type probeType = typeof(HandInteractionProbe).Assembly.GetType("CameraCoop.Tests.TrackerLauncherProbe");
            Assert.IsNotNull(probeType, "Camera tests must use a process probe, never a real webcam process.");

            GameObject rig = CreateRoot("camera controls test");
            rig.SetActive(false);
            modes = rig.AddComponent<InputModeManager>();
            Invoke(modes, "Awake");
            launcher = (TrackerLauncher)rig.AddComponent(probeType);
            router = rig.AddComponent<HandInputRouter>();
            SetField(router, "inputModeManager", modes);
            Invoke(router, "OnEnable");
            GameObject receiverRoot = CreateRoot("inactive camera receiver");
            receiverRoot.SetActive(false);
            receiver = receiverRoot.AddComponent<UdpHandReceiver>();

            GameObject control = CreateRoot("camera button", typeof(RectTransform), typeof(Image), typeof(Button));
            button = control.GetComponent<Button>();
            button.targetGraphic = control.GetComponent<Image>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.transition = Selectable.Transition.None;
            RectTransform rect = (RectTransform)control.transform;
            rect.sizeDelta = new Vector2(240f, 80f);
            rect.position = new Vector3(360f, 220f, 0f);
            inside = new Vector2(360f, 220f);
            outside = new Vector2(50f, 50f);
            buttonLabel = CreateRoot("camera button label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            statusLabel = CreateRoot("camera status", typeof(RectTransform), typeof(Text)).GetComponent<Text>();

            panel = (MonoBehaviour)rig.AddComponent(panelType);
            SetField(panel, "launcher", launcher);
            SetField(panel, "receiver", receiver);
            SetField(panel, "inputModeManager", modes);
            SetField(panel, "handInputRouter", router);
            SetField(panel, "cameraButton", button);
            SetField(panel, "buttonLabel", buttonLabel);
            SetField(panel, "statusLabel", statusLabel);
            rig.SetActive(true);
            Invoke(panel, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            if (panel != null) Invoke(panel, "OnDisable");
            if (router != null) Invoke(router, "OnDisable");
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                if (roots[i] != null) Object.DestroyImmediate(roots[i]);
            }
            roots.Clear();
            InputFocus.IsTyping = false;
        }

        [Test]
        public void InitialOffState_DoesNotStartCameraAndAllowsItsMouseControl()
        {
            Assert.AreEqual(0, ProbeCount("startCalls"));
            Assert.AreEqual("Off", State());
            Assert.AreEqual("캠 켜기", buttonLabel.text);
            StringAssert.Contains("꺼짐", statusLabel.text);
            Assert.AreEqual(InputMode.Interact, modes.CurrentMode);
            Assert.IsTrue(modes.DesiredCursorVisible);
            Assert.IsFalse(modes.CanMove);
        }

        [Test]
        public void CanvasMouse_RejectsUninitializedPanelWithoutLaunching()
        {
            Invoke(panel, "OnDisable");

            Click();
            Assert.AreEqual(0, ProbeCount("startCalls"));
        }

        [Test]
        public void CanvasMouse_StartsOwnedTrackerFromOffState()
        {
            Click();

            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual("Starting", State());
        }

        [Test]
        public void CanvasMouse_RejectsReentryWhileStarting()
        {
            Click();

            Click();
            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual(0, ProbeCount("stopCalls"));
        }

        [Test]
        public void CanvasMouse_StopsOwnedTrackerAfterReceiving()
        {
            Click();
            FreshPacket(1);
            Refresh(1f);

            Assert.AreEqual("Receiving", State());
            modes.ProcessInput(true, CursorLockMode.Locked);
            Assert.IsTrue(modes.CanUseCameraMouse);
            Click(1f);
            Assert.AreEqual(1, ProbeCount("stopCalls"));
            Assert.AreEqual("Off", State());
        }

        [Test]
        public void CanvasMouse_SameTargetPressRelease_StartsTrackerOnce()
        {
            Pointer(inside, true, false);
            Pointer(inside, false, true);

            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual("Starting", State());
        }

        [Test]
        public void CanvasMouse_PressOutsideThenReleaseOnButton_DoesNotStartTracker()
        {
            Pointer(new Vector2(-500f, -500f), true, false);
            Pointer(inside, false, true);

            Assert.AreEqual(0, ProbeCount("startCalls"));
            Assert.AreEqual("Off", State());
        }

        [Test]
        public void AutomaticStartup_DefaultRemainsManual()
        {
            Invoke(panel, "TryAutomaticStartup", 0f);

            Assert.AreEqual(0, ProbeCount("startCalls"));
            Assert.AreEqual("Off", State());
        }

        [Test]
        public void AutomaticStartup_EditModeLifecycleNeverLaunches()
        {
            SetField(panel, "autoStartCamera", true);
            Invoke(panel, "OnEnable");
            Invoke(panel, "Start");

            Assert.IsFalse(Application.isPlaying);
            Assert.AreEqual(0, ProbeCount("startCalls"));
        }

        [Test]
        public void AutomaticStartup_OptInAttemptsOnceAndWaitsForPacket()
        {
            SetField(panel, "autoStartCamera", true);
            Invoke(panel, "TryAutomaticStartup", 0f);
            Invoke(panel, "TryAutomaticStartup", 1f);
            Refresh(2f);

            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual("Starting", State());
            Assert.IsFalse(modes.CanUseHandUi);
        }

        [Test]
        public void AutomaticStartup_ExistingOwnedProcessIsNotReplaced()
        {
            Assert.IsTrue(launcher.StartTracker());
            Invoke(panel, "OnEnable");
            SetField(panel, "autoStartCamera", true);

            Invoke(panel, "TryAutomaticStartup", 0f);

            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual(0, ProbeCount("stopCalls"));
            Assert.AreEqual("Starting", State());
        }

        [Test]
        public void AutomaticStartup_ExternalPacketConsumesAttemptWithoutLaunch()
        {
            FreshPacket(1);
            SetField(panel, "autoStartCamera", true);
            Invoke(panel, "TryAutomaticStartup", 0f);
            Assert.AreEqual("External", State());

            SetField(receiver, "lastPacketReceivedAt", Time.realtimeSinceStartup - 1f);
            Refresh(1f);
            Invoke(panel, "TryAutomaticStartup", 2f);

            Assert.AreEqual(0, ProbeCount("startCalls"));
            Assert.AreEqual(0, ProbeCount("stopCalls"));
            Assert.AreEqual("Failed", State());
        }

        [Test]
        public void AutomaticStartup_ExplicitStopStaysOffAfterReenable()
        {
            SetField(panel, "autoStartCamera", true);
            Invoke(panel, "TryAutomaticStartup", 0f);
            FreshPacket(1);
            Refresh(1f);
            Click(2f);
            Invoke(panel, "OnDisable");
            Invoke(panel, "OnEnable");
            Invoke(panel, "TryAutomaticStartup", 3f);

            Assert.AreEqual("Off", State());
            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual(1, ProbeCount("stopCalls"));
        }

        [Test]
        public void AutomaticStartup_FailureSurvivesReenableAndOnlyClickRetries()
        {
            SetField(launcher, "failStart", true);
            SetField(panel, "autoStartCamera", true);
            Invoke(panel, "TryAutomaticStartup", 0f);
            string failure = statusLabel.text;
            Invoke(panel, "OnDisable");
            Invoke(panel, "OnEnable");
            Invoke(panel, "TryAutomaticStartup", 1f);
            Refresh(2f);

            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual("Failed", State());
            Assert.AreEqual(failure, statusLabel.text);
            SetField(launcher, "failStart", false);
            Click(3f);
            Assert.AreEqual(2, ProbeCount("startCalls"));
            Assert.AreEqual("Starting", State());
        }

        [Test]
        public void ConnectionTimeout_ExplainsPermissionsAndPlatformSetup()
        {
            Click();
            Refresh(16f);

            StringAssert.Contains("권한", statusLabel.text);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            StringAssert.Contains("setup_tracker.bat", statusLabel.text);
#else
            StringAssert.Contains("setup_tracker.sh", statusLabel.text);
#endif
        }

        [Test]
        public void OwnedProcessExit_ShowsCapturedDiagnosticAndCleansOnce()
        {
            Click();
            CaptureDiagnostic("ImportError: camera backend unavailable");
            SetField(launcher, "running", false);

            Refresh(1f);
            string failure = statusLabel.text;
            Refresh(2f);

            StringAssert.Contains("camera backend unavailable", launcher.LastError);
            Assert.LessOrEqual(failure.Split('\n').Length, 2);
            foreach (string line in failure.Split('\n')) Assert.LessOrEqual(line.Length, 32);
            Assert.AreEqual(failure, statusLabel.text);
            Assert.AreEqual(1, ProbeCount("stopCalls"));
        }

        [Test]
        public void DiagnosticCapture_IsBoundedAndIgnoresUnownedSender()
        {
            Click();
            using (var owned = new System.Diagnostics.Process())
            using (var unrelated = new System.Diagnostics.Process())
            {
                SetLauncherField("stderrProcess", owned);
                InvokeLauncher("CaptureStandardError", owned, new string('x', 5000) + " recent detail");
                InvokeLauncher("CaptureStandardError", unrelated, "unrelated process diagnostic");
                InvokeLauncher("CaptureStandardError", owned, null);
                SetField(launcher, "running", false);
                Refresh(1f);
            }

            StringAssert.Contains("recent detail", launcher.LastError);
            StringAssert.DoesNotContain("unrelated process diagnostic", launcher.LastError);
            Assert.Less(launcher.LastError.Length, 2600);
        }

        [Test]
        public void DiagnosticSanitizer_CapPreservesLeadingAndLatestText()
        {
            string detail = "camera busy " + new string('x', 5000) + " recent detail";

            string sanitized = TrackerLauncher.SanitizeDiagnostic(detail);

            StringAssert.Contains("camera busy", sanitized);
            StringAssert.Contains("recent detail", sanitized);
            Assert.LessOrEqual(sanitized.Length, TrackerLauncher.DiagnosticLimit);
        }

        [Test]
        public void DiagnosticCapture_NewAttemptClearsPreviousProcessDetail()
        {
            Click();
            CaptureDiagnostic("old process diagnostic");
            SetField(launcher, "running", false);
            Refresh(1f);
            Click(2f);
            SetField(launcher, "running", false);
            Refresh(3f);

            StringAssert.DoesNotContain("old process diagnostic", launcher.LastError);
            Assert.AreEqual(2, ProbeCount("startCalls"));
        }

        [Test]
        public void ConnectionTimeout_IncludesCapturedDetailBeforeStopClearsIt()
        {
            Click();
            CaptureDiagnostic("Camera is already in use");
            Refresh(16f);

            StringAssert.Contains("Camera is already in use", launcher.LastError);
            Assert.AreEqual(2, statusLabel.text.Split('\n').Length);
            Assert.AreEqual(1, ProbeCount("stopCalls"));
        }

        [Test]
        public void ProcessExit_LateDiagnosticUpdatesFailureAndLogsOnlyOnce()
        {
            Click();
            using (var owned = new System.Diagnostics.Process())
            {
                SetLauncherField("stderrProcess", owned);
                SetLauncherField("stderrComplete", false);
                SetField(launcher, "running", false);
                Refresh(1f);
                Assert.AreEqual("Failed", State());
                Assert.AreEqual(0, ProbeCount("stopCalls"));

                InvokeLauncher("CaptureStandardError", owned, "Camera access denied");
                InvokeLauncher("CaptureStandardError", owned, null);
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Camera access denied"));
                Refresh(2f);
                Refresh(3f);

                StringAssert.Contains("Camera access denied", statusLabel.text);
                Assert.AreEqual(1, ProbeCount("stopCalls"));
                LogAssert.NoUnexpectedReceived();
            }
        }

        [Test]
        public void ProcessExit_DrainDeadlineDoesNotRequireSleepOrKeepOwnership()
        {
            Click();
            using (var owned = new System.Diagnostics.Process())
            {
                SetLauncherField("stderrProcess", owned);
                SetLauncherField("stderrComplete", false);
                SetField(launcher, "running", false);
                Refresh(1f);
                Assert.AreEqual(0, ProbeCount("stopCalls"));
                SetLauncherField("exitedAt", double.MinValue);
                double configuredExit = (double)typeof(TrackerLauncher)
                    .GetField("exitedAt", InstanceFlags).GetValue(launcher);
                Assert.GreaterOrEqual(
                    Time.realtimeSinceStartupAsDouble - configuredExit,
                    0.5d,
                    "The fixture must configure an expired double precision deadline.");
                Assert.IsTrue(
                    (bool)typeof(TrackerLauncher).GetField("ownsProcess", InstanceFlags).GetValue(launcher),
                    "The launcher must still own the exited process before deadline cleanup.");
                Assert.IsFalse(launcher.IsRunning, "The probe must report the owned process as exited.");
                Assert.IsTrue(
                    (bool)typeof(CameraControlPanel).GetField("initialized", InstanceFlags).GetValue(panel),
                    "The camera panel must remain initialized for deadline cleanup.");

                launcher.RefreshStatus();
                Assert.AreEqual(1, ProbeCount("stopCalls"), "Direct launcher refresh must clean an expired exit.");

                Refresh(2f);

                Assert.AreEqual(1, ProbeCount("stopCalls"));
                Assert.IsNull(typeof(TrackerLauncher).GetField("stderrProcess", InstanceFlags).GetValue(launcher));
                Assert.AreEqual("Failed", State());
            }
        }

        [Test]
        public void ProcessDiagnostic_RedactsCredentialsBeforeStatusAndLog()
        {
            Click();
            CaptureDiagnostic("ImportError\ntoken=private-test-value\nAuthorization: Bearer private-token");
            SetField(launcher, "running", false);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[redacted\]"));

            Refresh(1f);

            StringAssert.DoesNotContain("private-test-value", launcher.LastError);
            StringAssert.DoesNotContain("private-token", launcher.LastError);
            StringAssert.Contains("[redacted]", launcher.LastError);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MouseOutsideCameraButton_CannotStartOrDispatchAnotherButton()
        {
            Button other = CreateRoot("game button", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            int otherClicks = 0;
            other.onClick.AddListener(() => otherClicks++);
            Pointer(outside, true, false);
            Pointer(outside, false, true);
            Assert.AreEqual(0, ProbeCount("startCalls"));
            Assert.AreEqual(0, otherClicks);
        }

        [Test]
        public void PressOutsideThenReleaseInside_DoesNotStart()
        {
            Pointer(outside, true, false);
            Pointer(inside, false, true);
            Assert.AreEqual(0, ProbeCount("startCalls"));
        }

        [Test]
        public void PressLeavesButtonAndReturns_CancelsClick()
        {
            Pointer(inside, true, false);
            Pointer(outside, false, false);
            Pointer(inside, false, true);
            Assert.AreEqual(0, ProbeCount("startCalls"));
        }

        [Test]
        public void ValidClick_StartsOnceAndStartingRejectsRepeatedClicks()
        {
            Click();
            Assert.AreEqual("Starting", State());
            Assert.IsFalse(button.interactable);
            Click();
            Refresh(1f);
            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual("Starting", State(), "A process is not evidence of webcam packets.");
            Assert.IsFalse(modes.CanUseHandUi);
        }

        [Test]
        public void EmptyHandsPacket_IsReceivingAndRestoresExistingContext()
        {
            modes.SetContext(InputContext.UiOnly);
            Click();
            FreshPacket(1);
            Refresh(1f);
            Assert.AreEqual("Receiving", State());
            Assert.AreEqual("캠 끄기", buttonLabel.text);
            StringAssert.Contains("송신 수신 중", statusLabel.text);
            Assert.AreEqual(InputContext.UiOnly, modes.CurrentContext);
            Assert.IsTrue(modes.CanUseHandUi, "No hand landmarks is not a disconnected sender.");
        }

        [Test]
        public void ExternalFreshPackets_DoNotLaunchOrStopExternalProcess()
        {
            FreshPacket(1);
            Refresh(1f);
            Assert.AreEqual("External", State());
            Assert.IsFalse(button.interactable);
            StringAssert.Contains("외부", statusLabel.text);
            Click();
            Invoke(panel, "OnDisable");
            Assert.AreEqual(0, ProbeCount("startCalls"));
            Assert.AreEqual(0, ProbeCount("stopCalls"));
        }

        [Test]
        public void StartFailure_RemainsVisibleAcrossRefreshAndCanRetry()
        {
            SetField(launcher, "failStart", true);
            Click();
            string failure = statusLabel.text;
            Assert.AreEqual("Failed", State());
            StringAssert.Contains("실행 실패", failure);
            Refresh(1f);
            Refresh(2f);
            Assert.AreEqual(failure, statusLabel.text);
            Assert.IsTrue(button.interactable);
            SetField(launcher, "failStart", false);
            Click(3f);
            Assert.AreEqual("Starting", State());
            Assert.AreEqual(2, ProbeCount("startCalls"));
        }

        [Test]
        public void ConnectionTimeout_StopsOwnedProcessAndKeepsFailureVisible()
        {
            Click();
            Refresh(16f);
            Assert.AreEqual(1, ProbeCount("stopCalls"));
            Assert.AreEqual("Failed", State());
            StringAssert.Contains("시간 초과", statusLabel.text);
            string failure = statusLabel.text;
            Refresh(17f);
            Assert.AreEqual(failure, statusLabel.text);
            Assert.IsFalse(modes.CanMove);
        }

        [Test]
        public void StopOwnedCamera_DoesNotReconnectFromCachedPacket()
        {
            modes.SetContext(InputContext.UiOnly);
            Click();
            FreshPacket(1);
            Refresh(1f);
            Click(2f);
            Refresh(2.1f);
            Assert.AreEqual(1, ProbeCount("stopCalls"));
            Assert.AreEqual("Off", State());
            Assert.AreEqual("캠 켜기", buttonLabel.text);
            Assert.IsFalse(modes.CanUseHandUi);
        }

        [Test]
        public void NewExternalPacketAfterOwnedStop_IsAdoptedWithoutAnotherLaunch()
        {
            modes.SetContext(InputContext.UiOnly);
            Click();
            FreshPacket(1);
            Refresh(1f);
            Click(2f);
            FreshPacket(2);
            Refresh(3f);

            Assert.AreEqual("External", State());
            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual(1, ProbeCount("stopCalls"));
            Assert.IsTrue(modes.CanUseHandUi);
        }

        [Test]
        public void SenderLoss_CancelsHandCaptureAndReconnectRequiresNewOpen()
        {
            modes.SetContext(InputContext.UiOnly);
            FreshPacket(1);
            Refresh(0f);
            HandInteractionProbe target = CreateRoot("captured game target").AddComponent<HandInteractionProbe>();
            SendHand(1, 0f, false, target);
            SendHand(2, 0.11f, false, target);
            Assert.IsTrue(router.HasArmedHand, "Fresh open samples must rearm before the test pinch.");
            SendHand(3, 0.12f, true, target);
            Assert.AreEqual(1, target.presses,
                $"router active={router.isActiveAndEnabled}, handUI={modes.CanUseHandUi}, state={State()}");

            SetField(receiver, "lastPacketReceivedAt", Time.realtimeSinceStartup - 1f);
            Refresh(0.13f);
            Assert.AreEqual("Failed", State());
            Assert.AreEqual(1, target.cancels);
            Assert.AreEqual(0, target.releases);
            FreshPacket(2);
            Refresh(0.14f);
            SendHand(4, 0.15f, true, target);
            Assert.AreEqual(1, target.presses);
            SendHand(5, 0.16f, false, target);
            SendHand(6, 0.27f, false, target);
            SendHand(7, 0.28f, true, target);
            Assert.AreEqual(2, target.presses);
        }

        [Test]
        public void UnexpectedOwnedProcessExit_IsFailureDespiteRecentCachedPacket()
        {
            modes.SetContext(InputContext.UiOnly);
            Click();
            FreshPacket(1);
            Refresh(1f);
            SetField(launcher, "running", false);
            Refresh(1.1f);
            Assert.AreEqual("Failed", State());
            Assert.IsFalse(modes.CanUseHandUi);
            Refresh(1.2f);
            Assert.AreEqual("Failed", State());
        }

        [Test]
        public void FocusChangeBetweenPressAndRelease_CancelsMouseClick()
        {
            Pointer(inside, true, false);
            Invoke(modes, "OnApplicationFocus", false);
            Invoke(modes, "OnApplicationFocus", true);
            Pointer(inside, false, true);
            Assert.AreEqual(0, ProbeCount("startCalls"));
        }

        [Test]
        public void BlockedContext_RejectsCameraMouseClicksWhileReceiving()
        {
            Click();
            FreshPacket(1);
            Refresh(1f);
            Assert.AreEqual("Receiving", State());

            modes.SetContext(InputContext.Blocked);
            Click();

            Assert.AreEqual(0, ProbeCount("stopCalls"), "수신 중 Blocked에서는 캠 컨트롤을 닫는다.");
            Assert.AreEqual("Receiving", State());
        }

        // 차폐 중 캠이 끊기면 재시도 말고는 복구 경로가 없다 (docs/06 §9, docs/09 §7).
        [Test]
        public void BlockedContext_StillAllowsCameraRetryWhileNotReceiving()
        {
            modes.SetContext(InputContext.Blocked);

            Click();

            Assert.AreEqual(1, ProbeCount("startCalls"));
            Assert.AreEqual("Starting", State());
        }

        [Test]
        public void Disable_StopsOwnedProcessAndReleasesPreparationPermission()
        {
            Click();
            Invoke(panel, "OnDisable");
            Assert.AreEqual(1, ProbeCount("stopCalls"));
            Assert.AreEqual(InputMode.Move, modes.CurrentMode);
            Assert.IsFalse(modes.DesiredCursorVisible);
        }

        [TestCase("launcher")]
        [TestCase("receiver")]
        [TestCase("inputModeManager")]
        [TestCase("handInputRouter")]
        [TestCase("cameraButton")]
        [TestCase("buttonLabel")]
        [TestCase("statusLabel")]
        public void MissingExplicitReference_DisablesPanel(string field)
        {
            Invoke(panel, "OnDisable");
            SetField(panel, field, null);
            LogAssert.Expect(LogType.Error, "CameraControlPanel: " + field + " is unassigned.");
            Invoke(panel, "OnEnable");
            Assert.IsFalse(panel.enabled);
        }

        private void Click(float now = 0f)
        {
            Pointer(inside, true, false, now);
            Pointer(inside, false, true, now);
        }

        private void CaptureDiagnostic(string detail)
        {
            using (var owned = new System.Diagnostics.Process())
            {
                SetLauncherField("stderrProcess", owned);
                InvokeLauncher("CaptureStandardError", owned, detail);
                InvokeLauncher("CaptureStandardError", owned, null);
            }
        }

        private void SetLauncherField(string name, object value)
        {
            FieldInfo field = typeof(TrackerLauncher).GetField(name, InstanceFlags);
            Assert.IsNotNull(field, name);
            field.SetValue(launcher, value);
        }

        private void InvokeLauncher(string name, params object[] arguments)
        {
            MethodInfo method = typeof(TrackerLauncher).GetMethod(name, InstanceFlags);
            Assert.IsNotNull(method, name);
            method.Invoke(launcher, arguments);
        }

        private void Pointer(Vector2 position, bool pressed, bool released, float now = 0f)
        {
            Invoke(panel, "ProcessPointer", position, pressed, released, now);
        }

        private void Refresh(float now) => Invoke(panel, "RefreshConnection", now);
        private string State() => ReadProperty(panel, "State").ToString();
        private int ProbeCount(string field) => (int)launcher.GetType().GetField(field, InstanceFlags).GetValue(launcher);

        private void FreshPacket(uint sequence)
        {
            var packet = new HandPacket { v = 1, seq = sequence, hands = Array.Empty<HandData>() };
            SetField(receiver, "<LatestPacket>k__BackingField", packet);
            SetField(receiver, "lastSeq", (uint?)sequence);
            SetField(receiver, "lastPacketReceivedAt", Time.realtimeSinceStartup);
        }

        private void SendHand(ulong id, float now, bool pinched, HandInteractable target)
        {
            var sample = new HandInputSample("Left", Vector2.zero, (uint)id, id, 0f, true, pinched);
            router.ProcessSample(sample, now, target, Vector3.zero);
        }

        private GameObject CreateRoot(string name, params Type[] components)
        {
            var root = new GameObject(name, components);
            roots.Add(root);
            return root;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, InstanceFlags);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private static object ReadProperty(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, InstanceFlags);
            Assert.IsNotNull(property, name);
            return property.GetValue(target);
        }

        private static void Invoke(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, InstanceFlags);
            Assert.IsNotNull(method, name);
            method.Invoke(target, arguments);
        }

    }
}
