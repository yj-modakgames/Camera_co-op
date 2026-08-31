using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CameraCoop.Party
{
    public enum PartyJoinRejection
    {
        None = 0,
        InvalidIdentity = 1,
        DuplicateIdentity = 2,
        Full = 3,
        RosterLocked = 4
    }

    public sealed class PartyRosterSlotSnapshot
    {
        internal PartyRosterSlotSnapshot(int slot, string identity, string displayName, bool connected)
        {
            Slot = slot;
            Identity = identity;
            DisplayName = displayName;
            Connected = connected;
        }

        public int Slot { get; }
        public string Identity { get; }
        public string DisplayName { get; }
        public bool Connected { get; }
    }

    public sealed class PartyRosterSnapshot
    {
        private readonly ReadOnlyCollection<PartyRosterSlotSnapshot> slots;

        internal PartyRosterSnapshot(
            string sessionId,
            int generation,
            string hostIdentity,
            PartyRosterSlotSnapshot[] slots)
        {
            SessionId = sessionId;
            Generation = generation;
            HostIdentity = hostIdentity;
            this.slots = Array.AsReadOnly((PartyRosterSlotSnapshot[])slots.Clone());
        }

        public string SessionId { get; }
        public int Generation { get; }
        public string HostIdentity { get; }
        public IReadOnlyList<PartyRosterSlotSnapshot> Slots => slots;
    }

    public sealed class PartyRoster
    {
        public const int Capacity = 4;

        private readonly SlotRecord[] slots = new SlotRecord[Capacity];

        internal PartyRoster(string sessionId, string hostIdentity, string hostDisplayName)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session identity is required.", nameof(sessionId));
            if (string.IsNullOrWhiteSpace(hostIdentity)) throw new ArgumentException("Host identity is required.", nameof(hostIdentity));
            SessionId = sessionId;
            HostIdentity = hostIdentity;
            slots[0] = new SlotRecord(hostIdentity, hostDisplayName ?? string.Empty);
            Generation = 1;
        }

        public string SessionId { get; }
        public string HostIdentity { get; }
        public int Generation { get; private set; }
        public bool Locked { get; private set; }

        public int OccupiedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null) count++;
                }
                return count;
            }
        }

        internal bool TryJoin(string identity, string displayName, out int slot, out PartyJoinRejection rejection)
        {
            slot = -1;
            if (Locked)
            {
                rejection = PartyJoinRejection.RosterLocked;
                return false;
            }
            if (string.IsNullOrWhiteSpace(identity))
            {
                rejection = PartyJoinRejection.InvalidIdentity;
                return false;
            }
            if (FindSlot(identity) >= 0)
            {
                rejection = PartyJoinRejection.DuplicateIdentity;
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) continue;
                slots[i] = new SlotRecord(identity, displayName ?? string.Empty);
                slot = i;
                Generation++;
                rejection = PartyJoinRejection.None;
                return true;
            }

            rejection = PartyJoinRejection.Full;
            return false;
        }

        internal bool TryDisconnect(string identity)
        {
            int slot = FindSlot(identity);
            if (slot < 0 || !slots[slot].Connected) return false;

            if (Locked)
            {
                slots[slot].Connected = false;
            }
            else if (slot == 0)
            {
                slots[slot].Connected = false;
            }
            else
            {
                slots[slot] = null;
            }

            Generation++;
            return true;
        }

        internal int FindSlot(string identity)
        {
            if (identity == null) return -1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && string.Equals(slots[i].Identity, identity, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        internal bool IsConnected(int slot)
        {
            ValidateSlot(slot);
            return slots[slot] != null && slots[slot].Connected;
        }

        internal bool TryLock()
        {
            if (Locked || OccupiedCount != Capacity) return false;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                if (!slots[slot].Connected) return false;
            }
            Locked = true;
            return true;
        }

        internal void BeginNextSetup()
        {
            Locked = false;
            for (int slot = 1; slot < slots.Length; slot++)
            {
                if (slots[slot] != null && !slots[slot].Connected) slots[slot] = null;
            }
            if (slots[0] != null) slots[0].Connected = true;
            Generation++;
        }

        internal PartyRosterSnapshot CreateSnapshot()
        {
            var snapshot = new PartyRosterSlotSnapshot[Capacity];
            for (int slot = 0; slot < snapshot.Length; slot++)
            {
                SlotRecord record = slots[slot];
                if (record == null) throw new InvalidOperationException("Cannot snapshot an incomplete party roster.");
                snapshot[slot] = new PartyRosterSlotSnapshot(slot, record.Identity, record.DisplayName, record.Connected);
            }
            return new PartyRosterSnapshot(SessionId, Generation, HostIdentity, snapshot);
        }

        internal static void ValidateSlot(int slot)
        {
            if (slot < 0 || slot >= Capacity) throw new ArgumentOutOfRangeException(nameof(slot));
        }

        private sealed class SlotRecord
        {
            internal SlotRecord(string identity, string displayName)
            {
                Identity = identity;
                DisplayName = displayName;
                Connected = true;
            }

            internal string Identity { get; }
            internal string DisplayName { get; }
            internal bool Connected { get; set; }
        }
    }
}
