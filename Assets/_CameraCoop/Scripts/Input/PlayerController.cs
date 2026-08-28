using UnityEngine;
using UnityEngine.InputSystem;

namespace CameraCoop
{
    // WASD 이동 + 우클릭 홀드 마우스 룩 (docs/11 §4).
    // 커서 잠금(Cursor.lockState)을 쓰지 않는다 — 로비 UI(Host/Clear)를 계속 좌클릭으로 눌러야 한다.
    // ponytail: 사각 방 전용 clamp, 장애물이 생기면 CharacterController로 승격
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private Transform playerCamera;                  // pitch 적용 대상 (자식 Camera). yaw는 이 오브젝트가 담당
        [SerializeField, Min(0f)] private float moveSpeed = 3f;           // m/s
        [SerializeField, Min(0f)] private float lookSensitivity = 0.12f;  // deg / 마우스 픽셀
        [SerializeField] private Vector2 minXZ = new Vector2(-5.5f, -8.75f); // 벽 실측값 - 여유 0.5m (docs/11 §4)
        [SerializeField] private Vector2 maxXZ = new Vector2(5.5f, 0.65f);
        [SerializeField, Range(0f, 89f)] private float maxPitch = 80f;

        private float pitch;

        private void Update()
        {
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

        // 이동 1스텝. Update가 키보드를 읽어 호출하고, 검증(eval)은 입력을 직접 넣어 같은 경로를 태운다 (docs/11 §6 P-5).
        public void Step(Vector2 input, float deltaTime)
        {
            Vector3 next = transform.position + PlayerMoveLogic.Step(input, transform.eulerAngles.y, moveSpeed, deltaTime);
            transform.position = PlayerMoveLogic.ClampToRoom(next, minXZ, maxXZ);
        }

        private void ApplyLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.isPressed)
            {
                return; // 우클릭 홀드 중에만 시선이 돌아간다
            }
            Vector2 delta = mouse.delta.ReadValue();
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
