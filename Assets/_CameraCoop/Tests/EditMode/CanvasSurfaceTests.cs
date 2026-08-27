using CameraCoop;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/10 §2 — norm [0,1] 좌상단 원점 -> 1x1 Quad 로컬/월드 매핑
    public class CanvasSurfaceTests
    {
        [Test]
        public void NormToLocal_Center_MapsToOrigin()
        {
            Vector3 local = CanvasSurfaceLogic.NormToLocal(new Vector2(0.5f, 0.5f), zOffset: -0.005f);
            Assert.AreEqual(0f, local.x, 1e-5f);
            Assert.AreEqual(0f, local.y, 1e-5f);
            Assert.AreEqual(-0.005f, local.z, 1e-5f);
        }

        [Test]
        public void NormToLocal_TopLeft_MapsToUpperLeftQuadCorner()
        {
            // norm (0,0) = 좌상단 -> 로컬 (-0.5, +0.5) (y 반전)
            Vector3 local = CanvasSurfaceLogic.NormToLocal(new Vector2(0f, 0f), zOffset: 0f);
            Assert.AreEqual(-0.5f, local.x, 1e-5f);
            Assert.AreEqual(0.5f, local.y, 1e-5f);
        }

        [Test]
        public void NormToLocal_BottomRight_MapsToLowerRightQuadCorner()
        {
            Vector3 local = CanvasSurfaceLogic.NormToLocal(new Vector2(1f, 1f), zOffset: 0f);
            Assert.AreEqual(0.5f, local.x, 1e-5f);
            Assert.AreEqual(-0.5f, local.y, 1e-5f);
        }

        [Test]
        public void NormToWorld_AppliesPositionAndScale()
        {
            var go = new GameObject("canvas");
            try
            {
                go.transform.position = new Vector3(1f, 2f, 3f);
                go.transform.localScale = new Vector3(2f, 4f, 1f);
                var surface = go.AddComponent<CanvasSurface>();
                // norm (0,0) 좌상단 -> 로컬 (-0.5, +0.5, -0.005) -> 스케일·이동 적용
                Vector3 world = surface.NormToWorld(new Vector2(0f, 0f));
                Assert.AreEqual(1f - 1f, world.x, 1e-4f);   // 1 + (-0.5 * 2)
                Assert.AreEqual(2f + 2f, world.y, 1e-4f);   // 2 + (0.5 * 4)
                Assert.AreEqual(3f - 0.005f, world.z, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NormToWorld_AppliesRotation()
        {
            var go = new GameObject("canvas");
            try
            {
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // 뒤집으면 x 부호 반전
                var surface = go.AddComponent<CanvasSurface>();
                Vector3 world = surface.NormToWorld(new Vector2(0f, 0.5f)); // 로컬 (-0.5, 0, -0.005)
                Assert.AreEqual(0.5f, world.x, 1e-4f);
                Assert.AreEqual(0f, world.y, 1e-4f);
                Assert.AreEqual(0.005f, world.z, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---- HandScreenMapper 왕복 (docs/10 §2 — screen↔norm 단일 진실 원천) ----

        [Test]
        public void HandScreenMapper_RoundTrip_IsIdentity()
        {
            var norm = new Vector2(0.25f, 0.75f);
            Vector2 screen = HandScreenMapper.ToScreen(norm.x, norm.y, 1920f, 1080f);
            Vector2 back = HandScreenMapper.ToNormalized(screen, 1920f, 1080f);
            Assert.AreEqual(norm.x, back.x, 1e-5f);
            Assert.AreEqual(norm.y, back.y, 1e-5f);
        }

        [Test]
        public void HandScreenMapper_ToNormalized_FlipsY()
        {
            // 화면 좌하단 원점 (0,0) -> norm 좌상단 원점이므로 (0,1)
            Vector2 norm = HandScreenMapper.ToNormalized(Vector2.zero, 1920f, 1080f);
            Assert.AreEqual(0f, norm.x, 1e-5f);
            Assert.AreEqual(1f, norm.y, 1e-5f);
        }

        // ---- docs/11 §2 — 역변환 (레이 hit 지점 -> norm) ----

        [Test]
        public void LocalToNorm_IsInverseOfNormToLocal()
        {
            AssertLocalRoundTrip(new Vector2(0f, 0f));
            AssertLocalRoundTrip(new Vector2(1f, 1f));
            AssertLocalRoundTrip(new Vector2(0.37f, 0.82f));
        }

        private static void AssertLocalRoundTrip(Vector2 norm)
        {
            Vector3 local = CanvasSurfaceLogic.NormToLocal(norm, zOffset: -0.005f);
            Vector2 back = CanvasSurfaceLogic.LocalToNorm(local);
            Assert.AreEqual(norm.x, back.x, 1e-5f, "x roundtrip");
            Assert.AreEqual(norm.y, back.y, 1e-5f, "y roundtrip");
        }

        [Test]
        public void WorldToNorm_IsInverseOfNormToWorld_WithRotation()
        {
            var go = new GameObject("canvas");
            try
            {
                go.transform.position = new Vector3(1f, 1.5f, -0.5f);
                go.transform.localScale = new Vector3(2.4f, 1.35f, 1f); // 씬 실제 캔버스 스케일
                go.transform.rotation = Quaternion.Euler(0f, 30f, 0f);  // 회전이 있어도 복원돼야 한다
                var surface = go.AddComponent<CanvasSurface>();

                var norms = new Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0.13f, 0.91f) };
                for (int i = 0; i < norms.Length; i++)
                {
                    Vector3 world = surface.NormToWorld(norms[i]);
                    Vector2 back = surface.WorldToNorm(world);
                    Assert.AreEqual(norms[i].x, back.x, 1e-4f, "x roundtrip at " + i);
                    Assert.AreEqual(norms[i].y, back.y, 1e-4f, "y roundtrip at " + i);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
