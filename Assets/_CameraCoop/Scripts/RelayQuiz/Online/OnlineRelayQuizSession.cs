using System;
using System.Collections.Generic;
using CameraCoop.Netplay;
using CameraCoop.Party;
using UnityEngine;

namespace CameraCoop
{
    public sealed class OnlineRelayQuizSession : IDisposable
    {
        private const float TransferTimeout = 10f;
        private const float SceneBarrierTimeout = 15f;
        private const int MaxQueuedBytes = 2 * 1024 * 1024;
        private const int MaxQueuedMessages = 192;
        private const int MaxHandshakeCandidates = OnlineRelayQuizProtocol.PlayerCount - 1;
        private readonly INetTransport transport;
        private readonly string expectedHostId;
        private readonly Func<CanvasDrawingData> drawingSource;
        private readonly Func<string> answerSource;
        private readonly int brushes;
        private readonly int partySize;
        private readonly string[] rosterStatus;
        private readonly RelayQuizLogic logic;
        private readonly object queueLock = new object();
        private readonly Queue<(string kind, string peer, byte[] bytes)> incoming = new Queue<(string, string, byte[])>();
        private readonly string[] roster = new string[OnlineRelayQuizProtocol.PlayerCount];
        private readonly bool[] cameraReady = new bool[OnlineRelayQuizProtocol.PlayerCount];
        private readonly bool[] focused = { true, true, true, true };
        private readonly bool[] freshHand = { true, true, true, true };
        private readonly Dictionary<string, int> slotByPeer = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> candidates = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> candidateMessages = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> incomingSequences = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> outgoingSequences = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly List<OnlineRelayQuizGalleryEntry> records = new List<OnlineRelayQuizGalleryEntry>(3);
        private readonly Dictionary<string, OnlineRelayQuizDrawingTransfer> clientTransfers = new Dictionary<string, OnlineRelayQuizDrawingTransfer>(StringComparer.Ordinal);
        private readonly Dictionary<string, CanvasDrawingData> privateCache = new Dictionary<string, CanvasDrawingData>(StringComparer.Ordinal);
        private int queuedBytes;
        private bool queueOverflow;
        private volatile bool disposed;
        private bool hostConnected;
        private bool rosterLocked;
        private string sessionId;
        private int rosterGeneration;
        private int roundId;
        private int modeGeneration;
        private int startSignal;
        private int transitionGeneration;
        private PartyTransitionPhase transitionPhase;
        private int sceneReadyMask;
        private int stateRevision = 1;
        private int drawingRevision;
        private int localSlot = -1;
        private long outgoingSequence;
        private long hostIncomingSequence;
        private bool finalPending;
        private bool preparedPending;
        private float pendingElapsed;
        private float handshakeElapsed;
        private float publishElapsed;
        private float transitionElapsed;
        private CanvasDrawingData finalDrawing;
        private string finalAnswer;
        private string finalTransferId;
        private int finalOwnerSlot = -1;
        private OnlineRelayQuizDrawingTransfer finalReceiving;
        private OnlineRelayQuizGalleryEntry preparedDrawing;
        private int preparedDestination = -1;
        private string lastCapturedTransferId;
        private string secretWord = string.Empty;
        private PartyMode selectedMode;
        private bool hasSelectedMode;
        private bool modeStarted;
        private bool coopMuralFinalDisplay;
        private string transitionStatus = string.Empty;

        // partySize는 host가 정하는 이번 party의 정원이다. 배열·packet 크기는 그대로 PlayerCount(4)를 쓰고,
        // "몇 명 모이면 roster를 잠그고 시작하는가"만 이 값이 정한다. client는 host의 rosterLocked를 따르므로
        // 이 값을 알 필요가 없다.
        public OnlineRelayQuizSession(INetTransport transport, string expectedHostId, Func<string> wordSource,
            Func<CanvasDrawingData> drawingSource, Func<string> answerSource, int brushCount,
            int partySize = OnlineRelayQuizProtocol.PlayerCount)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (string.IsNullOrEmpty(expectedHostId)) throw new ArgumentException("Expected host identity is required.");
            if (brushCount <= 0 || brushCount > 32) throw new ArgumentOutOfRangeException(nameof(brushCount));
            if (partySize < PartySizeOption.SoloTestSize || partySize > OnlineRelayQuizProtocol.PlayerCount)
                throw new ArgumentOutOfRangeException(nameof(partySize));
            this.expectedHostId = expectedHostId;
            this.drawingSource = drawingSource;
            this.answerSource = answerSource;
            this.partySize = partySize;
            brushes = brushCount;
            // Publish는 0.25초마다 peer 수만큼 돈다. roster 문구를 매번 조립하지 않고 미리 만들어 둔다.
            rosterStatus = new string[OnlineRelayQuizProtocol.PlayerCount + 1];
            for (int count = 0; count < rosterStatus.Length; count++)
                rosterStatus[count] = count == partySize
                    ? partySize + "명 roster 연결됨"
                    : count + "/" + partySize + "명 연결됨";
            if (transport.IsHost)
            {
                sessionId = Guid.NewGuid().ToString("N");
                localSlot = 0;
                roster[0] = transport.LocalPlayerId;
                slotByPeer[transport.LocalPlayerId] = 0;
                rosterGeneration = 1;
                logic = new RelayQuizLogic(new RelayQuizTimings(), () =>
                {
                    secretWord = wordSource?.Invoke() ?? string.Empty;
                    return secretWord;
                }, () => finalDrawing, () => finalAnswer);
                logic.SetPlayerCount(Math.Max(partySize, RelayQuizLogic.MinPlayers), logic.PhaseGeneration);
                // 정원이 1이면 host 혼자로 roster가 이미 찬 상태다. join 경로를 지나지 않으므로 여기서 잠근다.
                if (RosterCount == partySize) rosterLocked = true;
            }
            View = new OnlineRelayQuizView { isHost = transport.IsHost, localSlot = localSlot };
            transport.OnPeerConnected += OnConnected;
            transport.OnPeerDisconnected += OnDisconnected;
            transport.OnMessage += OnMessage;
            if (transport.IsHost) Publish();
        }

        public OnlineRelayQuizView View { get; private set; }
        public bool IsHost => transport.IsHost;
        private int RosterCount
        {
            get
            {
                int count = 0;
                while (count < roster.Length && !string.IsNullOrEmpty(roster[count])) count++;
                return count;
            }
        }
        private int CandidateCount
        {
            get
            {
                lock (queueLock) return candidates.Count;
            }
        }
        private int CurrentOwnerSlot => IsHost && logic.State != RelayQuizState.Setup ? logic.PlayerIndex : -1;
        private int CurrentTurnId => roundId == 0 || !IsHost || logic.State == RelayQuizState.Setup ? 0 : logic.PlayerIndex + 1;
        private bool Assigned => IsHost || localSlot > 0 && !string.IsNullOrEmpty(sessionId);
        private bool Pending => finalPending || preparedPending;
        private int SelectedModeValue => hasSelectedMode ? (int)selectedMode : -1;
        private int ViewSelectedModeValue => View.hasSelectedMode ? (int)View.selectedMode : -1;
        private bool AllPlayersReady
        {
            get
            {
                if (RosterCount != partySize) return false;
                for (int i = 0; i < partySize; i++)
                    if (!cameraReady[i]) return false;
                return true;
            }
        }

        private void OnConnected(string peer)
        {
            if (string.IsNullOrEmpty(peer) || peer == transport.LocalPlayerId) return;
            if (IsHost && !TryAdmitCandidate(peer))
            {
                SendAdmissionReject(peer);
                return;
            }
            Queue("connected", peer, null);
        }

        private void OnDisconnected(string peer)
        {
            if (IsHost && !IsAdmittedPeer(peer)) return;
            Queue("disconnected", peer, null);
        }

