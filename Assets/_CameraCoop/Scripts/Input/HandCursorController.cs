using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UI;

// 테스트 어셈블리에서 internal 순수 로직(CursorStateLogic)에 접근 허용 (docs/04 §5)
[assembly: InternalsVisibleTo("CameraCoop.Tests.EditMode")]

namespace CameraCoop
{
    // 커서 위치·색·핀치 표현, lost fade, Phase 2용 핀치 이벤트 발행 (docs/04_unity_client.md §4)
    public class HandCursorController : MonoBehaviour
    {
        private const float InputFreshnessSeconds = 0.20f;
        private const float MinimumPalmLength = 0.000001f;

        [SerializeField] private UdpHandReceiver receiver;
        [SerializeField] private RectTransform leftCursor;
        [SerializeField] private RectTransform rightCursor;
        [SerializeField] private float pinchThreshold = 0.30f;
        [SerializeField] private float pinchReleaseThreshold = 0.40f;
        [SerializeField] private float pinchScale = 0.7f;
        [SerializeField] private Color leftColor = new Color(0.2f, 0.6f, 1f);   // 청색 계열
        [SerializeField] private Color rightColor = new Color(1f, 0.6f, 0.1f);  // 주황색 계열
        [SerializeField] private float fadeDuration = 0.2f;

        // Phase 2 드로잉 접점. Phase 1에서는 발행만 하고 구독자 없음.
        public event Action<string, Vector2> OnPinchStart;
        public event Action<string, Vector2> OnPinchMove;
        public event Action<string> OnPinchEnd;
        public event Action<HandInputSample> OnHandSample;

        // 손 하나(좌/우)당 표현 상태. CanvasGroup/Image 참조는 Awake에서 1회 캐싱.
        private class HandCursorState
        {
            public RectTransform cursor;
            public CanvasGroup canvasGroup;
            public Image image;
            public Color baseColor;
            public bool pinched;
            public bool hovered;
            public float targetAlpha;
            public bool hasSample;
            public HandInputSample sample;
        }

        private HandCursorState leftState;
        private HandCursorState rightState;
        private HandPacket sampledPacket;
        private uint sampledSequence;
        private ulong sampleId;
        private float lastSampleAgeSeconds;

        private void Awake()
        {
            if (receiver == null)
            {
                Debug.LogError("[HandCursorController] Assign receiver before enabling hand input.", this);
                enabled = false;
                return;
            }
            leftState = BuildState(leftCursor, leftColor);
            rightState = BuildState(rightCursor, rightColor);
            if (leftState == null || rightState == null)
            {
                Debug.LogError("[HandCursorController] Assign leftCursor and rightCursor with CanvasGroup and Image before enabling hand input.", this);
                enabled = false;
            }
        }

        private static HandCursorState BuildState(RectTransform cursor, Color baseColor)
        {
            if (cursor == null)
            {
                return null;
            }
            CanvasGroup canvasGroup = cursor.GetComponent<CanvasGroup>();
            Image image = cursor.GetComponent<Image>();
            if (canvasGroup == null || image == null)
            {
                return null;
            }

            var state = new HandCursorState
            {
                cursor = cursor,
                canvasGroup = canvasGroup,
                image = image,
                baseColor = baseColor,
                pinched = false,
                targetAlpha = 0f
            };
            state.canvasGroup.alpha = 0f; // 시작 시 미검출 상태
            state.image.raycastTarget = false;
            return state;
        }

        private void Update()
        {
            HandPacket packet = receiver != null ? receiver.LatestPacket : null;
            bool serverLost = receiver == null || packet == null || receiver.IsServerLost;
            float age = receiver != null ? receiver.TimeSinceLastPacket : lastSampleAgeSeconds;
            ProcessPacket(packet, serverLost, age, Screen.width, Screen.height);
        }

        internal void ProcessPacket(HandPacket packet, bool serverLost, float sampleAgeSeconds, int screenWidth, int screenHeight)
        {
            if (leftState == null || rightState == null)
            {
                return;
            }
            lastSampleAgeSeconds = sampleAgeSeconds;

            if (serverLost || packet == null)
            {
                UpdateHandVisual(leftState, null, "Left", Vector2.zero, false);
                PublishCancellation(leftState, "Left", HandCancelReason.TrackingLost, sampleAgeSeconds);
                UpdateHandVisual(rightState, null, "Right", Vector2.zero, false);
                PublishCancellation(rightState, "Right", HandCancelReason.TrackingLost, sampleAgeSeconds);
                return; // lost 상태: 위치/핀치 갱신 스킵 (docs/04 §6)
            }

            bool acceptedPacket = !ReferenceEquals(packet, sampledPacket);
            if (acceptedPacket)
            {
                sampledPacket = packet;
                sampledSequence = packet.seq;
                sampleId++;
            }

            // hands 배열에서 좌/우 손 검색 (LINQ 금지, for 루프)
            HandData left = null;
            HandData right = null;
            HandData[] hands = packet.hands;
            if (hands != null)
            {
                for (int i = 0; i < hands.Length; i++)
                {
                    HandData h = hands[i];
                    if (h == null)
                    {
                        continue;
                    }
                    if (string.Equals(h.handedness, "Left", StringComparison.Ordinal))
                    {
                        left = h;
                    }
                    else if (string.Equals(h.handedness, "Right", StringComparison.Ordinal))
                    {
                        right = h;
                    }
                }
            }

            bool visualFresh = IsFinite(sampleAgeSeconds) && sampleAgeSeconds >= 0f && sampleAgeSeconds < InputFreshnessSeconds;
            ProcessHand(leftState, left, "Left", sampleAgeSeconds, screenWidth, screenHeight, acceptedPacket, visualFresh);
            ProcessHand(rightState, right, "Right", sampleAgeSeconds, screenWidth, screenHeight, acceptedPacket, visualFresh);
        }

