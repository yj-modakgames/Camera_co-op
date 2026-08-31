using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CameraCoop.Netplay;
using CameraCoop.Party;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    public sealed class PartyPoseSessionTests
    {
        private const string SessionId = "pose-session";
        private const int Generation = 9;

        [Test]
        public void FourEndpointsRelayClientPoseOnlyToAuthenticatedOtherPeers()
        {
            using (Fixture fixture = new Fixture())
            {
                var received = new List<PartyPoseSample>[4];
                for (int i = 0; i < received.Length; i++)
                {
                    int slot = i;
                    received[i] = new List<PartyPoseSample>();
                    fixture.Sessions[i].RemotePoseUpdated += sample => received[slot].Add(sample);
                }

                fixture.Sessions[1].Tick(0f, new Vector3(2f, 0f, 3f), 45f, PartyMoveState.Walking);
                fixture.PumpAt(0f);

                Assert.That(received[0], Has.Exactly(1).Matches<PartyPoseSample>(x => x.Slot == 1));
                Assert.That(received[1], Has.None.Matches<PartyPoseSample>(x => x.Slot == 1),
                    "sender must not receive its own remote avatar");
                Assert.That(received[2], Has.Exactly(1).Matches<PartyPoseSample>(x => x.Slot == 1));
                Assert.That(received[3], Has.Exactly(1).Matches<PartyPoseSample>(x => x.Slot == 1));
                CollectionAssert.AreEquivalent(new[] { "p2", "p3" }, fixture.Transports[0].PoseRelayTargets(1));
            }
        }

        [Test]
        public void HostUsesTransportIdentityAndRewritesForgedSlot()
        {
            using (Fixture fixture = new Fixture())
            {
                PartyPoseSample accepted = default;
                int acceptedCount = 0;
                fixture.Sessions[0].RemotePoseUpdated += sample => { accepted = sample; acceptedCount++; };
                fixture.Transports[1].SendToHost(Packet("submit", 50, 3, Vector3.one, 10f), false);

                fixture.Sessions[0].Tick(1f, Vector3.zero, 0f, PartyMoveState.Idle);

                Assert.That(acceptedCount, Is.EqualTo(1));
                Assert.That(accepted.Slot, Is.EqualTo(1));
                Assert.That(fixture.Transports[0].PoseRelayTargets(1), Has.Length.EqualTo(2));
            }
        }

        [Test]
        public void StaleDuplicateSessionAndRosterPacketsAreRejectedPerSender()
        {
            using (Fixture fixture = new Fixture())
            {
                int accepted = 0;
                fixture.Sessions[0].RemotePoseUpdated += _ => accepted++;

                fixture.Transports[1].SendToHost(Packet("submit", 10, 1, Vector3.zero, 0f), false);
                fixture.Sessions[0].Tick(1f, Vector3.zero, 0f, PartyMoveState.Idle);
                fixture.Transports[1].SendToHost(Packet("submit", 10, 1, Vector3.zero, 0f), false);
                fixture.Transports[1].SendToHost(Packet("submit", 9, 1, Vector3.zero, 0f), false);
                fixture.Transports[1].SendToHost(Packet("submit", 11, 1, Vector3.zero, 0f, "old-session", Generation), false);
                fixture.Transports[1].SendToHost(Packet("submit", 12, 1, Vector3.zero, 0f, SessionId, Generation - 1), false);
                fixture.Sessions[0].Tick(1.1f, Vector3.zero, 0f, PartyMoveState.Idle);

                Assert.That(accepted, Is.EqualTo(1));
            }
        }

        [Test]
        public void NonFiniteOutOfBoundsAndOverSpeedPosesAreRejected()
        {
            using (Fixture fixture = new Fixture(positionBound: 20f, maxSpeed: 5f))
            {
                int accepted = 0;
                fixture.Sessions[0].RemotePoseUpdated += _ => accepted++;

                fixture.Transports[1].SendToHost(Packet("submit", 1, 1, Vector3.zero, 0f), false);
                fixture.Sessions[0].Tick(1f, Vector3.zero, 0f, PartyMoveState.Idle);
                fixture.Transports[1].SendToHost(Packet("submit", 2, 1, new Vector3(float.NaN, 0f, 0f), 0f), false);
                fixture.Transports[1].SendToHost(Packet("submit", 3, 1, new Vector3(21f, 0f, 0f), 0f), false);
                fixture.Transports[1].SendToHost(Packet("submit", 4, 1, new Vector3(10f, 0f, 0f), 0f), false);
                fixture.Transports[1].SendToHost(Packet("submit", 5, 1, new Vector3(0.4f, 0f, 0f), 30f), false);
                fixture.Sessions[0].Tick(1.1f, Vector3.zero, 0f, PartyMoveState.Idle);

                Assert.That(accepted, Is.EqualTo(2), "first and physically reachable samples only");
            }
        }

        [Test]
        public void UnknownAndDisconnectedPeersCannotPublishAndDisconnectRemovesEveryRemotePose()
        {
            using (Fixture fixture = new Fixture())
            {
                int hostUpdates = 0;
                var hostRemoved = new List<int>();
                var peerTwoRemoved = new List<int>();
                fixture.Sessions[0].RemotePoseUpdated += _ => hostUpdates++;
                fixture.Sessions[0].RemotePoseRemoved += hostRemoved.Add;
                fixture.Sessions[2].RemotePoseRemoved += peerTwoRemoved.Add;

                fixture.Network.Inject("intruder", "p0", Packet("submit", 1, 3, Vector3.zero, 0f));
                fixture.Sessions[0].Tick(0f, Vector3.zero, 0f, PartyMoveState.Idle);
                Assert.That(hostUpdates, Is.Zero);

                fixture.Sessions[1].Tick(0f, Vector3.one, 0f, PartyMoveState.Idle);
                fixture.PumpAt(0f);
                fixture.Network.Disconnect("p1");
                fixture.PumpAt(0.1f);

                CollectionAssert.Contains(hostRemoved, 1);
                CollectionAssert.Contains(peerTwoRemoved, 1);
                int before = hostUpdates;
                fixture.Network.Inject("p1", "p0", Packet("submit", 99, 1, Vector3.zero, 0f));
                fixture.Sessions[0].Tick(0.2f, Vector3.zero, 0f, PartyMoveState.Idle);
                Assert.That(hostUpdates, Is.EqualTo(before), "disconnect is rejected until a new roster snapshot is configured");
            }
        }

        [Test]
        public void LocalPoseTransmissionNeverExceedsFifteenHertz()
        {
            using (Fixture fixture = new Fixture())
            {
                fixture.Sessions[1].Tick(0f, Vector3.zero, 0f, PartyMoveState.Idle);
                fixture.Sessions[1].Tick(0.01f, Vector3.zero, 0f, PartyMoveState.Idle);
                fixture.Sessions[1].Tick(0.066f, Vector3.zero, 0f, PartyMoveState.Idle);
                fixture.Sessions[1].Tick(0.067f, Vector3.zero, 0f, PartyMoveState.Idle);

                Assert.That(fixture.Transports[1].SentToHost.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void SerializedPoseContractContainsNoPrivateCameraOrGameData()
        {
            string[] forbidden = { "frame", "landmark", "fist", "pinch", "drawing", "word", "answer", "sender" };
            foreach (FieldInfo field in typeof(PartyPosePacket).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                foreach (string term in forbidden)
                    Assert.That(field.Name.ToLowerInvariant(), Does.Not.Contain(term), field.Name);
            }

            string json = Encoding.UTF8.GetString(Packet("submit", 1, 2, new Vector3(1f, 2f, 3f), 90f)).ToLowerInvariant();
            foreach (string term in forbidden) Assert.That(json, Does.Not.Contain(term));
        }

        private static byte[] Packet(
            string kind,
            long sequence,
            int slot,
            Vector3 position,
            float yaw,
            string sessionId = SessionId,
            int rosterGeneration = Generation)
        {
            return PartyPoseProtocol.Encode(new PartyPosePacket
            {
                game = PartyPoseProtocol.GameId,
                version = PartyPoseProtocol.Version,
                sessionId = sessionId,
                rosterGeneration = rosterGeneration,
                sequence = sequence,
                kind = kind,
                slot = slot,
                positionX = position.x,
                positionY = position.y,
                positionZ = position.z,
                yawDegrees = yaw,
                moveState = (int)PartyMoveState.Walking
            });
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly FakePoseNetwork Network = new FakePoseNetwork();
            internal readonly FakePoseTransport[] Transports = new FakePoseTransport[4];
            internal readonly PartyPoseSession[] Sessions = new PartyPoseSession[4];

            internal Fixture(float positionBound = 100f, float maxSpeed = 20f)
            {
                PartyRosterSnapshot roster = Roster();
                for (int slot = 0; slot < 4; slot++)
                {
                    Transports[slot] = Network.Add("p" + slot, slot == 0);
                    Sessions[slot] = new PartyPoseSession(Transports[slot], 15f, positionBound, maxSpeed);
                    Sessions[slot].Configure(roster);
                }
            }

            internal void PumpAt(float now)
            {
                for (int cycle = 0; cycle < 4; cycle++)
                {
                    for (int slot = 0; slot < 4; slot++)
                        Sessions[slot].Tick(now, new Vector3(slot * 2f, 0f, 0f), 0f, PartyMoveState.Idle);
                }
            }

            public void Dispose()
            {
                for (int i = 0; i < Sessions.Length; i++) Sessions[i]?.Dispose();
            }
        }

        private static PartyRosterSnapshot Roster()
        {
            var slots = new PartyRosterSlotSnapshot[4];
            for (int slot = 0; slot < slots.Length; slot++)
                slots[slot] = new PartyRosterSlotSnapshot(slot, "p" + slot, "P" + slot, true);
            return new PartyRosterSnapshot(SessionId, Generation, "p0", slots);
        }

        private sealed class FakePoseNetwork
        {
            private readonly Dictionary<string, FakePoseTransport> endpoints = new Dictionary<string, FakePoseTransport>();

            internal FakePoseTransport Add(string id, bool isHost)
            {
                var transport = new FakePoseTransport(this, id, isHost);
                endpoints.Add(id, transport);
                return transport;
            }

            internal void Send(string sender, string recipient, byte[] bytes)
            {
                if (endpoints.TryGetValue(recipient, out FakePoseTransport target)) target.Enqueue(sender, bytes);
            }

            internal void Inject(string sender, string recipient, byte[] bytes)
            {
                Send(sender, recipient, bytes);
            }

            internal void Disconnect(string id)
            {
                foreach (FakePoseTransport endpoint in endpoints.Values) endpoint.RaiseDisconnected(id);
            }
        }

        private sealed class FakePoseTransport : INetTransport
        {
            private readonly FakePoseNetwork network;
            private readonly Queue<Message> pending = new Queue<Message>();
            internal readonly List<byte[]> SentToHost = new List<byte[]>();
            internal readonly List<SentMessage> Sent = new List<SentMessage>();

            internal FakePoseTransport(FakePoseNetwork network, string id, bool isHost)
            {
                this.network = network;
                LocalPlayerId = id;
                IsHost = isHost;
            }

            public bool IsHost { get; }
            public string LocalPlayerId { get; }
            public event Action<string> OnPeerConnected { add { } remove { } }
            public event Action<string> OnPeerDisconnected;
            public event Action<string, byte[]> OnMessage;

            public void SendToHost(byte[] data, bool reliable)
            {
                SentToHost.Add(data);
                network.Send(LocalPlayerId, "p0", data);
            }

            public void SendTo(string playerId, byte[] data, bool reliable)
            {
                Sent.Add(new SentMessage(playerId, data));
                network.Send(LocalPlayerId, playerId, data);
            }

            public void Tick()
            {
                while (pending.Count > 0)
                {
                    Message message = pending.Dequeue();
                    OnMessage?.Invoke(message.Sender, message.Bytes);
                }
            }

            public void Shutdown() { }

            internal void Enqueue(string sender, byte[] bytes) => pending.Enqueue(new Message(sender, bytes));
            internal void RaiseDisconnected(string id) => OnPeerDisconnected?.Invoke(id);

            internal string[] PoseRelayTargets(int slot)
            {
                var targets = new List<string>();
                foreach (SentMessage message in Sent)
                {
                    if (PartyPoseProtocol.TryDecode(message.Bytes, out PartyPosePacket packet)
                        && packet.kind == PartyPoseProtocol.KindRelay && packet.slot == slot)
                        targets.Add(message.Target);
                }
                return targets.ToArray();
            }

            private readonly struct Message
            {
                internal Message(string sender, byte[] bytes) { Sender = sender; Bytes = bytes; }
                internal string Sender { get; }
                internal byte[] Bytes { get; }
            }

            internal readonly struct SentMessage
            {
                internal SentMessage(string target, byte[] bytes) { Target = target; Bytes = bytes; }
                internal string Target { get; }
                internal byte[] Bytes { get; }
            }
        }
    }
}
