using System;
using System.Collections.Generic;
using CameraCoop.Netplay;

namespace CameraCoop.Party
{
    public sealed class CoopMuralSession : IDisposable
    {
        private const float MaximumSendHz = 10f;

        private readonly INetTransport transport;
        private readonly Func<CanvasDrawingData> drawingSource;
        private readonly int brushCount;
        private readonly float sendInterval;
        private readonly string[] identities = new string[PartyRoster.Capacity];
        private readonly bool[] connected = new bool[PartyRoster.Capacity];
        private readonly long[] lastIncomingSequence = new long[PartyRoster.Capacity];
        private readonly IncomingTransfer[] incoming = new IncomingTransfer[PartyRoster.Capacity];
        private readonly Dictionary<string, int> identityToSlot =
            new Dictionary<string, int>(PartyRoster.Capacity, StringComparer.Ordinal);

        private string sessionId;
        private string hostIdentity;
        private int rosterGeneration;
        private long outgoingSequence;
        private int lastSentLocalRevision;
        private float lastLocalSendAt = float.NegativeInfinity;
        private float currentTime;
        private bool configured;
        private bool turnCompletionPending;
        private bool disposed;

        public CoopMuralSession(
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
            View = new CoopMuralView();
            transport.OnMessage += HandleMessage;
            transport.OnPeerDisconnected += HandlePeerDisconnected;
        }

        public event Action<CoopMuralView> ViewChanged;

        public CoopMuralView View { get; }

        public void Configure(PartyStartSnapshot start)
        {
            ThrowIfDisposed();
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (start.Mode != PartyMode.CoopMural)
                throw new ArgumentException("Coop mural requires a CoopMural party start snapshot.", nameof(start));
            PartyRosterSnapshot roster = start.Roster;
            if (roster == null || string.IsNullOrEmpty(roster.SessionId) || roster.SessionId.Length > 64
                || roster.Generation <= 0 || string.IsNullOrEmpty(roster.HostIdentity)
                || roster.Slots == null || roster.Slots.Count != PartyRoster.Capacity)
                throw new ArgumentException("A complete fixed party roster is required.", nameof(start));

            identityToSlot.Clear();
            Array.Clear(identities, 0, identities.Length);
            Array.Clear(connected, 0, connected.Length);
            Array.Clear(lastIncomingSequence, 0, lastIncomingSequence.Length);
            Array.Clear(incoming, 0, incoming.Length);
            int localSlot = -1;
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                PartyRosterSlotSnapshot entry = roster.Slots[slot];
                if (entry == null || entry.Slot != slot || string.IsNullOrEmpty(entry.Identity)
                    || !entry.Connected || identityToSlot.ContainsKey(entry.Identity))
                    throw new ArgumentException("Roster slots must be connected and have unique transport identities.", nameof(start));
                identities[slot] = entry.Identity;
                connected[slot] = true;
                identityToSlot.Add(entry.Identity, slot);
                if (string.Equals(entry.Identity, transport.LocalPlayerId, StringComparison.Ordinal)) localSlot = slot;
            }

            if (localSlot < 0 || !identityToSlot.TryGetValue(roster.HostIdentity, out int hostSlot) || hostSlot != 0
                || transport.IsHost != (localSlot == 0))
                throw new ArgumentException("Transport role and roster identity do not match.", nameof(start));

            sessionId = roster.SessionId;
            hostIdentity = roster.HostIdentity;
            rosterGeneration = roster.Generation;
            outgoingSequence = 0;
            lastSentLocalRevision = 0;
            lastLocalSendAt = float.NegativeInfinity;
            currentTime = 0f;
            turnCompletionPending = false;
            configured = true;
            View.Configure(sessionId, rosterGeneration, localSlot);
            ViewChanged?.Invoke(View);
        }

        public void Tick(float nowSeconds, int localRevision)
        {
            ThrowIfDisposed();
            if (!IsFinite(nowSeconds) || nowSeconds < currentTime) throw new ArgumentOutOfRangeException(nameof(nowSeconds));
            if (localRevision < 0) throw new ArgumentOutOfRangeException(nameof(localRevision));
            currentTime = nowSeconds;
            transport.Tick();
            if (!configured || View.Aborted || !View.CanLocalWrite || turnCompletionPending
                || localRevision <= lastSentLocalRevision
                || nowSeconds - lastLocalSendAt + float.Epsilon < sendInterval)
                return;

            CanvasDrawingData source = drawingSource();
            if (!CoopMuralProtocol.TryDrawingBytes(source, brushCount, out byte[] bytes)) return;

            int ownerSlot = View.LocalSlot;
            if (localRevision <= View.Layers[ownerSlot].Revision) return;
            if (!CoopMuralProtocol.TryReadDrawing(bytes, brushCount, out CanvasDrawingData drawing)) return;
            if (transport.IsHost) SendSnapshotToClients(ownerSlot, localRevision, bytes, ownerSlot);
            else SendSnapshotToHost(ownerSlot, localRevision, bytes);
            View.Apply(ownerSlot, localRevision, drawing);
            lastSentLocalRevision = localRevision;
            lastLocalSendAt = nowSeconds;
            ViewChanged?.Invoke(View);
        }

