using UnityEngine;

namespace CameraCoop
{
    public enum HandCancelReason
    {
        None,
        TrackingLost,
        StaleSample,
        InvalidSample,
        ModeChanged,
        ViewChanged,
        FocusLost,
        TargetUnavailable,
        DrawingCommand,
        ComponentDisabled
    }

    public readonly struct HandInputSample
    {
        public readonly string handedness;
        public readonly Vector2 screenPosition;
        public readonly uint sequence;
        public readonly ulong sampleId;
        public readonly float sampleAgeSeconds;
        public readonly bool isTracked;
        public readonly bool isPinched;
        public readonly HandCancelReason cancelReason;

        public HandInputSample(string handedness, Vector2 screenPosition, uint sequence, ulong sampleId,
            float sampleAgeSeconds, bool isTracked, bool isPinched, HandCancelReason cancelReason = HandCancelReason.None)
        {
            this.handedness = handedness;
            this.screenPosition = screenPosition;
            this.sequence = sequence;
            this.sampleId = sampleId;
            this.sampleAgeSeconds = sampleAgeSeconds;
            this.isTracked = isTracked;
            this.isPinched = isPinched;
            this.cancelReason = cancelReason;
        }
    }

    public readonly struct HandInputState
    {
        public readonly HandInputSample sample;
        public readonly bool isFresh;
        public readonly bool isArmed;

        public HandInputState(HandInputSample sample, bool isFresh, bool isArmed)
        {
            this.sample = sample;
            this.isFresh = isFresh;
            this.isArmed = isArmed;
        }
    }

    public readonly struct HandClickContext
    {
        public readonly string handedness;
        public readonly int viewGeneration;
        public readonly ulong pressSampleId;

        public HandClickContext(string handedness, int viewGeneration, ulong pressSampleId)
        {
            this.handedness = handedness;
            this.viewGeneration = viewGeneration;
            this.pressSampleId = pressSampleId;
        }
    }
}
