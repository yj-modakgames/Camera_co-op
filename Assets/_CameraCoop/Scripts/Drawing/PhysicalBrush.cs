using UnityEngine;

namespace CameraCoop
{
    public class PhysicalBrush : HandInteractable
    {
        [SerializeField] private PhysicalPaintTool paintTool;
        private PhysicalPaintTool owner;
        public bool IsHeld { get; private set; }

        private void Awake()
        {
            if (paintTool != null) paintTool.RegisterBrush(this);
        }

        internal void Bind(PhysicalPaintTool value) => owner = value;
        internal void SetHeld(bool value) => IsHeld = value;

        public bool TryPickup(string playerId, Vector3 interactionPosition) => owner != null && owner.TryPickupBrush(playerId, this, interactionPosition);
        public bool TryPickup(string playerId, Vector3 interactionPosition, string hand) => owner != null && owner.TryPickupBrush(playerId, this, interactionPosition, hand);
        public bool TryPutDown(string playerId, Vector3 interactionPosition) => owner != null && owner.TryPutDownBrush(playerId, interactionPosition);

        public override bool IsAvailable => owner != null && !IsHeld;
        public override bool Exclusive => true;
        public override bool UsesWorldHitPosition => true;

        public override void Press(HandInputSample sample, Vector3 hitPosition, HandClickContext context)
        {
            base.Press(sample, hitPosition, context);
            if (owner != null) TryPickup(owner.LocalPlayerId, hitPosition, sample.handedness);
        }

        public override bool Release(HandInputSample sample, Vector3 hitPosition)
        {
            return base.Release(sample, hitPosition);
        }
    }
}
