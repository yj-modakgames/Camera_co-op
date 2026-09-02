using System;
using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop
{
    public class PhysicalPaintTool : MonoBehaviour
    {
        public enum BrushLocation { Docked, Held }

        [SerializeField] private ToolState toolState;
        [SerializeField] private string localPlayerId;
        [SerializeField, Min(0.01f)] private float maxInteractionDistance = 2f;
        [SerializeField] private Transform rack;
        [SerializeField] private Transform dockAnchor;
        [SerializeField] private Transform leftCarryAnchor;
        [SerializeField] private Transform rightCarryAnchor;
        [SerializeField] private PhysicalBrush[] brushReferences;

        private readonly List<PhysicalBrush> brushes = new List<PhysicalBrush>();
        private PhysicalBrush heldBrush;
        private string heldOwner;
        private string heldHand;

        public BrushLocation Location => heldBrush == null ? BrushLocation.Docked : BrushLocation.Held;
        public string HeldOwner => heldOwner;
        public PhysicalBrush HeldBrush => heldBrush;
        public string HeldHand => heldHand;
        public string LocalPlayerId => localPlayerId;
        public float MaxInteractionDistance { get => maxInteractionDistance; set => maxInteractionDistance = Mathf.Max(0.01f, value); }

        private void Awake()
        {
            if (brushReferences == null) return;
            for (int i = 0; i < brushReferences.Length; i++) RegisterBrush(brushReferences[i]);
        }

        public void SetToolState(ToolState value) => toolState = value != null
            ? value : throw new ArgumentNullException(nameof(value));
        public void SetToolStateForTests(ToolState value) => SetToolState(value);
        public void SetLocalPlayerId(string value) => localPlayerId = value;
        public void SetCarryAnchor(string hand, Transform anchor)
        {
            if (hand == "Right") rightCarryAnchor = anchor;
            else leftCarryAnchor = anchor;
        }
        public void SetDockAnchor(Transform anchor) => dockAnchor = anchor;

        public void RegisterBrush(PhysicalBrush brush)
        {
            if (brush != null && !brushes.Contains(brush))
            {
                brushes.Add(brush);
                brush.Bind(this);
                DockBrush(brush);
            }
        }

        public bool TryPickupBrush(string owner, PhysicalBrush brush, Vector3 interactionPosition)
        {
            return TryPickupBrush(owner, brush, interactionPosition, null);
        }

        public bool TryPickupBrush(string owner, PhysicalBrush brush, Vector3 interactionPosition, string hand)
        {
            if (!IsAllowedPlayer(owner) || brush == null || heldBrush != null || !brushes.Contains(brush) ||
                !WithinRange(brush.transform.position, interactionPosition)) return false;
            heldBrush = brush;
            heldOwner = owner;
            heldHand = hand;
            brush.SetHeld(true);
            Transform carry = hand == "Right" ? rightCarryAnchor : leftCarryAnchor;
            if (carry != null)
            {
                brush.transform.SetParent(carry, false);
                brush.transform.localPosition = Vector3.zero;
                brush.transform.localRotation = Quaternion.identity;
            }
            return true;
        }

        public bool TryPutDownBrush(string owner, Vector3 interactionPosition)
        {
            if (!Owns(owner) || heldBrush == null || !WithinRange(heldBrush.transform.position, interactionPosition)) return false;
            PhysicalBrush brush = heldBrush;
            heldBrush = null;
            heldOwner = null;
            heldHand = null;
            DockBrush(brush);
            return true;
        }

        public bool TrySelectPaint(string owner, int index, Vector3 interactionPosition) => TryApply(owner, ToolKind.Color, index, interactionPosition);
        public bool TrySelectWidth(string owner, int index, Vector3 interactionPosition) => TryApply(owner, ToolKind.Width, index, interactionPosition);
        public bool TrySelectEraser(string owner, Vector3 interactionPosition) => TryApply(owner, ToolKind.Eraser, 0, interactionPosition);

        public void HandleDisconnect(string owner)
        {
            if (Owns(owner)) ReturnHeldBrush();
        }

        public void ResetToRack() => ReturnHeldBrush();

        private bool TryApply(string owner, ToolKind kind, int index, Vector3 interactionPosition)
        {
            if (!Owns(owner) || toolState == null || !WithinRange(heldBrush.transform.position, interactionPosition)) return false;
            toolState.ApplySelection(kind, index);
            if (kind == ToolKind.Color) return toolState.CurrentColorIndex == index;
            if (kind == ToolKind.Width) return toolState.CurrentWidthIndex == index;
            return toolState.CurrentMode == ToolState.Mode.Erase;
        }

        private bool Owns(string owner) => heldBrush != null && IsAllowedPlayer(owner) && heldOwner == owner;
        private bool IsAllowedPlayer(string owner) => !string.IsNullOrEmpty(localPlayerId) && owner == localPlayerId;
        private bool WithinRange(Vector3 a, Vector3 b) => maxInteractionDistance <= 0f || Vector3.Distance(a, b) <= maxInteractionDistance;

        private void ReturnHeldBrush()
        {
            if (heldBrush == null) { heldOwner = null; return; }
            PhysicalBrush brush = heldBrush;
            heldBrush = null;
            heldOwner = null;
            heldHand = null;
            DockBrush(brush);
        }

        private void DockBrush(PhysicalBrush brush)
        {
            if (brush == null) return;
            brush.SetHeld(false);
            Transform target = dockAnchor != null ? dockAnchor : rack;
            if (target != null)
            {
                brush.transform.SetParent(target, false);
                brush.transform.localPosition = Vector3.zero;
                brush.transform.localRotation = Quaternion.identity;
            }
        }
    }
}
