using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CameraCoop.Tests
{
    public class HandCanvasRoutingTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<string> strokeEvents = new List<string>();
        private GameObject rig;
        private HandPointer pointer;
        private CanvasSurface surface;
        private ToolState tools;
        private InputModeManager modes;
        private HandInputRouter router;
        private HandInteractable canvas;
        private Camera camera;
        private Vector3 lastWorld;
        private Vector2 lastNorm;
        private int erases;

        [SetUp]
        public void SetUp()
        {
            InputFocus.IsTyping = false;
            strokeEvents.Clear();
            erases = 0;
            rig = Root("local canvas input");
            rig.SetActive(false);
            modes = rig.AddComponent<InputModeManager>();
            Set(modes, "initialContext", InputContext.Drawing);
            tools = rig.AddComponent<ToolState>();
            camera = rig.AddComponent<Camera>();
            camera.transform.position = new Vector3(100f, 100f, -5f);
            surface = Root("writable canvas").AddComponent<CanvasSurface>();
            surface.transform.position = new Vector3(100f, 100f, 0f);
            surface.gameObject.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.01f);
            pointer = rig.AddComponent<HandPointer>();
            var source = typeof(HandPointer).Assembly.GetType("CameraCoop.HandPointerInputSource");
            Assert.IsNotNull(source, "Step 3 requires an explicit pointer input source.");
            Set(pointer, "inputSource", Enum.Parse(source, "HandRouter"));
            Set(pointer, "inputModeManager", modes);
            Set(pointer, "canvasSurface", surface);
            Set(pointer, "toolState", tools);
            Set(pointer, "aimCamera", camera);
            Type canvasType = typeof(HandPointer).Assembly.GetType("CameraCoop.HandCanvasInteractable");
            Assert.IsNotNull(canvasType);
            surface.gameObject.SetActive(false);
            canvas = (HandInteractable)surface.gameObject.AddComponent(canvasType);
            Set(canvas, "canvasSurface", surface);
            Set(canvas, "handPointer", pointer);
            router = rig.AddComponent<HandInputRouter>();
            Set(router, "inputModeManager", modes);
            Set(router, "playerCamera", camera);
            Set(router, "eventSystem", Root("canvas events").AddComponent<EventSystem>());
            Set(router, "uiRaycasters", Array.Empty<GraphicRaycaster>());
            Set(router, "activeCanvas", canvas);
            Set(router, "handPointer", pointer);
            pointer.OnCanvasStrokeStart += (hand, norm, world) => { strokeEvents.Add("start:" + hand); lastNorm = norm; lastWorld = world; };
            pointer.OnCanvasStrokeMove += (hand, norm, world) => { strokeEvents.Add("move:" + hand); lastNorm = norm; lastWorld = world; };
            pointer.OnCanvasStrokeEnd += hand => strokeEvents.Add("end:" + hand);
            pointer.OnCanvasErase += world => { erases++; lastWorld = world; };
            rig.SetActive(true);
            Call(modes, "Awake");
            Call(pointer, "Awake");
            Call(pointer, "OnEnable");
            Call(router, "OnEnable");
            surface.gameObject.SetActive(true);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (router != null) Call(router, "OnDisable");
            if (pointer != null) Call(pointer, "OnDisable");
            for (int i = roots.Count - 1; i >= 0; i--) if (roots[i] != null) Object.DestroyImmediate(roots[i]);
            roots.Clear();
            InputFocus.IsTyping = false;
        }

        [Test]
        public void LocalPointer_EmitsInkPlaneCoordinatesAndEndsIdempotently()
        {
            var norm = new Vector2(0.2f, 0.7f);
            Pointer("BeginCanvasStroke", "Left", surface, norm);
            Assert.AreEqual(surface.NormToWorld(norm), lastWorld);
            Assert.AreEqual(norm, lastNorm);
            Pointer("MoveCanvasStroke", "Left", surface, new Vector2(0.3f, 0.6f));
            Pointer("EndCanvasStroke", "Left");
            Pointer("EndCanvasStroke", "Left");
            CollectionAssert.AreEqual(new[] { "start:Left", "move:Left", "end:Left" }, strokeEvents);
        }

        [Test]
        public void LocalPointer_RejectsUnregisteredSurfaceAndInvalidCoordinates()
        {
            CanvasSurface other = Root("gallery").AddComponent<CanvasSurface>();
            Pointer("BeginCanvasStroke", "Left", other, Vector2.one * 0.5f);
            Pointer("BeginCanvasStroke", "Left", surface, new Vector2(float.NaN, 0.5f));
            Pointer("BeginCanvasStroke", "Left", surface, new Vector2(1.01f, 0.5f));
            Pointer("MoveCanvasStroke", "Left", surface, Vector2.one * 0.5f);
            Assert.IsEmpty(strokeEvents);
            Pointer("BeginCanvasStroke", "Left", surface, Vector2.one * 0.5f);
            Pointer("MoveCanvasStroke", "Left", other, Vector2.one * 0.5f);
            Pointer("MoveCanvasStroke", "Left", surface, Vector2.one * 0.5f);
            CollectionAssert.AreEqual(new[] { "start:Left", "end:Left" }, strokeEvents);
        }

        [Test]
        public void LocalPointer_PermissionLossEndsBothHandsAndHeldMoveCannotRestart()
        {
            Pointer("BeginCanvasStroke", "Left", surface, Vector2.zero);
            Pointer("BeginCanvasStroke", "Right", surface, Vector2.one);
            modes.SetContext(InputContext.UiOnly);
            Pointer("MoveCanvasStroke", "Left", surface, Vector2.one);
            Pointer("BeginCanvasStroke", "Right", surface, Vector2.one);
            CollectionAssert.AreEquivalent(new[] { "start:Left", "start:Right", "end:Left", "end:Right" }, strokeEvents);
        }

        [Test]
        public void LocalPointer_StrokesDisabledBlocksStartAndCancelsBothHands()
        {
            Pointer("BeginCanvasStroke", "Left", surface, Vector2.zero);
            Pointer("BeginCanvasStroke", "Right", surface, Vector2.one);
            pointer.StrokesEnabled = false;
            Pointer("BeginCanvasStroke", "Left", surface, Vector2.one);
            Pointer("MoveCanvasStroke", "Right", surface, Vector2.one);
            Assert.AreEqual(4, strokeEvents.Count);
            Assert.AreEqual(2, strokeEvents.FindAll(item => item.StartsWith("end:")).Count);
        }

        [Test]
        public void LocalPointer_FreezesDrawEraseModeForCapturedHand()
        {
            Pointer("BeginCanvasStroke", "Left", surface, Vector2.zero);
            Set(tools, "mode", ToolState.Mode.Erase);
            Pointer("MoveCanvasStroke", "Left", surface, Vector2.one);
            Pointer("BeginCanvasStroke", "Right", surface, Vector2.zero);
            Set(tools, "mode", ToolState.Mode.Draw);
            Pointer("MoveCanvasStroke", "Right", surface, Vector2.one);
            CollectionAssert.AreEqual(new[] { "start:Left", "move:Left" }, strokeEvents);
            Assert.AreEqual(2, erases);
        }

        [Test]
        public void LocalCanvas_ReleaseIsNotClickAndDisableEndsBothHands()
        {
            canvas.Press(Sample(1, true), surface.NormToWorld(Vector2.zero), default);
            Assert.IsFalse(canvas.Release(Sample(2, false), Vector3.zero));
            canvas.Press(Sample(3, true), surface.NormToWorld(Vector2.zero), default);
            canvas.Press(Sample(3, true, "Right"), surface.NormToWorld(Vector2.one), default);
            Call(canvas, "OnDisable");
            Call(canvas, "OnDisable");
            Assert.AreEqual(3, strokeEvents.FindAll(item => item.StartsWith("end:")).Count);
        }

        [Test]
        public void LocalRouter_OnlyRegisteredActualSurfaceMayReceivePhysicsHit()
        {
            Assert.AreSame(canvas, Resolve());
            var blocker = Root("front gallery collider");
            blocker.transform.position = new Vector3(100f, 100f, -1f);
            blocker.AddComponent<CanvasSurface>();
            blocker.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            Assert.IsNull(Resolve(), "An unrelated front collider must block the writable canvas.");
            blocker.SetActive(false);
            Set(canvas, "canvasSurface", Root("wrong registered identity").AddComponent<CanvasSurface>());
            Assert.IsNull(Resolve());
        }

        [Test]
        public void LocalRouter_DrawingGateBlocksPhysicsCanvas()
        {
            Assert.AreSame(canvas, Resolve());
            modes.SetContext(InputContext.UiOnly);
            Assert.IsNull(Resolve());
            modes.SetContext(InputContext.Drawing);
            pointer.StrokesEnabled = false;
            Assert.IsNull(Resolve());
        }

        [Test]
        public void LocalRouter_GapThenHeldReentryRequiresFreshOpenSamples()
        {
            Send(1, 0f, false, canvas);
            Send(2, 0.11f, false, canvas);
            Send(3, 0.12f, true, canvas);
            Send(4, 0.13f, true, null);
            Send(5, 0.14f, true, canvas);
            CollectionAssert.AreEqual(new[] { "start:Left", "end:Left" }, strokeEvents);
            Send(6, 0.15f, false, canvas);
            Send(7, 0.26f, false, canvas);
            Send(8, 0.27f, true, canvas);
            Assert.AreEqual("start:Left", strokeEvents[2]);
            router.CancelCanvasCaptures(HandCancelReason.DrawingCommand);
            Send(9, 0.28f, true, canvas);
            Assert.AreEqual(4, strokeEvents.Count);
        }

        [Test]
        public void LegacyPointer_RejectsLocalCanvasApi()
        {
            Call(pointer, "OnDisable");
            FieldInfo field = typeof(HandPointer).GetField("inputSource", Private);
            Set(pointer, "inputSource", Enum.ToObject(field.FieldType, 0));
            Pointer("BeginCanvasStroke", "Left", surface, Vector2.one);
            Assert.IsEmpty(strokeEvents);
        }

        private HandInteractable Resolve() => router.ResolveTarget(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out _, out _);
        private void Send(ulong id, float now, bool pinched, HandInteractable target) => router.ProcessSample(Sample(id, pinched), now, target, surface.NormToWorld(Vector2.one * 0.5f));
        private static HandInputSample Sample(ulong id, bool pinched, string hand = "Left") => new HandInputSample(hand, new Vector2(50f, 50f), (uint)id, id, 0f, true, pinched, HandCancelReason.None);
        private void Pointer(string name, params object[] args)
        {
            MethodInfo method = typeof(HandPointer).GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(method, name);
            method.Invoke(pointer, args);
        }
        private GameObject Root(string name)
        {
            var result = new GameObject(name);
            roots.Add(result);
            return result;
        }
        private static void Set(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, Private);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }
        private static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    }
}

