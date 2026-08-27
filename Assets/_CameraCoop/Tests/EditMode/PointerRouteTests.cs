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
    }
}
