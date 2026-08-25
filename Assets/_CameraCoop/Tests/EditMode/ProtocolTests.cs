using CameraCoop;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/04_unity_client.md §5 표의 순수 함수 + JsonUtility 파싱 왕복 테스트.
    public class ProtocolTests
    {
        // ---- PacketFilter.ShouldAccept ----

        [Test]
        public void ShouldAccept_RejectsVersionMismatch()
        {
            var packet = new HandPacket { v = 2, seq = 5, timestamp = 0, hands = new HandData[0] };
            Assert.IsFalse(PacketFilter.ShouldAccept(packet, lastSeq: 0));
        }

        [Test]
        public void ShouldAccept_RejectsEqualSeq()
        {
            var packet = new HandPacket { v = 1, seq = 5, timestamp = 0, hands = new HandData[0] };
            Assert.IsFalse(PacketFilter.ShouldAccept(packet, lastSeq: 5));
        }

        [Test]
        public void ShouldAccept_RejectsLowerSeq()
        {
            var packet = new HandPacket { v = 1, seq = 4, timestamp = 0, hands = new HandData[0] };
            Assert.IsFalse(PacketFilter.ShouldAccept(packet, lastSeq: 5));
        }

        [Test]
        public void ShouldAccept_AcceptsHigherSeq()
        {
            var packet = new HandPacket { v = 1, seq = 6, timestamp = 0, hands = new HandData[0] };
            Assert.IsTrue(PacketFilter.ShouldAccept(packet, lastSeq: 5));
        }

        [Test]
        public void ShouldAccept_RejectsNullPacket()
        {
            Assert.IsFalse(PacketFilter.ShouldAccept(null, lastSeq: 0));
        }

        // ---- HandScreenMapper.ToScreen ----

        [Test]
        public void ToScreen_ImageTopLeftMapsToScreenTopLeft()
        {
            Vector2 result = HandScreenMapper.ToScreen(0f, 0f, 1920f, 1080f);
            Assert.AreEqual(new Vector2(0f, 1080f), result);
        }

        [Test]
        public void ToScreen_ImageBottomRightMapsToScreenBottomRight()
        {
            Vector2 result = HandScreenMapper.ToScreen(1f, 1f, 1920f, 1080f);
            Assert.AreEqual(new Vector2(1920f, 0f), result);
        }

        [Test]
        public void ToScreen_CenterMapsToCenter()
        {
            Vector2 result = HandScreenMapper.ToScreen(0.5f, 0.5f, 1920f, 1080f);
            Assert.AreEqual(new Vector2(960f, 540f), result);
        }

        // ---- PinchStateMachine.Next ----

        [Test]
        public void PinchNext_StartsWhenBelowStartThreshold()
        {
            Assert.IsTrue(PinchStateMachine.Next(current: false, pinch: 0.29f, startThreshold: 0.30f, releaseThreshold: 0.40f));
        }

        [Test]
        public void PinchNext_DoesNotStartAtExactStartThreshold()
        {
            // 경계값: pinch == startThreshold는 '미만'이 아니므로 시작하지 않는다.
            Assert.IsFalse(PinchStateMachine.Next(current: false, pinch: 0.30f, startThreshold: 0.30f, releaseThreshold: 0.40f));
        }

        [Test]
        public void PinchNext_StaysPinchedAtExactReleaseThreshold()
        {
            // 경계값: pinch == releaseThreshold는 '초과'가 아니므로 유지된다 (히스테리시스).
            Assert.IsTrue(PinchStateMachine.Next(current: true, pinch: 0.40f, startThreshold: 0.30f, releaseThreshold: 0.40f));
        }

        [Test]
        public void PinchNext_ReleasesAboveReleaseThreshold()
        {
            Assert.IsFalse(PinchStateMachine.Next(current: true, pinch: 0.41f, startThreshold: 0.30f, releaseThreshold: 0.40f));
        }

        [Test]
        public void PinchNext_StaysPinchedBetweenThresholds()
        {
            // 히스테리시스 구간(start~release 사이)에서는 떨림 없이 현재 상태 유지.
            Assert.IsTrue(PinchStateMachine.Next(current: true, pinch: 0.35f, startThreshold: 0.30f, releaseThreshold: 0.40f));
            Assert.IsFalse(PinchStateMachine.Next(current: false, pinch: 0.35f, startThreshold: 0.30f, releaseThreshold: 0.40f));
        }

        // ---- HandData.GetLandmark ----

        [Test]
        public void GetLandmark_Index0ReturnsFirstTriplet()
        {
            var hand = new HandData { landmarks = BuildLandmarks(21) };
            Assert.AreEqual(new Vector3(0f, 1f, 2f), hand.GetLandmark(0));
        }

        [Test]
        public void GetLandmark_Index20ReturnsLastTriplet()
        {
            var hand = new HandData { landmarks = BuildLandmarks(21) };
            Assert.AreEqual(new Vector3(60f, 61f, 62f), hand.GetLandmark(20));
        }

        [Test]
        public void GetLandmark_Index21OutOfRangeReturnsZero()
        {
            var hand = new HandData { landmarks = BuildLandmarks(21) };
            Assert.AreEqual(Vector3.zero, hand.GetLandmark(21));
        }

        [Test]
        public void GetLandmark_NegativeIndexReturnsZero()
        {
            var hand = new HandData { landmarks = BuildLandmarks(21) };
            Assert.AreEqual(Vector3.zero, hand.GetLandmark(-1));
        }

        [Test]
        public void GetLandmark_NullLandmarksReturnsZero()
        {
            var hand = new HandData { landmarks = null };
            Assert.AreEqual(Vector3.zero, hand.GetLandmark(0));
        }

        // ---- JsonUtility 파싱 왕복 ----

        [Test]
        public void FromJson_ParsesFullPacketWithHand()
        {
            const string json = "{\"v\":1,\"seq\":42,\"timestamp\":1234567890.123,\"hands\":[" +
                "{\"handedness\":\"Right\",\"landmarks\":[0.51,0.42,-0.01,0.55,0.40,-0.02],\"pinch\":0.21}]}";

            HandPacket packet = JsonUtility.FromJson<HandPacket>(json);

            Assert.AreEqual(1, packet.v);
            Assert.AreEqual(42u, packet.seq);
            Assert.AreEqual(1234567890.123, packet.timestamp, 0.0001);
            Assert.AreEqual(1, packet.hands.Length);
            Assert.AreEqual("Right", packet.hands[0].handedness);
            Assert.AreEqual(6, packet.hands[0].landmarks.Length);
            Assert.AreEqual(0.51f, packet.hands[0].landmarks[0], 0.0001f);
            Assert.AreEqual(0.21f, packet.hands[0].pinch, 0.0001f);
        }

        [Test]
        public void FromJson_ParsesEmptyHandsHeartbeat()
        {
            const string json = "{\"v\":1,\"seq\":7,\"timestamp\":1000.0,\"hands\":[]}";

            HandPacket packet = JsonUtility.FromJson<HandPacket>(json);

            Assert.AreEqual(1, packet.v);
            Assert.AreEqual(7u, packet.seq);
            Assert.IsNotNull(packet.hands);
            Assert.AreEqual(0, packet.hands.Length);
        }

        private static float[] BuildLandmarks(int landmarkCount)
        {
            var landmarks = new float[landmarkCount * 3];
            for (int i = 0; i < landmarks.Length; i++)
            {
                landmarks[i] = i;
            }
            return landmarks;
        }

        // ---- CursorStateLogic (HandCursorController.cs 내부 internal 순수 함수) ----

        [Test]
        public void TargetAlpha_MatchesPresence()
        {
            Assert.AreEqual(1f, CursorStateLogic.TargetAlpha(present: true));
            Assert.AreEqual(0f, CursorStateLogic.TargetAlpha(present: false));
        }

        [Test]
        public void StepAlpha_MovesTowardTargetByDeltaOverFadeDuration()
        {
            // fadeDuration 0.2초 중 0.05초 경과 → 0.25만큼 이동
            float result = CursorStateLogic.StepAlpha(currentAlpha: 0f, targetAlpha: 1f, deltaTime: 0.05f, fadeDuration: 0.2f);
            Assert.AreEqual(0.25f, result, 0.0001f);
        }

        [Test]
        public void StepAlpha_DoesNotOvershootTarget()
        {
            float result = CursorStateLogic.StepAlpha(currentAlpha: 0.9f, targetAlpha: 1f, deltaTime: 1f, fadeDuration: 0.2f);
            Assert.AreEqual(1f, result, 0.0001f);
        }

        [Test]
        public void StepAlpha_ZeroFadeDurationJumpsImmediately()
        {
            float result = CursorStateLogic.StepAlpha(currentAlpha: 0f, targetAlpha: 1f, deltaTime: 0.016f, fadeDuration: 0f);
            Assert.AreEqual(1f, result);
        }

        [Test]
        public void Scale_MatchesPinchState()
        {
            Assert.AreEqual(0.7f, CursorStateLogic.Scale(pinched: true, pinchScale: 0.7f));
            Assert.AreEqual(1f, CursorStateLogic.Scale(pinched: false, pinchScale: 0.7f));
        }

        [Test]
        public void DetermineEvent_TransitionsCorrectly()
        {
            Assert.AreEqual(CursorStateLogic.PinchEvent.Start, CursorStateLogic.DetermineEvent(wasPinched: false, nowPinched: true));
            Assert.AreEqual(CursorStateLogic.PinchEvent.Move, CursorStateLogic.DetermineEvent(wasPinched: true, nowPinched: true));
            Assert.AreEqual(CursorStateLogic.PinchEvent.End, CursorStateLogic.DetermineEvent(wasPinched: true, nowPinched: false));
            Assert.AreEqual(CursorStateLogic.PinchEvent.None, CursorStateLogic.DetermineEvent(wasPinched: false, nowPinched: false));
        }
    }
}
