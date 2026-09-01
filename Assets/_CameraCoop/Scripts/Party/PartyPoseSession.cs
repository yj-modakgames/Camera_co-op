using System;
using CameraCoop.Netplay;
using UnityEngine;

namespace CameraCoop.Party
{
    public sealed class PartyPoseSession : IDisposable
    {
        private readonly INetTransport transport;
        private readonly float sendInterval;
        private readonly float positionBound;
        private readonly float maxSpeed;
        private readonly PartyPoseSlotState slots = new PartyPoseSlotState();
        private int transitionGeneration;
        private long localSequence;
        private float currentTime;
        private float lastLocalSendAt = float.NegativeInfinity;
        private bool configured;
        private bool disposed;

        public PartyPoseSession(INetTransport transport, float sendHz = 15f, float positionBound = 100f, float maxSpeed = 20f)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (!PartyPoseSlotState.IsFinite(sendHz) || sendHz <= 0f || sendHz > 15f) throw new ArgumentOutOfRangeException(nameof(sendHz));
            if (!PartyPoseSlotState.IsFinite(positionBound) || positionBound <= 0f) throw new ArgumentOutOfRangeException(nameof(positionBound));
            if (!PartyPoseSlotState.IsFinite(maxSpeed) || maxSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            sendInterval = 1f / sendHz;
            this.positionBound = positionBound;
            this.maxSpeed = maxSpeed;
            transport.OnMessage += HandleMessage;
            transport.OnPeerDisconnected += HandlePeerDisconnected;
        }

        public event Action<PartyPoseSample> RemotePoseUpdated;
        public event Action<int> RemotePoseRemoved;
        public event Action<int, bool> SlotOccupancyChanged;

        public bool IsConfigured => configured;
        public int LocalSlot => slots.LocalSlot;
        public int TransitionGeneration => transitionGeneration;
        public Vector3 LocalSpaceSpawn { get; private set; }
        public float LocalSpaceYawDegrees { get; private set; }
        public bool IsSlotOccupied(int slot) => slots.IsOccupied(slot);

