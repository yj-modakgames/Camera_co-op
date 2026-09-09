using System;
using CameraCoop.Party;

namespace CameraCoop
{
    [Serializable]
    public sealed class OnlineRelayQuizGalleryEntry
    {
        public string drawingId = string.Empty;
        public int ownerSlot = -1;
        public int revision;
        [NonSerialized] public CanvasDrawingData drawing;

        public OnlineRelayQuizGalleryEntry CopyDescriptor()
        {
            return new OnlineRelayQuizGalleryEntry
            {
                drawingId = drawingId,
                ownerSlot = ownerSlot,
                revision = revision
            };
        }
    }

    [Serializable]
    public sealed class OnlineRelayQuizView
    {
        public RelayQuizState state;
        public int generation = 1;
        public int serial = 1;
        public string sessionId = string.Empty;
        public int rosterGeneration;
        public int roundId;
        public int turnId;
        public int ownerSlot = -1;
        public int revision;
        public int rosterCount;
        public bool rosterLocked;
        public int localSlot = -1;
        public string[] roster = Array.Empty<string>();
        public bool isHost;
        public bool connected;
        public bool localReady;
        public bool remoteReady;
        public bool allReady;
        public bool hasSelectedMode;
        public PartyMode selectedMode;
        public int modeGeneration;
        public int startSignal;
        public int transitionGeneration;
        public PartyTransitionPhase transitionPhase;
        public int sceneReadyMask;
        public bool modeStarted;
        public bool active;
        public bool paused;
        public bool transferPending;
        public bool aborted;
        public bool hasTimer;
        public float remaining;
        public string status = string.Empty;
        public string word = string.Empty;
        public string answer = string.Empty;
        public string[] revealedTexts = Array.Empty<string>();
        public RelayQuizTextRole textRole;
        public RelayQuizReferenceKind referenceKind;
        public string privatePrompt = string.Empty;
        public bool localAnswerSubmitted;
        public bool chainSucceeded;
        public bool correct;
        public string drawingId = string.Empty;
        public int drawingOwnerSlot = -1;
        public int drawingRevision;
        public OnlineRelayQuizGalleryEntry[] gallery = Array.Empty<OnlineRelayQuizGalleryEntry>();
        public OnlineRelayQuizGalleryEntry[] visibleDrawings = Array.Empty<OnlineRelayQuizGalleryEntry>();
        public int payloadRevision;
        [NonSerialized] public CanvasDrawingData drawing;
        [NonSerialized] public CanvasDrawingData referenceDrawing;

        public bool CanSeeDrawing => !aborted && !string.IsNullOrEmpty(drawingId)
            && active && (state == RelayQuizState.Handover || state == RelayQuizState.ObservePrevious
                || state == RelayQuizState.Drawing || state == RelayQuizState.Guessing);

        public bool CanSeeSlotDrawing(int slot)
        {
            bool sharedDrawingPhase = selectedMode == PartyMode.RelayCopy
                || selectedMode == PartyMode.DrawingWordChain
                    && referenceKind != RelayQuizReferenceKind.OwnDrawing;
            if (aborted || !hasSelectedMode || !sharedDrawingPhase
                || transitionPhase != PartyTransitionPhase.InGame || localSlot < 0
                || localSlot >= rosterCount || slot < 0 || slot >= rosterCount || ownerSlot < 0
                || ownerSlot >= rosterCount || state == RelayQuizState.Setup) return false;
            if (state == RelayQuizState.Reveal || state == RelayQuizState.Gallery) return true;
            int first = Math.Max(0, ownerSlot - 1);
            int second = Math.Max(1, ownerSlot);
            if (localSlot == 0) return slot <= second;
            return (localSlot == first || localSlot == second) && (slot == first || slot == second);
        }
    }
}
