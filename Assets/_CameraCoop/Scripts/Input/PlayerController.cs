using UnityEngine;
using UnityEngine.InputSystem;

namespace CameraCoop
{
    public enum PlayerControlProfile
    {
        Legacy = 0,
        ModalFirstPerson = 1
    }

    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private PlayerControlProfile controlProfile = PlayerControlProfile.Legacy;
        [SerializeField] private Transform playerCamera;                  // pitch 적용 대상 (자식 Camera). yaw는 이 오브젝트가 담당
        [SerializeField] private CharacterController characterController;
        [SerializeField] private InputModeManager inputModeManager;
        [SerializeField, Min(0f)] private float moveSpeed = 3f;           // m/s
        [SerializeField, Min(0f)] private float lookSensitivity = 0.12f;  // deg / 마우스 픽셀
        [SerializeField] private float gravity = -20f;
        [SerializeField, Min(0f)] private float jumpHeight = 1.5f;
        [SerializeField] private Vector2 minXZ = new Vector2(-5.5f, -8.75f); // 벽 실측값 - 여유 0.5m (docs/11 §4)
        [SerializeField] private Vector2 maxXZ = new Vector2(5.5f, 0.65f);
        [SerializeField, Range(0f, 89f)] private float maxPitch = 80f;

        private float pitch;
        private float verticalVelocity;
        private bool jumpRequested;
        private bool localInputReady;
        private bool useConfiguredMovementBounds;
        private Vector2 configuredMinXZ;
        private Vector2 configuredMaxXZ;

        private bool HasLocalReferences => playerCamera != null && characterController != null && inputModeManager != null;
        private bool CanMoveLocally => enabled && localInputReady && HasLocalReferences && inputModeManager.CanMove;
        private bool CanLookLocally => enabled && localInputReady && HasLocalReferences && inputModeManager.CanLook;

        private void Awake()
        {
            if (controlProfile != PlayerControlProfile.ModalFirstPerson)
            {
                return;
            }
            localInputReady = HasLocalReferences;
            if (!localInputReady)
            {
                Debug.LogError("PlayerController ModalFirstPerson requires explicit playerCamera, characterController, and inputModeManager references. Local input is disabled.", this);
                return;
            }
            pitch = Mathf.DeltaAngle(0f, playerCamera.localEulerAngles.x);
        }

