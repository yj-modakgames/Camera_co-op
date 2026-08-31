using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CameraCoop
{
    [DefaultExecutionOrder(100)]
    public class HandInputRouter : MonoBehaviour
    {
        [SerializeField] private HandCursorController cursorController;
        [SerializeField] private InputModeManager inputModeManager;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private EventSystem eventSystem;
        [SerializeField] private GraphicRaycaster[] uiRaycasters;
        [SerializeField] private HandCanvasInteractable activeCanvas;
        [SerializeField] private HandPointer handPointer;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private Text trackingStatusLabel;
        [SerializeField] private Text hoverStatusLabel;
        [SerializeField] private float inputFreshnessSeconds = 0.20f;
        [SerializeField] private float rearmOpenSeconds = 0.10f;
        [SerializeField] private float clickCooldownSeconds = 0.15f;
        [SerializeField] private float maxDistance = 20f;

        private sealed class HandRuntime
        {
            public bool hasSample;
            public HandInputSample sample;
            public bool fresh;
            public bool armed;
            public bool wasPinched;
            public bool wasFist;
            public float sourceTime;
            public float openStartedAt;
            public int openSamples;
            public int revision;
            public HandInteractable hover;
            public uint hoverRevision;
            public Vector3 hoverPosition;
            public HandInteractable capture;
            public uint captureRevision;
            public Vector3 capturePosition;
            public HandClickContext pressContext;
        }

        private readonly HandRuntime left = new HandRuntime();
        private readonly HandRuntime right = new HandRuntime();
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();
        private readonly RaycastHit[] canvasRaycastHits = new RaycastHit[32];
        private readonly Dictionary<HandInteractable, float> lastClicks = new Dictionary<HandInteractable, float>();
        private HandCursorController subscribedCursor;
        private InputModeManager subscribedModes;
        private EventSystem pointerEventSystem;
        private PointerEventData pointerData;
        private bool routingEnabled;
        private bool hasFocus = true;
        private int viewGeneration;
        private float lastHoverSoundTime = float.NegativeInfinity;

        public bool HasFreshHand => left.fresh || right.fresh;
        public bool HasArmedHand => (left.fresh && left.armed) || (right.fresh && right.armed);

        private void OnEnable()
        {
            Unsubscribe();
            routingEnabled = true;
            subscribedCursor = cursorController;
            subscribedModes = inputModeManager;
            if (subscribedCursor != null)
            {
                subscribedCursor.OnHandSample += HandleHandSample;
            }
            if (subscribedModes != null)
            {
                subscribedModes.OnModeChanged += HandleModeChanged;
            }
            CancelAll(HandCancelReason.ComponentDisabled);
        }

        private void Start()
        {
            if ((activeCanvas != null || handPointer != null) &&
                (activeCanvas == null || handPointer == null || activeCanvas.Pointer != handPointer ||
                 activeCanvas.Surface == null || handPointer.InputSource != HandPointerInputSource.HandRouter))
            {
                Debug.LogError("HandInputRouter: assign matching activeCanvas and HandRouter handPointer references.", this);
                enabled = false;
                return;
            }
            if (cursorController == null || inputModeManager == null || playerCamera == null || eventSystem == null ||
                uiRaycasters == null || uiRaycasters.Length == 0 || audioSource == null || hoverClip == null || clickClip == null ||
                inputFreshnessSeconds <= 0f || rearmOpenSeconds < 0f || clickCooldownSeconds < 0f || maxDistance <= 0f)
            {
                Debug.LogError("HandInputRouter: assign cursorController, inputModeManager, playerCamera, eventSystem, UI raycasters, audioSource and both clips; timing and distance settings must be valid.", this);
                enabled = false;
                return;
            }
            if (trackingStatusLabel == null || hoverStatusLabel == null)
            {
                string missingLabel = trackingStatusLabel == null ? "trackingStatusLabel" : "hoverStatusLabel";
                Debug.LogError("HandInputRouter: " + missingLabel + " is unassigned.", this);
                enabled = false;
                return;
            }
            for (int i = 0; i < uiRaycasters.Length; i++)
            {
                if (uiRaycasters[i] == null)
                {
                    Debug.LogError("HandInputRouter: uiRaycasters[" + i + "] is unassigned.", this);
                    enabled = false;
                    return;
                }
            }
        }

        private void Update()
        {
            Tick(Time.unscaledTime);
        }

        private void OnDisable()
        {
            routingEnabled = false;
            Unsubscribe();
            CancelAll(HandCancelReason.ComponentDisabled);
        }

        private void Unsubscribe()
        {
            if (subscribedCursor != null)
            {
                subscribedCursor.OnHandSample -= HandleHandSample;
            }
            if (subscribedModes != null)
            {
                subscribedModes.OnModeChanged -= HandleModeChanged;
            }
            subscribedCursor = null;
            subscribedModes = null;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (hasFocus == focused)
            {
                return;
            }
            hasFocus = focused;
            CancelAll(HandCancelReason.FocusLost);
        }

        private void HandleModeChanged(InputMode mode)
        {
            CancelAll(HandCancelReason.ModeChanged);
        }

        private void HandleHandSample(HandInputSample sample)
        {
            Vector3 hitPosition = Vector3.zero;
            HandInteractable target = null;
            if (sample.isTracked && sample.cancelReason == HandCancelReason.None && routingEnabled && hasFocus &&
                inputModeManager != null && inputModeManager.CanUseHandUi)
            {
                target = ResolveTarget(sample.screenPosition, out hitPosition, out _);
            }
            ProcessSample(sample, Time.unscaledTime, target, hitPosition);
        }

        public void CancelAll(HandCancelReason reason)
        {
            CancelHand(left, reason);
            CancelHand(right, reason);
            UpdateFeedback();
        }

        public void CancelCanvasCaptures(HandCancelReason reason)
        {
            if (left.capture != null && left.capture.IsCanvas)
            {
                CancelHand(left, reason);
            }
            if (right.capture != null && right.capture.IsCanvas)
            {
                CancelHand(right, reason);
            }
            UpdateFeedback();
        }

        public void SetViewGeneration(int generation)
        {
            if (viewGeneration == generation)
            {
                return;
            }
            viewGeneration = generation;
            CancelAll(HandCancelReason.ViewChanged);
        }

        public bool TryGetHandState(string handedness, out HandInputState state)
        {
            HandRuntime hand = GetHand(handedness);
            if (hand == null || !hand.hasSample)
            {
                state = default;
                return false;
            }
            state = new HandInputState(hand.sample, hand.fresh, hand.fresh && hand.armed);
            return true;
        }

        internal void ProcessSample(HandInputSample sample, float now, HandInteractable target, Vector3 hitPosition)
        {
            if (!IsFinite(now))
            {
                return;
            }
            Tick(now);
            HandRuntime hand = GetHand(sample.handedness);
            if (hand == null || (hand.hasSample && sample.sampleId < hand.sample.sampleId))
            {
                return;
            }

            bool normalObservation = sample.isTracked && sample.cancelReason == HandCancelReason.None;
            if (hand.hasSample && sample.sampleId == hand.sample.sampleId &&
                (normalObservation || (!hand.sample.isTracked && hand.sample.cancelReason == sample.cancelReason)))
            {
                return;
            }

            bool valid = normalObservation && IsFinite(sample.sampleAgeSeconds) && sample.sampleAgeSeconds >= 0f &&
                sample.sampleAgeSeconds <= inputFreshnessSeconds && IsFinite(sample.screenPosition.x) &&
                IsFinite(sample.screenPosition.y) && sample.screenPosition.x >= 0f && sample.screenPosition.y >= 0f &&
                sample.screenPosition.x <= Screen.width && sample.screenPosition.y <= Screen.height;
            if (!valid)
            {
                hand.hasSample = true;
                hand.sample = sample;
                hand.fresh = false;
                hand.wasPinched = false;
                hand.wasFist = false;
                HandCancelReason reason = sample.cancelReason != HandCancelReason.None ? sample.cancelReason :
                    (IsFinite(sample.sampleAgeSeconds) && sample.sampleAgeSeconds > inputFreshnessSeconds ?
                        HandCancelReason.StaleSample : HandCancelReason.InvalidSample);
                CancelHand(hand, reason);
                UpdateFeedback();
                return;
            }

            float sourceTime = now - sample.sampleAgeSeconds;
            if (!hand.fresh || sourceTime - hand.sourceTime > inputFreshnessSeconds || sourceTime < hand.sourceTime)
            {
                ResetRearm(hand);
            }
            bool wasPinched = hand.wasPinched;
            bool wasFist = hand.wasFist;
            bool canPinchPress = !wasPinched && sample.isPinched && hand.armed;
            bool canFistPress = !wasFist && sample.isFist && hand.armed;
            hand.sample = sample;
            hand.hasSample = true;
            hand.fresh = true;
            hand.sourceTime = sourceTime;
            hand.wasPinched = sample.isPinched;
            hand.wasFist = sample.isFist;
            hand.revision++;
            if (sample.isPinched || sample.isFist)
            {
                ResetRearm(hand);
            }
            else
            {
                ObserveOpen(hand, sourceTime);
            }

            if (!IsAvailable(target) || !CanDeliver(target))
            {
                target = null;
            }
            if (HasCapture(hand))
            {
                HandCancelReason reason = CaptureInvalidReason(hand, target);
                if (reason != HandCancelReason.None)
                {
                    CancelHand(hand, reason);
                    UpdateFeedback();
                    return;
                }
            }
            if (!UpdateHover(hand, target, hitPosition, now))
            {
                UpdateFeedback();
                return;
            }

            if (HasCapture(hand))
            {
                Vector3 capturePosition = PositionFor(hand.capture, sample, hitPosition);
                hand.capturePosition = capturePosition;
                bool held = hand.capture.IsCanvas ? sample.isFist : sample.isPinched;
                bool wasHeld = hand.capture.IsCanvas ? wasFist : wasPinched;
                if (held)
                {
                    hand.capture.Hold(sample, capturePosition);
                }
                else if (wasHeld)
                {
                    ReleaseCapture(hand, capturePosition, now);
                }
            }
            else if (IsAvailable(target) && CanDeliver(target) && !OwnedByOtherHand(hand, target) &&
                (target.IsCanvas ? canFistPress : canPinchPress))
            {
                hand.capture = target;
                hand.captureRevision = target.LifecycleRevision;
                hand.capturePosition = PositionFor(target, sample, hitPosition);
                hand.pressContext = new HandClickContext(sample.handedness, viewGeneration, sample.sampleId);
                target.Press(sample, hand.capturePosition, hand.pressContext);
            }
            UpdateFeedback();
        }

        internal void Tick(float now)
        {
            if (!IsFinite(now))
            {
                return;
            }
            CheckHand(left, now);
            CheckHand(right, now);
            UpdateFeedback();
        }

        private void CheckHand(HandRuntime hand, float now)
        {
            if (hand.fresh && now - hand.sourceTime > inputFreshnessSeconds)
            {
                hand.fresh = false;
                CancelHand(hand, HandCancelReason.StaleSample);
            }
            else if ((HasCapture(hand) && !IsCurrent(hand.capture, hand.captureRevision)) ||
                (!ReferenceEquals(hand.hover, null) && !IsCurrent(hand.hover, hand.hoverRevision)))
            {
                CancelHand(hand, HandCancelReason.TargetUnavailable);
            }
            else if (HasCapture(hand) && !CanDeliver(hand.capture))
            {
                CancelHand(hand, HandCancelReason.ModeChanged);
            }
        }

        private void ObserveOpen(HandRuntime hand, float sourceTime)
        {
            if (hand.openSamples == 0)
            {
                hand.openStartedAt = sourceTime;
            }
            hand.openSamples++;
            hand.armed = hand.openSamples >= 2 && sourceTime - hand.openStartedAt >= rearmOpenSeconds;
        }

        private static void ResetRearm(HandRuntime hand)
        {
            hand.armed = false;
            hand.openSamples = 0;
            hand.openStartedAt = 0f;
        }

        private bool CanDeliver(HandInteractable target)
        {
            return routingEnabled && isActiveAndEnabled && hasFocus && inputModeManager != null &&
                (target.IsCanvas ? inputModeManager.CanDraw && IsRegisteredCanvas(target) : inputModeManager.CanUseHandUi);
        }

        private bool IsRegisteredCanvas(HandInteractable target)
        {
            if (!(target is HandCanvasInteractable canvas)) return true;
            return canvas == activeCanvas && handPointer != null && canvas.Pointer == handPointer && handPointer.CanUseCanvas(canvas.Surface);
        }

        private HandCancelReason CaptureInvalidReason(HandRuntime hand, HandInteractable target)
        {
            if (!IsCurrent(hand.capture, hand.captureRevision))
            {
                return HandCancelReason.TargetUnavailable;
            }
            if (hand.pressContext.viewGeneration != viewGeneration)
            {
                return HandCancelReason.ViewChanged;
            }
            if (!CanDeliver(hand.capture))
            {
                return HandCancelReason.ModeChanged;
            }
            return hand.capture.RequiresInside && hand.capture != target ? HandCancelReason.TargetUnavailable : HandCancelReason.None;
        }

        private bool OwnedByOtherHand(HandRuntime hand, HandInteractable target)
        {
            if (!target.Exclusive)
            {
                return false;
            }
            return (hand != left && left.capture == target) || (hand != right && right.capture == target);
        }

        private bool UpdateHover(HandRuntime hand, HandInteractable target, Vector3 hitPosition, float now)
        {
            if (ReferenceEquals(hand.hover, target) && (target == null || hand.hoverRevision == target.LifecycleRevision))
            {
                hand.hoverPosition = hitPosition;
                return true;
            }
            HandInteractable previous = hand.hover;
            uint previousRevision = hand.hoverRevision;
            Vector3 previousPosition = hand.hoverPosition;
            hand.hover = target;
            hand.hoverRevision = target != null ? target.LifecycleRevision : 0;
            hand.hoverPosition = hitPosition;
            int revision = hand.revision;
            if (IsCurrent(previous, previousRevision))
            {
                previous.HoverExit(hand.sample, previousPosition);
            }
            if (hand.revision != revision)
            {
                return false;
            }
            if (!ReferenceEquals(target, null))
            {
                if (!IsCurrent(target, hand.hoverRevision))
                {
                    CancelHand(hand, HandCancelReason.TargetUnavailable);
                    return false;
                }
                target.HoverEnter(hand.sample, hitPosition);
                if (hand.revision != revision)
                {
                    return false;
                }
                if (!IsCurrent(target, hand.hoverRevision))
                {
                    CancelHand(hand, HandCancelReason.TargetUnavailable);
                    return false;
                }
                PlayHoverSound(now);
            }
            return true;
        }

        private void ReleaseCapture(HandRuntime hand, Vector3 hitPosition, float now)
        {
            HandInteractable target = hand.capture;
            if (!IsCurrent(target, hand.captureRevision))
            {
                CancelHand(hand, HandCancelReason.TargetUnavailable);
                return;
            }
            bool confirmsClick = !target.IsCanvas;
            float previousClickTime = 0f;
            bool hadPreviousClick = confirmsClick && lastClicks.TryGetValue(target, out previousClickTime);
            if (hadPreviousClick && now - previousClickTime < clickCooldownSeconds)
            {
                CancelHand(hand, HandCancelReason.TargetUnavailable);
                return;
            }
            float pitch = target.ClickPitch;
            int revision = hand.revision;
            // 클릭 콜백은 즉시 화면을 바꾸거나 대상을 파괴할 수 있다.
            hand.capture = null;
            hand.captureRevision = 0;
            hand.pressContext = default;
            if (confirmsClick)
            {
                lastClicks[target] = now;
            }
            bool accepted = target.Release(hand.sample, hitPosition);
            if (!accepted)
            {
                if (confirmsClick && lastClicks.TryGetValue(target, out float reservation) && reservation == now)
                {
                    if (hadPreviousClick) lastClicks[target] = previousClickTime;
                    else lastClicks.Remove(target);
                }
                if (hand.revision == revision)
                {
                    CancelHand(hand, HandCancelReason.TargetUnavailable);
                }
                return;
            }
            if (confirmsClick && audioSource != null && clickClip != null)
            {
                audioSource.pitch = pitch;
                audioSource.PlayOneShot(clickClip);
            }
        }

        private static bool HasCapture(HandRuntime hand) => !ReferenceEquals(hand.capture, null);
        private static bool IsAvailable(HandInteractable target) => target != null && target.IsAvailable;
        private static bool IsCurrent(HandInteractable target, uint revision) => IsAvailable(target) && target.LifecycleRevision == revision;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private HandRuntime GetHand(string handedness)
        {
            if (string.Equals(handedness, "Left", StringComparison.Ordinal)) return left;
            if (string.Equals(handedness, "Right", StringComparison.Ordinal)) return right;
            return null;
        }

        private static void CancelHand(HandRuntime hand, HandCancelReason reason)
        {
            HandInteractable capture = hand.capture;
            HandInteractable hover = hand.hover;
            uint captureRevision = hand.captureRevision;
            uint hoverRevision = hand.hoverRevision;
            Vector3 capturePosition = hand.capturePosition;
            Vector3 hoverPosition = hand.hoverPosition;
            hand.capture = null;
            hand.hover = null;
            hand.captureRevision = 0;
            hand.hoverRevision = 0;
            hand.pressContext = default;
            hand.revision++;
            ResetRearm(hand);
            var cancelled = new HandInputSample(hand.sample.handedness, hand.sample.screenPosition, hand.sample.sequence,
                hand.sample.sampleId, hand.sample.sampleAgeSeconds, false, false, reason);
            if (IsCurrent(capture, captureRevision))
            {
                capture.Cancel(cancelled, capturePosition);
            }
            if (IsCurrent(hover, hoverRevision))
            {
                hover.HoverExit(cancelled, hoverPosition);
            }
        }

        internal HandInteractable ResolveTarget(Vector2 screenPosition, out Vector3 hitPosition, out bool uiBlocked)
        {
            hitPosition = Vector3.zero;
            uiBlocked = false;
            if (eventSystem == null || uiRaycasters == null)
            {
                return null;
            }
            if (pointerData == null || pointerEventSystem != eventSystem)
            {
                pointerEventSystem = eventSystem;
                pointerData = new PointerEventData(eventSystem);
            }
            pointerData.Reset();
            pointerData.position = screenPosition;
            raycastResults.Clear();
            for (int i = 0; i < uiRaycasters.Length; i++)
            {
                GraphicRaycaster raycaster = uiRaycasters[i];
                if (raycaster == null || !raycaster.IsActive())
                {
                    continue;
                }
                Canvas canvas = raycaster.GetComponent<Canvas>();
                if (canvas == null || !canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    continue;
                }
                raycaster.Raycast(pointerData, raycastResults);
            }
            if (raycastResults.Count == 0)
            {
                return ResolveWorldTarget(screenPosition, out hitPosition);
            }
            raycastResults.Sort(CompareRaycasts);
            RaycastResult top = raycastResults[0];
            uiBlocked = true;
            hitPosition = screenPosition;
            HandInteractable target = top.gameObject != null ? top.gameObject.GetComponentInParent<HandInteractable>() : null;
            return IsAvailable(target) ? target : null;
        }

        private HandInteractable ResolveWorldTarget(Vector2 screenPosition, out Vector3 hitPosition)
        {
            hitPosition = screenPosition;
            if (playerCamera == null) return null;

            int hitCount = Physics.RaycastNonAlloc(playerCamera.ScreenPointToRay(screenPosition), canvasRaycastHits,
                maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (hitCount == 0) return null;

            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                nearestDistance = Mathf.Min(nearestDistance, canvasRaycastHits[i].distance);
            }

            const float coplanarTolerance = 0.0001f;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = canvasRaycastHits[i];
                if (hit.distance > nearestDistance + coplanarTolerance) continue;

                HandInteractable candidate = hit.collider.GetComponentInParent<HandInteractable>();
                if (candidate == null || !candidate.UsesWorldHitPosition || !IsAvailable(candidate)) continue;
                if (candidate is HandCanvasInteractable &&
                    (inputModeManager == null || !inputModeManager.CanDraw || candidate != activeCanvas || !IsRegisteredCanvas(candidate))) continue;
                hitPosition = hit.point;
                return candidate;
            }
            return null;
        }

        private static int CompareRaycasts(RaycastResult leftHit, RaycastResult rightHit)
        {
            if (leftHit.module != rightHit.module)
            {
                int sortPriority = rightHit.module.sortOrderPriority.CompareTo(leftHit.module.sortOrderPriority);
                if (sortPriority != 0) return sortPriority;
                int renderPriority = rightHit.module.renderOrderPriority.CompareTo(leftHit.module.renderOrderPriority);
                if (renderPriority != 0) return renderPriority;
            }
            if (leftHit.sortingLayer != rightHit.sortingLayer)
            {
                return SortingLayer.GetLayerValueFromID(rightHit.sortingLayer).CompareTo(SortingLayer.GetLayerValueFromID(leftHit.sortingLayer));
            }
            if (leftHit.sortingOrder != rightHit.sortingOrder)
            {
                return rightHit.sortingOrder.CompareTo(leftHit.sortingOrder);
            }
            if (leftHit.depth != rightHit.depth && leftHit.module.rootRaycaster == rightHit.module.rootRaycaster)
            {
                return rightHit.depth.CompareTo(leftHit.depth);
            }
            if (leftHit.distance != rightHit.distance)
            {
                return leftHit.distance.CompareTo(rightHit.distance);
            }
            return leftHit.index.CompareTo(rightHit.index);
        }

        private void PlayHoverSound(float now)
        {
            if (now - lastHoverSoundTime < 0.15f || audioSource == null || hoverClip == null)
            {
                return;
            }
            lastHoverSoundTime = now;
            audioSource.pitch = 1f;
            audioSource.PlayOneShot(hoverClip);
        }

        private void UpdateFeedback()
        {
            bool leftHover = IsAvailable(left.hover);
            bool rightHover = IsAvailable(right.hover);
            if (cursorController != null)
            {
                cursorController.SetHoverFeedback("Left", leftHover);
                cursorController.SetHoverFeedback("Right", rightHover);
            }
            if (trackingStatusLabel != null)
            {
                trackingStatusLabel.text = HasFreshHand ? string.Empty : "손을 카메라에 보여주세요";
            }
            if (hoverStatusLabel != null)
            {
                hoverStatusLabel.text = leftHover ? left.hover.DisplayName : rightHover ? right.hover.DisplayName : string.Empty;
            }
        }

        private static Vector3 PositionFor(HandInteractable target, HandInputSample sample, Vector3 worldHit)
        {
            return target != null && target.UsesWorldHitPosition ? worldHit : (Vector3)sample.screenPosition;
        }
    }
}
