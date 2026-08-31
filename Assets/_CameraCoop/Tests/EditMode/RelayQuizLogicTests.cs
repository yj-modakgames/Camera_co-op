using System;
using System.Collections.Generic;
using System.Reflection;
using CameraCoop;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/09 §4~5·§8~9의 순수 상태·중복 전이·정답 판정 회귀.
    // 실제 손·IME·화면 차폐는 사용자 Play 항목이며 여기서 대체하지 않는다.
    public class RelayQuizLogicTests
    {
        private const string WordAssetPath = "Assets/_CameraCoop/Data/RelayQuizWords.asset";

        private sealed class Harness
        {
            public readonly RelayQuizLogic Logic;
            public int WordCalls;
            public int DrawingCalls;
            public int AnswerCalls;
            public string NextWord = "사과";
            public string NextAnswer = string.Empty;
            public CanvasDrawingData NextDrawing;

            public Harness(RelayQuizTimings timings = null)
            {
                Logic = new RelayQuizLogic(timings ?? new RelayQuizTimings(), TakeWord, TakeDrawing, TakeAnswer);
            }

            private string TakeWord() { WordCalls++; return NextWord; }
            private CanvasDrawingData TakeDrawing() { DrawingCalls++; return NextDrawing; }
            private string TakeAnswer() { AnswerCalls++; return NextAnswer; }

            public int Generation { get { return Logic.PhaseGeneration; } }

            public void Begin(int players)
            {
                Assert.IsTrue(Logic.SetPlayerCount(players, Generation));
                Assert.IsTrue(Logic.StartGame(Generation));
            }

            public void Ready() { Assert.IsTrue(Logic.ConfirmReady(Generation)); }

            public void Expire(float seconds) { Logic.Tick(seconds); }

            // Handover → (제시어·관찰) → Drawing까지 진행하고 완료 버튼으로 턴을 닫는다.
            public void PlayDrawingTurn()
            {
                Ready();
                Assert.IsTrue(Logic.State == RelayQuizState.WordReveal || Logic.State == RelayQuizState.ObservePrevious,
                    "그리기 전 단계는 WordReveal 또는 ObservePrevious여야 합니다. 실제: " + Logic.State);
                Expire(999f);
                Assert.AreEqual(RelayQuizState.Drawing, Logic.State);
                Assert.IsTrue(Logic.CompleteDrawing(Generation));
            }
        }

        private static CanvasDrawingData MakeDrawing(float x)
        {
            return new CanvasDrawingData
            {
                version = 1,
                strokes = new[]
                {
                    new CanvasStrokeData
                    {
                        strokeId = 1,
                        order = 0,
                        xy = new[] { x, 0.2f, x + 0.1f, 0.2f },
                        colorArgb = unchecked((int)0xFF112233),
                        widthNormalized = 0.02f,
                        brushId = 0
                    }
                }
            };
        }

        // ---- 인원별 상태 순서와 N-1 archive (docs/09 §12) ----

        [Test]
        public void TwoPlayers_SkipObserveAndArchiveOneDrawing()
        {
            var harness = new Harness();
            harness.Begin(2);
            Assert.AreEqual(RelayQuizState.Handover, harness.Logic.State);
            Assert.AreEqual(0, harness.Logic.PlayerIndex);

            harness.Ready();
            Assert.AreEqual(RelayQuizState.WordReveal, harness.Logic.State);
            harness.Expire(5f);
            Assert.AreEqual(RelayQuizState.Drawing, harness.Logic.State);
            harness.Logic.CompleteDrawing(harness.Generation);

            Assert.AreEqual(RelayQuizState.Handover, harness.Logic.State);
            Assert.AreEqual(1, harness.Logic.PlayerIndex);
            Assert.AreEqual(1, harness.Logic.Records.Count);

            harness.Ready();
            Assert.AreEqual(RelayQuizState.Guessing, harness.Logic.State, "2인은 중간 관찰·재그리기를 건너뛴다.");
        }

        [Test]
        public void ThreePlayers_MiddlePlayerObservesThenRedraws()
        {
            var harness = new Harness();
            harness.Begin(3);
            harness.PlayDrawingTurn();

            harness.Ready();
            Assert.AreEqual(RelayQuizState.ObservePrevious, harness.Logic.State);
            harness.Expire(5f);
            Assert.AreEqual(RelayQuizState.Drawing, harness.Logic.State);
            harness.Logic.CompleteDrawing(harness.Generation);

            Assert.AreEqual(2, harness.Logic.Records.Count);
            harness.Ready();
            Assert.AreEqual(RelayQuizState.Guessing, harness.Logic.State);
        }

        [Test]
        public void FourPlayers_ArchiveThreeDrawingsInAuthorOrder()
        {
            var harness = new Harness();
            harness.Begin(4);
            harness.PlayDrawingTurn();
            harness.PlayDrawingTurn();
            harness.PlayDrawingTurn();

            IReadOnlyList<RelayTurnRecord> records = harness.Logic.Records;
            Assert.AreEqual(3, records.Count);
            for (int i = 0; i < records.Count; i++)
            {
                Assert.AreEqual(i, records[i].playerIndex);
                Assert.AreEqual(i, records[i].drawingIndex);
            }
            harness.Ready();
            Assert.AreEqual(RelayQuizState.Guessing, harness.Logic.State);
            Assert.AreEqual(3, harness.Logic.PlayerIndex);
        }

        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 3)]
        public void RecordCount_IsExactlyPlayersMinusOne(int players, int expected)
        {
            var harness = new Harness();
            harness.Begin(players);
            for (int i = 0; i < expected; i++) harness.PlayDrawingTurn();
            harness.Ready();
            harness.Logic.SubmitAnswer(harness.Generation);
            Assert.AreEqual(RelayQuizState.Reveal, harness.Logic.State);
            Assert.AreEqual(expected, harness.Logic.Records.Count);
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(0)]
        public void SetPlayerCount_RejectsOutOfRange(int players)
        {
            var harness = new Harness();
            Assert.IsFalse(harness.Logic.SetPlayerCount(players, harness.Generation));
            Assert.AreEqual(2, harness.Logic.PlayerCount);
        }

        // ---- 타이머와 중복 전이 ----

        [Test]
        public void Tick_DoesNotCarryLeftoverDeltaIntoNextTimer()
        {
            var harness = new Harness();
            harness.Begin(2);
            harness.Ready();
            harness.Expire(7f);   // WordReveal 5초를 2초 초과
            Assert.AreEqual(RelayQuizState.Drawing, harness.Logic.State);
            Assert.AreEqual(60f, harness.Logic.RemainingSeconds, 0.0001f);
        }

        [Test]
        public void DrawingTimeout_ArchivesOnce()
        {
            var harness = new Harness();
            harness.Begin(2);
            harness.Ready();
            harness.Expire(5f);
            harness.Expire(60f);
            Assert.AreEqual(RelayQuizState.Handover, harness.Logic.State);
            Assert.AreEqual(1, harness.Logic.Records.Count);
            Assert.AreEqual(1, harness.DrawingCalls);
        }

        [Test]
        public void CompleteButtonAndTimeout_SameFrame_ArchiveAndAdvanceOnce()
        {
            var harness = new Harness();
            harness.Begin(2);
            harness.Ready();
            harness.Expire(5f);
            int generation = harness.Generation;

            Assert.IsTrue(harness.Logic.CompleteDrawing(generation));
            // 같은 프레임의 timeout과 두 번째 완료는 모두 폐기된다.
            Assert.IsFalse(harness.Logic.CompleteDrawing(generation));
            harness.Expire(60f);

            Assert.AreEqual(1, harness.Logic.Records.Count);
            Assert.AreEqual(1, harness.DrawingCalls);
            Assert.AreEqual(1, harness.Logic.PlayerIndex);
        }

        [Test]
        public void StaleGeneration_ActionIsDiscarded()
        {
            var harness = new Harness();
            harness.Begin(2);
            int stale = harness.Generation;
            harness.Ready();
            Assert.AreNotEqual(stale, harness.Generation);

            Assert.IsFalse(harness.Logic.ConfirmReady(stale), "이전 화면의 늦은 release는 폐기된다.");
            Assert.AreEqual(RelayQuizState.WordReveal, harness.Logic.State);
        }

        [Test]
        public void DoubleReady_OnlyFirstIsAccepted()
        {
            var harness = new Harness();
            harness.Begin(3);
            int generation = harness.Generation;
            Assert.IsTrue(harness.Logic.ConfirmReady(generation));
            Assert.IsFalse(harness.Logic.ConfirmReady(generation));
            Assert.AreEqual(RelayQuizState.WordReveal, harness.Logic.State);
        }

        [Test]
        public void PhaseGeneration_ChangesOnEveryTransitionAndPause()
        {
            var harness = new Harness();
            var seen = new HashSet<int> { harness.Generation };
            harness.Begin(2);
            Assert.IsTrue(seen.Add(harness.Generation));
            harness.Ready();
            Assert.IsTrue(seen.Add(harness.Generation));
            harness.Logic.RequestPause();
            Assert.IsTrue(seen.Add(harness.Generation), "pause 경계도 세대를 갱신한다.");
            harness.Logic.Resume(harness.Generation);
            Assert.IsTrue(seen.Add(harness.Generation));
        }

        [Test]
        public void UntimedState_TickDoesNothing()
        {
            var harness = new Harness();
            harness.Begin(2);
            int serial = harness.Logic.StateSerial;
            Assert.IsFalse(harness.Logic.Tick(600f));
            Assert.AreEqual(RelayQuizState.Handover, harness.Logic.State);
            Assert.AreEqual(serial, harness.Logic.StateSerial);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Tick_RejectsNonPositiveOrNonFiniteDelta(float delta)
        {
            var harness = new Harness();
            harness.Begin(2);
            harness.Ready();
            Assert.IsFalse(harness.Logic.Tick(delta));
            Assert.AreEqual(5f, harness.Logic.RemainingSeconds, 0.0001f);
        }

        [Test]
        public void InvalidTimings_AreRejected()
        {
            var timings = new RelayQuizTimings { drawingSeconds = 0f };
            Assert.Throws<ArgumentException>(() => new RelayQuizLogic(timings, null, null, null));
        }

        // ---- 자동 pause 정책 (docs/09 §7) ----

        // Setup은 캠을 켜기 전 화면이다. 여기서 차폐하면 복구 조건(유효한 손)을 영영 못 채운다.
        [TestCase(RelayQuizState.Setup, false, false, false)]
        [TestCase(RelayQuizState.Setup, true, false, false)]
        [TestCase(RelayQuizState.Handover, false, true, true)]
        [TestCase(RelayQuizState.WordReveal, false, true, true)]
        [TestCase(RelayQuizState.Drawing, false, true, true)]
        [TestCase(RelayQuizState.Guessing, false, true, true)]
        [TestCase(RelayQuizState.Reveal, false, true, true)]
        [TestCase(RelayQuizState.Gallery, false, true, true)]
        public void ShouldAutoPause_FocusLossPausesEveryStateExceptSetup(
            RelayQuizState state, bool hasFocus, bool hasFreshHand, bool expected)
        {
            Assert.AreEqual(expected, RelayQuizLogic.ShouldAutoPause(state, hasFocus, hasFreshHand));
        }

        // 손 추적 상실만을 원인으로 하는 자동 pause는 Drawing에만 적용한다.
        [TestCase(RelayQuizState.Drawing, true)]
        [TestCase(RelayQuizState.WordReveal, false)]
        [TestCase(RelayQuizState.ObservePrevious, false)]
        [TestCase(RelayQuizState.Guessing, false)]
        [TestCase(RelayQuizState.Handover, false)]
        [TestCase(RelayQuizState.Gallery, false)]
        [TestCase(RelayQuizState.Setup, false)]
        public void ShouldAutoPause_MissingHandPausesOnlyWhileDrawing(RelayQuizState state, bool expected)
        {
            Assert.AreEqual(expected, RelayQuizLogic.ShouldAutoPause(state, hasFocus: true, hasFreshHand: false));
        }

        [Test]
        public void ShouldAutoPause_NeverPausesWhenFocusedWithAFreshHand()
        {
            foreach (RelayQuizState state in System.Enum.GetValues(typeof(RelayQuizState)))
            {
                Assert.IsFalse(RelayQuizLogic.ShouldAutoPause(state, hasFocus: true, hasFreshHand: true), state.ToString());
            }
        }

        // ---- pause와 수동 resume (docs/09 §7) ----

        [Test]
        public void Pause_PreservesStateAndRemainingTime()
        {
            var harness = new Harness();
            harness.Begin(2);
            harness.Ready();
            harness.Expire(5f);
            harness.Expire(10f);
            float remaining = harness.Logic.RemainingSeconds;

            Assert.IsTrue(harness.Logic.RequestPause());
            Assert.IsTrue(harness.Logic.Paused);
            harness.Expire(30f);

            Assert.AreEqual(RelayQuizState.Drawing, harness.Logic.State);
            Assert.AreEqual(remaining, harness.Logic.RemainingSeconds, 0.0001f);
            Assert.AreEqual(0, harness.Logic.Records.Count, "pause는 archive를 일으키지 않는다.");
            Assert.AreEqual(0, harness.Logic.PlayerIndex);
        }

        [Test]
        public void Pause_BlocksEveryActionExceptResume()
        {
            var harness = new Harness();
            harness.Begin(2);
            harness.Ready();
            harness.Expire(5f);
            harness.Logic.RequestPause();
            int generation = harness.Generation;

            Assert.IsFalse(harness.Logic.CompleteDrawing(generation));
            Assert.IsFalse(harness.Logic.ConfirmReady(generation));
            Assert.IsFalse(harness.Logic.StartGame(generation));
            Assert.AreEqual(0, harness.Logic.Records.Count);

            Assert.IsTrue(harness.Logic.Resume(generation));
            Assert.IsFalse(harness.Logic.Paused);
        }

        [Test]
        public void Resume_RejectsStaleGenerationAndDoesNotAutoResume()
        {
            var harness = new Harness();
            harness.Begin(2);
            int stale = harness.Generation;
            harness.Logic.RequestPause();

            Assert.IsFalse(harness.Logic.Resume(stale));
            Assert.IsTrue(harness.Logic.Paused, "자동 재개는 없다.");
            Assert.IsTrue(harness.Logic.Resume(harness.Generation));
        }

        [Test]
        public void Resume_RestartsTimerFromPreservedRemaining()
        {
            var harness = new Harness();
            harness.Begin(2);
            harness.Ready();
            harness.Expire(2f);
            harness.Logic.RequestPause();
            harness.Logic.Resume(harness.Generation);

            Assert.AreEqual(3f, harness.Logic.RemainingSeconds, 0.0001f);
            harness.Expire(3f);
            Assert.AreEqual(RelayQuizState.Drawing, harness.Logic.State);
        }

        [Test]
        public void RequestPause_IsIdempotent()
        {
            var harness = new Harness();
            harness.Begin(2);
            Assert.IsTrue(harness.Logic.RequestPause());
            int generation = harness.Generation;
            Assert.IsFalse(harness.Logic.RequestPause());
            Assert.AreEqual(generation, harness.Generation);
        }

        // ---- 정답 판정 (docs/09 §9) ----

        [Test]
        public void EmptyAnswer_IsWrong()
        {
            var harness = new Harness();
            harness.NextWord = "사과";
            harness.NextAnswer = "   ";
            harness.Begin(2);
            harness.PlayDrawingTurn();
            harness.Ready();
            harness.Logic.SubmitAnswer(harness.Generation);

            Assert.AreEqual(RelayQuizState.Reveal, harness.Logic.State);
            Assert.IsFalse(harness.Logic.AnswerCorrect);
            Assert.IsTrue(harness.Logic.AnswerSubmitted);
        }

        [TestCase("사과", "사과", true)]
        [TestCase("사과", " 사 과 ", true)]
        [TestCase("Robot", "  robot ", true)]
        [TestCase("사과", "사과나무", false)]
        [TestCase("사과", "", false)]
        public void Answer_UsesGuessJudgeNormalization(string word, string guess, bool correct)
        {
            var harness = new Harness();
            harness.NextWord = word;
            harness.NextAnswer = guess;
            harness.Begin(2);
            harness.PlayDrawingTurn();
            harness.Ready();
            harness.Logic.SubmitAnswer(harness.Generation);

            Assert.AreEqual(correct, harness.Logic.AnswerCorrect);
            Assert.AreEqual(guess, harness.Logic.SubmittedAnswer);
        }

        [Test]
        public void GuessTimeout_JudgesConfirmedStringOnce()
        {
            var harness = new Harness();
            harness.NextWord = "로봇";
            harness.NextAnswer = "로봇";
            harness.Begin(2);
            harness.PlayDrawingTurn();
            harness.Ready();
            Assert.AreEqual(RelayQuizState.Guessing, harness.Logic.State);

            harness.Expire(30f);
            Assert.AreEqual(RelayQuizState.Reveal, harness.Logic.State);
            Assert.IsTrue(harness.Logic.AnswerCorrect);
            Assert.AreEqual(1, harness.AnswerCalls);
        }

        [Test]
        public void SubmitThenTimeout_JudgesOnce()
        {
            var harness = new Harness();
            harness.Begin(2);
            harness.PlayDrawingTurn();
            harness.Ready();
            int generation = harness.Generation;

            Assert.IsTrue(harness.Logic.SubmitAnswer(generation));
            Assert.IsFalse(harness.Logic.SubmitAnswer(generation));
            harness.Expire(30f);
            Assert.AreEqual(1, harness.AnswerCalls);
        }

        // ---- 제시어 비공개 (docs/09 §2·§8) ----

        [Test]
        public void Word_IsVisibleOnlyInWordRevealAndReveal()
        {
            var harness = new Harness();
            harness.NextWord = "우산";
            harness.Begin(3);
            Assert.AreEqual(string.Empty, harness.Logic.VisibleWord, "Handover에서는 제시어를 노출하지 않는다.");

            harness.Ready();
            Assert.AreEqual("우산", harness.Logic.VisibleWord);
            harness.Expire(5f);
            Assert.AreEqual(string.Empty, harness.Logic.VisibleWord, "Drawing에서는 제시어를 숨긴다.");

            harness.Logic.CompleteDrawing(harness.Generation);
            harness.Ready();
            Assert.AreEqual(string.Empty, harness.Logic.VisibleWord, "ObservePrevious에서도 숨긴다.");
            harness.Expire(5f);
            harness.Logic.CompleteDrawing(harness.Generation);
            harness.Ready();
            Assert.AreEqual(string.Empty, harness.Logic.VisibleWord, "Guessing에서도 숨긴다.");

            harness.Logic.SubmitAnswer(harness.Generation);
            Assert.AreEqual("우산", harness.Logic.VisibleWord, "결과 공개에서만 다시 보인다.");
        }

        [Test]
        public void TurnRecord_HasNoTextFieldForSecretWord()
        {
            FieldInfo[] fields = typeof(RelayTurnRecord).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                Assert.AreNotEqual(typeof(string), field.FieldType,
                    "RelayTurnRecord는 제시어를 담을 수 있는 문자열 필드를 갖지 않는다: " + field.Name);
            }
        }

        // ---- 깊은 복사와 재시작 정리 (docs/09 §8·§4-3) ----

        [Test]
        public void ArchivedDrawing_IsDeepCopiedFromExport()
        {
            var harness = new Harness();
            CanvasDrawingData source = MakeDrawing(0.3f);
            harness.NextDrawing = source;
            harness.Begin(2);
            harness.PlayDrawingTurn();

            CanvasDrawingData archived = harness.Logic.Records[0].drawing;
            Assert.AreNotSame(source, archived);
            Assert.AreNotSame(source.strokes, archived.strokes);
            Assert.AreNotSame(source.strokes[0], archived.strokes[0]);
            Assert.AreNotSame(source.strokes[0].xy, archived.strokes[0].xy);

            source.strokes[0].xy[0] = 0.9f;
            source.strokes[0] = null;
            Assert.AreEqual(0.3f, archived.strokes[0].xy[0], 0.0001f);
        }

        [Test]
        public void NullExport_StillArchivesAnEmptyDrawing()
        {
            var harness = new Harness();
            harness.NextDrawing = null;
            harness.Begin(2);
            harness.PlayDrawingTurn();

            Assert.AreEqual(1, harness.Logic.Records.Count);
            Assert.IsNotNull(harness.Logic.Records[0].drawing);
            Assert.AreEqual(0, harness.Logic.Records[0].drawing.strokes.Length);
        }

        [Test]
        public void PreviousDrawing_IsTheMostRecentRecord()
        {
            var harness = new Harness();
            harness.Begin(4);
            harness.NextDrawing = MakeDrawing(0.1f);
            harness.PlayDrawingTurn();
            harness.NextDrawing = MakeDrawing(0.5f);
            harness.PlayDrawingTurn();

            Assert.AreEqual(0.5f, harness.Logic.PreviousDrawing.strokes[0].xy[0], 0.0001f);
        }

        [Test]
        public void Restart_ClearsRecordsAnswerAndWord()
        {
            var harness = new Harness();
            harness.NextWord = "피아노";
            harness.NextAnswer = "피아노";
            harness.NextDrawing = MakeDrawing(0.2f);
            harness.Begin(3);
            harness.PlayDrawingTurn();
            harness.PlayDrawingTurn();
            harness.Ready();
            harness.Logic.SubmitAnswer(harness.Generation);
            Assert.IsTrue(harness.Logic.OpenGallery(harness.Generation));
            Assert.AreEqual(RelayQuizState.Gallery, harness.Logic.State);

            Assert.IsTrue(harness.Logic.Restart(harness.Generation));
            Assert.AreEqual(RelayQuizState.Setup, harness.Logic.State);
            Assert.AreEqual(0, harness.Logic.Records.Count);
            Assert.AreEqual(0, harness.Logic.PlayerIndex);
            Assert.AreEqual(string.Empty, harness.Logic.SubmittedAnswer);
            Assert.IsFalse(harness.Logic.AnswerSubmitted);
            Assert.IsFalse(harness.Logic.AnswerCorrect);
            Assert.IsFalse(harness.Logic.Paused);
            Assert.IsFalse(harness.Logic.HasTimer);
            Assert.AreEqual(3, harness.Logic.PlayerCount, "인원 선택은 Setup에서 다시 바꾼다.");
        }

        [Test]
        public void Gallery_OpensOnlyFromReveal()
        {
            var harness = new Harness();
            harness.Begin(2);
            Assert.IsFalse(harness.Logic.OpenGallery(harness.Generation));
            Assert.IsFalse(harness.Logic.Restart(harness.Generation));
            Assert.AreEqual(RelayQuizState.Handover, harness.Logic.State);
        }

        [Test]
        public void Restart_DrawsANewWordFromTheSameDeck()
        {
            var bank = new CameraCoop.Game.WordBank("가\n나\n다\n라", 1234);
            var logic = new RelayQuizLogic(new RelayQuizTimings(), bank.Next, () => null, () => string.Empty);
            var drawn = new List<string>();

            for (int round = 0; round < 4; round++)
            {
                logic.SetPlayerCount(2, logic.PhaseGeneration);
                logic.StartGame(logic.PhaseGeneration);
                logic.ConfirmReady(logic.PhaseGeneration);
                drawn.Add(logic.VisibleWord);
                logic.Tick(5f);
                logic.CompleteDrawing(logic.PhaseGeneration);
                logic.ConfirmReady(logic.PhaseGeneration);
                logic.SubmitAnswer(logic.PhaseGeneration);
                logic.OpenGallery(logic.PhaseGeneration);
                logic.Restart(logic.PhaseGeneration);
            }

            CollectionAssert.AllItemsAreUnique(drawn, "재시작해도 덱의 추출 위치를 유지해 한 바퀴 안에서 중복되지 않는다.");
            CollectionAssert.AreEquivalent(new[] { "가", "나", "다", "라" }, drawn);
        }

        // ---- 단어 데이터 (docs/09 §9) ----

        [Test]
        public void WordListAsset_HasTwentyUniqueKoreanWords()
        {
            var list = AssetDatabase.LoadAssetAtPath<RelayQuizWordList>(WordAssetPath);
            Assert.IsNotNull(list, WordAssetPath + " 자산이 필요합니다.");
            Assert.AreEqual(20, list.Count);

            string deck;
            string error;
            Assert.IsTrue(list.TryBuildDeckText(out deck, out error), error);
            var bank = new CameraCoop.Game.WordBank(deck, 7);
            Assert.AreEqual(20, bank.Count);

            var seen = new HashSet<string>();
            for (int i = 0; i < 20; i++)
            {
                Assert.IsTrue(seen.Add(bank.Next()), "한 바퀴 안에서 중복 추출이 없어야 합니다.");
            }
        }

        [Test]
        public void WordList_RejectsEmptyEntry()
        {
            RelayQuizWordList list = MakeWordList(Entry("사과"), Entry("  "));
            string deck;
            string error;
            Assert.IsFalse(list.TryBuildDeckText(out deck, out error));
            Assert.IsNull(deck);
            StringAssert.Contains("empty", error);
            UnityEngine.Object.DestroyImmediate(list);
        }

        [Test]
        public void WordList_RejectsNormalizedDuplicate()
        {
            RelayQuizWordList list = MakeWordList(Entry("사과"), Entry(" 사 과 "));
            string deck;
            string error;
            Assert.IsFalse(list.TryBuildDeckText(out deck, out error));
            StringAssert.Contains("duplicate", error);
            UnityEngine.Object.DestroyImmediate(list);
        }

        [Test]
        public void WordList_RejectsEmptyList()
        {
            RelayQuizWordList list = MakeWordList();
            string deck;
            string error;
            Assert.IsFalse(list.TryBuildDeckText(out deck, out error));
            UnityEngine.Object.DestroyImmediate(list);
        }

        private static RelayQuizWordList.Entry Entry(string text)
        {
            return new RelayQuizWordList.Entry { text = text, difficulty = RelayQuizDifficulty.Easy };
        }

        private static RelayQuizWordList MakeWordList(params RelayQuizWordList.Entry[] entries)
        {
            var list = ScriptableObject.CreateInstance<RelayQuizWordList>();
            FieldInfo field = typeof(RelayQuizWordList).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "RelayQuizWordList.entries is required.");
            field.SetValue(list, entries);
            return list;
        }
    }
}
