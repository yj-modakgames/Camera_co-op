using System;
using CameraCoop.Netplay;
using CameraCoop.Party;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    public sealed class PartyPosePresenterTests
    {
        private GameObject rootObject;
        private GameObject presenterObject;
        private PartyPoseSession session;

        [SetUp]
        public void SetUp()
        {
            rootObject = new GameObject("RemoteAvatarRoot");
            presenterObject = new GameObject("RemoteAvatarPresenter");
            session = new PartyPoseSession(new PresenterTransport(), 15f, 100f, 20f);
            session.Configure(Roster());
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            UnityEngine.Object.DestroyImmediate(presenterObject);
            UnityEngine.Object.DestroyImmediate(rootObject);
        }

        [Test]
        public void LocalAvatarCannotBeInitializedAsRemote()
        {
            var presenter = presenterObject.AddComponent<RemoteAvatarPresenter>();
            Assert.Throws<ArgumentException>(() => presenter.Initialize(session, 1, rootObject.transform));
        }

        [Test]
        public void RemoteRootInterpolatesPositionAndYawWithoutPerRenderAllocation()
        {
            var presenter = presenterObject.AddComponent<RemoteAvatarPresenter>();
            presenter.Initialize(session, 2, rootObject.transform, null, 0.1f);
            presenter.ApplyPose(new PartyPoseSample(2, new Vector3(4f, 0f, 2f), 90f, PartyMoveState.Walking, 1), 10f);

            presenter.Render(10.05f);
            Assert.That(rootObject.transform.position.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(rootObject.transform.position.z, Is.EqualTo(1f).Within(0.001f));
            Assert.That(rootObject.transform.eulerAngles.y, Is.EqualTo(45f).Within(0.1f));

            presenter.Render(10.1f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) presenter.Render(10.1f);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.Zero);
        }

        private static PartyRosterSnapshot Roster()
        {
            var slots = new PartyRosterSlotSnapshot[4];
            for (int slot = 0; slot < slots.Length; slot++)
                slots[slot] = new PartyRosterSlotSnapshot(slot, "p" + slot, "P" + slot, true);
            return new PartyRosterSnapshot("presenter-session", 3, "p0", slots);
        }

        private sealed class PresenterTransport : INetTransport
        {
            public bool IsHost => false;
            public string LocalPlayerId => "p1";
            public event Action<string> OnPeerConnected { add { } remove { } }
            public event Action<string> OnPeerDisconnected { add { } remove { } }
            public event Action<string, byte[]> OnMessage { add { } remove { } }
            public void SendToHost(byte[] data, bool reliable) { }
            public void SendTo(string playerId, byte[] data, bool reliable) { }
            public void Tick() { }
            public void Shutdown() { }
        }
    }
}
