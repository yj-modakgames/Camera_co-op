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

        [Test]
        public void EmptyRemoteSlotsAreHiddenForTwoAndThreePlayerRosters()
        {
            var occupiedPresenter = presenterObject.AddComponent<RemoteAvatarPresenter>();
            var emptyPresenterObject = new GameObject("EmptyRemotePresenter");
            var emptyRootObject = new GameObject("EmptyRemoteRoot");
            var thirdPresenterObject = new GameObject("ThirdRemotePresenter");
            var thirdRootObject = new GameObject("ThirdRemoteRoot");
            try
            {
                session.Configure(Roster("p0", "p1"));
                occupiedPresenter.Initialize(session, 0, rootObject.transform);
                var emptyPresenter = emptyPresenterObject.AddComponent<RemoteAvatarPresenter>();
                emptyPresenter.Initialize(session, 2, emptyRootObject.transform);

                Assert.That(rootObject.activeSelf, Is.True);
                Assert.That(emptyRootObject.activeSelf, Is.False);

                session.Configure(Roster("p0", "p1", "p2"));
                thirdPresenterObject.AddComponent<RemoteAvatarPresenter>().Initialize(session, 2, thirdRootObject.transform);
                emptyPresenter.Initialize(session, 3, emptyRootObject.transform);

                Assert.That(thirdRootObject.activeSelf, Is.True);
                Assert.That(emptyRootObject.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(thirdRootObject);
                UnityEngine.Object.DestroyImmediate(thirdPresenterObject);
                UnityEngine.Object.DestroyImmediate(emptyRootObject);
                UnityEngine.Object.DestroyImmediate(emptyPresenterObject);
            }
        }

        [Test]
        public void EmptyPresentersFollowOneToFourRosterLifecycleWithoutReinitialization()
        {
            var firstPresenterObject = new GameObject("FirstRosterPresenter");
            var firstRootObject = new GameObject("FirstRosterRoot");
            var secondPresenterObject = new GameObject("SecondRosterPresenter");
            var secondRootObject = new GameObject("SecondRosterRoot");
            var thirdPresenterObject = new GameObject("ThirdRosterPresenter");
            var thirdRootObject = new GameObject("ThirdRosterRoot");
            using (var poseSession = new PartyPoseSession(new HostPresenterTransport(), 15f, 100f, 20f))
            {
                try
                {
                    poseSession.Configure(Roster("p0"));
                    firstPresenterObject.AddComponent<RemoteAvatarPresenter>().Initialize(poseSession, 1, firstRootObject.transform);
                    secondPresenterObject.AddComponent<RemoteAvatarPresenter>().Initialize(poseSession, 2, secondRootObject.transform);
                    thirdPresenterObject.AddComponent<RemoteAvatarPresenter>().Initialize(poseSession, 3, thirdRootObject.transform);

                    Assert.That(firstRootObject.activeSelf, Is.False);
                    Assert.That(secondRootObject.activeSelf, Is.False);
                    Assert.That(thirdRootObject.activeSelf, Is.False);

                    poseSession.Configure(Roster("p0", "p1"));
                    Assert.That(firstRootObject.activeSelf, Is.True);
                    Assert.That(secondRootObject.activeSelf, Is.False);

                    poseSession.Configure(Roster("p0", "p1", "p2"));
                    Assert.That(firstRootObject.activeSelf, Is.True);
                    Assert.That(secondRootObject.activeSelf, Is.True);
                    Assert.That(thirdRootObject.activeSelf, Is.False);

                    poseSession.Configure(Roster("p0", "p1", "p2", "p3"));
                    Assert.That(firstRootObject.activeSelf, Is.True);
                    Assert.That(secondRootObject.activeSelf, Is.True);
                    Assert.That(thirdRootObject.activeSelf, Is.True);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(thirdRootObject);
                    UnityEngine.Object.DestroyImmediate(thirdPresenterObject);
                    UnityEngine.Object.DestroyImmediate(secondRootObject);
                    UnityEngine.Object.DestroyImmediate(secondPresenterObject);
                    UnityEngine.Object.DestroyImmediate(firstRootObject);
                    UnityEngine.Object.DestroyImmediate(firstPresenterObject);
                }
            }
        }

        [Test]
        public void OccupiedRemoteRootMovesAndHidesWhenItsPoseIsRemoved()
        {
            var transport = new DeliveringTransport();
            using (var poseSession = new PartyPoseSession(transport, 15f, 100f, 20f))
            {
                poseSession.Configure(Roster());
                var presenter = presenterObject.AddComponent<RemoteAvatarPresenter>();
                presenter.Initialize(poseSession, 2, rootObject.transform, null, 0.01f);

                transport.Deliver("p0", Packet(PartyPoseProtocol.KindRelay, 1, 2, new Vector3(3f, 0f, 4f), 90f));
                poseSession.Tick(1f, Vector3.zero, 0f, PartyMoveState.Idle);
                presenter.Render(float.MaxValue);

                Assert.That(rootObject.transform.position, Is.EqualTo(new Vector3(3f, 0f, 4f)));

                transport.Deliver("p0", Packet(PartyPoseProtocol.KindRemove, 2, 2, Vector3.zero, 0f));
                poseSession.Tick(1.1f, Vector3.zero, 0f, PartyMoveState.Idle);

                Assert.That(rootObject.activeSelf, Is.False);
            }
        }

        [Test]
        public void DisconnectHidesRemoteAvatarRoot()
        {
            var transport = new DeliveringTransport();
            using (var poseSession = new PartyPoseSession(transport, 15f, 100f, 20f))
            {
                poseSession.Configure(Roster());
                var presenter = presenterObject.AddComponent<RemoteAvatarPresenter>();
                presenter.Initialize(poseSession, 2, rootObject.transform);

                transport.Deliver("p0", Packet(PartyPoseProtocol.KindRelay, 1, 2, Vector3.one, 0f));
                poseSession.Tick(1f, Vector3.zero, 0f, PartyMoveState.Idle);
                transport.Disconnect("p2");

                Assert.That(rootObject.activeSelf, Is.False);
            }
        }

        [Test]
        public void ReconfigurationHidesRemovedRemoteAvatarRoot()
        {
            var transport = new DeliveringTransport();
            using (var poseSession = new PartyPoseSession(transport, 15f, 100f, 20f))
            {
                poseSession.Configure(Roster());
                var presenter = presenterObject.AddComponent<RemoteAvatarPresenter>();
                presenter.Initialize(poseSession, 2, rootObject.transform);

                transport.Deliver("p0", Packet(PartyPoseProtocol.KindRelay, 1, 2, Vector3.one, 0f));
                poseSession.Tick(1f, Vector3.zero, 0f, PartyMoveState.Idle);
                poseSession.Configure(Roster("p0", "p1"));

                Assert.That(rootObject.activeSelf, Is.False);
            }
        }

        private static PartyRosterSnapshot Roster()
        {
            var slots = new PartyRosterSlotSnapshot[4];
            for (int slot = 0; slot < slots.Length; slot++)
                slots[slot] = new PartyRosterSlotSnapshot(slot, "p" + slot, "P" + slot, true);
            return new PartyRosterSnapshot("presenter-session", 3, "p0", slots);
        }

        private static PartyRosterSnapshot Roster(params string[] identities)
        {
            var slots = new PartyRosterSlotSnapshot[PartyRoster.Capacity];
            for (int slot = 0; slot < identities.Length; slot++)
                slots[slot] = new PartyRosterSlotSnapshot(slot, identities[slot], identities[slot], true);
            return new PartyRosterSnapshot("presenter-session", 3, "p0", slots);
        }

        private static byte[] Packet(string kind, long sequence, int slot, Vector3 position, float yawDegrees)
        {
            return PartyPoseProtocol.Encode(new PartyPosePacket
            {
                game = PartyPoseProtocol.GameId,
                version = PartyPoseProtocol.Version,
                sessionId = "presenter-session",
                rosterGeneration = 3,
                transitionGeneration = 0,
                sequence = sequence,
                kind = kind,
                slot = slot,
                positionX = position.x,
                positionY = position.y,
                positionZ = position.z,
                yawDegrees = yawDegrees,
                moveState = (int)PartyMoveState.Walking
            });
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

        private sealed class DeliveringTransport : INetTransport
        {
            public bool IsHost => false;
            public string LocalPlayerId => "p1";
            public event Action<string> OnPeerConnected { add { } remove { } }
            public event Action<string> OnPeerDisconnected;
            public event Action<string, byte[]> OnMessage;
            public void SendToHost(byte[] data, bool reliable) { }
            public void SendTo(string playerId, byte[] data, bool reliable) { }
            public void Tick() { }
            internal void Deliver(string peerIdentity, byte[] bytes) => OnMessage?.Invoke(peerIdentity, bytes);
            internal void Disconnect(string peerIdentity) => OnPeerDisconnected?.Invoke(peerIdentity);
            public void Shutdown() { }
        }

        private sealed class HostPresenterTransport : INetTransport
        {
            public bool IsHost => true;
            public string LocalPlayerId => "p0";
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
