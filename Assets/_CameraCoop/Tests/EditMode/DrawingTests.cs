using CameraCoop;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/07_phase2_drawing.md §8 표의 StrokeLogic 순수 함수 테스트.
    public class DrawingTests
    {
        // ---- StrokeLogic.ShouldAppendPoint ----

        [Test]
        public void ShouldAppendPoint_FirstPointAlwaysAppends()
        {
            Assert.IsTrue(StrokeLogic.ShouldAppendPoint(hasLastPoint: false, Vector3.zero, Vector3.zero, minDistance: 0.01f));
        }

        [Test]
        public void ShouldAppendPoint_BelowMinDistanceRejects()
        {
            var last = Vector3.zero;
            var next = new Vector3(0.005f, 0f, 0f);
            Assert.IsFalse(StrokeLogic.ShouldAppendPoint(hasLastPoint: true, last, next, minDistance: 0.01f));
        }

        [Test]
        public void ShouldAppendPoint_AtExactMinDistanceAppends()
        {
            var last = Vector3.zero;
            var next = new Vector3(0.01f, 0f, 0f);
            Assert.IsTrue(StrokeLogic.ShouldAppendPoint(hasLastPoint: true, last, next, minDistance: 0.01f));
        }

        [Test]
        public void ShouldAppendPoint_AboveMinDistanceAppends()
        {
            var last = Vector3.zero;
            var next = new Vector3(0.5f, 0f, 0f);
            Assert.IsTrue(StrokeLogic.ShouldAppendPoint(hasLastPoint: true, last, next, minDistance: 0.01f));
        }

        // ---- StrokeLogic.Decide (docs/07 §6 엣지 케이스 표) ----

        [Test]
        public void Decide_StartWithoutActive_StartsNew()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.StartNew, StrokeLogic.Decide(hasActiveStroke: false, StrokeLogic.PinchKind.Start));
        }

        [Test]
        public void Decide_StartWithActive_EndsThenStartsNew()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.EndThenStartNew, StrokeLogic.Decide(hasActiveStroke: true, StrokeLogic.PinchKind.Start));
        }

        [Test]
        public void Decide_MoveWithActive_Appends()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.Append, StrokeLogic.Decide(hasActiveStroke: true, StrokeLogic.PinchKind.Move));
        }

        [Test]
        public void Decide_MoveWithoutActive_None()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.None, StrokeLogic.Decide(hasActiveStroke: false, StrokeLogic.PinchKind.Move));
        }

        [Test]
        public void Decide_EndWithActive_Ends()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.End, StrokeLogic.Decide(hasActiveStroke: true, StrokeLogic.PinchKind.End));
        }

        [Test]
        public void Decide_EndWithoutActive_None()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.None, StrokeLogic.Decide(hasActiveStroke: false, StrokeLogic.PinchKind.End));
        }

        // ---- StrokeLogic.ShouldSplitStroke (재검출 스냅 방어, docs/07 §6) ----

        [Test]
        public void ShouldSplitStroke_WithinThreshold_False()
        {
            Assert.IsFalse(StrokeLogic.ShouldSplitStroke(new Vector2(100f, 100f), new Vector2(150f, 100f), maxSegmentDistance: 100f));
        }

        [Test]
        public void ShouldSplitStroke_AtExactThreshold_False()
        {
            Assert.IsFalse(StrokeLogic.ShouldSplitStroke(new Vector2(0f, 0f), new Vector2(100f, 0f), maxSegmentDistance: 100f));
        }

        [Test]
        public void ShouldSplitStroke_BeyondThreshold_True()
        {
            Assert.IsTrue(StrokeLogic.ShouldSplitStroke(new Vector2(0f, 0f), new Vector2(300f, 0f), maxSegmentDistance: 100f));
        }

        // ---- StrokeLogic.ShouldDiscardOnEnd ----

        [Test]
        public void ShouldDiscardOnEnd_ZeroPoints_True()
        {
            Assert.IsTrue(StrokeLogic.ShouldDiscardOnEnd(0));
        }

        [Test]
        public void ShouldDiscardOnEnd_OnePoint_True()
        {
            Assert.IsTrue(StrokeLogic.ShouldDiscardOnEnd(1));
        }

        [Test]
        public void ShouldDiscardOnEnd_TwoPoints_False()
        {
            Assert.IsFalse(StrokeLogic.ShouldDiscardOnEnd(2));
        }
    }
}