        private void OnMessage(string peer, byte[] bytes)
        {
            if (bytes == null || bytes.Length > OnlineRelayQuizProtocol.MaxMessageBytes) return;
            if (IsHost && !TryAdmitMessage(peer)) return;
            Queue("message", peer, bytes);
        }

        private bool TryAdmitCandidate(string peer)
        {
            lock (queueLock)
            {
                if (disposed || slotByPeer.ContainsKey(peer)) return true;
                if (candidates.Contains(peer)) return false;
                int openSlots = partySize - RosterCount;
                if (rosterLocked || logic.State != RelayQuizState.Setup || openSlots <= 0
                    || candidates.Count >= Math.Min(MaxHandshakeCandidates, openSlots)) return false;
                candidates.Add(peer);
                return true;
            }
        }

        private bool IsAdmittedPeer(string peer)
        {
            if (string.IsNullOrEmpty(peer)) return false;
            lock (queueLock) return slotByPeer.ContainsKey(peer) || candidates.Contains(peer);
        }

        private bool TryAdmitMessage(string peer)
        {
            if (string.IsNullOrEmpty(peer)) return false;
            lock (queueLock)
            {
                if (slotByPeer.ContainsKey(peer)) return true;
                return candidates.Contains(peer) && candidateMessages.Add(peer);
            }
        }

        private bool IsCandidate(string peer)
        {
            lock (queueLock) return candidates.Contains(peer);
        }

        private void RemoveCandidate(string peer)
        {
            lock (queueLock)
            {
                candidates.Remove(peer);
                candidateMessages.Remove(peer);
            }
        }

        private string[] ExpireCandidates()
        {
            lock (queueLock)
            {
                string[] expired = new List<string>(candidates).ToArray();
                candidates.Clear();
                candidateMessages.Clear();
                return expired;
            }
        }

        private void Queue(string kind, string peer, byte[] bytes)
        {
            lock (queueLock)
            {
                if (disposed) return;
                int count = bytes == null ? 0 : bytes.Length;
                if (incoming.Count >= MaxQueuedMessages || queuedBytes + count > MaxQueuedBytes)
                {
                    queueOverflow = true;
                    return;
                }
                incoming.Enqueue((kind, peer, bytes == null ? null : (byte[])bytes.Clone()));
                queuedBytes += count;
            }
        }

