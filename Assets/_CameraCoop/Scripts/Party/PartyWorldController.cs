using System;
using CameraCoop.Netplay;
using UnityEngine;

namespace CameraCoop.Party
{
    public interface IPartyWorldGateway
    {
        bool IsHost { get; }
        bool IsCameraConnected { get; }
        bool HasFreshHand { get; }
        string LocalIdentity { get; }
        OnlineRelayQuizView PartyView { get; }
        void SetReady(bool ready);
        bool SelectMode(PartyMode mode);
        bool StartSelectedMode();
        void RequestHost();
        void RequestInvite();
        void RequestLeave();
        void RequestCamera(PartyWorldAction action);
    }

    [DefaultExecutionOrder(250)]
    public sealed class PartyWorldController : MonoBehaviour
    {
        [Header("Gateway")]
        [SerializeField] private OnlineRelayQuizController relayController;
        [SerializeField] private WorldReadyPadInteractable[] readyPadsBySlot;
        [SerializeField] private WorldActionInteractable[] worldActions;

        [Header("Local player")]
        [SerializeField] private InputModeManager inputModeManager;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private Transform localPlayerRoot;
        [SerializeField] private BoxCollider[] playerZoneBounds;
        [SerializeField] private Transform[] playerSpawnPointsBySlot;
        [SerializeField] private PersonalCanvasPlacement personalCanvas;
        [SerializeField] private Transform carriedCanvasAnchor;
        [SerializeField] private Transform[] canvasDockAnchorsBySlot;
        [SerializeField, Min(0f)] private float canvasDockRadius = 0.5f;
        [SerializeField] private PhysicalPaintTool physicalPaintTool;
        [SerializeField] private DrawingController drawingController;
        [SerializeField] private ToolState toolState;
        [SerializeField] private GameObject localWritableCanvasRoot;

        [Header("Remote avatars")]
        [SerializeField] private RemoteAvatarPresenter[] remoteAvatarPresenters;
        [SerializeField] private Transform[] avatarRootsBySlot;
        [SerializeField] private Animator[] avatarAnimatorsBySlot;

        [Header("Coop mural public layers")]
        [SerializeField] private GameObject[] muralLayerRoots;
        [SerializeField] private CanvasDrawingPresenter[] muralLayerPresenters;
        [SerializeField] private CanvasSurface[] muralLayerSurfaces;

        private IPartyWorldGateway gateway;
        private OnlineRelayQuizSession boundRelaySession;
        private INetTransport boundTransport;
        private PartyPoseSession poseSession;
        private CoopMuralSession muralSession;
        private int boundBrushCount;
        private int poseRosterGeneration;
        private int muralStartSignal;
        private int renderedMuralSerial = -1;
        private PartyRosterSnapshot cachedRoster;
        private string cachedRosterSessionId;
        private int cachedRosterGeneration;
        private Vector3 previousLocalPosition;
        private bool hasPreviousLocalPosition;
        private bool resetApplied;
        private bool muralPresentationActive;

        public bool HasPoseSession => poseSession != null;
        public bool HasMuralSession => muralSession != null;

        public bool CanOccupyReadyPad(int slot)
        {
            OnlineRelayQuizView view = gateway?.PartyView;
            return gateway != null && gateway.IsCameraConnected && gateway.HasFreshHand
                && view != null && view.localSlot == slot && !view.aborted
                && view.state == RelayQuizState.Setup && !view.modeStarted;
        }

        public bool IsReadyPadAvailable(int slot)
        {
            OnlineRelayQuizView view = gateway?.PartyView;
            return view != null && view.localSlot == slot && !view.aborted
                && view.state == RelayQuizState.Setup && !view.modeStarted;
        }

        private void Awake()
        {
            if (gateway == null && relayController != null) gateway = relayController;
            if (readyPadsBySlot != null)
                for (int slot = 0; slot < readyPadsBySlot.Length; slot++)
                    if (readyPadsBySlot[slot] != null)
                        readyPadsBySlot[slot].Configure(this, slot, readyPadsBySlot[slot].DwellSeconds);
            if (worldActions != null)
                foreach (WorldActionInteractable action in worldActions)
                    if (action != null) action.Configure(this, action.Action);
        }

