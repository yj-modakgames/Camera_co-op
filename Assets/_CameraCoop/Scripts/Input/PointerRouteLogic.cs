namespace CameraCoop
{
    // 레이 hit 종류 × 핀치 종류 × 그리는 중 여부 -> 행동 (docs/11 §2 조준 규칙, §5 엣지 케이스 표).
    // StrokeLogic.Decide와 같은 형태로 판정을 MonoBehaviour 밖에 둔다 (docs/04 §5).
    internal static class PointerRouteLogic
    {
        public enum HitKind { None, Canvas, Tool }
        public enum RouteAction { None, StartStroke, AppendStroke, EndStroke, ClickTool }

        public static RouteAction Decide(HitKind hit, StrokeLogic.PinchKind kind, bool isDrawing)
        {
            switch (kind)
            {
                case StrokeLogic.PinchKind.Start:
                    if (hit == HitKind.Tool)
                    {
                        return RouteAction.ClickTool; // 새 핀치가 버튼에 맞으면 스트로크를 시작하지 않는다
                    }
                    return hit == HitKind.Canvas ? RouteAction.StartStroke : RouteAction.None;
                case StrokeLogic.PinchKind.Move:
                    if (!isDrawing)
                    {
                        return RouteAction.None; // 고아 Move (docs/07 §6)
                    }
                    // 드래그가 캔버스를 벗어나면 도구를 바꾸지 않고 스트로크만 끊는다 — 선이 캔버스 밖으로 튀지 않게
                    return hit == HitKind.Canvas ? RouteAction.AppendStroke : RouteAction.EndStroke;
                case StrokeLogic.PinchKind.End:
                    return isDrawing ? RouteAction.EndStroke : RouteAction.None;
                default:
                    return RouteAction.None;
            }
        }
    }
}
