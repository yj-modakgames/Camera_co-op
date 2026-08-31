using System;
using UnityEngine;

namespace CameraCoop.Party
{
    public sealed class WorldReadyPadInteractable : HandInteractable
    {
        [SerializeField] private PartyWorldController partyWorld;
        [SerializeField, Min(0.05f)] private float dwellSeconds = 1f;

        private bool leftPresent;
        private bool rightPresent;
        private bool ready;
        private float dwellElapsed;
        private int assignedSlot;

        public bool IsReady => ready;
        public float DwellSeconds => dwellSeconds;
        public int AssignedSlot => assignedSlot;
        public float DwellProgress => dwellSeconds <= 0f ? 0f : Mathf.Clamp01(dwellElapsed / dwellSeconds);
        public override bool Exclusive => false;
        public override bool UsesWorldHitPosition => true;
        public override bool IsAvailable => base.IsAvailable && partyWorld != null
            && partyWorld.IsReadyPadAvailable(assignedSlot);

        public void Configure(PartyWorldController controller, float requiredDwellSeconds = 1f)
        {
            Configure(controller, 0, requiredDwellSeconds);
        }

        public void Configure(PartyWorldController controller, int slot, float requiredDwellSeconds = 1f)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            PartyRoster.ValidateSlot(slot);
            if (float.IsNaN(requiredDwellSeconds) || float.IsInfinity(requiredDwellSeconds) || requiredDwellSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(requiredDwellSeconds));
            partyWorld = controller;
            assignedSlot = slot;
            dwellSeconds = requiredDwellSeconds;
            ResetPresence(false);
        }

        public void SetHandPresence(string handedness, bool present)
        {
            if (string.Equals(handedness, "Left", StringComparison.Ordinal)) leftPresent = present;
            else if (string.Equals(handedness, "Right", StringComparison.Ordinal)) rightPresent = present;
        }

        public void TickRuntime(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            bool eligible = partyWorld != null && partyWorld.CanOccupyReadyPad(assignedSlot) && (leftPresent || rightPresent);
            if (!eligible)
            {
                ResetPresence(true);
                return;
            }
            if (ready) return;
            dwellElapsed += deltaSeconds;
            if (dwellElapsed + Mathf.Epsilon < dwellSeconds) return;
            ready = true;
            partyWorld.SetWorldReady(true);
        }

        public override void HoverEnter(HandInputSample sample, Vector3 hitPosition)
        {
            if (sample.isTracked && sample.cancelReason == HandCancelReason.None)
                SetHandPresence(sample.handedness, true);
        }

        public override void HoverExit(HandInputSample sample, Vector3 hitPosition)
        {
            SetHandPresence(sample.handedness, false);
            if (!leftPresent && !rightPresent) ResetPresence(true);
        }

        private void Update()
        {
            TickRuntime(Time.unscaledDeltaTime);
        }

        protected override void OnDisable()
        {
            ResetPresence(true);
            base.OnDisable();
        }

        public void ResetPad()
        {
            leftPresent = false;
            rightPresent = false;
            ResetPresence(true);
        }

        private void ResetPresence(bool notify)
        {
            dwellElapsed = 0f;
            if (!ready) return;
            ready = false;
            if (notify && partyWorld != null) partyWorld.SetWorldReady(false);
        }
    }
}
