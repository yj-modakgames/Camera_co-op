using CameraCoop;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/11 §2 — WASD 이동 델타, 방 경계 clamp, pitch clamp
    public class PlayerMoveTests
    {
        [Test]
        public void Step_YawZero_ForwardIsPlusZ()
        {
            Vector3 delta = PlayerMoveLogic.Step(new Vector2(0f, 1f), yawDegrees: 0f, speed: 3f, deltaTime: 0.5f);
            Assert.AreEqual(0f, delta.x, 1e-4f);
            Assert.AreEqual(0f, delta.y, 1e-4f);
            Assert.AreEqual(1.5f, delta.z, 1e-4f); // 3 m/s * 0.5s
        }

        [Test]
        public void Step_Yaw90_ForwardIsPlusX()
        {
            Vector3 delta = PlayerMoveLogic.Step(new Vector2(0f, 1f), yawDegrees: 90f, speed: 2f, deltaTime: 1f);
            Assert.AreEqual(2f, delta.x, 1e-4f);
            Assert.AreEqual(0f, delta.z, 1e-4f);
        }

        [Test]
        public void Step_Strafe_YawZero_IsPlusX()
        {
            Vector3 delta = PlayerMoveLogic.Step(new Vector2(1f, 0f), yawDegrees: 0f, speed: 1f, deltaTime: 1f);
            Assert.AreEqual(1f, delta.x, 1e-4f);
            Assert.AreEqual(0f, delta.z, 1e-4f);
        }

        [Test]
        public void Step_Diagonal_IsNotFasterThanStraight()
        {
            // 대각선 (1,1)이 √2배 빨라지면 안 된다 — 정규화 누락 회귀 방지
            Vector3 diagonal = PlayerMoveLogic.Step(new Vector2(1f, 1f), yawDegrees: 0f, speed: 3f, deltaTime: 1f);
            Assert.LessOrEqual(diagonal.magnitude, 3f + 1e-4f);
            Assert.AreEqual(3f, diagonal.magnitude, 1e-3f); // 방향 유지 + 속도 보존
        }

        [Test]
        public void Step_NoInput_IsZero()
        {
            Vector3 delta = PlayerMoveLogic.Step(Vector2.zero, yawDegrees: 45f, speed: 5f, deltaTime: 1f);
            Assert.AreEqual(0f, delta.magnitude, 1e-5f);
        }

        [Test]
        public void ClampToRoom_InsideIsUnchanged()
        {
            var min = new Vector2(-5.5f, -8.75f);
            var max = new Vector2(5.5f, 0.75f);
            Vector3 pos = PlayerMoveLogic.ClampToRoom(new Vector3(1f, 1.5f, -3f), min, max);
            Assert.AreEqual(1f, pos.x, 1e-4f);
            Assert.AreEqual(-3f, pos.z, 1e-4f);
        }

        [Test]
        public void ClampToRoom_ClampsXZ_AndKeepsY()
        {
            var min = new Vector2(-5.5f, -8.75f);
            var max = new Vector2(5.5f, 0.75f);
            Vector3 high = PlayerMoveLogic.ClampToRoom(new Vector3(9f, 1.23f, 4f), min, max);
            Assert.AreEqual(5.5f, high.x, 1e-4f);
            Assert.AreEqual(0.75f, high.z, 1e-4f);
            Assert.AreEqual(1.23f, high.y, 1e-4f); // y는 건드리지 않는다

            Vector3 low = PlayerMoveLogic.ClampToRoom(new Vector3(-9f, 0f, -20f), min, max);
            Assert.AreEqual(-5.5f, low.x, 1e-4f);
            Assert.AreEqual(-8.75f, low.z, 1e-4f);
        }

        [Test]
        public void ClampPitch_LimitsBothDirections()
        {
            Assert.AreEqual(80f, PlayerMoveLogic.ClampPitch(120f, 80f), 1e-4f);
            Assert.AreEqual(-80f, PlayerMoveLogic.ClampPitch(-120f, 80f), 1e-4f);
            Assert.AreEqual(12.5f, PlayerMoveLogic.ClampPitch(12.5f, 80f), 1e-4f);
        }
    }
}