        private void Update()
        {
            if (controlProfile == PlayerControlProfile.ModalFirstPerson)
            {
                UpdateModalInput();
                return;
            }
            ApplyLook();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return; // 장치 없음 방어. legacy Input API는 이 프로젝트에서 예외를 던진다
            }
            if (InputFocus.IsTyping)
            {
                return; // 정답 입력 중 WASD 이동 차단 (docs/12 §2). 마우스 룩은 ApplyLook에서 이미 처리됐다
            }
            var input = new Vector2(
                (keyboard[Key.D].isPressed ? 1f : 0f) - (keyboard[Key.A].isPressed ? 1f : 0f),
                (keyboard[Key.W].isPressed ? 1f : 0f) - (keyboard[Key.S].isPressed ? 1f : 0f));
            if (input.sqrMagnitude <= 0f)
            {
                return;
            }
            Step(input, Time.deltaTime);
        }

        private void UpdateModalInput()
        {
            EnsureLocalInputReady();
            if (!CanMoveLocally)
            {
                verticalVelocity = 0f;
                return;
            }
            ApplyLook();

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                RequestJump();
            }
            Vector2 input = keyboard == null ? Vector2.zero : new Vector2(
                (keyboard[Key.D].isPressed ? 1f : 0f) - (keyboard[Key.A].isPressed ? 1f : 0f),
                (keyboard[Key.W].isPressed ? 1f : 0f) - (keyboard[Key.S].isPressed ? 1f : 0f));
            Step(input, Time.deltaTime);
        }

        // 이동 1스텝. Update가 키보드를 읽어 호출하고, 검증(eval)은 입력을 직접 넣어 같은 경로를 태운다 (docs/11 §6 P-5).
        public void Step(Vector2 input, float deltaTime)
        {
            if (controlProfile == PlayerControlProfile.ModalFirstPerson)
            {
                EnsureLocalInputReady();
                if (!CanMoveLocally)
                {
                    verticalVelocity = 0f;
                    jumpRequested = false;
                    return;
                }
                Vector3 movement = PlayerMoveLogic.Step(input, transform.eulerAngles.y, moveSpeed, deltaTime);
                if (characterController.isGrounded && verticalVelocity < 0f)
                {
                    verticalVelocity = 0f;
                }
                if (jumpRequested && characterController.isGrounded)
                {
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                }
                jumpRequested = false;
                verticalVelocity += gravity * deltaTime;
                movement.y = verticalVelocity * deltaTime;
                CollisionFlags collisions = characterController.Move(movement);
                if ((collisions & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
                {
                    verticalVelocity = 0f;
                }
                if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                {
                    verticalVelocity = 0f;
                }
                if (useConfiguredMovementBounds)
                {
                    Vector3 clamped = PlayerMoveLogic.ClampToRoom(transform.position, configuredMinXZ, configuredMaxXZ);
                    if ((clamped - transform.position).sqrMagnitude > 0f)
                    {
                        bool wasEnabled = characterController.enabled;
                        characterController.enabled = false;
                        transform.position = clamped;
                        characterController.enabled = wasEnabled;
                    }
                }
                return;
            }
            Vector3 next = transform.position + PlayerMoveLogic.Step(input, transform.eulerAngles.y, moveSpeed, deltaTime);
            transform.position = PlayerMoveLogic.ClampToRoom(next, minXZ, maxXZ);
        }

        public void RequestJump()
        {
            EnsureLocalInputReady();
            if (!CanMoveLocally || !characterController.isGrounded)
            {
                return;
            }
            jumpRequested = true;
        }

        private void EnsureLocalInputReady()
        {
            if (controlProfile == PlayerControlProfile.ModalFirstPerson && !localInputReady && HasLocalReferences)
            {
                localInputReady = true;
                pitch = Mathf.DeltaAngle(0f, playerCamera.localEulerAngles.x);
            }
        }

        // 차폐 중 작업·갤러리 시점 정렬 (docs/06 §6). CharacterController 제약을 배치 구간에서만 끈다.
        public void PlaceAt(Transform pose)
        {
            if (controlProfile != PlayerControlProfile.ModalFirstPerson)
            {
                return;
            }
            if (pose == null || !HasLocalReferences)
            {
                Debug.LogError("PlayerController.PlaceAt requires a pose and the ModalFirstPerson references.", this);
                return;
            }
            bool wasEnabled = characterController.enabled;
            characterController.enabled = false;
            transform.SetPositionAndRotation(pose.position, Quaternion.Euler(0f, pose.eulerAngles.y, 0f));
            characterController.enabled = wasEnabled;
            pitch = 0f;
            verticalVelocity = 0f;
            jumpRequested = false;
            playerCamera.localRotation = Quaternion.identity;
        }

        public void ConfigureMovementBounds(Vector2 minimumXZ, Vector2 maximumXZ, bool enabled)
        {
            if (float.IsNaN(minimumXZ.x) || float.IsInfinity(minimumXZ.x)
                || float.IsNaN(minimumXZ.y) || float.IsInfinity(minimumXZ.y)
                || float.IsNaN(maximumXZ.x) || float.IsInfinity(maximumXZ.x)
                || float.IsNaN(maximumXZ.y) || float.IsInfinity(maximumXZ.y)
                || minimumXZ.x > maximumXZ.x || minimumXZ.y > maximumXZ.y)
                throw new System.ArgumentException("Movement bounds require finite ordered XZ values.");
            configuredMinXZ = minimumXZ;
            configuredMaxXZ = maximumXZ;
            useConfiguredMovementBounds = enabled;
        }

        private void ApplyLook()
        {
            if (controlProfile == PlayerControlProfile.ModalFirstPerson && !CanLookLocally)
            {
                verticalVelocity = 0f;
                return;
            }
            Mouse mouse = Mouse.current;
            if (mouse == null || (controlProfile != PlayerControlProfile.ModalFirstPerson && !mouse.rightButton.isPressed))
            {
                return;
            }
            ApplyLookDelta(mouse.delta.ReadValue());
        }

        internal void ApplyLookDelta(Vector2 delta)
        {
            if (controlProfile == PlayerControlProfile.ModalFirstPerson && !CanLookLocally)
            {
                verticalVelocity = 0f;
                return;
            }
            if (delta.sqrMagnitude <= 0f)
            {
                return;
            }
            transform.Rotate(0f, delta.x * lookSensitivity, 0f, Space.Self);
            if (playerCamera == null)
            {
                return;
            }
            pitch = PlayerMoveLogic.ClampPitch(pitch - delta.y * lookSensitivity, maxPitch);
            playerCamera.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
