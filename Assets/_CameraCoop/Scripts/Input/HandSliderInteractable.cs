using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop
{
    public sealed class HandSliderInteractable : HandInteractable
    {
        private const float PressedScale = 0.96f;

        [SerializeField] private Slider targetSlider;
        [SerializeField] private ToolState toolState;
        [SerializeField] private ToolButton[] widthButtons;
        [SerializeField] private Graphic hoverGraphic;
        [SerializeField] private Graphic pressedGraphic;
        [SerializeField] private Color leftHandColor = new Color(0.2f, 0.6f, 1f);
        [SerializeField] private Color rightHandColor = new Color(1f, 0.6f, 0.1f);

        private readonly HashSet<string> hoveringHands = new HashSet<string>();
        private Vector3 normalScale;
        private Color normalPressedColor;
        private bool initialized;
        private bool subscribed;
        private bool hasPress;
        private HandClickContext pressContext;

        public override string DisplayName => "굵기";
        public override bool RequiresInside => false;
        public override bool IsAvailable => base.IsAvailable && initialized && targetSlider != null
            && targetSlider.isActiveAndEnabled && targetSlider.IsInteractable() && toolState != null
            && hoverGraphic != null && pressedGraphic != null;

        private void Awake()
        {
            if (initialized)
            {
                return;
            }
            if (targetSlider == null || toolState == null || hoverGraphic == null || pressedGraphic == null
                || hoverGraphic == pressedGraphic || !HasWidthButtonIndices())
            {
                Debug.LogError("HandSliderInteractable requires a Slider, ToolState, three Width ToolButtons (indices 0..2), and separate hover/pressed graphics.", this);
                enabled = false;
                return;
            }

            normalScale = targetSlider.transform.localScale;
            normalPressedColor = pressedGraphic.color;
            hoverGraphic.raycastTarget = false;
            pressedGraphic.raycastTarget = false;
            targetSlider.minValue = 0f;
            targetSlider.maxValue = widthButtons.Length - 1;
            targetSlider.wholeNumbers = true;
            initialized = true;
            SyncFromToolState();
            ApplyFeedback();
        }

        private void OnEnable()
        {
            if (!initialized || subscribed)
            {
                return;
            }
            targetSlider.onValueChanged.AddListener(OnSliderValueChanged);
            toolState.OnChanged += SyncFromToolState;
            subscribed = true;
            SyncFromToolState();
            ApplyFeedback();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (subscribed)
            {
                if (targetSlider != null)
                {
                    targetSlider.onValueChanged.RemoveListener(OnSliderValueChanged);
                }
                if (toolState != null)
                {
                    toolState.OnChanged -= SyncFromToolState;
                }
                subscribed = false;
            }
            hoveringHands.Clear();
            hasPress = false;
            ApplyFeedback();
        }

        public override void HoverEnter(HandInputSample sample, Vector3 hitPosition)
        {
            if (IsAvailable && hoveringHands.Add(sample.handedness))
            {
                ApplyFeedback();
            }
        }

        public override void HoverExit(HandInputSample sample, Vector3 hitPosition)
        {
            if (hoveringHands.Remove(sample.handedness))
            {
                ApplyFeedback();
            }
        }

        public override void Press(HandInputSample sample, Vector3 hitPosition, HandClickContext context)
        {
            if (!IsAvailable || hasPress)
            {
                return;
            }
            hasPress = true;
            pressContext = context;
            if (!UpdateValue(sample))
            {
                hasPress = false;
                ApplyFeedback();
                return;
            }
            ApplyFeedback();
        }

        public override void Hold(HandInputSample sample, Vector3 hitPosition)
        {
            if (!hasPress || pressContext.handedness != sample.handedness || !IsAvailable)
            {
                return;
            }
            if (!UpdateValue(sample))
            {
                hasPress = false;
                ApplyFeedback();
                return;
            }
            ApplyFeedback();
        }

        public override bool Release(HandInputSample sample, Vector3 hitPosition)
        {
            if (!hasPress || pressContext.handedness != sample.handedness || !IsAvailable)
            {
                return false;
            }
            bool updated = UpdateValue(sample);
            hasPress = false;
            ApplyFeedback();
            return updated;
        }

        public override void Cancel(HandInputSample sample, Vector3 hitPosition)
        {
            HoverExit(sample, hitPosition);
            if (hasPress && pressContext.handedness == sample.handedness)
            {
                hasPress = false;
                ApplyFeedback();
            }
        }

        private bool HasWidthButtonIndices()
        {
            if (widthButtons == null || widthButtons.Length != 3)
            {
                return false;
            }
            for (int index = 0; index < widthButtons.Length; index++)
            {
                if (widthButtons[index] == null || widthButtons[index].Kind != ToolKind.Width || widthButtons[index].Index != index)
                {
                    return false;
                }
            }
            return true;
        }

        private bool UpdateValue(HandInputSample sample)
        {
            RectTransform rect = targetSlider.transform as RectTransform;
            if (rect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, sample.screenPosition, null, out Vector2 localPoint))
            {
                return false;
            }
            float normalized = Mathf.InverseLerp(rect.rect.xMin, rect.rect.xMax, localPoint.x);
            int index = Mathf.RoundToInt(Mathf.Lerp(targetSlider.minValue, targetSlider.maxValue, normalized));
            uint revision = LifecycleRevision;
            targetSlider.SetValueWithoutNotify(index);
            toolState.Apply(widthButtons[index]);
            return this != null && LifecycleRevision == revision && IsAvailable;
        }

        private void OnSliderValueChanged(float value)
        {
            if (!IsAvailable)
            {
                return;
            }
            int index = Mathf.Clamp(Mathf.RoundToInt(value), 0, widthButtons.Length - 1);
            toolState.Apply(widthButtons[index]);
        }

        private void SyncFromToolState()
        {
            if (initialized && targetSlider != null && toolState != null)
            {
                targetSlider.SetValueWithoutNotify(Mathf.Clamp(toolState.CurrentWidthIndex, 0, widthButtons.Length - 1));
                ApplyFeedback();
            }
        }

        private void ApplyFeedback()
        {
            if (!initialized || targetSlider == null)
            {
                return;
            }
            bool available = IsAvailable;
            bool pressed = available && hasPress;
            targetSlider.transform.localScale = pressed ? normalScale * PressedScale : normalScale;
            hoverGraphic.enabled = available && hoveringHands.Count > 0;
            pressedGraphic.enabled = pressed;
            if (pressed)
            {
                Color color = pressContext.handedness == "Left" ? leftHandColor : rightHandColor;
                color.a = normalPressedColor.a;
                pressedGraphic.color = color;
            }
            else
            {
                pressedGraphic.color = normalPressedColor;
            }
        }
    }
}
