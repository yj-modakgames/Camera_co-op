using CameraCoop;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/11 §2 조준 규칙 + §5 엣지 케이스 — (hit 종류 × 핀치 종류 × 그리는 중) 분기표 전건.
    // 내부(internal) 열거형은 public 테스트 메서드 파라미터로 쓸 수 없어(CS0051) 표를 본문에 둔다.
    public class PointerRouteTests
    {
        [Test]
        public void CanvasRaycast_EmitsInkAtSurfaceOffset()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var rig = new GameObject("pointer test");
            rig.SetActive(false);
            try
            {
                quad.transform.position = new Vector3(100f, 100f, 0f);
                var surface = quad.AddComponent<CanvasSurface>();
                var camera = rig.AddComponent<Camera>();
                camera.transform.position = new Vector3(100f, 100f, -3f);
                camera.pixelRect = new Rect(0f, 0f, 1920f, 1080f);
                var state = rig.AddComponent<ToolState>();
                var pointer = rig.AddComponent<HandPointer>();
                var so = new SerializedObject(pointer);
                so.FindProperty("aimCamera").objectReferenceValue = camera;
                so.FindProperty("canvasSurface").objectReferenceValue = surface;
                so.FindProperty("toolState").objectReferenceValue = state;
                so.ApplyModifiedPropertiesWithoutUndo();
                Vector3 emitted = Vector3.positiveInfinity;
                pointer.OnCanvasStrokeStart += (hand, norm, world) => emitted = world;
                Physics.SyncTransforms();
                Vector2 screen = camera.WorldToScreenPoint(quad.transform.position);
                typeof(HandPointer).GetMethod("HandlePinchStart", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(pointer, new object[] { "Right", screen });
                Assert.AreEqual(-0.005f, surface.transform.InverseTransformPoint(emitted).z, 1e-5f,
                    "로컬 잉크도 원격 잉크와 같은 surfaceOffset에 있어야 한다");
            }
            finally
            {
                Object.DestroyImmediate(rig);
                Object.DestroyImmediate(quad);
            }
        }

        private struct Row
        {
            public PointerRouteLogic.HitKind Hit;
            public bool IsDrawing;
            public PointerRouteLogic.RouteAction Expected;
        }

        private static Row R(PointerRouteLogic.HitKind hit, bool isDrawing, PointerRouteLogic.RouteAction expected)
        {
            return new Row { Hit = hit, IsDrawing = isDrawing, Expected = expected };
        }

        private static void AssertRows(StrokeLogic.PinchKind kind, Row[] rows)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.AreEqual(rows[i].Expected, PointerRouteLogic.Decide(rows[i].Hit, kind, rows[i].IsDrawing),
                    kind + " / " + rows[i].Hit + " / isDrawing=" + rows[i].IsDrawing);
            }
        }

        [Test]
        public void Decide_Start_CanvasStartsStroke_ToolClicks_MissDoesNothing()
        {
            AssertRows(StrokeLogic.PinchKind.Start, new Row[]
            {
                R(PointerRouteLogic.HitKind.Canvas, false, PointerRouteLogic.RouteAction.StartStroke),
                R(PointerRouteLogic.HitKind.Tool, false, PointerRouteLogic.RouteAction.ClickTool),
                R(PointerRouteLogic.HitKind.None, false, PointerRouteLogic.RouteAction.None),
                R(PointerRouteLogic.HitKind.Tool, true, PointerRouteLogic.RouteAction.ClickTool),
            });
        }

        [Test]
        public void Decide_Move_LeavingCanvasEndsStroke_OrphanMoveIsIgnored()
        {
            AssertRows(StrokeLogic.PinchKind.Move, new Row[]
            {
                R(PointerRouteLogic.HitKind.Canvas, true, PointerRouteLogic.RouteAction.AppendStroke),
                R(PointerRouteLogic.HitKind.Tool, true, PointerRouteLogic.RouteAction.EndStroke),   // 드래그 중 도구 변경 없음
                R(PointerRouteLogic.HitKind.None, true, PointerRouteLogic.RouteAction.EndStroke),
                R(PointerRouteLogic.HitKind.Canvas, false, PointerRouteLogic.RouteAction.None),     // 고아 Move
            });
        }

        [Test]
        public void Decide_End_EndsOnlyWhileDrawing_RegardlessOfHit()
        {
            AssertRows(StrokeLogic.PinchKind.End, new Row[]
            {
                R(PointerRouteLogic.HitKind.Canvas, true, PointerRouteLogic.RouteAction.EndStroke),
                R(PointerRouteLogic.HitKind.Tool, true, PointerRouteLogic.RouteAction.EndStroke),
                R(PointerRouteLogic.HitKind.None, true, PointerRouteLogic.RouteAction.EndStroke),
                R(PointerRouteLogic.HitKind.Canvas, false, PointerRouteLogic.RouteAction.None),
                R(PointerRouteLogic.HitKind.Tool, false, PointerRouteLogic.RouteAction.None),
                R(PointerRouteLogic.HitKind.None, false, PointerRouteLogic.RouteAction.None),
            });
        }

        // Phase 3b Task 4 — strokesEnabled=false (StrokeGate 잠금 중) 분기표 (docs/12 §2)
        [Test]
        public void Decide_StrokesDisabled_Start_BlocksCanvas_ButToolStillClicks()
        {
            Assert.AreEqual(PointerRouteLogic.RouteAction.None,
                PointerRouteLogic.Decide(PointerRouteLogic.HitKind.Canvas, StrokeLogic.PinchKind.Start, false, false),
                "게이트 잠금 중엔 캔버스 신규 스트로크를 시작하지 않는다");
            Assert.AreEqual(PointerRouteLogic.RouteAction.ClickTool,
                PointerRouteLogic.Decide(PointerRouteLogic.HitKind.Tool, StrokeLogic.PinchKind.Start, false, false),
                "게이트 잠금 중에도 도구 클릭은 허용한다");
        }

        [Test]
        public void Decide_StrokesDisabled_ReclaimsInProgressStroke()
        {
            Assert.AreEqual(PointerRouteLogic.RouteAction.EndStroke,
                PointerRouteLogic.Decide(PointerRouteLogic.HitKind.Canvas, StrokeLogic.PinchKind.Move, true, false),
                "잠금 순간 진행 중이던 스트로크는 Move에서 End로 회수한다");
            Assert.AreEqual(PointerRouteLogic.RouteAction.EndStroke,
                PointerRouteLogic.Decide(PointerRouteLogic.HitKind.None, StrokeLogic.PinchKind.End, true, false),
                "잠금 중이어도 End는 그대로 회수한다");
        }

        // Task 4 리뷰 Minor 1 — StrokesEnabled setter가 진행 중 스트로크를 실제로 회수하는지 (양손 포함).
        // 분기표(Decide)와 달리 이건 HandPointer의 상태 회수 경로다 — 라운드 종료 시 그리다 만 선의 고아 방지 (docs/12 §5)
        [Test]
        public void StrokesEnabled_FalseWhileDrawing_EndsEveryHandsStroke()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var rig = new GameObject("strokes enabled test");
            rig.SetActive(false);
            try
            {
                quad.transform.position = new Vector3(100f, 100f, 0f);
                var surface = quad.AddComponent<CanvasSurface>();
                var camera = rig.AddComponent<Camera>();
                camera.transform.position = new Vector3(100f, 100f, -3f);
                camera.pixelRect = new Rect(0f, 0f, 1920f, 1080f);
                var state = rig.AddComponent<ToolState>();
                var pointer = rig.AddComponent<HandPointer>();
                var so = new SerializedObject(pointer);
                so.FindProperty("aimCamera").objectReferenceValue = camera;
                so.FindProperty("canvasSurface").objectReferenceValue = surface;
                so.FindProperty("toolState").objectReferenceValue = state;
                so.ApplyModifiedPropertiesWithoutUndo();
                var ended = new System.Collections.Generic.List<string>();
                pointer.OnCanvasStrokeEnd += hand => ended.Add(hand);
                Physics.SyncTransforms();
                Vector2 screen = camera.WorldToScreenPoint(quad.transform.position);
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                System.Reflection.MethodInfo pinchStart = typeof(HandPointer).GetMethod("HandlePinchStart", flags);
                System.Reflection.MethodInfo isDrawing = typeof(HandPointer).GetMethod("IsDrawing", flags);
                pinchStart.Invoke(pointer, new object[] { "Left", screen });
                pinchStart.Invoke(pointer, new object[] { "Right", screen });
                Assert.IsTrue((bool)isDrawing.Invoke(pointer, new object[] { "Left" }), "양손 모두 그리는 중이어야 한다");
                Assert.IsTrue((bool)isDrawing.Invoke(pointer, new object[] { "Right" }));

                pointer.StrokesEnabled = false;

                Assert.AreEqual(2, ended.Count, "잠금 순간 양손의 진행 중 스트로크를 모두 End로 회수한다");
                Assert.IsFalse((bool)isDrawing.Invoke(pointer, new object[] { "Left" }));
                Assert.IsFalse((bool)isDrawing.Invoke(pointer, new object[] { "Right" }));
            }
            finally
            {
                Object.DestroyImmediate(rig);
                Object.DestroyImmediate(quad);
            }
        }

        // 회귀 가드 — 기존 3인자 호출이 4인자 strokesEnabled:true와 전 조합에서 동일해야 한다
        [Test]
        public void Decide_ThreeArgOverload_MatchesFourArgTrue_ForAllCombinations()
        {
            var hits = new[] { PointerRouteLogic.HitKind.None, PointerRouteLogic.HitKind.Canvas, PointerRouteLogic.HitKind.Tool };
            var kinds = new[] { StrokeLogic.PinchKind.Start, StrokeLogic.PinchKind.Move, StrokeLogic.PinchKind.End };
            var drawingStates = new[] { false, true };
            foreach (PointerRouteLogic.HitKind hit in hits)
            {
                foreach (StrokeLogic.PinchKind kind in kinds)
                {
                    foreach (bool isDrawing in drawingStates)
                    {
                        Assert.AreEqual(
                            PointerRouteLogic.Decide(hit, kind, isDrawing, true),
                            PointerRouteLogic.Decide(hit, kind, isDrawing),
                            kind + " / " + hit + " / isDrawing=" + isDrawing);
                    }
                }
            }
        }
    }
}
