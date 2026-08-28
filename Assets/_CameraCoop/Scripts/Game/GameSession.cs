using System;
using System.Collections.Generic;
using CameraCoop.Netplay;
using UnityEngine;

namespace CameraCoop.Game
{
    // NetSession 게임 통로 ↔ (host) GuessGameLogic / (전원) GameClientState 플러밍 (docs/12 §2).
    // 게임 규칙은 하나도 여기 없다 — host의 로직 전이를 송신으로 옮기고, 수신 메시지를 표시 미러에 넣는 것만 한다.
    public class GameSession : MonoBehaviour
    {
        [SerializeField] private NetSession netSession;
        [SerializeField] private HandPointer handPointer;          // 로컬 그리기 게이트 대상
        [SerializeField] private TextAsset wordAsset;              // Data/words_ko.txt (1줄 1단어)
        [SerializeField, Min(0f)] private float introSec = 3f;
        [SerializeField, Min(0f)] private float drawSec = 90f;
        [SerializeField, Min(0f)] private float revealSec = 5f;
        [SerializeField, Min(0f)] private float gameEndSec = 8f;
        [SerializeField, Min(0f)] private float relaySwapSec = 15f;
        [SerializeField, Min(1)] private int cycles = 2;

        private GameClientState state;
        private GuessGameLogic logic;                              // host에서만 구동한다
        private WordBank words;                                    // 세션 내 무중복 (docs/12 §1) — 게임마다 새로 만들지 않는다
        private Func<string, bool> strokeGate;                     // 전이마다 새 델리게이트를 만들지 않도록 캐시
        private readonly List<string> knownPlayers = new List<string>(4);  // 이탈 감지용 (OnPlayersChanged는 누가 나갔는지 알려주지 않는다)
        private readonly List<string> leftPlayers = new List<string>(4);
        private readonly List<string> idBuffer = new List<string>(4);      // 평행 배열 조립용
        private readonly List<int> scoreBuffer = new List<int>(4);

        // UI가 읽는 표시 상태 — 항상 non-null (씬 배선 전·Awake 전에도 접근된다)
        public GameClientState State
        {
            get
            {
                if (state == null)
                {
                    state = new GameClientState(gameEndSec);
                }
                return state;
            }
        }

        public bool IsGameRunning
        {
            get { return State.CurrentPhase != GuessGameLogic.Phase.Idle; }
        }

        public event Action OnStateChanged; // 표시 상태 변화 통지 (덩어리 1개)

        private void OnEnable()
        {
            if (netSession == null)
            {
                Debug.LogWarning("[GameSession] netSession 미할당 — 게임이 동작하지 않습니다.");
                return;
            }
            netSession.OnGameMessage += HandleGameMessage;
            netSession.OnPeerJoinedSession += HandlePeerJoined;
            netSession.OnPlayersChanged += HandlePlayersChanged;
        }

        private void OnDisable()
        {
            if (netSession == null)
            {
                return;
            }
            netSession.OnGameMessage -= HandleGameMessage;
            netSession.OnPeerJoinedSession -= HandlePeerJoined;
            netSession.OnPlayersChanged -= HandlePlayersChanged;
        }

        private void Update()
        {
            TickInternal(Time.deltaTime);
        }

        // EditMode 테스트는 Update가 없어 시간을 직접 주입한다
        internal void TickForTest(float deltaTime)
        {
            TickInternal(deltaTime);
        }

        // 표시 미러를 먼저 진행시킨다 — 뒤이은 host 전이가 새로 채운 phase를 같은 프레임에 또 깎지 않도록
        private void TickInternal(float deltaTime)
        {
            bool changed = State.Tick(deltaTime);
            if (logic != null && netSession != null && netSession.IsHost)
            {
                GuessGameLogic.Transition transition = logic.Tick(deltaTime);
                if (transition != GuessGameLogic.Transition.None)
                {
                    changed |= HandleTransition(transition);
                }
            }
            if (changed)
            {
                UpdateGates();
                RaiseChanged();
            }
        }

        // ---- UI 진입점 ----

        // host && 세션 중 && 미진행 && 인원 충족 (Relay는 3인 이상 — docs/12 §1)
        public bool CanStartGame(int mode)
        {
            if (netSession == null || !netSession.IsRunning || !netSession.IsHost)
            {
                return false;
            }
            if (IsGameRunning)
            {
                return false;
            }
            int required = mode == (int)GuessGameLogic.GameMode.Relay ? 3 : 2;
            return netSession.Players.Count >= required;
        }

