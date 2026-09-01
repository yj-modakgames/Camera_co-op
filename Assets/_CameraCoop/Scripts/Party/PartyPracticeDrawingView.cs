using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CameraCoop.Party
{
    public sealed class PartyPracticeDrawingLayerSnapshot
    {
        internal PartyPracticeDrawingLayerSnapshot(
            int ownerSlot, string ownerIdentity, bool occupied, int revision, CanvasDrawingData drawing)
        {
            OwnerSlot = ownerSlot;
            OwnerIdentity = ownerIdentity ?? string.Empty;
            Occupied = occupied;
            Revision = revision;
            Drawing = drawing;
        }

        public int OwnerSlot { get; }
        public string OwnerIdentity { get; }
        public bool Occupied { get; }
        public int Revision { get; }
        public CanvasDrawingData Drawing { get; }
    }

    public sealed class PartyPracticeDrawingView
    {
        private readonly PartyPracticeDrawingLayerSnapshot[] layers =
            new PartyPracticeDrawingLayerSnapshot[PartyRoster.Capacity];
        private readonly ReadOnlyCollection<PartyPracticeDrawingLayerSnapshot> readOnlyLayers;

        internal PartyPracticeDrawingView()
        {
            readOnlyLayers = Array.AsReadOnly(layers);
            ClearLayers(null);
        }

        public bool Configured { get; private set; }
        public int LocalSlot { get; private set; } = -1;
        public string SessionId { get; private set; } = string.Empty;
        public int RosterGeneration { get; private set; }
        public int TransitionGeneration { get; private set; }
        public int Serial { get; private set; }
        public IReadOnlyList<PartyPracticeDrawingLayerSnapshot> Layers => readOnlyLayers;

        internal void Configure(
            string sessionId, int rosterGeneration, int transitionGeneration,
            int localSlot, string[] identities)
        {
            SessionId = sessionId;
            RosterGeneration = rosterGeneration;
            TransitionGeneration = transitionGeneration;
            LocalSlot = localSlot;
            Configured = true;
            ClearLayers(identities);
            Serial++;
        }

        internal bool Apply(int ownerSlot, int revision, CanvasDrawingData drawing)
        {
            if (!Configured || ownerSlot < 0 || ownerSlot >= layers.Length
                || !layers[ownerSlot].Occupied || revision <= layers[ownerSlot].Revision
                || drawing == null)
                return false;
            PartyPracticeDrawingLayerSnapshot current = layers[ownerSlot];
            layers[ownerSlot] = new PartyPracticeDrawingLayerSnapshot(
                ownerSlot, current.OwnerIdentity, true, revision, drawing);
            Serial++;
            return true;
        }

        internal bool Remove(int ownerSlot)
        {
            if (!Configured || ownerSlot < 0 || ownerSlot >= layers.Length
                || !layers[ownerSlot].Occupied)
                return false;
            layers[ownerSlot] = EmptyLayer(ownerSlot);
            Serial++;
            return true;
        }

        internal void Reset()
        {
            Configured = false;
            LocalSlot = -1;
            SessionId = string.Empty;
            RosterGeneration = 0;
            TransitionGeneration = 0;
            ClearLayers(null);
            Serial++;
        }

        private void ClearLayers(string[] identities)
        {
            for (int slot = 0; slot < layers.Length; slot++)
            {
                string identity = identities != null ? identities[slot] : null;
                layers[slot] = string.IsNullOrEmpty(identity)
                    ? EmptyLayer(slot)
                    : new PartyPracticeDrawingLayerSnapshot(slot, identity, true, 0, null);
            }
        }

        private static PartyPracticeDrawingLayerSnapshot EmptyLayer(int ownerSlot)
        {
            return new PartyPracticeDrawingLayerSnapshot(ownerSlot, string.Empty, false, 0, null);
        }
    }
}
