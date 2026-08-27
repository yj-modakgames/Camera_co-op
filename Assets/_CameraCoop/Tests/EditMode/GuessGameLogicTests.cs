using System.Collections.Generic;
using CameraCoop.Game;
using NUnit.Framework;

namespace CameraCoop.Tests
{
    // docs/12 §2 — 그림 맞추기 상태 머신(host 권위 순수 로직). 행동 규칙 표 전건 커버
    public class GuessGameLogicTests
    {
        private static readonly string[] Two = { "A", "B" };
        private static readonly string[] Three = { "A", "B", "C" };
        private static readonly string[] Four = { "A", "B", "C", "D" };

        private const string WrongAnswer = "절대아닌단어zzz";

        // 단어 8개 — 한 라운드씩 뽑아도 사이클 안에서 무중복
        private static GuessGameLogic Make(
            float introSec = 3f,
            float drawSec = 90f,
            float revealSec = 5f,
            float gameEndSec = 8f,
            float relaySwapSec = 15f)
        {
            var bank = new WordBank("사과\n바나나\n포도\n수박\n딸기\n참외\n메론\n귤", seed: 7);
            return new GuessGameLogic(bank, introSec, drawSec, revealSec, gameEndSec, relaySwapSec);
        }

        // RoundIntro 소진 → Drawing (Tick 1회당 전이 1개)
        private static void EnterDrawing(GuessGameLogic logic)
        {
            Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(1000f));
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, logic.CurrentPhase);
        }

        // ── 규칙: StartGame 거부 ────────────────────────────────────────────

        [Test]
        public void StartGame_FewerThanTwo_Rejected()
        {
            var logic = Make();
            Assert.IsFalse(logic.StartGame(new[] { "A" }, GuessGameLogic.GameMode.Turns, 2));
            Assert.IsFalse(logic.StartGame(new string[0], GuessGameLogic.GameMode.Turns, 2));
            Assert.AreEqual(GuessGameLogic.Phase.Idle, logic.CurrentPhase);
            Assert.AreEqual(0, logic.Round);
        }

        [Test]
        public void StartGame_RelayFewerThanThree_Rejected()
        {
            var logic = Make();
            Assert.IsFalse(logic.StartGame(Two, GuessGameLogic.GameMode.Relay, 2));
            Assert.AreEqual(GuessGameLogic.Phase.Idle, logic.CurrentPhase);
            Assert.IsTrue(logic.StartGame(Two, GuessGameLogic.GameMode.Turns, 2));
        }

        [Test]
        public void StartGame_AlreadyRunning_Rejected()
        {
            var logic = Make();
            Assert.IsTrue(logic.StartGame(Four, GuessGameLogic.GameMode.Turns, 2));
            Assert.IsFalse(logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1));
            Assert.AreEqual(8, logic.TotalRounds, "거부된 StartGame이 큐를 덮어쓰지 않는다");
            Assert.AreEqual(GuessGameLogic.GameMode.Turns, logic.Mode);
        }

        [Test]
        public void StartGame_NonPositiveCycles_Rejected()
        {
            var logic = Make();
            Assert.IsFalse(logic.StartGame(Four, GuessGameLogic.GameMode.Turns, 0));
            Assert.AreEqual(GuessGameLogic.Phase.Idle, logic.CurrentPhase);
        }

        [Test]
        public void StartGame_EntersRoundIntro_WithZeroScores()
        {
            var logic = Make();
            Assert.IsTrue(logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1));
            Assert.AreEqual(GuessGameLogic.Phase.RoundIntro, logic.CurrentPhase);
            Assert.AreEqual(1, logic.Round);
            Assert.AreEqual("A", logic.ActiveId);
            Assert.AreEqual(3, logic.Scores.Count);
            Assert.AreEqual(0, logic.Scores["A"]);
            Assert.AreEqual(0, logic.Scores["B"]);
            Assert.AreEqual(0, logic.Scores["C"]);
        }

        // ── 규칙: 순환 큐 ──────────────────────────────────────────────────

        [Test]
        public void Queue_FourPlayersTwoCycles_ActiveRotatesInOrder()
        {
            var logic = Make(introSec: 1f, drawSec: 10f, revealSec: 1f, gameEndSec: 1f);
            logic.StartGame(Four, GuessGameLogic.GameMode.Turns, 2);
            Assert.AreEqual(8, logic.TotalRounds);

            var seen = new List<string>();
            seen.Add(logic.ActiveId);
            for (int i = 0; i < 7; i++)
            {
                Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(1000f));
                Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.Tick(1000f));
                Assert.AreEqual(GuessGameLogic.Transition.ToRoundIntro, logic.Tick(1000f));
                seen.Add(logic.ActiveId);
            }

            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", "A", "B", "C", "D" }, seen);
            Assert.AreEqual(8, logic.Round);
            Assert.AreEqual(8, logic.TotalRounds);
        }

        // ── 규칙: 시간 전이 + 단어 + 타임아웃 사유 ─────────────────────────

        [Test]
        public void Timeline_FullChain_RoundIntroToIdle()
        {
            var logic = Make(introSec: 3f, drawSec: 90f, revealSec: 5f, gameEndSec: 8f);
            logic.StartGame(Two, GuessGameLogic.GameMode.Turns, 1); // 2라운드

            Assert.AreEqual(GuessGameLogic.Phase.RoundIntro, logic.CurrentPhase);
            Assert.AreEqual(3f, logic.PhaseRemaining, 0.001f);
            string round1Word = logic.CurrentWord;
            Assert.IsNotNull(round1Word);

            Assert.AreEqual(GuessGameLogic.Transition.None, logic.Tick(1f));
            Assert.AreEqual(2f, logic.PhaseRemaining, 0.001f);

            Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(2f));
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, logic.CurrentPhase);
            Assert.AreEqual(90f, logic.PhaseRemaining, 0.001f);
            Assert.AreEqual(logic.ActiveId, logic.CurrentDrawerId, "Turns는 CurrentDrawerId == ActiveId");

            Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.Tick(90f));
            Assert.AreEqual(GuessGameLogic.RoundEndReason.Timeout, logic.LastRoundEndReason);
            Assert.AreEqual(round1Word, logic.CurrentWord, "제시어는 RoundReveal까지 유지");
            Assert.AreEqual(5f, logic.PhaseRemaining, 0.001f);

            Assert.AreEqual(GuessGameLogic.Transition.ToRoundIntro, logic.Tick(5f));
            Assert.AreEqual(2, logic.Round);
            Assert.AreEqual("B", logic.ActiveId);
            Assert.AreNotEqual(round1Word, logic.CurrentWord, "라운드 세팅마다 words.Next()");

            Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(3f));
            Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.Tick(90f));

            Assert.AreEqual(GuessGameLogic.Transition.ToGameEnd, logic.Tick(5f));
            Assert.AreEqual(GuessGameLogic.Phase.GameEnd, logic.CurrentPhase);
            Assert.AreEqual(8f, logic.PhaseRemaining, 0.001f);

            Assert.AreEqual(GuessGameLogic.Transition.ToIdle, logic.Tick(8f));
            Assert.AreEqual(GuessGameLogic.Phase.Idle, logic.CurrentPhase);
            Assert.AreEqual(0, logic.Round);
            Assert.IsNull(logic.ActiveId);
            Assert.IsNull(logic.CurrentWord, "Idle 복귀 시 제시어 null");
            Assert.AreEqual(0f, logic.PhaseRemaining, 0.001f);

            Assert.AreEqual(GuessGameLogic.Transition.None, logic.Tick(10f), "Idle에서는 전이 없음");
            Assert.AreEqual(0f, logic.PhaseRemaining, 0.001f, "PhaseRemaining은 0 미만으로 안 내려간다");
        }

        [Test]
        public void Tick_HugeDelta_OnlyOneTransition_NoRollover()
        {
            var logic = Make(introSec: 1f, drawSec: 90f);
            logic.StartGame(Two, GuessGameLogic.GameMode.Turns, 1);

            Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(100f));
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, logic.CurrentPhase);
            Assert.AreEqual(90f, logic.PhaseRemaining, 0.001f, "잔여 시간 이월 없음");
        }

        // ── 규칙: 정답(Turns) ──────────────────────────────────────────────

        [Test]
        public void Correct_Turns_ScoresGuesserPlusRemainingAndDrawerFifty()
        {
            var logic = Make(introSec: 0f, drawSec: 42.7f);
            logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1);
            Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(0f));
            Assert.AreEqual(42.7f, logic.PhaseRemaining, 0.001f);

            Assert.AreEqual(GuessGameLogic.GuessResult.Correct, logic.SubmitGuess("B", logic.CurrentWord));
            Assert.AreEqual(142, logic.Scores["B"], "100 + (int)42.7");
            Assert.AreEqual(50, logic.Scores["A"], "출제자 보너스");
            Assert.AreEqual(0, logic.Scores["C"]);
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, logic.CurrentPhase, "아직 전원 정답 아님");
        }

        [Test]
        public void Correct_Turns_AllGuessersCorrect_EndsRoundEarly()
        {
            var logic = Make(introSec: 0f, drawSec: 90f, revealSec: 5f);
            logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1);
            logic.Tick(0f);

            Assert.AreEqual(GuessGameLogic.GuessResult.Correct, logic.SubmitGuess("B", logic.CurrentWord));
            Assert.AreEqual(GuessGameLogic.GuessResult.CorrectAndRoundEnd, logic.SubmitGuess("C", logic.CurrentWord));

            Assert.AreEqual(GuessGameLogic.Phase.RoundReveal, logic.CurrentPhase);
            Assert.AreEqual(GuessGameLogic.RoundEndReason.AllGuessed, logic.LastRoundEndReason);
            Assert.AreEqual(100, logic.Scores["A"], "출제자 50 × 정답자 2명");
            Assert.AreEqual(5f, logic.PhaseRemaining, 0.001f);
        }

        // ── 규칙: 릴레이 교대 ──────────────────────────────────────────────

        [Test]
        public void Relay_DrawerRotatesEverySwapInterval_ActiveStaysFixed()
        {
            var logic = Make(introSec: 0f, drawSec: 60f, relaySwapSec: 15f);
            logic.StartGame(Four, GuessGameLogic.GameMode.Relay, 1);
            Assert.AreEqual(GuessGameLogic.GameMode.Relay, logic.Mode);

            Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(0f));
            Assert.AreEqual("A", logic.ActiveId);
            Assert.AreEqual("B", logic.CurrentDrawerId, "첫 drawer는 Drawing 진입 시 결정");

            Assert.AreEqual(GuessGameLogic.Transition.RelaySwap, logic.Tick(15f));
            Assert.AreEqual("C", logic.CurrentDrawerId);
            Assert.AreEqual(GuessGameLogic.Transition.RelaySwap, logic.Tick(15f));
            Assert.AreEqual("D", logic.CurrentDrawerId);
            Assert.AreEqual(GuessGameLogic.Transition.RelaySwap, logic.Tick(15f));
            Assert.AreEqual("B", logic.CurrentDrawerId, "ActiveId(A)를 건너뛰고 순환");
            Assert.AreEqual("A", logic.ActiveId);

            Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.Tick(15f), "phase 만료가 교대보다 우선");
        }

        // ── 규칙: 정답(Relay) ──────────────────────────────────────────────

        [Test]
        public void Correct_Relay_OnlyActiveMaySubmit_TeamScoreForEveryone()
        {
            var logic = Make(introSec: 0f, drawSec: 30f, revealSec: 5f);
            logic.StartGame(Four, GuessGameLogic.GameMode.Relay, 1);
            logic.Tick(0f);

            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("B", logic.CurrentWord),
                "Relay에서 ActiveId가 아닌 사람");
            Assert.AreEqual(GuessGameLogic.GuessResult.CorrectAndRoundEnd, logic.SubmitGuess("A", logic.CurrentWord));

            Assert.AreEqual(130, logic.Scores["A"]);
            Assert.AreEqual(130, logic.Scores["B"]);
            Assert.AreEqual(130, logic.Scores["C"]);
            Assert.AreEqual(130, logic.Scores["D"]);
            Assert.AreEqual(GuessGameLogic.Phase.RoundReveal, logic.CurrentPhase);
            Assert.AreEqual(GuessGameLogic.RoundEndReason.AllGuessed, logic.LastRoundEndReason);
        }

        // ── 규칙: Ignored 조건 ─────────────────────────────────────────────

        [Test]
        public void SubmitGuess_IgnoredConditions()
        {
            var logic = Make(introSec: 3f, drawSec: 90f);
            logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1);

            // Phase != Drawing
            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("B", logic.CurrentWord));

            EnterDrawing(logic);
            string word = logic.CurrentWord;

            // Turns에서 ActiveId 본인
            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("A", word));
            // 미참가자
            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("Z", word));
            // 정규화 결과 빈 문자열
            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("B", "   "));
            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("B", null));
            // 정규화 길이 > 64
            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("B", new string('가', 65)));
            Assert.AreEqual(GuessGameLogic.GuessResult.Wrong, logic.SubmitGuess("B", new string('가', 64)), "64는 경계 통과");

            // 이미 맞힌 사람
            Assert.AreEqual(GuessGameLogic.GuessResult.Correct, logic.SubmitGuess("B", word));
            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("B", word));
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, logic.CurrentPhase);
        }

        // ── 규칙: 오답 ─────────────────────────────────────────────────────

        [Test]
        public void Wrong_LeavesStateUnchanged()
        {
            var logic = Make(introSec: 0f, drawSec: 90f);
            logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1);
            logic.Tick(0f);

            Assert.AreEqual(GuessGameLogic.GuessResult.Wrong, logic.SubmitGuess("B", WrongAnswer));
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, logic.CurrentPhase);
            Assert.AreEqual(90f, logic.PhaseRemaining, 0.001f);
            Assert.AreEqual(0, logic.Scores["A"]);
            Assert.AreEqual(0, logic.Scores["B"]);
            // 오답은 "이미 맞힌 사람"으로 기록되지 않는다
            Assert.AreEqual(GuessGameLogic.GuessResult.Correct, logic.SubmitGuess("B", logic.CurrentWord));
        }

        // ── 규칙: 이탈 (ActiveId) ──────────────────────────────────────────

        [Test]
        public void PlayerLeft_ActiveDuringDrawing_EndsRoundAndDropsFutureTurns()
        {
            var logic = Make(introSec: 1f, drawSec: 90f, revealSec: 1f);
            logic.StartGame(Four, GuessGameLogic.GameMode.Turns, 2);
            EnterDrawing(logic);
            Assert.AreEqual(8, logic.TotalRounds);

            Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.PlayerLeft("A"));
            Assert.AreEqual(GuessGameLogic.Phase.RoundReveal, logic.CurrentPhase);
            Assert.AreEqual(GuessGameLogic.RoundEndReason.ActiveLeft, logic.LastRoundEndReason);
            Assert.AreEqual(0, logic.Scores["A"], "무효 라운드 — 점수 없음");
            Assert.AreEqual(7, logic.TotalRounds, "큐에서 A의 미래 항목 1개 제거");

            var seen = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(GuessGameLogic.Transition.ToRoundIntro, logic.Tick(1f));
                seen.Add(logic.ActiveId);
                Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(1f));
                Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.Tick(90f));
            }
            CollectionAssert.AreEqual(new[] { "B", "C", "D", "B", "C", "D" }, seen, "이탈자는 다시 안 나온다");
            Assert.AreEqual(GuessGameLogic.Transition.ToGameEnd, logic.Tick(1f));
        }

        [Test]
        public void PlayerLeft_ActiveDuringRoundIntro_EndsRound()
        {
            var logic = Make(introSec: 3f);
            logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1);
            Assert.AreEqual(GuessGameLogic.Phase.RoundIntro, logic.CurrentPhase);

            Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.PlayerLeft("A"));
            Assert.AreEqual(GuessGameLogic.RoundEndReason.ActiveLeft, logic.LastRoundEndReason);
        }

        // ── 규칙: 이탈 (일반) ──────────────────────────────────────────────

        [Test]
        public void PlayerLeft_Bystander_OnlyRemovesFutureTurns()
        {
            var logic = Make(introSec: 1f, drawSec: 90f);
            logic.StartGame(Four, GuessGameLogic.GameMode.Turns, 2);
            EnterDrawing(logic);

            Assert.AreEqual(GuessGameLogic.Transition.None, logic.PlayerLeft("C"));
            Assert.AreEqual(6, logic.TotalRounds, "C의 미래 항목 2개 제거");
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, logic.CurrentPhase);
            Assert.AreEqual("A", logic.ActiveId);
            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("C", logic.CurrentWord));
        }

        [Test]
        public void PlayerLeft_RelayCurrentDrawer_SwapsImmediatelyAndResetsTimer()
        {
            var logic = Make(introSec: 0f, drawSec: 60f, relaySwapSec: 15f);
            logic.StartGame(Four, GuessGameLogic.GameMode.Relay, 1);
            logic.Tick(0f);
            Assert.AreEqual("B", logic.CurrentDrawerId);

            Assert.AreEqual(GuessGameLogic.Transition.RelaySwap, logic.PlayerLeft("B"));
            Assert.AreEqual("C", logic.CurrentDrawerId);

            Assert.AreEqual(GuessGameLogic.Transition.None, logic.Tick(14f), "교대 타이머 리셋");
            Assert.AreEqual("C", logic.CurrentDrawerId);
            Assert.AreEqual(GuessGameLogic.Transition.RelaySwap, logic.Tick(1f));
            Assert.AreEqual("D", logic.CurrentDrawerId);
        }

        [Test]
        public void PlayerLeft_RelayNonDrawer_KeepsDrawer()
        {
            var logic = Make(introSec: 0f, drawSec: 60f, relaySwapSec: 15f);
            logic.StartGame(Four, GuessGameLogic.GameMode.Relay, 1);
            logic.Tick(0f);
            Assert.AreEqual("B", logic.CurrentDrawerId);

            Assert.AreEqual(GuessGameLogic.Transition.None, logic.PlayerLeft("D"));
            Assert.AreEqual("B", logic.CurrentDrawerId);
            Assert.AreEqual(GuessGameLogic.Transition.RelaySwap, logic.Tick(15f));
            Assert.AreEqual("C", logic.CurrentDrawerId, "이탈자는 순환에서 제외");
            Assert.AreEqual(GuessGameLogic.Transition.RelaySwap, logic.Tick(15f));
            Assert.AreEqual("B", logic.CurrentDrawerId);
        }

        // ── 규칙: 이탈로 인원 < 2 ──────────────────────────────────────────

        [Test]
        public void PlayerLeft_BelowTwoPlayers_GoesIdle()
        {
            var logic = Make(introSec: 0f, drawSec: 90f);
            logic.StartGame(Two, GuessGameLogic.GameMode.Turns, 1);
            logic.Tick(0f);
            logic.SubmitGuess("B", logic.CurrentWord);

            Assert.AreEqual(GuessGameLogic.Transition.ToIdle, logic.PlayerLeft("B"));
            Assert.AreEqual(GuessGameLogic.Phase.Idle, logic.CurrentPhase);
            Assert.AreEqual(0, logic.Round);
            Assert.AreEqual(0, logic.TotalRounds);
            Assert.IsNull(logic.ActiveId);
            Assert.IsNull(logic.CurrentWord);
            Assert.IsTrue(logic.Scores.ContainsKey("B"), "이탈자 점수 항목 보존");
        }

        [Test]
        public void PlayerLeft_ActiveInTwoPlayerGame_GoesIdleNotRoundReveal()
        {
            var logic = Make(introSec: 0f);
            logic.StartGame(Two, GuessGameLogic.GameMode.Turns, 1);
            logic.Tick(0f);

            Assert.AreEqual(GuessGameLogic.Transition.ToIdle, logic.PlayerLeft("A"));
            Assert.AreEqual(GuessGameLogic.Phase.Idle, logic.CurrentPhase);
        }

        // ── 규칙: 점수 보존 ────────────────────────────────────────────────

        [Test]
        public void PlayerLeft_KeepsScoreEntry()
        {
            var logic = Make(introSec: 0f, drawSec: 90f);
            logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1);
            logic.Tick(0f);
            Assert.AreEqual(GuessGameLogic.GuessResult.Correct, logic.SubmitGuess("B", logic.CurrentWord));
            int scoreBefore = logic.Scores["B"];
            Assert.AreEqual(190, scoreBefore);

            Assert.AreEqual(GuessGameLogic.Transition.None, logic.PlayerLeft("B"));
            Assert.AreEqual(scoreBefore, logic.Scores["B"], "이탈자 점수는 지우지 않는다");
        }

        // ── 규칙: 늦은 참가 ────────────────────────────────────────────────

        [Test]
        public void AddPlayer_Idle_Ignored()
        {
            var logic = Make();
            logic.AddPlayer("X");
            Assert.AreEqual(0, logic.Scores.Count);
            Assert.AreEqual(GuessGameLogic.Phase.Idle, logic.CurrentPhase);
        }

        [Test]
        public void AddPlayer_MidGame_EligibleFromNextRound_AppendedToQueueEnd()
        {
            var logic = Make(introSec: 0f, drawSec: 90f, revealSec: 0f);
            logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1);
            Assert.AreEqual(3, logic.TotalRounds);
            logic.Tick(0f); // Drawing (라운드 1, 출제자 A)

            logic.AddPlayer("D");
            Assert.AreEqual(0, logic.Scores["D"]);
            Assert.AreEqual(4, logic.TotalRounds, "큐 끝에 1회 추가");
            Assert.AreEqual(GuessGameLogic.GuessResult.Ignored, logic.SubmitGuess("D", logic.CurrentWord),
                "참가 당 라운드는 정답 자격 없음");

            // 라운드 2 (출제자 B)
            Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.Tick(90f));
            Assert.AreEqual(GuessGameLogic.Transition.ToRoundIntro, logic.Tick(0f));
            Assert.AreEqual(2, logic.Round);
            Assert.AreEqual("B", logic.ActiveId);
            Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(0f));

            Assert.AreEqual(GuessGameLogic.GuessResult.Wrong, logic.SubmitGuess("D", WrongAnswer));
            Assert.AreEqual(GuessGameLogic.GuessResult.Correct, logic.SubmitGuess("D", logic.CurrentWord));

            // 라운드 3(C) → 라운드 4(D): 큐 끝에 등장
            Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.Tick(90f));
            Assert.AreEqual(GuessGameLogic.Transition.ToRoundIntro, logic.Tick(0f));
            Assert.AreEqual("C", logic.ActiveId);
            Assert.AreEqual(GuessGameLogic.Transition.ToDrawing, logic.Tick(0f));
            Assert.AreEqual(GuessGameLogic.Transition.ToRoundReveal, logic.Tick(90f));
            Assert.AreEqual(GuessGameLogic.Transition.ToRoundIntro, logic.Tick(0f));
            Assert.AreEqual(4, logic.Round);
            Assert.AreEqual("D", logic.ActiveId);
        }

        // ── 인터페이스: Abort ──────────────────────────────────────────────

        [Test]
        public void Abort_FromDrawing_ResetsToIdle()
        {
            var logic = Make(introSec: 0f, drawSec: 90f);
            logic.StartGame(Four, GuessGameLogic.GameMode.Turns, 2);
            logic.Tick(0f);

            logic.Abort();
            Assert.AreEqual(GuessGameLogic.Phase.Idle, logic.CurrentPhase);
            Assert.AreEqual(0, logic.Round);
            Assert.AreEqual(0, logic.TotalRounds);
            Assert.IsNull(logic.ActiveId);
            Assert.IsNull(logic.CurrentWord);
            Assert.AreEqual(GuessGameLogic.Transition.None, logic.Tick(1000f));
            Assert.IsTrue(logic.StartGame(Three, GuessGameLogic.GameMode.Turns, 1), "Abort 후 재시작 가능");
        }
    }
}