        public bool CompleteLocalTurn(int localRevision)
        {
            ThrowIfDisposed();
            if (!configured || View.Aborted || !View.CanLocalWrite || turnCompletionPending
                || localRevision <= 0 || localRevision != View.Layers[View.LocalSlot].Revision)
                return false;

            int completedSlot = View.LocalSlot;
            if (transport.IsHost)
            {
                if (!View.CompleteTurn(completedSlot, localRevision)) return false;
                SendTurnAdvancedToClients(completedSlot, localRevision);
                ViewChanged?.Invoke(View);
                return true;
            }

            transport.SendToHost(CoopMuralProtocol.Encode(CreatePacket(
                CoopMuralProtocol.KindTurnComplete, completedSlot, localRevision, "{}")), true);
            turnCompletionPending = true;
            return true;
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
            sessionId = null;
            hostIdentity = null;
            rosterGeneration = 0;
            turnCompletionPending = false;
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
            if (!configured || View.Aborted || string.IsNullOrEmpty(peerIdentity)
                || !CoopMuralProtocol.TryDecode(bytes, out CoopMuralPacket packet)
                || packet.sessionId != sessionId || packet.rosterGeneration != rosterGeneration)
                return;

            if (transport.IsHost) HandleHostPacket(peerIdentity, packet);
            else HandleClientPacket(peerIdentity, packet);
        }

        private void HandleHostPacket(string peerIdentity, CoopMuralPacket packet)
        {
            if (!identityToSlot.TryGetValue(peerIdentity, out int senderSlot)
                || senderSlot == View.LocalSlot || !connected[senderSlot]
                || packet.ownerSlot != senderSlot)
                return;

            if (packet.kind == CoopMuralProtocol.KindTurnComplete)
            {
                if (senderSlot != View.ActiveSlot || packet.sequence <= lastIncomingSequence[senderSlot]
                    || !View.CompleteTurn(senderSlot, packet.revision)) return;
                lastIncomingSequence[senderSlot] = packet.sequence;
                incoming[senderSlot] = null;
                SendTurnAdvancedToClients(senderSlot, packet.revision);
                ViewChanged?.Invoke(View);
                return;
            }
            if (packet.kind != CoopMuralProtocol.KindSubmit || senderSlot != View.ActiveSlot) return;

            if (!TryAcceptChunk(senderSlot, packet, out byte[] complete)) return;
            if (complete == null) return;
            if (!CoopMuralProtocol.TryReadDrawing(complete, brushCount, out CanvasDrawingData drawing))
            {
                incoming[senderSlot] = null;
                return;
            }

            incoming[senderSlot] = null;
            View.Apply(senderSlot, packet.revision, drawing);
            ViewChanged?.Invoke(View);
            SendSnapshotToClients(senderSlot, packet.revision, complete, senderSlot);
        }

        private void HandleClientPacket(string peerIdentity, CoopMuralPacket packet)
        {
            if (!string.Equals(peerIdentity, hostIdentity, StringComparison.Ordinal)) return;
            if (packet.kind == CoopMuralProtocol.KindAbort)
            {
                if (packet.sequence <= lastIncomingSequence[0]) return;
                lastIncomingSequence[0] = packet.sequence;
                AbortLocal();
                return;
            }
            if (packet.kind == CoopMuralProtocol.KindTurnAdvanced)
            {
                if (packet.sequence <= lastIncomingSequence[0]
                    || packet.ownerSlot != View.ActiveSlot
                    || !View.CompleteTurn(packet.ownerSlot, packet.revision)) return;
                lastIncomingSequence[0] = packet.sequence;
                incoming[packet.ownerSlot] = null;
                turnCompletionPending = false;
                ViewChanged?.Invoke(View);
                return;
            }
            if (packet.kind != CoopMuralProtocol.KindRelay || packet.ownerSlot == View.LocalSlot
                || packet.ownerSlot != View.ActiveSlot
                || !connected[packet.ownerSlot]) return;
            if (!TryAcceptChunk(0, packet, out byte[] complete)) return;
            if (complete == null) return;
            int ownerSlot = packet.ownerSlot;
            if (!CoopMuralProtocol.TryReadDrawing(complete, brushCount, out CanvasDrawingData drawing))
            {
                incoming[ownerSlot] = null;
                return;
            }

            incoming[ownerSlot] = null;
            View.Apply(ownerSlot, packet.revision, drawing);
            ViewChanged?.Invoke(View);
        }

