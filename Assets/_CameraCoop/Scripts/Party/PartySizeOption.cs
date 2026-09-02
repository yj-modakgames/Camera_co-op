using System;

namespace CameraCoop.Party
{
    // 이번 party의 정원을 실행 인자로 정한다: CameraCoopRelayOnline.exe -partysize 2
    // 기본값은 설계 명세대로 4다. host만 이 값을 쓰고, client는 host가 잠근 roster를 따른다.
    // 1은 혼자 손 tracking·그리기를 확인하기 위한 시험값이다. 이 값에서는 mode 선택이 열리지 않는다
    // (relay는 최소 2인이 필요하다 — RelayQuizLogic.MinPlayers).
    public static class PartySizeOption
    {
        public const string Flag = "-partysize";
        public const int SoloTestSize = 1;

        private static int resolved;

        // 실행 인자는 프로세스 수명 동안 바뀌지 않는다. Host·Bind 양쪽에서 같은 값을 쓰도록 한 번만 읽는다.
        public static int Resolve()
        {
            if (resolved != 0) return resolved;
            try { resolved = Parse(Environment.GetCommandLineArgs()); }
            catch (Exception) { resolved = PartyRoster.Capacity; }
            return resolved;
        }

        public static int Parse(string[] args)
        {
            if (args == null) return PartyRoster.Capacity;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase)) continue;
                if (!int.TryParse(args[i + 1], out int size)) return PartyRoster.Capacity;
                if (size < SoloTestSize || size > PartyRoster.Capacity) return PartyRoster.Capacity;
                return size;
            }
            return PartyRoster.Capacity;
        }
    }
}
