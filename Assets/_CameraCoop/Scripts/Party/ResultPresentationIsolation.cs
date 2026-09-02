using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop.Party
{
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class ResultPresentationIsolation : MonoBehaviour
    {
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Collider> hiddenColliders = new List<Collider>();

        private void OnEnable()
        {
            Apply();
        }

        public void Apply()
        {
            if (hiddenRenderers.Count != 0 || hiddenColliders.Count != 0) return;
            hiddenRenderers.Clear();
            hiddenColliders.Clear();
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || renderer.transform.IsChildOf(transform)) continue;
                    renderer.enabled = false;
                    hiddenRenderers.Add(renderer);
                }
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (!collider.enabled || collider.transform.IsChildOf(transform)) continue;
                    collider.enabled = false;
                    hiddenColliders.Add(collider);
                }
            }
        }

        private void OnDisable()
        {
            Release();
        }

        public void Release()
        {
            foreach (Renderer renderer in hiddenRenderers)
                if (renderer != null) renderer.enabled = true;
            foreach (Collider collider in hiddenColliders)
                if (collider != null) collider.enabled = true;
            hiddenRenderers.Clear();
            hiddenColliders.Clear();
        }
    }
}