        private void ProcessHand(HandCursorState state, HandData hand, string handedness, float sampleAgeSeconds,
            int screenWidth, int screenHeight, bool acceptedPacket, bool visualFresh)
        {
            bool valid = TryGetScreenPosition(hand, screenWidth, screenHeight, out Vector2 screenPosition);
            UpdateHandVisual(state, valid ? hand : null, handedness, screenPosition, visualFresh);

            // Legacy Move는 렌더 프레임마다 유지하고, 라우터 샘플만 수용 패킷 참조로 중복을 막는다.
            if (!acceptedPacket)
            {
                return;
            }
            if (valid)
            {
                PublishSample(state, new HandInputSample(handedness, screenPosition, sampledSequence, sampleId,
                    sampleAgeSeconds, true, state.pinched, HandCancelReason.None, HandGestureClassifier.IsFist(hand)));
                return;
            }

            HandCancelReason reason = hand == null ? HandCancelReason.TrackingLost : HandCancelReason.InvalidSample;
            PublishCancellation(state, handedness, reason, sampleAgeSeconds, hand != null);
        }

        // lost 시에도 Start-End 쌍 계약 보장 (docs/07 §4): 핀치 중이던 손이 사라지면 End를 발행한다.
        private void EndPinchIfActive(HandCursorState state, string handedness)
        {
            if (CursorStateLogic.DetermineEvent(state.pinched, nowPinched: false) == CursorStateLogic.PinchEvent.End)
            {
                state.pinched = false;
                OnPinchEnd?.Invoke(handedness);
            }
        }

        private void UpdateHand(HandCursorState state, HandData hand, string handedness)
        {
            bool valid = TryGetScreenPosition(hand, Screen.width, Screen.height, out Vector2 screenPosition);
            UpdateHandVisual(state, valid ? hand : null, handedness, screenPosition, true);
        }

        private void UpdateHandVisual(HandCursorState state, HandData hand, string handedness, Vector2 screenPos, bool visualFresh)
        {
            bool present = hand != null;
            state.targetAlpha = CursorStateLogic.TargetAlpha(present && visualFresh);
            state.canvasGroup.alpha = CursorStateLogic.StepAlpha(state.canvasGroup.alpha, state.targetAlpha, Time.deltaTime, fadeDuration);

            if (!present)
            {
                EndPinchIfActive(state, handedness);
                state.hovered = false;
                UpdateCursorAppearance(state);
                return; // 손 미검출: 위치/핀치 갱신 스킵
            }

            // 커서는 항상 화면 좌표(= 레이 원점)에 둔다. Phase 3e에서 조준이 카메라 레이캐스트로 바뀌었으므로
            // 커서를 캔버스에 투영하면 실제 조준점과 어긋난다 (docs/11 §2 — Phase 3d 투영 분기 삭제).
            state.cursor.position = screenPos;

            bool wasPinched = state.pinched;
            bool nowPinched = PinchStateMachine.Next(wasPinched, hand.pinch, pinchThreshold, pinchReleaseThreshold);
            state.pinched = nowPinched;

            UpdateCursorAppearance(state);

            switch (CursorStateLogic.DetermineEvent(wasPinched, nowPinched))
            {
                case CursorStateLogic.PinchEvent.Start:
                    OnPinchStart?.Invoke(handedness, screenPos);
                    break;
                case CursorStateLogic.PinchEvent.Move:
                    OnPinchMove?.Invoke(handedness, screenPos);
                    break;
                case CursorStateLogic.PinchEvent.End:
                    OnPinchEnd?.Invoke(handedness);
                    break;
            }
        }

        public void SetHoverFeedback(string handedness, bool hovered)
        {
            HandCursorState state = string.Equals(handedness, "Left", StringComparison.Ordinal) ? leftState
                : string.Equals(handedness, "Right", StringComparison.Ordinal) ? rightState : null;
            if (state == null)
            {
                return;
            }
            state.hovered = hovered;
            UpdateCursorAppearance(state);
        }

