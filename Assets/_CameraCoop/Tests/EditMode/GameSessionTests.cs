using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using CameraCoop.Game;
using CameraCoop.Netplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CameraCoop.Tests
{
    // docs/12 §2 — GameSession 플러밍: host 전이 → 송신 매핑, 클라 수신 규칙, 게이트 갱신.
    // Update 대신 internal TickForTest(dt)로 시간을 주입한다 (EditMode에는 Update가 없다).
    public class GameSessionTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string HostId = "local-host";
        private const string Word = "사과";      // 단어장을 1개로 두어 제시어를 결정적으로 만든다

        // 라운드가 빨리 돌도록 축소한 시간 (host 권위 — 클라는 RoundBegin으로 같은 값을 받는다)
        private const float IntroSec = 0.5f;
        private const float DrawSec = 1f;
        private const float RevealSec = 0.5f;
        private const float GameEndSec = 0.5f;
        private const float RelaySwapSec = 0.25f;

        private GameObject root;
        private NetSession session;
        private GameSession game;
        private HandPointer pointer;
        private LoopbackTransport transport;
        private TextAsset wordAsset;
        private LoopbackTransport.FakePeer peerA;
        private LoopbackTransport.FakePeer peerB;
        private LoopbackTransport.FakePeer hostPeer;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
            if (wordAsset != null)
            {
                Object.DestroyImmediate(wordAsset);
            }
            root = null;
            session = null;
            game = null;
            pointer = null;
            transport = null;
            wordAsset = null;
            peerA = null;
            peerB = null;
            hostPeer = null;
        }

        // ---- 하네스 ----

        private void Build(int cycles = 2, bool withWordAsset = true)
        {
            root = new GameObject("GameSessionTest");
            root.SetActive(false); // EditMode에는 Awake/Update가 없다 — 구독은 OnEnable을 직접 호출해 건다
            session = root.AddComponent<NetSession>();
            pointer = root.AddComponent<HandPointer>();
            game = root.AddComponent<GameSession>();

            var so = new SerializedObject(game);
            so.FindProperty("netSession").objectReferenceValue = session;
            so.FindProperty("handPointer").objectReferenceValue = pointer;
            if (withWordAsset)
            {
                wordAsset = new TextAsset(Word);
                so.FindProperty("wordAsset").objectReferenceValue = wordAsset;
            }
            so.FindProperty("introSec").floatValue = IntroSec;
            so.FindProperty("drawSec").floatValue = DrawSec;
            so.FindProperty("revealSec").floatValue = RevealSec;
            so.FindProperty("gameEndSec").floatValue = GameEndSec;
            so.FindProperty("relaySwapSec").floatValue = RelaySwapSec;
            so.FindProperty("cycles").intValue = cycles;
            so.ApplyModifiedPropertiesWithoutUndo();

            Enable();
        }

        private void Enable()
        {
            typeof(GameSession).GetMethod("OnEnable", Flags).Invoke(game, null);
        }

        // 로컬이 host, 가짜 클라 peerCount명이 Hello까지 마친 상태
        private void StartHost(int peerCount = 2, int cycles = 2, bool withWordAsset = true)
        {
            Build(cycles, withWordAsset);
            transport = new LoopbackTransport();
            session.StartSession(transport, "Host");
            if (peerCount > 0)
            {
                peerA = AddPeer("a", "A");
            }
            if (peerCount > 1)
            {
                peerB = AddPeer("b", "B");
            }
        }

        private LoopbackTransport.FakePeer AddPeer(string id, string name)
        {
            LoopbackTransport.FakePeer peer = transport.AddFakePeer(id, name);
            Deliver(peer, NetProtocol.TypeHello, id, new HelloPayload { name = name });
            return peer;
        }

        private void StartClient()
        {
            Build();
            transport = new LoopbackTransport(isHost: false, localPlayerId: "cli");
            session.StartSession(transport, "Cli");
            hostPeer = transport.AddFakePeer("host", "Host");
            Deliver(hostPeer, NetProtocol.TypeWelcome, "host", new WelcomePayload
            {
                players = new[]
                {
                    new PlayerInfo { playerId = "host", name = "Host", colorIndex = 0 },
                    new PlayerInfo { playerId = "cli", name = "Cli", colorIndex = 1 }
                },
                snapshot = new StrokeSnapshot[0]
            });
        }

        private void Deliver<T>(LoopbackTransport.FakePeer peer, string type, string sender, T payload)
        {
            peer.Send(NetProtocol.Encode(type, sender, payload));
            transport.Tick(); // EditMode에는 Update가 없으므로 직접 펌프
        }

        private void Tick(float dt)
        {
            game.TickForTest(dt);
        }

        // RoundIntro → Drawing → RoundReveal → (다음 RoundIntro | GameEnd). 전이는 Tick당 1개다
        private void AdvanceToDrawing()
        {
            Tick(IntroSec);
        }

        private void AdvanceRound()
        {
            Tick(IntroSec);
            Tick(DrawSec);
            Tick(RevealSec);
        }

        private static int CountReceived(LoopbackTransport.FakePeer peer, string type)
        {
            int count = 0;
            for (int i = 0; i < peer.Received.Count; i++)
            {
                NetEnvelope env = NetProtocol.Decode(peer.Received[i]);
                if (env != null && env.type == type)
                {
                    count++;
                }
            }
            return count;
        }

        private static NetEnvelope LastReceived(LoopbackTransport.FakePeer peer, string type)
        {
            for (int i = peer.Received.Count - 1; i >= 0; i--)
            {
                NetEnvelope env = NetProtocol.Decode(peer.Received[i]);
                if (env != null && env.type == type)
                {
                    return env;
                }
            }
            return null;
        }

        private static List<T> AllPayloads<T>(LoopbackTransport.FakePeer peer, string type)
        {
            var result = new List<T>();
            for (int i = 0; i < peer.Received.Count; i++)
            {
                NetEnvelope env = NetProtocol.Decode(peer.Received[i]);
                if (env != null && env.type == type)
                {
                    result.Add(NetProtocol.DecodePayload<T>(env));
                }
            }
            return result;
        }

        // 제시어는 출제자 1명에게만 간다 (docs/12 §3 — Broadcast로 보내면 게임이 성립하지 않는다)
        private void AssertWordAssignOnlyTo(string activeId, int expectedCount)
        {
            Assert.AreEqual(activeId == "a" ? expectedCount : 0, CountReceived(peerA, GameMsg.TypeWordAssign), "peerA WordAssign");
            Assert.AreEqual(activeId == "b" ? expectedCount : 0, CountReceived(peerB, GameMsg.TypeWordAssign), "peerB WordAssign");
            if (activeId == HostId)
            {
                Assert.AreEqual(Word, game.State.LocalWord, "출제자가 로컬이면 로컬 적용만 한다");
            }
        }

        // ---- CanStartGame (docs/12 §5 이중 방어) ----

        [Test]
        public void CanStartGame_RequiresHostSessionAndPlayerCount()
        {
            Build();
            Assert.IsFalse(game.CanStartGame(0), "세션이 없으면 시작 불가");

            transport = new LoopbackTransport();
            session.StartSession(transport, "Host");
            Assert.IsFalse(game.CanStartGame(0), "1인은 시작 불가");

            peerA = AddPeer("a", "A");
            Assert.IsTrue(game.CanStartGame(0));
            Assert.IsFalse(game.CanStartGame(1), "2인 릴레이는 거부 (docs/12 §1)");

            peerB = AddPeer("b", "B");
            Assert.IsTrue(game.CanStartGame(1));

            game.StartGame(0);
            Assert.IsFalse(game.CanStartGame(0), "진행 중에는 시작 불가");
        }

        [Test]
        public void CanStartGame_ClientIsFalse()
        {
            StartClient();
            Assert.IsFalse(game.CanStartGame(0), "게임 시작은 host 전용");
        }

        [Test]
        public void StartGame_WithoutWordAsset_LogsErrorAndRejects()
        {
            StartHost(withWordAsset: false);
            LogAssert.Expect(LogType.Error, new Regex("GameSession.*wordAsset"));

            game.StartGame(0);

            Assert.IsFalse(game.IsGameRunning, "조용한 실패 금지 — 거부하고 로그를 남긴다");
            Assert.AreEqual(0, CountReceived(peerA, GameMsg.TypeGameStart));
        }

        // ---- StartGame 성공 → 라운드 세팅 공통 ----

        [Test]
        public void Host_StartGameTurns_SendsStartClearRoundBegin_AndWordAssignToDrawerOnly()
        {
            StartHost();
            int changed = 0;
            game.OnStateChanged += () => changed++;

            game.StartGame(0);

            Assert.AreEqual(1, CountReceived(peerA, GameMsg.TypeGameStart));
            Assert.AreEqual(1, CountReceived(peerB, GameMsg.TypeGameStart));
            Assert.AreEqual(1, CountReceived(peerA, NetProtocol.TypeClear), "라운드 세팅은 SendClear로 캔버스를 비운다");

            NetEnvelope env = LastReceived(peerA, GameMsg.TypeRoundBegin);
            Assert.IsNotNull(env);
            var begin = NetProtocol.DecodePayload<RoundBeginPayload>(env);
            Assert.AreEqual(1, begin.round);
            Assert.AreEqual(6, begin.totalRounds, "3인 × cycles 2");
            Assert.AreEqual(Word.Length, begin.wordLen);
            Assert.AreEqual(IntroSec, begin.introSec, 1e-4f);
            Assert.AreEqual(DrawSec, begin.durationSec, 1e-4f);

            string active1 = game.State.ActiveId;
            Assert.AreEqual(begin.activeId, active1, "host 미러도 같은 payload로 갱신된다");
            AssertWordAssignOnlyTo(active1, 1);
            Assert.GreaterOrEqual(changed, 1, "표시 상태 변화가 통지된다");

            AdvanceRound(); // 2라운드로 — 출제자가 바뀐 분기도 같은 불변식을 지켜야 한다
            string active2 = game.State.ActiveId;
            Assert.AreNotEqual(active1, active2, "출제자는 라운드마다 순환한다");
            Assert.AreEqual(2, game.State.Round);
            AssertWordAssignOnlyTo(active2, 1);
        }

        [Test]
        public void Host_DrawingEntry_SendsNoMessage_AndUpdatesGates()
        {
            StartHost();
            game.StartGame(0);
            AdvanceRound(); // 2라운드 = 출제자가 가짜 클라
            int beforeA = peerA.Received.Count;

            AdvanceToDrawing();

            Assert.AreEqual(beforeA, peerA.Received.Count, "Drawing 진입은 클라 자체 전환 — 메시지 없음");
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, game.State.CurrentPhase);
            Assert.IsFalse(pointer.StrokesEnabled, "로컬이 출제자가 아니면 로컬 스트로크를 막는다");
            Assert.IsNotNull(session.StrokeGate);
            Assert.IsTrue(session.StrokeGate(game.State.CurrentDrawerId), "출제자만 통과");
            Assert.IsFalse(session.StrokeGate(HostId));
        }

        [Test]
        public void Host_NonDrawerStrokeStart_IsNotRelayed()
        {
            StartHost();
            game.StartGame(0);
            AdvanceToDrawing(); // 1라운드 출제자 = host (첫 등록자)
            Assert.AreEqual(HostId, game.State.CurrentDrawerId);
            Assert.IsTrue(pointer.StrokesEnabled, "로컬이 출제자면 그릴 수 있다");
            var starts = new List<string>();
            session.OnRemoteStrokeStart += (id, sender, p, style) => starts.Add(id);

            Deliver(peerA, NetProtocol.TypeStrokeStart, "a",
                new StrokeStartPayload { strokeId = "a:0", hand = "Right", x = 0.2f, y = 0.3f, color = 0, width = 0.02f, brush = 0 });

            Assert.AreEqual(0, CountReceived(peerB, NetProtocol.TypeStrokeStart), "비출제자 스트로크는 중계되지 않는다");
            Assert.AreEqual(0, starts.Count, "host에도 반영되지 않는다");
        }

        // ---- 정답 흐름 (docs/12 §2) ----

        [Test]
        public void Host_ClientGuess_WrongKeepsText_CorrectClearsText_AndAllGuessedEndsRound()
        {
            StartHost();
            game.StartGame(0);
            AdvanceToDrawing();
            Assert.AreEqual(HostId, game.State.ActiveId, "1라운드 출제자는 host — a·b가 게서다");

            Deliver(peerA, GameMsg.TypeGuessSubmit, "a", new GuessSubmitPayload { text = "바나나" });

            Assert.AreEqual(0, CountReceived(peerB, GameMsg.TypeGuessSubmit), "GuessSubmit은 절대 중계되지 않는다");
            List<GuessFeedPayload> feedB = AllPayloads<GuessFeedPayload>(peerB, GameMsg.TypeGuessFeed);
            Assert.AreEqual(1, feedB.Count);
            Assert.AreEqual("바나나", feedB[0].text, "오답은 원문을 실어 전원 피드에 노출된다");
            Assert.IsFalse(feedB[0].correct);
            Assert.AreEqual("a", feedB[0].playerId);
            Assert.AreEqual(1, game.State.Feed.Count, "host 미러에도 같은 피드가 쌓인다");

            Deliver(peerA, GameMsg.TypeGuessSubmit, "a", new GuessSubmitPayload { text = Word });

            feedB = AllPayloads<GuessFeedPayload>(peerB, GameMsg.TypeGuessFeed);
            Assert.AreEqual(2, feedB.Count);
            Assert.IsTrue(feedB[1].correct);
            Assert.AreEqual("", feedB[1].text, "정답 피드는 text를 비운다 (정답 유출 방지)");
            Assert.AreEqual(0, CountReceived(peerB, GameMsg.TypeRoundEnd), "아직 b가 남았다");

            Deliver(peerB, GameMsg.TypeGuessSubmit, "b", new GuessSubmitPayload { text = Word });

            NetEnvelope env = LastReceived(peerA, GameMsg.TypeRoundEnd);
            Assert.IsNotNull(env, "출제자 외 전원 정답 → 조기 종료");
            var end = NetProtocol.DecodePayload<RoundEndPayload>(env);
            Assert.AreEqual(Word, end.word, "라운드 끝에 제시어를 공개한다");
            Assert.AreEqual((int)GuessGameLogic.RoundEndReason.AllGuessed, end.reason);
            Assert.AreEqual(3, end.playerIds.Length);
            Assert.AreEqual(3, end.scores.Length);
            Assert.AreEqual(GuessGameLogic.Phase.RoundReveal, game.State.CurrentPhase);
            Assert.AreEqual(100, game.State.Scores[HostId], "출제자는 정답자 2명 × 50");
        }

        [Test]
        public void Host_IgnoredGuess_SendsNothing()
        {
            StartHost();
            game.StartGame(0);
            AdvanceToDrawing();
            int beforeB = peerB.Received.Count;

            game.SubmitGuess(Word); // 로컬 host = 출제자 본인 → 무시

            Assert.AreEqual(beforeB, peerB.Received.Count, "무시된 제출은 아무것도 보내지 않는다");
        }

        [Test]
        public void Host_RoundTimeout_BroadcastsRoundEndWithTimeout()
        {
            StartHost();
            game.StartGame(0);
            AdvanceToDrawing();

            Tick(DrawSec);

            NetEnvelope env = LastReceived(peerA, GameMsg.TypeRoundEnd);
            Assert.IsNotNull(env);
            var end = NetProtocol.DecodePayload<RoundEndPayload>(env);
            Assert.AreEqual((int)GuessGameLogic.RoundEndReason.Timeout, end.reason);
            Assert.IsFalse(pointer.StrokesEnabled, "RoundReveal에서는 아무도 그리지 않는다");
        }

        [Test]
        public void Host_LastRound_BroadcastsGameEnd_ThenReturnsToIdleAndReleasesGates()
        {
            StartHost(cycles: 1); // 3인 × 1 = 3라운드
            game.StartGame(0);
            AdvanceRound();
            AdvanceRound();
            AdvanceRound(); // 마지막 RoundReveal 만료 → ToGameEnd

            NetEnvelope env = LastReceived(peerA, GameMsg.TypeGameEnd);
            Assert.IsNotNull(env, "마지막 라운드 후 최종 점수판을 보낸다");
            var final = NetProtocol.DecodePayload<GameEndPayload>(env);
            Assert.AreEqual(3, final.playerIds.Length);
            Assert.AreEqual(GuessGameLogic.Phase.GameEnd, game.State.CurrentPhase);

            Tick(GameEndSec);

            Assert.AreEqual(GuessGameLogic.Phase.Idle, game.State.CurrentPhase);
            Assert.IsFalse(game.IsGameRunning);
            Assert.IsTrue(pointer.StrokesEnabled, "Idle 복귀 = 자유 그리기");
            Assert.IsNull(session.StrokeGate, "게이트 전부 해제");
        }

        // ---- 늦은 참가 (docs/12 §5) ----

        [Test]
        public void Host_LateJoinDuringGame_SendsGameStateSyncToThatPeerOnly()
        {
            StartHost();
            game.StartGame(0);
            AdvanceToDrawing();

            LoopbackTransport.FakePeer peerC = AddPeer("c", "C");

            Assert.AreEqual(1, CountReceived(peerC, GameMsg.TypeGameStateSync));
            Assert.AreEqual(0, CountReceived(peerA, GameMsg.TypeGameStateSync), "기존 피어에는 보내지 않는다");
            var sync = NetProtocol.DecodePayload<GameStateSyncPayload>(LastReceived(peerC, GameMsg.TypeGameStateSync));
            Assert.AreEqual((int)GuessGameLogic.Phase.Drawing, sync.phase);
            Assert.AreEqual(1, sync.round);
            Assert.AreEqual(7, sync.totalRounds, "늦은 참가자가 큐 끝에 1회 추가된다");
            Assert.AreEqual(game.State.ActiveId, sync.activeId);
            Assert.AreEqual(Word.Length, sync.wordLen);
            Assert.AreEqual(4, sync.playerIds.Length, "점수판에 늦은 참가자도 0점으로 등장");
        }

        [Test]
        public void Host_PeerJoinsWhileIdle_NoGameStateSync()
        {
            StartHost();

            LoopbackTransport.FakePeer peerC = AddPeer("c", "C");

            Assert.AreEqual(0, CountReceived(peerC, GameMsg.TypeGameStateSync), "게임 중이 아니면 보낼 상태가 없다");
        }

        // ---- 이탈 (docs/12 §5) ----

        [Test]
        public void Host_ActivePlayerLeaves_BroadcastsRoundEndActiveLeft()
        {
            StartHost();
            game.StartGame(0);
            AdvanceRound(); // 2라운드 출제자 = "a" (등록 순서상 host 다음)
            Assert.AreEqual("a", game.State.ActiveId);

            transport.RemoveFakePeer("a");

            NetEnvelope env = LastReceived(peerB, GameMsg.TypeRoundEnd);
            Assert.IsNotNull(env);
            var end = NetProtocol.DecodePayload<RoundEndPayload>(env);
            Assert.AreEqual((int)GuessGameLogic.RoundEndReason.ActiveLeft, end.reason);
            Assert.AreEqual(GuessGameLogic.Phase.RoundReveal, game.State.CurrentPhase);
        }

        [Test]
        public void Host_PlayerCountBelowTwo_AbortsToIdle()
        {
            StartHost(peerCount: 1);
            game.StartGame(0);
            AdvanceToDrawing();
            Assert.IsTrue(game.IsGameRunning);

            transport.RemoveFakePeer("a"); // host 1명만 남는다

            Assert.AreEqual(GuessGameLogic.Phase.Idle, game.State.CurrentPhase, "인원 부족 → GameAbort + Idle");
            Assert.IsTrue(pointer.StrokesEnabled);
            Assert.IsNull(session.StrokeGate);
        }

        [Test]
        public void Host_StopSessionDuringGame_ResetsStateAndRestoresGates()
        {
            StartHost();
            game.StartGame(0);
            AdvanceRound();
            AdvanceToDrawing(); // 2라운드 Drawing — 출제자는 "a"라 로컬 스트로크가 막힌 상태
            Assert.IsFalse(pointer.StrokesEnabled);

            session.StopSession();

            Assert.AreEqual(GuessGameLogic.Phase.Idle, game.State.CurrentPhase, "host 이탈·중단이면 게임도 함께 끝난다");
            Assert.IsTrue(pointer.StrokesEnabled, "자유 그리기 복원");
            Assert.IsNull(session.StrokeGate);
        }

        // ---- 릴레이 모드 ----

        [Test]
        public void Host_RelayMode_WordAssignToAllExceptActive_AndRelaySwapDrivesDrawer()
        {
            StartHost();
            game.StartGame(1);
            Assert.AreEqual(HostId, game.State.ActiveId, "1라운드 맞히는 사람 = host");

            Assert.AreEqual(1, CountReceived(peerA, GameMsg.TypeWordAssign), "ActiveId 제외 전원이 제시어를 안다");
            Assert.AreEqual(1, CountReceived(peerB, GameMsg.TypeWordAssign));
            Assert.IsNull(game.State.LocalWord, "맞히는 사람에게는 제시어를 주지 않는다");

            AdvanceToDrawing();

            List<RelaySwapPayload> swapsA = AllPayloads<RelaySwapPayload>(peerA, GameMsg.TypeRelaySwap);
            Assert.AreEqual(1, swapsA.Count, "Drawing 진입 시 첫 drawer를 알린다");
            Assert.AreNotEqual(HostId, swapsA[0].drawerId, "맞히는 사람은 그리지 않는다");
            Assert.AreEqual(swapsA[0].drawerId, game.State.CurrentDrawerId);

            Tick(RelaySwapSec);

            swapsA = AllPayloads<RelaySwapPayload>(peerA, GameMsg.TypeRelaySwap);
            Assert.AreEqual(2, swapsA.Count, "relaySwapSec마다 교대");
            Assert.AreNotEqual(swapsA[0].drawerId, swapsA[1].drawerId, "drawerId가 실제로 바뀔 때만 보낸다 (docs/14 §6-3)");
        }

        [Test]
        public void Host_RelayMode_CorrectGuessScoresWholeTeam()
        {
            StartHost();
            game.StartGame(1);
            AdvanceToDrawing();

            game.SubmitGuess(Word); // 로컬 host가 맞히는 사람

            NetEnvelope env = LastReceived(peerA, GameMsg.TypeRoundEnd);
            Assert.IsNotNull(env);
            var end = NetProtocol.DecodePayload<RoundEndPayload>(env);
            Assert.AreEqual((int)GuessGameLogic.RoundEndReason.AllGuessed, end.reason);
            Assert.AreEqual(3, end.scores.Length);
            for (int i = 1; i < end.scores.Length; i++)
            {
                Assert.AreEqual(end.scores[0], end.scores[i], "릴레이는 팀 협동 점수 — 전원 동일");
            }
            Assert.Greater(end.scores[0], 0);
        }

        [Test]
        public void Host_TurnsMode_NeverSendsRelaySwap()
        {
            StartHost();
            game.StartGame(0);
            AdvanceToDrawing();
            Tick(DrawSec * 0.5f);

            Assert.AreEqual(0, CountReceived(peerA, GameMsg.TypeRelaySwap), "Turns의 drawer는 RoundBegin.activeId로 이미 정해졌다");
        }

        // ---- 클라 수신 규칙 (docs/12 §5 위조 방어) ----

        [Test]
        public void Client_ForgedGameMessageFromNonHost_IsIgnored()
        {
            StartClient();

            Deliver(hostPeer, GameMsg.TypeRoundBegin, "evil", new RoundBeginPayload
            {
                round = 9, totalRounds = 9, activeId = "evil", wordLen = 5, introSec = 1f, durationSec = 30f
            });

            Assert.AreEqual(GuessGameLogic.Phase.Idle, game.State.CurrentPhase, "host sender가 아니면 적용하지 않는다");
            Assert.AreEqual(0, game.State.Round);
        }

        [Test]
        public void Client_HostGameMessages_AreMirrored()
        {
            StartClient();
            int changed = 0;
            game.OnStateChanged += () => changed++;

            Deliver(hostPeer, GameMsg.TypeGameStart, "host", new GameStartPayload { gameId = GameMsg.GuessGameId, mode = 0 });
            Deliver(hostPeer, GameMsg.TypeRoundBegin, "host", new RoundBeginPayload
            {
                round = 1, totalRounds = 4, activeId = "cli", wordLen = 2, introSec = IntroSec, durationSec = DrawSec
            });
            Deliver(hostPeer, GameMsg.TypeWordAssign, "host", new WordAssignPayload { word = Word });

            Assert.AreEqual(GuessGameLogic.Phase.RoundIntro, game.State.CurrentPhase);
            Assert.AreEqual(1, game.State.Round);
            Assert.AreEqual(Word, game.State.LocalWord);
            Assert.IsTrue(game.IsGameRunning);
            Assert.GreaterOrEqual(changed, 3);

            AdvanceToDrawing();
            Assert.AreEqual(GuessGameLogic.Phase.Drawing, game.State.CurrentPhase, "클라는 introSec 뒤 스스로 Drawing 표시로 전환");
            Assert.IsTrue(pointer.StrokesEnabled, "로컬이 출제자면 그릴 수 있다");
            Assert.IsNull(session.StrokeGate, "StrokeGate는 host만 설정한다 (docs/14 §6-5)");

            Deliver(hostPeer, GameMsg.TypeGameAbort, "host", new EmptyPayload());
            Assert.AreEqual(GuessGameLogic.Phase.Idle, game.State.CurrentPhase);
            Assert.IsTrue(pointer.StrokesEnabled);
        }

        [Test]
        public void Client_SubmitGuess_GoesToHostOnly()
        {
            StartClient();
            Deliver(hostPeer, GameMsg.TypeRoundBegin, "host", new RoundBeginPayload
            {
                round = 1, totalRounds = 4, activeId = "host", wordLen = 2, introSec = 0f, durationSec = DrawSec
            });
            AdvanceToDrawing();
            int before = transport.SentToHost.Count;

            game.SubmitGuess(Word);

            Assert.AreEqual(before + 1, transport.SentToHost.Count);
            NetEnvelope env = NetProtocol.Decode(transport.SentToHost[transport.SentToHost.Count - 1]);
            Assert.AreEqual(GameMsg.TypeGuessSubmit, env.type);
            Assert.AreEqual(Word, NetProtocol.DecodePayload<GuessSubmitPayload>(env).text);
        }

        [Test]
        public void Client_HostDisconnect_EndsGame()
        {
            StartClient();
            Deliver(hostPeer, GameMsg.TypeRoundBegin, "host", new RoundBeginPayload
            {
                round = 1, totalRounds = 4, activeId = "host", wordLen = 2, introSec = IntroSec, durationSec = DrawSec
            });
            Assert.IsTrue(game.IsGameRunning);

            transport.RemoveFakePeer("host"); // 클라의 직결 피어는 host뿐 → StopSession

            Assert.IsFalse(session.IsRunning);
            Assert.AreEqual(GuessGameLogic.Phase.Idle, game.State.CurrentPhase, "host 이탈 — 게임도 함께 끝 (docs/12 §5)");
            Assert.IsTrue(pointer.StrokesEnabled);
        }
    }
}