        public void StartGame(int mode)
        {
            if (!CanStartGame(mode))
            {
                return;
            }
            if (wordAsset == null)
            {
                Debug.LogError("[GameSession] wordAsset 미할당 — 제시어가 없어 게임을 시작할 수 없습니다 (docs/12 §1)");
                return;
            }
            if (words == null)
            {
                words = new WordBank(wordAsset.text, Environment.TickCount);
            }
            logic = new GuessGameLogic(words, introSec, drawSec, revealSec, gameEndSec, relaySwapSec);

            idBuffer.Clear();
            foreach (KeyValuePair<string, PlayerInfo> pair in netSession.Players)
            {
                idBuffer.Add(pair.Key);
            }
            if (!logic.StartGame(idBuffer, (GuessGameLogic.GameMode)mode, cycles))
            {
                logic = null;
                return; // 로직이 거부했으면 아무것도 보내지 않는다 (UI 게이트와 이중 방어)
            }
            RefreshKnownPlayers();

            var start = new GameStartPayload { gameId = GameMsg.GuessGameId, mode = mode };
            netSession.BroadcastGameMsg(GameMsg.TypeGameStart, start);
            State.ApplyGameStart(start);
            BeginRoundCommon();
            RaiseChanged();
        }

        // 로컬 입력. host면 로직 직접, 클라면 host에게 위임 (docs/12 §2 정답 흐름)
        public void SubmitGuess(string text)
        {
            if (netSession == null || !netSession.IsRunning)
            {
                return;
            }
            if (netSession.IsHost)
            {
                HandleGuess(netSession.LocalPlayerId, text);
                return;
            }
            if (!IsGameRunning)
            {
                return;
            }
            netSession.SendGameToHost(GameMsg.TypeGuessSubmit, new GuessSubmitPayload { text = text });
        }

        // ---- host: 로직 전이 → 송신 ----

        private bool HandleTransition(GuessGameLogic.Transition transition)
        {
            switch (transition)
            {
                case GuessGameLogic.Transition.ToRoundIntro:
                    BeginRoundCommon();
                    return true;
                case GuessGameLogic.Transition.ToDrawing:
                case GuessGameLogic.Transition.RelaySwap:
                    SyncDrawer(); // Drawing 진입·교대 모두 "drawer가 바뀌었으면 알린다"로 같다
                    return true;
                case GuessGameLogic.Transition.ToRoundReveal:
                    BroadcastRoundEnd();
                    return true;
                case GuessGameLogic.Transition.ToGameEnd:
                    BroadcastGameEnd();
                    return true;
                case GuessGameLogic.Transition.ToIdle:
                    return true; // 메시지 없음 — 표시 미러는 자기 Tick으로 Idle이 되고 게이트만 해제된다
                default:
                    return false;
            }
        }

        // 라운드 세팅 공통: SendClear를 게이트 갱신보다 먼저 (in-flight 스트로크 고아 방지, docs/14 §6-4)
        private void BeginRoundCommon()
        {
            netSession.SendClear();
            string word = logic.CurrentWord;
            var begin = new RoundBeginPayload
            {
                round = logic.Round,
                totalRounds = logic.TotalRounds,
                activeId = logic.ActiveId,
                wordLen = word != null ? word.Length : 0,
                introSec = introSec,
                durationSec = drawSec
            };
            netSession.BroadcastGameMsg(GameMsg.TypeRoundBegin, begin);
            State.ApplyRoundBegin(begin);
            SendWordAssign(word);
            UpdateGates();
        }

        // 제시어는 SendGameTo만 — Broadcast로 보내는 순간 게임이 성립하지 않는다 (docs/12 §3)
        private void SendWordAssign(string word)
        {
            if (word == null)
            {
                return;
            }
            var assign = new WordAssignPayload { word = word };
            string activeId = logic.ActiveId;
            string localId = netSession.LocalPlayerId;

            if (logic.Mode != GuessGameLogic.GameMode.Relay)
            {
                if (activeId == localId)
                {
                    State.ApplyWordAssign(assign); // 출제자가 로컬이면 로컬 적용만
                }
                else
                {
                    netSession.SendGameTo(activeId, GameMsg.TypeWordAssign, assign);
                }
                return;
            }
            // Relay: 맞히는 사람(ActiveId)을 제외한 전원이 제시어를 안다
            foreach (KeyValuePair<string, PlayerInfo> pair in netSession.Players)
            {
                if (pair.Key == activeId)
                {
                    continue;
                }
                if (pair.Key == localId)
                {
                    State.ApplyWordAssign(assign);
                }
                else
                {
                    netSession.SendGameTo(pair.Key, GameMsg.TypeWordAssign, assign);
                }
            }
        }

        // drawerId가 실제로 바뀐 경우에만 브로드캐스트 (docs/14 §6-3 — 릴레이 2인이면 로직이 no-op RelaySwap을 반복한다).
        // Turns는 미러의 drawer가 이미 ActiveId라 아무것도 보내지 않는다.
        private bool SyncDrawer()
        {
            string drawerId = logic.CurrentDrawerId;
            if (drawerId == State.CurrentDrawerId)
            {
                return false;
            }
            var swap = new RelaySwapPayload { drawerId = drawerId };
            netSession.BroadcastGameMsg(GameMsg.TypeRelaySwap, swap);
            State.ApplyRelaySwap(swap);
            return true;
        }