        public void Configure(PartyRosterSnapshot roster)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PartyPoseSession));
            if (roster == null) throw new ArgumentNullException(nameof(roster));

            bool beginsNewSession = !configured || !string.Equals(slots.SessionId, roster.SessionId, StringComparison.Ordinal);
            ClearRemotePoses();
            slots.Configure(roster, transport);
            if (beginsNewSession)
            {
                transitionGeneration = 0;
                LocalSpaceSpawn = Vector3.zero;
                LocalSpaceYawDegrees = 0f;
            }

            localSequence = 0;
            lastLocalSendAt = float.NegativeInfinity;
            configured = true;
            PublishOccupancy();
        }

        public void RebindSpace(int nextTransitionGeneration, Vector3 spawn, float yawDegrees)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PartyPoseSession));
            if (!configured) throw new InvalidOperationException("Configure the pose session before rebinding its space.");
            if (nextTransitionGeneration <= transitionGeneration) throw new ArgumentOutOfRangeException(nameof(nextTransitionGeneration));
            float normalizedYaw = PartyPoseSlotState.NormalizeYaw(yawDegrees);
            if (!PartyPoseSlotState.TryValidateFields(spawn, normalizedYaw, PartyMoveState.Idle, positionBound))
                throw new ArgumentOutOfRangeException(nameof(spawn));

            ClearRemotePoses();
            slots.ResetTracking();
            transitionGeneration = nextTransitionGeneration;
            LocalSpaceSpawn = spawn;
            LocalSpaceYawDegrees = normalizedYaw;
            localSequence = 0;
            lastLocalSendAt = float.NegativeInfinity;
        }

        public void Tick(float nowSeconds, Vector3 localPosition, float localYawDegrees, PartyMoveState moveState)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PartyPoseSession));
            if (!PartyPoseSlotState.IsFinite(nowSeconds) || nowSeconds < currentTime) throw new ArgumentOutOfRangeException(nameof(nowSeconds));
            currentTime = nowSeconds;
            transport.Tick();
            if (!configured || LocalSlot < 0 || !slots.IsOccupied(LocalSlot)
                || nowSeconds - lastLocalSendAt + Mathf.Epsilon < sendInterval) return;

            float normalizedYaw = PartyPoseSlotState.NormalizeYaw(localYawDegrees);
            if (!PartyPoseSlotState.TryValidateFields(localPosition, normalizedYaw, moveState, positionBound)) return;
            lastLocalSendAt = nowSeconds;
            localSequence++;
            byte[] bytes = PartyPoseProtocol.Encode(CreatePacket(
                transport.IsHost ? PartyPoseProtocol.KindRelay : PartyPoseProtocol.KindSubmit,
                localSequence, LocalSlot, localPosition, normalizedYaw, moveState));
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
                || packet.sessionId != slots.SessionId || packet.rosterGeneration != slots.Generation
                || packet.transitionGeneration != transitionGeneration) return;
            if (transport.IsHost) HandleHostMessage(peerIdentity, packet);
            else HandleClientMessage(peerIdentity, packet);
        }

        private void HandleHostMessage(string peerIdentity, PartyPosePacket packet)
        {
            if (packet.kind != PartyPoseProtocol.KindSubmit || !slots.TryGetSlot(peerIdentity, out int slot)
                || slot == LocalSlot || !slots.IsOccupied(slot)
                || !slots.TryAcceptPose(slot, packet, currentTime, positionBound, maxSpeed)) return;

            PartyPoseSample sample = Sample(slot, packet);
            RemotePoseUpdated?.Invoke(sample);
            SendToRoster(PartyPoseProtocol.Encode(CreatePacket(
                PartyPoseProtocol.KindRelay, packet.sequence, slot, sample.Position, sample.YawDegrees, sample.MoveState)), slot);
        }

        private void HandleClientMessage(string peerIdentity, PartyPosePacket packet)
        {
            if (!string.Equals(peerIdentity, slots.HostIdentity, StringComparison.Ordinal)) return;
            int slot = packet.slot;
            if (slot < 0 || slot >= PartyRoster.Capacity || slot == LocalSlot || !slots.IsOccupied(slot)) return;
            if (packet.kind == PartyPoseProtocol.KindRemove)
            {
                if (!slots.TryAcceptRemoval(slot, packet.sequence)) return;
                RemovePose(slot);
                SetDisconnected(slot);
                return;
            }

            if (packet.kind == PartyPoseProtocol.KindRelay && slots.TryAcceptPose(slot, packet, currentTime, positionBound, maxSpeed))
                RemotePoseUpdated?.Invoke(Sample(slot, packet));
        }

        private void HandlePeerDisconnected(string peerIdentity)
        {
            if (!configured || !slots.TryGetSlot(peerIdentity, out int slot) || slot == LocalSlot) return;
            if (!transport.IsHost && slot == 0)
            {
                for (int remoteSlot = 0; remoteSlot < PartyRoster.Capacity; remoteSlot++)
                {
                    if (remoteSlot != LocalSlot) RemovePose(remoteSlot);
                    SetDisconnected(remoteSlot);
                }
                return;
            }

            SetDisconnected(slot);
            RemovePose(slot);
            if (!transport.IsHost) return;
            long removalSequence = slots.CreateRemovalSequence(slot);
            SendToRoster(PartyPoseProtocol.Encode(CreatePacket(
                PartyPoseProtocol.KindRemove, removalSequence, slot, Vector3.zero, 0f, PartyMoveState.Idle)), slot);
        }

        private void SendToRoster(byte[] bytes, int sourceSlot)
        {
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                if (slot == LocalSlot || slot == sourceSlot || !slots.IsOccupied(slot)) continue;
                transport.SendTo(slots.GetIdentity(slot), bytes, false);
            }
        }

        private PartyPosePacket CreatePacket(string kind, long sequence, int slot, Vector3 position, float yawDegrees, PartyMoveState moveState)
        {
            return new PartyPosePacket
            {
                game = PartyPoseProtocol.GameId,
                version = PartyPoseProtocol.Version,
                sessionId = slots.SessionId,
                rosterGeneration = slots.Generation,
                transitionGeneration = transitionGeneration,
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

        private void PublishOccupancy()
        {
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                SlotOccupancyChanged?.Invoke(slot, slots.IsOccupied(slot));
        }

        private void SetDisconnected(int slot)
        {
            if (!slots.IsOccupied(slot)) return;
            slots.SetDisconnected(slot);
            SlotOccupancyChanged?.Invoke(slot, false);
        }

        private void ClearRemotePoses()
        {
            for (int slot = 0; slot < PartyRoster.Capacity; slot++) RemovePose(slot);
        }

        private void RemovePose(int slot)
        {
            if (slots.TryRemovePose(slot)) RemotePoseRemoved?.Invoke(slot);
        }

        private static PartyPoseSample Sample(int slot, PartyPosePacket packet)
        {
            return new PartyPoseSample(slot, new Vector3(packet.positionX, packet.positionY, packet.positionZ), packet.yawDegrees,
                (PartyMoveState)packet.moveState, packet.sequence);
        }
    }
}
