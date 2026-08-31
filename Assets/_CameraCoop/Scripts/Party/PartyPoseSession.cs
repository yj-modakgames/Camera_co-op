using System;
using System.Collections.Generic;
using CameraCoop.Netplay;
using UnityEngine;

namespace CameraCoop.Party
{
    public sealed class PartyPoseSession : IDisposable
    {
        private const float MinimumMovementAllowance = 0.05f;

        private readonly INetTransport transport;
        private readonly float sendInterval;
        private readonly float positionBound;
        private readonly float maxSpeed;
        private readonly string[] identities = new string[PartyRoster.Capacity];
        private readonly bool[] connected = new bool[PartyRoster.Capacity];
        private readonly bool[] hasPose = new bool[PartyRoster.Capacity];
        private readonly Vector3[] lastPosition = new Vector3[PartyRoster.Capacity];
        private readonly float[] lastAcceptedAt = new float[PartyRoster.Capacity];
        private readonly long[] lastSequence = new long[PartyRoster.Capacity];
        private readonly Dictionary<string, int> identityToSlot = new Dictionary<string, int>(PartyRoster.Capacity, StringComparer.Ordinal);

        private string sessionId;
        private string hostIdentity;
        private int rosterGeneration;
        private long localSequence;
        private float currentTime;
        private float lastLocalSendAt = float.NegativeInfinity;
        private bool configured;
        private bool disposed;

        public PartyPoseSession(INetTransport transport, float sendHz = 15f, float positionBound = 100f, float maxSpeed = 20f)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (!IsFinite(sendHz) || sendHz <= 0f || sendHz > 15f) throw new ArgumentOutOfRangeException(nameof(sendHz));
            if (!IsFinite(positionBound) || positionBound <= 0f) throw new ArgumentOutOfRangeException(nameof(positionBound));
            if (!IsFinite(maxSpeed) || maxSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            sendInterval = 1f / sendHz;
            this.positionBound = positionBound;
            this.maxSpeed = maxSpeed;
            transport.OnMessage += HandleMessage;
            transport.OnPeerDisconnected += HandlePeerDisconnected;
        }

        public event Action<PartyPoseSample> RemotePoseUpdated;
        public event Action<int> RemotePoseRemoved;

        public bool IsConfigured => configured;
        public int LocalSlot { get; private set; } = -1;

