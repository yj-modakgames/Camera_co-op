using System.Collections.Generic;
using System.Reflection;
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
