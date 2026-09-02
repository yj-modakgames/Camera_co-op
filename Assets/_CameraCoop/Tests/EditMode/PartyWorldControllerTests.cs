using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CameraCoop.Party;
using CameraCoop.Netplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CameraCoop.Tests
{
    public sealed class PartyWorldControllerTests
    {
        private static readonly Vector3 WorldTestPoint = new Vector3(1000f, 1000f, 1000f);
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject created in objects)
                if (created != null) Object.DestroyImmediate(created);
            objects.Clear();
        }

        [Test]
        public void ReadyPad_DwellsEitherHandOnceAndClearsOnExitOrCameraLoss()
        {
            var gateway = new FakeGateway { CameraConnected = true, FreshHand = true };
            PartyWorldController controller = CreateController(gateway);
            WorldReadyPadInteractable pad = CreateObject("ready pad").AddComponent<WorldReadyPadInteractable>();
            pad.Configure(controller, 1f);

            pad.SetHandPresence("Left", true);
            pad.SetHandPresence("Right", true);
            pad.TickRuntime(0.6f);
            pad.TickRuntime(0.4f);

            CollectionAssert.AreEqual(new[] { true }, gateway.ReadyChanges);
            pad.SetHandPresence("Left", false);
            pad.TickRuntime(0f);
            CollectionAssert.AreEqual(new[] { true }, gateway.ReadyChanges, "the other fresh hand keeps one local ready state");
            gateway.CameraConnected = false;
            pad.TickRuntime(0f);
            CollectionAssert.AreEqual(new[] { true, false }, gateway.ReadyChanges);
        }

        [Test]
        public void ClientCannotUseHostModeOrStartPedestals()
        {
            var gateway = new FakeGateway { Host = false };
            gateway.View = ReadySetup();
            PartyWorldController controller = CreateController(gateway);

            Assert.That(controller.TryExecute(PartyWorldAction.SelectMemoryCopy), Is.False);
            Assert.That(controller.TryExecute(PartyWorldAction.StartSelectedMode), Is.False);
            Assert.That(gateway.SelectedModes, Is.Empty);
            Assert.That(gateway.StartCalls, Is.Zero);
        }

        [Test]
        public void HostStartsSelectorBeforeChoosingCatalogMode()
        {
            var gateway = new FakeGateway { Host = true };
            gateway.View = ReadySetup();
            gateway.View.hasSelectedMode = false;
            gateway.View.connected = true;
            gateway.View.transitionPhase = PartyTransitionPhase.Lobby;
            PartyWorldController controller = CreateController(gateway);
            GameObject selectorRoot = CreateObject("mode selector");
            selectorRoot.SetActive(false);
            controller.ConfigureModeSelectorRoot(selectorRoot);

            Assert.That(gateway.View.hasSelectedMode, Is.False);
            Assert.That(selectorRoot.activeSelf, Is.False);
            Assert.That(controller.TryExecute(PartyWorldAction.SelectRelayCopy), Is.False);
            Assert.That(controller.TryExecute(PartyWorldAction.StartSelectedMode), Is.True);
            Assert.That(gateway.View.transitionPhase, Is.EqualTo(PartyTransitionPhase.SelectingMode),
                "START opens the mode selector before any catalog mode exists.");
            controller.TickRuntime(0f);
            Assert.That(selectorRoot.activeSelf, Is.True);
            Assert.That(controller.TryExecute(PartyWorldAction.SelectRelayCopy), Is.True);
            Assert.That(controller.TryExecute(PartyWorldAction.SelectMemoryCopy), Is.True);
            Assert.That(controller.TryExecute(PartyWorldAction.SelectCoopMural), Is.True);
            CollectionAssert.AreEqual(
                new[] { PartyMode.RelayCopy, PartyMode.MemoryCopy, PartyMode.CoopMural },
                gateway.SelectedModes);
            Assert.That(gateway.StartCalls, Is.EqualTo(1));
            gateway.View.transitionPhase = PartyTransitionPhase.LoadingGame;
            controller.TickRuntime(1f);
            Assert.That(selectorRoot.activeSelf, Is.False);
            gateway.View.transitionPhase = PartyTransitionPhase.InGame;
            controller.TickRuntime(2f);
            Assert.That(selectorRoot.activeSelf, Is.False);
            gateway.View.transitionPhase = PartyTransitionPhase.Lobby;
            controller.TickRuntime(3f);
            Assert.That(selectorRoot.activeSelf, Is.False);
        }

        [Test]
        public void GameScenePortsCanRebindWithoutDisposingPersistentNetworkDomains()
        {
            var gateway = new FakeGateway { Host = true, LocalPlayerId = "p1" };
            gateway.View = PartialLobbyView();
            PartyWorldController controller = CreateController(gateway);
            var transport = new TrackingTransport(true, "p1");
            using var relay = new OnlineRelayQuizSession(transport, "p1", () => "camera",
                () => new CanvasDrawingData(), () => string.Empty, 3);
            controller.BindNetwork(relay, transport, 3);
            controller.TickRuntime(0f);
            object poseBefore = GetField(controller, "poseSession");
            object practiceBefore = GetField(controller, "practiceSession");

            FakeGamePort relayPort = CreateGamePort(PartyMode.RelayCopy);
            FakeGamePort memoryPort = CreateGamePort(PartyMode.MemoryCopy);
            controller.BindGamePort(relayPort);
            controller.UnbindScenePort(relayPort);
            controller.BindGamePort(memoryPort);

            Assert.That(GetField(controller, "poseSession"), Is.SameAs(poseBefore));
            Assert.That(GetField(controller, "practiceSession"), Is.SameAs(practiceBefore));
            Assert.That(controller.ActiveSceneMode, Is.EqualTo(PartyMode.MemoryCopy));
            Assert.That(transport.ShutdownCalls, Is.Zero);
        }

        [Test]
        public void GamePortRebindMovesPersistentDrawingAndPlacementTargetThenRestoresLobby()
        {
            var gateway = new FakeGateway { Host = true, LocalPlayerId = "p1", View = PartialLobbyView() };
            GameObject runtime = CreateObject("persistent drawing runtime");
            runtime.SetActive(false);
            ToolState tools = runtime.AddComponent<ToolState>();
            InputModeManager modes = runtime.AddComponent<InputModeManager>();
            GameObject lobbyPaper = CreateObject("lobby paper");
            CanvasSurface lobbySurface = CreateObject("lobby surface", lobbyPaper.transform).AddComponent<CanvasSurface>();
            HandPointer pointer = runtime.AddComponent<HandPointer>();
            SetPrivate(pointer, "inputSource", HandPointerInputSource.HandRouter);
            SetPrivate(pointer, "inputModeManager", modes);
            SetPrivate(pointer, "canvasSurface", lobbySurface);
            SetPrivate(pointer, "toolState", tools);
            DrawingController drawing = runtime.AddComponent<DrawingController>();
            SetPrivate(drawing, "handPointer", pointer);
            SetPrivate(drawing, "toolState", tools);
            SetPrivate(drawing, "canvasSurface", lobbySurface);
            HandCanvasInteractable lobbyInteractable = lobbySurface.gameObject.AddComponent<HandCanvasInteractable>();
            SetPrivate(lobbyInteractable, "canvasSurface", lobbySurface);
            SetPrivate(lobbyInteractable, "handPointer", pointer);
            runtime.SetActive(true);
            InputFocus.IsTyping = false;
            modes.SetContext(InputContext.Drawing);

            Transform carry = CreateObject("lobby carry").transform;
            var docks = new Transform[PartyRoster.Capacity];
            for (int slot = 0; slot < docks.Length; slot++) docks[slot] = CreateObject("lobby dock " + slot).transform;
            PersonalCanvasPlacement placement = lobbyPaper.AddComponent<PersonalCanvasPlacement>();
            placement.Configure("p1", carry, docks[0], 1f);
            PartyWorldController controller = CreateController(gateway);
            SetPrivate(controller, "drawingController", drawing);
            SetPrivate(controller, "toolState", tools);
            SetPrivate(controller, "handPointer", pointer);
            SetPrivate(controller, "localWritableCanvasRoot", lobbyPaper);
            SetPrivate(controller, "personalCanvas", placement);
            SetPrivate(controller, "canvasDockAnchorsBySlot", docks);
            SetPrivate(controller, "carriedCanvasAnchor", carry);
            FakeGamePort game = CreateGamePort(PartyMode.RelayCopy);

            controller.BindGamePort(game);
            CanvasSurface gameSurface = game.Bindings.WritableSurface;
            Assert.That(drawing.Surface, Is.SameAs(gameSurface));
            Assert.That(placement.CanvasTarget, Is.SameAs(game.Bindings.WritablePaperRoot.transform));
            Assert.That(GetField(game.Bindings.PhysicalPaintTool, "toolState"), Is.SameAs(tools));
            Assert.That(pointer.CanUseCanvas(gameSurface), Is.True);

            Assert.That(drawing.TryDrawStrokeForTest(new Vector2(0.1f, 0.1f), new Vector2(0.3f, 0.3f)), Is.True);

            Assert.That(gameSurface.GetComponentsInChildren<LineRenderer>(true).Length, Is.EqualTo(1));
            Assert.That(lobbySurface.GetComponentsInChildren<LineRenderer>(true).Length, Is.Zero);

            controller.UnbindScenePort(game);

            Assert.That(drawing.Surface, Is.SameAs(lobbySurface));
            Assert.That(placement.CanvasTarget, Is.SameAs(lobbyPaper.transform));
            Assert.That(gameSurface.GetComponentsInChildren<LineRenderer>(true).Length, Is.Zero);
            Assert.That(lobbySurface.GetComponentsInChildren<LineRenderer>(true).Length, Is.EqualTo(1));
        }

        [Test]
        public void ReboundLobbyPracticeWritesOnlyToCurrentPort()
        {
            var gateway = new FakeGateway { Host = true, LocalPlayerId = "p1", View = PartialLobbyView() };
            PartyWorldController controller = CreateController(gateway);
            LobbyFixture oldLobby = CreateLobbyPort("old lobby");
            LobbyFixture newLobby = CreateLobbyPort("new lobby");
            var transport = new TrackingTransport(true, "p1");
            using var relay = new OnlineRelayQuizSession(transport, "p1", () => "camera",
                () => new CanvasDrawingData(), () => string.Empty, 3);
            controller.BindLobbyPort(oldLobby.Port);
            controller.BindNetwork(relay, transport, 3);
            controller.TickRuntime(0f);
            var practice = (PartyPracticeDrawingSession)GetField(controller, "practiceSession");

            controller.BindLobbyPort(newLobby.Port);
            var drawing = new CanvasDrawingData
            {
                strokes = new[]
                {
                    new CanvasStrokeData
                    {
                        strokeId = 1, order = 0, brushId = 0, widthNormalized = 0.05f,
                        colorArgb = unchecked((int)0xff00ff00), xy = new[] { 0.1f, 0.1f, 0.9f, 0.9f }
                    }
                }
            };
            Assert.That(practice.View.Apply(0, 1, drawing), Is.True);
            var changed = (Action<PartyPracticeDrawingView>)typeof(PartyPracticeDrawingSession)
                .GetField("ViewChanged", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(practice);
            changed(practice.View);

            Assert.That(oldLobby.PracticeRoots[0].activeSelf, Is.False);
            Assert.That(oldLobby.PracticePresenters[0].transform.childCount, Is.Zero,
                "the unbound port must retain its cleared write revision");
            Assert.That(newLobby.PracticeRoots[0].activeSelf, Is.True);
            Assert.That(newLobby.PracticePresenters[0].transform.childCount, Is.EqualTo(1),
                "the later drawing revision must render on the current port only");
        }

        [Test]
        public void PartialPoseRebindsLobbyAvatarPresentersAndEpochWithoutWritingOldPort()
        {
            var gateway = new FakeGateway { Host = true, LocalPlayerId = "p1", View = TwoPlayerLobbyView() };
            PartyWorldController controller = CreateController(gateway);
            LobbyFixture oldLobby = CreateLobbyPort("old lobby");
            LobbyFixture newLobby = CreateLobbyPort("new lobby");
            var transport = new TrackingTransport(true, "p1");
            transport.AddFakePeer("p2", "P2");
            using var relay = new OnlineRelayQuizSession(transport, "p1", () => "camera",
                () => new CanvasDrawingData(), () => string.Empty, 3);
            controller.BindLobbyPort(oldLobby.Port);
            controller.BindNetwork(relay, transport, 3);
            controller.TickRuntime(0f);
            var pose = (PartyPoseSession)GetField(controller, "poseSession");
            Assert.That(oldLobby.AvatarRoots[1].activeSelf, Is.True);

            gateway.View.transitionGeneration = 2;
            controller.BindLobbyPort(newLobby.Port);

            Assert.That(oldLobby.AvatarRoots[1].activeSelf, Is.False);
            Assert.That(newLobby.AvatarRoots[1].activeSelf, Is.True);
            Assert.That(pose.TransitionGeneration, Is.EqualTo(2));
        }

        [Test]
        public void PracticeAndPartialPoseUseLobbyOnlyAndReconfigureForReturnedLobbyEpoch()
        {
            var gateway = new FakeGateway { Host = true, LocalPlayerId = "p1" };
            gateway.View = PartialLobbyView();
            PartyWorldController controller = CreateController(gateway);
            var transport = new TrackingTransport(true, "p1");
            using var relay = new OnlineRelayQuizSession(transport, "p1", () => "camera",
                () => new CanvasDrawingData(), () => string.Empty, 3);
            controller.BindNetwork(relay, transport, 3);

            controller.TickRuntime(0f);
            var practice = (PartyPracticeDrawingSession)GetField(controller, "practiceSession");
            Assert.That(controller.HasPoseSession, Is.True);
            Assert.That(practice.View.Configured, Is.True);
            Assert.That(practice.View.Layers[0].Occupied, Is.True);
            Assert.That(practice.View.Layers[1].Occupied, Is.False);
            Assert.That(practice.View.TransitionGeneration, Is.EqualTo(1));

            gateway.View.transitionPhase = PartyTransitionPhase.InGame;
            gateway.View.transitionGeneration = 2;
            controller.TickRuntime(1f);
            Assert.That(practice.View.Configured, Is.False);

            gateway.View.transitionPhase = PartyTransitionPhase.Lobby;
            gateway.View.transitionGeneration = 3;
            controller.TickRuntime(2f);
            Assert.That(practice.View.Configured, Is.True);
            Assert.That(practice.View.TransitionGeneration, Is.EqualTo(3));
            Assert.That(transport.ShutdownCalls, Is.Zero);
        }

        [Test]
        public void CanvasWorldActionsAlwaysUseLocalTransportIdentity()
        {
            var gateway = new FakeGateway { LocalPlayerId = "local-owner" };
            PartyWorldController controller = CreateController(gateway);
            Transform avatar = CreateObject("avatar").transform;
            Transform dock = CreateObject("dock").transform;
            PersonalCanvasPlacement placement = CreateObject("personal canvas").AddComponent<PersonalCanvasPlacement>();
            placement.Configure("local-owner", avatar, dock, 0.5f);
            controller.ConfigurePersonalCanvas(placement);

            Assert.That(controller.TryExecute(PartyWorldAction.CarryCanvas), Is.True);
            Assert.That(placement.State, Is.EqualTo(PersonalCanvasPlacementState.Carried));
            placement.transform.position = dock.position;
            Assert.That(controller.TryExecute(PartyWorldAction.DockCanvas), Is.True);
            Assert.That(placement.State, Is.EqualTo(PersonalCanvasPlacementState.Docked));
        }

        [Test]
        public void ReferenceVisibilityMatchesRelayAndMemoryCopyPolicies()
        {
            var drawing = new CanvasDrawingData();
            OnlineRelayQuizView relay = ReadySetup();
            relay.state = RelayQuizState.Drawing;
            relay.active = true;
            relay.selectedMode = PartyMode.RelayCopy;
            relay.referenceDrawing = drawing;
            Assert.That(PartyWorldController.IsReferenceVisible(relay), Is.True);

            OnlineRelayQuizView memoryObserve = ReadySetup();
            memoryObserve.state = RelayQuizState.ObservePrevious;
            memoryObserve.active = true;
            memoryObserve.selectedMode = PartyMode.MemoryCopy;
            memoryObserve.referenceDrawing = drawing;
            Assert.That(PartyWorldController.IsReferenceVisible(memoryObserve), Is.True);

            OnlineRelayQuizView memoryDrawing = ReadySetup();
            memoryDrawing.state = RelayQuizState.Drawing;
            memoryDrawing.active = true;
            memoryDrawing.selectedMode = PartyMode.MemoryCopy;
            memoryDrawing.referenceDrawing = drawing;
            Assert.That(PartyWorldController.IsReferenceVisible(memoryDrawing), Is.False);

            OnlineRelayQuizView mural = ReadySetup();
            mural.state = RelayQuizState.Drawing;
            mural.active = true;
            mural.selectedMode = PartyMode.CoopMural;
            mural.referenceDrawing = drawing;
            Assert.That(PartyWorldController.IsReferenceVisible(mural), Is.False);

            relay.hasSelectedMode = false;
            Assert.That(PartyWorldController.IsReferenceVisible(relay), Is.False);
        }

        [Test]
        public void CoopMuralCanvas_PauseOrConnectionLossClosesAuthoritativeDrawingUntilContextResumes()
        {
            var gateway = new FakeGateway { LocalPlayerId = "local-owner" };
            gateway.View = ActiveCoopMural();
            PartyWorldController controller = CreateController(gateway);
            InputModeManager modes = CreateObject("modes").AddComponent<InputModeManager>();
            GameObject writableCanvas = CreateObject("writable canvas");
            Transform carry = CreateObject("carry").transform;
            Transform dock = CreateObject("dock").transform;
            PersonalCanvasPlacement placement = CreateObject("personal canvas").AddComponent<PersonalCanvasPlacement>();
            placement.Configure("local-owner", carry, dock, 0.5f);
            controller.ConfigureInput(modes, null);
            controller.ConfigurePersonalCanvas(placement);
            controller.ConfigureWritableCanvas(writableCanvas);
            Assert.That(placement.TryCarry("local-owner"), Is.True);
            modes.SetContext(InputContext.Drawing);
            var transport = new TrackingTransport(true, "local-owner");
            transport.AddFakePeer("p1", "P1");
            transport.AddFakePeer("p2", "P2");
            transport.AddFakePeer("p3", "P3");
            var slots = new[]
            {
                new PartyRosterSlotSnapshot(0, "local-owner", "P0", true),
                new PartyRosterSlotSnapshot(1, "p1", "P1", true),
                new PartyRosterSlotSnapshot(2, "p2", "P2", true),
                new PartyRosterSlotSnapshot(3, "p3", "P3", true)
            };
            var muralSession = new CoopMuralSession(transport,
                () => new CanvasDrawingData { strokes = Array.Empty<CanvasStrokeData>() }, 3);
            muralSession.Configure(new PartyStartSnapshot(PartyMode.CoopMural,
                new PartyRosterSnapshot("mural", 1, "local-owner", slots)), 1);
            typeof(PartyWorldController).GetField("muralSession", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, muralSession);

            controller.TickRuntime(1f);

            Assert.That(writableCanvas.activeSelf, Is.True);
            Assert.That(modes.CanDraw, Is.True);
            Assert.That(modes.DrawingMovementAllowed, Is.True);

            gateway.View.paused = true;
            controller.TickRuntime(2f);

            Assert.That(writableCanvas.activeSelf, Is.False);
            Assert.That(modes.CurrentContext, Is.EqualTo(InputContext.Blocked));
            Assert.That(modes.CanDraw, Is.False);
            Assert.That(modes.DrawingMovementAllowed, Is.False);
            gateway.View.paused = false;
            controller.TickRuntime(3f);
            Assert.That(writableCanvas.activeSelf, Is.False,
                "Party runtime must not reopen input before the authoritative online controller restores Drawing context.");
            Assert.That(modes.CanDraw, Is.False);

            modes.SetContext(InputContext.Drawing);
            controller.TickRuntime(4f);
            Assert.That(writableCanvas.activeSelf, Is.True);
            Assert.That(modes.CanDraw, Is.True);

            gateway.View.connected = false;
            controller.TickRuntime(5f);
            Assert.That(writableCanvas.activeSelf, Is.False);
            Assert.That(modes.CanDraw, Is.False);
            Assert.That(modes.DrawingMovementAllowed, Is.False);
            muralSession.Dispose();
        }

        [Test]
        public void AbortResetReturnsCanvasDockedAndClearsDrawingMovementPermission()
        {
            var gateway = new FakeGateway { LocalPlayerId = "local-owner" };
            PartyWorldController controller = CreateController(gateway);
            InputModeManager modes = CreateObject("modes").AddComponent<InputModeManager>();
            controller.ConfigureInput(modes, null);
            Transform avatar = CreateObject("avatar").transform;
            Transform dock = CreateObject("dock").transform;
            PersonalCanvasPlacement placement = CreateObject("personal canvas").AddComponent<PersonalCanvasPlacement>();
            placement.Configure("local-owner", avatar, dock, 0.5f);
            controller.ConfigurePersonalCanvas(placement);
            Assert.That(controller.TryExecute(PartyWorldAction.CarryCanvas), Is.True);
            modes.SetDrawingMovementAllowed(true);

            controller.ResetRuntimeState();

            Assert.That(placement.State, Is.EqualTo(PersonalCanvasPlacementState.Docked));
            Assert.That(modes.DrawingMovementAllowed, Is.False);
        }

        [Test]
        public void AssignedSlotConfiguresOwnDockIdentitySpawnAndResetDestination()
        {
            var gateway = new FakeGateway { LocalPlayerId = "slot-two" };
            gateway.View.localSlot = 2;
            PartyWorldController controller = CreateController(gateway);
            Transform localRoot = CreateObject("local root").transform;
            Transform carry = CreateObject("carry").transform;
            var docks = new Transform[4];
            var spawns = new Transform[4];
            var zones = new BoxCollider[4];
            for (int slot = 0; slot < 4; slot++)
            {
                docks[slot] = CreateObject("dock " + slot).transform;
                spawns[slot] = CreateObject("spawn " + slot).transform;
                spawns[slot].position = new Vector3(slot * 10f, 1f, slot);
                zones[slot] = CreateObject("zone " + slot).AddComponent<BoxCollider>();
            }
            PersonalCanvasPlacement placement = CreateObject("personal canvas").AddComponent<PersonalCanvasPlacement>();
            controller.ConfigurePersonalCanvas(placement);
            controller.ConfigureInput(null, null);
            controller.ConfigureSlotLayout(localRoot, carry, docks, 0.5f, zones, spawns);

            controller.ConfigureAssignedSlot(2, "slot-two");

            Assert.That(placement.OwnerPlayerId, Is.EqualTo("slot-two"));
            Assert.That(placement.transform.parent, Is.SameAs(docks[2]));
            Assert.That(localRoot.position, Is.EqualTo(spawns[2].position));
            Assert.That(controller.TryExecute(PartyWorldAction.CarryCanvas), Is.True);
            controller.ResetRuntimeState();
            Assert.That(placement.transform.parent, Is.SameAs(docks[2]));
            Assert.That(placement.State, Is.EqualTo(PersonalCanvasPlacementState.Docked));
        }

        [Test]
        public void ResetRuntimeState_ReleasesConfiguredZoneSoPlayerCanWalkBeyondFormerSlot()
        {
            PartyWorldController controller = CreateController(new FakeGateway());
            GameObject rig = CreateObject("zone-bound player");
            rig.SetActive(false);
            Transform camera = new GameObject("zone-bound camera").transform;
            camera.SetParent(rig.transform, false);
            InputModeManager modes = rig.AddComponent<InputModeManager>();
            CharacterController character = rig.AddComponent<CharacterController>();
            character.height = 1.8f;
            character.radius = 0.3f;
            character.center = new Vector3(0f, 0.9f, 0f);
            PlayerController player = rig.AddComponent<PlayerController>();
            SerializedObject playerSerialized = new SerializedObject(player);
            playerSerialized.FindProperty("controlProfile").enumValueIndex = (int)PlayerControlProfile.ModalFirstPerson;
            playerSerialized.FindProperty("playerCamera").objectReferenceValue = camera;
            playerSerialized.FindProperty("characterController").objectReferenceValue = character;
            playerSerialized.FindProperty("inputModeManager").objectReferenceValue = modes;
            playerSerialized.ApplyModifiedPropertiesWithoutUndo();
            typeof(InputModeManager).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(modes, Array.Empty<object>());
            typeof(PlayerController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(player, Array.Empty<object>());
            rig.transform.position = new Vector3(1000f, 1000f, 1000f);
            rig.SetActive(true);
            Physics.SyncTransforms();

            controller.ConfigureInput(null, player);
            player.ConfigureMovementBounds(new Vector2(999.5f, 999.5f), new Vector2(1000.25f, 1000.25f), true);
            player.Step(Vector2.right, 1f);
            float edge = player.transform.position.x;

            controller.ResetRuntimeState();
            player.Step(Vector2.right, 0.5f);

            Assert.That(edge, Is.LessThanOrEqualTo(1000.251f));
            Assert.That(player.transform.position.x, Is.GreaterThan(edge + 1f));
        }

        [Test]
        public void OnlyAssignedSlotReadyPadCanChangeLocalReady()
        {
            var gateway = new FakeGateway { CameraConnected = true, FreshHand = true };
            gateway.View.localSlot = 2;
            PartyWorldController controller = CreateController(gateway);
            var pads = new WorldReadyPadInteractable[4];
            for (int slot = 0; slot < pads.Length; slot++)
                pads[slot] = CreateObject("ready " + slot).AddComponent<WorldReadyPadInteractable>();
            controller.ConfigureReadyPads(pads);

            pads[1].SetHandPresence("Left", true);
            pads[1].TickRuntime(1f);
            Assert.That(gateway.ReadyChanges, Is.Empty);

            pads[2].SetHandPresence("Left", true);
            pads[2].TickRuntime(1f);
            CollectionAssert.AreEqual(new[] { true }, gateway.ReadyChanges);
        }

        [Test]
        public void WorldInteractablesOptIntoPhysicsHitRouting()
        {
            PartyWorldController controller = CreateController(new FakeGateway());
            WorldReadyPadInteractable pad = CreateObject("ready pad").AddComponent<WorldReadyPadInteractable>();
            pad.Configure(controller, 0, 1f);
            WorldActionInteractable action = CreateObject("action").AddComponent<WorldActionInteractable>();
            action.Configure(controller, PartyWorldAction.Host);

            Assert.That(pad.UsesWorldHitPosition, Is.True);
            Assert.That(action.UsesWorldHitPosition, Is.True);
        }

        [Test]
        public void WorldLabelBillboard_ActionLabelFacesPitchedOffAxisCameraWithReadableFrontAndKeepsLayout()
        {
            GameObject cameraObject = CreateObject("label camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(new Vector3(4f, 6f, -5f), Quaternion.Euler(28f, 37f, 0f));
            GameObject labelObject = CreateObject("action label");
            labelObject.transform.SetPositionAndRotation(new Vector3(-1f, 1.2f, 2f), Quaternion.Euler(13f, 19f, 7f));
            labelObject.transform.localScale = new Vector3(1.3f, 0.8f, 1.1f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            WorldLabelBillboard billboard = labelObject.AddComponent<WorldLabelBillboard>();
            billboard.Configure(label, camera);
            Vector3 positionBefore = labelObject.transform.position;
            Vector3 scaleBefore = labelObject.transform.localScale;

            bool updated = billboard.RefreshFacing();

            Vector3 awayFromCamera = (labelObject.transform.position - camera.transform.position).normalized;
            Assert.That(updated, Is.True);
            Assert.That(Vector3.Dot(labelObject.transform.forward, awayFromCamera), Is.GreaterThan(0.999f));
            Assert.That(labelObject.transform.position, Is.EqualTo(positionBefore));
            Assert.That(labelObject.transform.localScale, Is.EqualTo(scaleBefore));
        }

        [Test]
        public void WorldLabelBillboard_WithoutCameraLeavesTransformUnchanged()
        {
            GameObject labelObject = CreateObject("unbound action label");
            labelObject.transform.SetPositionAndRotation(new Vector3(2f, 3f, 4f), Quaternion.Euler(17f, 31f, 11f));
            labelObject.transform.localScale = new Vector3(0.9f, 1.4f, 0.7f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            WorldLabelBillboard billboard = labelObject.AddComponent<WorldLabelBillboard>();
            billboard.Configure(label, null);
            Vector3 positionBefore = labelObject.transform.position;
            Quaternion rotationBefore = labelObject.transform.rotation;
            Vector3 scaleBefore = labelObject.transform.localScale;
            Camera[] activeCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            var originalTags = new List<string>();
            foreach (Camera camera in activeCameras)
            {
                originalTags.Add(camera.gameObject.tag);
                camera.gameObject.tag = "Untagged";
            }
            try
            {
                bool updated = billboard.RefreshFacing();

                Assert.That(updated, Is.False);
                Assert.That(labelObject.transform.position, Is.EqualTo(positionBefore));
                Assert.That(labelObject.transform.rotation, Is.EqualTo(rotationBefore));
                Assert.That(labelObject.transform.localScale, Is.EqualTo(scaleBefore));
            }
            finally
            {
                for (int index = 0; index < activeCameras.Length; index++)
                    activeCameras[index].gameObject.tag = originalTags[index];
            }
        }

        [Test]
        public void RelayQuizOnline_LobbyTitleMountsFlushOnDeskFacadeWithoutBillboard()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/_CameraCoop/Scenes/RelayQuizOnline.unity",
                OpenSceneMode.Single);
            TextMesh title = Resources.FindObjectsOfTypeAll<TextMesh>()
                .Single(item => item != null && item.gameObject.scene == scene && item.text == "4 PLAYER CAMERA CO-OP");
            GameObject desk = Resources.FindObjectsOfTypeAll<GameObject>()
                .Single(item => item != null && item.scene == scene && item.name == "LobbyDesk");
            Renderer deskRenderer = desk.GetComponent<Renderer>();
            Renderer titleRenderer = title.GetComponent<Renderer>();
            WorldActionInteractable[] lobbyActions = Resources.FindObjectsOfTypeAll<WorldActionInteractable>()
                .Where(item => item != null && item.gameObject.scene == scene
                    && (item.Action == PartyWorldAction.Host || item.Action == PartyWorldAction.Invite
                        || item.Action == PartyWorldAction.Leave))
                .ToArray();

            Assert.That(title.GetComponent<WorldLabelBillboard>(), Is.Null);
            Assert.That(deskRenderer, Is.Not.Null);
            Assert.That(titleRenderer, Is.Not.Null);
            Assert.That(lobbyActions, Has.Length.EqualTo(3));

            Bounds deskBounds = deskRenderer.bounds;
            Bounds titleBounds = titleRenderer.bounds;
            float outwardOffset = deskBounds.min.z - title.transform.position.z;
            Assert.That(outwardOffset, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.03f));
            Assert.That(titleBounds.min.x, Is.GreaterThanOrEqualTo(deskBounds.min.x));
            Assert.That(titleBounds.max.x, Is.LessThanOrEqualTo(deskBounds.max.x));
            Assert.That(titleBounds.min.y, Is.GreaterThanOrEqualTo(deskBounds.min.y));
            Assert.That(titleBounds.max.y, Is.LessThanOrEqualTo(deskBounds.max.y));
            Assert.That(Vector3.Dot(title.transform.forward, Vector3.forward), Is.GreaterThan(0.999f));
            Assert.That(titleBounds.max.y, Is.LessThan(lobbyActions.Min(item => item.GetComponent<Collider>().bounds.min.y)));
        }

        [Test]
        public void RelayQuizOnline_OnlyTwentyOneImmediateControlsReceiveBoundWorldLabelBillboards()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/_CameraCoop/Scenes/RelayQuizOnline.unity",
                OpenSceneMode.Single);
            WorldLabelBillboard[] billboards = Resources.FindObjectsOfTypeAll<WorldLabelBillboard>()
                .Where(item => item != null && item.gameObject.scene == scene).ToArray();
            WorldActionInteractable[] actions = Resources.FindObjectsOfTypeAll<WorldActionInteractable>()
                .Where(item => item != null && item.gameObject.scene == scene).ToArray();
            WorldReadyPadInteractable[] pads = Resources.FindObjectsOfTypeAll<WorldReadyPadInteractable>()
                .Where(item => item != null && item.gameObject.scene == scene).ToArray();
            PhysicalToolStation[] labeledStations = Resources.FindObjectsOfTypeAll<PhysicalToolStation>()
                .Where(item => item != null && item.gameObject.scene == scene
                    && item.GetComponentInChildren<TextMesh>(true) != null).ToArray();

            Assert.That(billboards, Has.Length.EqualTo(21));
            Assert.That(actions, Has.Length.EqualTo(13));
            Assert.That(pads, Has.Length.EqualTo(4));
            Assert.That(labeledStations, Has.Length.EqualTo(4));
            foreach (Component control in actions.Cast<Component>().Concat(pads).Concat(labeledStations))
            {
                TextMesh label = control.GetComponentInChildren<TextMesh>(true);
                WorldLabelBillboard billboard = control.GetComponentInChildren<WorldLabelBillboard>(true);
                Assert.That(label, Is.Not.Null, control.name + " needs a control label.");
                Assert.That(billboard, Is.Not.Null, control.name + " needs a WorldLabelBillboard.");
                Assert.That(billboard.TextLabel, Is.SameAs(label));
                Assert.That(billboard.PlayerCamera, Is.Not.Null);
            }
        }

        [Test]
        public void PhysicsRouter_ReleasesWorldActionAndDwellsReadyPadAtResolvedCollider()
        {
            var gateway = new FakeGateway
            {
                LocalPlayerId = string.Empty,
                CameraConnected = true,
                FreshHand = true
            };
            PartyWorldController controller = CreateController(gateway);
            InputModeManager modes = CreateObject("world modes").AddComponent<InputModeManager>();
            modes.SetContext(InputContext.UiOnly);
            HandInputRouter router = CreateWorldRouter(modes, Array.Empty<GraphicRaycaster>(), out Camera camera);
            Vector2 screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 worldPoint = camera.ScreenPointToRay(screen).GetPoint(5f);

            WorldActionInteractable action = CreateObject("host pedestal").AddComponent<WorldActionInteractable>();
            action.transform.position = worldPoint;
            action.gameObject.AddComponent<BoxCollider>();
            action.Configure(controller, PartyWorldAction.Host);
            Physics.SyncTransforms();

            Assert.That(modes.CanUseHandUi, Is.True);
            Assert.That(router.isActiveAndEnabled, Is.True);
            Assert.That(controller.CanExecute(PartyWorldAction.Host), Is.True);
            Assert.That(router.ResolveTarget(screen, out _, out bool actionBlocked), Is.SameAs(action));
            Assert.That(actionBlocked, Is.False);

            Assert.That(Route(router, screen, 1, 0f, false), Is.SameAs(action));
            Assert.That(Route(router, screen, 2, 0.11f, false), Is.SameAs(action));
            Assert.That(router.TryGetHandState("Left", out HandInputState armed), Is.True);
            Assert.That(armed.isFresh, Is.True);
            Assert.That(armed.isArmed, Is.True);
            Assert.That(Route(router, screen, 3, 0.12f, true), Is.SameAs(action));
            Assert.That(Route(router, screen, 4, 0.13f, false), Is.SameAs(action));

            Assert.That(gateway.HostCalls, Is.EqualTo(1));

            action.transform.position = worldPoint + Vector3.right * 5f;
            WorldReadyPadInteractable pad = CreateObject("ready pad collider").AddComponent<WorldReadyPadInteractable>();
            pad.transform.position = worldPoint;
            pad.gameObject.AddComponent<BoxCollider>();
            pad.Configure(controller, 0, 1f);
            Physics.SyncTransforms();

            HandInteractable resolved = router.ResolveTarget(screen, out Vector3 hit, out bool blocked);
            Assert.That(blocked, Is.False);
            Assert.That(resolved, Is.SameAs(pad));
            router.ProcessSample(WorldSample("Right", screen, 1, false), 1f, resolved, hit);
            pad.TickRuntime(1f);

            CollectionAssert.AreEqual(new[] { true }, gateway.ReadyChanges);
        }

        [Test]
        public void PhysicsRouter_IgnoresNonInteractableTriggerVolumeBeforeWorldAction()
        {
            PartyWorldController controller = CreateController(new FakeGateway());
            InputModeManager modes = CreateObject("trigger routing modes").AddComponent<InputModeManager>();
            modes.SetContext(InputContext.UiOnly);
            HandInputRouter router = CreateWorldRouter(modes, Array.Empty<GraphicRaycaster>(), out Camera camera);
            Vector2 screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Ray ray = camera.ScreenPointToRay(screen);

            GameObject triggerVolume = CreateObject("room bounds trigger");
            triggerVolume.transform.position = ray.GetPoint(2f);
            BoxCollider triggerCollider = triggerVolume.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;

            WorldActionInteractable action = CreateObject("camera action").AddComponent<WorldActionInteractable>();
            action.transform.position = ray.GetPoint(5f);
            action.gameObject.AddComponent<BoxCollider>();
            action.Configure(controller, PartyWorldAction.CameraRefresh);
            Physics.SyncTransforms();

            HandInteractable resolved = router.ResolveTarget(screen, out _, out bool blocked);

            Assert.That(blocked, Is.False);
            Assert.That(resolved, Is.SameAs(action), "A non-interactable trigger volume must not block a world action behind it.");
        }

        [Test]
        public void PhysicsRouter_NonTriggerColliderStillBlocksWorldActionBehindIt()
        {
            PartyWorldController controller = CreateController(new FakeGateway());
            InputModeManager modes = CreateObject("occluder routing modes").AddComponent<InputModeManager>();
            modes.SetContext(InputContext.UiOnly);
            HandInputRouter router = CreateWorldRouter(modes, Array.Empty<GraphicRaycaster>(), out Camera camera);
            Vector2 screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Ray ray = camera.ScreenPointToRay(screen);

            GameObject occluder = CreateObject("solid room prop");
            occluder.transform.position = ray.GetPoint(2f);
            occluder.AddComponent<BoxCollider>();

            WorldActionInteractable action = CreateObject("camera action").AddComponent<WorldActionInteractable>();
            action.transform.position = ray.GetPoint(5f);
            action.gameObject.AddComponent<BoxCollider>();
            action.Configure(controller, PartyWorldAction.CameraRefresh);
            Physics.SyncTransforms();

            HandInteractable resolved = router.ResolveTarget(screen, out _, out bool blocked);

            Assert.That(blocked, Is.False);
            Assert.That(resolved, Is.Null, "A nearer solid collider must continue to occlude a world action behind it.");
        }

        [UnityTest]
        public IEnumerator PhysicsRouter_OverlayBlockerPreventsWorldReadyPadHover()
        {
            var gateway = new FakeGateway { CameraConnected = true, FreshHand = true };
            PartyWorldController controller = CreateController(gateway);
            InputModeManager modes = CreateObject("blocked modes").AddComponent<InputModeManager>();
            modes.SetContext(InputContext.UiOnly);
            GraphicRaycaster raycaster = CreateOverlayBlocker();
            HandInputRouter router = CreateWorldRouter(modes, new[] { raycaster }, out Camera camera);
            Vector2 screen = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            WorldReadyPadInteractable pad = CreateObject("blocked ready pad").AddComponent<WorldReadyPadInteractable>();
            pad.transform.position = camera.ScreenPointToRay(screen).GetPoint(5f);
            pad.gameObject.AddComponent<BoxCollider>();
            pad.Configure(controller, 0, 0.05f);
            Physics.SyncTransforms();
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();

            HandInteractable resolved = router.ResolveTarget(screen, out _, out bool blocked);

            Assert.That(blocked, Is.True);
            Assert.That(resolved, Is.Null);
            pad.TickRuntime(1f);
            Assert.That(gateway.ReadyChanges, Is.Empty);
        }

        [Test]
        public void CoopMuralPresentationSkipsLiveLocalLayerAndEnablesThreeRemoteLayers()
        {
            PartyWorldController controller = CreateController(new FakeGateway());
            var roots = new GameObject[4];
            var presenters = new CanvasDrawingPresenter[4];
            var surfaces = new CanvasSurface[4];
            for (int slot = 0; slot < 4; slot++)
            {
                roots[slot] = CreateObject("layer root " + slot);
                presenters[slot] = roots[slot].AddComponent<CanvasDrawingPresenter>();
                surfaces[slot] = CreateObject("shared surface " + slot).AddComponent<CanvasSurface>();
            }
            controller.ConfigureMuralLayers(roots, presenters, surfaces);
            var view = new CoopMuralView();
            typeof(CoopMuralView).GetMethod("Configure", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, new object[] { "mural", 1, 2 });
            typeof(CoopMuralView).GetProperty(nameof(CoopMuralView.ActiveSlot))
                .SetValue(view, 2);

            controller.RenderMural(view, 2);

            Assert.That(roots[2].activeSelf, Is.False, "the DrawingController already renders the local owner layer");
            Assert.That(roots[0].activeSelf, Is.True);
            Assert.That(roots[1].activeSelf, Is.True);
            Assert.That(roots[3].activeSelf, Is.True);
        }

        [Test]
        public void CoopMuralFinalDisplayEnablesAllFourReadOnlyLayers()
        {
            PartyWorldController controller = CreateController(new FakeGateway());
            var roots = new GameObject[PartyRoster.Capacity];
            var presenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
            var surfaces = new CanvasSurface[PartyRoster.Capacity];
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                roots[slot] = CreateObject("final mural layer " + slot);
                presenters[slot] = roots[slot].AddComponent<CanvasDrawingPresenter>();
                surfaces[slot] = CreateObject("final mural surface " + slot).AddComponent<CanvasSurface>();
            }
            controller.ConfigureMuralLayers(roots, presenters, surfaces);
            var view = new CoopMuralView();
            typeof(CoopMuralView).GetMethod("Configure", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(view, new object[] { "mural", 1, 2 });
            typeof(CoopMuralView).GetProperty(nameof(CoopMuralView.IsFinalDisplay))
                .SetValue(view, true);
            typeof(CoopMuralView).GetProperty(nameof(CoopMuralView.ActiveSlot))
                .SetValue(view, -1);

            controller.RenderMural(view, 2);

            Assert.That(roots, Has.All.Matches<GameObject>(root => root.activeSelf));
        }

        [Test]
        public void RealOnlineRoster_StartCreatesPartyDomainsAndResetOrUnbindDoesNotShutdownSharedTransport()
        {
            using (var online = new OnlinePartyFixture())
            {
                Assert.That(online.Host.View.rosterLocked, Is.True);
                Assert.That(online.Host.View.rosterCount, Is.EqualTo(PartyRoster.Capacity));
                Assert.That(online.Host.View.modeStarted, Is.True);
                Assert.That(online.Host.View.startSignal, Is.GreaterThan(0));

                PartyWorldController controller = CreateObject("integrated party world").AddComponent<PartyWorldController>();
                controller.ConfigureGateway(new SessionGateway(online.Host));
                InputModeManager modes = CreateObject("integrated modes").AddComponent<InputModeManager>();
                modes.SetContext(InputContext.Drawing);
                controller.ConfigureInput(modes, null);
                var roots = new GameObject[PartyRoster.Capacity];
                var presenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
                var surfaces = new CanvasSurface[PartyRoster.Capacity];
                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                {
                    roots[slot] = CreateObject("integrated mural root " + slot);
                    presenters[slot] = roots[slot].AddComponent<CanvasDrawingPresenter>();
                    surfaces[slot] = CreateObject("integrated mural surface " + slot).AddComponent<CanvasSurface>();
                }
                controller.ConfigureMuralLayers(roots, presenters, surfaces);
                controller.BindNetwork(online.Host, online.HostTransport, 3);

                controller.TickRuntime(1f);

                Assert.That(controller.HasPoseSession, Is.True);
                Assert.That(controller.HasMuralSession, Is.True);
                Assert.That(roots[0].activeSelf, Is.False);
                Assert.That(roots[1].activeSelf, Is.True);
                Assert.That(roots[2].activeSelf, Is.True);
                Assert.That(roots[3].activeSelf, Is.True);

                controller.ResetRuntimeState();

                Assert.That(controller.HasPoseSession, Is.False);
                Assert.That(controller.HasMuralSession, Is.False);
                Assert.That(roots, Has.All.Matches<GameObject>(root => !root.activeSelf));
                Assert.That(online.HostTransport.ShutdownCalls, Is.Zero);
                Assert.That(online.Host.View.aborted, Is.False);

                modes.SetContext(InputContext.Drawing);
                controller.TickRuntime(2f);
                Assert.That(controller.HasPoseSession, Is.True);
                Assert.That(controller.HasMuralSession, Is.True);

                controller.UnbindNetwork();

                Assert.That(controller.HasPoseSession, Is.False);
                Assert.That(controller.HasMuralSession, Is.False);
                Assert.That(roots, Has.All.Matches<GameObject>(root => !root.activeSelf));
                Assert.That(online.HostTransport.ShutdownCalls, Is.Zero,
                    "Party domains share the online transport but do not own its shutdown lifecycle.");
                Assert.That(online.Host.View.aborted, Is.False);
            }
        }

        [Test]
        public void ReturnToLobby_IsHostOnlyAndResultOnly()
        {
            var gateway = new FakeGateway { Host = true };
            gateway.View = ReadySetup();
            gateway.View.transitionPhase = PartyTransitionPhase.InGame;
            PartyWorldController controller = CreateController(gateway);
            FakeGamePort port = CreateGamePort(PartyMode.RelayCopy);
            controller.BindGamePort(port);

            Assert.That(controller.TryExecute(PartyWorldAction.ReturnToLobby), Is.False);
            port.Bindings.ResultRoot.SetActive(true);
            gateway.Host = false;
            Assert.That(controller.TryExecute(PartyWorldAction.ReturnToLobby), Is.False);
            gateway.Host = true;
            Assert.That(controller.TryExecute(PartyWorldAction.ReturnToLobby), Is.True);
            Assert.That(gateway.ReturnCalls, Is.EqualTo(1));
        }

        [Test]
        public void ConnectedLobbySlot_ShowsPracticePaperUntilGameOrAbort()
        {
            var gateway = new FakeGateway { View = PartialLobbyView() };
            PartyWorldController controller = CreateController(gateway);
            GameObject writable = CreateObject("lobby writable");
            controller.ConfigureWritableCanvas(writable);

            controller.TickRuntime(0f);
            Assert.That(writable.activeSelf, Is.True);
            gateway.View.transitionPhase = PartyTransitionPhase.InGame;
            controller.TickRuntime(1f);
            Assert.That(writable.activeSelf, Is.False);
            gateway.View.transitionPhase = PartyTransitionPhase.Lobby;
            gateway.View.aborted = true;
            controller.TickRuntime(2f);
            Assert.That(writable.activeSelf, Is.False);
        }

        [Test]
        public void FinalMuralCallbackUsesCurrentEpochAndMarksTheAuthoritativeHostOnce()
        {
            using (var online = new OnlinePartyFixture())
            {
                PartyWorldController controller = CreateObject("mural final host").AddComponent<PartyWorldController>();
                controller.ConfigureGateway(new SessionGateway(online.Host));
                controller.BindNetwork(online.Host, online.HostTransport, 3);
                controller.TickRuntime(1f);

                CoopMuralSession session = (CoopMuralSession)typeof(PartyWorldController)
                    .GetField("muralSession", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(controller);
                Assert.That(session, Is.Not.Null);
                Assert.That(online.Host.RequestReturnToLobby(), Is.False);

                var murals = new CoopMuralSession[PartyRoster.Capacity - 1];
                try
                {
                    for (int slot = 1; slot < PartyRoster.Capacity; slot++)
                    {
                        murals[slot - 1] = new CoopMuralSession(online.ClientTransports[slot - 1],
                            () => MuralDrawing(), 3);
                        murals[slot - 1].Configure(MuralStart(online.Host.View), online.Host.View.startSignal);
                    }
                    for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                    {
                        Assert.That(session.View.Apply(slot, 1, MuralDrawing()), Is.True);
                        foreach (CoopMuralSession mural in murals)
                            Assert.That(mural.View.Apply(slot, 1, MuralDrawing()), Is.True);
                        if (slot == 0)
                            Assert.That(session.CompleteLocalTurn(1), Is.True);
                        else
                        {
                            CoopMuralSession owner = murals[slot - 1];
                            Assert.That(owner.View.CanLocalWrite, Is.True);
                            Assert.That(owner.CompleteLocalTurn(1), Is.True);
                        }
                        online.Pump();
                    }
                }
                finally
                {
                    foreach (CoopMuralSession mural in murals) mural?.Dispose();
                }

                Assert.That(session.View.IsFinalDisplay, Is.True);
                Assert.That(online.Host.MarkCoopMuralFinalDisplay(), Is.False);
                Assert.That(online.Host.RequestReturnToLobby(), Is.True);
            }
        }

        private static PartyStartSnapshot MuralStart(OnlineRelayQuizView view)
        {
            var slots = new PartyRosterSlotSnapshot[PartyRoster.Capacity];
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                slots[slot] = new PartyRosterSlotSnapshot(slot, view.roster[slot], "P" + slot, true);
            return new PartyStartSnapshot(PartyMode.CoopMural,
                new PartyRosterSnapshot(view.sessionId, view.rosterGeneration, view.roster[0], slots));
        }

        private static CanvasDrawingData MuralDrawing()
        {
            return new CanvasDrawingData
            {
                strokes = new[]
                {
                    new CanvasStrokeData
                    {
                        strokeId = 1,
                        order = 0,
                        xy = new[] { 0.1f, 0.2f, 0.2f, 0.3f },
                        colorArgb = unchecked((int)0xFFFFFFFF),
                        widthNormalized = 0.1f,
                        brushId = 0
                    }
                }
            };
        }

        [Test]
        public void RuntimeValidationReportsFirstMissingSceneContractReference()
        {
            PartyWorldController controller = CreateController(new FakeGateway());

            Assert.That(controller.ValidateRuntimeConfiguration(out string error), Is.False);
            Assert.That(error, Does.Contain("readyPadsBySlot"));
        }

        private PartyWorldController CreateController(FakeGateway gateway)
        {
            PartyWorldController controller = CreateObject("party world").AddComponent<PartyWorldController>();
            controller.ConfigureGateway(gateway);
            return controller;
        }

        private GameObject CreateObject(string name, Transform parent = null)
        {
            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            objects.Add(created);
            return created;
        }

        private HandInputRouter CreateWorldRouter(InputModeManager modes, GraphicRaycaster[] raycasters, out Camera camera)
        {
            GameObject rig = CreateObject("world router");
            rig.SetActive(false);
            camera = rig.AddComponent<Camera>();
            camera.transform.position = WorldTestPoint + Vector3.back * 5f;
            camera.transform.rotation = Quaternion.identity;
            EventSystem events = CreateObject("world events").AddComponent<EventSystem>();
            HandInputRouter router = rig.AddComponent<HandInputRouter>();
            var serialized = new SerializedObject(router);
            serialized.FindProperty("inputModeManager").objectReferenceValue = modes;
            serialized.FindProperty("playerCamera").objectReferenceValue = camera;
            serialized.FindProperty("eventSystem").objectReferenceValue = events;
            SerializedProperty list = serialized.FindProperty("uiRaycasters");
            list.arraySize = raycasters.Length;
            for (int index = 0; index < raycasters.Length; index++)
                list.GetArrayElementAtIndex(index).objectReferenceValue = raycasters[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            rig.SetActive(true);
            typeof(HandInputRouter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(router, Array.Empty<object>());
            return router;
        }

        private GraphicRaycaster CreateOverlayBlocker()
        {
            GameObject canvasObject = CreateObject("world UI blocker");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            GraphicRaycaster raycaster = canvasObject.AddComponent<DeterministicOverlayRaycaster>();
            GameObject blocker = new GameObject("full screen blocker", typeof(RectTransform), typeof(Image));
            objects.Add(blocker);
            blocker.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = (RectTransform)blocker.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            blocker.GetComponent<Image>().raycastTarget = true;
            return raycaster;
        }

        private static HandInteractable Route(HandInputRouter router, Vector2 screen, ulong id, float now, bool pinched)
        {
            HandInteractable target = router.ResolveTarget(screen, out Vector3 hit, out _);
            router.ProcessSample(WorldSample("Left", screen, id, pinched), now, target, hit);
            return target;
        }

        private static HandInputSample WorldSample(string hand, Vector2 screen, ulong id, bool pinched)
        {
            return new HandInputSample(hand, screen, (uint)id, id, 0f, true, pinched, HandCancelReason.None);
        }

        private static OnlineRelayQuizView ReadySetup()
        {
            return new OnlineRelayQuizView
            {
                state = RelayQuizState.Setup,
                rosterCount = 4,
                rosterLocked = true,
                allReady = true,
                hasSelectedMode = true,
                selectedMode = PartyMode.RelayCopy
            };
        }

        private static OnlineRelayQuizView PartialLobbyView()
        {
            return new OnlineRelayQuizView
            {
                state = RelayQuizState.Setup,
                connected = true,
                sessionId = "partial-party",
                rosterGeneration = 1,
                transitionGeneration = 1,
                transitionPhase = PartyTransitionPhase.Lobby,
                localSlot = 0,
                rosterCount = 1,
                roster = new[] { "p1", string.Empty, string.Empty, string.Empty }
            };
        }

        private static OnlineRelayQuizView TwoPlayerLobbyView()
        {
            OnlineRelayQuizView view = PartialLobbyView();
            view.rosterCount = 2;
            view.roster[1] = "p2";
            return view;
        }

        private LobbyFixture CreateLobbyPort(string name)
        {
            GameObject root = CreateObject(name + " root");
            PartyLobbyScenePort port = CreateObject(name + " port", root.transform).AddComponent<PartyLobbyScenePort>();
            var spawns = new Transform[PartyRoster.Capacity];
            var practiceRoots = new GameObject[PartyRoster.Capacity];
            var practicePresenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
            var practiceSurfaces = new CanvasSurface[PartyRoster.Capacity];
            var avatarRoots = new GameObject[PartyRoster.Capacity];
            var avatarPresenters = new RemoteAvatarPresenter[PartyRoster.Capacity - 1];
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                spawns[slot] = CreateObject(name + " spawn " + slot, root.transform).transform;
                practiceRoots[slot] = CreateObject(name + " practice root " + slot, root.transform);
                practicePresenters[slot] = CreateObject(name + " practice presenter " + slot, root.transform)
                    .AddComponent<CanvasDrawingPresenter>();
                typeof(CanvasDrawingPresenter).GetField("brushMaterials", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(practicePresenters[slot], new Material[1]);
                practiceSurfaces[slot] = CreateObject(name + " practice surface " + slot, root.transform)
                    .AddComponent<CanvasSurface>();
                avatarRoots[slot] = CreateObject(name + " avatar root " + slot, root.transform);
                if (slot < avatarPresenters.Length)
                    avatarPresenters[slot] = CreateObject(name + " avatar presenter " + slot, root.transform)
                        .AddComponent<RemoteAvatarPresenter>();
            }
            port.Configure(root, spawns, practiceRoots, practicePresenters, practiceSurfaces,
                avatarRoots, avatarPresenters);
            Assert.That(port.ValidateBindings(out string error), Is.True, error);
            return new LobbyFixture(port, practiceRoots, practicePresenters, avatarRoots);
        }

        private FakeGamePort CreateGamePort(PartyMode mode)
        {
            GameObject root = CreateObject(mode + " scene root");
            GameObject writable = CreateObject(mode + " writable", root.transform);
            CanvasSurface surface = CreateObject(mode + " surface", writable.transform).AddComponent<CanvasSurface>();
            HandCanvasInteractable interactable = surface.gameObject.AddComponent<HandCanvasInteractable>();
            return new FakeGamePort(mode, new PartySceneBindings
            {
                Mode = mode,
                SceneRoot = root,
                WritablePaperRoot = writable,
                WritableSurface = surface,
                WritableInteractable = interactable,
                PhysicalPaintTool = CreateObject(mode + " paint tool", root.transform).AddComponent<PhysicalPaintTool>(),
                ResultRoot = CreateObject(mode + " result root", root.transform)
            });
        }

        private static object GetField(Component component, string name)
        {
            FieldInfo field = component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, component.GetType().Name + " must own " + name + ".");
            return field.GetValue(component);
        }

        private static void SetPrivate(Component component, string name, object value)
        {
            FieldInfo field = component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, component.GetType().Name + " must own " + name + ".");
            field.SetValue(component, value);
        }

        private static void InvokePrivate(Component component, string name, params object[] arguments)
        {
            MethodInfo method = component.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, component.GetType().Name + " must own " + name + ".");
            method.Invoke(component, arguments);
        }


        private static OnlineRelayQuizView ActiveCoopMural()
        {
            return new OnlineRelayQuizView
            {
                state = RelayQuizState.Setup,
                rosterCount = PartyRoster.Capacity,
                rosterLocked = true,
                localSlot = 0,
                roster = new[] { "local-owner", "p2", "p3", "p4" },
                connected = true,
                hasSelectedMode = true,
                selectedMode = PartyMode.CoopMural,
                modeStarted = true,
                startSignal = 1,
                sessionId = "coop-session",
                rosterGeneration = 1
            };
        }

        private sealed class FakeGateway : IPartyWorldGateway
        {
            internal readonly List<bool> ReadyChanges = new List<bool>();
            internal readonly List<PartyMode> SelectedModes = new List<PartyMode>();
            internal int StartCalls;
            internal int HostCalls;
            internal int ReturnCalls;
            internal bool Host;
            internal bool CameraConnected;
            internal bool FreshHand;
            internal string LocalPlayerId = "local";
            internal OnlineRelayQuizView View = new OnlineRelayQuizView { state = RelayQuizState.Setup, localSlot = 0 };

            public bool IsHost => Host;
            public bool IsCameraConnected => CameraConnected;
            public bool HasFreshHand => FreshHand;
            public string LocalIdentity => LocalPlayerId;
            public OnlineRelayQuizView PartyView => View;
            public void SetReady(bool ready) => ReadyChanges.Add(ready);
            public bool SelectMode(PartyMode mode) { SelectedModes.Add(mode); return true; }
            public bool StartSelectedMode()
            {
                StartCalls++;
                View.transitionPhase = PartyTransitionPhase.SelectingMode;
                return true;
            }
            public void RequestHost() { HostCalls++; }
            public void RequestInvite() { }
            public void RequestLeave() { }
            public void RequestCamera(PartyWorldAction action) { }
            public bool RequestReturnToLobby() { ReturnCalls++; return true; }
        }

        private sealed class FakeGamePort : IPartyGameScenePort
        {
            internal FakeGamePort(PartyMode mode, PartySceneBindings bindings)
            {
                Mode = mode;
                Bindings = bindings;
            }

            public PartyMode Mode { get; }
            public PartySceneBindings Bindings { get; }
            public bool IsRegistered { get; private set; }
            public bool ValidateBindings(out string error) { error = string.Empty; return true; }
            public bool Register(PartyMode expectedMode, PartyTransitionKey transitionKey, out string error)
            {
                IsRegistered = expectedMode == Mode;
                error = IsRegistered ? string.Empty : "mode mismatch";
                return IsRegistered;
            }
            public void Unregister() => IsRegistered = false;
        }

        private sealed class LobbyFixture
        {
            internal LobbyFixture(PartyLobbyScenePort port, GameObject[] practiceRoots,
                CanvasDrawingPresenter[] practicePresenters, GameObject[] avatarRoots)
            {
                Port = port;
                PracticeRoots = practiceRoots;
                PracticePresenters = practicePresenters;
                AvatarRoots = avatarRoots;
            }

            internal PartyLobbyScenePort Port { get; }
            internal GameObject[] PracticeRoots { get; }
            internal CanvasDrawingPresenter[] PracticePresenters { get; }
            internal GameObject[] AvatarRoots { get; }
        }


        private sealed class SessionGateway : IPartyWorldGateway
        {
            private readonly OnlineRelayQuizSession session;

            internal SessionGateway(OnlineRelayQuizSession session)
            {
                this.session = session;
            }

            public bool IsHost => true;
            public bool IsCameraConnected => true;
            public bool HasFreshHand => true;
            public string LocalIdentity => "p1";
            public OnlineRelayQuizView PartyView => session.View;
            public void SetReady(bool ready) => session.SetReady(ready);
            public bool SelectMode(PartyMode mode) => session.SelectModeAndBeginLoad(mode);
            public bool StartSelectedMode() => session.OpenModeSelector();
            public void RequestHost() { }
            public void RequestInvite() { }
            public void RequestLeave() { }
            public void RequestCamera(PartyWorldAction action) { }
            public bool RequestReturnToLobby() => session.RequestReturnToLobby();
        }

        private sealed class OnlinePartyFixture : IDisposable
        {
            private const string HostId = "p1";
            private readonly List<LoopbackTransport> clientTransports = new List<LoopbackTransport>();
            private readonly List<OnlineRelayQuizSession> clients = new List<OnlineRelayQuizSession>();
            private readonly List<LoopbackTransport.FakePeer> hostWires = new List<LoopbackTransport.FakePeer>();
            private readonly List<LoopbackTransport.FakePeer> clientWires = new List<LoopbackTransport.FakePeer>();
            private readonly int[] sentToHost = new int[PartyRoster.Capacity];
            private readonly int[] sentToClient = new int[PartyRoster.Capacity];

            internal readonly TrackingTransport HostTransport;
            internal readonly OnlineRelayQuizSession Host;
            internal IReadOnlyList<LoopbackTransport> ClientTransports => clientTransports;

            internal OnlinePartyFixture()
            {
                HostTransport = new TrackingTransport(true, HostId);
                Host = NewSession(HostTransport, HostId);
                for (int slot = 1; slot < PartyRoster.Capacity; slot++)
                {
                    string id = "p" + (slot + 1);
                    var transport = new LoopbackTransport(false, id);
                    clientTransports.Add(transport);
                    clients.Add(NewSession(transport, id));
                    hostWires.Add(HostTransport.AddFakePeer(id, id));
                    clientWires.Add(transport.AddFakePeer(HostId, HostId));
                }
                Pump();
                Host.UpdateLocalConditions(true, true);
                foreach (OnlineRelayQuizSession client in clients) client.UpdateLocalConditions(true, true);
                Host.SetReady(true);
                foreach (OnlineRelayQuizSession client in clients) client.SetReady(true);
                Pump();
                if (!Host.OpenModeSelector())
                    throw new InvalidOperationException("Host could not open the mode selector in the ready four-player fixture.");
                Pump();
                if (!Host.SelectModeAndBeginLoad(PartyMode.CoopMural))
                    throw new InvalidOperationException("Host could not select CoopMural in the locked four-player fixture.");
                Pump();
                int transition = Host.View.transitionGeneration;
                if (!Host.MarkLocalSceneReady(transition))
                    throw new InvalidOperationException("Host could not acknowledge the CoopMural Scene.");
                foreach (OnlineRelayQuizSession client in clients)
                    if (!client.MarkLocalSceneReady(transition))
                        throw new InvalidOperationException("Client could not acknowledge the CoopMural Scene.");
                Pump();
            }

            public void Dispose()
            {
                foreach (OnlineRelayQuizSession client in clients) client.Dispose();
                Host.Dispose();
            }

            private static OnlineRelayQuizSession NewSession(INetTransport transport, string identity)
            {
                return new OnlineRelayQuizSession(transport, HostId, () => "camera",
                    () => new CanvasDrawingData(), () => identity, 3);
            }

            internal void Pump(int cycles = 24)
            {
                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    for (int slot = 1; slot < PartyRoster.Capacity; slot++)
                    {
                        LoopbackTransport transport = clientTransports[slot - 1];
                        while (sentToHost[slot] < transport.SentToHost.Count)
                            hostWires[slot - 1].Send(transport.SentToHost[sentToHost[slot]++]);
                    }
                    for (int slot = 1; slot < PartyRoster.Capacity; slot++)
                    {
                        LoopbackTransport.FakePeer wire = hostWires[slot - 1];
                        while (sentToClient[slot] < wire.Received.Count)
                            clientWires[slot - 1].Send(wire.Received[sentToClient[slot]++]);
                    }
                    Host.Tick(0f);
                    foreach (OnlineRelayQuizSession client in clients) client.Tick(0f);
                }
            }
        }

        private sealed class TrackingTransport : INetTransport
        {
            private readonly LoopbackTransport inner;

            internal TrackingTransport(bool isHost, string localPlayerId)
            {
                inner = new LoopbackTransport(isHost, localPlayerId);
            }

            internal int ShutdownCalls { get; private set; }
            public bool IsHost => inner.IsHost;
            public string LocalPlayerId => inner.LocalPlayerId;
            public event Action<string> OnPeerConnected
            {
                add => inner.OnPeerConnected += value;
                remove => inner.OnPeerConnected -= value;
            }
            public event Action<string> OnPeerDisconnected
            {
                add => inner.OnPeerDisconnected += value;
                remove => inner.OnPeerDisconnected -= value;
            }
            public event Action<string, byte[]> OnMessage
            {
                add => inner.OnMessage += value;
                remove => inner.OnMessage -= value;
            }

            internal LoopbackTransport.FakePeer AddFakePeer(string id, string name) => inner.AddFakePeer(id, name);
            public void SendToHost(byte[] data, bool reliable) => inner.SendToHost(data, reliable);
            public void SendTo(string playerId, byte[] data, bool reliable) => inner.SendTo(playerId, data, reliable);
            public void Tick() => inner.Tick();
            public void Shutdown()
            {
                ShutdownCalls++;
                inner.Shutdown();
            }
        }
    }
}
