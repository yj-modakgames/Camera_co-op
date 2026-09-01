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

        private PartyPoseSession session;
        private int remoteSlot = -1;
        private int moveSpeedHash;
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
            animator = explicitAnimator;
            interpolationSeconds = smoothingSeconds;
            moveSpeedHash = Animator.StringToHash(moveSpeedParameter);
            fromPosition = targetPosition = avatarRoot.position;
            fromYaw = targetYaw = avatarRoot.eulerAngles.y;
            hasPose = false;
            SetAvatarRootActive(session.IsSlotOccupied(remoteSlot));
            session.RemotePoseUpdated += HandlePose;
            session.RemotePoseRemoved += HandleRemoved;
            session.SlotOccupancyChanged += HandleSlotOccupancyChanged;
        }

        public void Render(float nowSeconds)
        {
            if (avatarRoot == null || session == null || !hasPose) return;
            float t = Mathf.Clamp01((nowSeconds - poseReceivedAt) / interpolationSeconds);
            avatarRoot.SetPositionAndRotation(
                Vector3.LerpUnclamped(fromPosition, targetPosition, t),
                Quaternion.Euler(0f, Mathf.LerpAngle(fromYaw, targetYaw, t), 0f));
            if (animator != null && moveSpeedHash != 0) animator.SetFloat(moveSpeedHash, targetMoveSpeed);
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
            if (animator != null && moveSpeedHash != 0) animator.SetFloat(moveSpeedHash, 0f);
            SetAvatarRootActive(occupied);
        }

        private void HideAvatar()
        {
            hasPose = false;
            targetMoveSpeed = 0f;
            if (animator != null && moveSpeedHash != 0) animator.SetFloat(moveSpeedHash, 0f);
            SetAvatarRootActive(false);
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
