using System;
using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop.Party
{
    public readonly struct PartyWorldBounds
    {
        public PartyWorldBounds(Vector3 min, Vector3 max)
        {
            if (!IsFinite(min) || !IsFinite(max) || min.x > max.x || min.y > max.y || min.z > max.z)
            {
                throw new ArgumentException("World bounds require finite ordered min and max values.");
            }

            Min = min;
            Max = max;
        }

        public Vector3 Min { get; }
        public Vector3 Max { get; }

        public bool Contains(Vector3 position)
        {
            return IsFinite(position)
                && position.x >= Min.x && position.x <= Max.x
                && position.y >= Min.y && position.y <= Max.y
                && position.z >= Min.z && position.z <= Max.z;
        }

        public Vector3 Clamp(Vector3 position)
        {
            if (!IsFinite(position))
            {
                throw new ArgumentException("Position must be finite.", nameof(position));
            }

            return new Vector3(
                Mathf.Clamp(position.x, Min.x, Max.x),
                Mathf.Clamp(position.y, Min.y, Max.y),
                Mathf.Clamp(position.z, Min.z, Max.z));
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }

    public sealed class PartyPlayerZone
    {
        public PartyPlayerZone(
            int slot,
            PartyWorldBounds movementBounds,
            PartyWorldBounds readyPadBounds,
            PartyWorldBounds dockBounds)
        {
            PartyRoster.ValidateSlot(slot);
            Slot = slot;
            MovementBounds = movementBounds;
            ReadyPadBounds = readyPadBounds;
            DockBounds = dockBounds;
        }

        public int Slot { get; }
        public PartyWorldBounds MovementBounds { get; }
        public PartyWorldBounds ReadyPadBounds { get; }
        public PartyWorldBounds DockBounds { get; }
    }

    public sealed class PartyZoneLayout
    {
        private readonly PartyPlayerZone[] zones = new PartyPlayerZone[PartyRoster.Capacity];

        public PartyZoneLayout(IEnumerable<PartyPlayerZone> zones, PartyWorldBounds startPedestalBounds)
        {
            if (zones == null) throw new ArgumentNullException(nameof(zones));
            int count = 0;
            foreach (PartyPlayerZone zone in zones)
            {
                if (zone == null) throw new ArgumentException("Player zones cannot contain null.", nameof(zones));
                if (this.zones[zone.Slot] != null) throw new ArgumentException("Every fixed slot must occur once.", nameof(zones));
                this.zones[zone.Slot] = zone;
                count++;
            }

            if (count != PartyRoster.Capacity)
            {
                throw new ArgumentException("A party layout requires exactly four player zones.", nameof(zones));
            }

            StartPedestalBounds = startPedestalBounds;
        }

        public PartyWorldBounds StartPedestalBounds { get; }

        public PartyPlayerZone GetZone(int slot)
        {
            PartyRoster.ValidateSlot(slot);
            return zones[slot];
        }

        public Vector3 ClampMovement(int slot, Vector3 position)
        {
            return GetZone(slot).MovementBounds.Clamp(position);
        }

        public bool OwnsDock(int slot, Vector3 worldPosition)
        {
            return GetZone(slot).DockBounds.Contains(worldPosition);
        }

        public bool ContainsReadyHand(int slot, Vector3 worldPosition)
        {
            return GetZone(slot).ReadyPadBounds.Contains(worldPosition);
        }
    }
}
