using UnityEngine;

namespace CameraCoop
{
    // WASD 이동·시선 수식을 MonoBehaviour 밖으로 분리한 순수 함수 (docs/11 §2, docs/04 §5).
    internal static class PlayerMoveLogic
    {
        // 로컬 입력(x=좌우, y=전후) + yaw(도) -> 월드 이동 델타. 대각선이 √2배 빨라지지 않게 정규화한다.
        public static Vector3 Step(Vector2 input, float yawDegrees, float speed, float deltaTime)
        {
            if (input.sqrMagnitude > 1f)
            {
                input = input.normalized;
            }
            Vector3 world = Quaternion.Euler(0f, yawDegrees, 0f) * new Vector3(input.x, 0f, input.y);
            return world * (speed * deltaTime);
        }

        // 사각 방 경계로 clamp. y는 건드리지 않는다 (점프·중력 없음).
        public static Vector3 ClampToRoom(Vector3 position, Vector2 minXZ, Vector2 maxXZ)
        {
            return new Vector3(
                Mathf.Clamp(position.x, minXZ.x, maxXZ.x),
                position.y,
                Mathf.Clamp(position.z, minXZ.y, maxXZ.y));
        }

        // pitch 누적값 clamp. Unity 규약상 +pitch = 아래를 봄.
        public static float ClampPitch(float pitch, float maxPitch)
        {
            return Mathf.Clamp(pitch, -maxPitch, maxPitch);
        }
    }
}
