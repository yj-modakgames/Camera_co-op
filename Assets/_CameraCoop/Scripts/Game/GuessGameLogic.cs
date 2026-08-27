using System.Collections.Generic;

namespace CameraCoop.Game
{
    // 그림 맞추기 상태 머신 (docs/12 §2). host에서만 구동하는 순수 로직 —
    // 엔진 의존 없이(순수 C#) 시간은 Tick(deltaTime)으로 주입받는다. 정답 판정은 GuessJudge에 위임.
    public class GuessGameLogic
    {
        public enum Phase { Idle = 0, RoundIntro = 1, Drawing = 2, RoundReveal = 3, GameEnd = 4 } // GameStateSyncPayload.phase와 같은 인코딩
        public enum GameMode { Turns = 0, Relay = 1 }
        public enum RoundEndReason { Timeout = 0, AllGuessed = 1, ActiveLeft = 2 }               // RoundEndPayload.reason과 같은 인코딩
        public enum Transition { None, ToRoundIntro, ToDrawing, ToRoundReveal, ToGameEnd, ToIdle, RelaySwap }
        public enum GuessResult { Ignored, Wrong, Correct, CorrectAndRoundEnd }

        private const int MaxGuessLength = 64;          // 정규화 후 길이 상한 (초과는 무시)
        private const int CorrectBaseScore = 100;       // 정답 기본 점수 (+ 남은 초)
        private const int DrawerBonusPerCorrect = 50;   // Turns 출제자 보너스 (정답자 1명당)
        private const int MinPlayers = 2;
        private const int MinRelayPlayers = 3;

        private readonly WordBank _words;
        private readonly float _introSec;
        private readonly float _drawSec;
        private readonly float _revealSec;
        private readonly float _gameEndSec;
        private readonly float _relaySwapSec;

        private readonly List<string> _participants = new List<string>();       // 현재 게임 참가자 (순서 = drawer 순환 순서)
        private readonly List<string> _upcomingQueue = new List<string>();      // 남은 라운드의 ActiveId 순환 큐
        private readonly Dictionary<string, int> _scores = new Dictionary<string, int>();
        private readonly HashSet<string> _guessedThisRound = new HashSet<string>();
        private readonly Dictionary<string, int> _eligibleFromRound = new Dictionary<string, int>(); // 늦은 참가 = Round+1

        private Phase _phase = Phase.Idle;
        private GameMode _mode = GameMode.Turns;
        private int _round;
        private string _activeId;
        private string _currentWord;
        private float _phaseRemaining;
        private float _swapRemaining;
        private int _relayDrawerIndex = -1;             // _participants 인덱스. Drawing(Relay) 밖에서는 -1
        private RoundEndReason _lastReason = RoundEndReason.Timeout;

        public GuessGameLogic(WordBank words, float introSec, float drawSec, float revealSec, float gameEndSec, float relaySwapSec)
        {
            _words = words;
            _introSec = introSec;
            _drawSec = drawSec;
            _revealSec = revealSec;
            _gameEndSec = gameEndSec;
            _relaySwapSec = relaySwapSec;
        }

        public Phase CurrentPhase
        {
            get { return _phase; }
        }

        public GameMode Mode
        {
            get { return _mode; }
        }

        public int Round
        {
            get { return _round; }
        }

        // 진행한 라운드 + 남은 큐. 이탈로 줄고 늦은 참가로 는다
        public int TotalRounds
        {
            get { return _round + _upcomingQueue.Count; }
        }

        public string ActiveId
        {
            get { return _activeId; }
        }

        public string CurrentDrawerId
        {
            get
            {
                if (_mode == GameMode.Turns)
                {
                    return _activeId;
                }
                if (_relayDrawerIndex < 0 || _relayDrawerIndex >= _participants.Count)
                {
                    return null;
                }
                return _participants[_relayDrawerIndex];
            }
        }

        public string CurrentWord
        {
            get { return _currentWord; }
        }

        // 표시용 — 0 미만으로 내려가지 않는다
        public float PhaseRemaining
        {
            get { return _phaseRemaining > 0f ? _phaseRemaining : 0f; }
        }

        public RoundEndReason LastRoundEndReason
        {
            get { return _lastReason; }
        }

        public IReadOnlyDictionary<string, int> Scores
        {
            get { return _scores; }
        }

