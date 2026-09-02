using System;
using System.Threading;
using System.Threading.Tasks;
using CameraCoop.Game;
using CameraCoop.Netplay;
using CameraCoop.Party;
using CameraCoop.Party.SceneFlow;
using Steamworks;
using Steamworks.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace CameraCoop
{
    [DefaultExecutionOrder(200)]
    public sealed class OnlineRelayQuizController : MonoBehaviour, IPartyWorldGateway, IPartySceneCoordinatorCallbacks
    {
        [Header("Input")]
        [SerializeField] private InputModeManager inputModeManager;
        [SerializeField] private HandInputRouter handInputRouter;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private Transform lobbyPose;
        [SerializeField] private Transform galleryPose;
        [SerializeField] private CameraControlPanel cameraControlPanel;

        [Header("Drawing")]
        [SerializeField] private DrawingController drawingController;
        [SerializeField] private ToolState toolState;
        [SerializeField] private GameObject workCanvasRoot;
        [SerializeField] private CanvasDrawingPresenter previewPresenter;
        [SerializeField] private CanvasSurface previewSurface;

        [Header("Game")]
        [SerializeField] private RelayQuizUI relayQuizUI;
        [SerializeField] private RelayQuizGallery relayQuizGallery;
        [SerializeField] private RelayQuizWordList wordList;
        [SerializeField] private int wordDeckSeed;
        [SerializeField] private PartyWorldController partyWorldController;
        [SerializeField] private PartyLobbyScenePort lobbyScenePort;
        [SerializeField] private PartySceneCoordinator sceneCoordinator;

        private bool sceneCoordinatorConfigured;
        private SynchronizationContext unityContext;
        private OnlineRelayQuizSession session;
        private OnlineRelayQuizSession appliedSession;
        private SteamTransport steamTransport;
        private Lobby? joinedLobby;
        private WordBank wordBank;
        private bool initialized, subscribed, busy, hasFocus = true, transportClosed;
        private int operationGeneration;
        private int appliedSerial, appliedGeneration, appliedFlags, appliedPayloadRevision;
        private bool appliedLobby;
        private CanvasDrawingData appliedDrawing;
        private float retrySteamAt;
        private string status = "Steam 연결을 준비하는 중";
        private string confirmedAnswer = string.Empty;
        private GameObject resultRoot;
        private Transform lobbyGalleryPose;
        private OnlineRelayQuizSession autoReadySession;
        private int autoReadyGeneration = -1;

        private bool CameraReady => cameraControlPanel != null
            && (cameraControlPanel.State == CameraConnectionState.Receiving || cameraControlPanel.State == CameraConnectionState.External);

        public bool IsHost => session != null && session.IsHost;
        public bool IsCameraConnected => CameraReady;
        public bool HasFreshHand => handInputRouter != null && handInputRouter.HasFreshHand;
        public string LocalIdentity => steamTransport != null ? steamTransport.LocalPlayerId : string.Empty;
        public OnlineRelayQuizView PartyView => session?.View;

        private void Start()
        {
            if (!Application.isPlaying) return;
            unityContext = SynchronizationContext.Current;
            if (!ValidateRuntimeConfiguration(out string error))
            {
                status = "설정 오류: " + error;
                if (relayQuizUI != null) relayQuizUI.ShowSetupError(status);
                enabled = false;
                return;
            }
            if (!TryInitializeSceneCoordinator(out error))
            {
                status = "Scene 전환 설정 오류: " + error;
                relayQuizUI.ShowSetupError(status);
                enabled = false;
                return;
            }
            wordBank = new WordBank(error, wordDeckSeed != 0 ? wordDeckSeed : Environment.TickCount);
            initialized = true;
            SubscribeSteam();
            SyncView(true);
        }

        public bool ValidateRuntimeConfiguration(out string result)
        {
            result = "필수 참조를 모두 할당해주세요";
            if (inputModeManager == null || handInputRouter == null || playerController == null
                || lobbyPose == null || galleryPose == null || cameraControlPanel == null || drawingController == null
                || toolState == null || toolState.BrushCount == 0
                || relayQuizUI == null || relayQuizGallery == null || wordList == null
                )
                return false;
            if (partyWorldController != null && !partyWorldController.ValidateRuntimeConfiguration(out result)) return false;
            if (!relayQuizGallery.ValidateRuntimeConfiguration(out result)) return false;
            return wordList.TryBuildDeckText(out result, out string wordError) || SetError(wordError, out result);
        }

        private static bool SetError(string error, out string result) { result = error; return false; }

        private void OnEnable()
        {
            if (initialized && Application.isPlaying) { SubscribeSteam(); SyncView(true); }
        }

        private void SubscribeSteam()
        {
            if (!SteamBootstrap.TryInit())
            {
                status = "Steam을 실행하고 로그인해주세요 · 연결을 다시 확인합니다";
                retrySteamAt = Time.unscaledTime + 5f;
                return;
            }
            if (!subscribed)
            {
                SteamFriends.OnGameLobbyJoinRequested += HandleJoinRequested;
                subscribed = true;
            }
            if (session == null && !busy) status = "Host로 방을 만들거나 친구의 Steam 초대를 수락해주세요";
        }

        private void OnDisable()
        {
            operationGeneration++;
            busy = false;
            if (subscribed) SteamFriends.OnGameLobbyJoinRequested -= HandleJoinRequested;
            subscribed = false;
            if (sceneCoordinatorConfigured)
                sceneCoordinator.ShutdownSceneBoundary(_ => ReleaseSession());
            else ReleaseSession();
            HidePrivateContent();
            if (relayQuizUI != null) relayQuizUI.HideAll();
        }

        private void OnDestroy()
        {
            ReleaseSession();
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            if (!initialized) return;
            session?.UpdateLocalConditions(hasFocus, handInputRouter.HasFreshHand);
            SyncView(true);
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            if (!SteamBootstrap.IsValid && !busy && session == null && Time.unscaledTime >= retrySteamAt) SubscribeSteam();
            try
            {
                if (session != null && !session.View.aborted)
                {
                    if (!session.View.transferPending) confirmedAnswer = relayQuizUI.ConfirmedAnswer;
                    session.UpdateLocalConditions(hasFocus, handInputRouter.HasFreshHand);
                    TryAutoReadyHandover();
                    DrainActions();
                    session.Tick(Time.unscaledDeltaTime);
                }
                if (session != null && session.View.aborted && !transportClosed) CloseTransport();
                SyncView(false);
                ProcessMouse();
                relayQuizUI.UpdateAnswerInput();
                if (session != null) relayQuizUI.UpdateOnlineTimer(session.View, !hasFocus && session.View.state != RelayQuizState.Setup);
            }
            catch (Exception exception)
            {
                if (session != null) session.Abort("온라인 처리 실패 · 새 초대가 필요합니다 (" + exception.GetType().Name + ")");
                status = "온라인 처리 실패 · 나간 뒤 다시 초대해주세요";
                CloseTransport();
                HidePrivateContent();
                relayQuizUI.HideAll();
            }
        }

        private void DrainActions()
        {
            while (relayQuizUI.TryDequeueAction(out RelayQuizActionRequest request))
            {
                OnlineRelayQuizView view = session.View;
                if (request.captureGeneration != view.generation || !hasFocus || view.aborted) continue;
                if (request.action == RelayQuizAction.StartGame) { session.SetReady(CameraReady); continue; }
                if (request.action == RelayQuizAction.Submit && relayQuizUI.IsComposing) continue;
                if (request.action == RelayQuizAction.Undo || request.action == RelayQuizAction.Clear)
                {
                    if (!view.active || view.state != RelayQuizState.Drawing || view.paused || view.transferPending) continue;
                    handInputRouter.CancelCanvasCaptures(HandCancelReason.DrawingCommand);
                    drawingController.FinalizeActiveStrokes();
                    if (request.action == RelayQuizAction.Undo) drawingController.UndoLastStroke();
                    else drawingController.ClearAll();
                    continue;
                }
                session.Execute(request.action, request.captureGeneration);
            }
        }

        private void TryAutoReadyHandover()
        {
            if (session == null) return;
            if (!ReferenceEquals(autoReadySession, session))
            {
                autoReadySession = session;
                autoReadyGeneration = -1;
            }
            OnlineRelayQuizView view = session.View;
            if (!ShouldAutoReadyHandover(view, CameraReady, handInputRouter.HasFreshHand,
                    hasFocus, autoReadyGeneration)) return;
            autoReadyGeneration = view.generation;
            session.Execute(RelayQuizAction.Ready, view.generation);
        }

        internal static bool ShouldAutoReadyHandover(OnlineRelayQuizView view, bool cameraReady,
            bool freshHand, bool focused, int completedGeneration)
        {
            return view != null && view.active && view.state == RelayQuizState.Handover
                && view.generation != completedGeneration && cameraReady && freshHand && focused
                && !view.aborted && !view.paused && !view.transferPending;
        }

        private RelayQuizPauseStage PauseStage(OnlineRelayQuizView view)
        {
            if (!view.paused) return RelayQuizPauseStage.None;
            return view.active && hasFocus && (view.state != RelayQuizState.Drawing || handInputRouter.HasFreshHand)
                ? RelayQuizPauseStage.ResumeReady : RelayQuizPauseStage.Blocked;
        }

        private void SyncView(bool force)
        {
            OnlineRelayQuizView view = session == null ? new OnlineRelayQuizView() : session.View;
            if (sceneCoordinatorConfigured) sceneCoordinator.ApplyView(view);
            bool hidden = !hasFocus && view.state != RelayQuizState.Setup;
            RelayQuizPauseStage stage = PauseStage(view);
            int flags = (hidden ? 1 : 0) | (view.paused ? 2 : 0) | (view.transferPending ? 4 : 0)
                | (view.aborted ? 8 : 0) | (view.connected ? 16 : 0) | (view.localReady ? 32 : 0)
                | (view.remoteReady ? 64 : 0) | (CameraReady ? 128 : 0) | ((int)stage << 8);
            bool newSession = appliedSession != session;
            bool stateChanged = newSession || appliedSerial != view.serial;
            if (!force && !stateChanged && appliedGeneration == view.generation && appliedFlags == flags
                && appliedPayloadRevision == view.payloadRevision && ReferenceEquals(appliedDrawing, view.drawing)) return;
            handInputRouter.CancelAll(HandCancelReason.ViewChanged);
            drawingController.FinalizeActiveStrokes();
            handInputRouter.SetViewGeneration(view.generation);
            relayQuizUI.ClearActions();
            if (stateChanged || force && appliedSerial == 0)
            {
                if (view.state == RelayQuizState.Setup)
                {
                    drawingController.ClearAll();
                    relayQuizUI.ResetAnswerText();
                    confirmedAnswer = string.Empty;
                }
                else if (view.state == RelayQuizState.Drawing && view.active) drawingController.ClearAll();
                else if (view.state == RelayQuizState.Guessing)
                {
                    relayQuizUI.ResetAnswerText();
                    confirmedAnswer = string.Empty;
                }
            }
            bool visible = !hidden && !view.paused && !view.aborted && !view.transferPending;
            bool relayDrawing = visible && view.connected && view.state == RelayQuizState.Drawing && view.active;
            bool coopDrawing = visible && view.connected && view.modeStarted && view.hasSelectedMode
                && view.selectedMode == PartyMode.CoopMural && view.startSignal > 0;
            bool drawing = relayDrawing || coopDrawing;
            if (workCanvasRoot != null) workCanvasRoot.SetActive(relayDrawing);
            drawingController.SetStrokesVisible(drawing);
            bool referencePreview = visible && PartyWorldController.IsReferenceVisible(view);
            bool guessingPreview = visible && view.state == RelayQuizState.Guessing && view.active && view.drawing != null;
            bool preview = referencePreview || guessingPreview;
            if (previewSurface != null) previewSurface.gameObject.SetActive(preview);
            if (preview && previewPresenter != null && previewSurface != null)
                previewPresenter.Show(referencePreview ? view.referenceDrawing : view.drawing, previewSurface);
            else if (previewPresenter != null) previewPresenter.ClearPresentation();
            ApplyGallery(view, visible);
            relayQuizUI.ApplyOnlineView(view, stage, hidden, CameraReady);
            ApplyNavigation(view, session == null, newSession, appliedSerial == 0, hidden, stage, drawing, stateChanged);
            if (steamTransport != null && steamTransport.IsHost)
                new Lobby(steamTransport.LobbyId).SetJoinable(!view.aborted && view.state == RelayQuizState.Setup
                    && !view.rosterLocked && view.rosterCount < PartyRoster.Capacity);
            appliedSession = session;
            appliedSerial = view.serial;
            appliedGeneration = view.generation;
            appliedFlags = flags;
            appliedPayloadRevision = view.payloadRevision;
            appliedDrawing = view.drawing;
        }

        private void ApplyNavigation(OnlineRelayQuizView view, bool noSession, bool newSession, bool firstApplication,
            bool hidden, RelayQuizPauseStage stage, bool drawing, bool stateChanged)
        {
            bool visible = !hidden && !view.paused && !view.aborted && !view.transferPending;
            bool lobby = visible && view.state == RelayQuizState.Setup;
            bool enteredLobby = lobby && (newSession || firstApplication || !appliedLobby);
            if (!lobby) inputModeManager.SetContext(InputContext.Blocked);
            InputContext context = hidden || view.aborted || view.paused && stage != RelayQuizPauseStage.ResumeReady
                ? InputContext.Blocked : drawing ? InputContext.Drawing
                : visible && (view.state == RelayQuizState.Setup || view.state == RelayQuizState.Gallery)
                    ? InputContext.Explore : InputContext.UiOnly;
            inputModeManager.SetContext(context);
            if (enteredLobby)
            {
                if (noSession || !view.rosterLocked || view.localSlot < 0)
                    playerController.PlaceAt(lobbyPose);
                inputModeManager.RequestMode(InputMode.Move);
            }
            else if (visible && view.state == RelayQuizState.Gallery && stateChanged)
            {
                playerController.PlaceAt(galleryPose);
                inputModeManager.RequestMode(InputMode.Move);
            }
            appliedLobby = lobby;
        }

        internal void ApplyNavigationForTests(OnlineRelayQuizView view, bool noSession, bool newSession,
            bool firstApplication, bool hidden, RelayQuizPauseStage stage, bool drawing, bool stateChanged)
        {
            ApplyNavigation(view, noSession, newSession, firstApplication, hidden, stage, drawing, stateChanged);
        }

        private void ApplyGallery(OnlineRelayQuizView view, bool visible)
        {
            bool show = visible && view.state == RelayQuizState.Gallery && relayQuizGallery.IsReady;
            if (resultRoot != null) resultRoot.SetActive(show);
            if (show) relayQuizGallery.Show(BuildGalleryRecords(view.gallery));
            else relayQuizGallery.Clear();
        }

        internal void ApplyGalleryForTests(OnlineRelayQuizView view, bool visible)
        {
            ApplyGallery(view, visible);
        }

        private CanvasDrawingData CaptureDrawing()
        {
            handInputRouter.CancelCanvasCaptures(HandCancelReason.ViewChanged);
            drawingController.FinalizeActiveStrokes();
            return drawingController.ExportDrawing();
        }

        private static RelayTurnRecord[] BuildGalleryRecords(OnlineRelayQuizGalleryEntry[] entries)
        {
            if (entries == null || entries.Length == 0) return Array.Empty<RelayTurnRecord>();
            var records = new RelayTurnRecord[entries.Length];
            for (int index = 0; index < entries.Length; index++)
            {
                OnlineRelayQuizGalleryEntry entry = entries[index];
                records[index] = new RelayTurnRecord
                {
                    playerIndex = entry?.ownerSlot ?? index,
                    drawingIndex = index,
                    drawing = entry?.drawing
                };
            }
            return records;
        }

        private string CaptureAnswer()
        {
            string result = confirmedAnswer;
            relayQuizUI.ReleaseAnswerFocus();
            return result;
        }

        private void HidePrivateContent()
        {
            if (inputModeManager != null) inputModeManager.SetContext(InputContext.Blocked);
            if (handInputRouter != null) handInputRouter.CancelAll(HandCancelReason.ViewChanged);
            if (drawingController != null) { drawingController.FinalizeActiveStrokes(); drawingController.SetStrokesVisible(false); }
            if (workCanvasRoot != null) workCanvasRoot.SetActive(false);
            if (previewPresenter != null) previewPresenter.ClearPresentation();
            if (previewSurface != null) previewSurface.gameObject.SetActive(false);
            if (relayQuizGallery != null) relayQuizGallery.Clear();
            if (resultRoot != null) resultRoot.SetActive(false);
        }

        private void ProcessMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !hasFocus) return;
            Vector2 position = mouse.position.ReadValue();
            bool pressed = mouse.leftButton.wasPressedThisFrame;
            bool released = mouse.leftButton.wasReleasedThisFrame;
            if (session != null && relayQuizUI.ProcessOnlineResumePointer(position, pressed, released, session.View.generation,
                PauseStage(session.View) == RelayQuizPauseStage.ResumeReady)) session.Execute(RelayQuizAction.Resume, session.View.generation);
        }

        public async void OnClickHostSteam()
        {
            if (!initialized || !isActiveAndEnabled || busy || session != null) return;
            SubscribeSteam();
            if (!SteamBootstrap.IsValid) return;
            busy = true;
            int operation = ++operationGeneration;
            status = "Steam 방을 만드는 중";
            try
            {
                SteamTransport created = await SteamTransport.HostAsync(PartySizeOption.Resolve()).ConfigureAwait(false);
                unityContext.Post(_ => CompleteHost(operation, created), null);
            }
            catch (Exception exception)
            {
                unityContext.Post(_ => OperationFailed(operation, "Host 생성 실패: " + exception.Message), null);
            }
        }

        private bool IsCurrentOperation(int operation) => this != null && isActiveAndEnabled && operation == operationGeneration;

        private void CompleteHost(int operation, SteamTransport created)
        {
            if (!IsCurrentOperation(operation)) { created.Shutdown(); return; }
            busy = false;
            var lobby = new Lobby(created.LobbyId);
            try
            {
                if (!lobby.SetData("relay-game", OnlineRelayQuizProtocol.GameId)
                    || !lobby.SetData("relay-version", OnlineRelayQuizProtocol.Version.ToString())
                    || !lobby.SetData("relay-brushes", toolState.BrushCount.ToString()))
                    throw new InvalidOperationException("게임 정보 설정 실패");
                Bind(created, created.LocalPlayerId);
            }
            catch (Exception exception) { created.Shutdown(); OperationFailed(operation, "Host 설정 실패: " + exception.Message); }
        }

        public void OnClickInvite()
        {
            if (!initialized || !isActiveAndEnabled || steamTransport == null || !steamTransport.IsHost
                || session == null || session.View.aborted || session.View.connected) return;
            try { SteamFriends.OpenGameInviteOverlay(steamTransport.LobbyId); }
            catch (Exception exception) { status = "초대 표시 실패: " + exception.Message; }
        }

        private void HandleJoinRequested(Lobby lobby, SteamId friendId)
        {
            SynchronizationContext context = unityContext;
            if (context != null) context.Post(_ => BeginJoin(lobby), null);
        }

        private async void BeginJoin(Lobby lobby)
        {
            if (this == null || !initialized || !isActiveAndEnabled || busy || session != null) return;
            busy = true;
            int operation = ++operationGeneration;
            status = "Steam 초대에 참가하는 중";
            try
            {
                RoomEnter result = await lobby.Join().ConfigureAwait(false);
                unityContext.Post(_ => CompleteJoin(operation, lobby, result), null);
            }
            catch (Exception exception)
            {
                unityContext.Post(_ => { lobby.Leave(); OperationFailed(operation, "참가 실패: " + exception.Message); }, null);
            }
        }

        private void CompleteJoin(int operation, Lobby lobby, RoomEnter result)
        {
            if (!IsCurrentOperation(operation)) { if (result == RoomEnter.Success) lobby.Leave(); return; }
            busy = false;
            if (result != RoomEnter.Success) { status = "참가 실패: " + result; return; }
            if (lobby.GetData("relay-game") != OnlineRelayQuizProtocol.GameId
                || lobby.GetData("relay-version") != OnlineRelayQuizProtocol.Version.ToString()
                || lobby.GetData("relay-brushes") != toolState.BrushCount.ToString()
                || lobby.Owner.Id == SteamClient.SteamId)
            { lobby.Leave(); status = "같은 RelayQuiz version의 친구 초대만 참가할 수 있습니다"; return; }
            SteamTransport created = null;
            try
            {
                SteamId owner = lobby.Owner.Id;
                created = SteamTransport.ConnectTo(owner);
                joinedLobby = lobby;
                Bind(created, owner.ToString());
            }
            catch (Exception exception)
            {
                created?.Shutdown();
                lobby.Leave();
                joinedLobby = null;
                OperationFailed(operation, "연결 실패: " + exception.Message);
            }
        }

        private void Bind(SteamTransport transport, string expectedHost)
        {
            session = new OnlineRelayQuizSession(transport, expectedHost, () => wordBank.Next(), CaptureDrawing,
                CaptureAnswer, toolState.BrushCount, PartySizeOption.Resolve());
            if (sceneCoordinatorConfigured)
            {
                try { sceneCoordinator.ResetForSession(session.View.sessionId); }
                catch (InvalidOperationException exception)
                {
                    status = "Scene 전환 초기화 실패: " + exception.Message;
                    session.ReportSceneLoadFailure(session.View.transitionGeneration, PartySceneLoadFailure.InvalidTransition);
                }
            }
            steamTransport = transport;
            transportClosed = false;
            appliedSerial = 0;
            partyWorldController?.BindNetwork(session, transport, toolState.BrushCount);
            SyncView(true);
        }

        private void OperationFailed(int operation, string message)
        {
            if (!IsCurrentOperation(operation)) return;
            busy = false;
            status = message;
        }

        public void OnClickLeave()
        {
            if (!initialized) return;
            operationGeneration++;
            busy = true;
            status = "Lobby로 돌아가는 중";
            if (sceneCoordinatorConfigured)
            {
                sceneCoordinator.ShutdownSceneBoundary(CompleteLocalLeave);
                return;
            }
            CompleteLocalLeave(true);
        }

        private void CompleteLocalLeave(bool sceneBoundaryClosed)
        {
            if (this == null) return;
            busy = false;
            ReleaseSession();
            status = sceneBoundaryClosed
                ? "세션을 나갔습니다 · 새로 Host를 만들거나 친구 초대를 받아주세요"
                : "Scene 정리에 실패했지만 세션 연결은 종료했습니다";
            SyncView(true);
        }

        private void CloseTransport()
        {
            partyWorldController?.UnbindNetwork();
            if (!transportClosed) session?.Dispose();
            transportClosed = true;
            steamTransport = null;
            if (joinedLobby.HasValue) { joinedLobby.Value.Leave(); joinedLobby = null; }
        }

        private void ReleaseSession()
        {
            CloseTransport();
            session = null;
            autoReadySession = null;
            autoReadyGeneration = -1;
        }

        public void SetReady(bool ready) => session?.SetReady(ready);
        public bool SelectMode(PartyMode mode) => session != null && session.SelectModeAndBeginLoad(mode);
        public bool StartSelectedMode() => session != null && session.OpenModeSelector();
        public void RequestHost() => OnClickHostSteam();
        public void RequestInvite() => OnClickInvite();
        public void RequestLeave() => OnClickLeave();
        public bool RequestReturnToLobby() => session != null && session.RequestReturnToLobby();

        public void RequestCamera(PartyWorldAction action)
        {
            if (cameraControlPanel == null) return;
            _ = RunWorldCameraActionAsync(action);
        }

        private async Task RunWorldCameraActionAsync(PartyWorldAction action)
        {
            await Task.Yield();
            if (this == null || !isActiveAndEnabled || cameraControlPanel == null) return;
            switch (action)
            {
                case PartyWorldAction.CameraRefresh: cameraControlPanel.RefreshCameras(); break;
                case PartyWorldAction.CameraPrevious: cameraControlPanel.CycleCamera(-1); break;
                case PartyWorldAction.CameraNext: cameraControlPanel.CycleCamera(1); break;
                case PartyWorldAction.CameraPreview: cameraControlPanel.SelectPreview(!cameraControlPanel.PreviewEnabled); break;
            }
        }

        private bool TryInitializeSceneCoordinator(out string error)
        {
            if (lobbyScenePort == null)
            {
                if (sceneCoordinator != null)
                {
                    error = "lobbyScenePort가 필요합니다";
                    return false;
                }
                error = string.Empty;
                return true;
            }
            if (sceneCoordinator == null) sceneCoordinator = gameObject.AddComponent<PartySceneCoordinator>();
            if (!sceneCoordinatorConfigured)
            {
                sceneCoordinator.Configure(new UnityPartySceneLoader(), new UnityPartyGameSceneResolver(),
                    lobbyScenePort, this);
                sceneCoordinatorConfigured = true;
            }
            error = string.Empty;
            return true;
        }

        public void BindGameScene(IPartyGameScenePort adapter)
        {
            if (adapter == null) throw new ArgumentNullException(nameof(adapter));
            PartySceneBindings bindings = adapter.Bindings ?? throw new ArgumentException("Scene bindings are required.", nameof(adapter));
            partyWorldController?.BindGamePort(adapter);
            workCanvasRoot = bindings.WritablePaperRoot;
            previewPresenter = bindings.ReferencePresenter;
            previewSurface = bindings.ReferenceSurface;
            resultRoot = adapter.Mode != PartyMode.CoopMural ? bindings.ResultRoot : null;
            if (resultRoot != null) resultRoot.SetActive(false);
            if (adapter.Mode != PartyMode.CoopMural)
            {
                relayQuizGallery?.Configure(bindings.GalleryRoots, bindings.GalleryPresenters,
                    bindings.GallerySurfaces);
                if (lobbyGalleryPose == null) lobbyGalleryPose = galleryPose;
                galleryPose = bindings.ResultViewPose;
            }
        }

        public void DisableGameSceneInteractions(IPartyGameScenePort adapter)
        {
            WorldActionInteractable[] actions = adapter?.Bindings?.Actions;
            if (actions == null) return;
            for (int index = 0; index < actions.Length; index++)
                if (actions[index] != null) actions[index].enabled = false;
        }

        public void UnbindGameScene(IPartyGameScenePort adapter)
        {
            if (previewPresenter != null) previewPresenter.ClearPresentation();
            if (previewSurface != null) previewSurface.gameObject.SetActive(false);
            if (workCanvasRoot != null) workCanvasRoot.SetActive(false);
            if (resultRoot != null) resultRoot.SetActive(false);
            relayQuizGallery?.Release();
            if (lobbyGalleryPose != null) galleryPose = lobbyGalleryPose;
            partyWorldController?.UnbindScenePort(adapter);
            workCanvasRoot = null;
            previewPresenter = null;
            previewSurface = null;
            resultRoot = null;
        }

        public bool ActivateLobbyScene()
        {
            Scene lobbyScene = lobbyScenePort != null ? lobbyScenePort.gameObject.scene : default;
            if (!lobbyScene.IsValid() || !lobbyScene.isLoaded) return false;
            // 활성 씬을 unload하면 Unity가 남은 씬을 자동으로 활성화한다. 이미 로비가 활성이면 성공으로 본다 —
            // 그 상태에서 SetActiveScene은 false를 돌려주고, 그대로 두면 정상 복귀가 ActivationFailed로 끊긴다.
            return SceneManager.GetActiveScene() == lobbyScene || SceneManager.SetActiveScene(lobbyScene);
        }

        public void RebaseToGame(IPartyGameScenePort adapter)
        {
            OnlineRelayQuizView view = session?.View;
            if (view == null || view.localSlot < 0) return;
            partyWorldController?.ConfigureAssignedSlot(view.localSlot, LocalIdentity);
        }

        public void RebaseToLobby(PartyLobbyScenePort lobby)
        {
            partyWorldController?.BindLobbyPort(lobby);
            OnlineRelayQuizView view = session?.View;
            if (view != null && view.localSlot >= 0)
                partyWorldController?.ConfigureAssignedSlot(view.localSlot, LocalIdentity);
        }

        public bool MarkLocalSceneReady(int generation)
            => session != null && session.MarkLocalSceneReady(generation);

        public bool MarkLocalLobbyReady(int generation)
            => session != null && session.MarkLocalLobbyReady(generation);

        public void ReportSceneLoadFailure(int generation, PartySceneLoadFailure failure)
        {
            status = "Scene 전환 실패: " + failure;
            session?.ReportSceneLoadFailure(generation, failure);
            if (relayQuizUI != null) relayQuizUI.ShowSetupError(status);
        }
    }
}
