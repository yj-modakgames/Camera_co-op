using UnityEngine;

namespace CameraCoop
{
    // 월드 공간 캔버스 평면 (docs/10 §2). 1x1 Quad에 부착 — transform 스케일이 곧 캔버스 크기.
    // 정규화 [0,1] 좌상단 원점 좌표(docs/02 §3)를 캔버스 표면 위 월드 좌표로 매핑한다.
    // 소비자(DrawingController/RemotePresenter/HandCursorController)는 이 컴포넌트를 optional 참조 — 역방향 참조 없음.
    public class CanvasSurface : MonoBehaviour
    {
        [SerializeField] private float surfaceOffset = -0.005f; // 로컬 z. Quad 정면(-Z) 쪽으로 띄워 z-fighting 방지

        public Vector3 NormToWorld(Vector2 norm)
        {
            return transform.TransformPoint(CanvasSurfaceLogic.NormToLocal(norm, surfaceOffset));
        }
    }

    // 매핑 수식을 MonoBehaviour 밖으로 분리한 순수 함수 (docs/04 §5 테스트 가능 설계)
    internal static class CanvasSurfaceLogic
    {
        // norm [0,1] 좌상단 원점 -> 1x1 Quad 로컬 좌표 (중심 원점, y 반전)
        public static Vector3 NormToLocal(Vector2 norm, float zOffset)
        {
            return new Vector3(norm.x - 0.5f, 0.5f - norm.y, zOffset);
        }
    }
}