        // 성공 시 RoundIntro 진입 + 1라운드 세팅. 인원/모드/진행 상태 검사
        public bool StartGame(IReadOnlyList<string> playerIds, GameMode mode, int cycles)
        {
            if (_phase != Phase.Idle)
            {
                return false;
            }
            if (playerIds == null || playerIds.Count < MinPlayers)
            {
                return false;
            }
            if (mode == GameMode.Relay && playerIds.Count < MinRelayPlayers)
            {
                return false;
            }
            if (cycles < 1)
            {
                return false;
            }

            _mode = mode;
            _participants.Clear();
            _upcomingQueue.Clear();
            _scores.Clear();
            _eligibleFromRound.Clear();
            _guessedThisRound.Clear();

            for (int i = 0; i < playerIds.Count; i++)
            {
                string id = playerIds[i];
                _participants.Add(id);
                _scores[id] = 0;
                _eligibleFromRound[id] = 1; // 1라운드부터 정답 자격
            }
            for (int c = 0; c < cycles; c++)
            {
                for (int i = 0; i < playerIds.Count; i++)
                {
                    _upcomingQueue.Add(playerIds[i]);
                }
            }

            _round = 0;
            _lastReason = RoundEndReason.Timeout;
            BeginRound();
            return true;
        }

        // 시간 전이. 호출당 전이 최대 1개 — 남은 시간은 다음 phase로 이월하지 않는다
        public Transition Tick(float deltaTime)
        {
            if (_phase == Phase.Idle)
            {
                return Transition.None;
            }

            _phaseRemaining -= deltaTime;
            if (_phaseRemaining <= 0f)
            {
                switch (_phase)
                {
                    case Phase.RoundIntro:
                        EnterDrawing();
                        return Transition.ToDrawing;
                    case Phase.Drawing:
                        EndRound(RoundEndReason.Timeout);
                        return Transition.ToRoundReveal;
                    case Phase.RoundReveal:
                        if (_upcomingQueue.Count > 0)
                        {
                            BeginRound();
                            return Transition.ToRoundIntro;
                        }
                        _phase = Phase.GameEnd;
                        _phaseRemaining = _gameEndSec;
                        return Transition.ToGameEnd;
                    default: // GameEnd
                        ResetToIdle();
                        return Transition.ToIdle;
                }
            }

            // 릴레이 교대는 phase 만료보다 낮은 우선순위 (라운드가 끝나면 교대는 의미 없다)
            if (_phase == Phase.Drawing && _mode == GameMode.Relay && _relaySwapSec > 0f)
            {
                _swapRemaining -= deltaTime;
                if (_swapRemaining <= 0f)
                {
                    _swapRemaining = _relaySwapSec;
                    _relayDrawerIndex = FindNextDrawerIndex(_relayDrawerIndex + 1);
                    return Transition.RelaySwap;
                }
            }
            return Transition.None;
        }

        public GuessResult SubmitGuess(string playerId, string text)
        {
            if (_phase != Phase.Drawing)
            {
                return GuessResult.Ignored;
            }
            if (playerId == null || !_participants.Contains(playerId))
            {
                return GuessResult.Ignored;
            }
            if (!IsEligible(playerId))
            {
                return GuessResult.Ignored; // 늦은 참가자는 다음 라운드부터
            }
            if (_mode == GameMode.Turns && playerId == _activeId)
            {
                return GuessResult.Ignored; // 출제자 본인
            }
            if (_mode == GameMode.Relay && playerId != _activeId)
            {
                return GuessResult.Ignored; // 맞히는 사람만 제출 가능
            }
            if (_guessedThisRound.Contains(playerId))
            {
                return GuessResult.Ignored;
            }

            string normalized = GuessJudge.Normalize(text);
            if (normalized.Length == 0 || normalized.Length > MaxGuessLength)
            {
                return GuessResult.Ignored;
            }
            if (!GuessJudge.IsMatch(_currentWord, text))
            {
                return GuessResult.Wrong; // 상태 변화 없음 — 피드 브로드캐스트는 GameSession 몫
            }

            _guessedThisRound.Add(playerId);
            int gain = CorrectBaseScore + (int)PhaseRemaining;

            if (_mode == GameMode.Relay)
            {
                for (int i = 0; i < _participants.Count; i++)
                {
                    AddScore(_participants[i], gain); // 팀 협동 점수
                }
                EndRound(RoundEndReason.AllGuessed);
                return GuessResult.CorrectAndRoundEnd;
            }

            AddScore(playerId, gain);
            AddScore(_activeId, DrawerBonusPerCorrect);
            if (AllEligibleGuessed())
            {
                EndRound(RoundEndReason.AllGuessed);
                return GuessResult.CorrectAndRoundEnd;
            }
            return GuessResult.Correct;
        }

