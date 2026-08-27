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
    }
}
