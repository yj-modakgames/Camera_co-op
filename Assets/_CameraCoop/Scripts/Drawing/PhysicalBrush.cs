using UnityEngine;

namespace CameraCoop
{
    public class PhysicalBrush : HandInteractable
    {
        [SerializeField] private PhysicalPaintTool paintTool;
        // 붓 모델의 collider는 실제 붓 굵기(3~8 cm)를 그대로 쓴다. 5 m 떨어져서 손으로 조준하면
        // 화면에서 10 px도 안 되는 표적이라 뒤에 있는 큰 BrushRack만 계속 맞는다 (사용자 보고 2026-09-04).
        // 잡기 전용 box를 덧대 어느 축이든 최소 표적 크기를 world 기준으로 보장한다.
        [SerializeField, Min(0.01f)] private float minGrabSize = 0.22f;
        private PhysicalPaintTool owner;
        public bool IsHeld { get; private set; }

        private void Awake()
        {
            EnsureGrabCollider();
            if (paintTool != null) paintTool.RegisterBrush(this);
        }

        private void EnsureGrabCollider()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return;
            Bounds local = mesh.bounds;
            Vector3 scale = transform.lossyScale;
            BoxCollider box = gameObject.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = new Vector3(
                AtLeastGrabSize(local.size.x, scale.x),
                AtLeastGrabSize(local.size.y, scale.y),
                AtLeastGrabSize(local.size.z, scale.z));
        }

        private float AtLeastGrabSize(float localSize, float axisScale)
        {
            float worldScale = Mathf.Abs(axisScale);
            return worldScale < 0.0001f ? localSize : Mathf.Max(localSize, minGrabSize / worldScale);
        }

        internal void Bind(PhysicalPaintTool value) => owner = value;

        internal void SetHeld(bool value)
        {
            IsHeld = value;
            // 손에 든 붓은 카메라 바로 앞에 매달린다. HandInputRouter.ResolveWorldTarget은 가장 가까운 hit만
            // 후보로 삼으므로(대상이 아니어도 거리는 계산된다) 붓 collider를 켜 두면 그 뒤의 물감통·지우개·
            // 캔버스를 통째로 가린다. 들고 있는 동안은 집을 수도 없으니 collider가 할 일이 없다.
            foreach (Collider blocker in GetComponentsInChildren<Collider>(true)) blocker.enabled = !value;
        }

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