        public Transition PlayerLeft(string playerId)
        {
            if (playerId == null)
            {
                return Transition.None;
            }
            int index = _participants.IndexOf(playerId);
            if (index < 0)
            {
                return Transition.None;
            }

            _participants.RemoveAt(index);
            RemoveFutureTurns(playerId);
            _guessedThisRound.Remove(playerId);
            // _scores 항목은 남긴다 — 최종 점수판 표시용

            if (_participants.Count < MinPlayers)
            {
                ResetToIdle();
                return Transition.ToIdle; // GameSession이 GameAbort 송신
            }

            if (playerId == _activeId && (_phase == Phase.Drawing || _phase == Phase.RoundIntro))
            {
                EndRound(RoundEndReason.ActiveLeft); // 라운드 무효 — 점수 없음
                return Transition.ToRoundReveal;
            }

            if (_mode == GameMode.Relay && _phase == Phase.Drawing && _relayDrawerIndex >= 0)
            {
                if (index == _relayDrawerIndex)
                {
                    // 제거로 같은 인덱스가 이미 다음 사람을 가리킨다 → 거기서부터 ActiveId 아닌 사람 탐색
                    _relayDrawerIndex = FindNextDrawerIndex(_relayDrawerIndex);
                    _swapRemaining = _relaySwapSec;
                    return Transition.RelaySwap;
                }
                if (index < _relayDrawerIndex)
                {
                    _relayDrawerIndex--;
                }
            }
            return Transition.None;
        }

        // 늦은 참가: 점수 0 등록 + 큐 끝에 1회 추가 + 다음 라운드부터 정답 자격
        public void AddPlayer(string playerId)
        {
            if (_phase == Phase.Idle || string.IsNullOrEmpty(playerId))
            {
                return;
            }
            if (_participants.Contains(playerId))
            {
                return;
            }

            _participants.Add(playerId);
            if (!_scores.ContainsKey(playerId))
            {
                _scores[playerId] = 0; // 이탈 후 재참가면 이전 점수 보존
            }
            _upcomingQueue.Add(playerId);
            _eligibleFromRound[playerId] = _round + 1;
        }

        public void Abort()
        {
            ResetToIdle();
        }

        // 큐 앞에서 ActiveId를 꺼내 라운드 세팅
        private void BeginRound()
        {
            _round++;
            _activeId = _upcomingQueue[0];
            _upcomingQueue.RemoveAt(0);
            _currentWord = _words != null ? _words.Next() : null;
            _guessedThisRound.Clear();
            _relayDrawerIndex = -1;
            _phase = Phase.RoundIntro;
            _phaseRemaining = _introSec;
        }

        private void EnterDrawing()
        {
            _phase = Phase.Drawing;
            _phaseRemaining = _drawSec;
            _swapRemaining = _relaySwapSec;
            _relayDrawerIndex = _mode == GameMode.Relay ? FindNextDrawerIndex(0) : -1;
        }

        private void EndRound(RoundEndReason reason)
        {
            _lastReason = reason;
            _phase = Phase.RoundReveal;
            _phaseRemaining = _revealSec;
            _relayDrawerIndex = -1;
        }

        private void ResetToIdle()
        {
            _phase = Phase.Idle;
            _round = 0;
            _activeId = null;
            _currentWord = null;
            _phaseRemaining = 0f;
            _swapRemaining = 0f;
            _relayDrawerIndex = -1;
            _participants.Clear();
            _upcomingQueue.Clear();
            _guessedThisRound.Clear();
            _eligibleFromRound.Clear();
            // _scores는 유지 — 최종 점수판을 Idle 복귀 후에도 읽는다
        }

        // start부터 순환하며 ActiveId가 아닌 첫 참가자 인덱스. 없으면 -1
        private int FindNextDrawerIndex(int start)
        {
            int count = _participants.Count;
            if (count == 0)
            {
                return -1;
            }
            for (int k = 0; k < count; k++)
            {
                int i = (start + k) % count;
                if (_participants[i] != _activeId)
                {
                    return i;
                }
            }
            return -1;
        }

        private void RemoveFutureTurns(string playerId)
        {
            for (int i = _upcomingQueue.Count - 1; i >= 0; i--)
            {
                if (_upcomingQueue[i] == playerId)
                {
                    _upcomingQueue.RemoveAt(i);
                }
            }
        }

        private bool IsEligible(string playerId)
        {
            int fromRound;
            if (!_eligibleFromRound.TryGetValue(playerId, out fromRound))
            {
                return false;
            }
            return _round >= fromRound;
        }

        // 출제자 제외, 자격 있는 참가자 전원이 맞혔는가 (이탈자는 이미 목록에 없다)
        private bool AllEligibleGuessed()
        {
            for (int i = 0; i < _participants.Count; i++)
            {
                string id = _participants[i];
                if (id == _activeId || !IsEligible(id))
                {
                    continue;
                }
                if (!_guessedThisRound.Contains(id))
                {
                    return false;
                }
            }
            return true;
        }

        private void AddScore(string playerId, int amount)
        {
            if (playerId == null)
            {
                return;
            }
            int current;
            _scores.TryGetValue(playerId, out current);
            _scores[playerId] = current + amount;
        }
    }
}
