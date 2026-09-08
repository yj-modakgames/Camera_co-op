using System;
using UnityEngine;

namespace CameraCoop.Party
{
    public sealed class RemoteAvatarPresenter : MonoBehaviour
    {
        [SerializeField] private Transform avatarRoot;
        [SerializeField] private Animator animator;
        [SerializeField, Min(0.001f)] private float interpolationSeconds = 0.1f;
        [SerializeField] private string moveSpeedParameter = "MoveSpeed";
        // Stylized Astronaut의 controller는 float가 아니라 int AnimationPar로 Idle/Run을 가른다.
        [SerializeField] private string moveStateIntParameter = "AnimationPar";
        [SerializeField] private Transform leftHandBrush;
        [SerializeField] private Transform rightHandBrush;
        // 씬마다 port 계약을 넓히지 않고 아바타 루트 안에서 이름으로 찾는다. 로비와 game Scene이 같은 규칙을 쓴다.
        public const string LeftHandBrushName = "HandBrush_Left";
        public const string RightHandBrushName = "HandBrush_Right";

        private PartyPoseSession session;
        private int remoteSlot = -1;
        private int moveSpeedHash;
        private int moveStateIntHash;
        private bool hasFloatParameter;
        private bool hasIntParameter;
        private PartyCarriedHand targetCarriedHand;
        private Vector3 fromPosition;
        private Vector3 targetPosition;
        private float fromYaw;
        private float targetYaw;
        private float poseReceivedAt;
        private float targetMoveSpeed;
        private bool hasPose;

        public void Initialize(
            PartyPoseSession poseSession,
            int slot,
            Transform explicitAvatarRoot,
            Animator explicitAnimator = null,
            float smoothingSeconds = 0.1f)
        {
            if (poseSession == null) throw new ArgumentNullException(nameof(poseSession));
            if (!poseSession.IsConfigured) throw new InvalidOperationException("Configure the pose session before its presenter.");
            PartyRoster.ValidateSlot(slot);
            if (slot == poseSession.LocalSlot) throw new ArgumentException("The local avatar cannot use a remote presenter.", nameof(slot));
            if (explicitAvatarRoot == null) throw new ArgumentNullException(nameof(explicitAvatarRoot));
            if (float.IsNaN(smoothingSeconds) || float.IsInfinity(smoothingSeconds) || smoothingSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(smoothingSeconds));

            Unsubscribe();
            HideAvatar();
            session = poseSession;
            remoteSlot = slot;
            avatarRoot = explicitAvatarRoot;
            animator = explicitAnimator != null ? explicitAnimator : avatarRoot.GetComponentInChildren<Animator>(true);
            leftHandBrush = FindChild(avatarRoot, LeftHandBrushName) ?? leftHandBrush;
            rightHandBrush = FindChild(avatarRoot, RightHandBrushName) ?? rightHandBrush;
            interpolationSeconds = smoothingSeconds;
            moveSpeedHash = Animator.StringToHash(moveSpeedParameter);
            moveStateIntHash = Animator.StringToHash(moveStateIntParameter);
            hasFloatParameter = HasParameter(moveSpeedHash, AnimatorControllerParameterType.Float);
            hasIntParameter = HasParameter(moveStateIntHash, AnimatorControllerParameterType.Int);
            targetCarriedHand = PartyCarriedHand.None;
            ApplyCarriedHand(PartyCarriedHand.None);
            fromPosition = targetPosition = avatarRoot.position;
            fromYaw = targetYaw = avatarRoot.eulerAngles.y;
            hasPose = false;
            SetAvatarRootActive(session.IsSlotOccupied(remoteSlot));
            session.RemotePoseUpdated += HandlePose;
            session.RemotePoseRemoved += HandleRemoved;
            session.SlotOccupancyChanged += HandleSlotOccupancyChanged;
        }

        public void Unbind()
        {
            HideAvatar();
            Unsubscribe();
        }

        public void Render(float nowSeconds)
        {
            if (avatarRoot == null || session == null || !hasPose) return;
            float t = Mathf.Clamp01((nowSeconds - poseReceivedAt) / interpolationSeconds);
            avatarRoot.SetPositionAndRotation(
                Vector3.LerpUnclamped(fromPosition, targetPosition, t),
                Quaternion.Euler(0f, Mathf.LerpAngle(fromYaw, targetYaw, t), 0f));
            ApplyMoveState(targetMoveSpeed);
            ApplyCarriedHand(targetCarriedHand);
        }

        internal void ApplyPose(PartyPoseSample sample, float receivedAt)
        {
            if (sample.Slot != remoteSlot || avatarRoot == null || session == null) return;
            SetAvatarRootActive(true);
            fromPosition = avatarRoot.position;
            fromYaw = avatarRoot.eulerAngles.y;
            targetPosition = sample.Position;
            targetYaw = sample.YawDegrees;
            targetMoveSpeed = sample.MoveState == PartyMoveState.Running ? 1f
                : sample.MoveState == PartyMoveState.Walking ? 0.5f : 0f;
            targetCarriedHand = sample.CarriedHand;
            poseReceivedAt = receivedAt;
            hasPose = true;
        }

        private void Update()
        {
            Render(Time.unscaledTime);
        }

        private void OnDestroy()
        {
            HideAvatar();
            Unsubscribe();
        }

        private void HandlePose(PartyPoseSample sample)
        {
            ApplyPose(sample, Time.unscaledTime);
        }

        private void HandleRemoved(int slot)
        {
            if (slot != remoteSlot) return;
            HideAvatar();
        }

        private void HandleSlotOccupancyChanged(int slot, bool occupied)
        {
            if (slot != remoteSlot) return;
            hasPose = false;
            targetMoveSpeed = 0f;
            targetCarriedHand = PartyCarriedHand.None;
            ApplyMoveState(0f);
            ApplyCarriedHand(PartyCarriedHand.None);
            SetAvatarRootActive(occupied);
        }

        private void HideAvatar()
        {
            hasPose = false;
            targetMoveSpeed = 0f;
            targetCarriedHand = PartyCarriedHand.None;
            ApplyMoveState(0f);
            ApplyCarriedHand(PartyCarriedHand.None);
            SetAvatarRootActive(false);
        }

        private void ApplyMoveState(float moveSpeed)
        {
            if (animator == null) return;
            if (hasFloatParameter) animator.SetFloat(moveSpeedHash, moveSpeed);
            if (hasIntParameter) animator.SetInteger(moveStateIntHash, moveSpeed > 0.01f ? 1 : 0);
        }

        private void ApplyCarriedHand(PartyCarriedHand hand)
        {
            SetBrushVisible(leftHandBrush, hand == PartyCarriedHand.Left);
            SetBrushVisible(rightHandBrush, hand == PartyCarriedHand.Right);
        }

        private static void SetBrushVisible(Transform brush, bool visible)
        {
            if (brush != null && brush.gameObject.activeSelf != visible) brush.gameObject.SetActive(visible);
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(item.name, name, StringComparison.Ordinal)) return item;
            return null;
        }

        private bool HasParameter(int hash, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.nameHash == hash && parameter.type == type) return true;
            return false;
        }

        private void SetAvatarRootActive(bool active)
        {
            if (avatarRoot != null && avatarRoot.gameObject.activeSelf != active)
                avatarRoot.gameObject.SetActive(active);
        }

        private void Unsubscribe()
        {
            if (session == null) return;
            session.RemotePoseUpdated -= HandlePose;
            session.RemotePoseRemoved -= HandleRemoved;
            session.SlotOccupancyChanged -= HandleSlotOccupancyChanged;
            session = null;
            remoteSlot = -1;
        }
    }
}