namespace CameraCoop.Tests
{
    public class HandInputRouterTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> roots = new List<GameObject>();
        private GameObject rig;
        private HandInputRouter router;
        private InputModeManager modes;
        private EventSystem events;
        private EventSystem previousEvents;
        private HandInteractionProbe a;
        private HandInteractionProbe b;

        [SetUp]
        public void SetUp()
        {
            InputFocus.IsTyping = false;
            previousEvents = EventSystem.current;
            rig = CreateRoot("router test");
            rig.SetActive(false);
            modes = rig.AddComponent<InputModeManager>();
            Invoke(modes, "Awake");
            modes.SetContext(InputContext.UiOnly);
            router = rig.AddComponent<HandInputRouter>();
            GameObject eventObject = CreateRoot("router test events");
            eventObject.SetActive(false);
            events = eventObject.AddComponent<EventSystem>();
            events.sendNavigationEvents = false;
            eventObject.SetActive(true);
            Invoke(events, "OnEnable");
            EventSystem.current = events;
            SetField(router, "inputModeManager", modes);
            SetField(router, "eventSystem", events);
            SetField(router, "uiRaycasters", Array.Empty<GraphicRaycaster>());
            rig.SetActive(true);
            Invoke(router, "OnEnable");
            a = CreateRoot("target A").AddComponent<HandInteractionProbe>();
            b = CreateRoot("target B").AddComponent<HandInteractionProbe>();
        }

