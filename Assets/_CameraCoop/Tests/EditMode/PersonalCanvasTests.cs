using System;
using System.Reflection;
using CameraCoop;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CameraCoop.Tests
{
    public class FistClassifierTests
    {
        [Test]
        public void CurledLandmarksAreFistButExtendedLandmarksAreNot()
        {
            Assert.IsTrue(IsFist(BuildHand(curled: true, scale: 0.3f, rotation: 37f)));
            Assert.IsFalse(IsFist(BuildHand(curled: false, scale: 0.3f, rotation: 37f)));
        }

        [Test]
        public void CurledFingersWithExtendedThumbAreNotFist()
        {
            Assert.IsFalse(IsFist(BuildHand(curled: true, scale: 0.3f, rotation: 37f, thumbCurled: false)));
        }

        [Test]
        public void ClassifierRejectsMalformedNonFiniteAndZeroScaleHands()
        {
            Assert.IsFalse(IsFist(new HandData { landmarks = null }));
            Assert.IsFalse(IsFist(new HandData { landmarks = new float[62] }));
            Assert.IsFalse(IsFist(new HandData { landmarks = new float[63] }));
            HandData nan = BuildHand(curled: true, scale: 1f, rotation: 0f);
            nan.landmarks[17] = float.NaN;
            Assert.IsFalse(IsFist(nan));
        }

        private static bool IsFist(HandData hand)
        {
            Type type = typeof(HandData).Assembly.GetType("CameraCoop.HandGestureClassifier");
            Assert.IsNotNull(type, "HandGestureClassifier is required.");
            MethodInfo method = type.GetMethod("IsFist", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "HandGestureClassifier.IsFist is required.");
            return (bool)method.Invoke(null, new object[] { hand });
        }

        private static HandData BuildHand(bool curled, float scale, float rotation, bool? thumbCurled = null)
        {
            var points = new Vector3[21];
            points[0] = Vector3.zero;
            points[1] = new Vector3(-0.55f, 0.15f);
            points[2] = new Vector3(-0.75f, 0.35f);
            bool curlThumb = thumbCurled ?? curled;
            points[3] = curlThumb ? new Vector3(-0.5f, 0.25f) : new Vector3(-0.95f, 0.55f);
            points[4] = curlThumb ? new Vector3(-0.25f, 0.2f) : new Vector3(-1.15f, 0.75f);
            for (int finger = 0; finger < 4; finger++)
            {
                int mcp = 5 + finger * 4;
                float x = -0.36f + finger * 0.24f;
                points[mcp] = new Vector3(x, 0.45f);
                if (curled)
                {
                    points[mcp + 1] = new Vector3(x, 0.78f);
                    points[mcp + 2] = new Vector3(x + 0.24f, 0.62f);
                    points[mcp + 3] = new Vector3(x + 0.08f, 0.38f);
                }
                else
                {
                    points[mcp + 1] = new Vector3(x, 0.78f);
                    points[mcp + 2] = new Vector3(x, 1.11f);
                    points[mcp + 3] = new Vector3(x, 1.44f);
                }
            }
            Quaternion turn = Quaternion.Euler(0f, 0f, rotation);
            var values = new float[63];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 point = turn * (points[i] * scale) + new Vector3(0.43f, 0.57f, -0.2f);
                values[i * 3] = point.x;
                values[i * 3 + 1] = point.y;
                values[i * 3 + 2] = point.z;
            }
            return new HandData { landmarks = values };
        }
    }

    public class PersonalCanvasPlacementTests
    {
        private GameObject root;
        private Transform avatar;
        private Transform dock;
        private Component placement;
        private Type placementType;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("personal canvas test");
            root.SetActive(false);
            avatar = new GameObject("avatar anchor").transform;
            dock = new GameObject("dock anchor").transform;
            dock.position = new Vector3(4f, 2f, 1f);
            placementType = typeof(CanvasSurface).Assembly.GetType("CameraCoop.PersonalCanvasPlacement");
            Assert.IsNotNull(placementType, "PersonalCanvasPlacement is required.");
            placement = root.AddComponent(placementType);
            Invoke("Configure", "owner", avatar, dock, 0.5f);
            root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
            if (avatar != null) Object.DestroyImmediate(avatar.gameObject);
            if (dock != null) Object.DestroyImmediate(dock.gameObject);
        }

        [Test]
        public void OwnerCanCarryAndDockAtOwnCenterOnly()
        {
            Assert.AreEqual("Docked", Read("State").ToString());
            Assert.AreSame(dock, root.transform.parent);
            Assert.IsFalse((bool)Invoke("TryCarry", "other"));
            Assert.IsTrue((bool)Invoke("TryCarry", "owner"));
            Assert.AreEqual("Carried", Read("State").ToString());
            Assert.AreSame(avatar, root.transform.parent);
            Assert.AreEqual("owner", Read("HolderPlayerId"));

            Assert.IsFalse((bool)Invoke("TryDock", "other"));
            Assert.IsFalse((bool)Invoke("TryDock", "owner"),
                "An owner id cannot spoof a remote dock while the controlled canvas is outside its dock radius.");
            avatar.position = dock.position + Vector3.right * 0.49f;
            Assert.IsTrue((bool)Invoke("TryDock", "owner"));
            Assert.AreEqual("Docked", Read("State").ToString());
            Assert.AreSame(dock, root.transform.parent);
            Assert.AreEqual(Vector3.zero, root.transform.localPosition);
            Assert.IsNull(Read("HolderPlayerId"));
        }

        [Test]
        public void CarryAppliesConfiguredGripPositionAndRotationDeterministically()
        {
            Vector3 gripPosition = new Vector3(.2f, -.15f, .35f);
            Quaternion gripRotation = Quaternion.Euler(12f, 34f, 56f);
            Invoke("ConfigureCarriedPose", gripPosition, gripRotation);

            Assert.IsTrue((bool)Invoke("TryCarry", "owner"));

            Assert.AreEqual(gripPosition, root.transform.localPosition);
            Assert.Less(Quaternion.Angle(gripRotation, root.transform.localRotation), .001f);
        }

        [Test]
        public void AbortOrDisconnectResetsDockAttachmentAndPrivateHolderState()
        {
            Assert.IsTrue((bool)Invoke("TryCarry", "owner"));
            Invoke("ResetForAbortOrDisconnect");

            Assert.AreEqual("Docked", Read("State").ToString());
            Assert.AreSame(dock, root.transform.parent);
            Assert.IsNull(Read("HolderPlayerId"));
        }

        private object Invoke(string name, params object[] args)
        {
            MethodInfo method = null;
            foreach (MethodInfo candidate in placementType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (candidate.Name == name && candidate.GetParameters().Length == args.Length)
                {
                    method = candidate;
                    break;
                }
            }
            Assert.IsNotNull(method, name + " is required.");
            return method.Invoke(placement, args);
        }

        private object Read(string name)
        {
            PropertyInfo property = placementType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, name + " is required.");
            return property.GetValue(placement);
        }
    }
}
