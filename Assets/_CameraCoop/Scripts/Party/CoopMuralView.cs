using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CameraCoop.Party
{
    public sealed class CoopMuralLayerSnapshot
    {
        internal CoopMuralLayerSnapshot(int ownerSlot, int revision, CanvasDrawingData drawing)
        {
            OwnerSlot = ownerSlot;
            Revision = revision;
            Drawing = drawing;
        }

        public int OwnerSlot { get; }
        public int Revision { get; }
        public CanvasDrawingData Drawing { get; }
    }

    public sealed class CoopMuralView
    {
        private readonly CoopMuralLayerSnapshot[] layers = new CoopMuralLayerSnapshot[PartyRoster.Capacity];
        private readonly ReadOnlyCollection<CoopMuralLayerSnapshot> readOnlyLayers;

        internal CoopMuralView()
        {
            readOnlyLayers = Array.AsReadOnly(layers);
            ClearLayers();
        }

        public bool Configured { get; private set; }
        public bool Aborted { get; private set; }
        public bool IsFinalDisplay { get; private set; }
        public int ActiveSlot { get; private set; } = -1;
        public bool CanLocalWrite => Configured && !Aborted && !IsFinalDisplay && LocalSlot == ActiveSlot;
        public int LocalSlot { get; private set; } = -1;
        public string SessionId { get; private set; } = string.Empty;
        public int RosterGeneration { get; private set; }
        public int Serial { get; private set; }
        public IReadOnlyList<CoopMuralLayerSnapshot> Layers => readOnlyLayers;

        public bool TryGetLayer(int ownerSlot, out CoopMuralLayerSnapshot layer)
        {
            if (ownerSlot < 0 || ownerSlot >= layers.Length)
            {
                layer = null;
                return false;
            }
            layer = layers[ownerSlot];
            return true;
        }

        internal void Configure(string sessionId, int rosterGeneration, int localSlot)
        {
            SessionId = sessionId;
            RosterGeneration = rosterGeneration;
            LocalSlot = localSlot;
            Configured = true;
            Aborted = false;
            IsFinalDisplay = false;
            ActiveSlot = 0;
            ClearLayers();
            Serial++;
        }

        internal bool Apply(int ownerSlot, int revision, CanvasDrawingData drawing)
        {
            if (!Configured || Aborted || IsFinalDisplay || ownerSlot != ActiveSlot
                || revision <= layers[ownerSlot].Revision)
                return false;
            layers[ownerSlot] = new CoopMuralLayerSnapshot(ownerSlot, revision, drawing);
            Serial++;
            return true;
        }

        internal bool CompleteTurn(int ownerSlot, int revision)
        {
            if (!Configured || Aborted || IsFinalDisplay || ownerSlot != ActiveSlot
                || revision <= 0 || layers[ownerSlot].Revision != revision)
                return false;
            if (ownerSlot == PartyRoster.Capacity - 1)
            {
                IsFinalDisplay = true;
                ActiveSlot = -1;
            }
            else ActiveSlot++;
            Serial++;
            return true;
        }

        internal void Abort()
        {
            if (Aborted) return;
            Aborted = true;
            IsFinalDisplay = false;
            ActiveSlot = -1;
            ClearLayers();
            Serial++;
        }

        internal void Reset()
        {
            Configured = false;
            Aborted = false;
            IsFinalDisplay = false;
            ActiveSlot = -1;
            LocalSlot = -1;
            SessionId = string.Empty;
            RosterGeneration = 0;
            ClearLayers();
            Serial++;
        }

        private void ClearLayers()
        {
            for (int ownerSlot = 0; ownerSlot < layers.Length; ownerSlot++)
                layers[ownerSlot] = new CoopMuralLayerSnapshot(ownerSlot, 0, null);
        }
    }
}