        [TearDown]
        public void TearDown()
        {
            InputFocus.IsTyping = false;
            if (router != null)
            {
                Invoke(router, "OnDisable");
            }
            if (events != null)
            {
                Invoke(events, "OnDisable");
            }
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                if (roots[i] != null)
                {
                    Object.DestroyImmediate(roots[i]);
                }
            }
            roots.Clear();
            if (previousEvents != null)
            {
                EventSystem.current = previousEvents;
            }
        }

        [Test]
        public void StartupHeldPinch_CannotPressUntilNewOpenSamplesRearm()
        {
            Send(1, 0f, true, a);
            Send(2, 0.11f, true, a);
            Assert.AreEqual(0, a.presses);
            Assert.IsTrue(router.HasFreshHand);
            Assert.IsFalse(router.HasArmedHand);

            Send(3, 0.12f, false, a);
            Send(4, 0.23f, false, a);
            Send(5, 0.24f, true, a);
            Assert.AreEqual(1, a.presses);
        }

        [Test]
        public void Rearm_RequiresTwoNewOpenSamplesSpanningMinimumDuration()
        {
            Send(1, 0f, false, a);
            router.Tick(0.04f);
            Assert.IsFalse(router.HasArmedHand, "Elapsed frames cannot manufacture sensor evidence.");
            Send(2, 0.05f, false, a);
            Assert.IsFalse(router.HasArmedHand);
            Send(3, 0.11f, false, a);
            Assert.IsTrue(router.HasArmedHand);
            Assert.IsTrue(router.TryGetHandState("Left", out HandInputState state));
            Assert.AreEqual(3ul, state.sample.sampleId);
            Assert.IsTrue(state.isFresh);
            Assert.IsTrue(state.isArmed);
            Assert.IsFalse(router.TryGetHandState("Unknown", out _));
        }

        [Test]
        public void DuplicateOpenSample_DoesNotRearmOrRenewFreshness()
        {
            Send(1, 0f, false, a);
            Send(1, 0.11f, false, a);
            Assert.IsFalse(router.HasArmedHand);
            router.Tick(0.201f);
            Assert.IsFalse(router.HasFreshHand);
            Assert.AreEqual(1, a.enters);
        }

        [Test]
        public void OpenSampleGapPastFreshness_RestartsRearmWindow()
        {
            Send(1, 0f, false, a);
            Send(2, 0.201f, false, a);
            Assert.IsFalse(router.HasArmedHand);
            Send(3, 0.25f, false, a);
            Assert.IsFalse(router.HasArmedHand);
            Send(4, 0.312f, false, a);
            Assert.IsTrue(router.HasArmedHand);
        }

        [Test]
        public void DeliveredSampleAge_CountsTowardFreshnessTimeout()
        {
            router.ProcessSample(Sample(1, false, age: 0.19f), 1f, a, Vector3.zero);
            Assert.IsTrue(router.HasFreshHand);
            router.Tick(1.011f);
            Assert.IsFalse(router.HasFreshHand);
        }

        [TestCase(0.201f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-0.01f)]
        public void InvalidOrAlreadyStaleSampleAge_NeverArms(float age)
        {
            router.ProcessSample(Sample(1, false, age: age), 1f, a, Vector3.zero);
            router.ProcessSample(Sample(2, false, age: age), 1.11f, a, Vector3.zero);
            Assert.IsFalse(router.HasFreshHand);
            Assert.IsFalse(router.HasArmedHand);
            Assert.AreEqual(0, a.enters);
        }

        [Test]
        public void DuplicateOrOlderSample_CannotHoldOrReleaseCapturedButton()
        {
            Arm();
            Send(3, 0.12f, true, a);
            Send(3, 0.13f, true, a);
            Send(2, 0.14f, false, a);
            Send(3, 0.15f, false, a);
            Assert.AreEqual(0, a.holds);
            Assert.AreEqual(0, a.releases);
            Send(4, 0.16f, false, a);
            Assert.AreEqual(1, a.releases);
        }

        [Test]
        public void NewLocalSampleId_AcceptsRestartedWireSequence()
        {
            router.ProcessSample(Sample(1, false, sequence: 999), 0f, a, Vector3.zero);
            router.ProcessSample(Sample(2, false, sequence: 1000), 0.11f, a, Vector3.zero);
            router.ProcessSample(Sample(3, true, sequence: 0), 0.12f, a, Vector3.zero);
            router.ProcessSample(Sample(4, false, sequence: 1), 0.13f, a, Vector3.zero);
            Assert.AreEqual(1, a.presses);
            Assert.AreEqual(1, a.releases);
        }

        [Test]
        public void SilenceWhileCaptured_CancelsOnceWithoutRelease()
        {
            Arm();
            Send(3, 0.12f, true, a);
            router.Tick(0.321f);
            router.Tick(0.5f);
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(HandCancelReason.StaleSample, a.lastCancelReason);
            Assert.AreEqual(0, a.releases);
            Assert.IsFalse(router.HasFreshHand);
            Assert.IsFalse(router.HasArmedHand);
        }

        [TestCase(HandCancelReason.TrackingLost)]
        [TestCase(HandCancelReason.InvalidSample)]
        public void InvalidObservation_CancelsCaptureAndNeverBecomesUp(HandCancelReason reason)
        {
            Arm();
            Send(3, 0.12f, true, a);
            router.ProcessSample(Sample(4, false, tracked: false, reason: reason), 0.13f, a, Vector3.zero);
            Send(5, 0.14f, false, a);
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(reason, a.lastCancelReason);
            Assert.AreEqual(0, a.releases);
            Assert.IsFalse(router.HasArmedHand);
        }

        [Test]
        public void TerminalCancelWithLastPacketId_IsNotDiscardedAsDuplicate()
        {
            Arm();
            Send(3, 0.12f, true, a);
            var lost = Sample(3, false, tracked: false, reason: HandCancelReason.TrackingLost);
            router.ProcessSample(lost, 0.13f, null, Vector3.zero);
            router.ProcessSample(lost, 0.14f, null, Vector3.zero);
            Send(3, 0.15f, false, a);
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(0, a.releases);
            Assert.IsFalse(router.HasFreshHand);
        }

        [Test]
        public void HeldPinch_DeliversOnlyNewHoldsAndOneRelease()
        {
            Arm();
            router.SetViewGeneration(7);
            Arm(3, 0.12f);
            Send(5, 0.24f, true, a);
            Send(6, 0.30f, true, a);
            Send(7, 0.40f, true, a);
            Send(8, 0.50f, true, a);
            Send(9, 0.51f, false, a);
            Send(10, 0.52f, false, a);
            Assert.AreEqual(1, a.presses);
            Assert.AreEqual(3, a.holds);
            Assert.AreEqual(1, a.releases);
            Assert.AreEqual(7, a.lastPress.viewGeneration);
            Assert.AreEqual(5ul, a.lastPress.pressSampleId);
            Assert.AreEqual("Left", a.lastPress.handedness);
        }

        [Test]
        public void LeavingButton_CancelsAndHeldReentryCannotResume()
        {
            Arm();
            Send(3, 0.12f, true, a);
            Send(4, 0.13f, true, b);
            Send(5, 0.14f, true, a);
            Send(6, 0.15f, false, a);
            Assert.AreEqual(1, a.presses);
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(0, a.releases);
            Assert.AreEqual(0, b.presses);
            Send(7, 0.26f, false, a);
            Send(8, 0.27f, true, a);
            Send(9, 0.28f, false, a);
            Assert.AreEqual(2, a.presses);
            Assert.AreEqual(1, a.releases);
        }

        [Test]
        public void UpOverDifferentTarget_DoesNotClickEitherTarget()
        {
            Arm();
            Send(3, 0.12f, true, a);
            Send(4, 0.13f, false, b);
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(0, a.releases);
            Assert.AreEqual(0, b.releases);
        }

        [Test]
        public void TargetCooldown_SuppressesFastSecondConfirmationButNotAnotherTarget()
        {
            ArmBoth(a, b);
            Send(3, 0.12f, true, a);
            Send(4, 0.13f, false, a);
            Send(5, 0.24f, false, a);
            Send(3, 0.24f, false, b, "Right");
            Send(6, 0.25f, true, a);
            Send(4, 0.25f, true, b, "Right");
            Send(7, 0.26f, false, a);
            Assert.AreEqual(1, a.releases);
            Send(5, 0.26f, false, b, "Right");
            Assert.AreEqual(1, b.releases);
        }

        [Test]
        public void SameButtonSimultaneousDown_LeftDeliveryOwnsCapture()
        {
            ArmBoth(a, a);
            Send(3, 0.12f, true, a, "Left");
            Send(3, 0.12f, true, a, "Right");
            Send(4, 0.13f, false, a, "Left");
            Send(4, 0.13f, true, a, "Right");
            Send(5, 0.14f, false, a, "Right");
            Assert.AreEqual(1, a.presses);
            Assert.AreEqual("Left", a.lastPress.handedness);
            Assert.AreEqual(1, a.releases);
        }

        [Test]
        public void DifferentTargets_KeepIndependentHandCaptures()
        {
            ArmBoth(a, b);
            Send(3, 0.12f, true, a, "Left");
            Send(3, 0.12f, true, b, "Right");
            router.ProcessSample(Sample(4, false, tracked: false, reason: HandCancelReason.TrackingLost), 0.13f, null, Vector3.zero);
            Send(4, 0.13f, false, b, "Right");
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(0, a.releases);
            Assert.AreEqual(1, b.releases);
        }

        [Test]
        public void RepeatedCancelAll_IsIdempotentAndDoesNotRelease()
        {
            ArmBoth(a, b);
            Send(3, 0.12f, true, a, "Left");
            Send(3, 0.12f, true, b, "Right");
            router.CancelAll(HandCancelReason.DrawingCommand);
            router.CancelAll(HandCancelReason.DrawingCommand);
            Send(4, 0.13f, false, a);
            Send(4, 0.13f, false, b, "Right");
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(1, b.cancels);
            Assert.AreEqual(HandCancelReason.DrawingCommand, a.lastCancelReason);
            Assert.AreEqual(0, a.releases + b.releases);
        }

        [Test]
        public void CancelCanvasCaptures_LeavesUiCaptureIntact()
        {
            modes.SetContext(InputContext.Drawing);
            a.canvas = true;
            ArmBoth(a, b);
            Send(3, 0.12f, true, a, "Left");
            Send(3, 0.12f, true, b, "Right");
            router.CancelCanvasCaptures(HandCancelReason.DrawingCommand);
            router.CancelCanvasCaptures(HandCancelReason.DrawingCommand);
            Send(4, 0.13f, false, a);
            Send(4, 0.13f, false, b, "Right");
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(0, b.cancels);
            Assert.AreEqual(0, a.releases);
            Assert.AreEqual(1, b.releases);
        }

        [Test]
        public void BlockedContext_StillObservesFreshnessAndRearmWithoutDeliveries()
        {
            modes.SetContext(InputContext.Blocked);
            Arm();
            Assert.IsTrue(router.HasFreshHand);
            Assert.IsTrue(router.HasArmedHand);
            Assert.AreEqual(0, a.enters);
            Send(3, 0.12f, true, a);
            Assert.AreEqual(0, a.presses);
            modes.SetContext(InputContext.UiOnly);
            Send(4, 0.13f, true, a);
            Assert.AreEqual(0, a.presses);
            Assert.IsFalse(router.HasArmedHand);
        }

        [TestCase(InputContext.Blocked)]
        [TestCase(InputContext.Explore)]
        public void ContextOrModeTransition_CancelsAndRequiresOpenAfterReturn(InputContext away)
        {
            Arm();
            Send(3, 0.12f, true, a);
            modes.SetContext(away);
            if (away == InputContext.Explore)
            {
                modes.RequestMode(InputMode.Move);
            }
            modes.SetContext(InputContext.UiOnly);
            Send(4, 0.13f, true, a);
            Send(5, 0.14f, false, a);
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(1, a.presses);
            Assert.AreEqual(0, a.releases);
            Assert.IsFalse(router.HasArmedHand);
        }

        [Test]
        public void ViewTransition_CancelsHeldCaptureButSameGenerationDoesNot()
        {
            router.SetViewGeneration(4);
            Arm();
            Send(3, 0.12f, true, a);
            router.SetViewGeneration(4);
            Assert.AreEqual(0, a.cancels);
            router.SetViewGeneration(5);
            Send(4, 0.13f, true, a);
            Send(5, 0.14f, false, a);
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(HandCancelReason.ViewChanged, a.lastCancelReason);
            Assert.AreEqual(0, a.releases);
            Assert.IsFalse(router.HasArmedHand);
        }

        [Test]
        public void ReleaseChangingView_InvalidatesOtherHandsOldCaptureBeforeDelivery()
        {
            router.SetViewGeneration(4);
            ArmBoth(a, b);
            Send(3, 0.12f, true, a, "Left");
            Send(3, 0.12f, true, b, "Right");
            a.released = () => router.SetViewGeneration(5);
            Send(4, 0.13f, false, a, "Left");
            Send(4, 0.13f, false, b, "Right");
            Assert.AreEqual(1, a.releases);
            Assert.AreEqual(0, a.cancels, "The release must detach its own capture before callbacks.");
            Assert.AreEqual(0, b.releases);
            Assert.AreEqual(1, b.cancels);
            Assert.IsFalse(router.HasArmedHand);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnavailableTarget_IsRevokedWithoutAnyMoreEvents(bool disableComponent)
        {
            Arm();
            Send(3, 0.12f, true, a);
            if (disableComponent) a.enabled = false;
            else a.available = false;
            router.Tick(0.13f);
            Send(4, 0.14f, false, a);
            Assert.AreEqual(0, a.holds + a.releases + a.cancels + a.exits);
            Assert.IsFalse(router.HasArmedHand);
            a.enabled = true;
            a.available = true;
            Send(5, 0.15f, true, a);
            Assert.AreEqual(1, a.presses);
        }

        [Test]
        public void DestroyedTarget_CanBeRevokedWithoutMissingReferenceException()
        {
            Arm();
            Send(3, 0.12f, true, a);
            Object.DestroyImmediate(a.gameObject);
            Assert.DoesNotThrow(() => router.Tick(0.13f));
            Assert.DoesNotThrow(() => router.CancelAll(HandCancelReason.TargetUnavailable));
            Assert.IsFalse(router.HasArmedHand);
        }

        [Test]
        public void FocusLossAndRecovery_CannotTurnHeldPinchIntoANewPress()
        {
            Arm();
            Send(3, 0.12f, true, a);
            Invoke(router, "OnApplicationFocus", false);
            Send(4, 0.13f, true, a);
            Invoke(router, "OnApplicationFocus", true);
            Send(5, 0.14f, true, a);
            Send(6, 0.15f, false, a);
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(HandCancelReason.FocusLost, a.lastCancelReason);
            Assert.AreEqual(1, a.presses);
            Assert.AreEqual(0, a.releases);
        }

        [Test]
        public void DisableAndReenable_CannotRestoreAnOldCapture()
        {
            Arm();
            Send(3, 0.12f, true, a);
            Invoke(router, "OnDisable");
            Invoke(router, "OnDisable");
            Invoke(router, "OnEnable");
            Send(4, 0.13f, true, a);
            Send(5, 0.14f, false, a);
            Assert.AreEqual(1, a.cancels);
            Assert.AreEqual(HandCancelReason.ComponentDisabled, a.lastCancelReason);
            Assert.AreEqual(1, a.presses);
            Assert.AreEqual(0, a.releases);
        }

        [Test]
        public void TargetDisableBetweenTicks_RevokesCaptureAndRestoresHover()
        {
            HandButtonInteractable adapter = CreateButton(out Button button);
            var hover = (Graphic)typeof(HandButtonInteractable).GetField("hoverGraphic", PrivateInstance).GetValue(adapter);
            int handClicks = 0;
            adapter.OnHandClick += context => handClicks++;
            Arm(target: adapter);
            Send(3, 0.12f, true, adapter);
            adapter.enabled = false;
            Invoke(adapter, "OnDisable");
            adapter.enabled = true;
            Invoke(adapter, "OnEnable");

            Send(4, 0.13f, true, adapter);
            bool restoredHover = hover.enabled;
            Send(5, 0.14f, false, adapter);

            Assert.IsTrue(restoredHover, "The re-enabled adapter needs a new hover entry even at the same screen position.");
            Assert.AreEqual(0, handClicks);
            Assert.IsFalse(HasClickCooldown(adapter), "An adapter reset must not create confirmed-click side effects.");
            Assert.IsFalse(router.HasArmedHand);
        }

        [Test]
        public void NativeSelectionRejectingRelease_DoesNotCreateConfirmedClickCooldown()
        {
            HandButtonInteractable adapter = CreateButton(out Button button);
            int handClicks = 0;
            adapter.OnHandClick += context => handClicks++;
            EventTrigger trigger = button.gameObject.AddComponent<EventTrigger>();
            var select = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            select.callback.AddListener(data =>
            {
                adapter.enabled = false;
                Invoke(adapter, "OnDisable");
            });
            trigger.triggers.Add(select);
            Arm(target: adapter);
            Send(3, 0.12f, true, adapter);

            Send(4, 0.13f, false, adapter);

            Assert.AreEqual(0, handClicks);
            Assert.IsFalse(HasClickCooldown(adapter), "A release rejected during native selection is not a confirmed click.");
        }

        [Test]
        public void Hover_EntersOncePerHandAndLeavesOncePerTarget()
        {
            Send(1, 0f, false, a, "Left");
            Send(1, 0f, false, a, "Right");
            Send(2, 0.05f, false, a, "Left");
            Send(2, 0.05f, false, a, "Right");
            Send(3, 0.10f, false, b, "Left");
            Assert.AreEqual(2, a.enters);
            Assert.AreEqual(1, a.exits);
            Assert.AreEqual(1, b.enters);
        }

        [Test]
        public void TrackingStatus_RequestsHandsAgainAfterSilentTimeout()
        {
            Text label = CreateRoot("tracking status", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            SetField(router, "trackingStatusLabel", label);
            router.Tick(0f);
            Assert.AreEqual("손을 카메라에 보여주세요", label.text);
            Send(1, 0.01f, false, a);
            Assert.AreEqual(string.Empty, label.text);
            router.Tick(0.211f);
            Assert.AreEqual("손을 카메라에 보여주세요", label.text);
        }

        [Test]
        public void TenPinchCycles_ConfirmExactlyTenHandClicksAndZeroNativeClicks()
        {
            HandButtonInteractable adapter = CreateButton(out Button button);
            int handClicks = 0;
            int nativeClicks = 0;
            adapter.OnHandClick += context => handClicks++;
            button.onClick.AddListener(() => nativeClicks++);
            ulong id = 1;
            for (int i = 0; i < 10; i++)
            {
                float start = i * 0.3f;
                Send(id++, start, false, adapter);
                Send(id++, start + 0.11f, false, adapter);
                Send(id++, start + 0.12f, true, adapter);
                Send(id++, start + 0.13f, false, adapter);
            }
            Assert.AreEqual(10, handClicks);
            Assert.AreEqual(0, nativeClicks);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingRaycasterEntry_LogsItsIndexAndDisablesRouter(bool includeValidRaycaster)
        {
            GraphicRaycaster valid = AssignValidStartReferences(out AudioClip hoverClip, out AudioClip clickClip);
            try
            {
                SetField(router, "uiRaycasters", includeValidRaycaster ?
                    new GraphicRaycaster[] { valid, null } : new GraphicRaycaster[] { null });
                int missingIndex = includeValidRaycaster ? 1 : 0;
                LogAssert.Expect(LogType.Error, "HandInputRouter: uiRaycasters[" + missingIndex + "] is unassigned.");

                Invoke(router, "Start");

                Assert.IsFalse(router.enabled);
            }
            finally
            {
                Object.DestroyImmediate(hoverClip);
                Object.DestroyImmediate(clickClip);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MissingStatusLabel_LogsItsNameAndDisablesRouter(bool missingTrackingLabel)
        {
            AssignValidStartReferences(out AudioClip hoverClip, out AudioClip clickClip);
            try
            {
                string missingField = missingTrackingLabel ? "trackingStatusLabel" : "hoverStatusLabel";
                SetField(router, missingField, null);
                LogAssert.Expect(LogType.Error, "HandInputRouter: " + missingField + " is unassigned.");

                Invoke(router, "Start");

                Assert.IsFalse(router.enabled);
            }
            finally
            {
                Object.DestroyImmediate(hoverClip);
                Object.DestroyImmediate(clickClip);
            }
        }

        [UnityTest]
        public IEnumerator GraphicRaycast_HigherOverlaySortOrderWinsRegardlessOfArrayOrder()
        {
            GraphicRaycaster low = CreateOverlay(1, out RectTransform lowRoot);
            GraphicRaycaster high = CreateOverlay(9, out RectTransform highRoot);
            AddFullScreenImage(lowRoot, "low target").gameObject.AddComponent<HandInteractionProbe>();
            HandInteractionProbe expected = AddFullScreenImage(highRoot, "high target").gameObject.AddComponent<HandInteractionProbe>();
            SetField(router, "uiRaycasters", new[] { low, high });
            yield return WaitForCanvasRender(low, high);

            HandInteractable actual = router.ResolveTarget(ScreenCenter(), out _, out bool blocked);

            Assert.IsTrue(blocked);
            Assert.AreSame(expected, actual);
        }

        [UnityTest]
        public IEnumerator GraphicRaycast_TopNonTargetGraphicBlocksUnderlyingTarget()
        {
            GraphicRaycaster raycaster = CreateOverlay(1, out RectTransform canvas);
            AddFullScreenImage(canvas, "underlying target").gameObject.AddComponent<HandInteractionProbe>();
            AddFullScreenImage(canvas, "blocking panel");
            SetField(router, "uiRaycasters", new[] { raycaster });
            yield return WaitForCanvasRender(raycaster);

            HandInteractable actual = router.ResolveTarget(ScreenCenter(), out _, out bool blocked);

            Assert.IsTrue(blocked, "A non-actionable graphic still blocks a future world fallback.");
            Assert.IsNull(actual);
        }

        [UnityTest]
        public IEnumerator GraphicRaycast_DisabledAdapterStillBlocks()
        {
            return VerifyUnavailableButtonBlocks(true);
        }

        [UnityTest]
        public IEnumerator GraphicRaycast_NonInteractableButtonStillBlocks()
        {
            return VerifyUnavailableButtonBlocks(false);
        }

        private IEnumerator VerifyUnavailableButtonBlocks(bool disableAdapter)
        {
            GraphicRaycaster raycaster = CreateOverlay(1, out RectTransform canvas);
            AddFullScreenImage(canvas, "underlying target").gameObject.AddComponent<HandInteractionProbe>();
            HandButtonInteractable adapter = CreateButton(out Button button);
            adapter.transform.SetParent(canvas, false);
            Stretch((RectTransform)adapter.transform);
            if (disableAdapter) adapter.enabled = false;
            else button.interactable = false;
            SetField(router, "uiRaycasters", new[] { raycaster });
            yield return WaitForCanvasRender(raycaster);

            HandInteractable actual = router.ResolveTarget(ScreenCenter(), out _, out bool blocked);

            Assert.IsTrue(blocked);
            Assert.IsNull(actual);
        }

        private static IEnumerator WaitForCanvasRender(params GraphicRaycaster[] raycasters)
        {
            EditorWindow previousFocus = EditorWindow.focusedWindow;
            try
            {
                Assert.IsTrue(EditorApplication.ExecuteMenuItem("Window/General/Game"), "The Editor Game view must be available for real overlay rendering.");
                for (int frame = 0; frame < 30; frame++)
                {
                    Canvas.ForceUpdateCanvases();
                    bool rendered = true;
                    foreach (GraphicRaycaster raycaster in raycasters)
                    {
                        IList<Graphic> graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(raycaster.GetComponent<Canvas>());
                        rendered &= graphics.Count > 0;
                        for (int i = 0; i < graphics.Count; i++)
                        {
                            rendered &= graphics[i].depth >= 0;
                        }
                    }
                    if (rendered)
                    {
                        yield break;
                    }
                    EditorApplication.QueuePlayerLoopUpdate();
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    yield return null;
                }
                foreach (GraphicRaycaster raycaster in raycasters)
                {
                    IList<Graphic> graphics = GraphicRegistry.GetRaycastableGraphicsForCanvas(raycaster.GetComponent<Canvas>());
                    Assert.Greater(graphics.Count, 0, raycaster.name + " has no registered raycast graphics.");
                    for (int i = 0; i < graphics.Count; i++)
                    {
                        Assert.GreaterOrEqual(graphics[i].depth, 0, graphics[i].name + " did not render within 30 Editor frames.");
                    }
                }
            }
            finally
            {
                if (previousFocus != null) previousFocus.Focus();
            }
        }

        private void Arm(ulong id = 1, float start = 0f, string hand = "Left", HandInteractable target = null)
        {
            Send(id, start, false, target != null ? target : a, hand);
            Send(id + 1, start + 0.11f, false, target != null ? target : a, hand);
        }

        private bool HasClickCooldown(HandInteractable target)
        {
            FieldInfo field = typeof(HandInputRouter).GetField("lastClicks", PrivateInstance);
            Assert.IsNotNull(field);
            return ((Dictionary<HandInteractable, float>)field.GetValue(router)).ContainsKey(target);
        }

        private void ArmBoth(HandInteractable left, HandInteractable right)
        {
            Send(1, 0f, false, left, "Left");
            Send(1, 0f, false, right, "Right");
            Send(2, 0.11f, false, left, "Left");
            Send(2, 0.11f, false, right, "Right");
        }

        private void Send(ulong id, float now, bool pinched, HandInteractable target, string hand = "Left")
        {
            router.ProcessSample(Sample(id, pinched, hand), now, target, Vector3.zero);
        }

        private static HandInputSample Sample(ulong id, bool pinched, string hand = "Left", float age = 0f,
            bool tracked = true, HandCancelReason reason = HandCancelReason.None, uint? sequence = null)
        {
            return new HandInputSample(hand, new Vector2(50f, 50f), sequence ?? (uint)id, id, age, tracked, pinched, reason);
        }

        private GraphicRaycaster AssignValidStartReferences(out AudioClip hoverClip, out AudioClip clickClip)
        {
            GameObject dependencies = CreateRoot("router start dependencies");
            dependencies.SetActive(false);
            SetField(router, "cursorController", dependencies.AddComponent<HandCursorController>());
            SetField(router, "playerCamera", dependencies.AddComponent<Camera>());
            SetField(router, "audioSource", dependencies.AddComponent<AudioSource>());
            SetField(router, "trackingStatusLabel", CreateRoot("tracking status", typeof(RectTransform), typeof(Text)).GetComponent<Text>());
            SetField(router, "hoverStatusLabel", CreateRoot("hover status", typeof(RectTransform), typeof(Text)).GetComponent<Text>());
            hoverClip = AudioClip.Create("router hover test", 32, 1, 8000, false);
            clickClip = AudioClip.Create("router click test", 32, 1, 8000, false);
            SetField(router, "hoverClip", hoverClip);
            SetField(router, "clickClip", clickClip);
            GraphicRaycaster raycaster = CreateOverlay(1, out _);
            SetField(router, "uiRaycasters", new[] { raycaster });
            return raycaster;
        }

        private HandButtonInteractable CreateButton(out Button button)
        {
            GameObject target = CreateRoot("actual hand button", typeof(RectTransform), typeof(Image));
            target.SetActive(false);
            button = target.AddComponent<Button>();
            button.targetGraphic = target.GetComponent<Image>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            HandButtonInteractable adapter = target.AddComponent<HandButtonInteractable>();
            SetField(adapter, "targetButton", button);
            SetField(adapter, "eventSystem", events);
            SetField(adapter, "hoverGraphic", AddButtonFeedback(target.transform, "hover"));
            SetField(adapter, "pressedGraphic", AddButtonFeedback(target.transform, "pressed"));
            target.SetActive(true);
            Invoke(adapter, "Awake");
            return adapter;
        }

        private static Image AddButtonFeedback(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform), typeof(Image));
            child.transform.SetParent(parent, false);
            Image graphic = child.GetComponent<Image>();
            graphic.raycastTarget = false;
            graphic.enabled = false;
            return graphic;
        }

        private GraphicRaycaster CreateOverlay(int sortingOrder, out RectTransform root)
        {
            GameObject canvasObject = CreateRoot("overlay " + sortingOrder, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            root = (RectTransform)canvasObject.transform;
            return canvasObject.GetComponent<GraphicRaycaster>();
        }

        private static Image AddFullScreenImage(RectTransform parent, string name)
        {
            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Stretch((RectTransform)imageObject.transform);
            Image image = imageObject.GetComponent<Image>();
            image.raycastTarget = true;
            return image;
        }

        private static void Stretch(RectTransform transform)
        {
            transform.anchorMin = Vector2.zero;
            transform.anchorMax = Vector2.one;
            transform.offsetMin = Vector2.zero;
            transform.offsetMax = Vector2.zero;
        }

        private static Vector2 ScreenCenter() => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        private GameObject CreateRoot(string name, params Type[] components)
        {
            var created = new GameObject(name, components);
            roots.Add(created);
            return created;
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field, "Required explicit Inspector reference: " + name);
            field.SetValue(instance, value);
        }

        private static void Invoke(object instance, string name, params object[] args)
        {
            MethodInfo callback = instance.GetType().GetMethod(name, PrivateInstance);
            Assert.IsNotNull(callback, name);
            callback.Invoke(instance, args);
        }
    }
}

namespace CameraCoop.Tests
{
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;
    using Object = UnityEngine.Object;

    public class HandButtonTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject rig;
        private EventSystem eventSystem;
        private EventSystem previousEventSystem;
        private HandButtonInteractable adapter;
        private Button button;
        private InputField inputField;
        private Image hover;
        private Image pressed;
        private readonly List<HandClickContext> clicks = new List<HandClickContext>();

        [SetUp]
        public void SetUp()
        {
            clicks.Clear();
            previousEventSystem = EventSystem.current;
            rig = new GameObject("hand adapter test");
            var events = new GameObject("test events", typeof(EventSystem));
            events.transform.SetParent(rig.transform, false);
            eventSystem = events.GetComponent<EventSystem>();
            Invoke(eventSystem, "OnEnable");
            EventSystem.current = eventSystem;
            eventSystem.sendNavigationEvents = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (eventSystem != null)
            {
                Invoke(eventSystem, "OnDisable");
            }
            Object.DestroyImmediate(rig);
            if (previousEventSystem != null)
            {
                EventSystem.current = previousEventSystem;
            }
        }

        [Test]
        public void ValidRelease_EmitsOriginalPressContextExactlyOnce()
        {
            CreateAdapter(false);
            var context = new HandClickContext("Left", 7, 11);
            adapter.Press(Sample("Left", 11, true), Vector3.zero, context);

            adapter.Release(Sample("Left", 13, false), Vector3.one);
            adapter.Release(Sample("Left", 14, false), Vector3.one);

            Assert.AreEqual(1, clicks.Count);
            Assert.AreEqual("Left", clicks[0].handedness);
            Assert.AreEqual(7, clicks[0].viewGeneration);
            Assert.AreEqual(11ul, clicks[0].pressSampleId);
        }

        [Test]
        public void ValidRelease_NeverInvokesNativeButtonOnClick()
        {
            CreateAdapter(false);
            int nativeClicks = 0;
            button.onClick.AddListener(() => nativeClicks++);

            Click("Left", 11);

            Assert.AreEqual(1, clicks.Count, "Only OnHandClick confirms the hand action.");
            Assert.AreEqual(0, nativeClicks);
        }

        [Test]
        public void Cancel_ResetsPressedFeedbackAndDoesNotClick()
        {
            CreateAdapter(false);
            adapter.Press(Sample("Left", 11, true), Vector3.zero, new HandClickContext("Left", 7, 11));
            Assert.That(button.transform.localScale.x, Is.EqualTo(0.96f).Within(0.0001f));
            Assert.IsTrue(pressed.enabled);

            adapter.Cancel(Sample("Left", 12, false), Vector3.zero);
            adapter.Release(Sample("Left", 13, false), Vector3.zero);

            Assert.AreEqual(0, clicks.Count);
            Assert.AreEqual(Vector3.one, button.transform.localScale);
            Assert.IsFalse(pressed.enabled);
        }

        [Test]
        public void ReleaseFromOtherHand_DoesNotConsumeOwnedPress()
        {
            CreateAdapter(false);
            adapter.Press(Sample("Left", 11, true), Vector3.zero, new HandClickContext("Left", 7, 11));
            adapter.Release(Sample("Right", 12, false), Vector3.zero);
            Assert.AreEqual(0, clicks.Count);
            Assert.IsTrue(pressed.enabled);

            adapter.Release(Sample("Left", 13, false), Vector3.zero);
            Assert.AreEqual(1, clicks.Count);
        }

        [Test]
        public void NonInteractableButton_RejectsReleaseAndRestoresScale()
        {
            CreateAdapter(false);
            adapter.Press(Sample("Left", 11, true), Vector3.zero, new HandClickContext("Left", 7, 11));
            button.interactable = false;

            adapter.Release(Sample("Left", 12, false), Vector3.zero);

            Assert.IsFalse(adapter.IsAvailable);
            Assert.AreEqual(0, clicks.Count);
            Assert.AreEqual(Vector3.one, button.transform.localScale);
            Assert.IsFalse(pressed.enabled);
        }

        [Test]
        public void InteractabilityRecovery_RestoresDefaultVisualColor()
        {
            CreateAdapter(false);
            Color originalColor = button.targetGraphic.color;
            button.interactable = false;
            Invoke(adapter, "Update");
            Assert.Less(button.targetGraphic.color.r, originalColor.r);

            button.interactable = true;
            Invoke(adapter, "Update");

            Assert.AreEqual(originalColor, button.targetGraphic.color);
            Assert.IsFalse(pressed.enabled);
        }

        [Test]
        public void DisabledAdapter_ClearsCaptureWithoutClick()
        {
            CreateAdapter(false);
            adapter.Press(Sample("Left", 11, true), Vector3.zero, new HandClickContext("Left", 7, 11));
            adapter.enabled = false;
            Invoke(adapter, "OnDisable");

            adapter.Release(Sample("Left", 12, false), Vector3.zero);

            Assert.AreEqual(0, clicks.Count);
            Assert.AreEqual(Vector3.one, button.transform.localScale);
            Assert.IsFalse(pressed.enabled);
        }

        [Test]
        public void TwoHandHover_OneExitKeepsOtherHandHighlighted()
        {
            CreateAdapter(false);
            adapter.HoverEnter(Sample("Left", 11, false), Vector3.zero);
            adapter.HoverEnter(Sample("Right", 11, false), Vector3.zero);

            adapter.HoverExit(Sample("Left", 12, false), Vector3.zero);
            Assert.IsTrue(hover.enabled);

            adapter.HoverExit(Sample("Right", 12, false), Vector3.zero);
            Assert.IsFalse(hover.enabled);
        }

        [Test]
        public void InputFieldPressAndCancel_KeepSelectionAndTextUnchanged()
        {
            CreateAdapter(true);
            inputField.text = "기존 답변";
            var previousSelection = new GameObject("previous selection");
            previousSelection.transform.SetParent(rig.transform, false);
            eventSystem.SetSelectedGameObject(previousSelection);

            adapter.Press(Sample("Left", 11, true), Vector3.zero, new HandClickContext("Left", 7, 11));
            Assert.AreSame(previousSelection, eventSystem.currentSelectedGameObject);
            Assert.IsTrue(pressed.enabled);
            adapter.Cancel(Sample("Left", 12, false), Vector3.zero);

            Assert.AreSame(previousSelection, eventSystem.currentSelectedGameObject);
            Assert.AreEqual("기존 답변", inputField.text);
            Assert.AreEqual(0, clicks.Count);
        }

        [Test]
        public void InputFieldValidRelease_SelectsWithoutClearingText()
        {
            CreateAdapter(true);
            inputField.text = "기존 답변";

            Click("Left", 11);

            Assert.AreSame(inputField.gameObject, eventSystem.currentSelectedGameObject);
            Assert.AreEqual("기존 답변", inputField.text);
            Assert.AreEqual(1, clicks.Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InputFieldSelectCallback_DisableAbortsSelectionAndActivation(bool reenable)
        {
            CreateAdapter(true);
            inputField.text = "기존 답변";
            inputField.shouldActivateOnSelect = true;
            int selections = 0;
            bool selectedUnderGuard = false;
            AddSelectHandler(inputField.gameObject, data =>
            {
                selections++;
                selectedUnderGuard = eventSystem.alreadySelecting;
                adapter.enabled = false;
                Invoke(adapter, "OnDisable");
                if (reenable)
                {
                    adapter.enabled = true;
                    Invoke(adapter, "OnEnable");
                }
            });
            adapter.Press(Sample("Left", 11, true), Vector3.zero, new HandClickContext("Left", 7, 11));

            Assert.DoesNotThrow(() => adapter.Release(Sample("Left", 12, false), Vector3.zero));

            Assert.AreEqual(1, selections, "The real EventSystem must dispatch EventTrigger.Select.");
            Assert.IsTrue(selectedUnderGuard);
            Assert.IsFalse(eventSystem.alreadySelecting);
            Assert.AreEqual(0, clicks.Count);
            FieldInfo activation = typeof(InputField).GetField("m_ShouldActivateNextUpdate", PrivateInstance);
            Assert.IsNotNull(activation);
            Assert.IsFalse((bool)activation.GetValue(inputField), "An aborted select must not leave keyboard activation queued.");
            Assert.IsFalse(inputField.isFocused);
            Assert.IsNull(eventSystem.currentSelectedGameObject);
            Assert.IsTrue(inputField.shouldActivateOnSelect, "Temporary native selection settings must be restored.");
            Assert.AreEqual("기존 답변", inputField.text);
            Assert.IsFalse(pressed.enabled);
        }

        [Test]
        public void InputFieldSelectCallback_DestroyTargetStopsNativeReleaseWithoutException()
        {
            CreateAdapter(true);
            GameObject originalTarget = inputField.gameObject;
            int selections = 0;
            AddSelectHandler(originalTarget, data =>
            {
                selections++;
                Object.DestroyImmediate(originalTarget);
            });
            adapter.Press(Sample("Left", 11, true), Vector3.zero, new HandClickContext("Left", 7, 11));

            Assert.DoesNotThrow(() => adapter.Release(Sample("Left", 12, false), Vector3.zero));

            Assert.AreEqual(1, selections);
            Assert.AreEqual(0, clicks.Count);
            Assert.IsTrue(originalTarget == null);
            Assert.IsFalse(eventSystem.alreadySelecting);
            Assert.IsTrue(eventSystem.currentSelectedGameObject == null);
        }

        [Test]
        public void InputFieldConfirmedRelease_PreservesAutomaticSelectionSettingAndQueuesActivation()
        {
            CreateAdapter(true);
            inputField.shouldActivateOnSelect = false;

            Click("Left", 11);

            Assert.AreEqual(1, clicks.Count);
            Assert.AreSame(inputField.gameObject, eventSystem.currentSelectedGameObject);
            Assert.IsFalse(inputField.shouldActivateOnSelect);
            FieldInfo activation = typeof(InputField).GetField("m_ShouldActivateNextUpdate", PrivateInstance);
            Assert.IsNotNull(activation);
            Assert.IsTrue((bool)activation.GetValue(inputField), "A confirmed hand click explicitly activates the input field.");
        }

        [Test]
        public void ButtonPressAndCancel_KeepSelectionUntilConfirmedRelease()
        {
            CreateAdapter(false);
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var previousSelection = new GameObject("previous selection");
            previousSelection.transform.SetParent(rig.transform, false);
            eventSystem.SetSelectedGameObject(previousSelection);

            adapter.Press(Sample("Left", 11, true), Vector3.zero, new HandClickContext("Left", 7, 11));
            Assert.AreSame(previousSelection, eventSystem.currentSelectedGameObject);
            adapter.Cancel(Sample("Left", 12, false), Vector3.zero);
            Assert.AreSame(previousSelection, eventSystem.currentSelectedGameObject);
            Assert.AreEqual(Navigation.Mode.Automatic, button.navigation.mode, "Native visual forwarding must restore navigation.");

            Click("Right", 21);

            Assert.AreSame(button.gameObject, eventSystem.currentSelectedGameObject);
            Assert.AreEqual(1, clicks.Count);
        }

        [Test]
        public void PanelReenable_RecordsOneDistinctConfirmationPerButton()
        {
            HandButtonInteractable a = CreateAdapter(false);
            HandButtonInteractable b = CreateAdapter(false);
            HandButtonInteractable c = CreateAdapter(false);
            var panelObject = new GameObject("test panel");
            panelObject.SetActive(false);
            panelObject.transform.SetParent(rig.transform, false);
            HandUiTestPanel panel = panelObject.AddComponent<HandUiTestPanel>();
            var labelObject = new GameObject("result", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(panelObject.transform, false);
            Text label = labelObject.GetComponent<Text>();
            SetField(panel, "testA", a);
            SetField(panel, "testB", b);
            SetField(panel, "testC", c);
            SetField(panel, "resultLabel", label);
            panelObject.SetActive(true);
            Invoke(panel, "OnEnable");
            Invoke(panel, "OnDisable");
            Invoke(panel, "OnEnable");

            adapter = a;
            Click("Left", 11);
            Assert.AreEqual(1, panel.TestACount);
            Assert.AreEqual("테스트 A 확인 · 1회", label.text);
            adapter = b;
            Click("Right", 21);
            Assert.AreEqual(1, panel.TestBCount);
            Assert.AreEqual("테스트 B 확인 · 1회", label.text);
            adapter = c;
            Click("Left", 31);
            Assert.AreEqual(1, panel.TestCCount);
            Assert.AreEqual("테스트 C 확인 · 1회", label.text);
        }

        private HandButtonInteractable CreateAdapter(bool useInputField)
        {
            var target = new GameObject("hand target", typeof(RectTransform), typeof(Image));
            target.SetActive(false);
            target.transform.SetParent(rig.transform, false);
            Image background = target.GetComponent<Image>();
            background.color = new Color(0.25f, 0.3f, 0.4f, 1f);
            Selectable selectable;
            if (useInputField)
            {
                inputField = target.AddComponent<InputField>();
                var textObject = new GameObject("input text", typeof(RectTransform), typeof(Text));
                textObject.transform.SetParent(target.transform, false);
                Text text = textObject.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                inputField.textComponent = text;
                selectable = inputField;
            }
            else
            {
                button = target.AddComponent<Button>();
                selectable = button;
            }
            selectable.targetGraphic = background;
            selectable.transition = Selectable.Transition.None;
            selectable.navigation = new Navigation { mode = Navigation.Mode.None };
            hover = CreateFeedback(target.transform, "hover");
            pressed = CreateFeedback(target.transform, "pressed");
            adapter = target.AddComponent<HandButtonInteractable>();
            SetField(adapter, useInputField ? "targetInputField" : "targetButton", selectable);
            SetField(adapter, "hoverGraphic", hover);
            SetField(adapter, "pressedGraphic", pressed);
            SetField(adapter, "eventSystem", eventSystem);
            target.SetActive(true);
            Invoke(adapter, "Awake");
            adapter.OnHandClick += clicks.Add;
            return adapter;
        }

        private Image CreateFeedback(Transform parent, string name)
        {
            var feedback = new GameObject(name, typeof(RectTransform), typeof(Image));
            feedback.transform.SetParent(parent, false);
            Image graphic = feedback.GetComponent<Image>();
            graphic.raycastTarget = false;
            graphic.enabled = false;
            return graphic;
        }

        private static void AddSelectHandler(GameObject target, UnityEngine.Events.UnityAction<BaseEventData> callback)
        {
            EventTrigger trigger = target.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            entry.callback.AddListener(callback);
            trigger.triggers.Add(entry);
        }

        private void Click(string hand, ulong sampleId)
        {
            adapter.Press(Sample(hand, sampleId, true), Vector3.zero, new HandClickContext(hand, 7, sampleId));
            adapter.Release(Sample(hand, sampleId + 1, false), Vector3.zero);
        }

        private static HandInputSample Sample(string hand, ulong sampleId, bool pinched)
        {
            return new HandInputSample(hand, new Vector2(50f, 50f), (uint)sampleId, sampleId, 0f, true, pinched);
        }

        private static void SetField(object instance, string name, object value)
        {
            FieldInfo field = instance.GetType().GetField(name, PrivateInstance);
            Assert.IsNotNull(field, "Required explicit Inspector field: " + name);
            field.SetValue(instance, value);
        }

        private static void Invoke(object instance, string method)
        {
            MethodInfo callback = instance.GetType().GetMethod(method, PrivateInstance);
            Assert.IsNotNull(callback, method);
            callback.Invoke(instance, null);
        }
    }
}

namespace CameraCoop.Tests
{
    [NUnit.Framework.TestFixture]
    public class HandSampleTests
    {
        private const System.Reflection.BindingFlags SamplePrivate = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        private UnityEngine.GameObject sampleRig;
        private HandCursorController sampleController;
        private UnityEngine.RectTransform sampleLeftCursor;
        private UnityEngine.RectTransform sampleRightCursor;
        private readonly System.Collections.Generic.List<HandInputSample> samples = new System.Collections.Generic.List<HandInputSample>();
        private readonly System.Collections.Generic.List<string> legacyEvents = new System.Collections.Generic.List<string>();

        [NUnit.Framework.SetUp]
        public void SetUpSampleRig()
        {
            sampleRig = new UnityEngine.GameObject("hand sample test");
            sampleRig.SetActive(false);
            sampleLeftCursor = CreateSampleCursor("left sample cursor");
            sampleRightCursor = CreateSampleCursor("right sample cursor");
            sampleController = sampleRig.AddComponent<HandCursorController>();
            var serialized = new UnityEditor.SerializedObject(sampleController);
            serialized.FindProperty("receiver").objectReferenceValue = sampleRig.AddComponent<UdpHandReceiver>();
            serialized.FindProperty("leftCursor").objectReferenceValue = sampleLeftCursor;
            serialized.FindProperty("rightCursor").objectReferenceValue = sampleRightCursor;
            serialized.FindProperty("fadeDuration").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            typeof(HandCursorController).GetMethod("Awake", SamplePrivate).Invoke(sampleController, null);
            samples.Clear();
            legacyEvents.Clear();
            sampleController.OnHandSample += samples.Add;
            sampleController.OnPinchStart += (hand, position) => legacyEvents.Add("start:" + hand);
            sampleController.OnPinchMove += (hand, position) => legacyEvents.Add("move:" + hand);
            sampleController.OnPinchEnd += hand => legacyEvents.Add("end:" + hand);
        }

        [NUnit.Framework.TearDown]
        public void TearDownSampleRig()
        {
            UnityEngine.Object.DestroyImmediate(sampleRig);
        }

        [NUnit.Framework.Test]
        public void AcceptedPacket_EmitsLeftThenRightWithOneSharedLocalId()
        {
            var packet = SamplePacket(77u, SampleHand("Right"), SampleHand("Left"));
            sampleController.ProcessPacket(packet, false, 0.0125f, 1280, 720);

            NUnit.Framework.Assert.That(samples.Count, NUnit.Framework.Is.EqualTo(2));
            NUnit.Framework.Assert.That(samples[0].handedness, NUnit.Framework.Is.EqualTo("Left"));
            NUnit.Framework.Assert.That(samples[1].handedness, NUnit.Framework.Is.EqualTo("Right"));
            NUnit.Framework.Assert.That(samples[0].sampleId, NUnit.Framework.Is.GreaterThan(0UL));
            NUnit.Framework.Assert.That(samples[1].sampleId, NUnit.Framework.Is.EqualTo(samples[0].sampleId));
            NUnit.Framework.Assert.That(samples[0].sequence, NUnit.Framework.Is.EqualTo(77u));
            NUnit.Framework.Assert.That(samples[0].sampleAgeSeconds, NUnit.Framework.Is.EqualTo(0.0125f));
            NUnit.Framework.Assert.That(samples[0].screenPosition, NUnit.Framework.Is.EqualTo(new UnityEngine.Vector2(640f, 360f)));
            NUnit.Framework.Assert.That(samples[0].isTracked, NUnit.Framework.Is.True);
            NUnit.Framework.Assert.That(samples[0].cancelReason, NUnit.Framework.Is.EqualTo(HandCancelReason.None));
        }

        [NUnit.Framework.Test]
        public void SamePacketReference_DoesNotRepeatSamples_AndKeepsLegacyMoveCadence()
        {
            var packet = SamplePacket(7u, SampleHand("Left", 0.1f), SampleHand("Right"));
            sampleController.ProcessPacket(packet, false, 0f, 1920, 1080);
            sampleController.ProcessPacket(packet, false, 0.016f, 1920, 1080);
            sampleController.ProcessPacket(packet, false, 0.032f, 1920, 1080);

            NUnit.Framework.Assert.That(samples.Count, NUnit.Framework.Is.EqualTo(2));
            NUnit.Framework.CollectionAssert.AreEqual(new[] { "start:Left", "move:Left", "move:Left" }, legacyEvents);
        }

        [NUnit.Framework.Test]
        public void AcceptedReferences_AdvanceLocalIdWhenRawSequenceRepeatsOrRestarts()
        {
            sampleController.ProcessPacket(SamplePacket(77u, SampleHand("Left")), false, 0f, 1920, 1080);
            sampleController.ProcessPacket(SamplePacket(77u, SampleHand("Left")), false, 0f, 1920, 1080);
            sampleController.ProcessPacket(SamplePacket(0u, SampleHand("Left")), false, 0f, 1920, 1080);

            var left = SamplesFor("Left");
            NUnit.Framework.Assert.That(left.Count, NUnit.Framework.Is.EqualTo(3));
            NUnit.Framework.Assert.That(left[1].sampleId, NUnit.Framework.Is.GreaterThan(left[0].sampleId));
            NUnit.Framework.Assert.That(left[2].sampleId, NUnit.Framework.Is.GreaterThan(left[1].sampleId));
            NUnit.Framework.Assert.That(left[1].sequence, NUnit.Framework.Is.EqualTo(77u));
            NUnit.Framework.Assert.That(left[2].sequence, NUnit.Framework.Is.EqualTo(0u));
        }

        [NUnit.Framework.Test]
        public void OmittedHand_EmitsTrackingLostOnceUntilItReturns()
        {
            sampleController.ProcessPacket(SamplePacket(1u, SampleHand("Left", 0.1f), SampleHand("Right")), false, 0f, 1920, 1080);
            samples.Clear();
            legacyEvents.Clear();
            sampleController.ProcessPacket(SamplePacket(2u, SampleHand("Right")), false, 0f, 1920, 1080);
            sampleController.ProcessPacket(SamplePacket(3u, SampleHand("Right")), false, 0f, 1920, 1080);

            var left = SamplesFor("Left");
            NUnit.Framework.Assert.That(left.Count, NUnit.Framework.Is.EqualTo(1));
            NUnit.Framework.Assert.That(left[0].isTracked, NUnit.Framework.Is.False);
            NUnit.Framework.Assert.That(left[0].isPinched, NUnit.Framework.Is.False);
            NUnit.Framework.Assert.That(left[0].cancelReason, NUnit.Framework.Is.EqualTo(HandCancelReason.TrackingLost));
            NUnit.Framework.CollectionAssert.AreEqual(new[] { "end:Left" }, legacyEvents);

            sampleController.ProcessPacket(SamplePacket(4u, SampleHand("Left"), SampleHand("Right")), false, 0f, 1920, 1080);
            sampleController.ProcessPacket(SamplePacket(5u, SampleHand("Right")), false, 0f, 1920, 1080);
            left = SamplesFor("Left");
            NUnit.Framework.Assert.That(left.Count, NUnit.Framework.Is.EqualTo(3));
            NUnit.Framework.Assert.That(left[1].isTracked, NUnit.Framework.Is.True);
            NUnit.Framework.Assert.That(left[2].cancelReason, NUnit.Framework.Is.EqualTo(HandCancelReason.TrackingLost));
        }

        [NUnit.Framework.Test]
        public void ServerLost_EmitsOneCancellationPerHandWithoutInventingAcceptance()
        {
            var packet = SamplePacket(31u, SampleHand("Left", 0.1f), SampleHand("Right", 0.1f));
            sampleController.ProcessPacket(packet, false, 0f, 1920, 1080);
            ulong acceptedId = samples[0].sampleId;
            samples.Clear();
            legacyEvents.Clear();
            sampleController.ProcessPacket(packet, true, 0.5f, 1920, 1080);
            sampleController.ProcessPacket(packet, true, 0.6f, 1920, 1080);

            NUnit.Framework.Assert.That(samples.Count, NUnit.Framework.Is.EqualTo(2));
            NUnit.Framework.Assert.That(samples[0].handedness, NUnit.Framework.Is.EqualTo("Left"));
            NUnit.Framework.Assert.That(samples[1].handedness, NUnit.Framework.Is.EqualTo("Right"));
            foreach (HandInputSample sample in samples)
            {
                NUnit.Framework.Assert.That(sample.cancelReason, NUnit.Framework.Is.EqualTo(HandCancelReason.TrackingLost));
                NUnit.Framework.Assert.That(sample.sampleId, NUnit.Framework.Is.EqualTo(acceptedId));
                NUnit.Framework.Assert.That(sample.sequence, NUnit.Framework.Is.EqualTo(31u));
                NUnit.Framework.Assert.That(sample.isTracked || sample.isPinched, NUnit.Framework.Is.False);
            }
            NUnit.Framework.CollectionAssert.AreEqual(new[] { "end:Left", "end:Right" }, legacyEvents);
        }

        [NUnit.Framework.Test]
        public void Disable_EmitsComponentDisabledAndLegacyEndExactlyOnce()
        {
            sampleController.ProcessPacket(SamplePacket(41u, SampleHand("Left", 0.1f), SampleHand("Right", 0.1f)), false, 0f, 1920, 1080);
            ulong acceptedId = samples[0].sampleId;
            samples.Clear();
            legacyEvents.Clear();
            var disable = typeof(HandCursorController).GetMethod("OnDisable", SamplePrivate);
            disable.Invoke(sampleController, null);
            disable.Invoke(sampleController, null);

            NUnit.Framework.Assert.That(samples.Count, NUnit.Framework.Is.EqualTo(2));
            foreach (HandInputSample sample in samples)
            {
                NUnit.Framework.Assert.That(sample.cancelReason, NUnit.Framework.Is.EqualTo(HandCancelReason.ComponentDisabled));
                NUnit.Framework.Assert.That(sample.sampleId, NUnit.Framework.Is.EqualTo(acceptedId));
                NUnit.Framework.Assert.That(sample.isTracked || sample.isPinched, NUnit.Framework.Is.False);
            }
            NUnit.Framework.CollectionAssert.AreEqual(new[] { "end:Left", "end:Right" }, legacyEvents);
        }

        [NUnit.Framework.TestCase("null-landmarks")]
        [NUnit.Framework.TestCase("short-landmarks")]
        [NUnit.Framework.TestCase("long-landmarks")]
        [NUnit.Framework.TestCase("landmark-nan")]
        [NUnit.Framework.TestCase("landmark-positive-infinity")]
        [NUnit.Framework.TestCase("landmark-negative-infinity")]
        [NUnit.Framework.TestCase("pinch-nan")]
        [NUnit.Framework.TestCase("pinch-infinity")]
        [NUnit.Framework.TestCase("pinch-negative")]
        [NUnit.Framework.TestCase("zero-palm")]
        [NUnit.Framework.TestCase("tiny-palm")]
        [NUnit.Framework.TestCase("palm-left")]
        [NUnit.Framework.TestCase("palm-right")]
        [NUnit.Framework.TestCase("palm-above")]
        [NUnit.Framework.TestCase("palm-below")]
        public void InvalidHand_CancelsWithoutMovingCursorOrClicking(string invalidity)
        {
            sampleController.ProcessPacket(SamplePacket(1u, SampleHand("Left", 0.1f), SampleHand("Right")), false, 0f, 1920, 1080);
            UnityEngine.Vector3 previousPosition = sampleLeftCursor.position;
            samples.Clear();
            legacyEvents.Clear();
            HandData invalid = InvalidSampleHand(invalidity);
            sampleController.ProcessPacket(SamplePacket(2u, invalid, SampleHand("Right")), false, 0f, 1920, 1080);

            var left = SamplesFor("Left");
            NUnit.Framework.Assert.That(left.Count, NUnit.Framework.Is.EqualTo(1));
            NUnit.Framework.Assert.That(left[0].cancelReason, NUnit.Framework.Is.EqualTo(HandCancelReason.InvalidSample));
            NUnit.Framework.Assert.That(left[0].isTracked || left[0].isPinched, NUnit.Framework.Is.False);
            NUnit.Framework.Assert.That(sampleLeftCursor.position, NUnit.Framework.Is.EqualTo(previousPosition));
            NUnit.Framework.CollectionAssert.AreEqual(new[] { "end:Left" }, legacyEvents);
        }

        [NUnit.Framework.TestCase(-2f)]
        [NUnit.Framework.TestCase(2f)]
        public void OffscreenFingertip_DoesNotRejectValidPalm(float fingertipCoordinate)
        {
            HandData hand = SampleHand("Left", 0.1f);
            hand.landmarks[8 * 3] = fingertipCoordinate;
            hand.landmarks[8 * 3 + 1] = fingertipCoordinate;
            sampleController.ProcessPacket(SamplePacket(1u, hand), false, 0f, 1920, 1080);

            var left = SamplesFor("Left");
            NUnit.Framework.Assert.That(left.Count, NUnit.Framework.Is.EqualTo(1));
            NUnit.Framework.Assert.That(left[0].isTracked, NUnit.Framework.Is.True);
            NUnit.Framework.Assert.That(left[0].isPinched, NUnit.Framework.Is.True);
            NUnit.Framework.Assert.That(left[0].screenPosition, NUnit.Framework.Is.EqualTo(new UnityEngine.Vector2(960f, 540f)));
        }

        [NUnit.Framework.TestCase(0f, 0f)]
        [NUnit.Framework.TestCase(1f, 1f)]
        public void PalmOnScreenBoundary_IsTracked(float x, float y)
        {
            HandData hand = SampleHand("Left", 0.1f, x, y);
            sampleController.ProcessPacket(SamplePacket(1u, hand), false, 0f, 1920, 1080);

            var left = SamplesFor("Left");
            NUnit.Framework.Assert.That(left.Count, NUnit.Framework.Is.EqualTo(1));
            NUnit.Framework.Assert.That(left[0].isTracked, NUnit.Framework.Is.True);
            NUnit.Framework.Assert.That(left[0].screenPosition, NUnit.Framework.Is.EqualTo(new UnityEngine.Vector2(x * 1920f, (1f - y) * 1080f)));
        }

        [NUnit.Framework.Test]
        public void NewSamples_PreserveStrictPinchHysteresis()
        {
            float[] pinch = { 0.30f, 0.29f, 0.35f, 0.40f, 0.41f };
            for (int i = 0; i < pinch.Length; i++)
            {
                sampleController.ProcessPacket(SamplePacket((uint)i, SampleHand("Left", pinch[i])), false, 0f, 1920, 1080);
            }

            var left = SamplesFor("Left");
            NUnit.Framework.Assert.That(left.Count, NUnit.Framework.Is.EqualTo(5));
            bool[] observed = new bool[left.Count];
            for (int i = 0; i < left.Count; i++) observed[i] = left[i].isPinched;
            NUnit.Framework.CollectionAssert.AreEqual(new[] { false, true, true, true, false }, observed);
            NUnit.Framework.CollectionAssert.AreEqual(new[] { "start:Left", "move:Left", "move:Left", "end:Left" }, legacyEvents);
        }

        [NUnit.Framework.Test]
        public void StaleDuplicate_FadesVisualWithoutPublishingAnotherSampleOrEndingLegacyPinch()
        {
            var packet = SamplePacket(1u, SampleHand("Left", 0.1f), SampleHand("Right"));
            sampleController.ProcessPacket(packet, false, 0.19f, 1920, 1080);
            NUnit.Framework.Assert.That(sampleLeftCursor.GetComponent<UnityEngine.CanvasGroup>().alpha, NUnit.Framework.Is.EqualTo(1f));
            sampleController.ProcessPacket(packet, false, 0.21f, 1920, 1080);

            NUnit.Framework.Assert.That(sampleLeftCursor.GetComponent<UnityEngine.CanvasGroup>().alpha, NUnit.Framework.Is.EqualTo(0f));
            NUnit.Framework.Assert.That(samples.Count, NUnit.Framework.Is.EqualTo(2));
            NUnit.Framework.CollectionAssert.AreEqual(new[] { "start:Left", "move:Left" }, legacyEvents);
        }

        [NUnit.Framework.Test]
        public void HoverFeedback_AccentsOnlyAssignedHandAndRestoresPinchAppearance()
        {
            sampleController.ProcessPacket(SamplePacket(1u, SampleHand("Left"), SampleHand("Right")), false, 0f, 1920, 1080);
            var leftImage = sampleLeftCursor.GetComponent<UnityEngine.UI.Image>();
            var rightImage = sampleRightCursor.GetComponent<UnityEngine.UI.Image>();
            UnityEngine.Color baseLeft = leftImage.color;
            UnityEngine.Color baseRight = rightImage.color;
            sampleController.SetHoverFeedback("Left", true);
            NUnit.Framework.Assert.That(leftImage.color, NUnit.Framework.Is.Not.EqualTo(baseLeft));
            NUnit.Framework.Assert.That(rightImage.color, NUnit.Framework.Is.EqualTo(baseRight));
            sampleController.SetHoverFeedback("Left", false);
            NUnit.Framework.Assert.That(leftImage.color, NUnit.Framework.Is.EqualTo(baseLeft));

            sampleController.ProcessPacket(SamplePacket(2u, SampleHand("Left", 0.1f), SampleHand("Right")), false, 0f, 1920, 1080);
            UnityEngine.Color pinched = leftImage.color;
            sampleController.SetHoverFeedback("Left", true);
            sampleController.SetHoverFeedback("Left", false);
            NUnit.Framework.Assert.That(leftImage.color, NUnit.Framework.Is.EqualTo(pinched));
        }

        [NUnit.Framework.Test]
        public void MissingReceiver_LogsAndDisablesInsteadOfReportingTrackingLoss()
        {
            var serialized = new UnityEditor.SerializedObject(sampleController);
            serialized.FindProperty("receiver").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                "[HandCursorController] Assign receiver before enabling hand input.");

            NUnit.Framework.Assert.DoesNotThrow(() => typeof(HandCursorController).GetMethod("Awake", SamplePrivate).Invoke(sampleController, null));

            NUnit.Framework.Assert.That(sampleController.enabled, NUnit.Framework.Is.False);
        }

        [NUnit.Framework.Test]
        public void MissingCursorReferences_LogsAndDisablesInsteadOfThrowing()
        {
            var incomplete = new UnityEngine.GameObject("missing cursor references");
            incomplete.SetActive(false);
            try
            {
                var controller = incomplete.AddComponent<HandCursorController>();
                var serialized = new UnityEditor.SerializedObject(controller);
                serialized.FindProperty("receiver").objectReferenceValue = incomplete.AddComponent<UdpHandReceiver>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error,
                    "[HandCursorController] Assign leftCursor and rightCursor with CanvasGroup and Image before enabling hand input.");
                NUnit.Framework.Assert.DoesNotThrow(() => typeof(HandCursorController).GetMethod("Awake", SamplePrivate).Invoke(controller, null));
                NUnit.Framework.Assert.That(controller.enabled, NUnit.Framework.Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(incomplete);
            }
        }

        private UnityEngine.RectTransform CreateSampleCursor(string name)
        {
            var cursor = new UnityEngine.GameObject(name, typeof(UnityEngine.RectTransform), typeof(UnityEngine.CanvasGroup), typeof(UnityEngine.UI.Image));
            cursor.transform.SetParent(sampleRig.transform);
            return (UnityEngine.RectTransform)cursor.transform;
        }

        private System.Collections.Generic.List<HandInputSample> SamplesFor(string handedness)
        {
            var selected = new System.Collections.Generic.List<HandInputSample>();
            foreach (HandInputSample sample in samples)
            {
                if (sample.handedness == handedness) selected.Add(sample);
            }
            return selected;
        }

        private static HandPacket SamplePacket(uint sequence, params HandData[] hands)
        {
            return new HandPacket { v = 1, seq = sequence, hands = hands };
        }

        private static HandData SampleHand(string handedness, float pinch = 0.7f, float centerX = 0.5f, float centerY = 0.5f)
        {
            var hand = new HandData { handedness = handedness, pinch = pinch, landmarks = new float[63] };
            for (int i = 0; i < 21; i++)
            {
                hand.landmarks[i * 3] = centerX;
                hand.landmarks[i * 3 + 1] = centerY;
            }
            hand.landmarks[1] = centerY + 0.25f;
            hand.landmarks[9 * 3 + 1] = centerY - 0.25f;
            return hand;
        }

        private static HandData InvalidSampleHand(string invalidity)
        {
            HandData hand = SampleHand("Left", 0.1f);
            switch (invalidity)
            {
                case "null-landmarks": hand.landmarks = null; break;
                case "short-landmarks": hand.landmarks = new float[62]; break;
                case "long-landmarks": hand.landmarks = new float[64]; break;
                case "landmark-nan": hand.landmarks[60] = float.NaN; break;
                case "landmark-positive-infinity": hand.landmarks[61] = float.PositiveInfinity; break;
                case "landmark-negative-infinity": hand.landmarks[62] = float.NegativeInfinity; break;
                case "pinch-nan": hand.pinch = float.NaN; break;
                case "pinch-infinity": hand.pinch = float.PositiveInfinity; break;
                case "pinch-negative": hand.pinch = -0.01f; break;
                case "zero-palm": hand.landmarks[28] = hand.landmarks[1]; break;
                case "tiny-palm": hand.landmarks[28] = hand.landmarks[1] - 0.0000005f; break;
                case "palm-left": return SampleHand("Left", 0.1f, -0.1f, 0.5f);
                case "palm-right": return SampleHand("Left", 0.1f, 1.1f, 0.5f);
                case "palm-above": return SampleHand("Left", 0.1f, 0.5f, -0.1f);
                case "palm-below": return SampleHand("Left", 0.1f, 0.5f, 1.1f);
                default: throw new System.ArgumentOutOfRangeException(nameof(invalidity), invalidity, "Unknown invalid sample case");
            }
            return hand;
        }
    }
}
