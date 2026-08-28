namespace CameraCoop
{
    // 타이핑 중 게임플레이 키 차단의 단일 출처 (docs/12 §2).
    // GameUI가 입력창 포커스 여부를 여기에 쓰고, PlayerController·DrawingController가 읽는다.
    public static class InputFocus
    {
        public static bool IsTyping;
    }
}