        public void Configure(PartyRosterSnapshot roster)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PartyPoseSession));
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            if (string.IsNullOrEmpty(roster.SessionId) || roster.SessionId.Length > 64 || roster.Generation <= 0
                || string.IsNullOrEmpty(roster.HostIdentity) || roster.Slots == null
                || roster.Slots.Count != PartyRoster.Capacity)
                throw new ArgumentException("A complete fixed party roster is required.", nameof(roster));

            ClearRemotePoses();
            identityToSlot.Clear();
            Array.Clear(identities, 0, identities.Length);
            Array.Clear(connected, 0, connected.Length);
            Array.Clear(lastSequence, 0, lastSequence.Length);
            LocalSlot = -1;

            for (int index = 0; index < PartyRoster.Capacity; index++)
            {
                PartyRosterSlotSnapshot entry = roster.Slots[index];
                if (entry == null || entry.Slot != index || string.IsNullOrEmpty(entry.Identity)
                    || identityToSlot.ContainsKey(entry.Identity))
                    throw new ArgumentException("Roster slots must have unique transport identities.", nameof(roster));
                identities[index] = entry.Identity;
                connected[index] = entry.Connected;
                identityToSlot.Add(entry.Identity, index);
                if (string.Equals(entry.Identity, transport.LocalPlayerId, StringComparison.Ordinal)) LocalSlot = index;
            }

            if (LocalSlot < 0 || !identityToSlot.TryGetValue(roster.HostIdentity, out int hostSlot) || hostSlot != 0
                || transport.IsHost != (LocalSlot == 0))
                throw new ArgumentException("Transport role and roster identity do not match.", nameof(roster));

            sessionId = roster.SessionId;
            rosterGeneration = roster.Generation;
            hostIdentity = roster.HostIdentity;
            localSequence = 0;
            lastLocalSendAt = float.NegativeInfinity;
            configured = true;
        }

        public void Tick(float nowSeconds, Vector3 localPosition, float localYawDegrees, PartyMoveState moveState)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PartyPoseSession));
            if (!IsFinite(nowSeconds) || nowSeconds < currentTime) throw new ArgumentOutOfRangeException(nameof(nowSeconds));
            currentTime = nowSeconds;
            transport.Tick();
            if (!configured || !connected[LocalSlot] || nowSeconds - lastLocalSendAt + Mathf.Epsilon < sendInterval) return;
            if (!TryValidateFields(localPosition, NormalizeYaw(localYawDegrees), moveState)) return;

            lastLocalSendAt = nowSeconds;
            localSequence++;
            var packet = CreatePacket(
                transport.IsHost ? PartyPoseProtocol.KindRelay : PartyPoseProtocol.KindSubmit,
                localSequence,
                LocalSlot,
                localPosition,
                NormalizeYaw(localYawDegrees),
                moveState);
            byte[] bytes = PartyPoseProtocol.Encode(packet);
            if (transport.IsHost) SendToRoster(bytes, LocalSlot);
            else transport.SendToHost(bytes, false);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            transport.OnMessage -= HandleMessage;
            transport.OnPeerDisconnected -= HandlePeerDisconnected;
            ClearRemotePoses();
            configured = false;
        }

        private void HandleMessage(string peerIdentity, byte[] bytes)
        {
            if (!configured || string.IsNullOrEmpty(peerIdentity)
                || !PartyPoseProtocol.TryDecode(bytes, out PartyPosePacket packet)
                || packet.sessionId != sessionId || packet.rosterGeneration != rosterGeneration)
                return;

            if (transport.IsHost) HandleHostMessage(peerIdentity, packet);
            else HandleClientMessage(peerIdentity, packet);
        }

        private void HandleHostMessage(string peerIdentity, PartyPosePacket packet)
        {
            if (packet.kind != PartyPoseProtocol.KindSubmit
                || !identityToSlot.TryGetValue(peerIdentity, out int slot)
                || slot == LocalSlot || !connected[slot]
                || !TryAcceptPose(slot, packet)) return;

            var sample = Sample(slot, packet);
            RemotePoseUpdated?.Invoke(sample);
            byte[] relay = PartyPoseProtocol.Encode(CreatePacket(
                PartyPoseProtocol.KindRelay,
                packet.sequence,
                slot,
                sample.Position,
                sample.YawDegrees,
                sample.MoveState));
            SendToRoster(relay, slot);
        }

        private void HandleClientMessage(string peerIdentity, PartyPosePacket packet)
        {
            if (!string.Equals(peerIdentity, hostIdentity, StringComparison.Ordinal)) return;
            int slot = packet.slot;
            if (slot < 0 || slot >= PartyRoster.Capacity || slot == LocalSlot || !connected[slot]) return;

            if (packet.kind == PartyPoseProtocol.KindRemove)
            {
                if (packet.sequence <= lastSequence[slot]) return;
                lastSequence[slot] = packet.sequence;
                RemovePose(slot);
                connected[slot] = false;
                return;
            }

            if (packet.kind != PartyPoseProtocol.KindRelay || !TryAcceptPose(slot, packet)) return;
            RemotePoseUpdated?.Invoke(Sample(slot, packet));
        }

        private bool TryAcceptPose(int slot, PartyPosePacket packet)
        {
            if (packet.sequence <= lastSequence[slot]) return false;
            var position = new Vector3(packet.positionX, packet.positionY, packet.positionZ);
            if (!TryValidateFields(position, packet.yawDegrees, (PartyMoveState)packet.moveState)) return false;
            if (hasPose[slot])
            {
                float elapsed = Mathf.Max(0f, currentTime - lastAcceptedAt[slot]);
                float allowedDistance = maxSpeed * elapsed + MinimumMovementAllowance;
                if ((position - lastPosition[slot]).sqrMagnitude > allowedDistance * allowedDistance) return false;
            }

            lastSequence[slot] = packet.sequence;
            lastPosition[slot] = position;
            lastAcceptedAt[slot] = currentTime;
            hasPose[slot] = true;
            return true;
        }

        private bool TryValidateFields(Vector3 position, float yawDegrees, PartyMoveState moveState)
        {
            return IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z)
                && Mathf.Abs(position.x) <= positionBound
                && Mathf.Abs(position.y) <= positionBound
                && Mathf.Abs(position.z) <= positionBound
                && IsFinite(yawDegrees) && yawDegrees >= 0f && yawDegrees < 360f
                && moveState >= PartyMoveState.Idle && moveState <= PartyMoveState.Running;
        }

        private void HandlePeerDisconnected(string peerIdentity)
        {
            if (!configured || !identityToSlot.TryGetValue(peerIdentity, out int slot) || slot == LocalSlot) return;
            if (!transport.IsHost && slot == 0)
            {
                for (int remoteSlot = 0; remoteSlot < PartyRoster.Capacity; remoteSlot++)
                {
                    if (remoteSlot != LocalSlot) RemovePose(remoteSlot);
                    connected[remoteSlot] = false;
                }
                return;
            }

            connected[slot] = false;
            RemovePose(slot);
            if (!transport.IsHost) return;
            long removalSequence = lastSequence[slot] + 1;
            lastSequence[slot] = removalSequence;
            byte[] remove = PartyPoseProtocol.Encode(CreatePacket(
                PartyPoseProtocol.KindRemove,
                removalSequence,
                slot,
                Vector3.zero,
                0f,
                PartyMoveState.Idle));
            SendToRoster(remove, slot);
        }

        private void SendToRoster(byte[] bytes, int sourceSlot)
        {
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                if (slot == LocalSlot || slot == sourceSlot || !connected[slot]) continue;
                transport.SendTo(identities[slot], bytes, false);
            }
        }

        private PartyPosePacket CreatePacket(
            string kind,
            long sequence,
            int slot,
            Vector3 position,
            float yawDegrees,
            PartyMoveState moveState)
        {
            return new PartyPosePacket
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
                yawDegrees = yawDegrees,
                moveState = (int)moveState
            };
        }

        private static PartyPoseSample Sample(int slot, PartyPosePacket packet)
        {
            return new PartyPoseSample(
                slot,
                new Vector3(packet.positionX, packet.positionY, packet.positionZ),
                packet.yawDegrees,
                (PartyMoveState)packet.moveState,
                packet.sequence);
        }

        private void ClearRemotePoses()
        {
            for (int slot = 0; slot < PartyRoster.Capacity; slot++) RemovePose(slot);
        }

        private void RemovePose(int slot)
        {
            if (!hasPose[slot]) return;
            hasPose[slot] = false;
            RemotePoseRemoved?.Invoke(slot);
        }

        private static float NormalizeYaw(float yawDegrees)
        {
            return IsFinite(yawDegrees) ? Mathf.Repeat(yawDegrees, 360f) : yawDegrees;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