        private void Update()
        {
            TickRuntime(Time.unscaledTime);
        }

        public void ConfigureGateway(IPartyWorldGateway worldGateway)
        {
            gateway = worldGateway ?? throw new ArgumentNullException(nameof(worldGateway));
        }

        public void ConfigureInput(InputModeManager modes, PlayerController player)
        {
            inputModeManager = modes;
            playerController = player;
        }

        public void ConfigurePersonalCanvas(PersonalCanvasPlacement placement)
        {
            personalCanvas = placement;
        }

        public void ConfigureWritableCanvas(GameObject writableCanvasRoot)
        {
            localWritableCanvasRoot = writableCanvasRoot != null
                ? writableCanvasRoot : throw new ArgumentNullException(nameof(writableCanvasRoot));
        }

        public void ConfigureReadyPad(WorldReadyPadInteractable pad, float dwellSeconds = 1f)
        {
            if (pad == null) throw new ArgumentNullException(nameof(pad));
            readyPadsBySlot = new WorldReadyPadInteractable[PartyRoster.Capacity];
            readyPadsBySlot[0] = pad;
            pad.Configure(this, 0, dwellSeconds);
        }

        public void ConfigureReadyPads(WorldReadyPadInteractable[] pads)
        {
            if (pads == null || pads.Length != PartyRoster.Capacity)
                throw new ArgumentException("Four ready pads are required.", nameof(pads));
            readyPadsBySlot = (WorldReadyPadInteractable[])pads.Clone();
            for (int slot = 0; slot < readyPadsBySlot.Length; slot++)
            {
                if (readyPadsBySlot[slot] == null)
                    throw new ArgumentException("Ready pads cannot contain null.", nameof(pads));
                readyPadsBySlot[slot].Configure(this, slot, readyPadsBySlot[slot].DwellSeconds);
            }
        }

        public void ConfigureSlotLayout(
            Transform localRoot,
            Transform carryAnchor,
            Transform[] dockAnchors,
            float dockRadius,
            BoxCollider[] zoneBounds,
            Transform[] spawnPoints)
        {
            if (localRoot == null) throw new ArgumentNullException(nameof(localRoot));
            if (carryAnchor == null) throw new ArgumentNullException(nameof(carryAnchor));
            if (dockAnchors == null || dockAnchors.Length != PartyRoster.Capacity)
                throw new ArgumentException("Four dock anchors are required.", nameof(dockAnchors));
            if (zoneBounds == null || zoneBounds.Length != PartyRoster.Capacity)
                throw new ArgumentException("Four zone bounds are required.", nameof(zoneBounds));
            if (spawnPoints == null || spawnPoints.Length != PartyRoster.Capacity)
                throw new ArgumentException("Four spawn points are required.", nameof(spawnPoints));
            if (float.IsNaN(dockRadius) || float.IsInfinity(dockRadius) || dockRadius < 0f)
                throw new ArgumentOutOfRangeException(nameof(dockRadius));
            localPlayerRoot = localRoot;
            carriedCanvasAnchor = carryAnchor;
            canvasDockAnchorsBySlot = (Transform[])dockAnchors.Clone();
            canvasDockRadius = dockRadius;
            playerZoneBounds = (BoxCollider[])zoneBounds.Clone();
            playerSpawnPointsBySlot = (Transform[])spawnPoints.Clone();
        }

        public void ConfigureMuralLayers(
            GameObject[] layerRoots,
            CanvasDrawingPresenter[] layerPresenters,
            CanvasSurface[] sharedSurfaces)
        {
            if (layerRoots == null || layerPresenters == null || sharedSurfaces == null
                || layerRoots.Length != PartyRoster.Capacity
                || layerPresenters.Length != PartyRoster.Capacity
                || sharedSurfaces.Length != PartyRoster.Capacity)
                throw new ArgumentException("Four mural roots, presenters and surfaces are required.");
            muralLayerRoots = (GameObject[])layerRoots.Clone();
            muralLayerPresenters = (CanvasDrawingPresenter[])layerPresenters.Clone();
            muralLayerSurfaces = (CanvasSurface[])sharedSurfaces.Clone();
        }

