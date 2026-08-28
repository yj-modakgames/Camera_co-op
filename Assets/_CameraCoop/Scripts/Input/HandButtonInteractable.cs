using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CameraCoop
{
    public sealed class HandButtonInteractable : HandInteractable
    {
        private const float PressedScale = 0.96f;
        private const float ConfirmationSeconds = 0.12f;

        [SerializeField] private Button targetButton;
        [SerializeField] private InputField targetInputField;
        [SerializeField] private Graphic hoverGraphic;
        [SerializeField] private Graphic pressedGraphic;
        [SerializeField] private EventSystem eventSystem;
        [SerializeField] private string displayName;
        [SerializeField, Min(0.01f)] private float clickPitch = 1f;
        [SerializeField] private Color leftHandColor = new Color(0.2f, 0.6f, 1f);
        [SerializeField] private Color rightHandColor = new Color(1f, 0.6f, 0.1f);

        private readonly HashSet<string> hoveringHands = new HashSet<string>();
        private Selectable target;
        private Graphic background;
        private Vector3 normalScale;
        private Color normalBackgroundColor;
        private Color normalHoverColor;
        private Color normalPressedColor;
        private bool initialized;
        private bool wasAvailable;
        private bool hasPress;
        private HandClickContext pressContext;
        private PointerEventData pressPointer;
        private float confirmationUntil;
        private string confirmationHand;

        public override bool IsAvailable => base.IsAvailable && initialized && target != null
            && target.isActiveAndEnabled && target.IsInteractable() && eventSystem != null
            && eventSystem.isActiveAndEnabled && hoverGraphic != null && pressedGraphic != null;
        public override string DisplayName => string.IsNullOrEmpty(displayName) ? base.DisplayName : displayName;
        public override float ClickPitch => clickPitch;

        public event Action<HandClickContext> OnHandClick;

        // IME 조합 중 제출 차단처럼 게임 상태가 버튼 하나를 끌 때 쓴다. interactable=false면 down·select도 막힌다.
        public void SetInteractable(bool value)
        {
            if (target == null || target.interactable == value)
            {
                return;
            }
            target.interactable = value;
            if (!value)
            {
                ResetInteraction();
            }
            else
            {
                ApplyFeedback();
            }
        }

        private void Awake()
        {
            if (initialized)
            {
                return;
            }
            target = targetButton != null ? (Selectable)targetButton : targetInputField;
            bool hasOneTarget = (targetButton != null) != (targetInputField != null);
            if (!hasOneTarget || eventSystem == null || hoverGraphic == null || pressedGraphic == null
                || target.targetGraphic == null || hoverGraphic == pressedGraphic
                || hoverGraphic == target.targetGraphic || pressedGraphic == target.targetGraphic
                || (targetInputField != null && (targetInputField.textComponent == null || targetInputField.textComponent.font == null)))
            {
                Debug.LogError("HandButtonInteractable requires exactly one Button or InputField, an EventSystem, a target graphic, and separate hover/pressed graphics. InputField also requires a text component and font.", this);
                enabled = false;
                return;
            }

            background = target.targetGraphic;
            normalScale = target.transform.localScale;
            normalBackgroundColor = background.color;
            normalHoverColor = hoverGraphic.color;
            normalPressedColor = pressedGraphic.color;
            hoverGraphic.raycastTarget = false;
            pressedGraphic.raycastTarget = false;
            initialized = true;
            ApplyFeedback();
        }

        private void OnEnable()
        {
            ApplyFeedback();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            GameObject disabledTarget = target != null ? target.gameObject : null;
            ResetInteraction();
            ClearCanceledSelection(eventSystem, disabledTarget);
        }

        private void Update()
        {
            if (!IsAvailable)
            {
                ResetInteraction();
                return;
            }
            if (!wasAvailable || (confirmationUntil > 0f && Time.unscaledTime >= confirmationUntil))
            {
                confirmationUntil = 0f;
                ApplyFeedback();
            }
        }

        public override void HoverEnter(HandInputSample sample, Vector3 hitPosition)
        {
            if (!IsAvailable || !hoveringHands.Add(sample.handedness))
            {
                return;
            }
            if (hoveringHands.Count == 1)
            {
                target.OnPointerEnter(CreatePointer(sample, hitPosition));
            }
            ApplyFeedback();
        }

        public override void HoverExit(HandInputSample sample, Vector3 hitPosition)
        {
            if (!hoveringHands.Remove(sample.handedness))
            {
                return;
            }
            if (hoveringHands.Count == 0 && CanSendNativeEvents())
            {
                target.OnPointerExit(CreatePointer(sample, hitPosition));
            }
            ApplyFeedback();
        }

        public override void Press(HandInputSample sample, Vector3 hitPosition, HandClickContext context)
        {
            if (!IsAvailable || hasPress)
            {
                return;
            }
            hasPress = true;
            pressContext = context;
            pressPointer = CreatePointer(sample, hitPosition);
            confirmationUntil = 0f;

            if (targetButton != null)
            {
                // native down의 즉시 선택을 막아 취소된 핀치가 입력창 포커스를 빼앗지 않게 한다.
                Navigation navigation = targetButton.navigation;
                Navigation withoutSelection = navigation;
                withoutSelection.mode = Navigation.Mode.None;
                targetButton.navigation = withoutSelection;
                try
                {
                    targetButton.OnPointerDown(pressPointer);
                }
                finally
                {
                    targetButton.navigation = navigation;
                }
            }
            ApplyFeedback();
        }

        public override void Hold(HandInputSample sample, Vector3 hitPosition)
        {
            if (!hasPress || pressContext.handedness != sample.handedness)
            {
                return;
            }
            if (!IsAvailable)
            {
                ResetInteraction();
                return;
            }
            UpdatePointer(pressPointer, sample, hitPosition);
        }

        public override bool Release(HandInputSample sample, Vector3 hitPosition)
        {
            if (!hasPress || pressContext.handedness != sample.handedness)
            {
                return false;
            }
            if (!IsAvailable)
            {
                ResetInteraction();
                return false;
            }

            uint releaseRevision = LifecycleRevision;
            EventSystem releaseEvents = eventSystem;
            GameObject releaseTarget = target.gameObject;
            InputField releaseInput = targetInputField;
            HandClickContext confirmedContext = pressContext;
            PointerEventData pointer = pressPointer;
            UpdatePointer(pointer, sample, hitPosition);
            hasPress = false;
            pressPointer = null;

            if (targetButton != null)
            {
                targetButton.OnPointerUp(pointer);
            }
            if (this == null || LifecycleRevision != releaseRevision || !IsAvailable)
            {
                return AbortRelease(releaseEvents, releaseTarget);
            }

            bool activateOnSelect = releaseInput != null && releaseInput.shouldActivateOnSelect;
            try
            {
                // 선택 콜백이 대상을 끄거나 파괴할 수 있어 자동 활성화는 마지막 확인 뒤에만 허용한다.
                if (releaseInput != null)
                {
                    releaseInput.shouldActivateOnSelect = false;
                }
                releaseEvents.SetSelectedGameObject(releaseTarget, pointer);
                if (!CanContinueRelease(releaseRevision, releaseEvents, releaseTarget))
                {
                    return AbortRelease(releaseEvents, releaseTarget);
                }
                if (releaseInput != null)
                {
                    releaseInput.OnPointerDown(pointer);
                    if (!CanContinueRelease(releaseRevision, releaseEvents, releaseTarget))
                    {
                        return AbortRelease(releaseEvents, releaseTarget);
                    }
                    releaseInput.OnPointerUp(pointer);
                    if (!CanContinueRelease(releaseRevision, releaseEvents, releaseTarget))
                    {
                        return AbortRelease(releaseEvents, releaseTarget);
                    }
                }
            }
            finally
            {
                if (releaseInput != null)
                {
                    releaseInput.shouldActivateOnSelect = activateOnSelect;
                }
            }

            if (!CanContinueRelease(releaseRevision, releaseEvents, releaseTarget))
            {
                return AbortRelease(releaseEvents, releaseTarget);
            }
            if (releaseInput != null)
            {
                releaseInput.ActivateInputField();
                if (!CanContinueRelease(releaseRevision, releaseEvents, releaseTarget))
                {
                    return AbortRelease(releaseEvents, releaseTarget);
                }
            }

            confirmationHand = confirmedContext.handedness;
            confirmationUntil = Time.unscaledTime + ConfirmationSeconds;
            ApplyFeedback();
            OnHandClick?.Invoke(confirmedContext);
            return true;
        }

        private bool CanContinueRelease(uint revision, EventSystem releaseEvents, GameObject releaseTarget)
        {
            return this != null && LifecycleRevision == revision && IsAvailable && releaseEvents != null
                && ReferenceEquals(releaseEvents.currentSelectedGameObject, releaseTarget);
        }

        private bool AbortRelease(EventSystem releaseEvents, GameObject releaseTarget)
        {
            if (this != null)
            {
                ResetInteraction();
            }
            ClearCanceledSelection(releaseEvents, releaseTarget);
            return false;
        }

        private static void ClearCanceledSelection(EventSystem releaseEvents, GameObject releaseTarget)
        {
            if (releaseEvents != null && !ReferenceEquals(releaseTarget, null) && !releaseEvents.alreadySelecting
                && ReferenceEquals(releaseEvents.currentSelectedGameObject, releaseTarget))
            {
                releaseEvents.SetSelectedGameObject(null);
            }
        }

        public override void Cancel(HandInputSample sample, Vector3 hitPosition)
        {
            HoverExit(sample, hitPosition);
            if (hasPress && pressContext.handedness != sample.handedness)
            {
                return;
            }
            if (hasPress && targetButton != null && CanSendNativeEvents())
            {
                UpdatePointer(pressPointer, sample, hitPosition);
                targetButton.OnPointerUp(pressPointer);
            }
            hasPress = false;
            pressPointer = null;
            confirmationUntil = 0f;
            ApplyFeedback();
        }

        private void ResetInteraction()
        {
            if (hasPress && targetButton != null && CanSendNativeEvents())
            {
                targetButton.OnPointerUp(pressPointer);
            }
            if (hoveringHands.Count > 0 && CanSendNativeEvents())
            {
                target.OnPointerExit(pressPointer ?? new PointerEventData(eventSystem));
            }
            hoveringHands.Clear();
            hasPress = false;
            pressPointer = null;
            confirmationUntil = 0f;
            ApplyFeedback();
        }

        private bool CanSendNativeEvents()
        {
            return initialized && target != null && target.isActiveAndEnabled && eventSystem != null;
        }

        private PointerEventData CreatePointer(HandInputSample sample, Vector3 hitPosition)
        {
            var pointer = new PointerEventData(eventSystem)
            {
                pointerId = sample.handedness == "Left" ? -101 : -102,
                button = PointerEventData.InputButton.Left,
                eligibleForClick = false,
                pressPosition = sample.screenPosition,
                pointerPress = target.gameObject,
                rawPointerPress = target.gameObject,
                pointerEnter = target.gameObject,
                useDragThreshold = false
            };
            UpdatePointer(pointer, sample, hitPosition);
            pointer.pointerPressRaycast = pointer.pointerCurrentRaycast;
            return pointer;
        }

        private void UpdatePointer(PointerEventData pointer, HandInputSample sample, Vector3 hitPosition)
        {
            pointer.position = sample.screenPosition;
            pointer.pointerCurrentRaycast = new RaycastResult
            {
                gameObject = target != null ? target.gameObject : null,
                screenPosition = sample.screenPosition,
                worldPosition = hitPosition
            };
        }

        private void ApplyFeedback()
        {
            if (!initialized || target == null)
            {
                return;
            }
            bool available = IsAvailable;
            wasAvailable = available;
            bool pressed = available && hasPress;
            bool confirmed = available && confirmationUntil > Time.unscaledTime;
            target.transform.localScale = pressed ? normalScale * PressedScale : normalScale;
            if (background != null)
            {
                Color color = normalBackgroundColor;
                float brightness = !available ? 0.55f : pressed ? 0.8f : 1f;
                background.color = new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
            }
            if (hoverGraphic != null)
            {
                hoverGraphic.enabled = available && (hoveringHands.Count > 0 || confirmed);
                hoverGraphic.color = confirmed ? Color.white : normalHoverColor;
            }
            if (pressedGraphic != null)
            {
                pressedGraphic.enabled = pressed || confirmed;
                Color color = normalPressedColor;
                if (pressed || confirmed)
                {
                    string hand = pressed ? pressContext.handedness : confirmationHand;
                    color = hand == "Left" ? leftHandColor : rightHandColor;
                    if (confirmed)
                    {
                        color = Color.Lerp(color, Color.white, 0.45f);
                    }
                    color.a = normalPressedColor.a;
                }
                pressedGraphic.color = color;
            }
        }
    }
}