        private bool TryAcceptChunk(int sequenceSlot, CoopMuralPacket packet, out byte[] complete)
        {
            complete = null;
            int ownerSlot = packet.ownerSlot;
            if (packet.sequence <= lastIncomingSequence[sequenceSlot]
                || packet.revision <= View.Layers[ownerSlot].Revision
                || !CoopMuralProtocol.TryReadChunk(packet.payload, out CoopMuralChunk chunk))
                return false;

            IncomingTransfer state = incoming[ownerSlot];
            if (state == null || state.Revision != packet.revision)
            {
                if (state != null && state.Revision > packet.revision || chunk.index != 0) return false;
                state = new IncomingTransfer(packet.revision);
                incoming[ownerSlot] = state;
            }
            if (!state.Transfer.TryAdd(chunk, out complete))
            {
                incoming[ownerSlot] = null;
                return false;
            }
            lastIncomingSequence[sequenceSlot] = packet.sequence;
            return true;
        }

        private void HandlePeerDisconnected(string peerIdentity)
        {
            if (!configured || View.Aborted || !identityToSlot.TryGetValue(peerIdentity, out int slot)) return;
            connected[slot] = false;
            if (transport.IsHost)
            {
                byte[] abort = CoopMuralProtocol.Encode(CreatePacket(
                    CoopMuralProtocol.KindAbort, -1, 0, "{}"));
                for (int recipient = 0; recipient < PartyRoster.Capacity; recipient++)
                {
                    if (recipient == View.LocalSlot || recipient == slot || !connected[recipient]) continue;
                    transport.SendTo(identities[recipient], abort, true);
                }
            }
            AbortLocal();
        }

        private void AbortLocal()
        {
            if (View.Aborted) return;
            Array.Clear(incoming, 0, incoming.Length);
            View.Abort();
            ViewChanged?.Invoke(View);
        }

        private void SendSnapshotToHost(int ownerSlot, int revision, byte[] bytes)
        {
            int count = ChunkCount(bytes.Length);
            string transferId = TransferId(ownerSlot, revision);
            for (int index = 0; index < count; index++)
            {
                byte[] packet = EncodeChunk(CoopMuralProtocol.KindSubmit, transferId, ownerSlot, revision,
                    bytes, index, count);
                transport.SendToHost(packet, true);
            }
        }

        private void SendSnapshotToClients(int ownerSlot, int revision, byte[] bytes, int sourceSlot)
        {
            int count = ChunkCount(bytes.Length);
            string transferId = TransferId(ownerSlot, revision);
            for (int index = 0; index < count; index++)
            {
                byte[] packet = EncodeChunk(CoopMuralProtocol.KindRelay, transferId, ownerSlot, revision,
                    bytes, index, count);
                for (int recipient = 0; recipient < PartyRoster.Capacity; recipient++)
                {
                    if (recipient == View.LocalSlot || recipient == sourceSlot || !connected[recipient]) continue;
                    transport.SendTo(identities[recipient], packet, true);
                }
            }
        }

        private void SendTurnAdvancedToClients(int completedSlot, int revision)
        {
            byte[] packet = CoopMuralProtocol.Encode(CreatePacket(
                CoopMuralProtocol.KindTurnAdvanced, completedSlot, revision, "{}"));
            for (int recipient = 0; recipient < PartyRoster.Capacity; recipient++)
            {
                if (recipient == View.LocalSlot || !connected[recipient]) continue;
                transport.SendTo(identities[recipient], packet, true);
            }
        }

        private byte[] EncodeChunk(
            string kind,
            string transferId,
            int ownerSlot,
            int revision,
            byte[] bytes,
            int index,
            int count)
        {
            int offset = index * CoopMuralProtocol.ChunkBytes;
            int length = Math.Min(CoopMuralProtocol.ChunkBytes, bytes.Length - offset);
            string data = Convert.ToBase64String(bytes, offset, length);
            var chunk = new CoopMuralChunk
            {
                transferId = transferId,
                index = index,
                count = count,
                total = bytes.Length,
                data = data
            };
            return CoopMuralProtocol.Encode(CreatePacket(kind, ownerSlot, revision,
                UnityEngine.JsonUtility.ToJson(chunk)));
        }

        private CoopMuralPacket CreatePacket(string kind, int ownerSlot, int revision, string payload)
        {
            outgoingSequence++;
            return new CoopMuralPacket
            {
                sessionId = sessionId,
                rosterGeneration = rosterGeneration,
                sequence = outgoingSequence,
                kind = kind,
                ownerSlot = ownerSlot,
                revision = revision,
                payload = payload
            };
        }

        private string TransferId(int ownerSlot, int revision)
        {
            return sessionId + ":" + rosterGeneration + ":" + ownerSlot + ":" + revision;
        }

        private static int ChunkCount(int total)
        {
            return (total + CoopMuralProtocol.ChunkBytes - 1) / CoopMuralProtocol.ChunkBytes;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CoopMuralSession));
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
                Transfer = new CoopMuralChunkTransfer();
            }

            internal int Revision { get; }
            internal CoopMuralChunkTransfer Transfer { get; }
        }
    }
}
