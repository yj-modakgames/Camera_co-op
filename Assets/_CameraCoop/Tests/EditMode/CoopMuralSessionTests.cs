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
    public sealed class CoopMuralSessionTests
    {
        private const string SessionId = "mural-session";
        private const int Generation = 17;
        private const int StartSignal = 8;

        [Test]
        public void OnlyP1CanPublishAtRoundStart()
        {
            using (var fixture = new Fixture())
            {
                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                {
                    fixture.Drawings[slot] = Drawing(0.1f + slot * 0.2f);
                    fixture.Sessions[slot].Tick(0f, 1);
                }

                fixture.Pump(0f);

                for (int viewer = 0; viewer < PartyRoster.Capacity; viewer++)
                {
                    Assert.That(fixture.Sessions[viewer].View.ActiveSlot, Is.Zero);
                    for (int owner = 0; owner < PartyRoster.Capacity; owner++)
                    {
                        Assert.That(fixture.Sessions[viewer].View.TryGetLayer(owner, out CoopMuralLayerSnapshot layer), Is.True);
                        Assert.That(layer.Revision, Is.EqualTo(owner == 0 ? 1 : 0));
                    }
                }

                CollectionAssert.AreEquivalent(new[] { "p1", "p2", "p3" }, fixture.Transports[0].RelayTargets(0, 1));
                Assert.That(fixture.Transports[0].RelayTargets(1, 1), Is.Empty);
                Assert.That(fixture.Transports[0].RelayTargets(2, 1), Is.Empty);
                Assert.That(fixture.Transports[0].RelayTargets(3, 1), Is.Empty);
            }
        }

        [Test]
        public void CompletingP1AdvancesToP2AndFreezesP1Layer()
        {
            using (var fixture = new Fixture())
            {
                fixture.Drawings[0] = Drawing(0.2f);
                fixture.Sessions[0].Tick(0f, 1);
                fixture.Pump(0f);

                Assert.That(fixture.Sessions[0].CompleteLocalTurn(1), Is.True);
                fixture.Pump(0.1f);

                fixture.Drawings[0] = Drawing(0.9f);
                fixture.Sessions[0].Tick(0.2f, 2);
                fixture.Drawings[1] = Drawing(0.4f);
                fixture.Sessions[1].Tick(0.2f, 1);
                fixture.Pump(0.2f);

                for (int viewer = 0; viewer < PartyRoster.Capacity; viewer++)
                {
                    Assert.That(fixture.Sessions[viewer].View.ActiveSlot, Is.EqualTo(1));
                    Assert.That(fixture.Sessions[viewer].View.TryGetLayer(0, out CoopMuralLayerSnapshot p1), Is.True);
                    Assert.That(p1.Revision, Is.EqualTo(1));
                    Assert.That(p1.Drawing.strokes[0].xy[0], Is.EqualTo(0.2f));
                    Assert.That(fixture.Sessions[viewer].View.TryGetLayer(1, out CoopMuralLayerSnapshot p2), Is.True);
                    Assert.That(p2.Revision, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void P1ThroughP4CompletionEndsInPublicFinalDisplay()
        {
            using (var fixture = new Fixture())
            {
                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                {
                    fixture.Drawings[slot] = Drawing(0.1f + slot * 0.2f);
                    fixture.Sessions[slot].Tick(slot, 1);
                    fixture.Pump(slot);
                    Assert.That(fixture.Sessions[slot].CompleteLocalTurn(1), Is.True);
                    fixture.Pump(slot + 0.1f);
                }

                for (int viewer = 0; viewer < PartyRoster.Capacity; viewer++)
                {
                    Assert.That(fixture.Sessions[viewer].View.IsFinalDisplay, Is.True);
                    Assert.That(fixture.Sessions[viewer].View.ActiveSlot, Is.EqualTo(-1));
                    for (int owner = 0; owner < PartyRoster.Capacity; owner++)
                    {
                        Assert.That(fixture.Sessions[viewer].View.TryGetLayer(owner, out CoopMuralLayerSnapshot layer), Is.True);
                        Assert.That(layer.Revision, Is.EqualTo(1));
                        Assert.That(layer.Drawing.strokes[0].xy[0], Is.EqualTo(0.1f + owner * 0.2f).Within(0.0001f));
                    }
                }
            }
        }

        [Test]
        public void HostSignalsFinalDisplayExactlyOnceWhenP4Completes()
        {
            using (var fixture = new Fixture())
            {
                int finalDisplaySignals = 0;
                fixture.Sessions[0].FinalDisplayReached += () => finalDisplaySignals++;

                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                {
                    fixture.Sessions[slot].Tick(slot, 1);
                    fixture.Pump(slot);
                    Assert.That(fixture.Sessions[slot].CompleteLocalTurn(1), Is.True);
                    fixture.Pump(slot + 0.1f);
                }

                Assert.That(fixture.Sessions[0].View.IsFinalDisplay, Is.True);
                Assert.That(finalDisplaySignals, Is.EqualTo(1));
            }
        }

        [Test]
        public void PriorEpochRelayIsRejectedEvenWhenItsSequenceAndRevisionAreHigher()
        {
            using (var fixture = new Fixture())
            {
                int serial = fixture.Sessions[2].View.Serial;
                fixture.Network.Inject("p0", "p2", SnapshotPacket(
                    CoopMuralProtocol.KindRelay, 100, 0, 99, Drawing(0.9f), StartSignal - 1));

                fixture.Sessions[2].Tick(0f, 0);

                Assert.That(fixture.Sessions[2].View.Serial, Is.EqualTo(serial));
                Assert.That(fixture.Sessions[2].View.TryGetLayer(0, out CoopMuralLayerSnapshot layer), Is.True);
                Assert.That(layer.Revision, Is.Zero);
                Assert.That(fixture.Transports[2].Sent, Is.Empty);
            }
        }

        [Test]
        public void PriorEpochTurnCompleteIsRejectedEvenWhenItsSequenceAndRevisionAreHigher()
        {
            using (var fixture = new Fixture())
            {
                int serial = fixture.Sessions[0].View.Serial;
                fixture.Network.Inject("p1", "p0", CoopMuralProtocol.Encode(new CoopMuralPacket
                {
                    sessionId = SessionId,
                    rosterGeneration = Generation,
                    startSignal = StartSignal - 1,
                    sequence = 100,
                    kind = CoopMuralProtocol.KindTurnComplete,
                    ownerSlot = 1,
                    revision = 99,
                    payload = "{}"
                }));

                fixture.Sessions[0].Tick(0f, 0);

                Assert.That(fixture.Sessions[0].View.Serial, Is.EqualTo(serial));
                Assert.That(fixture.Sessions[0].View.ActiveSlot, Is.Zero);
                Assert.That(fixture.Transports[0].Sent, Is.Empty);
            }
        }

        [Test]
        public void ChangedRevisionIsCapturedAtMostTenHertzAndUnchangedTicksDoNotCapture()
        {
            using (var fixture = new Fixture())
            {
                fixture.AdvanceTo(1);
                fixture.Sessions[1].Tick(0f, 1);
                fixture.Sessions[1].Tick(0.02f, 1);
                fixture.Sessions[1].Tick(0.05f, 2);
                fixture.Sessions[1].Tick(0.099f, 2);
                Assert.That(fixture.Captures[1], Is.EqualTo(1));
                Assert.That(fixture.Transports[1].SentToHost.Count, Is.GreaterThan(0));

                fixture.Sessions[1].Tick(0.1f, 2);
                fixture.Sessions[1].Tick(1f, 2);

                Assert.That(fixture.Captures[1], Is.EqualTo(2));
                Assert.That(fixture.Sessions[1].View.TryGetLayer(1, out CoopMuralLayerSnapshot layer), Is.True);
                Assert.That(layer.Revision, Is.EqualTo(2));
            }
        }

        [Test]
        public void InvalidSnapshotIsRetriedWhenTheSameRevisionBecomesValid()
        {
            using (var fixture = new Fixture())
            {
                fixture.AdvanceTo(1);
                fixture.Drawings[1] = new CanvasDrawingData { strokes = null };
                fixture.Sessions[1].Tick(0f, 1);
                Assert.That(fixture.Captures[1], Is.EqualTo(1));
                Assert.That(fixture.Transports[1].SentToHost, Is.Empty);

                fixture.Drawings[1] = Drawing(0.4f);
                fixture.Sessions[1].Tick(0.01f, 1);

                Assert.That(fixture.Captures[1], Is.EqualTo(2));
                Assert.That(fixture.Transports[1].SentToHost, Is.Not.Empty);
                Assert.That(fixture.Sessions[1].View.TryGetLayer(1, out CoopMuralLayerSnapshot layer), Is.True);
                Assert.That(layer.Revision, Is.EqualTo(1));
            }
        }

        [Test]
        public void OversizedSnapshotIsRetriedWhenTheSameRevisionBecomesValid()
        {
            using (var fixture = new Fixture())
            {
                fixture.AdvanceTo(1);
                fixture.Drawings[1] = OversizedDrawing();
                fixture.Sessions[1].Tick(0f, 1);
                Assert.That(fixture.Captures[1], Is.EqualTo(1));
                Assert.That(fixture.Transports[1].SentToHost, Is.Empty);

                fixture.Drawings[1] = Drawing(0.4f);
                fixture.Sessions[1].Tick(0.01f, 1);

                Assert.That(fixture.Captures[1], Is.EqualTo(2));
                Assert.That(fixture.Transports[1].SentToHost, Is.Not.Empty);
                Assert.That(fixture.Sessions[1].View.TryGetLayer(1, out CoopMuralLayerSnapshot layer), Is.True);
                Assert.That(layer.Revision, Is.EqualTo(1));
            }
        }

        [Test]
        public void HostRejectsClaimedOwnerThatDoesNotMatchTransportIdentity()
        {
            using (var fixture = new Fixture())
            {
                fixture.AdvanceTo(1);
                byte[] forged = SnapshotPacket("submit", 1, 2, 1, Drawing(0.5f));
                fixture.Network.Inject("p1", "p0", forged);
                fixture.Sessions[0].Tick(0f, 0);

                Assert.That(fixture.Sessions[0].View.TryGetLayer(2, out CoopMuralLayerSnapshot layer), Is.True);
                Assert.That(layer.Revision, Is.Zero);
                Assert.That(fixture.Transports[0].RelayTargets(2, 1), Is.Empty);
            }
        }

        [Test]
        public void DuplicateOutOfOrderAndNonHostRelayPacketsAreRejected()
        {
            using (var fixture = new Fixture())
            {
                fixture.AdvanceTo(1);
                fixture.Network.Inject("p0", "p2", SnapshotPacket("relay", 100, 1, 2, Drawing(0.6f)));
                fixture.Sessions[2].Tick(0f, 0);
                Assert.That(fixture.Sessions[2].View.TryGetLayer(1, out CoopMuralLayerSnapshot current), Is.True);
                Assert.That(current.Revision, Is.EqualTo(2));

                fixture.Network.Inject("p0", "p2", SnapshotPacket("relay", 101, 1, 2, Drawing(0.7f)));
                fixture.Network.Inject("p0", "p2", SnapshotPacket("relay", 102, 1, 1, Drawing(0.8f)));
                fixture.Network.Inject("p1", "p2", SnapshotPacket("relay", 103, 1, 3, Drawing(0.9f)));
                fixture.Sessions[2].Tick(0.1f, 0);

                Assert.That(fixture.Sessions[2].View.TryGetLayer(1, out current), Is.True);
                Assert.That(current.Revision, Is.EqualTo(2));
                Assert.That(current.Drawing.strokes[0].xy[0], Is.EqualTo(0.6f));
            }
        }

        [Test]
        public void MalformedOversizedAndInvalidChunkPacketsDoNotChangeView()
        {
            using (var fixture = new Fixture())
            {
                fixture.Network.Inject("p1", "p0", Encoding.UTF8.GetBytes("{broken"));
                fixture.Network.Inject("p1", "p0", new byte[CoopMuralProtocol.MaxMessageBytes + 1]);
                fixture.Network.Inject("p1", "p0", PacketWithChunk("submit", 1, 1, 1,
                    new CoopMuralChunk { transferId = "bad", index = 1, count = 1, total = 10, data = "not-base64" }));
                fixture.Sessions[0].Tick(0f, 0);

                Assert.That(fixture.Sessions[0].View.TryGetLayer(1, out CoopMuralLayerSnapshot layer), Is.True);
                Assert.That(layer.Revision, Is.Zero);
            }
        }

        [Test]
        public void IncomingSnapshotCannotOverwriteTheClientsLocalLayer()
        {
            using (var fixture = new Fixture())
            {
                fixture.AdvanceTo(2);
                fixture.Drawings[2] = Drawing(0.2f);
                fixture.Sessions[2].Tick(0f, 1);
                fixture.Network.Inject("p0", "p2", SnapshotPacket("relay", 100, 2, 99, Drawing(0.9f)));
                fixture.Sessions[2].Tick(0.1f, 1);

                Assert.That(fixture.Sessions[2].View.TryGetLayer(2, out CoopMuralLayerSnapshot layer), Is.True);
                Assert.That(layer.Revision, Is.EqualTo(1));
                Assert.That(layer.Drawing.strokes[0].xy[0], Is.EqualTo(0.2f));
            }
        }

        [Test]
        public void AnyDisconnectAbortsAndClearsEveryPublicLayerWithoutReassignment()
        {
            using (var fixture = new Fixture())
            {
                fixture.Sessions[0].Tick(0f, 1);
                fixture.Pump(0f);
                fixture.Network.Disconnect("p2");
                fixture.Pump(0.1f);

                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                {
                    Assert.That(fixture.Sessions[slot].View.Aborted, Is.True);
                    Assert.That(fixture.Sessions[slot].View.LocalSlot, Is.EqualTo(slot));
                    for (int owner = 0; owner < PartyRoster.Capacity; owner++)
                    {
                        Assert.That(fixture.Sessions[slot].View.TryGetLayer(owner, out CoopMuralLayerSnapshot layer), Is.True);
                        Assert.That(layer.Revision, Is.Zero);
                        Assert.That(layer.Drawing, Is.Null);
                    }
                }
            }
        }

        [Test]
        public void ResetClearsCacheAndDisposeUnsubscribesWithoutShuttingDownTransport()
        {
            var fixture = new Fixture();
            fixture.Sessions[1].Tick(0f, 1);
            fixture.Sessions[1].Reset();
            Assert.That(fixture.Sessions[1].View.Configured, Is.False);
            Assert.That(fixture.Sessions[1].View.TryGetLayer(1, out CoopMuralLayerSnapshot layer), Is.True);
            Assert.That(layer.Drawing, Is.Null);

            fixture.Dispose();

            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                Assert.That(fixture.Transports[slot].ShutdownCalls, Is.Zero);
        }

        [Test]
        public void ConfigureRequiresCoopMuralAndCompleteFixedRoleMatchedRoster()
        {
            var network = new FakeNetwork();
            var transport = network.Add("p0", true);
            using (var session = new CoopMuralSession(transport, () => Drawing(0.2f), 3))
            {
                Assert.Throws<ArgumentException>(() => session.Configure(Start(PartyMode.RelayCopy), StartSignal));
                Assert.Throws<ArgumentOutOfRangeException>(() => session.Configure(Start(PartyMode.CoopMural), 0));
                session.Configure(Start(PartyMode.CoopMural), StartSignal);
                Assert.That(session.View.Configured, Is.True);
                Assert.That(session.View.LocalSlot, Is.Zero);
                Assert.That(session.View.Layers.Count, Is.EqualTo(PartyRoster.Capacity));
            }
        }

        [Test]
        public void ProtocolRejectsPacketsWithOmittedOrZeroStartSignal()
        {
            var omitted = new CoopMuralPacket
            {
                sessionId = SessionId,
                rosterGeneration = Generation,
                sequence = 1,
                kind = CoopMuralProtocol.KindAbort,
                payload = "{}"
            };
            var zero = new CoopMuralPacket
            {
                sessionId = SessionId,
                rosterGeneration = Generation,
                startSignal = 0,
                sequence = 2,
                kind = CoopMuralProtocol.KindAbort,
                payload = "{}"
            };

            Assert.That(CoopMuralProtocol.TryDecode(CoopMuralProtocol.Encode(omitted), out _), Is.False);
            Assert.That(CoopMuralProtocol.TryDecode(CoopMuralProtocol.Encode(zero), out _), Is.False);
        }

        [Test]
        public void PublicWireAndViewContractsContainNoWordAnswerOrPrivateFields()
        {
            Type[] types = { typeof(CoopMuralPacket), typeof(CoopMuralChunk), typeof(CoopMuralView), typeof(CoopMuralLayerSnapshot) };
            string[] forbidden = { "word", "answer", "secret", "private", "reference" };
            foreach (Type type in types)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                foreach (string term in forbidden)
                    Assert.That(field.Name.ToLowerInvariant(), Does.Not.Contain(term), type.Name + "." + field.Name);

                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                foreach (string term in forbidden)
                    Assert.That(property.Name.ToLowerInvariant(), Does.Not.Contain(term), type.Name + "." + property.Name);
            }
        }

        private static byte[] SnapshotPacket(
            string kind,
            long sequence,
            int ownerSlot,
            int revision,
            CanvasDrawingData drawing,
            int startSignal = StartSignal)
        {
            Assert.That(CoopMuralProtocol.TryDrawingBytes(drawing, 3, out byte[] bytes), Is.True);
            return PacketWithChunk(kind, sequence, ownerSlot, revision, new CoopMuralChunk
            {
                transferId = "manual-" + ownerSlot + "-" + revision,
                index = 0,
                count = 1,
                total = bytes.Length,
                data = Convert.ToBase64String(bytes)
            }, startSignal);
        }

        private static byte[] PacketWithChunk(
            string kind,
            long sequence,
            int ownerSlot,
            int revision,
            CoopMuralChunk chunk,
            int startSignal = StartSignal)
        {
            return CoopMuralProtocol.Encode(new CoopMuralPacket
            {
                sessionId = SessionId,
                rosterGeneration = Generation,
                startSignal = startSignal,
                sequence = sequence,
                kind = kind,
                ownerSlot = ownerSlot,
                revision = revision,
                payload = JsonUtility.ToJson(chunk)
            });
        }

        private static CanvasDrawingData Drawing(float x)
        {
            return new CanvasDrawingData
            {
                strokes = new[]
                {
                    new CanvasStrokeData
                    {
                        strokeId = 1,
                        order = 0,
                        xy = new[] { x, 0.2f, Math.Min(1f, x + 0.05f), 0.3f },
                        colorArgb = unchecked((int)0xFFFFFFFF),
                        widthNormalized = 0.1f,
                        brushId = 0
                    }
                }
            };
        }

        private static CanvasDrawingData OversizedDrawing()
        {
            var xy = new float[CoopMuralProtocol.MaxPoints * 2];
            for (int index = 0; index < xy.Length; index++) xy[index] = 0.1234567f;
            return new CanvasDrawingData
            {
                strokes = new[]
                {
                    new CanvasStrokeData
                    {
                        strokeId = 1,
                        order = 0,
                        xy = xy,
                        colorArgb = unchecked((int)0xFFFFFFFF),
                        widthNormalized = 0.1f,
                        brushId = 0
                    }
                }
            };
        }

        private static PartyStartSnapshot Start(PartyMode mode)
        {
            var slots = new PartyRosterSlotSnapshot[PartyRoster.Capacity];
            for (int slot = 0; slot < slots.Length; slot++)
                slots[slot] = new PartyRosterSlotSnapshot(slot, "p" + slot, "P" + slot, true);
            return new PartyStartSnapshot(mode, new PartyRosterSnapshot(SessionId, Generation, "p0", slots));
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly FakeNetwork Network = new FakeNetwork();
            internal readonly FakeTransport[] Transports = new FakeTransport[PartyRoster.Capacity];
            internal readonly CoopMuralSession[] Sessions = new CoopMuralSession[PartyRoster.Capacity];
            internal readonly CanvasDrawingData[] Drawings = new CanvasDrawingData[PartyRoster.Capacity];
            internal readonly int[] Captures = new int[PartyRoster.Capacity];

            internal Fixture()
            {
                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                {
                    int sourceSlot = slot;
                    Drawings[slot] = Drawing(0.1f + slot * 0.1f);
                    Transports[slot] = Network.Add("p" + slot, slot == 0);
                    Sessions[slot] = new CoopMuralSession(Transports[slot], () =>
                    {
                        Captures[sourceSlot]++;
                        return Drawings[sourceSlot];
                    }, 3);
                    Sessions[slot].Configure(Start(PartyMode.CoopMural), StartSignal);
                }
            }

            internal void Pump(float now)
            {
                for (int cycle = 0; cycle < 8; cycle++)
                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                    Sessions[slot].Tick(now, Sessions[slot].View.TryGetLayer(slot, out CoopMuralLayerSnapshot layer)
                        ? layer.Revision : 0);
            }

            internal void AdvanceTo(int activeSlot)
            {
                for (int slot = 0; slot < activeSlot; slot++)
                {
                    Sessions[slot].Tick(0f, 1);
                    Pump(0f);
                    Assert.That(Sessions[slot].CompleteLocalTurn(1), Is.True);
                    Pump(0f);
                }
            }

            public void Dispose()
            {
                for (int slot = 0; slot < Sessions.Length; slot++) Sessions[slot]?.Dispose();
            }
        }

        private sealed class FakeNetwork
        {
            private readonly Dictionary<string, FakeTransport> endpoints = new Dictionary<string, FakeTransport>();

            internal FakeTransport Add(string identity, bool isHost)
            {
                var transport = new FakeTransport(this, identity, isHost);
                endpoints.Add(identity, transport);
                return transport;
            }

            internal void Send(string sender, string recipient, byte[] bytes)
            {
                if (endpoints.TryGetValue(recipient, out FakeTransport transport)) transport.Enqueue(sender, bytes);
            }

            internal void Inject(string sender, string recipient, byte[] bytes) => Send(sender, recipient, bytes);

            internal void Disconnect(string identity)
            {
                foreach (FakeTransport transport in endpoints.Values) transport.RaiseDisconnected(identity);
            }
        }

        private sealed class FakeTransport : INetTransport
        {
            private readonly FakeNetwork network;
            private readonly Queue<Message> pending = new Queue<Message>();
            internal readonly List<SentMessage> Sent = new List<SentMessage>();
            internal readonly List<byte[]> SentToHost = new List<byte[]>();

            internal FakeTransport(FakeNetwork network, string localPlayerId, bool isHost)
            {
                this.network = network;
                LocalPlayerId = localPlayerId;
                IsHost = isHost;
            }

            public bool IsHost { get; }
            public string LocalPlayerId { get; }
            public int ShutdownCalls { get; private set; }
            public event Action<string> OnPeerConnected { add { } remove { } }
            public event Action<string> OnPeerDisconnected;
            public event Action<string, byte[]> OnMessage;

            public void SendToHost(byte[] data, bool reliable)
            {
                Assert.That(reliable, Is.True);
                SentToHost.Add(data);
                network.Send(LocalPlayerId, "p0", data);
            }

            public void SendTo(string playerId, byte[] data, bool reliable)
            {
                Assert.That(reliable, Is.True);
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

            public void Shutdown() => ShutdownCalls++;
            internal void Enqueue(string sender, byte[] bytes) => pending.Enqueue(new Message(sender, bytes));
            internal void RaiseDisconnected(string identity) => OnPeerDisconnected?.Invoke(identity);

            internal string[] RelayTargets(int ownerSlot, int revision)
            {
                var targets = new List<string>();
                foreach (SentMessage message in Sent)
                {
                    if (CoopMuralProtocol.TryDecode(message.Bytes, out CoopMuralPacket packet)
                        && packet.kind == CoopMuralProtocol.KindRelay
                        && packet.ownerSlot == ownerSlot && packet.revision == revision)
                        targets.Add(message.Target);
                }
                return targets.ToArray();
            }

            internal readonly struct Message
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