        private void UpdateCursorAppearance(HandCursorState state)
        {
            float scale = CursorStateLogic.Scale(state.pinched, pinchScale);
            if (state.cursor != null)
            {
                state.cursor.localScale = new Vector3(scale, scale, scale);
            }
            if (state.image != null)
            {
                float highlight = state.pinched ? 0.5f : state.hovered ? 0.3f : 0f;
                state.image.color = Color.Lerp(state.baseColor, Color.white, highlight);
            }
        }

        private void PublishSample(HandCursorState state, HandInputSample sample)
        {
            state.sample = sample;
            state.hasSample = true;
            OnHandSample?.Invoke(sample);
        }

        private void PublishCancellation(HandCursorState state, string handedness, HandCancelReason reason,
            float sampleAgeSeconds, bool acceptedInvalidSample = false)
        {
            if (!acceptedInvalidSample && state.hasSample && !state.sample.isTracked && state.sample.cancelReason == reason)
            {
                return;
            }
            Vector2 position = state.hasSample ? state.sample.screenPosition : Vector2.zero;
            PublishSample(state, new HandInputSample(handedness, position, sampledSequence, sampleId,
                sampleAgeSeconds, false, false, reason));
        }

        private void OnDisable()
        {
            DisableHand(leftState, "Left");
            DisableHand(rightState, "Right");
        }

        private void DisableHand(HandCursorState state, string handedness)
        {
            if (state == null)
            {
                return;
            }
            EndPinchIfActive(state, handedness);
            state.hovered = false;
            state.targetAlpha = 0f;
            if (state.canvasGroup != null)
            {
                state.canvasGroup.alpha = 0f;
            }
            UpdateCursorAppearance(state);
            PublishCancellation(state, handedness, HandCancelReason.ComponentDisabled, lastSampleAgeSeconds);
        }

        private static bool TryGetScreenPosition(HandData hand, int screenWidth, int screenHeight, out Vector2 screenPosition)
        {
            screenPosition = Vector2.zero;
            if (hand == null || hand.landmarks == null || hand.landmarks.Length != 63 || !IsFinite(hand.pinch) || hand.pinch < 0f)
            {
                return false;
            }
            float[] landmarks = hand.landmarks;
            for (int i = 0; i < landmarks.Length; i++)
            {
                if (!IsFinite(landmarks[i]))
                {
                    return false;
                }
            }

            double palmX = (double)landmarks[0] - landmarks[27];
            double palmY = (double)landmarks[1] - landmarks[28];
            double palmZ = (double)landmarks[2] - landmarks[29];
            double minimumLengthSquared = (double)MinimumPalmLength * MinimumPalmLength;
            if (palmX * palmX + palmY * palmY + palmZ * palmZ < minimumLengthSquared)
            {
                return false;
            }

            double centerX = ((double)landmarks[0] + landmarks[15] + landmarks[27] + landmarks[39] + landmarks[51]) / 5d;
            double centerY = ((double)landmarks[1] + landmarks[16] + landmarks[28] + landmarks[40] + landmarks[52]) / 5d;
            if (centerX < 0d || centerX > 1d || centerY < 0d || centerY > 1d)
            {
                return false;
            }
            screenPosition = HandScreenMapper.ToScreen((float)centerX, (float)centerY, screenWidth, screenHeight);
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    // 프레임당 커서 상태 판정을 MonoBehaviour 밖으로 분리한 순수 함수 모음 (docs/04 §5 테스트 가능 설계).
    internal static class CursorStateLogic
    {
        public enum PinchEvent { None, Start, Move, End }

        // 손 검출 여부 → 목표 알파
        public static float TargetAlpha(bool present)
        {
            return present ? 1f : 0f;
        }

        // fadeDuration 기울기로 알파를 목표 방향으로 이동 (0 이하면 즉시 도달)
        public static float StepAlpha(float currentAlpha, float targetAlpha, float deltaTime, float fadeDuration)
        {
            if (fadeDuration <= 0f)
            {
                return targetAlpha;
            }
            float maxDelta = deltaTime / fadeDuration;
            return Mathf.MoveTowards(currentAlpha, targetAlpha, maxDelta);
        }

        // 핀치 여부 → 커서 스케일 배율
        public static float Scale(bool pinched, float pinchScale)
        {
            return pinched ? pinchScale : 1f;
        }

        // 이전/현재 핀치 상태 전이 → 발행할 이벤트 종류
        public static PinchEvent DetermineEvent(bool wasPinched, bool nowPinched)
        {
            if (!wasPinched && nowPinched)
            {
                return PinchEvent.Start;
            }
            if (wasPinched && nowPinched)
            {
                return PinchEvent.Move;
            }
            if (wasPinched && !nowPinched)
            {
                return PinchEvent.End;
            }
            return PinchEvent.None;
        }
    }
}
