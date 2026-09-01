using System;
using System.Collections.Generic;
using CameraCoop.Netplay;

namespace CameraCoop.Party
{
    public enum PartyPracticeDrawingPhase
    {
        Lobby = 0,
        SelectingMode = 1,
        Game = 2
    }

    public sealed class PartyPracticeDrawingSession : IDisposable
    {
        private const float MaximumSendHz = 10f;
        private const int MaximumAcceptedSnapshotsPerSecond = 10;

        private readonly INetTransport transport;
        private readonly Func<CanvasDrawingData> drawingSource;
        private readonly int brushCount;
        private readonly float sendInterval;
        private readonly string[] identities = new string[PartyRoster.Capacity];
        private readonly bool[] connected = new bool[PartyRoster.Capacity];
        private readonly long[] lastIncomingSequence = new long[PartyRoster.Capacity];
        private readonly IncomingTransfer[] incoming = new IncomingTransfer[PartyRoster.Capacity];
        private readonly Queue<float>[] startedAtByOwner = new Queue<float>[PartyRoster.Capacity];
        private readonly Dictionary<string, int> identityToSlot =
            new Dictionary<string, int>(PartyRoster.Capacity, StringComparer.Ordinal);

        private string sessionId;
        private string hostIdentity;
        private int rosterGeneration;
        private int transitionGeneration;
        private long outgoingSequence;
        private int lastSentLocalRevision;
        private float lastLocalSendAt = float.NegativeInfinity;
        private float currentTime;
        private bool configured;
        private bool disposed;

        public PartyPracticeDrawingSession(
            INetTransport transport,
            Func<CanvasDrawingData> drawingSource,
            int brushCount,
            float sendHz = MaximumSendHz)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.drawingSource = drawingSource ?? throw new ArgumentNullException(nameof(drawingSource));
            if (brushCount <= 0) throw new ArgumentOutOfRangeException(nameof(brushCount));
            if (!IsFinite(sendHz) || sendHz <= 0f || sendHz > MaximumSendHz)
                throw new ArgumentOutOfRangeException(nameof(sendHz));
            this.brushCount = brushCount;
            sendInterval = 1f / sendHz;
            for (int slot = 0; slot < startedAtByOwner.Length; slot++)
                startedAtByOwner[slot] = new Queue<float>(MaximumAcceptedSnapshotsPerSecond);
            View = new PartyPracticeDrawingView();
            transport.OnMessage += HandleMessage;
            transport.OnPeerDisconnected += HandlePeerDisconnected;
        }

        public event Action<PartyPracticeDrawingView> ViewChanged;
        public PartyPracticeDrawingView View { get; }

