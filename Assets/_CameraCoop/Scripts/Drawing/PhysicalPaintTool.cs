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
        // 붓을 놓으면 원래 놓여 있던 자리로 돌아가야 한다. dockAnchor 하나에 모으면 붓이 전부 한 점에
        // 겹치고, 그 anchor의 배율이 균일하지 않으면 메시까지 찌그러진다.
        private readonly Dictionary<PhysicalBrush, BrushHome> homes = new Dictionary<PhysicalBrush, BrushHome>();
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
                homes[brush] = BrushHome.Capture(brush.transform);
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
        private readonly struct BrushHome
        {
            private readonly Transform parent;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;

            private BrushHome(Transform parent, Vector3 localPosition, Quaternion localRotation)
            {
                this.parent = parent;
                this.localPosition = localPosition;
                this.localRotation = localRotation;
            }

            public static BrushHome Capture(Transform item)
            {
                return new BrushHome(item.parent, item.localPosition, item.localRotation);
            }

            public void Restore(Transform item)
            {
                item.SetParent(parent, false);
                item.localPosition = localPosition;
                item.localRotation = localRotation;
            }
        }

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
            if (dockAnchor != null)
            {
                brush.transform.SetParent(dockAnchor, false);
                brush.transform.localPosition = Vector3.zero;
                brush.transform.localRotation = Quaternion.identity;
                return;
            }
            if (homes.TryGetValue(brush, out BrushHome home))
            {
                home.Restore(brush.transform);
                return;
            }
            if (rack != null)
            {
                brush.transform.SetParent(rack, false);
                brush.transform.localPosition = Vector3.zero;
                brush.transform.localRotation = Quaternion.identity;
            }
        }
    }
}
