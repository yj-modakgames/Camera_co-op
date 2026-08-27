using CameraCoop;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/11 §2 지우개 — 점-선분 최소거리 판정 (월드 단위)
    public class EraseLogicTests
    {
        [Test]
        public void HitsSegment_PointOnSegment_Hits()
        {
            Assert.IsTrue(EraseLogic.HitsSegment(new Vector3(0.5f, 0f, 0f), Vector3.zero, new Vector3(1f, 0f, 0f), radius: 0.05f));
        }

        [Test]
        public void HitsSegment_PointNearSegment_WithinRadius_Hits()
        {
            Assert.IsTrue(EraseLogic.HitsSegment(new Vector3(0.5f, 0.04f, 0f), Vector3.zero, new Vector3(1f, 0f, 0f), radius: 0.05f));
        }

        [Test]
        public void HitsSegment_PointOutsideRadius_Misses()
        {
            Assert.IsFalse(EraseLogic.HitsSegment(new Vector3(0.5f, 0.2f, 0f), Vector3.zero, new Vector3(1f, 0f, 0f), radius: 0.05f));
        }

        [Test]
        public void HitsSegment_BeyondEndpoint_UsesEndpointDistance_NotLineDistance()
        {
            // 무한 직선 기준이면 수직거리 0으로 hit이 된다 — 선분 끝점 거리(1.0)로 판정해야 miss
            Assert.IsFalse(EraseLogic.HitsSegment(new Vector3(2f, 0f, 0f), Vector3.zero, new Vector3(1f, 0f, 0f), radius: 0.5f));
            Assert.IsFalse(EraseLogic.HitsSegment(new Vector3(-1f, 0f, 0f), Vector3.zero, new Vector3(1f, 0f, 0f), radius: 0.5f));
        }

        [Test]
        public void HitsSegment_JustBeyondEndpoint_WithinRadius_Hits()
        {
            Assert.IsTrue(EraseLogic.HitsSegment(new Vector3(1.03f, 0f, 0f), Vector3.zero, new Vector3(1f, 0f, 0f), radius: 0.05f));
        }

        // ---- HitsStroke: 점열 단위 판정 (DrawingController가 스트로크 선택에 쓴다) ----

        private static System.Collections.Generic.List<Vector3> Polyline(params float[] xs)
        {
            var points = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < xs.Length; i++)
            {
                points.Add(new Vector3(xs[i], 0f, 0f));
            }
            return points;
        }

        [Test]
        public void HitsStroke_HitsOnAnyInnerSegment()
        {
            System.Collections.Generic.List<Vector3> stroke = Polyline(0f, 1f, 2f, 3f);
            Assert.IsTrue(EraseLogic.HitsStroke(stroke, new Vector3(2.5f, 0.02f, 0f), radius: 0.05f));
        }

        [Test]
        public void HitsStroke_MissesWhenFarFromEverySegment()
        {
            System.Collections.Generic.List<Vector3> stroke = Polyline(0f, 1f, 2f, 3f);
            Assert.IsFalse(EraseLogic.HitsStroke(stroke, new Vector3(1.5f, 0.5f, 0f), radius: 0.05f));
        }

        [Test]
        public void HitsStroke_SinglePointStroke_HasNoSegment_Misses()
        {
            System.Collections.Generic.List<Vector3> stroke = Polyline(1f);
            Assert.IsFalse(EraseLogic.HitsStroke(stroke, new Vector3(1f, 0f, 0f), radius: 0.05f));
        }

        [Test]
        public void HitsStroke_NullList_Misses()
        {
            Assert.IsFalse(EraseLogic.HitsStroke(null, Vector3.zero, radius: 0.05f));
        }

        [Test]
        public void HitsSegment_ZeroLengthSegment_DoesNotThrow()
        {
            var a = new Vector3(1f, 2f, 3f);
            Assert.DoesNotThrow(() => EraseLogic.HitsSegment(new Vector3(1.02f, 2f, 3f), a, a, radius: 0.05f));
            Assert.IsTrue(EraseLogic.HitsSegment(new Vector3(1.02f, 2f, 3f), a, a, radius: 0.05f));
            Assert.IsFalse(EraseLogic.HitsSegment(new Vector3(1.5f, 2f, 3f), a, a, radius: 0.05f));
        }
    }
}