        public void ConfigureAssignedSlot(int localSlot, string localIdentity)
        {
            PartyRoster.ValidateSlot(localSlot);
            if (string.IsNullOrEmpty(localIdentity))
                throw new ArgumentException("Local identity is required.", nameof(localIdentity));
            if (personalCanvas != null && carriedCanvasAnchor != null && canvasDockAnchorsBySlot != null
                && localSlot < canvasDockAnchorsBySlot.Length && canvasDockAnchorsBySlot[localSlot] != null)
                personalCanvas.Configure(localIdentity, carriedCanvasAnchor, canvasDockAnchorsBySlot[localSlot], canvasDockRadius);
            Transform spawn = playerSpawnPointsBySlot != null && localSlot < playerSpawnPointsBySlot.Length
                ? playerSpawnPointsBySlot[localSlot] : null;
            if (spawn != null)
            {
                if (playerController != null) playerController.PlaceAt(spawn);
                else if (localPlayerRoot != null)
                    localPlayerRoot.SetPositionAndRotation(spawn.position, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));
            }
        }

        public void BindNetwork(OnlineRelayQuizSession relaySession, INetTransport transport, int brushCount)
        {
            if (relaySession == null) throw new ArgumentNullException(nameof(relaySession));
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (brushCount <= 0) throw new ArgumentOutOfRangeException(nameof(brushCount));
            DisposeNetworkDomains();
            boundRelaySession = relaySession;
            boundTransport = transport;
            boundBrushCount = brushCount;
            if (physicalPaintTool != null) physicalPaintTool.SetLocalPlayerId(transport.LocalPlayerId);
            resetApplied = false;
        }

        public void UnbindNetwork()
        {
            DisposeNetworkDomains();
            boundRelaySession = null;
            boundTransport = null;
            boundBrushCount = 0;
            ResetRuntimeState();
        }

        public bool CanExecute(PartyWorldAction action)
        {
            if (gateway == null) return false;
            OnlineRelayQuizView view = gateway.PartyView;
            switch (action)
            {
                case PartyWorldAction.Host: return string.IsNullOrEmpty(gateway.LocalIdentity);
                case PartyWorldAction.Invite:
                    return gateway.IsHost && view != null && !view.aborted && view.state == RelayQuizState.Setup
                        && !view.rosterLocked && view.rosterCount < PartyRoster.Capacity;
                case PartyWorldAction.Leave: return !string.IsNullOrEmpty(gateway.LocalIdentity);
                case PartyWorldAction.SelectRelayCopy:
                case PartyWorldAction.SelectMemoryCopy:
                case PartyWorldAction.SelectCoopMural:
                    return gateway.IsHost && view != null && !view.aborted && view.state == RelayQuizState.Setup
                        && !view.modeStarted && view.rosterLocked && view.rosterCount == PartyRoster.Capacity;
                case PartyWorldAction.StartSelectedMode:
                    return gateway.IsHost && IsStartReady(view);
                case PartyWorldAction.CarryCanvas:
                    return personalCanvas != null && !string.IsNullOrEmpty(gateway.LocalIdentity)
                        && string.Equals(personalCanvas.OwnerPlayerId, gateway.LocalIdentity, StringComparison.Ordinal)
                        && personalCanvas.State == PersonalCanvasPlacementState.Docked;
                case PartyWorldAction.DockCanvas:
                    return personalCanvas != null && !string.IsNullOrEmpty(gateway.LocalIdentity)
                        && string.Equals(personalCanvas.OwnerPlayerId, gateway.LocalIdentity, StringComparison.Ordinal)
                        && personalCanvas.State == PersonalCanvasPlacementState.Carried;
                case PartyWorldAction.CameraRefresh:
                case PartyWorldAction.CameraPrevious:
                case PartyWorldAction.CameraNext:
                case PartyWorldAction.CameraPreview:
                    return true;
                default: return false;
            }
        }

