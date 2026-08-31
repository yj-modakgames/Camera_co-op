using System;
using UnityEngine;

namespace CameraCoop.Party
{
    [DisallowMultipleComponent]
    public sealed class WorldLabelBillboard : MonoBehaviour
    {
        [SerializeField] private TextMesh textLabel;
        [SerializeField] private Camera playerCamera;

        public TextMesh TextLabel => textLabel;
        public Camera PlayerCamera => playerCamera;

        public void Configure(TextMesh label, Camera camera)
        {
            textLabel = label != null ? label : throw new ArgumentNullException(nameof(label));
            playerCamera = camera;
        }

        private void LateUpdate()
        {
            RefreshFacing();
        }

        public bool RefreshFacing()
        {
            if (textLabel == null)
            {
                return false;
            }

            Camera camera = playerCamera != null ? playerCamera : Camera.main;
            if (camera == null)
            {
                return false;
            }
            if (playerCamera == null)
            {
                playerCamera = camera;
            }

            Vector3 toCamera = camera.transform.position - transform.position;
            if (toCamera.sqrMagnitude < 0.000001f)
            {
                return false;
            }
            transform.rotation = Quaternion.LookRotation(-toCamera, camera.transform.up);
            return true;
        }
    }
}
