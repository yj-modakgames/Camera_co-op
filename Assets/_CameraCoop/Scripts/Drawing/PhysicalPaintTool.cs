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
            // 같은 손으로 다른 붓을 집으면 들고 있던 것을 제자리에 돌려놓고 바꿔 든다
            // (docs/15 §5 "명시적 반납·교체"). 그냥 무시하면 rack을 정확히 조준해 반납하기 전까지
            // 나머지 붓이 죽은 표적이 된다 — 사용자에게는 "가운데 붓만 집힌다"로 보인다 (보고 2026-09-08).
            // 다른 손이 든 붓은 건드리지 않는다. HandInputRouter의 그 손 capture가 살아 있어서
            // 여기서 dock해 버리면 놓여 있는 붓을 계속 Hold하는 유령 상태가 된다.
            if (!IsAllowedPlayer(owner) || brush == null || brush == heldBrush || !brushes.Contains(brush) ||
                (heldBrush != null && heldHand != hand) ||
                !WithinRange(brush.transform.position, interactionPosition)) return false;
            if (heldBrush != null) DockBrush(heldBrush);
            heldBrush = brush;
            heldOwner = owner;
            heldHand = hand;
            brush.SetHeld(true);
            Transform carry = hand == "Right" ? rightCarryAnchor : leftCarryAnchor;
            if (carry != null) Attach(brush.transform, carry);
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
            private readonly Vector3 localScale;

            private BrushHome(Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
            {
                this.parent = parent;
                this.localPosition = localPosition;
                this.localRotation = localRotation;
                this.localScale = localScale;
            }

            public static BrushHome Capture(Transform item)
            {
                return new BrushHome(item.parent, item.localPosition, item.localRotation, item.localScale);
            }

            public void Restore(Transform item)
            {
                item.SetParent(parent, false);
                item.localPosition = localPosition;
                item.localRotation = localRotation;
                item.localScale = localScale;
            }
        }

        // 붓을 다른 부모로 옮기는 유일한 경로. worldPositionStays=true가 새 부모의 배율을 되돌린
        // localScale을 계산해 준다. false로 옮기면 부모 배율이 그대로 곱해지는데, 손 본
        // Arm_2_Right_end는 FBX import 배율 0.001을 키 보정으로 되돌린 결과 lossyScale이 348이라
        // 붓(배율 2.7)이 944배 = 길이 313 m가 됐다 (사용자 보고 2026-09-08).
        private static void Attach(Transform item, Transform parent)
        {
            item.SetParent(parent, true);
            item.localPosition = Vector3.zero;
            item.localRotation = Quaternion.identity;
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
                Attach(brush.transform, dockAnchor);
                return;
            }
            // RegisterBrush가 등록할 때 homes를 채우므로 등록된 붓은 항상 여기서 걸린다.
            if (homes.TryGetValue(brush, out BrushHome home)) home.Restore(brush.transform);
        }
    }
}
