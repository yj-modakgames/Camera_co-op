using System;
using System.Collections.Generic;
using CameraCoop.Game;

namespace CameraCoop
{
    // 로컬 릴레이 상태 이름 (docs/09 §3). Pause는 별도 상태가 아니라 flag다.
    public enum RelayQuizState
    {
        Setup,
        Handover,
        WordReveal,
        Drawing,
        ObservePrevious,
        Guessing,
        Reveal,
        Gallery
    }

    // 한 턴의 완성 그림 기록 (docs/09 §8). 제시어는 넣지 않는다.
    [Serializable]
    public class RelayTurnRecord
    {
        public int playerIndex;
        public int drawingIndex;
        public CanvasDrawingData drawing;
    }

    [Serializable]
    public class RelayQuizTimings
    {
        public float wordRevealSeconds = 5f;
        public float drawingSeconds = 60f;
        public float observeSeconds = 5f;
        public float guessSeconds = 30f;

        public bool IsValid
        {
            get
            {
                return IsPositive(wordRevealSeconds) && IsPositive(drawingSeconds)
                    && IsPositive(observeSeconds) && IsPositive(guessSeconds);
            }
        }

        private static bool IsPositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }
    }

    // 순수 릴레이 상태 머신 (docs/09 §4~5). Unity transport·MonoBehaviour를 참조하지 않는다.
    // 시간은 Tick(deltaSeconds)로만 들어오고 wall clock을 다시 읽지 않는다.
    public sealed class RelayQuizLogic
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 4;

        private readonly RelayQuizTimings timings;
        private readonly Func<string> wordSource;
        private readonly Func<CanvasDrawingData> drawingSource;
        private readonly Func<string> answerSource;
        private readonly List<RelayTurnRecord> records = new List<RelayTurnRecord>();

        private RelayQuizState state = RelayQuizState.Setup;
        private int playerCount = MinPlayers;
        private int playerIndex;
        private int phaseGeneration = 1;   // 0은 기본값 충돌을 피해 쓰지 않는다
        private int stateSerial = 1;       // 상태 진입에만 증가. pause는 올리지 않는다
        private float remaining;
        private bool hasTimer;
        private bool paused;
        private string secretWord = string.Empty;
        private string submittedAnswer = string.Empty;
        private bool answerCorrect;
        private bool answerSubmitted;

        public RelayQuizLogic(RelayQuizTimings timings, Func<string> wordSource,
            Func<CanvasDrawingData> drawingSource, Func<string> answerSource)
        {
            if (timings == null || !timings.IsValid)
            {
                throw new ArgumentException("RelayQuizLogic requires four positive finite durations.", "timings");
            }
            this.timings = timings;
            this.wordSource = wordSource;
            this.drawingSource = drawingSource;
            this.answerSource = answerSource;
        }

        public RelayQuizState State { get { return state; } }
        public int PlayerCount { get { return playerCount; } }
        public int PlayerIndex { get { return playerIndex; } }
        public int PhaseGeneration { get { return phaseGeneration; } }
        public int StateSerial { get { return stateSerial; } }
        public bool Paused { get { return paused; } }
        public bool HasTimer { get { return hasTimer; } }
        public float RemainingSeconds { get { return hasTimer ? remaining : 0f; } }
        public IReadOnlyList<RelayTurnRecord> Records { get { return records; } }
        public string SubmittedAnswer { get { return submittedAnswer; } }
        public bool AnswerCorrect { get { return answerCorrect; } }
        public bool AnswerSubmitted { get { return answerSubmitted; } }

        // 제시어는 첫 WordReveal과 결과 Reveal에서만 노출한다 (docs/09 §2).
        public bool IsWordVisible
        {
            get { return state == RelayQuizState.WordReveal || state == RelayQuizState.Reveal; }
        }

        public string VisibleWord { get { return IsWordVisible ? secretWord : string.Empty; } }

        public bool IsLastPlayer { get { return playerIndex >= playerCount - 1; } }

        // 관찰·답변자가 보는 직전 그림. 없으면 null.
        public CanvasDrawingData PreviousDrawing
        {
            get { return records.Count == 0 ? null : records[records.Count - 1].drawing; }
        }

        public bool SetPlayerCount(int count, int captureGeneration)
        {
            if (!CanAct(captureGeneration) || state != RelayQuizState.Setup) return false;
            if (count < MinPlayers || count > MaxPlayers) return false;
            playerCount = count;
            return true;
        }

        public bool StartGame(int captureGeneration)
        {
            if (!CanAct(captureGeneration) || state != RelayQuizState.Setup) return false;
            ResetSession();
            secretWord = wordSource != null ? (wordSource() ?? string.Empty) : string.Empty;
            EnterState(RelayQuizState.Handover, 0f);
            return true;
        }

        public bool ConfirmReady(int captureGeneration)
        {
            if (!CanAct(captureGeneration) || state != RelayQuizState.Handover) return false;
            if (playerIndex == 0)
            {
                EnterState(RelayQuizState.WordReveal, timings.wordRevealSeconds);
            }
            else if (IsLastPlayer)
            {
                EnterState(RelayQuizState.Guessing, timings.guessSeconds);
            }
            else
            {
                EnterState(RelayQuizState.ObservePrevious, timings.observeSeconds);
            }
            return true;
        }

        public bool CompleteDrawing(int captureGeneration)
        {
            if (!CanAct(captureGeneration) || state != RelayQuizState.Drawing) return false;
            CommitDrawing();
            return true;
        }

        public bool SubmitAnswer(int captureGeneration)
        {
            if (!CanAct(captureGeneration) || state != RelayQuizState.Guessing) return false;
            CommitAnswer();
            return true;
        }

        public bool OpenGallery(int captureGeneration)
        {
            if (!CanAct(captureGeneration) || state != RelayQuizState.Reveal) return false;
            EnterState(RelayQuizState.Gallery, 0f);
            return true;
        }

        public bool Restart(int captureGeneration)
        {
            if (!CanAct(captureGeneration) || state != RelayQuizState.Gallery) return false;
            ResetSession();
            EnterState(RelayQuizState.Setup, 0f);
            return true;
        }

        // focus 상실·Drawing의 손 추적 상실이 호출한다. 턴 완료·archive는 일으키지 않는다.
        public bool RequestPause()
        {
            if (paused) return false;
            paused = true;
            AdvanceGeneration();
            return true;
        }

        // 자동 재개는 없다. 손 계속 버튼의 release만 통과한다 (docs/09 §7).
        public bool Resume(int captureGeneration)
        {
            if (!paused || captureGeneration != phaseGeneration) return false;
            paused = false;
            AdvanceGeneration();
            return true;
        }

        // 프레임당 한 번. 전이하면 남은 delta를 새 타이머에 다시 적용하지 않는다.
        public bool Tick(float deltaSeconds)
        {
            if (paused || !hasTimer) return false;
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0f) return false;
            remaining -= deltaSeconds;
            if (remaining > 0f) return false;
            remaining = 0f;
            switch (state)
            {
                case RelayQuizState.WordReveal:
                case RelayQuizState.ObservePrevious:
                    EnterState(RelayQuizState.Drawing, timings.drawingSeconds);
                    return true;
                case RelayQuizState.Drawing:
                    CommitDrawing();
                    return true;
                case RelayQuizState.Guessing:
                    CommitAnswer();
                    return true;
                default:
                    hasTimer = false;
                    return false;
            }
        }

        private bool CanAct(int captureGeneration)
        {
            return !paused && captureGeneration == phaseGeneration;
        }

        // 완료 버튼과 timeout의 공통 gate. 여기서만 archive하고 턴을 올린다.
        private void CommitDrawing()
        {
            CanvasDrawingData drawing = drawingSource != null ? drawingSource() : null;
            records.Add(new RelayTurnRecord
            {
                playerIndex = playerIndex,
                drawingIndex = records.Count,
                drawing = CanvasDrawingData.DeepCopy(drawing)
            });
            playerIndex++;
            EnterState(RelayQuizState.Handover, 0f);
        }

        private void CommitAnswer()
        {
            submittedAnswer = answerSource != null ? (answerSource() ?? string.Empty) : string.Empty;
            answerCorrect = GuessJudge.IsMatch(secretWord, submittedAnswer);
            answerSubmitted = true;
            EnterState(RelayQuizState.Reveal, 0f);
        }

        // 그림·답·제시어·타이머·player index를 지운다. 단어 덱 수명은 호출자가 따로 소유한다.
        private void ResetSession()
        {
            records.Clear();
            playerIndex = 0;
            secretWord = string.Empty;
            submittedAnswer = string.Empty;
            answerCorrect = false;
            answerSubmitted = false;
            paused = false;
            hasTimer = false;
            remaining = 0f;
        }

        private void EnterState(RelayQuizState next, float duration)
        {
            state = next;
            hasTimer = duration > 0f;
            remaining = hasTimer ? duration : 0f;
            unchecked { stateSerial++; }
            AdvanceGeneration();
        }

        private void AdvanceGeneration()
        {
            unchecked { phaseGeneration++; }
            if (phaseGeneration == 0) phaseGeneration = 1;
        }
    }
}
