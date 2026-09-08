using UnityEngine;

namespace CameraCoop.Party
{
    // 본인 캐릭터는 원격 pose를 받지 않으므로 RemoteAvatarPresenter가 구동하지 않는다.
    // 자기 이동량을 직접 보고 Idle/Run을 전환한다.
    [RequireComponent(typeof(Animator))]
    public sealed class LocalAvatarAnimator : MonoBehaviour
    {
        [SerializeField] private Transform playerRoot;
        [SerializeField] private string moveStateIntParameter = "AnimationPar";
        [SerializeField, Min(0.001f)] private float movingSpeedThreshold = 0.35f;

        private Animator animator;
        private int parameterHash;
        private bool hasParameter;
        private Vector3 previousPosition;
        private bool hasPreviousPosition;
        private int appliedState = -1;

        public void Configure(Transform root)
        {
            playerRoot = root;
            hasPreviousPosition = false;
        }

        private void Awake()
        {
            animator = GetComponent<Animator>();
            parameterHash = Animator.StringToHash(moveStateIntParameter);
            hasParameter = HasIntParameter();
            if (playerRoot == null) playerRoot = transform.parent != null ? transform.parent : transform;
        }

        private void Update()
        {
            if (!hasParameter || playerRoot == null) return;
            Vector3 position = playerRoot.position;
            if (!hasPreviousPosition)
            {
                previousPosition = position;
                hasPreviousPosition = true;
                Apply(0);
                return;
            }
            float delta = Time.deltaTime;
            float speed = delta > Mathf.Epsilon ? (position - previousPosition).magnitude / delta : 0f;
            previousPosition = position;
            Apply(speed >= movingSpeedThreshold ? 1 : 0);
        }

        private void Apply(int state)
        {
            if (appliedState == state) return;
            appliedState = state;
            animator.SetInteger(parameterHash, state);
        }

        private bool HasIntParameter()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
                if (parameter.nameHash == parameterHash && parameter.type == AnimatorControllerParameterType.Int) return true;
            return false;
        }
    }
}
