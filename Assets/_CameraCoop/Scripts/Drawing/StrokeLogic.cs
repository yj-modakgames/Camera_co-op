using UnityEngine;

namespace CameraCoop
{
    // 드로잉 스트로크 판정을 MonoBehaviour 밖으로 분리한 순수 함수 모음 (docs/07 §2, §8).
    internal static class StrokeLogic
    {
        // 구독자가 받은 핀치 이벤트 종류
        public enum PinchKind { Start, Move, End }

        // DrawingController가 취할 행동
        public enum StrokeAction { None, StartNew, EndThenStartNew, Append, End }

        // 활성 스트로크 유무 + 이벤트 종류 -> 행동 (docs/07 §6 엣지 케이스 표)
        public static StrokeAction Decide(bool hasActiveStroke, PinchKind kind)
        {
            switch (kind)
            {
                case PinchKind.Start:
                    return hasActiveStroke ? StrokeAction.EndThenStartNew : StrokeAction.StartNew;
                case PinchKind.Move:
                    return hasActiveStroke ? StrokeAction.Append : StrokeAction.None;
                case PinchKind.End:
                    return hasActiveStroke ? StrokeAction.End : StrokeAction.None;
                default:
                    return StrokeAction.None;
            }
        }

        // 새 점 추가 판정. 첫 점은 항상 추가, 이후는 최소 간격 이상일 때만 (~14Hz 입력의 근접점 필터).
        public static bool ShouldAppendPoint(bool hasLastPoint, Vector3 lastPoint, Vector3 newPoint, float minDistance)
        {
            if (!hasLastPoint)
            {
                return true;
            }
            return (newPoint - lastPoint).sqrMagnitude >= minDistance * minDistance;
        }

        // 연속 점 사이 화면 이동이 임계 초과면 스트로크를 분리한다 (재검출 스냅 방어, docs/07 §6)
        public static bool ShouldSplitStroke(Vector2 lastScreenPos, Vector2 newScreenPos, float maxSegmentDistance)
        {
            return (newScreenPos - lastScreenPos).sqrMagnitude > maxSegmentDistance * maxSegmentDistance;
        }

        // End 시 점 2개 미만 스트로크는 폐기 (점 찍기 미지원, docs/07 §6)
        public static bool ShouldDiscardOnEnd(int pointCount)
        {
            return pointCount < 2;
        }
    }
}
