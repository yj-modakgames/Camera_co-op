using System;
using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop
{
    // 레이캐스트 조준의 단독 소유자 (docs/11 §2). 핀치 이벤트를 받아 카메라에서 레이를 쏘고,
    // 맞은 것이 팔레트 버튼인지 캔버스인지에 따라 도구 클릭 / 드로잉 / 지우개로 갈라 발행한다.
    // 이동해도 그리는 위치가 따라오는 이유가 여기다 — norm이 카메라 조준에서 파생된다.
    public class HandPointer : MonoBehaviour
    {
        [SerializeField] private HandCursorController cursorController;
        [SerializeField] private Camera aimCamera;
        [SerializeField] private CanvasSurface canvasSurface;
        [SerializeField] private ToolState toolState;
        [SerializeField, Min(0f)] private float maxDistance = 20f;

        public event Action<string, Vector2, Vector3> OnCanvasStrokeStart;  // hand, norm, world
        public event Action<string, Vector2, Vector3> OnCanvasStrokeMove;
        public event Action<string> OnCanvasStrokeEnd;
        public event Action<Vector3> OnCanvasErase;                         // world. Erase 모드에서 Start/Move 대신 발행
        public event Action<ToolButton> OnToolClicked;

        // 손별 "핀치가 캔버스에서 활성" 상태. Erase 모드에서도 true가 된다 (드래그 지우기 유지).
        private readonly Dictionary<string, bool> isDrawing = new Dictionary<string, bool>(2);

        private bool strokesEnabled = true;

        // 기본 true. false 설정 순간 진행 중 스트로크 전부 End 발행 — 도구 클릭은 계속 허용 (docs/12 §2).
        // GameSession이 라운드 전이마다 "로컬 플레이어 == 출제자"로 갱신한다.
        public bool StrokesEnabled
        {
            get { return strokesEnabled; }
            set
            {
                if (strokesEnabled == value)
                {
                    return;
                }
                strokesEnabled = value;
                if (!strokesEnabled)
                {
                    // 라운드 종료 시 그리다 만 선이 고아가 되지 않게 진행 중 스트로크를 전부 회수 (docs/12 §5)
                    var drawingHands = new List<string>(isDrawing.Count);
                    foreach (KeyValuePair<string, bool> pair in isDrawing)
                    {
                        if (pair.Value)
                        {
                            drawingHands.Add(pair.Key);
                        }
                    }
                    for (int i = 0; i < drawingHands.Count; i++)
                    {
                        EndStroke(drawingHands[i]);
                    }
                }
            }
        }

        private void Awake()
        {
            if (cursorController == null || aimCamera == null || canvasSurface == null || toolState == null)
            {
                // 조용한 실패 금지 — docs/10 §7의 drawCamera 가드 누락 재발 방지
                Debug.LogError("[HandPointer] 필수 참조 미할당 (cursorController/aimCamera/canvasSurface/toolState) — 조준을 비활성화합니다 (docs/11 §2)");
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (cursorController == null)
            {
                return;
            }
            cursorController.OnPinchStart += HandlePinchStart;
            cursorController.OnPinchMove += HandlePinchMove;
            cursorController.OnPinchEnd += HandlePinchEnd;
        }

        private void OnDisable()
        {
            if (cursorController == null)
            {
                return;
            }
            cursorController.OnPinchStart -= HandlePinchStart;
            cursorController.OnPinchMove -= HandlePinchMove;
            cursorController.OnPinchEnd -= HandlePinchEnd;
        }

        private void HandlePinchStart(string hand, Vector2 screenPos)
        {
            Route(hand, screenPos, StrokeLogic.PinchKind.Start);
        }

        private void HandlePinchMove(string hand, Vector2 screenPos)
        {
            Route(hand, screenPos, StrokeLogic.PinchKind.Move);
        }

        // End는 레이를 쏘지 않는다 — 손이 이미 사라졌을 수 있고, 분기표상 hit과 무관하게 끝난다 (docs/11 §2)
        private void HandlePinchEnd(string hand)
        {
            if (PointerRouteLogic.Decide(PointerRouteLogic.HitKind.None, StrokeLogic.PinchKind.End, IsDrawing(hand))
                != PointerRouteLogic.RouteAction.EndStroke)
            {
                return;
            }
            EndStroke(hand);
        }

        private void Route(string hand, Vector2 screenPos, StrokeLogic.PinchKind kind)
        {
            // 레이는 핀치 이벤트당 1회. Update에서 매 프레임 쏘지 않는다 (docs/11 주의 §2)
            RaycastHit hit;
            var hitKind = PointerRouteLogic.HitKind.None;
            ToolButton button = null;
            if (Physics.Raycast(aimCamera.ScreenPointToRay(screenPos), out hit, maxDistance))
            {
                // 레이어 추가 없이 컴포넌트 유무로 구분한다 (ProjectSettings 무변경, docs/11 §2)
                button = hit.collider.GetComponentInParent<ToolButton>();
                if (button != null)
                {
                    hitKind = PointerRouteLogic.HitKind.Tool;
                }
                else if (hit.collider.GetComponentInParent<CanvasSurface>() != null)
                {
                    hitKind = PointerRouteLogic.HitKind.Canvas;
                }
            }

            switch (PointerRouteLogic.Decide(hitKind, kind, IsDrawing(hand), StrokesEnabled))
            {
                case PointerRouteLogic.RouteAction.ClickTool:
                    OnToolClicked?.Invoke(button);
                    break;
                case PointerRouteLogic.RouteAction.StartStroke:
                    isDrawing[hand] = true;
                    Emit(hand, hit.point, isStart: true);
                    break;
                case PointerRouteLogic.RouteAction.AppendStroke:
                    Emit(hand, hit.point, isStart: false);
                    break;
                case PointerRouteLogic.RouteAction.EndStroke:
                    EndStroke(hand);
                    break;
            }
        }

        // Erase 모드면 스트로크 대신 지우개 이벤트를 낸다 (docs/11 §2)
        private void Emit(string hand, Vector3 world, bool isStart)
        {
            if (toolState.CurrentMode == ToolState.Mode.Erase)
            {
                OnCanvasErase?.Invoke(world);
                return;
            }
            Vector2 norm = canvasSurface.WorldToNorm(world);
            world = canvasSurface.NormToWorld(norm); // 충돌체 표면이 아니라 원격 잉크와 같은 offset 평면에 그린다.
            if (isStart)
            {
                OnCanvasStrokeStart?.Invoke(hand, norm, world);
            }
            else
            {
                OnCanvasStrokeMove?.Invoke(hand, norm, world);
            }
        }

        private void EndStroke(string hand)
        {
            isDrawing[hand] = false;
            OnCanvasStrokeEnd?.Invoke(hand);
        }

        private bool IsDrawing(string hand)
        {
            bool drawing;
            return isDrawing.TryGetValue(hand, out drawing) && drawing;
        }
    }
}
