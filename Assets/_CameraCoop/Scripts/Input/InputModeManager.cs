using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CameraCoop
{
    public enum InputMode
    {
        Move,
        Interact
    }

    public enum InputContext
    {
        Explore,
        UiOnly,
        Drawing,
        Blocked
    }

    [DefaultExecutionOrder(-100)]
    public class InputModeManager : MonoBehaviour
    {
        [SerializeField] private InputContext initialContext = InputContext.Explore;
        [SerializeField] private InputMode initialMode = InputMode.Move;
        [SerializeField] private Key toggleKey = Key.Tab;
        [SerializeField] private Text modeLabel;

        private InputContext context;
        private InputMode requestedMode;
        private bool hasFocus = true;
        private bool wasTyping;
        private bool cameraControlAvailable;
        private bool cameraPreparing;
        private bool drawingMovementAllowed;
        private bool practiceDrawingAllowed;
        private static readonly Type TmpInputFieldType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");

        public InputContext CurrentContext => context;
        public InputMode CurrentMode => hasFocus && !IsCameraPreparing && context == InputContext.Explore ? requestedMode : InputMode.Interact;
        public bool DrawingMovementAllowed => drawingMovementAllowed;
        public bool CanMove => hasFocus && !IsCameraPreparing && !InputFocus.IsTyping
            && (context == InputContext.Explore && CurrentMode == InputMode.Move
                || context == InputContext.Drawing && drawingMovementAllowed);
        public bool CanLook => CanMove;
        public bool CanUseHandUi => hasFocus && !IsCameraPreparing && context != InputContext.Blocked && CurrentMode == InputMode.Interact;
        public bool CanDraw => hasFocus && !IsCameraPreparing && !InputFocus.IsTyping
            && (context == InputContext.Drawing || practiceDrawingAllowed
                && context == InputContext.Explore && CurrentMode == InputMode.Interact);
        public bool CanToggleMode => hasFocus && !IsCameraPreparing && context == InputContext.Explore && !InputFocus.IsTyping;
        // Blocked에서도 캠이 수신 중이 아니면 재시도만은 허용한다. 차폐 중 캠이 끊기면
        // 이 경로 말고는 복구 수단이 없다 (docs/06 §9, docs/09 §7).
        public bool CanUseCameraMouse => cameraControlAvailable && hasFocus && CurrentMode == InputMode.Interact
            && !InputFocus.IsTyping && !HasSelectedTextInput()
            && (context != InputContext.Blocked || IsCameraPreparing);

        internal CursorLockMode DesiredCursorLockState => CanLook ? CursorLockMode.Locked : CursorLockMode.None;
        internal bool DesiredCursorVisible => !hasFocus || !CanLook && cameraControlAvailable && CurrentMode == InputMode.Interact;

        private bool IsCameraPreparing => cameraControlAvailable && cameraPreparing;

        private static bool HasSelectedTextInput()
        {
            EventSystem events = EventSystem.current;
            GameObject selected = events != null ? events.currentSelectedGameObject : null;
            return selected != null && (selected.GetComponentInParent<InputField>() != null
                || TmpInputFieldType != null && selected.GetComponentInParent(TmpInputFieldType) != null);
        }

        // Interact가 유지되어도 컨텍스트·포커스 변경을 구독자에게 알린다.
        public event Action<InputMode> OnModeChanged;

        private void Awake()
        {
            context = initialContext;
            requestedMode = context == InputContext.Explore ? initialMode : InputMode.Interact;
            wasTyping = InputFocus.IsTyping;
            OnModeChanged += UpdateModeLabel;
            ApplyCursorState();
            UpdateModeLabel(CurrentMode);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            ProcessInput(keyboard != null && keyboard[toggleKey].wasPressedThisFrame, Cursor.lockState);
        }

        public void SetCameraControlState(bool available, bool preparing)
        {
            if (cameraControlAvailable == available && cameraPreparing == preparing)
            {
                return;
            }
            cameraControlAvailable = available;
            cameraPreparing = preparing;
            NotifyModeChanged();
        }

        public void SetDrawingMovementAllowed(bool allowed)
        {
            if (drawingMovementAllowed == allowed) return;
            drawingMovementAllowed = allowed;
            NotifyModeChanged();
        }

        public void SetPracticeDrawingAllowed(bool allowed)
        {
            practiceDrawingAllowed = allowed;
        }

        public void SetContext(InputContext nextContext)
        {
            if (context == nextContext)
            {
                return;
            }
            context = nextContext;
            if (context != InputContext.Explore)
            {
                requestedMode = InputMode.Interact;
            }
            NotifyModeChanged();
        }

        public bool RequestMode(InputMode mode)
        {
            if (!CanToggleMode)
            {
                return false;
            }
            if (requestedMode != mode)
            {
                requestedMode = mode;
                NotifyModeChanged();
            }
            return true;
        }

        internal void ProcessInput(bool togglePressed, CursorLockMode observedLockState)
        {
            if (hasFocus && CurrentMode == InputMode.Move && observedLockState != CursorLockMode.Locked)
            {
                requestedMode = InputMode.Interact;
                NotifyModeChanged();
                return;
            }
            if (wasTyping != InputFocus.IsTyping)
            {
                wasTyping = InputFocus.IsTyping;
                NotifyModeChanged();
            }
            if (togglePressed && CanToggleMode)
            {
                RequestMode(CurrentMode == InputMode.Move ? InputMode.Interact : InputMode.Move);
            }
            ApplyCursorState();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (hasFocus == focused)
            {
                return;
            }
            hasFocus = focused;
            requestedMode = InputMode.Interact;
            NotifyModeChanged();
        }

        private void NotifyModeChanged()
        {
            ApplyCursorState();
            OnModeChanged?.Invoke(CurrentMode);
        }

        private void ApplyCursorState()
        {
            if (!Application.isPlaying)
            {
                return;
            }
            Cursor.lockState = DesiredCursorLockState;
            Cursor.visible = DesiredCursorVisible;
        }

        private void UpdateModeLabel(InputMode mode)
        {
            if (modeLabel == null)
            {
                return;
            }
            if (!hasFocus || context == InputContext.Blocked)
            {
                modeLabel.text = "입력 중지";
            }
            else if (IsCameraPreparing)
            {
                modeLabel.text = "캠 준비 · 이동 잠금";
            }
            else if (context == InputContext.Explore)
            {
                modeLabel.text = mode == InputMode.Move ? "이동 · Tab: 손 조작" : "손 조작 · Tab: 이동";
            }
            else
            {
                modeLabel.text = context == InputContext.Drawing
                    ? drawingMovementAllowed ? "그리기 · WASD 이동" : "그리기"
                    : "손 조작";
            }
        }

        private void OnDestroy()
        {
            OnModeChanged -= UpdateModeLabel;
            if (Application.isPlaying)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }
}