        public bool TryExecute(PartyWorldAction action)
        {
            if (!CanExecute(action)) return false;
            switch (action)
            {
                case PartyWorldAction.Host: gateway.RequestHost(); return true;
                case PartyWorldAction.Invite: gateway.RequestInvite(); return true;
                case PartyWorldAction.Leave: gateway.RequestLeave(); return true;
                case PartyWorldAction.SelectRelayCopy: return gateway.SelectMode(PartyMode.RelayCopy);
                case PartyWorldAction.SelectMemoryCopy: return gateway.SelectMode(PartyMode.MemoryCopy);
                case PartyWorldAction.SelectCoopMural: return gateway.SelectMode(PartyMode.CoopMural);
                case PartyWorldAction.StartSelectedMode: return gateway.StartSelectedMode();
                case PartyWorldAction.CarryCanvas: return personalCanvas.TryCarry(gateway.LocalIdentity);
                case PartyWorldAction.DockCanvas:
                    if (!personalCanvas.TryDock(gateway.LocalIdentity)) return false;
                    if (muralSession == null || !muralSession.View.CanLocalWrite || drawingController == null) return true;
                    drawingController.FinalizeActiveStrokes();
                    uint muralRevision = drawingController.DrawingRevision;
                    return muralSession.CompleteLocalTurn(
                        muralRevision > int.MaxValue ? int.MaxValue : (int)muralRevision);
                case PartyWorldAction.CameraRefresh:
                case PartyWorldAction.CameraPrevious:
                case PartyWorldAction.CameraNext:
                case PartyWorldAction.CameraPreview:
                    gateway.RequestCamera(action);
                    return true;
                default: return false;
            }
        }

        public void SetWorldReady(bool ready)
        {
            if (gateway == null) return;
            int slot = gateway.PartyView != null ? gateway.PartyView.localSlot : -1;
            gateway.SetReady(ready && slot >= 0 && CanOccupyReadyPad(slot));
        }

        public void TickRuntime(float nowSeconds)
        {
            if (float.IsNaN(nowSeconds) || float.IsInfinity(nowSeconds) || nowSeconds < 0f) return;
            OnlineRelayQuizView view = gateway?.PartyView;
            if (view == null || view.aborted)
            {
                if (!resetApplied) ResetRuntimeState();
                return;
            }
            resetApplied = false;
            UpdateCanvasMovement(view);
            if (boundRelaySession == null || boundTransport == null) return;
            PartyRosterSnapshot roster = GetLockedRoster(view);
            if (roster != null) EnsurePoseSession(roster);
            TickPose(nowSeconds);
            UpdateMural(view, roster, nowSeconds);
        }

        public void ResetRuntimeState()
        {
            if (readyPadsBySlot != null)
                foreach (WorldReadyPadInteractable pad in readyPadsBySlot) pad?.ResetPad();
            if (personalCanvas != null) personalCanvas.ResetForAbortOrDisconnect();
            if (physicalPaintTool != null) physicalPaintTool.ResetToRack();
            if (inputModeManager != null) inputModeManager.SetDrawingMovementAllowed(false);
            if (drawingController != null)
            {
                drawingController.FinalizeActiveStrokes();
                drawingController.ClearAll();
            }
            if (localWritableCanvasRoot != null) localWritableCanvasRoot.SetActive(false);
            DisposeNetworkDomains();
            ClearMuralPresentation();
            resetApplied = true;
        }

