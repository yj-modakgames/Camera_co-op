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
    public sealed class PartyPracticeDrawingSessionTests
    {
        private const string SessionId = "practice-session";
        private const int RosterGeneration = 9;
        private const int TransitionGeneration = 3;

        [Test]
        public void FourOwnersPublishIndependentPublicLayers()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                {
                    fixture.Drawings[slot] = Drawing(0.1f + slot * 0.2f);
                    fixture.Sessions[slot].Tick(0f, 1, PartyPracticeDrawingPhase.Lobby);
                }

                fixture.Pump(0f);

                for (int viewer = 0; viewer < PartyRoster.Capacity; viewer++)
                for (int owner = 0; owner < PartyRoster.Capacity; owner++)
                {
                    PartyPracticeDrawingLayerSnapshot layer = fixture.Sessions[viewer].View.Layers[owner];
                    Assert.That(layer.OwnerSlot, Is.EqualTo(owner));
                    Assert.That(layer.Occupied, Is.True);
                    Assert.That(layer.Revision, Is.EqualTo(1));
                    Assert.That(layer.Drawing.strokes[0].xy[0], Is.EqualTo(0.1f + owner * 0.2f).Within(0.0001f));
                }
            }
        }

        [Test]
        public void EmptyRosterSlotsRemainVisibleButCannotPublish()
        {
            string[] identities = Identities("p0", "p1", null, string.Empty);
            using (var fixture = new Fixture(identities, 2))
            {
                fixture.Sessions[0].Tick(0f, 1, PartyPracticeDrawingPhase.Lobby);
                fixture.Sessions[1].Tick(0f, 1, PartyPracticeDrawingPhase.SelectingMode);
                fixture.Pump(0f);

                Assert.That(fixture.Sessions[0].View.Layers.Count, Is.EqualTo(PartyRoster.Capacity));
                Assert.That(fixture.Sessions[0].View.Layers[0].Revision, Is.EqualTo(1));
                Assert.That(fixture.Sessions[0].View.Layers[1].Revision, Is.EqualTo(1));
                Assert.That(fixture.Sessions[0].View.Layers[2].Occupied, Is.False);
                Assert.That(fixture.Sessions[0].View.Layers[2].Drawing, Is.Null);
                Assert.That(fixture.Sessions[0].View.Layers[3].Occupied, Is.False);
            }
        }

        [Test]
        public void GamePhaseDoesNotCaptureOrPublishLocalDrawing()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                fixture.Sessions[1].Tick(0f, 1, PartyPracticeDrawingPhase.Game);

                Assert.That(fixture.Captures[1], Is.Zero);
                Assert.That(fixture.Transports[1].SentToHost, Is.Empty);
                Assert.That(fixture.Sessions[1].View.Layers[1].Revision, Is.Zero);
            }
        }

        [Test]
        public void ChangedRevisionPublishesAtMostTenHertzAtOneHundredTwentyHertzTicks()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                for (int tick = 0; tick < 120; tick++)
                {
                    float now = tick / 120f;
                    fixture.Drawings[1] = Drawing(0.1f + tick * 0.001f);
                    fixture.Sessions[1].Tick(now, tick + 1, PartyPracticeDrawingPhase.Lobby);
                }

                Assert.That(fixture.Captures[1], Is.EqualTo(10));
                Assert.That(fixture.Sessions[1].View.Layers[1].Revision, Is.InRange(109, 120));
            }
        }

        [Test]
        public void HostRejectsSnapshotWhoseOwnerDoesNotMatchSenderIdentity()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                fixture.Network.Inject("p1", "p0", SnapshotPacket(
                    PartyPracticeDrawingProtocol.KindSnapshot, 1, 2, 1, Drawing(0.7f)));
                fixture.Sessions[0].Tick(0f, 0, PartyPracticeDrawingPhase.Lobby);

                Assert.That(fixture.Sessions[0].View.Layers[2].Revision, Is.Zero);
                Assert.That(fixture.Transports[0].RelayTargets(2, 1), Is.Empty);
            }
        }

        [Test]
        public void DuplicateSequenceAndStaleRevisionCannotRegressLayer()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                fixture.Network.Inject("p1", "p0", SnapshotPacket(
                    PartyPracticeDrawingProtocol.KindSnapshot, 10, 1, 2, Drawing(0.4f)));
                fixture.Sessions[0].Tick(0f, 0, PartyPracticeDrawingPhase.Lobby);
                fixture.Network.Inject("p1", "p0", SnapshotPacket(
                    PartyPracticeDrawingProtocol.KindSnapshot, 10, 1, 3, Drawing(0.8f)));
                fixture.Network.Inject("p1", "p0", SnapshotPacket(
                    PartyPracticeDrawingProtocol.KindSnapshot, 11, 1, 1, Drawing(0.9f)));
                fixture.Sessions[0].Tick(0.1f, 0, PartyPracticeDrawingPhase.Lobby);

                PartyPracticeDrawingLayerSnapshot layer = fixture.Sessions[0].View.Layers[1];
                Assert.That(layer.Revision, Is.EqualTo(2));
                Assert.That(layer.Drawing.strokes[0].xy[0], Is.EqualTo(0.4f));
            }
        }

        [Test]
        public void HostRejectsMoreThanTenAcceptedSnapshotsPerOwnerInRollingSecond()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                for (int revision = 1; revision <= 11; revision++)
                {
                    fixture.Network.Inject("p1", "p0", SnapshotPacket(
                        PartyPracticeDrawingProtocol.KindSnapshot,
                        revision, 1, revision, Drawing(0.1f + revision * 0.01f)));
                }

                fixture.Sessions[0].Tick(0f, 0, PartyPracticeDrawingPhase.Lobby);

                Assert.That(fixture.Sessions[0].View.Layers[1].Revision, Is.EqualTo(10));
                Assert.That(fixture.Transports[0].RelayTargets(1, 10), Is.Not.Empty);
                Assert.That(fixture.Transports[0].RelayTargets(1, 11), Is.Empty);

                fixture.Network.Inject("p1", "p0", SnapshotPacket(
                    PartyPracticeDrawingProtocol.KindSnapshot, 12, 1, 12, Drawing(0.8f)));
                fixture.Sessions[0].Tick(1f, 0, PartyPracticeDrawingPhase.Lobby);

                Assert.That(fixture.Sessions[0].View.Layers[1].Revision, Is.EqualTo(12));
                Assert.That(fixture.Transports[0].RelayTargets(1, 12), Is.Not.Empty);
            }
        }

        [Test]
        public void HostCapsIncompleteFirstChunkStartsAndCompletesAcceptedInFlightSnapshot()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                byte[] incomplete = new byte[PartyPracticeDrawingProtocol.ChunkBytes + 1];
                for (int revision = 1; revision < 10; revision++)
                    fixture.Network.Inject("p1", "p0", DrawingChunkPacket(revision, revision, incomplete, 0));

                Assert.That(PartyPracticeDrawingProtocol.TryDrawingBytes(
                    LargeDrawing(2000), 3, out byte[] acceptedDrawing), Is.True);
                Assert.That(acceptedDrawing.Length, Is.GreaterThan(PartyPracticeDrawingProtocol.ChunkBytes));
                fixture.Network.Inject("p1", "p0", DrawingChunkPacket(10, 10, acceptedDrawing, 0));

                byte[] maximum = new byte[PartyPracticeDrawingProtocol.MaxDrawingBytes];
                for (int revision = 11; revision <= 30; revision++)
                    fixture.Network.Inject("p1", "p0", DrawingChunkPacket(revision, revision, maximum, 0));

                int acceptedChunkCount = (acceptedDrawing.Length + PartyPracticeDrawingProtocol.ChunkBytes - 1)
                    / PartyPracticeDrawingProtocol.ChunkBytes;
                long continuationSequence = 31;
                for (int index = 1; index < acceptedChunkCount; index++)
                    fixture.Network.Inject("p1", "p0",
                        DrawingChunkPacket(continuationSequence++, 10, acceptedDrawing, index));
                fixture.Sessions[0].Tick(0f, 0, PartyPracticeDrawingPhase.Lobby);

                Assert.That(fixture.Sessions[0].View.Layers[1].Revision, Is.EqualTo(10));
                Assert.That(fixture.Transports[0].RelayTargets(1, 10), Is.Not.Empty);
                for (int revision = 11; revision <= 30; revision++)
                    Assert.That(fixture.Transports[0].RelayTargets(1, revision), Is.Empty);
            }
        }

        [Test]
        public void WrongEpochMalformedAndOversizedPacketsAreNotRelayed()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                fixture.Network.Inject("p1", "p0", SnapshotPacket(
                    PartyPracticeDrawingProtocol.KindSnapshot, 1, 1, 1, Drawing(0.3f), TransitionGeneration - 1));
                fixture.Network.Inject("p1", "p0", Encoding.UTF8.GetBytes("{broken"));
                fixture.Network.Inject("p1", "p0", new byte[PartyPracticeDrawingProtocol.MaxMessageBytes + 1]);
                fixture.Network.Inject("p1", "p0", PacketWithChunk(
                    PartyPracticeDrawingProtocol.KindSnapshot, 2, 1, 2,
                    new PartyPracticeDrawingChunk
                    {
                        transferId = "oversize",
                        index = 0,
                        count = 1,
                        total = PartyPracticeDrawingProtocol.MaxDrawingBytes + 1,
                        data = "AA=="
                    }));
                fixture.Sessions[0].Tick(0f, 0, PartyPracticeDrawingPhase.Lobby);

                Assert.That(fixture.Sessions[0].View.Layers[1].Revision, Is.Zero);
                Assert.That(fixture.Transports[0].RelayTargets(1, 1), Is.Empty);
                Assert.That(fixture.Transports[0].RelayTargets(1, 2), Is.Empty);
            }
        }

        [Test]
        public void InvalidStrokePointAndBrushBoundariesAreRejectedBeforeSend()
        {
            CanvasDrawingData tooManyPoints = Drawing(0.2f);
            tooManyPoints.strokes[0].xy = new float[(PartyPracticeDrawingProtocol.MaxPoints + 1) * 2];
            CanvasDrawingData invalidWidth = Drawing(0.2f);
            invalidWidth.strokes[0].widthNormalized = 1.01f;
            CanvasDrawingData invalidBrush = Drawing(0.2f);
            invalidBrush.strokes[0].brushId = 3;

            Assert.That(PartyPracticeDrawingProtocol.TryDrawingBytes(tooManyPoints, 3, out _), Is.False);
            Assert.That(PartyPracticeDrawingProtocol.TryDrawingBytes(invalidWidth, 3, out _), Is.False);
            Assert.That(PartyPracticeDrawingProtocol.TryDrawingBytes(invalidBrush, 3, out _), Is.False);
        }

        [Test]
        public void DisconnectRemovesOnlyDisconnectedOwnerLayerOnEveryRemainingPeer()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                    fixture.Sessions[slot].Tick(0f, 1, PartyPracticeDrawingPhase.Lobby);
                fixture.Pump(0f);

                fixture.Network.Disconnect("p2");
                fixture.Pump(0.1f);

                foreach (int viewer in new[] { 0, 1, 3 })
                {
                    Assert.That(fixture.Sessions[viewer].View.Layers[2].Occupied, Is.False);
                    Assert.That(fixture.Sessions[viewer].View.Layers[2].Revision, Is.Zero);
                    Assert.That(fixture.Sessions[viewer].View.Layers[2].Drawing, Is.Null);
                    Assert.That(fixture.Sessions[viewer].View.Layers[1].Revision, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void ReconfigureToNewRosterOrTransitionEpochClearsLayersAndRejectsOldPackets()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                fixture.Sessions[0].Tick(0f, 1, PartyPracticeDrawingPhase.Lobby);
                fixture.Sessions[0].Configure(SessionId, RosterGeneration + 1, TransitionGeneration + 1,
                    0, Identities("p0", "p1", null, null));
                fixture.Network.Inject("p1", "p0", SnapshotPacket(
                    PartyPracticeDrawingProtocol.KindSnapshot, 99, 1, 99, Drawing(0.8f)));
                fixture.Sessions[0].Tick(0.1f, 0, PartyPracticeDrawingPhase.Lobby);

                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                    Assert.That(fixture.Sessions[0].View.Layers[slot].Revision, Is.Zero);
                Assert.That(fixture.Sessions[0].View.RosterGeneration, Is.EqualTo(RosterGeneration + 1));
                Assert.That(fixture.Sessions[0].View.TransitionGeneration, Is.EqualTo(TransitionGeneration + 1));
            }
        }

        [Test]
        public void PublicTypesDeclareNoSecretAnswerCorrectPrivateOrReferenceFields()
        {
            Type[] types =
            {
                typeof(PartyPracticeDrawingPacket), typeof(PartyPracticeDrawingChunk),
                typeof(PartyPracticeDrawingView), typeof(PartyPracticeDrawingLayerSnapshot)
            };
            string[] forbidden = { "word", "answer", "correct", "secret", "private", "reference" };
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

        [Test]
        public void ProtocolRejectsEscapedAndNestedForbiddenEnvelopePropertyNames()
        {
            byte[] valid = SnapshotPacket(
                PartyPracticeDrawingProtocol.KindSnapshot, 1, 1, 1, Drawing(0.3f));
            string json = Encoding.UTF8.GetString(valid);
            byte[] withEscapedWord = Encoding.UTF8.GetBytes(
                json.Insert(json.Length - 1, ",\"w\\u006frd\":\"hidden\""));
            byte[] withNestedAnswer = Encoding.UTF8.GetBytes(
                json.Insert(json.Length - 1, ",\"metadata\":{\"nested\":{\"Answer\":\"hidden\"}}"));

            Assert.That(PartyPracticeDrawingProtocol.TryDecode(withEscapedWord, out _), Is.False);
            Assert.That(PartyPracticeDrawingProtocol.TryDecode(withNestedAnswer, out _), Is.False);
        }

        [Test]
        public void HostRejectsEscapedForbiddenPropertyInsideChunkJson()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                Assert.That(PartyPracticeDrawingProtocol.TryDrawingBytes(
                    Drawing(0.4f), 3, out byte[] drawing), Is.True);
                var chunk = new PartyPracticeDrawingChunk
                {
                    transferId = "nested-chunk",
                    index = 0,
                    count = 1,
                    total = drawing.Length,
                    data = Convert.ToBase64String(drawing)
                };
                string chunkJson = JsonUtility.ToJson(chunk);
                chunkJson = chunkJson.Insert(chunkJson.Length - 1,
                    ",\"metadata\":{\"s\\u0065cret\":\"hidden\"}");
                fixture.Network.Inject("p1", "p0", PacketWithPayload(
                    PartyPracticeDrawingProtocol.KindSnapshot, 1, 1, 1, chunkJson));

                fixture.Sessions[0].Tick(0f, 0, PartyPracticeDrawingPhase.Lobby);

                Assert.That(fixture.Sessions[0].View.Layers[1].Revision, Is.Zero);
                Assert.That(fixture.Transports[0].RelayTargets(1, 1), Is.Empty);
            }
        }

        [Test]
        public void HostRejectsNestedForbiddenPropertyInsideDrawingJson()
        {
            using (var fixture = new Fixture(Identities("p0", "p1", "p2", "p3")))
            {
                string drawingJson = JsonUtility.ToJson(Drawing(0.5f));
                drawingJson = drawingJson.Insert(drawingJson.Length - 1,
                    ",\"metadata\":{\"nested\":{\"r\\u0065ference\":\"hidden\"}}");
                byte[] drawing = Encoding.UTF8.GetBytes(drawingJson);
                fixture.Network.Inject("p1", "p0", DrawingChunkPacket(1, 1, drawing, 0));

                fixture.Sessions[0].Tick(0f, 0, PartyPracticeDrawingPhase.Lobby);

                Assert.That(fixture.Sessions[0].View.Layers[1].Revision, Is.Zero);
                Assert.That(fixture.Transports[0].RelayTargets(1, 1), Is.Empty);
            }
        }

        private static byte[] SnapshotPacket(
            string kind, long sequence, int ownerSlot, int revision, CanvasDrawingData drawing,
            int transitionGeneration = TransitionGeneration)
        {
            Assert.That(PartyPracticeDrawingProtocol.TryDrawingBytes(drawing, 3, out byte[] bytes), Is.True);
            return PacketWithChunk(kind, sequence, ownerSlot, revision, new PartyPracticeDrawingChunk
            {
                transferId = "manual-" + ownerSlot + "-" + revision,
                index = 0,
                count = 1,
                total = bytes.Length,
                data = Convert.ToBase64String(bytes)
            }, transitionGeneration);
        }

        private static byte[] PacketWithChunk(
            string kind, long sequence, int ownerSlot, int revision, PartyPracticeDrawingChunk chunk,
            int transitionGeneration = TransitionGeneration)
        {
            return PartyPracticeDrawingProtocol.Encode(new PartyPracticeDrawingPacket
            {
                sessionId = SessionId,
                rosterGeneration = RosterGeneration,
                transitionGeneration = transitionGeneration,
                sequence = sequence,
                kind = kind,
                ownerSlot = ownerSlot,
                revision = revision,
                payload = JsonUtility.ToJson(chunk)
            });
        }

        private static byte[] PacketWithPayload(
            string kind, long sequence, int ownerSlot, int revision, string payload)
        {
            return PartyPracticeDrawingProtocol.Encode(new PartyPracticeDrawingPacket
            {
                sessionId = SessionId,
                rosterGeneration = RosterGeneration,
                transitionGeneration = TransitionGeneration,
                sequence = sequence,
                kind = kind,
                ownerSlot = ownerSlot,
                revision = revision,
                payload = payload
            });
        }

        private static byte[] DrawingChunkPacket(
            long sequence, int revision, byte[] drawing, int index)
        {
            int count = (drawing.Length + PartyPracticeDrawingProtocol.ChunkBytes - 1)
                / PartyPracticeDrawingProtocol.ChunkBytes;
            int offset = index * PartyPracticeDrawingProtocol.ChunkBytes;
            int length = Math.Min(PartyPracticeDrawingProtocol.ChunkBytes, drawing.Length - offset);
            return PacketWithChunk(PartyPracticeDrawingProtocol.KindSnapshot, sequence, 1, revision,
                new PartyPracticeDrawingChunk
                {
                    transferId = "manual-1-" + revision,
                    index = index,
                    count = count,
                    total = drawing.Length,
                    data = Convert.ToBase64String(drawing, offset, length)
                });
        }

        private static string[] Identities(params string[] identities) => identities;

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

        private static CanvasDrawingData LargeDrawing(int points)
        {
            var xy = new float[points * 2];
            for (int index = 0; index < xy.Length; index += 2)
            {
                xy[index] = 0.2f;
                xy[index + 1] = 0.3f;
            }
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

        private sealed class Fixture : IDisposable
        {
            internal readonly FakeNetwork Network = new FakeNetwork();
            internal readonly FakeTransport[] Transports = new FakeTransport[PartyRoster.Capacity];
            internal readonly PartyPracticeDrawingSession[] Sessions = new PartyPracticeDrawingSession[PartyRoster.Capacity];
            internal readonly CanvasDrawingData[] Drawings = new CanvasDrawingData[PartyRoster.Capacity];
            internal readonly int[] Captures = new int[PartyRoster.Capacity];
            private readonly int occupiedCount;

            internal Fixture(string[] identities, int occupiedCount = PartyRoster.Capacity)
            {
                this.occupiedCount = occupiedCount;
                for (int slot = 0; slot < occupiedCount; slot++)
                {
                    int sourceSlot = slot;
                    Drawings[slot] = Drawing(0.1f + slot * 0.1f);
                    Transports[slot] = Network.Add(identities[slot], slot == 0);
                    Sessions[slot] = new PartyPracticeDrawingSession(Transports[slot], () =>
                    {
                        Captures[sourceSlot]++;
                        return Drawings[sourceSlot];
                    }, 3);
                    Sessions[slot].Configure(SessionId, RosterGeneration, TransitionGeneration, slot, identities);
                }
            }

            internal void Pump(float now)
            {
                for (int cycle = 0; cycle < 8; cycle++)
                for (int slot = 0; slot < occupiedCount; slot++)
                    Sessions[slot].Tick(now, Sessions[slot].View.Layers[slot].Revision,
                        PartyPracticeDrawingPhase.Lobby);
            }

            public void Dispose()
            {
                for (int slot = 0; slot < occupiedCount; slot++) Sessions[slot]?.Dispose();
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

            public void Shutdown() { }
            internal void Enqueue(string sender, byte[] bytes) => pending.Enqueue(new Message(sender, bytes));
            internal void RaiseDisconnected(string identity) => OnPeerDisconnected?.Invoke(identity);

            internal string[] RelayTargets(int ownerSlot, int revision)
            {
                var targets = new List<string>();
                foreach (SentMessage message in Sent)
                {
                    if (PartyPracticeDrawingProtocol.TryDecode(message.Bytes, out PartyPracticeDrawingPacket packet)
                        && packet.kind == PartyPracticeDrawingProtocol.KindRelay
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
