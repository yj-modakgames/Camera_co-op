using CameraCoop.Party;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    public class PartyCarriedBrushTests
    {
        [Test]
        public void PacketCarriesTheHandThatHoldsTheBrush()
        {
            var packet = new PartyPosePacket
            {
                sessionId = "s1",
                rosterGeneration = 1,
                transitionGeneration = 0,
                sequence = 1,
                kind = PartyPoseProtocol.KindRelay,
                slot = 2,
                carriedHand = (int)PartyCarriedHand.Right
            };

            Assert.That(PartyPoseProtocol.TryDecode(PartyPoseProtocol.Encode(packet), out PartyPosePacket decoded), Is.True);
            Assert.That(decoded.carriedHand, Is.EqualTo((int)PartyCarriedHand.Right));
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void OutOfRangeCarriedHandIsRejected(int hand)
        {
            var packet = new PartyPosePacket
            {
                sessionId = "s1",
                rosterGeneration = 1,
                transitionGeneration = 0,
                sequence = 1,
                kind = PartyPoseProtocol.KindRelay,
                slot = 1,
                carriedHand = hand
            };
            byte[] bytes = PartyPoseProtocol.Encode(packet);
            Assert.That(PartyPoseProtocol.TryDecode(bytes, out _), Is.False);
        }

        [Test]
        public void PresenterShowsOnlyTheHandThatHoldsTheBrush()
        {
            var root = new GameObject("AvatarRoot");
            try
            {
                var left = Child(root, RemoteAvatarPresenter.LeftHandBrushName);
                var right = Child(root, RemoteAvatarPresenter.RightHandBrushName);
                var presenter = root.AddComponent<RemoteAvatarPresenter>();
                SetField(presenter, "avatarRoot", root.transform);
                SetField(presenter, "leftHandBrush", left.transform);
                SetField(presenter, "rightHandBrush", right.transform);

                Apply(presenter, PartyCarriedHand.Right);
                Assert.That(right.activeSelf, Is.True, "오른손에 들었으면 오른손 붓만 보여야 한다");
                Assert.That(left.activeSelf, Is.False);

                Apply(presenter, PartyCarriedHand.Left);
                Assert.That(left.activeSelf, Is.True);
                Assert.That(right.activeSelf, Is.False);

                Apply(presenter, PartyCarriedHand.None);
                Assert.That(left.activeSelf, Is.False, "놓으면 양손 모두 사라져야 한다");
                Assert.That(right.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Apply(RemoteAvatarPresenter presenter, PartyCarriedHand hand)
        {
            typeof(RemoteAvatarPresenter)
                .GetMethod("ApplyCarriedHand", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(presenter, new object[] { hand });
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType()
                .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(target, value);
        }
    }
}