        public bool ValidateRuntimeConfiguration(out string error)
        {
            if (relayController == null && gateway == null) return Fail("relayController", out error);
            if (readyPadsBySlot == null || readyPadsBySlot.Length != PartyRoster.Capacity)
                return Fail("readyPadsBySlot[4]", out error);
            int actionCount = Enum.GetValues(typeof(PartyWorldAction)).Length;
            if (worldActions == null || worldActions.Length != actionCount)
                return Fail("one worldActions entry for every PartyWorldAction", out error);
            var foundActions = new bool[actionCount];
            for (int index = 0; index < worldActions.Length; index++)
            {
                WorldActionInteractable action = worldActions[index];
                int value = action != null ? (int)action.Action : -1;
                if (value < 0 || value >= actionCount || foundActions[value])
                    return Fail("unique worldActions entries", out error);
                foundActions[value] = true;
            }
            if (inputModeManager == null) return Fail("inputModeManager", out error);
            if (playerController == null) return Fail("playerController", out error);
            if (localPlayerRoot == null) return Fail("localPlayerRoot", out error);
            if (playerSpawnPointsBySlot == null || playerSpawnPointsBySlot.Length != PartyRoster.Capacity)
                return Fail("playerSpawnPointsBySlot[4]", out error);
            if (personalCanvas == null) return Fail("personalCanvas", out error);
            if (carriedCanvasAnchor == null) return Fail("carriedCanvasAnchor", out error);
            if (canvasDockAnchorsBySlot == null || canvasDockAnchorsBySlot.Length != PartyRoster.Capacity)
                return Fail("canvasDockAnchorsBySlot[4]", out error);
            if (float.IsNaN(canvasDockRadius) || float.IsInfinity(canvasDockRadius) || canvasDockRadius < 0f)
                return Fail("finite non-negative canvasDockRadius", out error);
            if (physicalPaintTool == null) return Fail("physicalPaintTool", out error);
            if (drawingController == null) return Fail("drawingController", out error);
            if (toolState == null || toolState.BrushCount <= 0) return Fail("toolState", out error);
            if (localWritableCanvasRoot == null) return Fail("localWritableCanvasRoot", out error);
            if (playerZoneBounds == null || playerZoneBounds.Length != PartyRoster.Capacity)
                return Fail("playerZoneBounds[4]", out error);
            if (remoteAvatarPresenters == null || remoteAvatarPresenters.Length != PartyRoster.Capacity - 1)
                return Fail("remoteAvatarPresenters[3]", out error);
            if (avatarRootsBySlot == null || avatarRootsBySlot.Length != PartyRoster.Capacity)
                return Fail("avatarRootsBySlot[4]", out error);
            if (muralLayerRoots == null || muralLayerPresenters == null || muralLayerSurfaces == null
                || muralLayerRoots.Length != PartyRoster.Capacity
                || muralLayerPresenters.Length != PartyRoster.Capacity
                || muralLayerSurfaces.Length != PartyRoster.Capacity)
                return Fail("mural layer root/presenter/surface arrays[4]", out error);
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                if (readyPadsBySlot[slot] == null || playerZoneBounds[slot] == null
                    || playerSpawnPointsBySlot[slot] == null || canvasDockAnchorsBySlot[slot] == null
                    || avatarRootsBySlot[slot] == null
                    || muralLayerRoots[slot] == null || muralLayerPresenters[slot] == null || muralLayerSurfaces[slot] == null)
                    return Fail("slot " + slot + " runtime references", out error);
            }
            for (int index = 0; index < remoteAvatarPresenters.Length; index++)
                if (remoteAvatarPresenters[index] == null) return Fail("remoteAvatarPresenters[" + index + "]", out error);
            error = string.Empty;
            return true;
        }

        public static bool IsReferenceVisible(OnlineRelayQuizView view)
        {
            if (view == null || view.aborted || !view.active || view.referenceDrawing == null
                || !view.hasSelectedMode) return false;
            switch (view.selectedMode)
            {
                case PartyMode.RelayCopy:
                    return view.state == RelayQuizState.ObservePrevious || view.state == RelayQuizState.Drawing;
                case PartyMode.MemoryCopy:
                    return view.state == RelayQuizState.ObservePrevious;
                default:
                    return false;
            }
        }

        private static bool IsStartReady(OnlineRelayQuizView view)
        {
            return view != null && !view.aborted && view.state == RelayQuizState.Setup && !view.modeStarted
                && view.rosterLocked && view.rosterCount == PartyRoster.Capacity && view.allReady && view.hasSelectedMode;
        }

        private void UpdateCanvasMovement(OnlineRelayQuizView view)
        {
            bool onlineGate = view.connected && !view.aborted && !view.paused && !view.transferPending;
            bool authoritativeDrawingContext = inputModeManager != null
                && inputModeManager.CurrentContext == InputContext.Drawing;
            bool coop = view.modeStarted && view.hasSelectedMode && view.selectedMode == PartyMode.CoopMural
                && view.startSignal > 0;
            bool writable = onlineGate && authoritativeDrawingContext
                && (coop && muralSession != null && muralSession.View.CanLocalWrite
                    || !coop && view.active && view.state == RelayQuizState.Drawing);
            if (localWritableCanvasRoot != null) localWritableCanvasRoot.SetActive(writable);
            bool carried = writable && personalCanvas != null && personalCanvas.State == PersonalCanvasPlacementState.Carried;
            if (inputModeManager != null)
            {
                inputModeManager.SetDrawingMovementAllowed(carried);
                if (!onlineGate && inputModeManager.CurrentContext == InputContext.Drawing)
                    inputModeManager.SetContext(InputContext.Blocked);
            }
        }

        private PartyRosterSnapshot GetLockedRoster(OnlineRelayQuizView view)
        {
            if (!view.rosterLocked || view.rosterCount != PartyRoster.Capacity || view.roster == null
                || view.roster.Length != PartyRoster.Capacity || string.IsNullOrEmpty(view.sessionId)
                || view.rosterGeneration <= 0) return null;
            if (cachedRoster != null && cachedRosterGeneration == view.rosterGeneration
                && string.Equals(cachedRosterSessionId, view.sessionId, StringComparison.Ordinal)) return cachedRoster;
            var slots = new PartyRosterSlotSnapshot[PartyRoster.Capacity];
            for (int slot = 0; slot < slots.Length; slot++)
            {
                string identity = view.roster[slot];
                if (string.IsNullOrEmpty(identity)) return null;
                slots[slot] = new PartyRosterSlotSnapshot(slot, identity, identity, true);
            }
            cachedRoster = new PartyRosterSnapshot(view.sessionId, view.rosterGeneration, view.roster[0], slots);
            cachedRosterSessionId = view.sessionId;
            cachedRosterGeneration = view.rosterGeneration;
            return cachedRoster;
        }

        private void EnsurePoseSession(PartyRosterSnapshot roster)
        {
            if (poseSession != null && poseRosterGeneration == roster.Generation) return;
            poseSession?.Dispose();
            poseSession = new PartyPoseSession(boundTransport, 15f);
            poseSession.Configure(roster);
            poseRosterGeneration = roster.Generation;
            ConfigureAssignedSlot(poseSession.LocalSlot, boundTransport.LocalPlayerId);
            int presenter = 0;
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                if (slot == poseSession.LocalSlot) continue;
                if (remoteAvatarPresenters != null && presenter < remoteAvatarPresenters.Length
                    && remoteAvatarPresenters[presenter] != null && avatarRootsBySlot != null
                    && slot < avatarRootsBySlot.Length && avatarRootsBySlot[slot] != null)
                {
                    Animator animator = avatarAnimatorsBySlot != null && slot < avatarAnimatorsBySlot.Length
                        ? avatarAnimatorsBySlot[slot] : null;
                    remoteAvatarPresenters[presenter].Initialize(poseSession, slot, avatarRootsBySlot[slot], animator);
                }
                presenter++;
            }
            if (playerController != null && playerZoneBounds != null && poseSession.LocalSlot < playerZoneBounds.Length
                && playerZoneBounds[poseSession.LocalSlot] != null)
            {
                Bounds bounds = playerZoneBounds[poseSession.LocalSlot].bounds;
                playerController.ConfigureMovementBounds(
                    new Vector2(bounds.min.x, bounds.min.z), new Vector2(bounds.max.x, bounds.max.z), true);
            }
        }

        private void TickPose(float nowSeconds)
        {
            if (poseSession == null || localPlayerRoot == null) return;
            Vector3 position = localPlayerRoot.position;
            PartyMoveState moveState = hasPreviousLocalPosition && (position - previousLocalPosition).sqrMagnitude > 0.000001f
                ? PartyMoveState.Walking : PartyMoveState.Idle;
            poseSession.Tick(nowSeconds, position, localPlayerRoot.eulerAngles.y, moveState);
            previousLocalPosition = position;
            hasPreviousLocalPosition = true;
        }

        private void UpdateMural(OnlineRelayQuizView view, PartyRosterSnapshot roster, float nowSeconds)
        {
            bool active = view.modeStarted && view.hasSelectedMode && view.selectedMode == PartyMode.CoopMural
                && view.startSignal > 0 && roster != null;
            if (!active)
            {
                if (muralSession != null) { muralSession.Reset(); muralSession.Dispose(); muralSession = null; }
                muralStartSignal = 0;
                if (muralPresentationActive) ClearMuralPresentation();
                return;
            }
            if (muralSession == null || muralStartSignal != view.startSignal)
            {
                muralSession?.Dispose();
                if (drawingController != null)
                {
                    drawingController.FinalizeActiveStrokes();
                    drawingController.ClearAll();
                    drawingController.SetStrokesVisible(true);
                }
                muralSession = new CoopMuralSession(boundTransport,
                    () => drawingController != null ? drawingController.ExportDrawing() : null,
                    boundBrushCount);
                muralSession.Configure(new PartyStartSnapshot(PartyMode.CoopMural, roster));
                muralStartSignal = view.startSignal;
                renderedMuralSerial = -1;
            }
            uint revision = drawingController != null ? drawingController.DrawingRevision : 0;
            muralSession.Tick(nowSeconds, revision > int.MaxValue ? int.MaxValue : (int)revision);
            if (renderedMuralSerial == muralSession.View.Serial) return;
            RenderMural(muralSession.View, muralSession.View.LocalSlot);
            renderedMuralSerial = muralSession.View.Serial;
        }

        public void RenderMural(CoopMuralView view, int localSlot)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            PartyRoster.ValidateSlot(localSlot);
            muralPresentationActive = true;
            bool localLayerIsLive = view.CanLocalWrite;
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                if (muralLayerRoots != null && slot < muralLayerRoots.Length && muralLayerRoots[slot] != null)
                    muralLayerRoots[slot].SetActive(slot != localSlot || !localLayerIsLive);
                if (muralLayerPresenters == null || muralLayerSurfaces == null
                    || slot >= muralLayerPresenters.Length || slot >= muralLayerSurfaces.Length
                    || muralLayerPresenters[slot] == null || muralLayerSurfaces[slot] == null) continue;
                if (slot == localSlot && localLayerIsLive)
                {
                    muralLayerPresenters[slot].ClearPresentation();
                    continue;
                }
                if (view.TryGetLayer(slot, out CoopMuralLayerSnapshot layer) && layer.Drawing != null)
                    muralLayerPresenters[slot].Show(layer.Drawing, muralLayerSurfaces[slot]);
                else muralLayerPresenters[slot].ClearPresentation();
            }
        }

        private void ClearMuralPresentation()
        {
            muralPresentationActive = false;
            renderedMuralSerial = -1;
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                if (muralLayerPresenters != null && slot < muralLayerPresenters.Length && muralLayerPresenters[slot] != null)
                    muralLayerPresenters[slot].ClearPresentation();
                if (muralLayerRoots != null && slot < muralLayerRoots.Length && muralLayerRoots[slot] != null)
                    muralLayerRoots[slot].SetActive(false);
            }
        }

        private void DisposeNetworkDomains()
        {
            if (playerController != null)
                playerController.ConfigureMovementBounds(Vector2.zero, Vector2.zero, false);
            muralSession?.Dispose();
            poseSession?.Dispose();
            muralSession = null;
            poseSession = null;
            poseRosterGeneration = 0;
            muralStartSignal = 0;
            renderedMuralSerial = -1;
            hasPreviousLocalPosition = false;
            cachedRoster = null;
            cachedRosterSessionId = null;
            cachedRosterGeneration = 0;
        }

        private void OnDestroy()
        {
            DisposeNetworkDomains();
        }

        private static bool Fail(string field, out string error)
        {
            error = "PartyWorldController requires " + field + ".";
            return false;
        }
    }
}
