using System;

namespace CameraCoop.Game
{
    // 게임 메시지 type 상수 (docs/12 §3). NetProtocol의 드로잉 타입과 겹치지 않는다.
    public static class GameMsg
    {
        public const int GuessGameId = 0;

        public const string TypeGameStart = "GameStart";
        public const string TypeRoundBegin = "RoundBegin";
        public const string TypeWordAssign = "WordAssign";
        public const string TypeRelaySwap = "RelaySwap";
        public const string TypeGuessSubmit = "GuessSubmit";
        public const string TypeGuessFeed = "GuessFeed";
        public const string TypeRoundEnd = "RoundEnd";
        public const string TypeGameEnd = "GameEnd";
        public const string TypeGameAbort = "GameAbort";
        public const string TypeGameStateSync = "GameStateSync";
    }

    [Serializable] public class GameStartPayload { public int gameId; public int mode; }
    [Serializable] public class RoundBeginPayload { public int round; public int totalRounds; public string activeId; public int wordLen; public float introSec; public float durationSec; }
    [Serializable] public class WordAssignPayload { public string word; }
    [Serializable] public class RelaySwapPayload { public string drawerId; }
    [Serializable] public class GuessSubmitPayload { public string text; }
    [Serializable] public class GuessFeedPayload { public string playerId; public string text; public bool correct; }
    [Serializable] public class RoundEndPayload { public string word; public string[] playerIds; public int[] scores; public int reason; } // reason: 0=Timeout 1=AllGuessed 2=ActiveLeft
    [Serializable] public class GameEndPayload { public string[] playerIds; public int[] scores; }
    [Serializable] public class GameStateSyncPayload { public int phase; public int gameId; public int mode; public int round; public int totalRounds; public string activeId; public int wordLen; public float remainingSec; public string[] playerIds; public int[] scores; }
}