        private void BroadcastRoundEnd()
        {
            BuildScores();
            var end = new RoundEndPayload
            {
                word = logic.CurrentWord,
                playerIds = idBuffer.ToArray(),
                scores = scoreBuffer.ToArray(),
                reason = (int)logic.LastRoundEndReason
            };
            netSession.BroadcastGameMsg(GameMsg.TypeRoundEnd, end);
            State.ApplyRoundEnd(end);
        }

        private void BroadcastGameEnd()
        {
            BuildScores();
            var final = new GameEndPayload { playerIds = idBuffer.ToArray(), scores = scoreBuffer.ToArray() };
            netSession.BroadcastGameMsg(GameMsg.TypeGameEnd, final);
            State.ApplyGameEnd(final);
        }

        private void BuildScores()
        {
            idBuffer.Clear();
            scoreBuffer.Clear();
            foreach (KeyValuePair<string, int> pair in logic.Scores)
            {
                idBuffer.Add(pair.Key);
                scoreBuffer.Add(pair.Value);
            }
        }

        // host 권위 판정. 정답 피드는 text를 비워 보낸다 — 원문을 실으면 아직 못 맞힌 사람에게 유출된다 (docs/12 §2)
        private void HandleGuess(string playerId, string text)
        {
            if (logic == null)
            {
                return;
            }
            switch (logic.SubmitGuess(playerId, text))
            {
                case GuessGameLogic.GuessResult.Wrong:
                    BroadcastFeed(playerId, text, false);
                    break;
                case GuessGameLogic.GuessResult.Correct:
                    BroadcastFeed(playerId, "", true);
                    break;
                case GuessGameLogic.GuessResult.CorrectAndRoundEnd:
                    BroadcastFeed(playerId, "", true);
                    BroadcastRoundEnd();
                    break;
                default:
                    return; // Ignored — 아무것도 보내지 않는다 (docs/12 §2 정답 흐름)
            }
            UpdateGates();
            RaiseChanged();
        }

        private void BroadcastFeed(string playerId, string text, bool correct)
        {
            var feed = new GuessFeedPayload { playerId = playerId, text = text, correct = correct };
            netSession.BroadcastGameMsg(GameMsg.TypeGuessFeed, feed);
            State.ApplyGuessFeed(feed);
        }

        // ---- 수신 ----

        private void HandleGameMessage(string type, string sender, string payloadJson)
        {
            if (netSession == null)
            {
                return;
            }
            if (netSession.IsHost)
            {
                // host는 GuessSubmit만 처리한다 — 나머지 게임 타입은 host가 정본이라 클라가 보낼 일이 없다(위조)
                if (type == GameMsg.TypeGuessSubmit)
                {
                    var submit = JsonUtility.FromJson<GuessSubmitPayload>(payloadJson);
                    HandleGuess(sender, submit != null ? submit.text : null);
                }
                return;
            }
            if (sender == null || sender != netSession.HostPlayerId)
            {
                return; // 위조 방어: 아무 클라나 RoundBegin을 위조하면 전원 화면이 바뀐다 (docs/12 §5)
            }

            bool changed;
            switch (type)
            {
                case GameMsg.TypeGameStart:
                    changed = State.ApplyGameStart(JsonUtility.FromJson<GameStartPayload>(payloadJson));
                    break;
                case GameMsg.TypeRoundBegin:
                    changed = State.ApplyRoundBegin(JsonUtility.FromJson<RoundBeginPayload>(payloadJson));
                    break;
                case GameMsg.TypeWordAssign:
                    changed = State.ApplyWordAssign(JsonUtility.FromJson<WordAssignPayload>(payloadJson));
                    break;
                case GameMsg.TypeRelaySwap:
                    changed = State.ApplyRelaySwap(JsonUtility.FromJson<RelaySwapPayload>(payloadJson));
                    break;
                case GameMsg.TypeGuessFeed:
                    changed = State.ApplyGuessFeed(JsonUtility.FromJson<GuessFeedPayload>(payloadJson));
                    break;
                case GameMsg.TypeRoundEnd:
                    changed = State.ApplyRoundEnd(JsonUtility.FromJson<RoundEndPayload>(payloadJson));
                    break;
                case GameMsg.TypeGameEnd:
                    changed = State.ApplyGameEnd(JsonUtility.FromJson<GameEndPayload>(payloadJson));
                    break;
                case GameMsg.TypeGameAbort:
                    changed = State.ApplyGameAbort();
                    break;
                case GameMsg.TypeGameStateSync:
                    changed = State.ApplyGameStateSync(JsonUtility.FromJson<GameStateSyncPayload>(payloadJson));
                    break;
                default:
                    return; // 모르는 게임 타입 — 다음 게임의 메시지일 수 있다
            }
            if (changed)
            {
                UpdateGates();
                RaiseChanged();
            }
        }

