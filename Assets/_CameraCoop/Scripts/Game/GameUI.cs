using System.Collections.Generic;
using System.Text;
using CameraCoop.Netplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CameraCoop.Game
{
    // 게임 UI (docs/12 §4). 로직 없음 — 표시와 입력만. 판정·전이는 GameSession/GuessGameLogic이 전부 결정한다.
    // legacy uGUI (TMP 미사용). Refresh()의 StringBuilder 패턴은 NetplayUI.Refresh를 따른다.
    public class GameUI : MonoBehaviour
    {
        [SerializeField] private GameSession gameSession;
        [SerializeField] private NetSession netSession;
        [SerializeField] private Text bannerText;       // 상단 중앙: 라운드/안내
        [SerializeField] private Text timerText;        // 상단 중앙: 남은 초
        [SerializeField] private Text wordText;         // 상단 중앙: 제시어(출제자) / 글자수 힌트(게서)
        [SerializeField] private Text scoreboardText;   // 우측: 점수판
        [SerializeField] private Text feedText;         // 우측: 최근 정답 피드(최근 6줄)
        [SerializeField] private InputField guessInput; // 하단: 정답 입력창
        [SerializeField] private Button startTurnsButton;  // host 전용: 게임 시작(기본)
        [SerializeField] private Button startRelayButton;  // host 전용: 게임 시작(릴레이)

        // Scoreboard/최종 순위 조립용 스크래치 버퍼 — 매 Refresh마다 새로 만들지 않는다
        private readonly List<KeyValuePair<string, int>> scoreRows = new List<KeyValuePair<string, int>>(4);

        private float timerAccum; // 0.1초 간격 타이머 텍스트 갱신용 누적자

        private void OnEnable()
        {
            if (gameSession != null)
            {
                gameSession.OnStateChanged += Refresh;
            }
            if (netSession != null)
            {
                netSession.OnPlayersChanged += Refresh;
            }
            if (guessInput != null)
            {
                guessInput.onEndEdit.AddListener(HandleGuessSubmit);
            }
            Refresh();
        }

        private void OnDisable()
        {
            if (gameSession != null)
            {
                gameSession.OnStateChanged -= Refresh;
            }
            if (netSession != null)
            {
                netSession.OnPlayersChanged -= Refresh;
            }
            if (guessInput != null)
            {
                guessInput.onEndEdit.RemoveListener(HandleGuessSubmit);
            }
            // 리뷰 요구사항(Task 4 리뷰): GameUI가 꺼져도 타이핑 게이트가 열린 채 남으면
            // WASD·C키가 게임이 끝날 때까지 죽는다 — 단일 출처를 여기서도 확실히 되돌린다
            InputFocus.IsTyping = false;
        }

        private void Update()
        {
            // ① 단일 출처 폴링 — 이벤트 기반이면 포커스 상실을 놓칠 수 있다 (docs/12 §4 Step 2)
            InputFocus.IsTyping = guessInput != null && guessInput.isFocused;

            // ② 타이머 텍스트는 0.1초 간격으로만 갱신 (매 프레임 문자열 할당 방지)
            timerAccum += Time.deltaTime;
            if (timerAccum >= 0.1f)
            {
                timerAccum = 0f;
                UpdateTimerText();
            }

            // ③ Enter로 입력창 포커스 (정답 자격이 있을 때만)
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && guessInput != null && gameSession != null && netSession != null
                && keyboard.enterKey.wasPressedThisFrame && !guessInput.isFocused
                && gameSession.State.CanGuess(netSession.LocalPlayerId))
            {
                guessInput.ActivateInputField();
            }
        }

        // Enter(메인/넘패드)로 끝난 onEndEdit만 제출로 취급 — 포커스만 잃은 onEndEdit는 제출이 아니다
        private void HandleGuessSubmit(string text)
        {
            if (guessInput == null)
            {
                return;
            }
            Keyboard keyboard = Keyboard.current;
            bool submitted = keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
            if (!submitted)
            {
                return;
            }
            if (gameSession != null)
            {
                gameSession.SubmitGuess(text);
            }
            guessInput.text = string.Empty;
            guessInput.ActivateInputField(); // 재포커스 — 연속 입력 편의
        }

        private void Refresh()
        {
            if (gameSession == null)
            {
                return;
            }
            GameClientState state = gameSession.State;

            RefreshStartButtons();
            RefreshBanner(state);
            RefreshWord(state);
            RefreshScoreboard(state);
            RefreshFeed(state);
            UpdateTimerText(); // 상태 변화 즉시 반영 — Update()의 0.1초 폴링을 기다리지 않는다

            bool canGuess = netSession != null && state.CanGuess(netSession.LocalPlayerId);
            SetGuessInputInteractable(canGuess);
        }

        // CanStartGame(mode)일 때만 표시+interactable (세션 중 + host + 인원 충족 — Relay는 3인 이상, docs/12 §1)
        private void RefreshStartButtons()
        {
            if (startTurnsButton != null)
            {
                bool canTurns = gameSession.CanStartGame((int)GuessGameLogic.GameMode.Turns);
                startTurnsButton.gameObject.SetActive(canTurns);
                startTurnsButton.interactable = canTurns;
            }
            if (startRelayButton != null)
            {
                bool canRelay = gameSession.CanStartGame((int)GuessGameLogic.GameMode.Relay);
                startRelayButton.gameObject.SetActive(canRelay);
                startRelayButton.interactable = canRelay;
            }
        }

        private void RefreshBanner(GameClientState state)
        {
            if (bannerText == null)
            {
                return;
            }
            switch (state.CurrentPhase)
            {
                case GuessGameLogic.Phase.RoundIntro:
                case GuessGameLogic.Phase.Drawing:
                    bannerText.text = BuildRoundBannerText(state);
                    break;
                case GuessGameLogic.Phase.RoundReveal:
                    // 리뷰 요구사항(Task 5 리뷰 Minor): RoundReveal은 RemainingSec==0 — 타이머 대신 정답만 배너에 표시
                    bannerText.text = "정답: " + (state.LocalWord ?? "?");
                    break;
                case GuessGameLogic.Phase.GameEnd:
                    bannerText.text = BuildFinalRankingText(state);
                    break;
                default: // Idle
                    bannerText.text = string.Empty;
                    break;
            }
        }

        private string BuildRoundBannerText(GameClientState state)
        {
            string drawerId = state.CurrentDrawerId;
            var sb = new StringBuilder();
            sb.Append("라운드 ").Append(state.Round).Append('/').Append(state.TotalRounds).Append(" — ");
            if (string.IsNullOrEmpty(drawerId))
            {
                // 리뷰 요구사항(Task 5 리뷰 Minor): Relay 늦은 참가자는 RelaySwap을 받기 전까지 drawerId가 null일 수 있다
                sb.Append("그림 그리는 사람을 확인하는 중");
            }
            else
            {
                sb.Append(GetPlayerName(drawerId)).Append("님이 그립니다");
            }
            return sb.ToString();
        }

        // 내가 받은 제시어가 있으면 그대로, 없으면 글자수 힌트(◯×WordLen). RoundReveal에서는 host가 보낸 정답이 곧 LocalWord다
        private void RefreshWord(GameClientState state)
        {
            if (wordText == null)
            {
                return;
            }
            if (state.CurrentPhase == GuessGameLogic.Phase.Idle || state.CurrentPhase == GuessGameLogic.Phase.GameEnd)
            {
                wordText.text = string.Empty;
                return;
            }
            wordText.text = state.LocalWord != null
                ? state.LocalWord
                : new string('◯', state.WordLen > 0 ? state.WordLen : 0); // ◯
        }

        private void RefreshScoreboard(GameClientState state)
        {
            if (scoreboardText == null)
            {
                return;
            }
            CollectSortedScores(state);
            var sb = new StringBuilder();
            for (int i = 0; i < scoreRows.Count; i++)
            {
                sb.Append(GetPlayerName(scoreRows[i].Key)).Append(" : ").Append(scoreRows[i].Value).AppendLine();
            }
            scoreboardText.text = sb.ToString();
        }

        // 최근 6줄만 (Feed는 오래된 것이 앞 — 최신 6개는 뒤쪽)
        private void RefreshFeed(GameClientState state)
        {
            if (feedText == null)
            {
                return;
            }
            IReadOnlyList<GuessFeedPayload> feed = state.Feed;
            int start = feed.Count > 6 ? feed.Count - 6 : 0;
            var sb = new StringBuilder();
            for (int i = start; i < feed.Count; i++)
            {
                GuessFeedPayload item = feed[i];
                sb.Append(GetPlayerName(item.playerId)).Append(" : ").Append(item.correct ? "정답!" : item.text).AppendLine();
            }
            feedText.text = sb.ToString();
        }

        private void UpdateTimerText()
        {
            if (timerText == null || gameSession == null)
            {
                return;
            }
            GuessGameLogic.Phase phase = gameSession.State.CurrentPhase;
            if (phase != GuessGameLogic.Phase.RoundIntro && phase != GuessGameLogic.Phase.Drawing)
            {
                // RoundReveal은 RemainingSec==0이라 감춘다(리뷰 요구사항). Idle·GameEnd는 라운드 타이머가 아니라 표시 안 함
                timerText.text = string.Empty;
                return;
            }
            timerText.text = Mathf.CeilToInt(gameSession.State.RemainingSec).ToString();
        }

        private string BuildFinalRankingText(GameClientState state)
        {
            CollectSortedScores(state);
            var sb = new StringBuilder();
            sb.AppendLine("게임 종료! 최종 순위");
            for (int i = 0; i < scoreRows.Count; i++)
            {
                sb.Append(i + 1).Append("위 ").Append(GetPlayerName(scoreRows[i].Key))
                    .Append(" (").Append(scoreRows[i].Value).Append(')').AppendLine();
            }
            return sb.ToString();
        }

        private void CollectSortedScores(GameClientState state)
        {
            scoreRows.Clear();
            foreach (KeyValuePair<string, int> pair in state.Scores)
            {
                scoreRows.Add(pair);
            }
            scoreRows.Sort(CompareScoreDescending);
        }

        private static int CompareScoreDescending(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
        {
            return b.Value.CompareTo(a.Value);
        }

        // 플레이어 이름 조회는 netSession.Players에서 — 없으면 id 그대로 표시 (이탈자, docs/12 Task6 리뷰 요구사항)
        private string GetPlayerName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                return playerId;
            }
            PlayerInfo info;
            if (netSession != null && netSession.Players.TryGetValue(playerId, out info))
            {
                return info.name;
            }
            return playerId;
        }

        // interactable이 false로 바뀌는 순간에만 명시적으로 처리 — 매 Refresh마다 반복 호출하지 않는다
        private void SetGuessInputInteractable(bool canGuess)
        {
            if (guessInput == null)
            {
                return;
            }
            if (guessInput.interactable == canGuess)
            {
                return;
            }
            guessInput.interactable = canGuess;
            if (!canGuess)
            {
                // 리뷰 요구사항(Task 4 리뷰): interactable=false만으로는 legacy InputField의 isFocused가 안 풀릴 수 있다
                // → DeactivateInputField()를 명시 호출해 폴링(IsTyping)이 계속 true를 쓰는 사고를 막는다
                if (guessInput.isFocused)
                {
                    guessInput.DeactivateInputField();
                }
                InputFocus.IsTyping = false;
            }
        }
    }
}
