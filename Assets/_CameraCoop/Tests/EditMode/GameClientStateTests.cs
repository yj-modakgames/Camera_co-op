using System.Text.RegularExpressions;
using CameraCoop.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CameraCoop.Tests
{
    // docs/12 §2 — 수신 메시지 → 표시 상태 미러. 판단하지 않는다(host가 보낸 것만 반영).
    // 타이머 권위는 host — 여기서 허용된 자체 전환은 RoundIntro→Drawing과 GameEnd→Idle 둘뿐이다.
    public class GameClientStateTests
    {
        private const float GameEndSec = 8f;

        private static GameClientState New()
        {
            return new GameClientState(GameEndSec);
        }

        private static RoundBeginPayload Begin(string activeId, int round = 1, int totalRounds = 4,
            int wordLen = 2, float introSec = 3f, float durationSec = 90f)
        {
            return new RoundBeginPayload
            {
                round = round,
                totalRounds = totalRounds,
                activeId = activeId,
                wordLen = wordLen,
                introSec = introSec,
                durationSec = durationSec
            };
        }

        // ---- RoundBegin / 카운트다운 ----

        [Test]
        public void RoundBegin_EntersRoundIntro_ThenTickEntersDrawing()
        {
            GameClientState state = New();

            Assert.IsTrue(state.ApplyRoundBegin(Begin("a", round: 2, totalRounds: 8, wordLen: 3, introSec: 3f, durationSec: 90f)));

            Assert.AreEqual(GuessGameLogic.Phase.RoundIntro, state.CurrentPhase);
            Assert.AreEqual(2, state.Round);
            Assert.AreEqual(8, state.TotalRounds);
            Assert.AreEqual("a", state.ActiveId);
            Assert.AreEqual(3, state.WordLen);
            Assert.AreEqual(3f, state.RemainingSec, 1e-4f);

            Assert.IsTrue(state.Tick(3f), "RoundIntro 만료는 표시 상태 변화다");
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, state.CurrentPhase);
            Assert.AreEqual(90f, state.RemainingSec, 1e-4f, "Drawing 진입 시 durationSec으로 재장전");
        }

        [Test]
        public void RoundBegin_Turns_DrawerIsActiveId()
        {
            GameClientState state = New();
            state.ApplyGameStart(new GameStartPayload { gameId = GameMsg.GuessGameId, mode = 0 });

            state.ApplyRoundBegin(Begin("a"));

            Assert.AreEqual("a", state.CurrentDrawerId, "Turns는 출제자가 곧 그리는 사람");
        }

        [Test]
        public void Tick_Drawing_NeverLeavesDrawingOnItsOwn()
        {
            GameClientState state = New();
            state.ApplyRoundBegin(Begin("a", introSec: 0f, durationSec: 5f));
            state.Tick(0f);
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, state.CurrentPhase);

            Assert.IsFalse(state.Tick(999f), "클라 시계로 라운드를 끝내지 않는다 (타이머 권위는 host)");
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, state.CurrentPhase);
            Assert.AreEqual(0f, state.RemainingSec, 1e-4f, "표시값은 0에서 멈춘다");
        }

        // ---- WordAssign ----

        [Test]
        public void WordAssign_SetsLocalWord_AndNextRoundBeginClearsIt()
        {
            GameClientState state = New();
            state.ApplyRoundBegin(Begin("a"));

            Assert.IsTrue(state.ApplyWordAssign(new WordAssignPayload { word = "소방차" }));
            Assert.AreEqual("소방차", state.LocalWord);

            state.ApplyRoundBegin(Begin("b", round: 2));
            Assert.IsNull(state.LocalWord, "다음 라운드에 이전 제시어가 남으면 안 된다");
        }

        // ---- RelaySwap ----

        [Test]
        public void RelaySwap_UpdatesDrawerOnly()
        {
            GameClientState state = New();
            state.ApplyGameStart(new GameStartPayload { gameId = GameMsg.GuessGameId, mode = 1 });
            state.ApplyRoundBegin(Begin("a"));
            Assert.IsNull(state.CurrentDrawerId, "Relay의 첫 drawer는 host의 RelaySwap으로 온다");

            Assert.IsTrue(state.ApplyRelaySwap(new RelaySwapPayload { drawerId = "b" }));

            Assert.AreEqual("b", state.CurrentDrawerId);
            Assert.AreEqual("a", state.ActiveId, "맞히는 사람은 그대로");
        }

        // ---- GuessFeed ----

        [Test]
        public void GuessFeed_KeepsLast32()
        {
            GameClientState state = New();
            state.ApplyRoundBegin(Begin("a"));

            for (int i = 0; i < 40; i++)
            {
                state.ApplyGuessFeed(new GuessFeedPayload { playerId = "b", text = i.ToString(), correct = false });
            }

            Assert.AreEqual(32, state.Feed.Count);
            Assert.AreEqual("8", state.Feed[0].text, "오래된 것부터 버린다");
            Assert.AreEqual("39", state.Feed[31].text);
        }

        [Test]
        public void GuessFeed_ResetOnRoundBegin()
        {
            GameClientState state = New();
            state.ApplyRoundBegin(Begin("a"));
            state.ApplyGuessFeed(new GuessFeedPayload { playerId = "b", text = "오답", correct = false });
            Assert.AreEqual(1, state.Feed.Count);

            state.ApplyRoundBegin(Begin("b", round: 2));
            Assert.AreEqual(0, state.Feed.Count);
        }

        // ---- RoundEnd / GameEnd ----

        [Test]
        public void RoundEnd_ParallelArraysBecomeScores_AndEntersReveal()
        {
            GameClientState state = New();
            state.ApplyRoundBegin(Begin("a"));
            state.Tick(3f);

            Assert.IsTrue(state.ApplyRoundEnd(new RoundEndPayload
            {
                word = "사과",
                playerIds = new[] { "a", "b", "c" },
                scores = new[] { 50, 142, 0 },
                reason = (int)GuessGameLogic.RoundEndReason.AllGuessed
            }));

            Assert.AreEqual(GuessGameLogic.Phase.RoundReveal, state.CurrentPhase);
            Assert.AreEqual(3, state.Scores.Count);
            Assert.AreEqual(50, state.Scores["a"]);
            Assert.AreEqual(142, state.Scores["b"]);
            Assert.AreEqual(0, state.Scores["c"]);
            Assert.AreEqual("사과", state.LocalWord, "RoundReveal에서는 제시어가 공개된다");
        }

        [Test]
        public void RoundEnd_MismatchedArrayLengths_TruncatesAndWarns()
        {
            GameClientState state = New();
            LogAssert.Expect(LogType.Warning, new Regex("GameClientState.*점수"));

            state.ApplyRoundEnd(new RoundEndPayload
            {
                word = "사과",
                playerIds = new[] { "a", "b", "c" },
                scores = new[] { 10, 20 },
                reason = 0
            });

            Assert.AreEqual(2, state.Scores.Count, "짧은 쪽 기준으로 자른다");
            Assert.AreEqual(10, state.Scores["a"]);
            Assert.AreEqual(20, state.Scores["b"]);
        }

        [Test]
        public void GameEnd_ThenTickGameEndSec_ReturnsToIdle()
        {
            GameClientState state = New();
            state.ApplyRoundBegin(Begin("a"));

            Assert.IsTrue(state.ApplyGameEnd(new GameEndPayload
            {
                playerIds = new[] { "a", "b" },
                scores = new[] { 192, 100 }
            }));
            Assert.AreEqual(GuessGameLogic.Phase.GameEnd, state.CurrentPhase);

            Assert.IsFalse(state.Tick(GameEndSec * 0.5f), "카운트다운만으로는 표시 전이가 아니다");
            Assert.AreEqual(GuessGameLogic.Phase.GameEnd, state.CurrentPhase);

            Assert.IsTrue(state.Tick(GameEndSec));
            Assert.AreEqual(GuessGameLogic.Phase.Idle, state.CurrentPhase);
            Assert.AreEqual(192, state.Scores["a"], "최종 점수판은 Idle 복귀 후에도 남는다 (docs/14 §6-2)");
        }

        [Test]
        public void GameAbort_ImmediateIdle()
        {
            GameClientState state = New();
            state.ApplyRoundBegin(Begin("a"));

            Assert.IsTrue(state.ApplyGameAbort());
            Assert.AreEqual(GuessGameLogic.Phase.Idle, state.CurrentPhase);
            Assert.IsNull(state.ActiveId);
            Assert.IsNull(state.LocalWord);
            Assert.IsFalse(state.ApplyGameAbort(), "이미 Idle이면 표시 변화 없음");
        }

        // ---- GameStateSync (늦은 참가) ----

        [Test]
        public void GameStateSync_MirrorsStateAsSpectator_ClearedByNextRoundBegin()
        {
            GameClientState state = New();

            Assert.IsTrue(state.ApplyGameStateSync(new GameStateSyncPayload
            {
                phase = (int)GuessGameLogic.Phase.Drawing,
                gameId = GameMsg.GuessGameId,
                mode = 0,
                round = 3,
                totalRounds = 8,
                activeId = "b",
                wordLen = 3,
                remainingSec = 42.5f,
                playerIds = new[] { "a", "b" },
                scores = new[] { 100, 50 }
            }));

            Assert.IsTrue(state.Spectator);
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, state.CurrentPhase);
            Assert.AreEqual(3, state.Round);
            Assert.AreEqual(8, state.TotalRounds);
            Assert.AreEqual("b", state.ActiveId);
            Assert.AreEqual("b", state.CurrentDrawerId, "Turns면 drawer = activeId");
            Assert.AreEqual(3, state.WordLen);
            Assert.AreEqual(42.5f, state.RemainingSec, 1e-4f);
            Assert.AreEqual(100, state.Scores["a"]);
            Assert.IsNull(state.LocalWord, "관전자에게 제시어는 오지 않는다");

            state.ApplyRoundBegin(Begin("a", round: 4));
            Assert.IsFalse(state.Spectator, "다음 RoundBegin부터 정식 참가");
        }

        // ---- CanGuess ----

        [Test]
        public void CanGuess_Turns_GuesserOnlyWhileDrawing()
        {
            GameClientState state = New();
            state.ApplyGameStart(new GameStartPayload { gameId = GameMsg.GuessGameId, mode = 0 });
            state.ApplyRoundBegin(Begin("a", introSec: 1f));

            Assert.IsFalse(state.CanGuess("b"), "RoundIntro에서는 아직 불가");

            state.Tick(1f);
            Assert.IsTrue(state.CanGuess("b"));
            Assert.IsFalse(state.CanGuess("a"), "출제자 본인은 불가");
            Assert.IsFalse(state.CanGuess(null));

            state.ApplyGuessFeed(new GuessFeedPayload { playerId = "b", text = "", correct = true });
            Assert.IsFalse(state.CanGuess("b"), "이미 맞힌 사람은 불가");
            Assert.IsTrue(state.CanGuess("c"), "남은 사람은 계속 가능");
        }

        [Test]
        public void CanGuess_Relay_ActiveIdOnly()
        {
            GameClientState state = New();
            state.ApplyGameStart(new GameStartPayload { gameId = GameMsg.GuessGameId, mode = 1 });
            state.ApplyRoundBegin(Begin("a", introSec: 0f));
            state.Tick(0f);

            Assert.IsTrue(state.CanGuess("a"), "Relay는 맞히는 사람 1명만");
            Assert.IsFalse(state.CanGuess("b"));
        }

        [Test]
        public void CanGuess_Spectator_False()
        {
            GameClientState state = New();
            state.ApplyGameStateSync(new GameStateSyncPayload
            {
                phase = (int)GuessGameLogic.Phase.Drawing,
                mode = 0,
                round = 1,
                totalRounds = 4,
                activeId = "a",
                wordLen = 2,
                remainingSec = 10f,
                playerIds = new string[0],
                scores = new int[0]
            });

            Assert.IsFalse(state.CanGuess("b"), "관전자는 제출할 수 없다");
        }

        [Test]
        public void GameStart_ResetsScoresAndMode()
        {
            GameClientState state = New();
            state.ApplyRoundEnd(new RoundEndPayload
            {
                word = "사과",
                playerIds = new[] { "a" },
                scores = new[] { 999 },
                reason = 0
            });

            Assert.IsTrue(state.ApplyGameStart(new GameStartPayload { gameId = GameMsg.GuessGameId, mode = 1 }));

            Assert.AreEqual(1, state.Mode);
            Assert.AreEqual(0, state.Scores.Count, "새 게임은 점수판을 초기화한다");
            Assert.AreEqual(GuessGameLogic.Phase.Idle, state.CurrentPhase, "표시 전이는 RoundBegin이 한다");
        }
    }
}
