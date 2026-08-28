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
            return Decide(hit, kind, isDrawing, strokesEnabled: true);
        }

        // strokesEnabled=false는 StrokeGate 잠금 중(비출제자 등, docs/12 §2) — 신규·계속 스트로크는 막되
        // 진행 중이던 것은 End로 회수해 그리다 만 선을 남기지 않는다. 도구 클릭은 무해하므로 계속 허용.
        public static RouteAction Decide(HitKind hit, StrokeLogic.PinchKind kind, bool isDrawing, bool strokesEnabled)
        {
            switch (kind)
            {
                case StrokeLogic.PinchKind.Start:
                    if (hit == HitKind.Tool)
                    {
                        return RouteAction.ClickTool; // 새 핀치가 버튼에 맞으면 스트로크를 시작하지 않는다
                    }
                    if (!strokesEnabled)
                    {
                        return RouteAction.None; // 게이트 잠금 중엔 신규 스트로크를 시작하지 않는다
                    }
                    return hit == HitKind.Canvas ? RouteAction.StartStroke : RouteAction.None;
                case StrokeLogic.PinchKind.Move:
                    if (!isDrawing)
                    {
                        return RouteAction.None; // 고아 Move (docs/07 §6)
                    }
                    if (!strokesEnabled)
                    {
                        return RouteAction.EndStroke; // 잠금 순간 진행 중이던 스트로크를 회수
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
