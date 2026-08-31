using System;
using UnityEngine;

namespace CameraCoop.Party
{
    public enum PartyLobbyState
    {
        Setup = 0,
        Started = 1
    }

    [Flags]
    public enum PartyResetScope
    {
        None = 0,
        SecretCache = 1 << 0,
        DrawingCache = 1 << 1,
        ToolOwnership = 1 << 2,
        CanvasPlacementCache = 1 << 3,
        AllRuntimeCaches = SecretCache | DrawingCache | ToolOwnership | CanvasPlacementCache
    }

    public readonly struct PartyHandPresence
    {
        private PartyHandPresence(bool fresh, Vector3 worldPosition)
        {
            Fresh = fresh;
            WorldPosition = worldPosition;
        }

        public static PartyHandPresence Missing => default;
        public bool Fresh { get; }
        public Vector3 WorldPosition { get; }

        public static PartyHandPresence FreshAt(Vector3 worldPosition)
        {
            return new PartyHandPresence(true, worldPosition);
        }
    }

    public sealed class PartyStartSnapshot
    {
        internal PartyStartSnapshot(PartyMode mode, PartyRosterSnapshot roster)
        {
            Mode = mode;
            Roster = roster;
        }

        public PartyMode Mode { get; }
        public PartyRosterSnapshot Roster { get; }
    }

    public readonly struct PartyLifecycleReset
    {
        internal PartyLifecycleReset(int sequence, int rosterGeneration, PartyResetScope scopes)
        {
            Sequence = sequence;
            RosterGeneration = rosterGeneration;
            Scopes = scopes;
        }

        public int Sequence { get; }
        public int RosterGeneration { get; }
        public PartyResetScope Scopes { get; }
    }

    public sealed class PartyLobby
    {
        private readonly PartyRoster roster;
        private readonly PartyZoneLayout zones;
        private readonly float readyDwellSeconds;
        private readonly float[] dwellSeconds = new float[PartyRoster.Capacity];
        private readonly bool[] ready = new bool[PartyRoster.Capacity];
        private int readinessGeneration;
        private int resetSequence;

        public PartyLobby(
            string sessionId,
            string hostIdentity,
            string hostDisplayName,
            PartyZoneLayout zones,
            float readyDwellSeconds)
        {
            if (zones == null) throw new ArgumentNullException(nameof(zones));
            if (float.IsNaN(readyDwellSeconds) || float.IsInfinity(readyDwellSeconds) || readyDwellSeconds <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(readyDwellSeconds));
            }

            roster = new PartyRoster(sessionId, hostIdentity, hostDisplayName);
            this.zones = zones;
            this.readyDwellSeconds = readyDwellSeconds;
            readinessGeneration = roster.Generation;
            SelectedMode = PartyMode.RelayCopy;
            State = PartyLobbyState.Setup;
        }

        public event Action<PartyLifecycleReset> LifecycleReset;

        public PartyLobbyState State { get; private set; }
        public PartyMode SelectedMode { get; private set; }
        public int RosterGeneration => roster.Generation;
        public int OccupiedCount => roster.OccupiedCount;
        public bool IsRosterLocked => roster.Locked;

        public int ReadyCount
        {
            get
            {
                int count = 0;
                for (int slot = 0; slot < ready.Length; slot++)
                {
                    if (ready[slot]) count++;
                }
                return count;
            }
        }

        public bool TryJoin(string identity, string displayName, out int slot, out PartyJoinRejection rejection)
        {
            bool joined = roster.TryJoin(identity, displayName, out slot, out rejection);
            if (joined) ResetReadiness();
            return joined;
        }

        public bool TryDisconnect(string identity)
        {
            bool disconnected = roster.TryDisconnect(identity);
            if (disconnected) ResetReadiness();
            return disconnected;
        }

        public bool TrySelectMode(string senderIdentity, int rosterGeneration, PartyMode mode)
        {
            if (State != PartyLobbyState.Setup
                || rosterGeneration != roster.Generation
                || !string.Equals(senderIdentity, roster.HostIdentity, StringComparison.Ordinal)
                || !PartyModeCatalog.TryGet(mode, out _))
            {
                return false;
            }

            SelectedMode = mode;
            return true;
        }

        public bool UpdateReadiness(
            string senderIdentity,
            int rosterGeneration,
            bool cameraConnected,
            PartyHandPresence leftHand,
            PartyHandPresence rightHand,
            float deltaSeconds)
        {
            if (State != PartyLobbyState.Setup || rosterGeneration != roster.Generation)
            {
                return false;
            }

            EnsureReadinessGeneration();
            int slot = roster.FindSlot(senderIdentity);
            if (slot < 0 || !roster.IsConnected(slot)) return false;

            bool leftOnOwnPad = leftHand.Fresh && zones.ContainsReadyHand(slot, leftHand.WorldPosition);
            bool rightOnOwnPad = rightHand.Fresh && zones.ContainsReadyHand(slot, rightHand.WorldPosition);
            if (!cameraConnected || !leftOnOwnPad && !rightOnOwnPad)
            {
                ClearReady(slot);
                return false;
            }

            float delta = IsFiniteNonNegative(deltaSeconds) ? deltaSeconds : 0f;
            dwellSeconds[slot] = Mathf.Min(readyDwellSeconds, dwellSeconds[slot] + delta);
            ready[slot] = dwellSeconds[slot] >= readyDwellSeconds;
            return ready[slot];
        }

        public bool IsReady(int slot)
        {
            PartyRoster.ValidateSlot(slot);
            EnsureReadinessGeneration();
            return ready[slot];
        }

        public bool TryActivateStartPedestal(
            string senderIdentity,
            int rosterGeneration,
            PartyHandPresence activatingHand,
            out PartyStartSnapshot snapshot)
        {
            snapshot = null;
            if (State != PartyLobbyState.Setup
                || rosterGeneration != roster.Generation
                || !string.Equals(senderIdentity, roster.HostIdentity, StringComparison.Ordinal)
                || !activatingHand.Fresh
                || !zones.StartPedestalBounds.Contains(activatingHand.WorldPosition)
                || roster.OccupiedCount != PartyRoster.Capacity
                || ReadyCount != PartyRoster.Capacity)
            {
                return false;
            }

            if (!roster.TryLock()) return false;
            snapshot = new PartyStartSnapshot(SelectedMode, roster.CreateSnapshot());
            State = PartyLobbyState.Started;
            return true;
        }

        public bool TryResetToSetup(string senderIdentity)
        {
            if (!string.Equals(senderIdentity, roster.HostIdentity, StringComparison.Ordinal)) return false;

            roster.BeginNextSetup();
            State = PartyLobbyState.Setup;
            SelectedMode = PartyMode.RelayCopy;
            ResetReadiness();
            resetSequence++;
            LifecycleReset?.Invoke(new PartyLifecycleReset(
                resetSequence,
                roster.Generation,
                PartyResetScope.AllRuntimeCaches));
            return true;
        }

        private void EnsureReadinessGeneration()
        {
            if (readinessGeneration != roster.Generation) ResetReadiness();
        }

        private void ResetReadiness()
        {
            Array.Clear(dwellSeconds, 0, dwellSeconds.Length);
            Array.Clear(ready, 0, ready.Length);
            readinessGeneration = roster.Generation;
        }

        private void ClearReady(int slot)
        {
            dwellSeconds[slot] = 0f;
            ready[slot] = false;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }
    }
}