        public void Configure(
            string sessionId,
            int rosterGeneration,
            int transitionGeneration,
            int localSlot,
            IReadOnlyList<string> slotIdentities)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(sessionId) || sessionId.Length > 64)
                throw new ArgumentException("A bounded session identity is required.", nameof(sessionId));
            if (rosterGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(rosterGeneration));
            if (transitionGeneration <= 0) throw new ArgumentOutOfRangeException(nameof(transitionGeneration));
            PartyRoster.ValidateSlot(localSlot);
            if (slotIdentities == null || slotIdentities.Count != PartyRoster.Capacity)
                throw new ArgumentException("Exactly four owner slots are required.", nameof(slotIdentities));

            identityToSlot.Clear();
            Array.Clear(identities, 0, identities.Length);
            Array.Clear(connected, 0, connected.Length);
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                string identity = slotIdentities[slot];
                if (string.IsNullOrEmpty(identity)) continue;
                if (identity.Length > 64 || identityToSlot.ContainsKey(identity))
                    throw new ArgumentException("Occupied slots require unique bounded identities.", nameof(slotIdentities));
                identities[slot] = identity;
                connected[slot] = true;
                identityToSlot.Add(identity, slot);
            }

            if (!connected[0] || !connected[localSlot]
                || !string.Equals(identities[localSlot], transport.LocalPlayerId, StringComparison.Ordinal)
                || transport.IsHost != (localSlot == 0))
                throw new ArgumentException("Transport role and local slot identity do not match.", nameof(slotIdentities));

            this.sessionId = sessionId;
            this.rosterGeneration = rosterGeneration;
            this.transitionGeneration = transitionGeneration;
            hostIdentity = identities[0];
            outgoingSequence = 0;
            lastSentLocalRevision = 0;
            lastLocalSendAt = float.NegativeInfinity;
            currentTime = 0f;
            Array.Clear(lastIncomingSequence, 0, lastIncomingSequence.Length);
            Array.Clear(incoming, 0, incoming.Length);
            ClearAcceptedTimes();
            configured = true;
            View.Configure(sessionId, rosterGeneration, transitionGeneration, localSlot, identities);
            ViewChanged?.Invoke(View);
        }

        public void Tick(float nowSeconds, int localRevision, PartyPracticeDrawingPhase phase)
        {
            ThrowIfDisposed();
            if (!IsFinite(nowSeconds) || nowSeconds < currentTime)
                throw new ArgumentOutOfRangeException(nameof(nowSeconds));
            if (localRevision < 0) throw new ArgumentOutOfRangeException(nameof(localRevision));
            currentTime = nowSeconds;
            transport.Tick();
            if (!configured || phase != PartyPracticeDrawingPhase.Lobby
                && phase != PartyPracticeDrawingPhase.SelectingMode
                || localRevision <= lastSentLocalRevision
                || nowSeconds - lastLocalSendAt + float.Epsilon < sendInterval)
                return;

            CanvasDrawingData source = drawingSource();
            if (!PartyPracticeDrawingProtocol.TryDrawingBytes(source, brushCount, out byte[] bytes)) return;
            int ownerSlot = View.LocalSlot;
            if (!View.Layers[ownerSlot].Occupied || localRevision <= View.Layers[ownerSlot].Revision
                || !PartyPracticeDrawingProtocol.TryReadDrawing(bytes, brushCount, out CanvasDrawingData drawing))
                return;

            if (transport.IsHost) SendSnapshotToClients(ownerSlot, localRevision, bytes, ownerSlot);
            else SendSnapshotToHost(ownerSlot, localRevision, bytes);
            View.Apply(ownerSlot, localRevision, drawing);
            lastSentLocalRevision = localRevision;
            lastLocalSendAt = nowSeconds;
            ViewChanged?.Invoke(View);
        }

        public void Reset()
        {
            ThrowIfDisposed();
            configured = false;
            identityToSlot.Clear();
            Array.Clear(identities, 0, identities.Length);
            Array.Clear(connected, 0, connected.Length);
            Array.Clear(lastIncomingSequence, 0, lastIncomingSequence.Length);
            Array.Clear(incoming, 0, incoming.Length);
            ClearAcceptedTimes();
            sessionId = null;
            hostIdentity = null;
            rosterGeneration = 0;
            transitionGeneration = 0;
            lastSentLocalRevision = 0;
            lastLocalSendAt = float.NegativeInfinity;
            View.Reset();
            ViewChanged?.Invoke(View);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            transport.OnMessage -= HandleMessage;
            transport.OnPeerDisconnected -= HandlePeerDisconnected;
            configured = false;
            View.Reset();
        }

        private void HandleMessage(string peerIdentity, byte[] bytes)
        {
            if (!configured || string.IsNullOrEmpty(peerIdentity)
                || !PartyPracticeDrawingProtocol.TryDecode(bytes, out PartyPracticeDrawingPacket packet)
                || packet.sessionId != sessionId || packet.rosterGeneration != rosterGeneration
                || packet.transitionGeneration != transitionGeneration)
                return;

            if (transport.IsHost) HandleHostPacket(peerIdentity, packet);
            else HandleClientPacket(peerIdentity, packet);
        }

        private void HandleHostPacket(string peerIdentity, PartyPracticeDrawingPacket packet)
        {
            if (packet.kind != PartyPracticeDrawingProtocol.KindSnapshot
                || !identityToSlot.TryGetValue(peerIdentity, out int senderSlot)
                || senderSlot == View.LocalSlot || !connected[senderSlot]
                || packet.ownerSlot != senderSlot
                || !TryAcceptChunk(senderSlot, packet, true, out byte[] complete)
                || complete == null)
                return;

            if (!PartyPracticeDrawingProtocol.TryReadDrawing(complete, brushCount, out CanvasDrawingData drawing))
            {
                incoming[senderSlot] = null;
                return;
            }

            incoming[senderSlot] = null;
            if (!View.Apply(senderSlot, packet.revision, drawing)) return;
            ViewChanged?.Invoke(View);
            SendSnapshotToClients(senderSlot, packet.revision, complete, senderSlot);
        }

        private void HandleClientPacket(string peerIdentity, PartyPracticeDrawingPacket packet)
        {
            if (!string.Equals(peerIdentity, hostIdentity, StringComparison.Ordinal)
                || packet.sequence <= lastIncomingSequence[0])
                return;

            if (packet.kind == PartyPracticeDrawingProtocol.KindRemove)
            {
                lastIncomingSequence[0] = packet.sequence;
                incoming[packet.ownerSlot] = null;
                connected[packet.ownerSlot] = false;
                if (View.Remove(packet.ownerSlot)) ViewChanged?.Invoke(View);
                return;
            }
            if (packet.kind != PartyPracticeDrawingProtocol.KindRelay
                || packet.ownerSlot == View.LocalSlot || !connected[packet.ownerSlot]
                || !TryAcceptChunk(0, packet, false, out byte[] complete) || complete == null)
                return;

            int ownerSlot = packet.ownerSlot;
            if (!PartyPracticeDrawingProtocol.TryReadDrawing(complete, brushCount, out CanvasDrawingData drawing))
            {
                incoming[ownerSlot] = null;
                return;
            }

            incoming[ownerSlot] = null;
            if (View.Apply(ownerSlot, packet.revision, drawing)) ViewChanged?.Invoke(View);
        }

        private bool TryAcceptChunk(
            int sequenceSlot,
            PartyPracticeDrawingPacket packet,
            bool enforceStartQuota,
            out byte[] complete)
        {
            complete = null;
            int ownerSlot = packet.ownerSlot;
            if (packet.sequence <= lastIncomingSequence[sequenceSlot]
                || packet.revision <= View.Layers[ownerSlot].Revision
                || !PartyPracticeDrawingProtocol.TryReadChunk(packet.payload, out PartyPracticeDrawingChunk chunk))
                return false;

            IncomingTransfer state = incoming[ownerSlot];
            if (state == null || state.Revision != packet.revision)
            {
                if (state != null && state.Revision > packet.revision || chunk.index != 0) return false;
                if (enforceStartQuota && !TryReserveInboundStart(ownerSlot)) return false;
                state = new IncomingTransfer(packet.revision);
            }
            if (!state.Transfer.TryAdd(chunk, out complete))
            {
                incoming[ownerSlot] = null;
                return false;
            }
            incoming[ownerSlot] = state;
            lastIncomingSequence[sequenceSlot] = packet.sequence;
            return true;
        }

        private void HandlePeerDisconnected(string peerIdentity)
        {
            if (!configured || !identityToSlot.TryGetValue(peerIdentity, out int slot)
                || !connected[slot])
                return;
            connected[slot] = false;
            incoming[slot] = null;
            startedAtByOwner[slot].Clear();
            if (View.Remove(slot)) ViewChanged?.Invoke(View);
            if (!transport.IsHost) return;

            byte[] remove = PartyPracticeDrawingProtocol.Encode(CreatePacket(
                PartyPracticeDrawingProtocol.KindRemove, slot, 0, "{}"));
            for (int recipient = 0; recipient < PartyRoster.Capacity; recipient++)
            {
                if (recipient == View.LocalSlot || recipient == slot || !connected[recipient]) continue;
                transport.SendTo(identities[recipient], remove, true);
            }
        }

        private void SendSnapshotToHost(int ownerSlot, int revision, byte[] bytes)
        {
            int count = ChunkCount(bytes.Length);
            string transferId = TransferId(ownerSlot, revision);
            for (int index = 0; index < count; index++)
                transport.SendToHost(EncodeChunk(PartyPracticeDrawingProtocol.KindSnapshot,
                    transferId, ownerSlot, revision, bytes, index, count), true);
        }

        private void SendSnapshotToClients(int ownerSlot, int revision, byte[] bytes, int sourceSlot)
        {
            int count = ChunkCount(bytes.Length);
            string transferId = TransferId(ownerSlot, revision);
            for (int index = 0; index < count; index++)
            {
                byte[] packet = EncodeChunk(PartyPracticeDrawingProtocol.KindRelay,
                    transferId, ownerSlot, revision, bytes, index, count);
                for (int recipient = 0; recipient < PartyRoster.Capacity; recipient++)
                {
                    if (recipient == View.LocalSlot || recipient == sourceSlot || !connected[recipient]) continue;
                    transport.SendTo(identities[recipient], packet, true);
                }
            }
        }

        private byte[] EncodeChunk(
            string kind, string transferId, int ownerSlot, int revision,
            byte[] bytes, int index, int count)
        {
            int offset = index * PartyPracticeDrawingProtocol.ChunkBytes;
            int length = Math.Min(PartyPracticeDrawingProtocol.ChunkBytes, bytes.Length - offset);
            var chunk = new PartyPracticeDrawingChunk
            {
                transferId = transferId,
                index = index,
                count = count,
                total = bytes.Length,
                data = Convert.ToBase64String(bytes, offset, length)
            };
            return PartyPracticeDrawingProtocol.Encode(CreatePacket(
                kind, ownerSlot, revision, UnityEngine.JsonUtility.ToJson(chunk)));
        }

        private PartyPracticeDrawingPacket CreatePacket(
            string kind, int ownerSlot, int revision, string payload)
        {
            outgoingSequence++;
            return new PartyPracticeDrawingPacket
            {
                sessionId = sessionId,
                rosterGeneration = rosterGeneration,
                transitionGeneration = transitionGeneration,
                sequence = outgoingSequence,
                kind = kind,
                ownerSlot = ownerSlot,
                revision = revision,
                payload = payload
            };
        }

        private string TransferId(int ownerSlot, int revision)
        {
            return sessionId + ":" + rosterGeneration + ":" + transitionGeneration
                + ":" + ownerSlot + ":" + revision;
        }

        private static int ChunkCount(int total)
        {
            return (total + PartyPracticeDrawingProtocol.ChunkBytes - 1)
                / PartyPracticeDrawingProtocol.ChunkBytes;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PartyPracticeDrawingSession));
        }

        private bool TryReserveInboundStart(int ownerSlot)
        {
            Queue<float> startedAt = startedAtByOwner[ownerSlot];
            while (startedAt.Count > 0 && currentTime - startedAt.Peek() >= 1f)
                startedAt.Dequeue();
            if (startedAt.Count >= MaximumAcceptedSnapshotsPerSecond) return false;
            startedAt.Enqueue(currentTime);
            return true;
        }

        private void ClearAcceptedTimes()
        {
            for (int slot = 0; slot < startedAtByOwner.Length; slot++)
                startedAtByOwner[slot].Clear();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class IncomingTransfer
        {
            internal IncomingTransfer(int revision)
            {
                Revision = revision;
                Transfer = new PartyPracticeDrawingChunkTransfer();
            }

            internal int Revision { get; }
            internal PartyPracticeDrawingChunkTransfer Transfer { get; }
        }
    }
}
