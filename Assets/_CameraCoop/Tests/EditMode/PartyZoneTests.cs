using CameraCoop.Party;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    public sealed class PartyZoneTests
    {
        [Test]
        public void MovementIsClampedToOwningSlotZone()
        {
            PartyZoneLayout layout = PartyTestLayout.Create();

            Vector3 clamped = layout.ClampMovement(1, new Vector3(99f, 1.5f, -99f));

            Assert.That(clamped, Is.EqualTo(new Vector3(7f, 1.5f, -2f)));
        }

        [Test]
        public void DockOwnershipIsSlotSpecific()
        {
            PartyZoneLayout layout = PartyTestLayout.Create();
            Vector3 slotTwoDock = PartyTestLayout.DockPosition(2);

            Assert.That(layout.OwnsDock(2, slotTwoDock), Is.True);
            Assert.That(layout.OwnsDock(1, slotTwoDock), Is.False);
            Assert.That(layout.OwnsDock(3, slotTwoDock), Is.False);
        }

        [Test]
        public void LayoutRequiresEveryFixedSlotExactlyOnce()
        {
            PartyPlayerZone[] invalid = PartyTestLayout.CreateZones();
            invalid[3] = invalid[2];

            Assert.That(() => new PartyZoneLayout(invalid, PartyTestLayout.StartPedestal), Throws.ArgumentException);
        }
    }

    internal static class PartyTestLayout
    {
        internal static readonly PartyWorldBounds StartPedestal = Bounds(-0.5f, 0.5f, 8f, 9f);

        internal static PartyZoneLayout Create()
        {
            return new PartyZoneLayout(CreateZones(), StartPedestal);
        }

        internal static PartyPlayerZone[] CreateZones()
        {
            var zones = new PartyPlayerZone[PartyRoster.Capacity];
            for (int slot = 0; slot < zones.Length; slot++)
            {
                float offset = slot * 5f;
                zones[slot] = new PartyPlayerZone(
                    slot,
                    Bounds(offset, offset + 2f, -2f, 2f),
                    Bounds(offset + 0.1f, offset + 0.9f, -0.5f, 0.5f),
                    Bounds(offset + 1.1f, offset + 1.9f, 1f, 1.8f));
            }
            return zones;
        }

        internal static Vector3 ReadyPosition(int slot)
        {
            return new Vector3(slot * 5f + 0.5f, 1f, 0f);
        }

        internal static Vector3 DockPosition(int slot)
        {
            return new Vector3(slot * 5f + 1.5f, 1f, 1.4f);
        }

        private static PartyWorldBounds Bounds(float minX, float maxX, float minZ, float maxZ)
        {
            return new PartyWorldBounds(new Vector3(minX, 0f, minZ), new Vector3(maxX, 2f, maxZ));
        }
    }
}