        public void Tick(float deltaSeconds)
        {
            if (disposed || View.aborted) return;
            float delta = !float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds) && deltaSeconds > 0f
                ? deltaSeconds : 0f;
            bool wasPending = Pending;
            transport.Tick();
            while (true)
            {
                (string kind, string peer, byte[] bytes) next;
                lock (queueLock)
                {
                    if (queueOverflow)
                    {
                        Abort("수신 대기 데이터가 한도를 초과했습니다 · 새 초대가 필요합니다");
                        return;
                    }
                    if (incoming.Count == 0) break;
                    next = incoming.Dequeue();
                    queuedBytes -= next.bytes == null ? 0 : next.bytes.Length;
                }
                if (next.kind == "connected") Connect(next.peer);
                else if (next.kind == "disconnected") Disconnect(next.peer);
                else Receive(next.peer, next.bytes);
                if (View.aborted) return;
            }
            bool handshakePending = IsHost ? CandidateCount > 0 : hostConnected && !Assigned;
            if (handshakePending)
            {
                handshakeElapsed += delta;
                if (handshakeElapsed >= TransferTimeout)
                {
                    if (IsHost)
                    {
                        foreach (string peer in ExpireCandidates()) SendReject(peer);
                        handshakeElapsed = 0f;
                    }
                    else Abort("연결 확인 시간 초과 · 새 초대가 필요합니다");
                    if (View.aborted) return;
                }
            }
            else handshakeElapsed = 0f;
            if (!IsHost) return;
            if (transitionPhase == PartyTransitionPhase.LoadingGame
                || transitionPhase == PartyTransitionPhase.ReturningToLobby)
            {
                transitionElapsed += delta;
                if (transitionElapsed >= SceneBarrierTimeout)
                {
                    if (transitionPhase == PartyTransitionPhase.LoadingGame)
                        BeginReturningToLobby("Scene load time out · lobby로 돌아갑니다");
                    else
                    {
                        transitionStatus = "Lobby return time out · lobby 상태를 복구했습니다";
                        CompleteReturnToLobby();
                    }
                }
                else
                {
                    publishElapsed += delta;
                    if (publishElapsed >= 0.25f) Publish();
                }
                return;
            }
            if (transitionPhase != PartyTransitionPhase.InGame) return;
            if (Pending)
            {
                if (wasPending) pendingElapsed += delta;
                if (pendingElapsed >= TransferTimeout)
                    Abort("private 데이터 전송 시간 초과 · 새 초대가 필요합니다");
                return;
            }
            if (logic.Paused) return;
            if (logic.HasTimer && delta >= logic.RemainingSeconds
                && (logic.State == RelayQuizState.Drawing || logic.State == RelayQuizState.Guessing))
            {
                BeginFinal();
                return;
            }
            if (logic.Tick(delta))
            {
                AdvanceRevision();
                Publish();
                return;
            }
            publishElapsed += delta;
            if (publishElapsed >= 0.25f) Publish();
        }

        private void Connect(string peer)
        {
            if (string.IsNullOrEmpty(peer) || peer == transport.LocalPlayerId) return;
            if (IsHost)
            {
                if (slotByPeer.ContainsKey(peer)) return;
                if (!IsCandidate(peer)) SendReject(peer);
                return;
            }
            if (peer != expectedHostId) return;
            hostConnected = true;
            SendHello();
        }

        private void Disconnect(string peer)
        {
            if (IsHost)
            {
                if (slotByPeer.ContainsKey(peer) && slotByPeer[peer] > 0)
                    Abort("연결이 끊겼습니다 · host migration 없이 새 초대가 필요합니다");
                else RemoveCandidate(peer);
            }
            else if (peer == expectedHostId || peer == "host")
            {
                Abort("연결이 끊겼습니다 · host migration 없이 새 초대가 필요합니다");
            }
        }

        private void Receive(string peer, byte[] bytes)
        {
            if (!OnlineRelayQuizProtocol.TryDecode(bytes, out OnlineRelayQuizPacket packet)) return;
            if (IsHost)
            {
                if (packet.kind == "hello") ReceiveHello(peer, packet);
                else ReceiveFromClient(peer, packet);
            }
            else ReceiveFromHost(peer, packet);
        }

        private void ReceiveHello(string peer, OnlineRelayQuizPacket packet)
        {
            OnlineRelayQuizHello hello = OnlineRelayQuizProtocol.Read<OnlineRelayQuizHello>(packet.payload);
            if (hello == null || hello.playerId != peer || hello.hostId != transport.LocalPlayerId
                || hello.brushes != brushes || packet.ownerSlot != -1)
            {
                RemoveCandidate(peer);
                SendReject(peer);
                return;
            }
            if (!IsCandidate(peer) || rosterLocked || logic.State != RelayQuizState.Setup
                || RosterCount >= partySize)
            {
                SendReject(peer);
                return;
            }
            if (incomingSequences.TryGetValue(peer, out long previous) && packet.sequence <= previous) return;
            int slot = RosterCount;
            roster[slot] = peer;
            slotByPeer[peer] = slot;
            incomingSequences[peer] = packet.sequence;
            RemoveCandidate(peer);
            rosterGeneration++;
            if (RosterCount == partySize) rosterLocked = true;
            AdvanceRevision();
            SendToPeer(peer, "welcome", new OnlineRelayQuizWelcome
            {
                hostId = transport.LocalPlayerId,
                playerId = peer,
                assignedSlot = slot,
                brushes = brushes
            }, -1);
            Publish();
        }

        private void ReceiveFromClient(string peer, OnlineRelayQuizPacket packet)
        {
            if (!slotByPeer.TryGetValue(peer, out int senderSlot) || senderSlot <= 0) return;
            bool setupReady = packet.kind == "ready" && logic.State == RelayQuizState.Setup
                && PacketMatchesSetup(packet);
            bool transitionCommand = (packet.kind == "scene-ready" || packet.kind == "lobby-ready"
                || packet.kind == "scene-load-failed") && PacketMatchesTransition(packet);
            if (!setupReady && !transitionCommand && !PacketMatchesHost(packet)) return;
            if (incomingSequences.TryGetValue(peer, out long previous) && packet.sequence <= previous) return;
            incomingSequences[peer] = packet.sequence;
            if (packet.kind == "abort")
            {
                Abort("참가자가 세션을 나갔습니다 · 새 초대가 필요합니다", false);
                return;
            }
            switch (packet.kind)
            {
                case "ready":
                    if (packet.ownerSlot == senderSlot)
                    {
                        OnlineRelayQuizCommand ready = OnlineRelayQuizProtocol.Read<OnlineRelayQuizCommand>(packet.payload);
                        if (ready != null) Ready(senderSlot, ready.cameraReady);
                    }
                    break;
                case "action":
                    if (packet.ownerSlot == senderSlot)
                    {
                        OnlineRelayQuizCommand action = OnlineRelayQuizProtocol.Read<OnlineRelayQuizCommand>(packet.payload);
                        if (action != null) Act(senderSlot, action.action);
                    }
                    break;
                case "conditions":
                    if (packet.ownerSlot == senderSlot)
                    {
                        OnlineRelayQuizCommand condition = OnlineRelayQuizProtocol.Read<OnlineRelayQuizCommand>(packet.payload);
                        if (condition != null) SetConditions(senderSlot, condition.focused, condition.freshHand);
                    }
                    break;
                case "final-drawing":
                    if (packet.ownerSlot == senderSlot) ReceiveFinalDrawing(senderSlot, packet);
                    break;
                case "final-answer":
                    if (packet.ownerSlot == senderSlot) ReceiveFinalAnswer(senderSlot, packet);
                    break;
                case "prepared-ack":
                    ReceivePreparedAck(senderSlot, packet);
                    break;
                case "scene-ready":
                    ReceiveTransitionReady(senderSlot, packet, PartyTransitionPhase.LoadingGame);
                    break;
                case "lobby-ready":
                    ReceiveTransitionReady(senderSlot, packet, PartyTransitionPhase.ReturningToLobby);
                    break;
                case "scene-load-failed":
                    ReceiveSceneLoadFailure(senderSlot, packet);
                    break;
            }
        }

        private void ReceiveFromHost(string peer, OnlineRelayQuizPacket packet)
        {
            if (!hostConnected || peer != expectedHostId && peer != "host") return;
            if (packet.kind == "reject")
            {
                Abort("4인 roster가 잠겨 참가할 수 없습니다 · 새 초대가 필요합니다", false);
                return;
            }
            if (packet.kind == "welcome" && !Assigned)
            {
                ReceiveWelcome(packet);
                return;
            }
            if (!Assigned || packet.sessionId != sessionId || packet.sequence <= hostIncomingSequence) return;
            if (packet.kind == "view")
            {
                hostIncomingSequence = packet.sequence;
                ReceiveView(packet);
                return;
            }
            if (!PacketMatchesView(packet)) return;
            hostIncomingSequence = packet.sequence;
            if (packet.kind == "abort")
            {
                Abort("세션이 종료되었습니다 · 새 초대가 필요합니다", false);
                return;
            }
            if (packet.kind == "capture")
            {
                OnlineRelayQuizCapture capture = OnlineRelayQuizProtocol.Read<OnlineRelayQuizCapture>(packet.payload);
                if (capture != null && packet.ownerSlot == localSlot) CaptureFinal(capture);
            }
            else if (packet.kind == "prepare-drawing") ReceivePrivateDrawing(packet, false);
            else if (packet.kind == "gallery-drawing") ReceivePrivateDrawing(packet, true);
        }

        private void ReceiveWelcome(OnlineRelayQuizPacket packet)
        {
            OnlineRelayQuizWelcome welcome = OnlineRelayQuizProtocol.Read<OnlineRelayQuizWelcome>(packet.payload);
            if (welcome == null || welcome.hostId != expectedHostId || welcome.playerId != transport.LocalPlayerId
                || welcome.brushes != brushes || welcome.assignedSlot <= 0
                || welcome.assignedSlot >= OnlineRelayQuizProtocol.PlayerCount
                || string.IsNullOrEmpty(packet.sessionId) || packet.rosterGeneration <= 0) return;
            sessionId = packet.sessionId;
            rosterGeneration = packet.rosterGeneration;
            roundId = packet.roundId;
            modeGeneration = packet.modeGeneration;
            startSignal = packet.startSignal;
            transitionGeneration = packet.transitionGeneration;
            transitionPhase = (PartyTransitionPhase)packet.transitionPhase;
            sceneReadyMask = packet.sceneReadyMask;
            hasSelectedMode = packet.selectedMode >= 0;
            if (hasSelectedMode) selectedMode = (PartyMode)packet.selectedMode;
            stateRevision = packet.revision;
            localSlot = welcome.assignedSlot;
            hostIncomingSequence = packet.sequence;
            ClearPrivateCache();
            View = new OnlineRelayQuizView
            {
                sessionId = sessionId,
                rosterGeneration = rosterGeneration,
                roundId = roundId,
                revision = stateRevision,
                hasSelectedMode = hasSelectedMode,
                selectedMode = selectedMode,
                modeGeneration = modeGeneration,
                startSignal = startSignal,
                transitionGeneration = transitionGeneration,
                transitionPhase = transitionPhase,
                sceneReadyMask = sceneReadyMask,
                localSlot = localSlot,
                isHost = false
            };
        }

        private bool PacketMatchesHost(OnlineRelayQuizPacket packet)
        {
            return packet.sessionId == sessionId && packet.rosterGeneration == rosterGeneration
                && packet.roundId == roundId && packet.turnId == CurrentTurnId
                && packet.revision == stateRevision && packet.selectedMode == SelectedModeValue
                && packet.modeGeneration == modeGeneration && packet.startSignal == startSignal
                && packet.transitionGeneration == transitionGeneration
                && packet.transitionPhase == (int)transitionPhase
                && packet.sceneReadyMask == sceneReadyMask;
        }

        private bool PacketMatchesSetup(OnlineRelayQuizPacket packet)
        {
            return packet.sessionId == sessionId && packet.rosterGeneration == rosterGeneration
                && packet.roundId == roundId && packet.turnId == 0
                && packet.revision > 0 && packet.revision <= stateRevision
                && packet.modeGeneration <= modeGeneration && packet.startSignal == startSignal
                && packet.transitionGeneration <= transitionGeneration;
        }

        private bool PacketMatchesTransition(OnlineRelayQuizPacket packet)
        {
            return packet.sessionId == sessionId && packet.rosterGeneration == rosterGeneration
                && packet.roundId == roundId && packet.selectedMode == SelectedModeValue
                && packet.modeGeneration == modeGeneration && packet.startSignal == startSignal
                && packet.transitionGeneration == transitionGeneration
                && packet.transitionPhase == (int)transitionPhase;
        }

        private bool PacketMatchesView(OnlineRelayQuizPacket packet)
        {
            return packet.sessionId == View.sessionId && packet.rosterGeneration == View.rosterGeneration
                && packet.roundId == View.roundId && packet.turnId == View.turnId
                && packet.revision == View.revision && packet.selectedMode == ViewSelectedModeValue
                && packet.modeGeneration == View.modeGeneration && packet.startSignal == View.startSignal
                && packet.transitionGeneration == View.transitionGeneration
                && packet.transitionPhase == (int)View.transitionPhase
                && packet.sceneReadyMask == View.sceneReadyMask;
        }

        public void SetReady(bool ready)
        {
            if (disposed || View.aborted || View.state != RelayQuizState.Setup || !Assigned
                || View.transitionPhase != PartyTransitionPhase.Lobby || View.modeStarted) return;
            if (IsHost) Ready(0, ready);
            else SendToHost("ready", new OnlineRelayQuizCommand { cameraReady = ready }, localSlot);
        }

        private void Ready(int slot, bool ready)
        {
            if (logic.State != RelayQuizState.Setup || transitionPhase != PartyTransitionPhase.Lobby
                || modeStarted || slot < 0 || slot >= RosterCount
                || ready && !freshHand[slot] || cameraReady[slot] == ready) return;
            cameraReady[slot] = ready;
            AdvanceRevision();
            Publish();
        }

        public bool OpenModeSelector()
        {
            if (disposed || View.aborted || !IsHost || logic.State != RelayQuizState.Setup
                || transitionPhase != PartyTransitionPhase.Lobby || modeStarted || hasSelectedMode
                || !rosterLocked || RosterCount != partySize
                || partySize < RelayQuizLogic.MinPlayers
                || !AllPlayersReady) return false;
            modeGeneration++;
            transitionGeneration++;
            transitionPhase = PartyTransitionPhase.SelectingMode;
            sceneReadyMask = 0;
            transitionElapsed = 0f;
            transitionStatus = string.Empty;
            AdvanceRevision();
            Publish();
            return true;
        }

        public bool SelectModeAndBeginLoad(PartyMode mode)
        {
            if (disposed || View.aborted || !IsHost || logic.State != RelayQuizState.Setup
                || transitionPhase != PartyTransitionPhase.SelectingMode || modeStarted || hasSelectedMode
                || !rosterLocked || RosterCount != partySize
                || partySize < RelayQuizLogic.MinPlayers
                || !AllPlayersReady
                || !PartyModeCatalog.TryGet(mode, out _)) return false;
            selectedMode = mode;
            hasSelectedMode = true;
            roundId++;
            modeGeneration++;
            startSignal++;
            transitionGeneration++;
            transitionPhase = PartyTransitionPhase.LoadingGame;
            sceneReadyMask = 0;
            transitionElapsed = 0f;
            coopMuralFinalDisplay = false;
            ClearRoundPayloads();
            AdvanceRevision();
            Publish();
            return true;
        }

        public bool MarkLocalSceneReady(int generation)
        {
            if (disposed || View.aborted || !Assigned || generation != View.transitionGeneration
                || View.transitionPhase != PartyTransitionPhase.LoadingGame) return false;
            if (IsHost) return MarkTransitionReady(0, generation, PartyTransitionPhase.LoadingGame);
            SendToHost("scene-ready", new OnlineRelayQuizTransitionCommand
            {
                transitionGeneration = generation
            }, localSlot);
            return true;
        }

        public bool RequestReturnToLobby()
        {
            if (disposed || View.aborted || !IsHost || transitionPhase != PartyTransitionPhase.InGame
                || !modeStarted || !hasSelectedMode) return false;
            bool resultVisible = selectedMode == PartyMode.CoopMural
                ? coopMuralFinalDisplay
                : logic.State == RelayQuizState.Reveal || logic.State == RelayQuizState.Gallery;
            if (!resultVisible) return false;
            BeginReturningToLobby();
            return true;
        }

        public bool MarkCoopMuralFinalDisplay()
        {
            if (disposed || View.aborted || !IsHost
                || transitionPhase != PartyTransitionPhase.InGame
                || !modeStarted || !hasSelectedMode || selectedMode != PartyMode.CoopMural
                || coopMuralFinalDisplay) return false;
            coopMuralFinalDisplay = true;
            AdvanceRevision();
            Publish();
            return true;
        }

        public bool MarkLocalLobbyReady(int generation)
        {
            if (disposed || View.aborted || !Assigned || generation != View.transitionGeneration
                || View.transitionPhase != PartyTransitionPhase.ReturningToLobby) return false;
            if (IsHost) return MarkTransitionReady(0, generation, PartyTransitionPhase.ReturningToLobby);
            SendToHost("lobby-ready", new OnlineRelayQuizTransitionCommand
            {
                transitionGeneration = generation
            }, localSlot);
            return true;
        }

        public void ReportSceneLoadFailure(int generation, PartySceneLoadFailure failure)
        {
            if (disposed || View.aborted || !Assigned
                || failure <= PartySceneLoadFailure.None
                || failure > PartySceneLoadFailure.InvalidTransition
                || generation != View.transitionGeneration
                || View.transitionPhase != PartyTransitionPhase.LoadingGame) return;
            if (IsHost) BeginReturningToLobby("Scene load failed: " + failure);
            else SendToHost("scene-load-failed", new OnlineRelayQuizTransitionCommand
            {
                transitionGeneration = generation,
                failure = (int)failure
            }, localSlot);
        }

        private void ReceiveTransitionReady(
            int senderSlot,
            OnlineRelayQuizPacket packet,
            PartyTransitionPhase expectedPhase)
        {
            OnlineRelayQuizTransitionCommand command =
                OnlineRelayQuizProtocol.Read<OnlineRelayQuizTransitionCommand>(packet.payload);
            if (command == null || packet.ownerSlot != senderSlot) return;
            MarkTransitionReady(senderSlot, command.transitionGeneration, expectedPhase);
        }

        private bool MarkTransitionReady(
            int senderSlot,
            int generation,
            PartyTransitionPhase expectedPhase)
        {
            if (!IsHost || transitionPhase != expectedPhase || generation != transitionGeneration
                || senderSlot < 0 || senderSlot >= RosterCount) return false;
            int bit = 1 << senderSlot;
            if ((sceneReadyMask & bit) != 0) return false;
            sceneReadyMask |= bit;
            if (sceneReadyMask == (1 << partySize) - 1)
            {
                if (expectedPhase == PartyTransitionPhase.LoadingGame) StartSelectedDomain();
                else CompleteReturnToLobby();
            }
            else
            {
                AdvanceRevision();
                Publish();
            }
            return true;
        }

        private void StartSelectedDomain()
        {
            if (transitionPhase != PartyTransitionPhase.LoadingGame || modeStarted) return;
            if (selectedMode != PartyMode.CoopMural)
            {
                logic.SetPlayerCount(partySize, logic.PhaseGeneration);
                if (!logic.StartGame(logic.PhaseGeneration))
                {
                    BeginReturningToLobby("Selected mode could not start");
                    return;
                }
            }
            modeStarted = true;
            transitionPhase = PartyTransitionPhase.InGame;
            transitionElapsed = 0f;
            AdvanceRevision();
            Publish();
        }

        private void ReceiveSceneLoadFailure(int senderSlot, OnlineRelayQuizPacket packet)
        {
            OnlineRelayQuizTransitionCommand command =
                OnlineRelayQuizProtocol.Read<OnlineRelayQuizTransitionCommand>(packet.payload);
            if (command == null || packet.ownerSlot != senderSlot
                || command.transitionGeneration != transitionGeneration
                || transitionPhase != PartyTransitionPhase.LoadingGame
                || command.failure <= (int)PartySceneLoadFailure.None
                || command.failure > (int)PartySceneLoadFailure.InvalidTransition) return;
            BeginReturningToLobby("Scene load failed: " + (PartySceneLoadFailure)command.failure);
        }

        private void BeginReturningToLobby(string status = "")
        {
            if (transitionPhase == PartyTransitionPhase.ReturningToLobby
                || transitionPhase == PartyTransitionPhase.Lobby) return;
            if (logic.State == RelayQuizState.Reveal)
                logic.OpenGallery(logic.PhaseGeneration);
            if (logic.State == RelayQuizState.Gallery)
                logic.Restart(logic.PhaseGeneration);
            rosterGeneration++;
            transitionGeneration++;
            transitionPhase = PartyTransitionPhase.ReturningToLobby;
            sceneReadyMask = 0;
            transitionElapsed = 0f;
            transitionStatus = status ?? string.Empty;
            modeStarted = false;
            coopMuralFinalDisplay = false;
            Array.Clear(cameraReady, 0, cameraReady.Length);
            ClearRoundPayloads();
            AdvanceRevision();
            Publish();
        }

        private void CompleteReturnToLobby()
        {
            if (transitionPhase != PartyTransitionPhase.ReturningToLobby) return;
            transitionPhase = PartyTransitionPhase.Lobby;
            sceneReadyMask = 0;
            transitionElapsed = 0f;
            hasSelectedMode = false;
            selectedMode = default;
            modeStarted = false;
            coopMuralFinalDisplay = false;
            modeGeneration++;
            AdvanceRevision();
            Publish();
        }

        public void Execute(RelayQuizAction action, int generation)
        {
            if (disposed || View.aborted || !Assigned || generation != View.generation) return;
            if (IsHost) Act(0, action);
            else SendToHost("action", new OnlineRelayQuizCommand { action = action }, localSlot);
        }

        private void Act(int senderSlot, RelayQuizAction action)
        {
            if (logic.State == RelayQuizState.Setup || senderSlot < 0 || senderSlot >= RosterCount) return;
            int owner = logic.PlayerIndex;
            bool changed = false;
            if (action == RelayQuizAction.Resume && senderSlot == owner && logic.Paused)
            {
                if (focused[senderSlot] && (logic.State != RelayQuizState.Drawing || freshHand[senderSlot]))
                    changed = logic.Resume(logic.PhaseGeneration);
            }
            else if (logic.Paused || Pending) return;
            else if (action == RelayQuizAction.Ready && senderSlot == owner)
                changed = logic.ConfirmReady(logic.PhaseGeneration);
            else if (action == RelayQuizAction.CompleteDrawing && senderSlot == owner
                && logic.State == RelayQuizState.Drawing)
            {
                BeginFinal();
                return;
            }
            else if (action == RelayQuizAction.Submit && senderSlot == partySize - 1
                && senderSlot == owner && logic.State == RelayQuizState.Guessing)
            {
                BeginFinal();
                return;
            }
            else if (action == RelayQuizAction.OpenGallery && senderSlot == 0
                && logic.OpenGallery(logic.PhaseGeneration))
            {
                AdvanceRevision();
                Publish();
                SendGalleryDrawings();
                return;
            }
            else if (action == RelayQuizAction.Restart && senderSlot == 0
                && transitionPhase == PartyTransitionPhase.InGame
                && (logic.State == RelayQuizState.Reveal || logic.State == RelayQuizState.Gallery))
            {
                BeginReturningToLobby();
                return;
            }
            if (!changed) return;
            AdvanceRevision();
            Publish();
        }

        public void UpdateLocalConditions(bool hasFocus, bool hasFreshHand)
        {
            if (disposed || View.aborted || !Assigned) return;
            if (IsHost) SetConditions(0, hasFocus, hasFreshHand);
            else
            {
                bool changed = focused[localSlot] != hasFocus || freshHand[localSlot] != hasFreshHand;
                focused[localSlot] = hasFocus;
                freshHand[localSlot] = hasFreshHand;
                if (changed) SendToHost("conditions", new OnlineRelayQuizCommand
                {
                    focused = hasFocus,
                    freshHand = hasFreshHand
                }, localSlot);
            }
        }

        private void SetConditions(int slot, bool hasFocus, bool hasFreshHand)
        {
            focused[slot] = hasFocus;
            freshHand[slot] = hasFreshHand;
            if (logic.State == RelayQuizState.Setup)
            {
                if (!modeStarted && !hasFreshHand && cameraReady[slot]) Ready(slot, false);
                return;
            }
            if (slot != logic.PlayerIndex || Pending || logic.Paused
                || !RelayQuizLogic.ShouldAutoPause(logic.State, hasFocus, hasFreshHand)) return;
            if (logic.RequestPause())
            {
                AdvanceRevision();
                Publish();
            }
        }

        private void BeginFinal()
        {
            if (Pending || logic.Paused || logic.State != RelayQuizState.Drawing
                && logic.State != RelayQuizState.Guessing) return;
            finalPending = true;
            pendingElapsed = 0f;
            finalReceiving = null;
            finalDrawing = null;
            finalAnswer = null;
            finalOwnerSlot = logic.PlayerIndex;
            finalTransferId = "final-r" + roundId + "-t" + CurrentTurnId + "-o" + finalOwnerSlot
                + "-v" + stateRevision;
            Publish();
            var capture = new OnlineRelayQuizCapture
            {
                transferId = finalTransferId,
                ownerSlot = finalOwnerSlot,
                revision = stateRevision
            };
            if (finalOwnerSlot == 0) CaptureFinal(capture);
            else SendToSlot(finalOwnerSlot, "capture", capture, finalOwnerSlot);
        }

        private void CaptureFinal(OnlineRelayQuizCapture capture)
        {
            if (capture == null || capture.ownerSlot != localSlot || capture.transferId == lastCapturedTransferId
                || !View.active && !IsHost || View.state != RelayQuizState.Drawing
                && View.state != RelayQuizState.Guessing) return;
            lastCapturedTransferId = capture.transferId;
            if ((IsHost ? logic.State : View.state) == RelayQuizState.Drawing)
            {
                CanvasDrawingData captured = drawingSource?.Invoke();
                if (captured == null) return;
                if (!OnlineRelayQuizProtocol.TryDrawingBytes(captured, brushes, out byte[] bytes))
                {
                    Abort("그림 데이터가 허용 범위를 벗어났습니다 · 새 초대가 필요합니다");
                    return;
                }
                if (IsHost)
                {
                    OnlineRelayQuizProtocol.TryDrawing(captured, brushes, out finalDrawing);
                    CommitFinal();
                }
                else SendChunksToHost("final-drawing", capture.transferId, bytes, localSlot);
            }
            else
            {
                string captured = answerSource?.Invoke();
                if (captured == null) return;
                if (captured.Length > OnlineRelayQuizProtocol.MaxAnswerCharacters)
                {
                    Abort("답 길이가 허용 범위를 벗어났습니다 · 새 초대가 필요합니다");
                    return;
                }
                if (IsHost)
                {
                    finalAnswer = captured;
                    CommitFinal();
                }
                else SendToHost("final-answer", new OnlineRelayQuizCommand
                {
                    complete = true,
                    text = captured
                }, localSlot);
            }
        }

        private void ReceiveFinalDrawing(int senderSlot, OnlineRelayQuizPacket packet)
        {
            if (!finalPending || logic.State != RelayQuizState.Drawing || senderSlot != finalOwnerSlot) return;
            OnlineRelayQuizChunk chunk = OnlineRelayQuizProtocol.Read<OnlineRelayQuizChunk>(packet.payload);
            if (chunk == null || chunk.id != finalTransferId) return;
            if (finalReceiving == null) finalReceiving = new OnlineRelayQuizDrawingTransfer();
            if (!finalReceiving.Add(chunk, out byte[] bytes))
            {
                Abort("그림 전송 데이터가 올바르지 않습니다 · 새 초대가 필요합니다");
                return;
            }
            if (bytes == null) return;
            if (!OnlineRelayQuizProtocol.TryReadDrawing(bytes, brushes, out finalDrawing))
            {
                Abort("그림 데이터 검증에 실패했습니다 · 새 초대가 필요합니다");
                return;
            }
            CommitFinal();
        }

        private void ReceiveFinalAnswer(int senderSlot, OnlineRelayQuizPacket packet)
        {
            if (!finalPending || logic.State != RelayQuizState.Guessing || senderSlot != finalOwnerSlot
                || senderSlot != partySize - 1) return;
            OnlineRelayQuizCommand command = OnlineRelayQuizProtocol.Read<OnlineRelayQuizCommand>(packet.payload);
            if (command == null || !command.complete || command.text == null
                || command.text.Length > OnlineRelayQuizProtocol.MaxAnswerCharacters) return;
            finalAnswer = command.text;
            CommitFinal();
        }

        private void CommitFinal()
        {
            if (!finalPending) return;
            if (logic.State == RelayQuizState.Drawing)
            {
                if (finalDrawing == null) return;
                int owner = finalOwnerSlot;
                if (!logic.CompleteDrawing(logic.PhaseGeneration)) return;
                var entry = new OnlineRelayQuizGalleryEntry
                {
                    drawingId = "drawing-r" + roundId + "-o" + owner + "-v" + (drawingRevision + 1),
                    ownerSlot = owner,
                    revision = ++drawingRevision,
                    drawing = CanvasDrawingData.DeepCopy(finalDrawing)
                };
                records.Add(entry);
                finalPending = false;
                finalReceiving = null;
                finalDrawing = null;
                finalTransferId = null;
                finalOwnerSlot = -1;
                AdvanceRevision();
                PrepareDrawing(entry, logic.PlayerIndex);
                return;
            }
            if (logic.State != RelayQuizState.Guessing || finalAnswer == null
                || !logic.SubmitAnswer(logic.PhaseGeneration)) return;
            finalPending = false;
            finalTransferId = null;
            finalOwnerSlot = -1;
            AdvanceRevision();
            Publish();
        }

        private void PrepareDrawing(OnlineRelayQuizGalleryEntry entry, int destinationSlot)
        {
            if (entry == null || destinationSlot <= entry.ownerSlot
                || destinationSlot >= OnlineRelayQuizProtocol.PlayerCount)
            {
                Abort("private 그림 수신자를 결정할 수 없습니다 · 새 초대가 필요합니다");
                return;
            }
            if (!OnlineRelayQuizProtocol.TryDrawingBytes(entry.drawing, brushes, out byte[] bytes))
            {
                Abort("private 그림을 전송할 수 없습니다 · 새 초대가 필요합니다");
                return;
            }
            preparedDrawing = entry;
            preparedDestination = destinationSlot;
            preparedPending = true;
            pendingElapsed = 0f;
            Publish();
            SendChunksToSlot(destinationSlot, "prepare-drawing", entry.drawingId, bytes, entry.ownerSlot);
        }

        private void ReceivePreparedAck(int senderSlot, OnlineRelayQuizPacket packet)
        {
            if (!preparedPending || preparedDrawing == null || senderSlot != preparedDestination
                || packet.ownerSlot != preparedDrawing.ownerSlot) return;
            OnlineRelayQuizPreparedAck ack = OnlineRelayQuizProtocol.Read<OnlineRelayQuizPreparedAck>(packet.payload);
            if (ack == null || ack.destinationSlot != preparedDestination || ack.ownerSlot != preparedDrawing.ownerSlot
                || ack.drawingId != preparedDrawing.drawingId || ack.revision != preparedDrawing.revision) return;
            preparedPending = false;
            pendingElapsed = 0f;
            AdvanceRevision();
            Publish();
        }

        private void ReceivePrivateDrawing(OnlineRelayQuizPacket packet, bool gallery)
        {
            OnlineRelayQuizChunk chunk = OnlineRelayQuizProtocol.Read<OnlineRelayQuizChunk>(packet.payload);
            if (chunk == null || !IsAuthorizedDrawing(chunk.id, gallery, packet.ownerSlot)) return;
            if (!clientTransfers.TryGetValue(chunk.id, out OnlineRelayQuizDrawingTransfer transfer))
            {
                transfer = new OnlineRelayQuizDrawingTransfer();
                clientTransfers[chunk.id] = transfer;
            }
            if (!transfer.Add(chunk, out byte[] bytes))
            {
                Abort("그림 전송 데이터가 올바르지 않습니다 · 새 초대가 필요합니다");
                return;
            }
            if (bytes == null) return;
            if (!OnlineRelayQuizProtocol.TryReadDrawing(bytes, brushes, out CanvasDrawingData drawing))
            {
                Abort("그림 데이터 검증에 실패했습니다 · 새 초대가 필요합니다");
                return;
            }
            clientTransfers.Remove(chunk.id);
            privateCache[chunk.id] = drawing;
            AttachPrivatePayloads(View);
            View.payloadRevision++;
            if (!gallery)
            {
                SendToHost("prepared-ack", new OnlineRelayQuizPreparedAck
                {
                    drawingId = View.drawingId,
                    destinationSlot = localSlot,
                    ownerSlot = View.drawingOwnerSlot,
                    revision = View.drawingRevision
                }, View.drawingOwnerSlot);
            }
        }

        private bool IsAuthorizedDrawing(string id, bool gallery, int ownerSlot)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!gallery) return id == View.drawingId && ownerSlot == View.drawingOwnerSlot && View.CanSeeDrawing;
            if (View.state != RelayQuizState.Gallery || View.gallery == null) return false;
            foreach (OnlineRelayQuizGalleryEntry entry in View.gallery)
                if (entry != null && entry.drawingId == id && entry.ownerSlot == ownerSlot) return true;
            return false;
        }

        private void ReceiveView(OnlineRelayQuizPacket packet)
        {
            OnlineRelayQuizView next = OnlineRelayQuizProtocol.Read<OnlineRelayQuizView>(packet.payload);
            if (next == null || next.sessionId != sessionId || next.localSlot != localSlot || next.isHost
                || next.rosterGeneration != packet.rosterGeneration || next.roundId != packet.roundId
                || next.turnId != packet.turnId || next.ownerSlot != packet.ownerSlot
                || next.revision != packet.revision || next.modeGeneration != packet.modeGeneration
                || next.startSignal != packet.startSignal
                || next.transitionGeneration != packet.transitionGeneration
                || (int)next.transitionPhase != packet.transitionPhase
                || next.sceneReadyMask != packet.sceneReadyMask
                || (next.hasSelectedMode ? (int)next.selectedMode : -1) != packet.selectedMode
                || next.rosterGeneration < View.rosterGeneration
                || next.roundId < View.roundId || next.revision < View.revision
                || next.modeGeneration < View.modeGeneration || next.startSignal < View.startSignal
                || next.transitionGeneration < View.transitionGeneration
                || next.generation < View.generation || next.serial < View.serial
                || !Enum.IsDefined(typeof(RelayQuizState), next.state)
                || next.hasSelectedMode && !Enum.IsDefined(typeof(PartyMode), next.selectedMode)
                || !PartyTransitionPhaseRules.IsDefined(next.transitionPhase)
                || next.sceneReadyMask < 0
                || next.sceneReadyMask >= 1 << OnlineRelayQuizProtocol.PlayerCount
                || float.IsNaN(next.remaining) || float.IsInfinity(next.remaining)
                || next.remaining < 0f || next.remaining > 60f || next.rosterCount < 1
                || next.rosterCount > OnlineRelayQuizProtocol.PlayerCount
                || next.roster == null || next.roster.Length != OnlineRelayQuizProtocol.PlayerCount
                || (next.word?.Length ?? 0) > 128
                || (next.answer?.Length ?? 0) > OnlineRelayQuizProtocol.MaxAnswerCharacters) return;
            bool reset = next.rosterGeneration != View.rosterGeneration || next.roundId != View.roundId
                || next.modeGeneration != View.modeGeneration;
            if (reset) ClearPrivateCache();
            if (next.state != RelayQuizState.Gallery) next.gallery = Array.Empty<OnlineRelayQuizGalleryEntry>();
            if (localSlot != 0) next.word = string.Empty;
            if (next.state != RelayQuizState.Reveal && next.state != RelayQuizState.Gallery)
            {
                next.answer = string.Empty;
                next.correct = false;
            }
            PurgeUnauthorizedPayloads(next);
            AttachPrivatePayloads(next);
            // client는 정원을 모른다. host가 잠근 roster를 신뢰하되, 정원 하한만 검사한다.
            next.connected = next.rosterLocked && next.rosterCount >= RelayQuizLogic.MinPlayers;
            View = next;
            rosterGeneration = next.rosterGeneration;
            roundId = next.roundId;
            modeGeneration = next.modeGeneration;
            startSignal = next.startSignal;
            transitionGeneration = next.transitionGeneration;
            transitionPhase = next.transitionPhase;
            sceneReadyMask = next.sceneReadyMask;
            hasSelectedMode = next.hasSelectedMode;
            selectedMode = next.selectedMode;
            modeStarted = next.modeStarted;
            stateRevision = next.revision;
        }

        private void PurgeUnauthorizedPayloads(OnlineRelayQuizView next)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal);
            if (next.CanSeeDrawing && !string.IsNullOrEmpty(next.drawingId)) allowed.Add(next.drawingId);
            if (next.state == RelayQuizState.Gallery && next.gallery != null)
                foreach (OnlineRelayQuizGalleryEntry entry in next.gallery)
                    if (entry != null && !string.IsNullOrEmpty(entry.drawingId)) allowed.Add(entry.drawingId);
            var remove = new List<string>();
            foreach (string id in privateCache.Keys) if (!allowed.Contains(id)) remove.Add(id);
            foreach (string id in remove) privateCache.Remove(id);
            remove.Clear();
            foreach (string id in clientTransfers.Keys) if (!allowed.Contains(id)) remove.Add(id);
            foreach (string id in remove) clientTransfers.Remove(id);
        }

        private void AttachPrivatePayloads(OnlineRelayQuizView view)
        {
            view.drawing = null;
            view.referenceDrawing = null;
            if (view.CanSeeDrawing && privateCache.TryGetValue(view.drawingId, out CanvasDrawingData current))
            {
                view.drawing = current;
                view.referenceDrawing = current;
            }
            if (view.gallery == null) view.gallery = Array.Empty<OnlineRelayQuizGalleryEntry>();
            foreach (OnlineRelayQuizGalleryEntry entry in view.gallery)
                if (entry != null && privateCache.TryGetValue(entry.drawingId, out CanvasDrawingData drawing))
                    entry.drawing = drawing;
        }

        private OnlineRelayQuizView BuildView(int recipientSlot)
        {
            bool setup = logic.State == RelayQuizState.Setup;
            int owner = setup ? -1 : logic.PlayerIndex;
            bool active = !setup && recipientSlot == owner;
            OnlineRelayQuizGalleryEntry current = CurrentPrivateDrawing(recipientSlot);
            OnlineRelayQuizGalleryEntry[] gallery = logic.State == RelayQuizState.Gallery
                ? BuildGallery(recipientSlot == 0) : Array.Empty<OnlineRelayQuizGalleryEntry>();
            return new OnlineRelayQuizView
            {
                state = logic.State,
                generation = logic.PhaseGeneration,
                serial = logic.StateSerial,
                sessionId = sessionId,
                rosterGeneration = rosterGeneration,
                roundId = roundId,
                turnId = CurrentTurnId,
                ownerSlot = owner,
                revision = stateRevision,
                rosterCount = RosterCount,
                rosterLocked = rosterLocked,
                localSlot = recipientSlot,
                roster = (string[])roster.Clone(),
                isHost = recipientSlot == 0,
                connected = rosterLocked && RosterCount == partySize,
                localReady = cameraReady[recipientSlot],
                remoteReady = OtherPlayersReady(recipientSlot),
                allReady = AllPlayersReady,
                hasSelectedMode = hasSelectedMode,
                selectedMode = selectedMode,
                modeGeneration = modeGeneration,
                startSignal = startSignal,
                transitionGeneration = transitionGeneration,
                transitionPhase = transitionPhase,
                sceneReadyMask = sceneReadyMask,
                modeStarted = modeStarted,
                active = active,
                paused = logic.Paused,
                transferPending = Pending,
                hasTimer = logic.HasTimer && !Pending,
                remaining = Pending ? 0f : logic.RemainingSeconds,
                status = string.IsNullOrEmpty(transitionStatus) ? rosterStatus[RosterCount] : transitionStatus,
                word = recipientSlot == 0 && (logic.State == RelayQuizState.WordReveal
                    || logic.State == RelayQuizState.Drawing) ? secretWord : string.Empty,
                answer = logic.State == RelayQuizState.Reveal || logic.State == RelayQuizState.Gallery
                    ? logic.SubmittedAnswer : string.Empty,
                correct = (logic.State == RelayQuizState.Reveal || logic.State == RelayQuizState.Gallery)
                    && logic.AnswerCorrect,
                drawingId = current?.drawingId ?? string.Empty,
                drawingOwnerSlot = current?.ownerSlot ?? -1,
                drawingRevision = current?.revision ?? 0,
                drawing = recipientSlot == 0 ? current?.drawing : null,
                referenceDrawing = recipientSlot == 0 ? current?.drawing : null,
                gallery = gallery
            };
        }

        private OnlineRelayQuizGalleryEntry CurrentPrivateDrawing(int recipientSlot)
        {
            if (records.Count == 0 || recipientSlot != logic.PlayerIndex || logic.PlayerIndex <= 0) return null;
            if (logic.State != RelayQuizState.Handover && logic.State != RelayQuizState.ObservePrevious
                && logic.State != RelayQuizState.Drawing && logic.State != RelayQuizState.Guessing) return null;
            if (selectedMode == PartyMode.MemoryCopy && logic.PlayerIndex < partySize - 1
                && logic.State == RelayQuizState.Drawing) return null;
            OnlineRelayQuizGalleryEntry entry = records[records.Count - 1];
            return entry.ownerSlot == recipientSlot - 1 ? entry : null;
        }

        private OnlineRelayQuizGalleryEntry[] BuildGallery(bool includeDrawing)
        {
            var result = new OnlineRelayQuizGalleryEntry[records.Count];
            for (int i = 0; i < records.Count; i++)
            {
                result[i] = records[i].CopyDescriptor();
                if (includeDrawing) result[i].drawing = records[i].drawing;
            }
            return result;
        }

        private bool OtherPlayersReady(int recipientSlot)
        {
            if (RosterCount != partySize) return false;
            for (int i = 0; i < RosterCount; i++)
                if (i != recipientSlot && !cameraReady[i]) return false;
            return true;
        }

        private void Publish()
        {
            publishElapsed = 0f;
            View = BuildView(0);
            for (int slot = 1; slot < RosterCount; slot++)
                SendToSlot(slot, "view", BuildView(slot), CurrentOwnerSlot);
        }

        private void SendGalleryDrawings()
        {
            for (int slot = 1; slot < RosterCount; slot++)
            {
                foreach (OnlineRelayQuizGalleryEntry entry in records)
                {
                    if (!OnlineRelayQuizProtocol.TryDrawingBytes(entry.drawing, brushes, out byte[] bytes))
                    {
                        Abort("Gallery 그림을 전송할 수 없습니다 · 새 초대가 필요합니다");
                        return;
                    }
                    SendChunksToSlot(slot, "gallery-drawing", entry.drawingId, bytes, entry.ownerSlot);
                }
            }
        }

        private void SendHello()
        {
            var packet = new OnlineRelayQuizPacket
            {
                sessionId = string.Empty,
                rosterGeneration = 0,
                roundId = 0,
                turnId = 0,
                ownerSlot = -1,
                revision = 0,
                selectedMode = -1,
                modeGeneration = 0,
                startSignal = 0,
                transitionGeneration = 0,
                transitionPhase = (int)PartyTransitionPhase.Lobby,
                sceneReadyMask = 0,
                sequence = ++outgoingSequence,
                kind = "hello",
                payload = JsonUtility.ToJson(new OnlineRelayQuizHello
                {
                    hostId = expectedHostId,
                    playerId = transport.LocalPlayerId,
                    brushes = brushes
                })
            };
            transport.SendToHost(OnlineRelayQuizProtocol.Encode(packet), true);
        }

        private void SendReject(string peer)
        {
            SendToPeer(peer, "reject", new OnlineRelayQuizCommand(), -1);
        }

        private void SendAdmissionReject(string peer)
        {
            if (disposed || string.IsNullOrEmpty(peer)) return;
            transport.SendTo(peer, OnlineRelayQuizProtocol.Encode(new OnlineRelayQuizPacket
            {
                sessionId = string.Empty,
                ownerSlot = -1,
                selectedMode = -1,
                sequence = 1,
                kind = "reject",
                payload = "{}"
            }), true);
        }

        private void SendToHost<T>(string kind, T payload, int ownerSlot)
        {
            if (disposed || !Assigned) return;
            var packet = new OnlineRelayQuizPacket
            {
                sessionId = View.sessionId,
                rosterGeneration = View.rosterGeneration,
                roundId = View.roundId,
                turnId = View.turnId,
                ownerSlot = ownerSlot,
                revision = View.revision,
                selectedMode = ViewSelectedModeValue,
                modeGeneration = View.modeGeneration,
                startSignal = View.startSignal,
                transitionGeneration = View.transitionGeneration,
                transitionPhase = (int)View.transitionPhase,
                sceneReadyMask = View.sceneReadyMask,
                sequence = ++outgoingSequence,
                kind = kind,
                payload = JsonUtility.ToJson(payload)
            };
            transport.SendToHost(OnlineRelayQuizProtocol.Encode(packet), true);
        }

        private void SendToSlot<T>(int slot, string kind, T payload, int ownerSlot)
        {
            if (slot <= 0 || slot >= RosterCount) return;
            SendToPeer(roster[slot], kind, payload, ownerSlot);
        }

        private void SendToPeer<T>(string peer, string kind, T payload, int ownerSlot)
        {
            if (disposed || string.IsNullOrEmpty(peer)) return;
            outgoingSequences.TryGetValue(peer, out long sequence);
            outgoingSequences[peer] = ++sequence;
            var packet = new OnlineRelayQuizPacket
            {
                sessionId = sessionId,
                rosterGeneration = rosterGeneration,
                roundId = roundId,
                turnId = CurrentTurnId,
                ownerSlot = ownerSlot,
                revision = stateRevision,
                selectedMode = SelectedModeValue,
                modeGeneration = modeGeneration,
                startSignal = startSignal,
                transitionGeneration = transitionGeneration,
                transitionPhase = (int)transitionPhase,
                sceneReadyMask = sceneReadyMask,
                sequence = sequence,
                kind = kind,
                payload = JsonUtility.ToJson(payload)
            };
            transport.SendTo(peer, OnlineRelayQuizProtocol.Encode(packet), true);
        }

        private void SendChunksToSlot(int slot, string kind, string id, byte[] bytes, int ownerSlot)
        {
            int count = (bytes.Length + OnlineRelayQuizProtocol.ChunkBytes - 1)
                / OnlineRelayQuizProtocol.ChunkBytes;
            for (int i = 0; i < count; i++)
            {
                int offset = i * OnlineRelayQuizProtocol.ChunkBytes;
                SendToSlot(slot, kind, new OnlineRelayQuizChunk
                {
                    id = id,
                    index = i,
                    count = count,
                    total = bytes.Length,
                    data = Convert.ToBase64String(bytes, offset,
                        Math.Min(OnlineRelayQuizProtocol.ChunkBytes, bytes.Length - offset))
                }, ownerSlot);
            }
        }

        private void SendChunksToHost(string kind, string id, byte[] bytes, int ownerSlot)
        {
            int count = (bytes.Length + OnlineRelayQuizProtocol.ChunkBytes - 1)
                / OnlineRelayQuizProtocol.ChunkBytes;
            for (int i = 0; i < count; i++)
            {
                int offset = i * OnlineRelayQuizProtocol.ChunkBytes;
                SendToHost(kind, new OnlineRelayQuizChunk
                {
                    id = id,
                    index = i,
                    count = count,
                    total = bytes.Length,
                    data = Convert.ToBase64String(bytes, offset,
                        Math.Min(OnlineRelayQuizProtocol.ChunkBytes, bytes.Length - offset))
                }, ownerSlot);
            }
        }

        private void AdvanceRevision()
        {
            unchecked { stateRevision++; }
            if (stateRevision <= 0) stateRevision = 1;
        }

        private void ClearRoundPayloads()
        {
            records.Clear();
            finalDrawing = null;
            finalAnswer = null;
            finalTransferId = null;
            finalOwnerSlot = -1;
            finalReceiving = null;
            preparedDrawing = null;
            preparedDestination = -1;
            finalPending = false;
            preparedPending = false;
            pendingElapsed = 0f;
            drawingRevision = 0;
            lastCapturedTransferId = null;
            secretWord = string.Empty;
            ClearPrivateCache();
        }

        private void ClearPrivateCache()
        {
            clientTransfers.Clear();
            privateCache.Clear();
        }

        public void Abort(string reason) => Abort(reason, true);

        private void Abort(string reason, bool notify)
        {
            if (View.aborted) return;
            if (notify)
            {
                if (IsHost)
                {
                    for (int slot = 1; slot < RosterCount; slot++)
                        SendToSlot(slot, "abort", new OnlineRelayQuizCommand(), CurrentOwnerSlot);
                }
                else if (Assigned)
                    SendToHost("abort", new OnlineRelayQuizCommand(), localSlot);
            }
            int generation = View.generation + 1;
            int serial = View.serial + 1;
            int revision = View.revision + 1;
            View = new OnlineRelayQuizView
            {
                state = View.state,
                generation = generation,
                serial = serial,
                sessionId = sessionId ?? string.Empty,
                rosterGeneration = rosterGeneration,
                roundId = roundId,
                turnId = IsHost ? CurrentTurnId : View.turnId,
                ownerSlot = -1,
                revision = revision,
                allReady = AllPlayersReady,
                hasSelectedMode = hasSelectedMode,
                selectedMode = selectedMode,
                modeGeneration = modeGeneration,
                startSignal = startSignal,
                transitionGeneration = transitionGeneration,
                transitionPhase = transitionPhase,
                sceneReadyMask = sceneReadyMask,
                modeStarted = modeStarted,
                rosterCount = IsHost ? RosterCount : View.rosterCount,
                rosterLocked = IsHost ? rosterLocked : View.rosterLocked,
                localSlot = localSlot,
                roster = IsHost ? (string[])roster.Clone() : View.roster ?? Array.Empty<string>(),
                isHost = IsHost,
                aborted = true,
                status = reason,
                gallery = Array.Empty<OnlineRelayQuizGalleryEntry>()
            };
            finalDrawing = null;
            finalAnswer = null;
            finalReceiving = null;
            finalPending = false;
            preparedPending = false;
            preparedDrawing = null;
            ClearPrivateCache();
        }

        public void Dispose()
        {
            if (disposed) return;
            if (!View.aborted) Abort("세션을 나갔습니다", true);
            disposed = true;
            transport.OnPeerConnected -= OnConnected;
            transport.OnPeerDisconnected -= OnDisconnected;
            transport.OnMessage -= OnMessage;
            lock (queueLock)
            {
                incoming.Clear();
                queuedBytes = 0;
            }
            transport.Shutdown();
        }
    }
}
