using System;
using System.Collections.Generic;
using CameraCoop.Netplay;
using UnityEngine;

namespace CameraCoop.Party
{
    internal sealed class PartyPoseSlotState
    {
        private const float MinimumMovementAllowance = 0.05f;

        private readonly string[] identities = new string[PartyRoster.Capacity];
        private readonly bool[] occupied = new bool[PartyRoster.Capacity];
        private readonly bool[] hasPose = new bool[PartyRoster.Capacity];
        private readonly Vector3[] lastPosition = new Vector3[PartyRoster.Capacity];
        private readonly float[] lastAcceptedAt = new float[PartyRoster.Capacity];
        private readonly long[] lastSequence = new long[PartyRoster.Capacity];
        private readonly Dictionary<string, int> identityToSlot = new Dictionary<string, int>(PartyRoster.Capacity, StringComparer.Ordinal);

        internal string SessionId { get; private set; }
        internal string HostIdentity { get; private set; }
        internal int Generation { get; private set; }
        internal int LocalSlot { get; private set; } = -1;

        internal void Configure(PartyRosterSnapshot roster, INetTransport transport)
        {
            if (string.IsNullOrEmpty(roster.SessionId) || roster.SessionId.Length > 64 || roster.Generation <= 0
                || string.IsNullOrEmpty(roster.HostIdentity) || roster.Slots == null
                || roster.Slots.Count != PartyRoster.Capacity)
                throw new ArgumentException("A fixed four-slot party roster is required.", nameof(roster));

            identityToSlot.Clear();
            Array.Clear(identities, 0, identities.Length);
            Array.Clear(occupied, 0, occupied.Length);
            ResetTracking();
            LocalSlot = -1;
            for (int index = 0; index < PartyRoster.Capacity; index++)
            {
                PartyRosterSlotSnapshot entry = roster.Slots[index];
                if (entry == null) continue;
                if (entry.Slot != index || string.IsNullOrEmpty(entry.Identity)
                    || identityToSlot.ContainsKey(entry.Identity))
                    throw new ArgumentException("Roster slots must have unique transport identities.", nameof(roster));
                identities[index] = entry.Identity;
                occupied[index] = entry.Connected;
                identityToSlot.Add(entry.Identity, index);
                if (string.Equals(entry.Identity, transport.LocalPlayerId, StringComparison.Ordinal)) LocalSlot = index;
            }

            if (LocalSlot < 0 || !identityToSlot.TryGetValue(roster.HostIdentity, out int hostSlot) || hostSlot != 0
                || transport.IsHost != (LocalSlot == 0))
                throw new ArgumentException("Transport role and roster identity do not match.", nameof(roster));

            SessionId = roster.SessionId;
            Generation = roster.Generation;
            HostIdentity = roster.HostIdentity;
        }

        internal bool IsOccupied(int slot)
        {
            PartyRoster.ValidateSlot(slot);
            return occupied[slot];
        }

        internal bool TryGetSlot(string identity, out int slot) => identityToSlot.TryGetValue(identity, out slot);
        internal string GetIdentity(int slot) => identities[slot];
        internal void SetDisconnected(int slot) => occupied[slot] = false;

        internal bool TryAcceptPose(int slot, PartyPosePacket packet, float currentTime, float positionBound, float maxSpeed)
        {
            if (packet.sequence <= lastSequence[slot]) return false;
            var position = new Vector3(packet.positionX, packet.positionY, packet.positionZ);
            if (!TryValidateFields(position, packet.yawDegrees, (PartyMoveState)packet.moveState, positionBound)) return false;
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

        internal bool TryAcceptRemoval(int slot, long sequence)
        {
            if (sequence <= lastSequence[slot]) return false;
            lastSequence[slot] = sequence;
            return true;
        }

        internal long CreateRemovalSequence(int slot) => ++lastSequence[slot];

        internal bool TryRemovePose(int slot)
        {
            if (!hasPose[slot]) return false;
            hasPose[slot] = false;
            return true;
        }

        internal void ResetTracking()
        {
            Array.Clear(hasPose, 0, hasPose.Length);
            Array.Clear(lastPosition, 0, lastPosition.Length);
            Array.Clear(lastAcceptedAt, 0, lastAcceptedAt.Length);
            Array.Clear(lastSequence, 0, lastSequence.Length);
        }

        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float NormalizeYaw(float yawDegrees) => IsFinite(yawDegrees) ? Mathf.Repeat(yawDegrees, 360f) : yawDegrees;

        internal static bool TryValidateFields(Vector3 position, float yawDegrees, PartyMoveState moveState, float positionBound)
        {
            return IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z)
                && Mathf.Abs(position.x) <= positionBound
                && Mathf.Abs(position.y) <= positionBound
                && Mathf.Abs(position.z) <= positionBound
                && IsFinite(yawDegrees) && yawDegrees >= 0f && yawDegrees < 360f
                && moveState >= PartyMoveState.Idle && moveState <= PartyMoveState.Running;
        }
    }
}
