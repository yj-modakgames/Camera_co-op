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

        // 손 하나(좌/우)당 표현 상태. CanvasGroup/Image 참조는 Awake에서 1회 캐싱.
        private class HandCursorState
        {
            public RectTransform cursor;
            public CanvasGroup canvasGroup;
            public Image image;
            public Color baseColor;
            public bool pinched;
            public float targetAlpha;
        }

        private HandCursorState leftState;
        private HandCursorState rightState;

        private void Awake()
        {
            leftState = BuildState(leftCursor, leftColor);
            rightState = BuildState(rightCursor, rightColor);
        }

        private static HandCursorState BuildState(RectTransform cursor, Color baseColor)
        {
            var state = new HandCursorState
            {
                cursor = cursor,
                canvasGroup = cursor.GetComponent<CanvasGroup>(),
                image = cursor.GetComponent<Image>(),
                baseColor = baseColor,
                pinched = false,
                targetAlpha = 0f
            };
            state.canvasGroup.alpha = 0f; // 시작 시 미검출 상태
            return state;
        }

        private void Update()
        {
            HandPacket packet = receiver != null ? receiver.LatestPacket : null;
            bool serverLost = receiver == null || packet == null || receiver.IsServerLost;

            if (serverLost)
            {
                EndPinchIfActive(leftState, "Left");
                EndPinchIfActive(rightState, "Right");
                FadeOut(leftState);
                FadeOut(rightState);
                return; // lost 상태: 위치/핀치 갱신 스킵 (docs/04 §6)
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

            UpdateHand(leftState, left, "Left");
            UpdateHand(rightState, right, "Right");
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

        private void FadeOut(HandCursorState state)
        {
            state.targetAlpha = 0f;
            state.canvasGroup.alpha = CursorStateLogic.StepAlpha(state.canvasGroup.alpha, state.targetAlpha, Time.deltaTime, fadeDuration);
        }

        private void UpdateHand(HandCursorState state, HandData hand, string handedness)
        {
            bool present = hand != null;
            state.targetAlpha = CursorStateLogic.TargetAlpha(present);
            state.canvasGroup.alpha = CursorStateLogic.StepAlpha(state.canvasGroup.alpha, state.targetAlpha, Time.deltaTime, fadeDuration);

            if (!present)
            {
                EndPinchIfActive(state, handedness);
                return; // 손 미검출: 위치/핀치 갱신 스킵
            }

            Vector3 palm = hand.GetPalmCenter();
            // 커서는 항상 화면 좌표(= 레이 원점)에 둔다. Phase 3e에서 조준이 카메라 레이캐스트로 바뀌었으므로
            // 커서를 캔버스에 투영하면 실제 조준점과 어긋난다 (docs/11 §2 — Phase 3d 투영 분기 삭제).
            Vector2 screenPos = HandScreenMapper.ToScreen(palm.x, palm.y, Screen.width, Screen.height);
            state.cursor.position = screenPos;

            bool wasPinched = state.pinched;
            bool nowPinched = PinchStateMachine.Next(wasPinched, hand.pinch, pinchThreshold, pinchReleaseThreshold);
            state.pinched = nowPinched;

            float scale = CursorStateLogic.Scale(nowPinched, pinchScale);
            state.cursor.localScale = new Vector3(scale, scale, scale);
            state.image.color = nowPinched ? Color.Lerp(state.baseColor, Color.white, 0.5f) : state.baseColor;

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
