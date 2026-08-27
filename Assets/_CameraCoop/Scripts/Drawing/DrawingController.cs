using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CameraCoop
{
    // 핀치 이벤트를 구독해 카메라 앞 고정 평면에 LineRenderer 스트로크를 그린다 (docs/07).
    // 기존 컴포넌트는 이 클래스의 존재를 모른다 — 참조는 단방향 (docs/01 §4).
    public class DrawingController : MonoBehaviour
    {
        [SerializeField] private HandCursorController cursorController;
        [SerializeField] private Camera drawCamera;
        [SerializeField] private float planeDistance = 5.0f;      // 카메라 -> 드로잉 평면 거리 (m)
        [SerializeField, Min(0f)] private float minPointDistance = 0.01f;  // 점 추가 최소 간격 (월드 단위)
        [SerializeField] private float lineWidth = 0.02f;
        [SerializeField, Min(0f)] private float maxSegmentScreenFraction = 0.25f;  // 연속 점 허용 최대 화면 이동 비율 (초과 시 스트로크 분리)
        [SerializeField] private Material lineMaterial;           // vertex color를 곱하는 셰이더여야 함 (URP Particles/Unlit)
        [SerializeField] private CanvasSurface canvasSurface;    // 할당 시 월드 캔버스에 그림 (docs/10 §2). 미할당 = 기존 카메라 평면
        [SerializeField] private Color leftStrokeColor = new Color(0.2f, 0.6f, 1f);   // 커서 색과 같은 계열
        [SerializeField] private Color rightStrokeColor = new Color(1f, 0.6f, 0.1f);
        [SerializeField] private Key clearKey = Key.C;            // 새 Input System 전용 (docs/07 §5)

        private class ActiveStroke
        {
            public LineRenderer line;
            public Vector3 lastPoint;
            public Vector2 lastScreenPos;
        }

        // 손별 진행 중 스트로크 ("Left"/"Right" 키). 확정분은 finishedStrokes로 이동.
        private readonly Dictionary<string, ActiveStroke> activeStrokes = new Dictionary<string, ActiveStroke>(2);
        private readonly List<GameObject> finishedStrokes = new List<GameObject>();

        private void OnEnable()
        {
            cursorController.OnPinchStart += HandlePinchStart;
            cursorController.OnPinchMove += HandlePinchMove;
            cursorController.OnPinchEnd += HandlePinchEnd;
        }

        private void OnDisable()
        {
            cursorController.OnPinchStart -= HandlePinchStart;
            cursorController.OnPinchMove -= HandlePinchMove;
            cursorController.OnPinchEnd -= HandlePinchEnd;
        }

        private void Update()
        {
            // Keyboard 부재(장치 없음) 방어. legacy Input API는 이 프로젝트에서 예외를 던진다.
            Keyboard keyboard = Keyboard.current;
            if (clearKey != Key.None && keyboard != null && keyboard[clearKey].wasPressedThisFrame)
            {
                ClearAll();
            }
        }

        private void HandlePinchStart(string handedness, Vector2 screenPos)
        {
            switch (StrokeLogic.Decide(activeStrokes.ContainsKey(handedness), StrokeLogic.PinchKind.Start))
            {
                case StrokeLogic.StrokeAction.EndThenStartNew: // 방어: 중복 Start (docs/07 §6)
                    FinishStroke(handedness);
                    BeginStroke(handedness, screenPos);
                    break;
                case StrokeLogic.StrokeAction.StartNew:
                    BeginStroke(handedness, screenPos);
                    break;
            }
        }

        private void HandlePinchMove(string handedness, Vector2 screenPos)
        {
            if (StrokeLogic.Decide(activeStrokes.ContainsKey(handedness), StrokeLogic.PinchKind.Move) != StrokeLogic.StrokeAction.Append)
            {
                return; // 고아 Move (docs/07 §6)
            }

            ActiveStroke stroke = activeStrokes[handedness];
            if (StrokeLogic.ShouldSplitStroke(stroke.lastScreenPos, screenPos, maxSegmentScreenFraction * Screen.width))
            {
                FinishStroke(handedness); // 재검출 스냅: 점프 선 대신 스트로크 분리 (docs/07 §6)
                BeginStroke(handedness, screenPos);
                return;
            }
            Vector3 point = ToPlanePoint(screenPos);
            if (!StrokeLogic.ShouldAppendPoint(hasLastPoint: true, stroke.lastPoint, point, minPointDistance))
            {
                return;
            }

            int count = stroke.line.positionCount;
            stroke.line.positionCount = count + 1;
            stroke.line.SetPosition(count, point);
            stroke.lastPoint = point;
            stroke.lastScreenPos = screenPos;
        }

        private void HandlePinchEnd(string handedness)
        {
            if (StrokeLogic.Decide(activeStrokes.ContainsKey(handedness), StrokeLogic.PinchKind.End) == StrokeLogic.StrokeAction.End)
            {
                FinishStroke(handedness);
            }
        }

        // 화면 좌표 -> 드로잉 표면 월드 좌표. canvasSurface 할당 시 월드 캔버스(docs/10 §2), 미할당 시 카메라 앞 평면(docs/07 §3)
        private Vector3 ToPlanePoint(Vector2 screenPos)
        {
            if (canvasSurface != null)
            {
                return canvasSurface.NormToWorld(HandScreenMapper.ToNormalized(screenPos, Screen.width, Screen.height));
            }
            return drawCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, planeDistance));
        }

        private void BeginStroke(string handedness, Vector2 screenPos)
        {
            var strokeObject = new GameObject("Stroke_" + handedness);
            strokeObject.transform.SetParent(transform, worldPositionStays: true);

            LineRenderer line = strokeObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = lineWidth;
            line.sharedMaterial = lineMaterial;
            line.numCapVertices = 4;    // ~14Hz 입력의 각짐 완화 (docs/07 §3)
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Color color = string.Equals(handedness, "Left", System.StringComparison.Ordinal) ? leftStrokeColor : rightStrokeColor;
            line.startColor = color;
            line.endColor = color;

            Vector3 point = ToPlanePoint(screenPos);
            line.positionCount = 1;
            line.SetPosition(0, point);

            activeStrokes[handedness] = new ActiveStroke { line = line, lastPoint = point, lastScreenPos = screenPos };
        }

        private void FinishStroke(string handedness)
        {
            ActiveStroke stroke = activeStrokes[handedness];
            activeStrokes.Remove(handedness);

            if (StrokeLogic.ShouldDiscardOnEnd(stroke.line.positionCount))
            {
                Destroy(stroke.line.gameObject); // 점 찍기 미지원 (docs/07 §6)
                return;
            }
            finishedStrokes.Add(stroke.line.gameObject);
        }

        public void ClearAll()
        {
            for (int i = 0; i < finishedStrokes.Count; i++)
            {
                if (finishedStrokes[i] != null)
                {
                    Destroy(finishedStrokes[i]);
                }
            }
            finishedStrokes.Clear();

            foreach (KeyValuePair<string, ActiveStroke> pair in activeStrokes)
            {
                Destroy(pair.Value.line.gameObject);
            }
            activeStrokes.Clear(); // 이후 Move/End는 고아로 무시된다 (docs/07 §6)
        }
    }
}
