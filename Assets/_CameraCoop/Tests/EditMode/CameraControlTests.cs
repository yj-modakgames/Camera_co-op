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
        public void BlockedContext_RejectsCameraMouseClicks()
        {
            modes.SetContext(InputContext.Blocked);
            Click();
            Assert.AreEqual(0, ProbeCount("startCalls"));
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