        // 늦은 참가자에게 현재 상태를 보낸다 → 관전, 다음 라운드부터 순환에 포함 (docs/12 §5)
        private void HandlePeerJoined(string playerId)
        {
            if (netSession == null || !netSession.IsHost || logic == null || !IsGameRunning)
            {
                return;
            }
            logic.AddPlayer(playerId); // 큐 끝에 1회 추가 — TotalRounds가 늘어난 뒤 sync를 만든다
            string word = logic.CurrentWord;
            BuildScores();
            var sync = new GameStateSyncPayload
            {
                phase = (int)logic.CurrentPhase,
                gameId = GameMsg.GuessGameId,
                mode = (int)logic.Mode,
                round = logic.Round,
                totalRounds = logic.TotalRounds,
                activeId = logic.ActiveId,
                wordLen = word != null ? word.Length : 0,
                remainingSec = logic.PhaseRemaining,
                playerIds = idBuffer.ToArray(),
                scores = scoreBuffer.ToArray()
            };
            netSession.SendGameTo(playerId, GameMsg.TypeGameStateSync, sync);
        }

        private void HandlePlayersChanged()
        {
            if (netSession == null || !netSession.IsRunning)
            {
                ResetLocal(); // host 이탈·StopSession — 게임도 함께 끝난다 (docs/12 §5)
                return;
            }
            if (!netSession.IsHost || logic == null || !IsGameRunning)
            {
                RefreshKnownPlayers(); // 클라는 판단하지 않는다 — 이탈 처리는 host의 메시지로만 온다
                return;
            }

            leftPlayers.Clear();
            for (int i = 0; i < knownPlayers.Count; i++)
            {
                if (!netSession.Players.ContainsKey(knownPlayers[i]))
                {
                    leftPlayers.Add(knownPlayers[i]);
                }
            }
            RefreshKnownPlayers();
            if (leftPlayers.Count == 0)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < leftPlayers.Count; i++)
            {
                switch (logic.PlayerLeft(leftPlayers[i]))
                {
                    case GuessGameLogic.Transition.ToIdle:
                        netSession.BroadcastGameMsg(GameMsg.TypeGameAbort, new EmptyPayload());
                        State.ApplyGameAbort();
                        changed = true;
                        break;
                    case GuessGameLogic.Transition.ToRoundReveal:
                        BroadcastRoundEnd();
                        changed = true;
                        break;
                    case GuessGameLogic.Transition.RelaySwap:
                        changed |= SyncDrawer();
                        break;
                }
            }
            if (changed)
            {
                UpdateGates();
                RaiseChanged();
            }
        }

        private void RefreshKnownPlayers()
        {
            knownPlayers.Clear();
            if (netSession == null || !netSession.IsRunning)
            {
                return;
            }
            foreach (KeyValuePair<string, PlayerInfo> pair in netSession.Players)
            {
                knownPlayers.Add(pair.Key);
            }
        }

        private void ResetLocal()
        {
            logic = null;
            knownPlayers.Clear();
            State.ApplyGameAbort();
            UpdateGates();
            RaiseChanged();
        }

        // ---- 게이트 (host·클라 공통 1개 함수) ----

        private void UpdateGates()
        {
            bool sessionActive = netSession != null && netSession.IsRunning;
            bool running = sessionActive && IsGameRunning;
            if (handPointer != null)
            {
                // 세션이 없거나(자유 로컬) 게임 미진행이면 전부 허용
                handPointer.StrokesEnabled = !running
                    || (State.CurrentPhase == GuessGameLogic.Phase.Drawing && State.CurrentDrawerId == netSession.LocalPlayerId);
            }
            if (netSession == null)
            {
                return;
            }
            if (!running)
            {
                netSession.StrokeGate = null; // 게임 종료·세션 종료 → 전원 허용 (남겨두면 다음 세션이 못 그린다)
            }
            else if (netSession.IsHost)
            {
                if (strokeGate == null)
                {
                    strokeGate = IsDrawerAllowed;
                }
                netSession.StrokeGate = strokeGate; // 클라에는 설정하지 않는다 (host 정본, docs/14 §6-5)
            }
        }

        private bool IsDrawerAllowed(string playerId)
        {
            return State.CurrentPhase == GuessGameLogic.Phase.Drawing && playerId == State.CurrentDrawerId;
        }

        private void RaiseChanged()
        {
            OnStateChanged?.Invoke();
        }
    }
}
