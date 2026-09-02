using System;
using UnityEngine;

namespace CameraCoop
{
    public enum PersonalCanvasPlacementState
    {
        Docked = 0,
        Carried = 1
    }

    public sealed class PersonalCanvasPlacement : MonoBehaviour
    {
        [SerializeField] private string ownerPlayerId;
        [SerializeField] private Transform avatarAnchor;
        [SerializeField] private Transform dockAnchor;
        [SerializeField, Min(0f)] private float dockRadius = 0.5f;
        [SerializeField] private Vector3 carriedLocalPosition;
        [SerializeField] private Vector3 carriedLocalEulerAngles;
        [SerializeField] private HandInputRouter handInputRouter;
        [SerializeField] private HandPointer handPointer;
        [SerializeField] private DrawingController drawingController;

        private PersonalCanvasPlacementState state;
        private string holderPlayerId;
        private uint revision;
        private Transform canvasTarget;

        public PersonalCanvasPlacementState State => state;
        public string OwnerPlayerId => ownerPlayerId;
        public string HolderPlayerId => holderPlayerId;
        public uint Revision => revision;
        public Transform CanvasTarget => canvasTarget != null ? canvasTarget : transform;

        public void RebindCanvasTarget(Transform target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            CancelInteraction();
            canvasTarget = target;
        }

        private void Awake()
        {
            if (!string.IsNullOrEmpty(ownerPlayerId) && avatarAnchor != null && dockAnchor != null &&
                IsFinite(dockRadius) && dockRadius >= 0f)
            {
                state = PersonalCanvasPlacementState.Docked;
                holderPlayerId = null;
                AttachDocked();
            }
        }

        public void Configure(string owner, Transform carriedAnchor, Transform ownDockAnchor, float ownDockRadius)
        {
            if (string.IsNullOrEmpty(owner)) throw new ArgumentException("Owner id is required.", nameof(owner));
            if (carriedAnchor == null) throw new ArgumentNullException(nameof(carriedAnchor));
            if (ownDockAnchor == null) throw new ArgumentNullException(nameof(ownDockAnchor));
            if (!IsFinite(ownDockRadius) || ownDockRadius < 0f) throw new ArgumentOutOfRangeException(nameof(ownDockRadius));
            ownerPlayerId = owner;
            avatarAnchor = carriedAnchor;
            dockAnchor = ownDockAnchor;
            dockRadius = ownDockRadius;
            state = PersonalCanvasPlacementState.Docked;
            holderPlayerId = null;
            AttachDocked();
        }

        public void ConfigureCarriedPose(Vector3 localPosition, Quaternion localRotation)
        {
            if (!IsFinite(localPosition)) throw new ArgumentOutOfRangeException(nameof(localPosition));
            if (!IsFinite(localRotation) || localRotation == default) throw new ArgumentOutOfRangeException(nameof(localRotation));
            carriedLocalPosition = localPosition;
            carriedLocalEulerAngles = localRotation.eulerAngles;
            if (state == PersonalCanvasPlacementState.Carried && avatarAnchor != null) AttachCarried();
        }

        public bool TryCarry(string requesterPlayerId)
        {
            if (!IsOwner(requesterPlayerId) || state != PersonalCanvasPlacementState.Docked || avatarAnchor == null)
            {
                return false;
            }
            CancelInteraction();
            state = PersonalCanvasPlacementState.Carried;
            holderPlayerId = ownerPlayerId;
            unchecked { revision++; }
            AttachCarried();
            return true;
        }

        public bool TryDock(string requesterPlayerId)
        {
            if (!IsOwner(requesterPlayerId) || state != PersonalCanvasPlacementState.Carried || dockAnchor == null ||
                !IsFinite(CanvasTarget.position) || (CanvasTarget.position - dockAnchor.position).sqrMagnitude > dockRadius * dockRadius)
            {
                return false;
            }
            CancelInteraction();
            state = PersonalCanvasPlacementState.Docked;
            holderPlayerId = null;
            unchecked { revision++; }
            AttachDocked();
            return true;
        }

        public void ResetForAbortOrDisconnect()
        {
            CancelInteraction();
            bool changed = state != PersonalCanvasPlacementState.Docked || holderPlayerId != null || CanvasTarget.parent != dockAnchor;
            state = PersonalCanvasPlacementState.Docked;
            holderPlayerId = null;
            if (changed) unchecked { revision++; }
            if (dockAnchor != null) AttachDocked();
        }

        private bool IsOwner(string requesterPlayerId)
        {
            return !string.IsNullOrEmpty(ownerPlayerId) &&
                string.Equals(ownerPlayerId, requesterPlayerId, StringComparison.Ordinal);
        }

        private void CancelInteraction()
        {
            if (handInputRouter != null) handInputRouter.CancelCanvasCaptures(HandCancelReason.CanvasPlacementChanged);
            if (handPointer != null) handPointer.CancelCanvasStrokes();
            if (drawingController != null) drawingController.FinalizeActiveStrokes();
        }

        private void AttachDocked()
        {
            Transform target = CanvasTarget;
            target.SetParent(dockAnchor, false);
            target.localPosition = Vector3.zero;
            target.localRotation = Quaternion.identity;
        }

        private void AttachCarried()
        {
            Transform target = CanvasTarget;
            target.SetParent(avatarAnchor, false);
            target.localPosition = carriedLocalPosition;
            target.localRotation = Quaternion.Euler(carriedLocalEulerAngles);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }
    }
}
