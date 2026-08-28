using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop.Game
{
    // 수신 메시지 → 표시 상태 미러 (docs/12 §2). host 포함 전원이 이 한 경로로 화면을 만든다 —
    // host는 브로드캐스트 직후 같은 payload를 자기 미러에 적용한다.
    // 판단하지 않는다: 점수·전이·판정은 전부 host의 GuessGameLogic이 결정한 결과를 그대로 반영한다.
    // 자체 전환은 둘뿐 — RoundIntro→Drawing(docs/12 §3 RoundBegin 비고)과 GameEnd→Idle.
    // Drawing·RoundReveal은 클라 시계로 끝내지 않는다 (타이머 권위는 host, docs/12 §2).
    public class GameClientState
    {
        private const int MaxFeed = 32; // 정답 피드 유지 개수 (UI는 최근 6줄만 보여준다)

        private readonly List<GuessFeedPayload> _feed = new List<GuessFeedPayload>(MaxFeed);
        private readonly Dictionary<string, int> _scores = new Dictionary<string, int>();
        private readonly HashSet<string> _correctThisRound = new HashSet<string>();
        private readonly float _gameEndSec;

        private GuessGameLogic.Phase _phase = GuessGameLogic.Phase.Idle;
        private int _mode;
        private int _round;
        private int _totalRounds;
        private int _wordLen;
        private string _activeId;
        private string _drawerId;
        private string _localWord;
        private float _remainingSec;
        private float _drawingSec;      // RoundBegin.durationSec — RoundIntro 만료 시 재장전할 값
        private bool _spectator;

        // gameEndSec은 프로토콜에 없다(host·클라가 같은 씬 값을 쓴다) — 최종 점수판 표시 시간일 뿐이다
        public GameClientState(float gameEndSec = 8f)
        {
            _gameEndSec = gameEndSec;
        }

        public GuessGameLogic.Phase CurrentPhase
        {
            get { return _phase; }
        }

        public int Mode
        {
            get { return _mode; }
        }

        public int Round
        {
            get { return _round; }
        }

        public int TotalRounds
        {
            get { return _totalRounds; }
        }

        public string ActiveId
        {
            get { return _activeId; }
        }

        // Turns면 ActiveId와 동일, Relay면 RelaySwap이 정한다 (Relay의 첫 drawer도 RelaySwap으로 온다)
        public string CurrentDrawerId
        {
            get { return _drawerId; }
        }

        // WordAssign 수신 시. 못 받았으면 null → UI는 wordLen 힌트를 표시한다. RoundEnd에서 공개된다
        public string LocalWord
        {
            get { return _localWord; }
        }

        public int WordLen
        {
            get { return _wordLen; }
        }

        // 표시용 로컬 카운트다운 — 0 미만으로 내려가지 않는다
        public float RemainingSec
        {
            get { return _remainingSec; }
        }

        // GameStateSync로 합류 → 다음 RoundBegin까지 true (docs/12 §5)
        public bool Spectator
        {
            get { return _spectator; }
        }

        public IReadOnlyList<GuessFeedPayload> Feed
        {
            get { return _feed; }
        }

        public IReadOnlyDictionary<string, int> Scores
        {
            get { return _scores; }
        }

        // ---- Apply: "표시 상태가 바뀌었으면 true" (GameSession이 이벤트 발행 조건으로 쓴다) ----

        public bool ApplyGameStart(GameStartPayload p)
        {
            if (p == null)
            {
                return false;
            }
            _mode = p.mode;
            ResetToIdle();
            _scores.Clear(); // 새 게임은 점수판을 초기화한다 (Idle 복귀 후 유지된 이전 게임 점수를 지우는 유일한 지점)
            return true;
        }

        public bool ApplyRoundBegin(RoundBeginPayload p)
        {
            if (p == null)
            {
                return false;
            }
            _round = p.round;
            _totalRounds = p.totalRounds;
            _activeId = p.activeId;
            _wordLen = p.wordLen;
            _drawerId = IsRelay ? null : p.activeId; // Relay의 첫 drawer는 host의 RelaySwap이 알려준다
            _localWord = null;
            _feed.Clear();
            _correctThisRound.Clear();
            _spectator = false;
            _phase = GuessGameLogic.Phase.RoundIntro;
            _remainingSec = p.introSec;
            _drawingSec = p.durationSec;
            return true;
        }

        public bool ApplyWordAssign(WordAssignPayload p)
        {
            if (p == null)
            {
                return false;
            }
            _localWord = p.word;
            return true;
        }

        public bool ApplyRelaySwap(RelaySwapPayload p)
        {
            if (p == null)
            {
                return false;
            }
            _drawerId = p.drawerId;
            return true;
        }

        public bool ApplyGuessFeed(GuessFeedPayload p)
        {
            if (p == null)
            {
                return false;
            }
            _feed.Add(p);
            if (_feed.Count > MaxFeed)
            {
                _feed.RemoveAt(0); // 32개 유지 — 오래된 것부터 버린다
            }
            if (p.correct && !string.IsNullOrEmpty(p.playerId))
            {
                _correctThisRound.Add(p.playerId); // 이미 맞힌 사람의 재입력을 UI에서도 막는다
            }
            return true;
        }

        public bool ApplyRoundEnd(RoundEndPayload p)
        {
            if (p == null)
            {
                return false;
            }
            _localWord = p.word; // 라운드 끝에 제시어 공개 (docs/12 §1)
            ApplyScores(p.playerIds, p.scores);
            _phase = GuessGameLogic.Phase.RoundReveal;
            _remainingSec = 0f; // revealSec은 프로토콜에 없다 — 다음 RoundBegin이 정본
            return true;
        }

        public bool ApplyGameEnd(GameEndPayload p)
        {
            if (p == null)
            {
                return false;
            }
            ApplyScores(p.playerIds, p.scores);
            _phase = GuessGameLogic.Phase.GameEnd;
            _remainingSec = _gameEndSec;
            return true;
        }

        public bool ApplyGameAbort()
        {
            bool changed = _phase != GuessGameLogic.Phase.Idle;
            ResetToIdle();
            return changed;
        }

        // 늦은 참가: 전체 미러 + Spectator=true. 제시어는 오지 않는다 (관전자, docs/12 §3)
        public bool ApplyGameStateSync(GameStateSyncPayload p)
        {
            if (p == null)
            {
                return false;
            }
            _phase = (GuessGameLogic.Phase)p.phase;
            _mode = p.mode;
            _round = p.round;
            _totalRounds = p.totalRounds;
            _activeId = p.activeId;
            _wordLen = p.wordLen;
            _drawerId = IsRelay ? null : p.activeId;
            _remainingSec = p.remainingSec;
            _drawingSec = 0f; // sync는 durationSec을 싣지 않는다 — RoundIntro 중 합류하면 다음 RoundBegin까지 타이머가 0으로 보인다
            _localWord = null;
            _feed.Clear();
            _correctThisRound.Clear();
            ApplyScores(p.playerIds, p.scores);
            _spectator = true;
            return true;
        }

        // 카운트다운은 표시용 — 반환값은 phase가 바뀐 경우에만 true (매 프레임 이벤트 폭탄 방지)
        public bool Tick(float deltaTime)
        {
            if (_phase == GuessGameLogic.Phase.Idle)
            {
                return false;
            }
            if (_remainingSec > 0f)
            {
                _remainingSec -= deltaTime;
                if (_remainingSec < 0f)
                {
                    _remainingSec = 0f;
                }
                if (_remainingSec > 0f)
                {
                    return false;
                }
            }
            switch (_phase)
            {
                case GuessGameLogic.Phase.RoundIntro:
                    _phase = GuessGameLogic.Phase.Drawing;
                    _remainingSec = _drawingSec;
                    return true;
                case GuessGameLogic.Phase.GameEnd:
                    ResetToIdle(); // _scores는 유지 — 최종 점수판을 Idle에서도 읽는다 (docs/14 §6-2)
                    return true;
                default:
                    return false; // Drawing·RoundReveal의 종료는 host의 RoundEnd/RoundBegin이 정한다
            }
        }

        // Drawing && !Spectator && 정답 자격(Turns: 출제자 아님 / Relay: ActiveId 본인) && 아직 안 맞힘
        public bool CanGuess(string localPlayerId)
        {
            if (string.IsNullOrEmpty(localPlayerId))
            {
                return false;
            }
            if (_phase != GuessGameLogic.Phase.Drawing || _spectator)
            {
                return false;
            }
            if (_correctThisRound.Contains(localPlayerId))
            {
                return false;
            }
            return IsRelay ? localPlayerId == _activeId : localPlayerId != _activeId;
        }

        private bool IsRelay
        {
            get { return _mode == (int)GuessGameLogic.GameMode.Relay; }
        }

        private void ResetToIdle()
        {
            _phase = GuessGameLogic.Phase.Idle;
            _round = 0;
            _totalRounds = 0;
            _wordLen = 0;
            _activeId = null;
            _drawerId = null;
            _localWord = null;
            _remainingSec = 0f;
            _drawingSec = 0f;
            _spectator = false;
            _feed.Clear();
            _correctThisRound.Clear();
        }

        // JsonUtility가 Dictionary를 못 실어 평행 배열로 온다 (docs/12 §3). 길이가 어긋나면 짧은 쪽 기준
        private void ApplyScores(string[] playerIds, int[] scores)
        {
            _scores.Clear();
            if (playerIds == null || scores == null)
            {
                return;
            }
            int count = playerIds.Length < scores.Length ? playerIds.Length : scores.Length;
            if (playerIds.Length != scores.Length)
            {
                Debug.LogWarning("[GameClientState] 점수 평행 배열 길이 불일치 (ids " + playerIds.Length
                    + " / scores " + scores.Length + ") — 짧은 쪽 기준으로 자릅니다");
            }
            for (int i = 0; i < count; i++)
            {
                if (!string.IsNullOrEmpty(playerIds[i]))
                {
                    _scores[playerIds[i]] = scores[i];
                }
            }
        }
    }
}
