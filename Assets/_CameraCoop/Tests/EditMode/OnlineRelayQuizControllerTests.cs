using System.Collections.Generic;
using System.Reflection;
using CameraCoop.Party;
using CameraCoop.Party.SceneFlow;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CameraCoop.Tests
{
    public sealed class OnlineRelayQuizControllerTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--)
                Object.DestroyImmediate(createdObjects[index]);
            createdObjects.Clear();
        }

        [Test]
        public void CoordinatorCallbacksRouteOnlyTheCurrentlyBoundGamePort()
        {
            OnlineRelayQuizController controller = CreateObject("persistent online controller")
                .AddComponent<OnlineRelayQuizController>();
            PartyWorldController world = CreateObject("persistent party world").AddComponent<PartyWorldController>();
            SetField(controller, "partyWorldController", world);
            var first = new FakeGamePort(PartyMode.RelayCopy, CreateObject("relay port root"));
            var second = new FakeGamePort(PartyMode.MemoryCopy, CreateObject("memory port root"));

            var callbacks = (IPartySceneCoordinatorCallbacks)controller;
            callbacks.BindGameScene(first);
            callbacks.UnbindGameScene(first);
            callbacks.BindGameScene(second);

            Assert.That(world.HasBoundScenePort, Is.True);
            Assert.That(world.ActiveSceneMode, Is.EqualTo(PartyMode.MemoryCopy));
            Assert.That(first.Bindings.WritablePaperRoot.activeSelf, Is.False);
        }

        [Test]
        public void GalleryWithoutGameSceneBindingIsAValidDeferredState()
        {
            RelayQuizGallery gallery = CreateObject("deferred relay gallery").AddComponent<RelayQuizGallery>();

            Assert.That(gallery.ValidateRuntimeConfiguration(out string error), Is.True, error);
            Assert.That(gallery.IsReady, Is.False);
            Assert.That(gallery.SlotCount, Is.Zero);
        }

        [Test]
        public void BindingPrivateGameSceneConfiguresExactlyThreeGallerySlots()
        {
            OnlineRelayQuizController controller = CreateObject("gallery binding controller")
                .AddComponent<OnlineRelayQuizController>();
            RelayQuizGallery gallery = CreateObject("persistent gallery").AddComponent<RelayQuizGallery>();
            SetField(controller, "relayQuizGallery", gallery);
            var port = new FakeGamePort(PartyMode.RelayCopy, CreateObject("relay gallery port"));

            ((IPartySceneCoordinatorCallbacks)controller).BindGameScene(port);

            Assert.That(gallery.IsReady, Is.True);
            Assert.That(gallery.SlotCount, Is.EqualTo(PartyRoster.Capacity - 1));
            Assert.That(port.Bindings.ResultRoot.activeSelf, Is.False);
        }

        [Test]
        public void GalleryStateShowsResultRootAndUsesBoundResultViewPose()
        {
            OnlineRelayQuizController controller = CreateController(out PlayerController player,
                out _, out _, out _);
            RelayQuizGallery gallery = CreateObject("persistent result gallery").AddComponent<RelayQuizGallery>();
            SetField(controller, "relayQuizGallery", gallery);
            var port = new FakeGamePort(PartyMode.MemoryCopy, CreateObject("memory gallery port"));
            ((IPartySceneCoordinatorCallbacks)controller).BindGameScene(port);
            var view = new OnlineRelayQuizView { state = RelayQuizState.Gallery };

            controller.ApplyGalleryForTests(view, true);
            controller.ApplyNavigationForTests(view, false, false, false,
                false, RelayQuizPauseStage.None, false, true);

            Assert.That(port.Bindings.ResultRoot.activeSelf, Is.True);
            Assert.That(player.transform.position, Is.EqualTo(port.Bindings.ResultViewPose.position));
            Assert.That(player.transform.eulerAngles.y,
                Is.EqualTo(port.Bindings.ResultViewPose.eulerAngles.y).Within(0.01f));
        }

        [Test]
        public void LeavingGalleryAndUnbindingPrivateSceneHideAndReleaseResults()
        {
            OnlineRelayQuizController controller = CreateObject("gallery release controller")
                .AddComponent<OnlineRelayQuizController>();
            RelayQuizGallery gallery = CreateObject("releasable gallery").AddComponent<RelayQuizGallery>();
            SetField(controller, "relayQuizGallery", gallery);
            var port = new FakeGamePort(PartyMode.RelayCopy, CreateObject("release gallery port"));
            var callbacks = (IPartySceneCoordinatorCallbacks)controller;
            callbacks.BindGameScene(port);
            controller.ApplyGalleryForTests(new OnlineRelayQuizView { state = RelayQuizState.Gallery }, true);

            controller.ApplyGalleryForTests(new OnlineRelayQuizView { state = RelayQuizState.Setup }, true);
            Assert.That(port.Bindings.ResultRoot.activeSelf, Is.False);

            callbacks.UnbindGameScene(port);
            Assert.That(gallery.IsReady, Is.False);
            Assert.That(gallery.SlotCount, Is.Zero);
            Assert.That(port.Bindings.ResultRoot.activeSelf, Is.False);
        }

        [Test]
        public void CoopFinalResultRootRemainsVisibleAfterPrivateGallerySync()
        {
            OnlineRelayQuizController controller = CreateObject("coop gallery isolation controller")
                .AddComponent<OnlineRelayQuizController>();
            RelayQuizGallery gallery = CreateObject("deferred coop gallery").AddComponent<RelayQuizGallery>();
            SetField(controller, "relayQuizGallery", gallery);
            var port = new FakeGamePort(PartyMode.CoopMural, CreateObject("coop result port"));
            ((IPartySceneCoordinatorCallbacks)controller).BindGameScene(port);

            port.Bindings.ResultRoot.SetActive(true);
            controller.ApplyGalleryForTests(new OnlineRelayQuizView { state = RelayQuizState.Setup }, true);

            Assert.That(port.Bindings.ResultRoot.activeSelf, Is.True,
                "PartyWorldController owns the Coop final display; private gallery LateUpdate must not hide it.");
            Assert.That(gallery.IsReady, Is.False);
        }

        [Test]
        public void AutoReadyHandover_RequiresOwnerPreparationAndRunsOncePerGeneration()
        {
            var view = new OnlineRelayQuizView
            {
                state = RelayQuizState.Handover,
                active = true,
                generation = 7
            };

            Assert.That(OnlineRelayQuizController.ShouldAutoReadyHandover(view, true, true, true, -1), Is.True);
            Assert.That(OnlineRelayQuizController.ShouldAutoReadyHandover(view, true, true, true, 7), Is.False);
            Assert.That(OnlineRelayQuizController.ShouldAutoReadyHandover(view, false, true, true, -1), Is.False);
            Assert.That(OnlineRelayQuizController.ShouldAutoReadyHandover(view, true, false, true, -1), Is.False);
            view.active = false;
            Assert.That(OnlineRelayQuizController.ShouldAutoReadyHandover(view, true, true, true, -1), Is.False);
        }

        [Test]
        public void Controller_DoesNotSerializeObsoleteWorkPose()
        {
            FieldInfo legacyWorkPose = typeof(OnlineRelayQuizController).GetField("workPose", InstanceFlags);

            Assert.That(legacyWorkPose, Is.Null);
        }

        [Test]
        public void SessionNullSetup_EntersMovableLobbyAtDedicatedPose()
        {
            OnlineRelayQuizController controller = CreateController(out PlayerController player,
                out InputModeManager modes, out Transform lobbyPose, out _);
            player.transform.position = new Vector3(40f, 2f, 40f);
            modes.SetContext(InputContext.UiOnly);

            controller.ApplyNavigationForTests(new OnlineRelayQuizView(), true, false, true,
                false, RelayQuizPauseStage.None, false, true);

            Assert.That(player.transform.position, Is.EqualTo(lobbyPose.position));
            Assert.That(modes.CurrentContext, Is.EqualTo(InputContext.Explore));
            Assert.That(modes.CurrentMode, Is.EqualTo(InputMode.Move));
            Assert.That(modes.CanMove, Is.True);
        }

        [Test]
        public void LockedSetup_PreservesPartyWorldSlotPlacementAndEnablesMovement()
        {
            OnlineRelayQuizController controller = CreateController(out PlayerController player,
                out InputModeManager modes, out _, out _);
            Vector3 assignedSlot = new Vector3(12f, 2f, -6f);
            player.transform.position = assignedSlot;
            modes.SetContext(InputContext.UiOnly);
            var view = new OnlineRelayQuizView { rosterLocked = true, localSlot = 2 };

            controller.ApplyNavigationForTests(view, false, true, false,
                false, RelayQuizPauseStage.None, false, true);

            Assert.That(player.transform.position, Is.EqualTo(assignedSlot));
            Assert.That(modes.CurrentContext, Is.EqualTo(InputContext.Explore));
            Assert.That(modes.CurrentMode, Is.EqualTo(InputMode.Move));
        }

        [Test]
        public void SetupRevision_DoesNotTeleportOrOverridePlayersInteractChoice()
        {
            OnlineRelayQuizController controller = CreateController(out PlayerController player,
                out InputModeManager modes, out _, out _);
            var view = new OnlineRelayQuizView();
            controller.ApplyNavigationForTests(view, false, true, false,
                false, RelayQuizPauseStage.None, false, true);
            Assert.That(modes.RequestMode(InputMode.Interact), Is.True);
            Vector3 walkedPosition = new Vector3(4f, 2f, -3f);
            player.transform.position = walkedPosition;

            controller.ApplyNavigationForTests(view, false, false, false,
                false, RelayQuizPauseStage.None, false, true);

            Assert.That(player.transform.position, Is.EqualTo(walkedPosition));
            Assert.That(modes.CurrentMode, Is.EqualTo(InputMode.Interact));
        }

        [Test]
        public void SetupRevision_PreservesMoveModeAndMovementPermission()
        {
            OnlineRelayQuizController controller = CreateController(out _, out InputModeManager modes, out _, out _);
            var view = new OnlineRelayQuizView();
            controller.ApplyNavigationForTests(view, false, true, false,
                false, RelayQuizPauseStage.None, false, true);

            controller.ApplyNavigationForTests(view, false, false, false,
                false, RelayQuizPauseStage.None, false, true);

            Assert.That(modes.CurrentContext, Is.EqualTo(InputContext.Explore));
            Assert.That(modes.CurrentMode, Is.EqualTo(InputMode.Move));
            Assert.That(modes.CanMove, Is.True);
        }

        [Test]
        public void Handover_DoesNotMovePlayerToLegacySharedWorkPose()
        {
            OnlineRelayQuizController controller = CreateController(out PlayerController player,
                out InputModeManager modes, out _, out _);
            Vector3 assignedSlot = new Vector3(-8f, 2f, 5f);
            player.transform.position = assignedSlot;
            var view = new OnlineRelayQuizView { state = RelayQuizState.Handover, rosterLocked = true, localSlot = 1 };

            controller.ApplyNavigationForTests(view, false, false, false,
                false, RelayQuizPauseStage.None, false, true);

            Assert.That(player.transform.position, Is.EqualTo(assignedSlot));
            Assert.That(modes.CurrentContext, Is.EqualTo(InputContext.UiOnly));
        }

        [Test]
        public void Gallery_StillPlacesAtGalleryPoseAndUsesMoveMode()
        {
            OnlineRelayQuizController controller = CreateController(out PlayerController player,
                out InputModeManager modes, out _, out Transform galleryPose);
            player.transform.position = Vector3.zero;
            var view = new OnlineRelayQuizView { state = RelayQuizState.Gallery };

            controller.ApplyNavigationForTests(view, false, false, false,
                false, RelayQuizPauseStage.None, false, true);

            Assert.That(player.transform.position, Is.EqualTo(galleryPose.position));
            Assert.That(modes.CurrentContext, Is.EqualTo(InputContext.Explore));
            Assert.That(modes.CurrentMode, Is.EqualTo(InputMode.Move));
        }

        private OnlineRelayQuizController CreateController(out PlayerController player, out InputModeManager modes,
            out Transform lobbyPose, out Transform galleryPose)
        {
            GameObject rig = CreateObject("online navigation player");
            rig.SetActive(false);
            Transform playerCamera = CreateObject("online navigation camera").transform;
            playerCamera.SetParent(rig.transform, false);
            modes = rig.AddComponent<InputModeManager>();
            Invoke(modes, "Awake");
            CharacterController character = rig.AddComponent<CharacterController>();
            player = rig.AddComponent<PlayerController>();
            SetField(player, "controlProfile", PlayerControlProfile.ModalFirstPerson);
            SetField(player, "playerCamera", playerCamera);
            SetField(player, "characterController", character);
            SetField(player, "inputModeManager", modes);
            Invoke(player, "Awake");
            rig.SetActive(true);

            lobbyPose = CreateObject("dedicated lobby pose").transform;
            lobbyPose.SetPositionAndRotation(new Vector3(2f, 3f, -4f), Quaternion.Euler(0f, 35f, 0f));
            galleryPose = CreateObject("gallery pose").transform;
            galleryPose.SetPositionAndRotation(new Vector3(-5f, 2f, 7f), Quaternion.Euler(0f, 180f, 0f));
            OnlineRelayQuizController controller = CreateObject("online navigation controller")
                .AddComponent<OnlineRelayQuizController>();
            SetField(controller, "inputModeManager", modes);
            SetField(controller, "playerController", player);
            SetField(controller, "lobbyPose", lobbyPose);
            SetField(controller, "galleryPose", galleryPose);
            return controller;
        }

        private sealed class FakeGamePort : IPartyGameScenePort
        {
            internal FakeGamePort(PartyMode mode, GameObject root)
            {
                Mode = mode;
                GameObject writableRoot = Child("Writable root", root.transform);
                CanvasSurface surface = writableRoot.AddComponent<CanvasSurface>();
                Bindings = new PartySceneBindings
                {
                    Mode = mode,
                    SceneRoot = root,
                    WritablePaperRoot = writableRoot,
                    WritableSurface = surface,
                    WritableInteractable = writableRoot.AddComponent<HandCanvasInteractable>()
                };
                Bindings.ResultRoot = Child("Result root", root.transform);
                Bindings.ResultRoot.SetActive(false);
                if (mode != PartyMode.CoopMural)
                {
                    Bindings.ResultViewPose = Child("Result view pose", root.transform).transform;
                    Bindings.ResultViewPose.SetPositionAndRotation(new Vector3(6f, 2f, -2f),
                        Quaternion.Euler(0f, 25f, 0f));
                    Bindings.GalleryRoots = new GameObject[PartyRoster.Capacity - 1];
                    Bindings.GalleryPresenters = new CanvasDrawingPresenter[PartyRoster.Capacity - 1];
                    Bindings.GallerySurfaces = new CanvasSurface[PartyRoster.Capacity - 1];
                    for (int slot = 0; slot < Bindings.GalleryRoots.Length; slot++)
                    {
                        GameObject galleryRoot = Child("Gallery slot " + slot, Bindings.ResultRoot.transform);
                        GameObject surfaceRoot = Child("Gallery surface " + slot, galleryRoot.transform);
                        Bindings.GalleryRoots[slot] = galleryRoot;
                        Bindings.GalleryPresenters[slot] = galleryRoot.AddComponent<CanvasDrawingPresenter>();
                        Bindings.GallerySurfaces[slot] = surfaceRoot.AddComponent<CanvasSurface>();
                    }
                }
            }

            private static GameObject Child(string name, Transform parent)
            {
                var child = new GameObject(name);
                child.transform.SetParent(parent, false);
                return child;
            }

            public PartyMode Mode { get; }
            public PartySceneBindings Bindings { get; }
            public bool IsRegistered => true;
            public bool ValidateBindings(out string error) { error = string.Empty; return true; }
            public bool Register(PartyMode expectedMode, PartyTransitionKey transitionKey, out string error)
            { error = string.Empty; return expectedMode == Mode; }
            public void Unregister() { }
        }

        private GameObject CreateObject(string name)
        {
            var created = new GameObject(name);
            createdObjects.Add(created);
            return created;
        }

        private static void SetField(Component component, string name, object value)
        {
            FieldInfo field = component.GetType().GetField(name, InstanceFlags);
            Assert.That(field, Is.Not.Null, component.GetType().Name + " must serialize " + name + ".");
            field.SetValue(component, value);
        }

        private static void Invoke(Component component, string method)
        {
            MethodInfo callback = component.GetType().GetMethod(method, InstanceFlags);
            Assert.That(callback, Is.Not.Null);
            callback.Invoke(component, null);
        }
    }
}
