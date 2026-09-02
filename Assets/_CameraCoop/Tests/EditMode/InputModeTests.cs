using System;
using System.Collections.Generic;
using System.Reflection;
using CameraCoop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CameraCoop.Tests
{
    public class InputModeTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject rig;
        private GameObject eventSystemRoot;
        private InputModeManager manager;
        private readonly List<InputMode> modeChanges = new List<InputMode>();

        [SetUp]
        public void SetUp()
        {
            InputFocus.IsTyping = false;
            modeChanges.Clear();
            rig = new GameObject("input mode test");
            rig.SetActive(false);
            manager = rig.AddComponent<InputModeManager>();
            InvokeCallback("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            InputFocus.IsTyping = false;
            if (rig != null)
            {
                Object.DestroyImmediate(rig);
            }
            if (eventSystemRoot != null)
            {
                EventSystem eventSystem = eventSystemRoot.GetComponent<EventSystem>();
                MethodInfo onDisable = typeof(EventSystem).GetMethod("OnDisable", InstanceFlags);
                Assert.IsNotNull(onDisable, "EventSystem teardown must clear its registered current instance.");
                onDisable.Invoke(eventSystem, null);
                Object.DestroyImmediate(eventSystemRoot);
            }
        }

        [TestCase(InputContext.Explore, InputMode.Move, InputMode.Move, true, true, false, false, true)]
        [TestCase(InputContext.Explore, InputMode.Interact, InputMode.Interact, false, false, true, false, true)]
        [TestCase(InputContext.UiOnly, InputMode.Move, InputMode.Interact, false, false, true, false, false)]
        [TestCase(InputContext.Drawing, InputMode.Move, InputMode.Interact, false, false, true, true, false)]
        [TestCase(InputContext.Blocked, InputMode.Move, InputMode.Interact, false, false, false, false, false)]
        public void ContextAndMode_ExposePermissionTable(InputContext context, InputMode requestedMode, InputMode effectiveMode,
            bool move, bool look, bool handUi, bool draw, bool toggle)
        {
            manager.SetContext(context);
            if (context == InputContext.Explore)
            {
                Assert.IsTrue(manager.RequestMode(requestedMode));
            }

            Assert.AreEqual(context, manager.CurrentContext);
            Assert.AreEqual(effectiveMode, manager.CurrentMode);
            AssertPermissions(move, look, handUi, draw, toggle);
        }

        [Test]
        public void InitialState_StartsInExploreMoveWithHiddenLockedCursorTarget()
        {
            Assert.AreEqual(InputContext.Explore, manager.CurrentContext);
            Assert.AreEqual(InputMode.Move, manager.CurrentMode);
            AssertPermissions(true, true, false, false, true);
            Assert.AreEqual(CursorLockMode.Locked, manager.DesiredCursorLockState);
            Assert.AreEqual(false, manager.DesiredCursorVisible);
        }

        [TestCase(InputContext.UiOnly)]
        [TestCase(InputContext.Drawing)]
        [TestCase(InputContext.Blocked)]
        public void ForcedContext_RejectsRequestsAndResetsMoveWhenReturningToExplore(InputContext context)
        {
            manager.SetContext(context);

            Assert.IsFalse(manager.RequestMode(InputMode.Move));
            Assert.IsFalse(manager.RequestMode(InputMode.Interact));
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            manager.SetContext(InputContext.Explore);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode, "Returning to Explore must not restore stale Move intent.");
            AssertPermissions(false, false, true, false, true);
            Assert.IsTrue(manager.RequestMode(InputMode.Move));
            AssertPermissions(true, true, false, false, true);
        }

        [Test]
        public void TypingInMove_BlocksMovementLookDrawAndModeRequests()
        {
            InputFocus.IsTyping = true;

            AssertPermissions(false, false, false, false, false);
            Assert.IsFalse(manager.RequestMode(InputMode.Interact));
            manager.ProcessInput(true, CursorLockMode.Locked);
            Assert.AreEqual(InputMode.Move, manager.CurrentMode);

            InputFocus.IsTyping = false;
            AssertPermissions(true, true, false, false, true);
        }

        [Test]
        public void TypingInInteract_StillAllowsHandUiSubmission()
        {
            Assert.IsTrue(manager.RequestMode(InputMode.Interact));
            InputFocus.IsTyping = true;

            AssertPermissions(false, false, true, false, false);
            Assert.IsFalse(manager.RequestMode(InputMode.Move));
            manager.ProcessInput(true, CursorLockMode.None);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
        }

        [Test]
        public void CameraMouse_IsBlockedWhenEventSystemSelectsAnInputField()
        {
            manager.SetCameraControlState(true, false);
            Assert.IsTrue(manager.RequestMode(InputMode.Interact));
            Assert.IsTrue(manager.CanUseCameraMouse);

            eventSystemRoot = new GameObject("input mode event system", typeof(EventSystem), typeof(StandaloneInputModule));
            GameObject inputRoot = new GameObject("focused input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
            inputRoot.transform.SetParent(eventSystemRoot.transform, false);
            EventSystem eventSystem = eventSystemRoot.GetComponent<EventSystem>();
            MethodInfo onEnable = typeof(EventSystem).GetMethod("OnEnable", InstanceFlags);
            Assert.IsNotNull(onEnable, "EventSystem must initialize its current instance for the selected input test.");
            onEnable.Invoke(eventSystem, null);
            EventSystem.current = eventSystem;
            Assert.AreSame(eventSystem, EventSystem.current);
            eventSystem.SetSelectedGameObject(inputRoot);

            Assert.IsFalse(manager.CanUseCameraMouse);
        }

        [Test]
        public void TypingInDrawing_BlocksDrawingWithoutBlockingHandUi()
        {
            manager.SetContext(InputContext.Drawing);
            InputFocus.IsTyping = true;

            AssertPermissions(false, false, true, false, false);

            InputFocus.IsTyping = false;
            AssertPermissions(false, false, true, true, false);
        }

        [Test]
        public void CarriedPersonalCanvas_AllowsMoveAndLookWhileKeepingFistAndWorldHandActions()
        {
            manager.SetContext(InputContext.Drawing);
            manager.SetDrawingMovementAllowed(true);

            Assert.That(manager.CurrentMode, Is.EqualTo(InputMode.Interact));
            Assert.That(manager.CanMove, Is.True);
            Assert.That(manager.CanLook, Is.True);
            Assert.That(manager.CanUseHandUi, Is.True);
            Assert.That(manager.CanDraw, Is.True);

            InputFocus.IsTyping = true;
            Assert.That(manager.CanMove, Is.False);
            Assert.That(manager.CanLook, Is.False);
            Assert.That(manager.CanDraw, Is.False);
            Assert.That(manager.CanUseHandUi, Is.True);
        }

        [Test]
        public void LobbyExplore_OnlyInteractModeAllowsPracticeDrawing()
        {
            manager.SetContext(InputContext.Explore);
            manager.SetPracticeDrawingAllowed(true);

            Assert.That(manager.CurrentMode, Is.EqualTo(InputMode.Move));
            Assert.That(manager.CanDraw, Is.False);
            Assert.That(manager.RequestMode(InputMode.Interact), Is.True);
            Assert.That(manager.CanDraw, Is.True);
            Assert.That(manager.RequestMode(InputMode.Move), Is.True);
            Assert.That(manager.CanDraw, Is.False);
        }

        [Test]
        public void DrawingMovement_RemainsBlockedDuringCameraPreparationAndAfterCanvasDock()
        {
            manager.SetContext(InputContext.Drawing);
            manager.SetDrawingMovementAllowed(true);
            manager.SetCameraControlState(true, true);
            Assert.That(manager.CanMove, Is.False);
            Assert.That(manager.CanLook, Is.False);

            manager.SetCameraControlState(true, false);
            manager.SetDrawingMovementAllowed(false);
            Assert.That(manager.CanMove, Is.False);
            Assert.That(manager.CanLook, Is.False);
            Assert.That(manager.CanDraw, Is.True);
        }

        [Test]
        public void FocusLoss_BlocksEveryPermissionAndRejectsRequests()
        {
            InvokeCallback("OnApplicationFocus", false);

            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            AssertPermissions(false, false, false, false, false);
            Assert.AreEqual(CursorLockMode.None, manager.DesiredCursorLockState);
            Assert.AreEqual(true, manager.DesiredCursorVisible);
            Assert.IsFalse(manager.RequestMode(InputMode.Move));
            Assert.IsFalse(manager.RequestMode(InputMode.Interact));
            manager.ProcessInput(true, CursorLockMode.Locked);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
        }

        [Test]
        public void FocusRecovery_StaysInSafeInteractUntilMoveIsExplicitlyRequested()
        {
            InvokeCallback("OnApplicationFocus", false);
            InvokeCallback("OnApplicationFocus", true);

            Assert.AreEqual(InputContext.Explore, manager.CurrentContext);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            AssertPermissions(false, false, true, false, true);
            Assert.AreEqual(CursorLockMode.None, manager.DesiredCursorLockState);
            Assert.AreEqual(false, manager.DesiredCursorVisible);
            Assert.IsTrue(manager.RequestMode(InputMode.Move));
            Assert.AreEqual(CursorLockMode.Locked, manager.DesiredCursorLockState);
            Assert.AreEqual(false, manager.DesiredCursorVisible);
        }

        [Test]
        public void FocusLoss_TakesPriorityOverDrawingAndTyping()
        {
            manager.SetContext(InputContext.Drawing);
            InputFocus.IsTyping = true;
            InvokeCallback("OnApplicationFocus", false);

            AssertPermissions(false, false, false, false, false);
            Assert.AreEqual(InputContext.Drawing, manager.CurrentContext);

            InvokeCallback("OnApplicationFocus", true);
            AssertPermissions(false, false, true, false, false);
            InputFocus.IsTyping = false;
            AssertPermissions(false, false, true, true, false);
        }

        [Test]
        public void Tab_TogglesBothExploreModes()
        {
            manager.ProcessInput(true, CursorLockMode.Locked);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            Assert.AreEqual(CursorLockMode.None, manager.DesiredCursorLockState);
            Assert.AreEqual(false, manager.DesiredCursorVisible);

            manager.ProcessInput(true, CursorLockMode.None);
            Assert.AreEqual(InputMode.Move, manager.CurrentMode);
            AssertPermissions(true, true, false, false, true);
        }

        [TestCase(InputContext.UiOnly)]
        [TestCase(InputContext.Drawing)]
        [TestCase(InputContext.Blocked)]
        public void Tab_DoesNotLeaveForcedInteract(InputContext context)
        {
            manager.SetContext(context);
            manager.ProcessInput(true, CursorLockMode.None);

            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            Assert.AreEqual(context, manager.CurrentContext);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnexpectedCursorUnlock_EntersSafeInteractBeforeProcessingTab(bool tabPressed)
        {
            manager.ProcessInput(tabPressed, CursorLockMode.None);

            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            AssertPermissions(false, false, true, false, true);
            Assert.AreEqual(CursorLockMode.None, manager.DesiredCursorLockState);
            Assert.AreEqual(false, manager.DesiredCursorVisible);
        }

        [Test]
        public void ExternalCursorLock_DoesNotSelectMove()
        {
            Assert.IsTrue(manager.RequestMode(InputMode.Interact));
            manager.ProcessInput(false, CursorLockMode.Locked);

            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            AssertPermissions(false, false, true, false, true);
        }

        [Test]
        public void ModeChanged_NotifiesContextAndFocusBoundariesEvenWhenModeStaysInteract()
        {
            manager.OnModeChanged += modeChanges.Add;

            Assert.IsTrue(manager.RequestMode(InputMode.Interact));
            manager.SetContext(InputContext.UiOnly);
            manager.SetContext(InputContext.Drawing);
            InvokeCallback("OnApplicationFocus", false);
            InvokeCallback("OnApplicationFocus", true);

            CollectionAssert.AreEqual(new[] { InputMode.Interact, InputMode.Interact, InputMode.Interact, InputMode.Interact, InputMode.Interact }, modeChanges);
        }

        [Test]
        public void Hud_UsesExactExploreModeHints()
        {
            Text label = AddModeLabel();

            Assert.IsTrue(manager.RequestMode(InputMode.Interact));
            Assert.AreEqual("손 조작 · Tab: 이동", label.text);
            Assert.IsTrue(manager.RequestMode(InputMode.Move));
            Assert.AreEqual("이동 · Tab: 손 조작", label.text);
        }

        [TestCase(InputContext.UiOnly)]
        [TestCase(InputContext.Drawing)]
        [TestCase(InputContext.Blocked)]
        public void Hud_ForcedContextHidesTabHintAndExploreRestoresIt(InputContext context)
        {
            Text label = AddModeLabel();
            Assert.IsTrue(manager.RequestMode(InputMode.Interact));

            manager.SetContext(context);
            Assert.IsNotEmpty(label.text);
            StringAssert.DoesNotContain("Tab", label.text);
            manager.SetContext(InputContext.Explore);
            Assert.AreEqual("손 조작 · Tab: 이동", label.text);
        }

        [Test]
        public void ExecutionOrder_ManagerRunsBeforePlayerController()
        {
            var managerOrder = (DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(InputModeManager), typeof(DefaultExecutionOrder));
            var playerOrder = (DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(PlayerController), typeof(DefaultExecutionOrder));

            Assert.IsNotNull(managerOrder, "Mode and focus permissions must be resolved before player input in the same frame.");
            Assert.Less(managerOrder.order, playerOrder == null ? 0 : playerOrder.order);
        }

        [Test]
        public void CameraControl_DefaultAvailabilityDeniesCameraMouse()
        {
            Assert.IsFalse(ReadCanUseCameraMouse());
            Assert.IsTrue(manager.RequestMode(InputMode.Interact));
            Assert.IsFalse(ReadCanUseCameraMouse());
            Assert.IsFalse(manager.DesiredCursorVisible);
        }

        [Test]
        public void CameraPreparation_StartupBlocksGameInputsAndAllowsCameraMouse()
        {
            Text label = AddModeLabel();

            SetCameraControlState(true, true);

            Assert.AreEqual(InputContext.Explore, manager.CurrentContext);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            AssertPermissions(false, false, false, false, false);
            Assert.IsTrue(ReadCanUseCameraMouse());
            Assert.AreEqual(CursorLockMode.None, manager.DesiredCursorLockState);
            Assert.IsTrue(manager.DesiredCursorVisible);
            Assert.AreEqual("캠 준비 · 이동 잠금", label.text);
            StringAssert.DoesNotContain("Tab", label.text);
            Assert.IsFalse(manager.RequestMode(InputMode.Move));
            Assert.IsFalse(manager.RequestMode(InputMode.Interact));
            manager.ProcessInput(true, CursorLockMode.None);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
        }

        [TestCase(InputMode.Move)]
        [TestCase(InputMode.Interact)]
        public void CameraPreparation_ClearingRestoresRequestedExploreMode(InputMode requestedMode)
        {
            Assert.IsTrue(manager.RequestMode(requestedMode));
            SetCameraControlState(true, true);
            manager.ProcessInput(true, CursorLockMode.None);

            SetCameraControlState(true, false);

            bool isMove = requestedMode == InputMode.Move;
            Assert.AreEqual(InputContext.Explore, manager.CurrentContext);
            Assert.AreEqual(requestedMode, manager.CurrentMode);
            AssertPermissions(isMove, isMove, !isMove, false, true);
            Assert.AreEqual(!isMove, ReadCanUseCameraMouse());
            Assert.AreEqual(isMove ? CursorLockMode.Locked : CursorLockMode.None, manager.DesiredCursorLockState);
            Assert.AreEqual(!isMove, manager.DesiredCursorVisible);
        }

        [TestCase(InputContext.UiOnly, false)]
        [TestCase(InputContext.Drawing, true)]
        public void CameraPreparation_ClearingRestoresExistingForcedContext(InputContext context, bool canDraw)
        {
            manager.SetContext(context);
            SetCameraControlState(true, true);
            AssertPermissions(false, false, false, false, false);

            SetCameraControlState(true, false);

            Assert.AreEqual(context, manager.CurrentContext);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            AssertPermissions(false, false, true, canDraw, false);
            Assert.IsTrue(ReadCanUseCameraMouse());
        }

        [TestCase(InputContext.UiOnly, true, false)]
        [TestCase(InputContext.Drawing, true, true)]
        [TestCase(InputContext.Blocked, false, false)]
        public void CameraPreparation_ClearingUsesContextSelectedDuringPreparation(InputContext nextContext, bool canUseUi, bool canDraw)
        {
            SetCameraControlState(true, true);
            manager.SetContext(nextContext);

            SetCameraControlState(true, false);

            Assert.AreEqual(nextContext, manager.CurrentContext);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            AssertPermissions(false, false, canUseUi, canDraw, false);
            Assert.AreEqual(canUseUi, ReadCanUseCameraMouse());
        }

        [Test]
        public void CameraPreparation_ReturningToExploreKeepsContextSafeReset()
        {
            SetCameraControlState(true, true);
            manager.SetContext(InputContext.UiOnly);
            manager.SetContext(InputContext.Explore);

            SetCameraControlState(true, false);

            Assert.AreEqual(InputContext.Explore, manager.CurrentContext);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode, "Preparation must not restore Move after a context reset.");
            AssertPermissions(false, false, true, false, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CameraPreparation_FocusRecoveryKeepsSafeInteract(bool regainFocusBeforeReady)
        {
            Text label = AddModeLabel();
            SetCameraControlState(true, true);
            InvokeCallback("OnApplicationFocus", false);

            AssertPermissions(false, false, false, false, false);
            Assert.IsFalse(ReadCanUseCameraMouse());
            Assert.AreEqual(CursorLockMode.None, manager.DesiredCursorLockState);
            Assert.IsTrue(manager.DesiredCursorVisible);
            Assert.AreEqual("입력 중지", label.text);
            if (regainFocusBeforeReady)
            {
                InvokeCallback("OnApplicationFocus", true);
                AssertPermissions(false, false, false, false, false);
                Assert.IsTrue(ReadCanUseCameraMouse());
            }

            SetCameraControlState(true, false);
            InvokeCallback("OnApplicationFocus", true);

            Assert.AreEqual(InputMode.Interact, manager.CurrentMode, "Preparation must not restore Move after focus loss.");
            AssertPermissions(false, false, true, false, true);
            Assert.IsTrue(ReadCanUseCameraMouse());
            Assert.IsTrue(manager.DesiredCursorVisible);
            Assert.IsTrue(manager.RequestMode(InputMode.Move));
        }

        [Test]
        public void CameraPreparation_BlockedContextTakesHudPriorityAndDeniesMouseOnlyWhileReceiving()
        {
            Text label = AddModeLabel();
            SetCameraControlState(true, true);
            manager.SetContext(InputContext.Blocked);

            AssertPermissions(false, false, false, false, false);
            // 캠이 끊긴 동안에는 Blocked라도 재시도 컨트롤을 남긴다 (docs/06 §9).
            Assert.IsTrue(ReadCanUseCameraMouse());
            Assert.AreEqual("입력 중지", label.text);
            Assert.IsTrue(manager.DesiredCursorVisible);

            SetCameraControlState(true, false);
            Assert.IsFalse(ReadCanUseCameraMouse(), "수신 중이면 Blocked에서 캠 마우스를 닫는다.");
        }

        [Test]
        public void CameraPreparation_BlockedRetryStillRequiresFocus()
        {
            SetCameraControlState(true, true);
            manager.SetContext(InputContext.Blocked);
            Assert.IsTrue(ReadCanUseCameraMouse());

            InvokeCallback("OnApplicationFocus", false);

            Assert.IsFalse(ReadCanUseCameraMouse(), "포커스를 잃으면 캠 마우스도 막는다.");
        }

        [Test]
        public void CameraPreparation_BlockedRetryDoesNotOpenGameInput()
        {
            SetCameraControlState(true, true);
            manager.SetContext(InputContext.Blocked);

            // 재시도 예외는 캠 컨트롤에만 적용되고 일반 게임 입력은 그대로 닫혀 있어야 한다.
            AssertPermissions(false, false, false, false, false);
            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
        }

        [Test]
        public void CameraControl_AvailableInteractAllowsMouseAndMoveLocksCursor()
        {
            SetCameraControlState(true, false);
            Assert.IsFalse(ReadCanUseCameraMouse());
            Assert.AreEqual(CursorLockMode.Locked, manager.DesiredCursorLockState);
            Assert.IsFalse(manager.DesiredCursorVisible);

            manager.ProcessInput(true, CursorLockMode.Locked);

            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            Assert.IsTrue(ReadCanUseCameraMouse());
            Assert.AreEqual(CursorLockMode.None, manager.DesiredCursorLockState);
            Assert.IsTrue(manager.DesiredCursorVisible);
            manager.ProcessInput(true, CursorLockMode.None);
            Assert.AreEqual(InputMode.Move, manager.CurrentMode);
            Assert.IsFalse(ReadCanUseCameraMouse());
            Assert.AreEqual(CursorLockMode.Locked, manager.DesiredCursorLockState);
            Assert.IsFalse(manager.DesiredCursorVisible);
        }

        [Test]
        public void CameraControl_DisablingRestoresExistingInteractCursorPolicy()
        {
            Assert.IsTrue(manager.RequestMode(InputMode.Interact));
            SetCameraControlState(true, true);

            SetCameraControlState(false, false);

            Assert.AreEqual(InputMode.Interact, manager.CurrentMode);
            AssertPermissions(false, false, true, false, true);
            Assert.IsFalse(ReadCanUseCameraMouse());
            Assert.AreEqual(CursorLockMode.None, manager.DesiredCursorLockState);
            Assert.IsFalse(manager.DesiredCursorVisible);
        }

        [Test]
        public void CameraControlState_NotifiesOnlyChangedValuesEvenWhenModeStaysInteract()
        {
            Assert.IsTrue(manager.RequestMode(InputMode.Interact));
            manager.OnModeChanged += modeChanges.Add;

            SetCameraControlState(false, false);
            Assert.IsEmpty(modeChanges);
            SetCameraControlState(true, true);
            SetCameraControlState(true, true);
            SetCameraControlState(true, false);
            SetCameraControlState(true, false);
            SetCameraControlState(false, false);
            SetCameraControlState(false, false);

            CollectionAssert.AreEqual(new[] { InputMode.Interact, InputMode.Interact, InputMode.Interact }, modeChanges);
        }

        private void AssertPermissions(bool move, bool look, bool handUi, bool draw, bool toggle)
        {
            Assert.AreEqual(move, manager.CanMove, "CanMove");
            Assert.AreEqual(look, manager.CanLook, "CanLook");
            Assert.AreEqual(handUi, manager.CanUseHandUi, "CanUseHandUi");
            Assert.AreEqual(draw, manager.CanDraw, "CanDraw");
            Assert.AreEqual(toggle, manager.CanToggleMode, "CanToggleMode");
        }

        private void SetCameraControlState(bool available, bool preparing)
        {
            MethodInfo method = typeof(InputModeManager).GetMethod("SetCameraControlState", BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(bool), typeof(bool) }, null);
            Assert.IsNotNull(method, "InputModeManager must expose SetCameraControlState(bool available, bool preparing).");
            method.Invoke(manager, new object[] { available, preparing });
        }

        private bool ReadCanUseCameraMouse()
        {
            PropertyInfo property = typeof(InputModeManager).GetProperty("CanUseCameraMouse", BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, "InputModeManager must expose CanUseCameraMouse.");
            return (bool)property.GetValue(manager);
        }

        private void InvokeCallback(string name, params object[] arguments)
        {
            MethodInfo method = typeof(InputModeManager).GetMethod(name, InstanceFlags);
            Assert.IsNotNull(method, "InputModeManager must implement " + name + ".");
            method.Invoke(manager, arguments);
        }

        private Text AddModeLabel()
        {
            var labelObject = new GameObject("mode label", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(rig.transform, false);
            var label = labelObject.GetComponent<Text>();
            FieldInfo field = typeof(InputModeManager).GetField("modeLabel", InstanceFlags);
            Assert.IsNotNull(field, "InputModeManager must have an explicit serialized HUD label reference.");
            field.SetValue(manager, label);
            return label;
        }

    }
}
