using CameraCoop;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

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
        public void IsNewSession_TrueBeforeFirstPacket()
        {
            Assert.IsTrue(PacketFilter.IsNewSession(null, timeSinceLastPacket: 0f, lostTimeout: 0.5f));
        }

        [Test]
        public void IsNewSession_FalseWhileReceiving()
        {
            Assert.IsFalse(PacketFilter.IsNewSession(100u, timeSinceLastPacket: 0.033f, lostTimeout: 0.5f));
        }

        [Test]
        public void IsNewSession_TrueAtLostTimeout()
        {
            Assert.IsTrue(PacketFilter.IsNewSession(100u, timeSinceLastPacket: 0.5f, lostTimeout: 0.5f));
        }

        // 송신 측 재시작 시나리오: lost 이후 seq 0 패킷이 와도 수용돼야 자동 복구가 성립한다.
        [Test]
        public void IsNewSession_AllowsRestartedSenderWithLowerSeq()
        {
            var restarted = new HandPacket { v = 1, seq = 0, timestamp = 0, hands = new HandData[0] };
            Assert.IsFalse(PacketFilter.ShouldAccept(restarted, lastSeq: 900), "seq 필터만으로는 재시작 패킷이 폐기된다");
            Assert.IsTrue(PacketFilter.IsNewSession(900u, timeSinceLastPacket: 1.2f, lostTimeout: 0.5f),
                "lost 이후에는 새 세션으로 보고 seq 체인을 리셋해야 한다");
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

        [TestCase("Left", false)]
        [TestCase("Right", false)]
        [TestCase("Left", true)]
        [TestCase("Right", true)]
        public void CursorAndPinchPosition_FollowPalmInsteadOfFingerFlexion(string handedness, bool movePalm)
        {
            var rig = new GameObject("hand aim test");
            rig.SetActive(false);
            try
            {
                var left = new GameObject("left", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
                var right = new GameObject("right", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
                left.transform.SetParent(rig.transform);
                right.transform.SetParent(rig.transform);
                // 수신기 Awake는 호출하지 않아 소켓을 열지 않는다.
                var receiver = rig.AddComponent<UdpHandReceiver>();
                var controller = rig.AddComponent<HandCursorController>();
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("receiver").objectReferenceValue = receiver;
                serialized.FindProperty("leftCursor").objectReferenceValue = left.transform;
                serialized.FindProperty("rightCursor").objectReferenceValue = right.transform;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(HandCursorController).GetMethod("Awake", flags).Invoke(controller, null);
                object state = typeof(HandCursorController)
                    .GetField(handedness == "Left" ? "leftState" : "rightState", flags).GetValue(controller);
                MethodInfo updateHand = typeof(HandCursorController).GetMethod("UpdateHand", flags);
                Transform cursor = handedness == "Left" ? left.transform : right.transform;
                var emitted = new List<Vector2>();
                controller.OnPinchStart += (hand, position) => emitted.Add(position);
                controller.OnPinchMove += (hand, position) => emitted.Add(position);
                int ends = 0;
                controller.OnPinchEnd += hand => ends++;
                var input = new HandData { handedness = handedness, landmarks = new float[63] };
                int[] palmIndices = { 0, 5, 9, 13, 17 };
                Vector2[] palmOffsets =
                {
                    new Vector2(0f, 0.10f), new Vector2(-0.06f, -0.02f),
                    new Vector2(-0.02f, -0.04f), new Vector2(0.02f, -0.03f), new Vector2(0.06f, -0.01f)
                };
                float[] fingerY = { 0.20f, 0.55f, 0.70f, 0.20f };
                float[] pinch = { 0.90f, 0.15f, 0.15f, 0.90f };
                var actual = new List<Vector2>();
                var expected = new List<Vector2>();

                for (int frame = 0; frame < fingerY.Length; frame++)
                {
                    Vector2 palm = new Vector2(0.5f, 0.5f) + (movePalm ? new Vector2(0.04f, -0.025f) * frame : Vector2.zero);
                    for (int landmark = 0; landmark < 21; landmark++)
                    {
                        input.landmarks[landmark * 3] = palm.x;
                        input.landmarks[landmark * 3 + 1] = fingerY[frame];
                    }
                    for (int index = 0; index < palmIndices.Length; index++)
                    {
                        Vector2 position = palm + palmOffsets[index];
                        input.landmarks[palmIndices[index] * 3] = position.x;
                        input.landmarks[palmIndices[index] * 3 + 1] = position.y;
                    }
                    input.pinch = pinch[frame];
                    updateHand.Invoke(controller, new object[] { state, input, handedness });
                    actual.Add(cursor.position);
                    expected.Add(HandScreenMapper.ToScreen(palm.x, palm.y, Screen.width, Screen.height));
                }

                for (int frame = 0; frame < actual.Count; frame++)
                {
                    Assert.That(Vector2.Distance(expected[frame], actual[frame]), Is.LessThan(0.01f),
                        "손가락 굽힘/펴짐과 무관하게 커서는 손바닥 이동만 따라야 한다: frame " + frame);
                }
                Assert.AreEqual(2, emitted.Count);
                Assert.That(Vector2.Distance(expected[1], emitted[0]), Is.LessThan(0.01f));
                Assert.That(Vector2.Distance(expected[2], emitted[1]), Is.LessThan(0.01f));
                Assert.AreEqual(1, ends);
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

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

        // docs/07 §4: lost 시에도 Start-End 쌍 보장. lost == nowPinched false로 판정한다.
        [Test]
        public void DetermineEvent_PinchedThenLost_ReturnsEnd()
        {
            Assert.AreEqual(CursorStateLogic.PinchEvent.End, CursorStateLogic.DetermineEvent(wasPinched: true, nowPinched: false));
        }

        [Test]
        public void DetermineEvent_NotPinchedThenLost_ReturnsNone()
        {
            Assert.AreEqual(CursorStateLogic.PinchEvent.None, CursorStateLogic.DetermineEvent(wasPinched: false, nowPinched: false));
        }
    }
}
