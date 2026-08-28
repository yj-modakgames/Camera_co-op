using UnityEngine;

namespace CameraCoop
{
    public abstract class HandInteractable : MonoBehaviour
    {
        public virtual bool IsAvailable => isActiveAndEnabled;
        public virtual bool IsCanvas => false;
        public virtual bool RequiresInside => true;
        public virtual bool Exclusive => !IsCanvas;
        public virtual string DisplayName => gameObject.name;
        public virtual float ClickPitch => 1f;
        internal uint LifecycleRevision { get; private set; }

        protected virtual void OnDisable()
        {
            unchecked
            {
                LifecycleRevision++;
            }
        }

        public virtual void HoverEnter(HandInputSample sample, Vector3 hitPosition) { }
        public virtual void HoverExit(HandInputSample sample, Vector3 hitPosition) { }
        public virtual void Press(HandInputSample sample, Vector3 hitPosition, HandClickContext context) { }
        public virtual void Hold(HandInputSample sample, Vector3 hitPosition) { }
        public virtual bool Release(HandInputSample sample, Vector3 hitPosition) => false;
        public virtual void Cancel(HandInputSample sample, Vector3 hitPosition) { }
    }
}
