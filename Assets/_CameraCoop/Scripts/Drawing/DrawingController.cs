using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CameraCoop
{
    // HandPointer의 조준 결과(캔버스 위 월드 지점)를 받아 LineRenderer 스트로크를 그린다 (docs/11 §2).
    // Phase 3d까지는 화면 좌표를 직접 평면에 투영했지만, 이제 좌표 계산은 HandPointer가 단독으로 한다.
    // 색·두께·브러시·머티리얼은 ToolState에서 읽고 스트로크 시작 시점 값으로 고정한다.
    public class DrawingController : MonoBehaviour
    {
        [SerializeField] private HandPointer handPointer;
        [SerializeField] private ToolState toolState;
        [SerializeField] private CanvasSurface canvasSurface;
        [SerializeField, Min(0f)] private float minPointDistance = 0.01f;         // 점 추가 최소 간격 (월드 단위)
        [SerializeField, Min(0f)] private float maxSegmentWorldDistance = 0.6f;   // 캔버스 폭(2.4)의 0.25. 초과 시 스트로크 분리 (docs/07 §6)
        [SerializeField] private Material lineMaterial;                           // 브러시 머티리얼 미할당 시 폴백
        [SerializeField] private Key clearKey = Key.C;                            // 새 Input System 전용 (docs/07 §5)

        public event Action<int, string> OnLocalStrokeStarted;  // localId, hand — NetSession이 전역 strokeId와 매핑한다
        public event Action<int> OnLocalStrokeErased;           // 지운 로컬 스트로크의 localId

        private class ActiveStroke
        {
            public int localId;
            public LineRenderer line;
            public Vector3 lastPoint;
            public List<Vector3> points;
            public CanvasStrokeData data;
            public List<float> xy;
        }

        private class FinishedStroke
        {
            public int localId;
            public GameObject go;
            public List<Vector3> points;   // 지우개 판정용. LineRenderer.GetPositions는 할당이 생긴다
            public CanvasStrokeData data;
        }

        // 손별 진행 중 스트로크 (Left/Right 키). 확정분은 finishedStrokes로 이동.
        private readonly Dictionary<string, ActiveStroke> activeStrokes = new Dictionary<string, ActiveStroke>(2);
        private readonly List<FinishedStroke> finishedStrokes = new List<FinishedStroke>();
        private int localIdCounter;
        private int orderCounter = -1;

        private bool IsLocalDrawing { get { return handPointer != null && handPointer.InputSource == HandPointerInputSource.HandRouter; } }

        private void Awake()
        {
            if (handPointer == null || toolState == null)
            {
                // 조용한 실패 금지 (docs/10 §7)
                Debug.LogError("[DrawingController] handPointer/toolState 미할당 — 로컬 드로잉이 동작하지 않습니다 (docs/11 §4)");
            }
            else if (IsLocalDrawing)
            {
                RequireLocalDrawing();
            }
        }

        private void OnEnable()
        {
            if (handPointer == null)
            {
                return;
            }
            handPointer.OnCanvasStrokeStart += HandleStrokeStart;
            handPointer.OnCanvasStrokeMove += HandleStrokeMove;
            handPointer.OnCanvasStrokeEnd += HandleStrokeEnd;
            handPointer.OnCanvasErase += HandleErase;
        }

        private void OnDisable()
        {
            if (IsLocalDrawing) FinalizeActiveStrokes();
            if (handPointer == null)
            {
                return;
            }
            handPointer.OnCanvasStrokeStart -= HandleStrokeStart;
            handPointer.OnCanvasStrokeMove -= HandleStrokeMove;
            handPointer.OnCanvasStrokeEnd -= HandleStrokeEnd;
            handPointer.OnCanvasErase -= HandleErase;
        }

        private void OnDestroy() { DestroyStrokeObjects(); }

        private void Update()
        {
            // Keyboard 부재(장치 없음) 방어. legacy Input API는 이 프로젝트에서 예외를 던진다.
            Keyboard keyboard = Keyboard.current;
            if (clearKey != Key.None && keyboard != null && keyboard[clearKey].wasPressedThisFrame && !InputFocus.IsTyping)
            {
                // 정답 타이핑 중 C키가 캔버스를 지우는 사고 방지 (docs/12 §2)
                ClearAll();
            }
        }

        private void HandleStrokeStart(string handedness, Vector2 norm, Vector3 world)
        {
            if (IsLocalDrawing)
            {
                if (!RequireLocalDrawing() || !IsValidNorm(norm)) return;
                world = canvasSurface.NormToWorld(norm);
            }
            switch (StrokeLogic.Decide(activeStrokes.ContainsKey(handedness), StrokeLogic.PinchKind.Start))
            {
                case StrokeLogic.StrokeAction.EndThenStartNew: // 방어: 중복 Start (docs/07 §6)
                    FinishStroke(handedness);
                    BeginStroke(handedness, norm, world);
                    break;
                case StrokeLogic.StrokeAction.StartNew:
                    BeginStroke(handedness, norm, world);
                    break;
            }
        }

        private void HandleStrokeMove(string handedness, Vector2 norm, Vector3 world)
        {
            if (IsLocalDrawing)
            {
                if (!RequireLocalDrawing() || !IsValidNorm(norm)) return;
                world = canvasSurface.NormToWorld(norm);
            }
            if (StrokeLogic.Decide(activeStrokes.ContainsKey(handedness), StrokeLogic.PinchKind.Move) != StrokeLogic.StrokeAction.Append)
            {
                return; // 고아 Move (docs/07 §6)
            }

            ActiveStroke stroke = activeStrokes[handedness];
            if (StrokeLogic.ShouldSplitStroke(stroke.lastPoint, world, maxSegmentWorldDistance))
            {
                FinishStroke(handedness); // 재검출 스냅: 점프 선 대신 스트로크 분리 (docs/07 §6)
                BeginStroke(handedness, norm, world);
                return;
            }
            if (!StrokeLogic.ShouldAppendPoint(hasLastPoint: true, stroke.lastPoint, world, minPointDistance))
            {
                return;
            }

            int count = stroke.line.positionCount;
            stroke.line.positionCount = count + 1;
            stroke.line.SetPosition(count, world);
            stroke.points.Add(world);
            if (stroke.xy != null)
            {
                stroke.xy.Add(norm.x);
                stroke.xy.Add(norm.y);
            }
            stroke.lastPoint = world;
        }

        private void HandleStrokeEnd(string handedness)
        {
            if (StrokeLogic.Decide(activeStrokes.ContainsKey(handedness), StrokeLogic.PinchKind.End) == StrokeLogic.StrokeAction.End)
            {
                FinishStroke(handedness);
            }
        }

        // 닿은 스트로크를 통째로 지운다 (docs/11 §2). 완료 스트로크만 대상 — 그리던 선은 지우지 않는다.
        // ponytail: 선형 스캔 O(스트로크x점). 수백 스트로크에서 체감되면 그때 공간 분할
        private void HandleErase(Vector3 world)
        {
            float radius = toolState != null ? toolState.EraseRadius : 0.05f;
            for (int i = finishedStrokes.Count - 1; i >= 0; i--) // 최근 것부터, 첫 hit 1개만
            {
                if (!EraseLogic.HitsStroke(finishedStrokes[i].points, world, radius))
                {
                    continue;
                }
                FinishedStroke hit = finishedStrokes[i];
                finishedStrokes.RemoveAt(i);
                if (hit.go != null)
                {
                    CanvasDrawingRender.DestroyOwned(hit.go);
                }
                if (IsLocalDrawing) RefreshRenderOrder();
                OnLocalStrokeErased?.Invoke(hit.localId);
                return;
            }
        }

        private void BeginStroke(string handedness, Vector2 norm, Vector3 world)
        {
            if (localIdCounter == int.MaxValue || (IsLocalDrawing && orderCounter == int.MaxValue))
            {
                Debug.LogError("[DrawingController] Stroke id/order capacity exhausted.");
                return;
            }
            CanvasStrokeData data = null;
            if (IsLocalDrawing)
            {
                float width = toolState.CurrentWidth / CanvasDrawingRender.ShortSide(canvasSurface);
                if (!CanvasDrawingData.IsFinite(width) || width <= 0f ||
                    toolState.CurrentBrushIndex < 0 || toolState.CurrentBrushIndex >= toolState.BrushCount)
                {
                    Debug.LogError("[DrawingController] Invalid local brush style.");
                    return;
                }
                Color32 packedColor = toolState.CurrentColor;
                data = new CanvasStrokeData
                {
                    strokeId = localIdCounter + 1,
                    order = ++orderCounter,
                    colorArgb = (packedColor.a << 24) | (packedColor.r << 16) | (packedColor.g << 8) | packedColor.b,
                    widthNormalized = width,
                    brushId = toolState.CurrentBrushIndex,
                    xy = new[] { norm.x, norm.y }
                };
            }
            var strokeObject = new GameObject("Stroke_" + handedness);
            strokeObject.transform.SetParent(transform, worldPositionStays: true);

            LineRenderer line = strokeObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.numCapVertices = 4;    // ~14Hz 입력의 각짐 완화 (docs/07 §3)
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            // 도구 값은 시작 시점에 고정한다 — 그리는 중 팔레트를 눌러도 진행 중 선은 바뀌지 않는다
            line.widthMultiplier = toolState != null ? toolState.CurrentWidth : 0.02f;
            Material material = toolState != null ? toolState.CurrentMaterial : null;
            line.sharedMaterial = material != null ? material : lineMaterial;
            Color color = toolState != null ? toolState.CurrentColor : Color.black;
            line.startColor = color;
            line.endColor = color;

            line.positionCount = 1;
            line.SetPosition(0, world);

            int localId = ++localIdCounter; // 1부터 단조 증가
            activeStrokes[handedness] = new ActiveStroke
            {
                localId = localId,
                line = line,
                lastPoint = world,
                points = new List<Vector3>(64) { world },
                data = data,
                xy = data != null ? new List<float>(128) { norm.x, norm.y } : null
            };
            if (data != null) RefreshRenderOrder();
            OnLocalStrokeStarted?.Invoke(localId, handedness);
        }

        private void FinishStroke(string handedness)
        {
            ActiveStroke stroke = activeStrokes[handedness];
            activeStrokes.Remove(handedness);

            if (StrokeLogic.ShouldDiscardOnEnd(stroke.line.positionCount))
            {
                CanvasDrawingRender.DestroyOwned(stroke.line.gameObject); // 점 찍기 미지원 (docs/07 §6)
                if (stroke.data != null) RefreshRenderOrder();
                return;
            }
            if (stroke.data != null) stroke.data.xy = stroke.xy.ToArray();
            finishedStrokes.Add(new FinishedStroke
            {
                localId = stroke.localId,
                go = stroke.line.gameObject,
                points = stroke.points,
                data = stroke.data
            });
            if (stroke.data != null)
            {
                finishedStrokes.Sort((left, right) => left.data.order.CompareTo(right.data.order));
                RefreshRenderOrder();
            }
        }

        public void ClearAll()
        {
            if (IsLocalDrawing) FinalizeActiveStrokes();
            DestroyStrokeObjects();
        }

        private void DestroyStrokeObjects()
        {
            for (int i = 0; i < finishedStrokes.Count; i++)
            {
                if (finishedStrokes[i].go != null)
                {
                    CanvasDrawingRender.DestroyOwned(finishedStrokes[i].go);
                }
            }
            finishedStrokes.Clear();

            foreach (KeyValuePair<string, ActiveStroke> pair in activeStrokes)
            {
                CanvasDrawingRender.DestroyOwned(pair.Value.line.gameObject);
            }
            activeStrokes.Clear(); // 이후 Move/End는 고아로 무시된다 (docs/07 §6)
        }

        public void FinalizeActiveStrokes()
        {
            var hands = new List<string>(activeStrokes.Keys);
            foreach (string hand in hands) FinishStroke(hand);
        }

        public CanvasDrawingData ExportDrawing()
        {
            if (!RequireLocalDrawing()) return null;
            FinalizeActiveStrokes();
            var strokes = new CanvasStrokeData[finishedStrokes.Count];
            for (int i = 0; i < strokes.Length; i++) strokes[i] = finishedStrokes[i].data.Copy();
            return new CanvasDrawingData { strokes = strokes };
        }

        public bool LoadDrawing(CanvasDrawingData data)
        {
            if (!RequireLocalDrawing()) return false;
            CanvasDrawingData copy;
            string error;
            if (!CanvasDrawingData.TryCopy(data, toolState.BrushCount, out copy, out error))
            {
                Debug.LogError("[DrawingController] " + error);
                return false;
            }

            ClearAll();
            foreach (CanvasStrokeData stroke in copy.strokes)
            {
                Material brush = toolState.GetBrushMaterial(stroke.brushId);
                LineRenderer line = CanvasDrawingRender.Create(stroke, canvasSurface, transform, brush != null ? brush : lineMaterial);
                var points = new List<Vector3>(stroke.xy.Length / 2);
                for (int i = 0; i < stroke.xy.Length; i += 2)
                    points.Add(canvasSurface.NormToWorld(new Vector2(stroke.xy[i], stroke.xy[i + 1])));
                finishedStrokes.Add(new FinishedStroke { localId = stroke.strokeId, go = line.gameObject, points = points, data = stroke });
                localIdCounter = Math.Max(localIdCounter, stroke.strokeId);
                orderCounter = Math.Max(orderCounter, stroke.order);
            }
            RefreshRenderOrder();
            return true;
        }

        public bool UndoLastStroke()
        {
            if (!RequireLocalDrawing()) return false;
            FinalizeActiveStrokes();
            if (finishedStrokes.Count == 0) return false;
            int index = finishedStrokes.Count - 1;
            CanvasDrawingRender.DestroyOwned(finishedStrokes[index].go);
            finishedStrokes.RemoveAt(index);
            RefreshRenderOrder();
            return true;
        }

        private bool RequireLocalDrawing()
        {
            if (!IsLocalDrawing || toolState == null || canvasSurface == null || !CanvasDrawingRender.HasValidSize(canvasSurface))
            {
                Debug.LogError("[DrawingController] Local drawing requires HandRouter, ToolState and a non-degenerate CanvasSurface.");
                return false;
            }
            return true;
        }

        private static bool IsValidNorm(Vector2 norm)
        {
            return CanvasDrawingData.IsFinite(norm.x) && CanvasDrawingData.IsFinite(norm.y) &&
                norm.x >= 0f && norm.x <= 1f && norm.y >= 0f && norm.y <= 1f;
        }

        private void RefreshRenderOrder()
        {
            var lines = new List<KeyValuePair<int, LineRenderer>>(finishedStrokes.Count + activeStrokes.Count);
            foreach (FinishedStroke stroke in finishedStrokes)
                lines.Add(new KeyValuePair<int, LineRenderer>(stroke.data.order, stroke.go.GetComponent<LineRenderer>()));
            foreach (ActiveStroke stroke in activeStrokes.Values)
                lines.Add(new KeyValuePair<int, LineRenderer>(stroke.data.order, stroke.line));
            lines.Sort((left, right) => left.Key.CompareTo(right.Key));
            for (int i = 0; i < lines.Count; i++) lines[i].Value.sortingOrder = i;
        }
    }
}
