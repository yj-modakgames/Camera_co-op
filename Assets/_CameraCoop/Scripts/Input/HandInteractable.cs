using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop
{
    public abstract class HandInteractable : MonoBehaviour
    {
        // 손은 mouse와 달리 커서가 물체에 닿았는지가 화면에서 잘 안 보인다. hover·press를 대상 자체의
        // 밝기로 돌려주지 않으면 조준이 맞았는지, 눌렸는지 알 수 없다 (사용자 보고 2026-09-02).
        private const float HoverBlend = 0.30f;
        private const float PressBlend = 0.62f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<HighlightTarget> highlights = new List<HighlightTarget>();
        private readonly HashSet<string> hoverHands = new HashSet<string>();
        private readonly HashSet<string> pressHands = new HashSet<string>();
        private MaterialPropertyBlock block;
        private bool highlightsCollected;
        private float appliedBlend = -1f;

        public virtual bool IsAvailable => isActiveAndEnabled;
        public virtual bool IsCanvas => false;
        public virtual bool RequiresInside => true;
        public virtual bool Exclusive => !IsCanvas;
        public virtual bool UsesWorldHitPosition => false;
        public virtual string DisplayName => gameObject.name;
        public virtual float ClickPitch => 1f;
        internal uint LifecycleRevision { get; private set; }

        // 캔버스는 그림이 곧 피드백이라 밝기를 건드리면 잉크 색이 왜곡된다.
        protected virtual bool UsesHighlight => !IsCanvas;

        protected virtual void OnDisable()
        {
            unchecked
            {
                LifecycleRevision++;
            }
            hoverHands.Clear();
            pressHands.Clear();
            ApplyHighlight();
        }

        public virtual void HoverEnter(HandInputSample sample, Vector3 hitPosition)
        {
            if (hoverHands.Add(sample.handedness)) ApplyHighlight();
        }

        public virtual void HoverExit(HandInputSample sample, Vector3 hitPosition)
        {
            bool changed = hoverHands.Remove(sample.handedness);
            changed |= pressHands.Remove(sample.handedness);
            if (changed) ApplyHighlight();
        }

        public virtual void Press(HandInputSample sample, Vector3 hitPosition, HandClickContext context)
        {
            if (pressHands.Add(sample.handedness)) ApplyHighlight();
        }

        public virtual void Hold(HandInputSample sample, Vector3 hitPosition) { }

        public virtual bool Release(HandInputSample sample, Vector3 hitPosition)
        {
            if (pressHands.Remove(sample.handedness)) ApplyHighlight();
            return false;
        }

        public virtual void Cancel(HandInputSample sample, Vector3 hitPosition)
        {
            bool changed = pressHands.Remove(sample.handedness);
            changed |= hoverHands.Remove(sample.handedness);
            if (changed) ApplyHighlight();
        }

        private void ApplyHighlight()
        {
            if (!UsesHighlight) return;
            float blend = pressHands.Count > 0 ? PressBlend : hoverHands.Count > 0 ? HoverBlend : 0f;
            if (Mathf.Approximately(blend, appliedBlend)) return;
            appliedBlend = blend;
            CollectHighlights();
            block ??= new MaterialPropertyBlock();
            for (int index = 0; index < highlights.Count; index++)
            {
                HighlightTarget target = highlights[index];
                if (target.Renderer == null) continue;
                target.Renderer.GetPropertyBlock(block);
                block.SetColor(target.PropertyId, Color.Lerp(target.OriginalColor, Color.white, blend));
                target.Renderer.SetPropertyBlock(block);
            }
        }

        private void CollectHighlights()
        {
            if (highlightsCollected) return;
            highlightsCollected = true;
            foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                // TextMesh는 글자용 renderer라 밝기를 바꾸면 읽기만 나빠진다.
                if (renderer.GetComponent<TextMesh>() != null) continue;
                Material material = renderer.sharedMaterial;
                if (material == null) continue;
                int propertyId = material.HasProperty(BaseColorId) ? BaseColorId
                    : material.HasProperty(ColorId) ? ColorId : 0;
                if (propertyId == 0) continue;
                highlights.Add(new HighlightTarget(renderer, propertyId, material.GetColor(propertyId)));
            }
        }

        private readonly struct HighlightTarget
        {
            public HighlightTarget(MeshRenderer renderer, int propertyId, Color originalColor)
            {
                Renderer = renderer;
                PropertyId = propertyId;
                OriginalColor = originalColor;
            }

            public MeshRenderer Renderer { get; }
            public int PropertyId { get; }
            public Color OriginalColor { get; }
        }
    }
}
