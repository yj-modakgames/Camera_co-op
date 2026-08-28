using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using CameraCoop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CameraCoop.Tests
{
    // docs/11 §2 — WASD 이동 델타, 방 경계 clamp, pitch clamp
    public class PlayerMoveTests
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> createdObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            InputFocus.IsTyping = false;
        }

        [TearDown]
        public void TearDown()
        {
            InputFocus.IsTyping = false;
            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(createdObjects[i]);
            }
            createdObjects.Clear();
        }

        [Test]
        public void Step_YawZero_ForwardIsPlusZ()
        {
            Vector3 delta = PlayerMoveLogic.Step(new Vector2(0f, 1f), yawDegrees: 0f, speed: 3f, deltaTime: 0.5f);
            Assert.AreEqual(0f, delta.x, 1e-4f);
            Assert.AreEqual(0f, delta.y, 1e-4f);
            Assert.AreEqual(1.5f, delta.z, 1e-4f); // 3 m/s * 0.5s
        }

        [Test]
        public void Step_Yaw90_ForwardIsPlusX()
        {
            Vector3 delta = PlayerMoveLogic.Step(new Vector2(0f, 1f), yawDegrees: 90f, speed: 2f, deltaTime: 1f);
            Assert.AreEqual(2f, delta.x, 1e-4f);
            Assert.AreEqual(0f, delta.z, 1e-4f);
        }

        [Test]
        public void Step_Strafe_YawZero_IsPlusX()
        {
            Vector3 delta = PlayerMoveLogic.Step(new Vector2(1f, 0f), yawDegrees: 0f, speed: 1f, deltaTime: 1f);
            Assert.AreEqual(1f, delta.x, 1e-4f);
            Assert.AreEqual(0f, delta.z, 1e-4f);
        }

        [Test]
        public void Step_Diagonal_IsNotFasterThanStraight()
        {
            // 대각선 (1,1)이 √2배 빨라지면 안 된다 — 정규화 누락 회귀 방지
            Vector3 diagonal = PlayerMoveLogic.Step(new Vector2(1f, 1f), yawDegrees: 0f, speed: 3f, deltaTime: 1f);
            Assert.LessOrEqual(diagonal.magnitude, 3f + 1e-4f);
            Assert.AreEqual(3f, diagonal.magnitude, 1e-3f); // 방향 유지 + 속도 보존
        }

        [Test]
        public void Step_NoInput_IsZero()
        {
            Vector3 delta = PlayerMoveLogic.Step(Vector2.zero, yawDegrees: 45f, speed: 5f, deltaTime: 1f);
            Assert.AreEqual(0f, delta.magnitude, 1e-5f);
        }

        [Test]
        public void ClampToRoom_InsideIsUnchanged()
        {
            var min = new Vector2(-5.5f, -8.75f);
            var max = new Vector2(5.5f, 0.75f);
            Vector3 pos = PlayerMoveLogic.ClampToRoom(new Vector3(1f, 1.5f, -3f), min, max);
            Assert.AreEqual(1f, pos.x, 1e-4f);
            Assert.AreEqual(-3f, pos.z, 1e-4f);
        }

        [Test]
        public void ClampToRoom_ClampsXZ_AndKeepsY()
        {
            var min = new Vector2(-5.5f, -8.75f);
            var max = new Vector2(5.5f, 0.75f);
            Vector3 high = PlayerMoveLogic.ClampToRoom(new Vector3(9f, 1.23f, 4f), min, max);
            Assert.AreEqual(5.5f, high.x, 1e-4f);
            Assert.AreEqual(0.75f, high.z, 1e-4f);
            Assert.AreEqual(1.23f, high.y, 1e-4f); // y는 건드리지 않는다

            Vector3 low = PlayerMoveLogic.ClampToRoom(new Vector3(-9f, 0f, -20f), min, max);
            Assert.AreEqual(-5.5f, low.x, 1e-4f);
            Assert.AreEqual(-8.75f, low.z, 1e-4f);
        }

        [Test]
        public void ClampPitch_LimitsBothDirections()
        {
            Assert.AreEqual(80f, PlayerMoveLogic.ClampPitch(120f, 80f), 1e-4f);
            Assert.AreEqual(-80f, PlayerMoveLogic.ClampPitch(-120f, 80f), 1e-4f);
            Assert.AreEqual(12.5f, PlayerMoveLogic.ClampPitch(12.5f, 80f), 1e-4f);
        }

        [Test]
        public void Controller_DefaultProfilePreservesLegacyClampedStep()
        {
            PlayerController player = CreateLegacyPlayer(out _);
            FieldInfo profile = typeof(PlayerController).GetField("controlProfile", InstanceFlags);
            Assert.IsNotNull(profile, "PlayerController must serialize a Legacy-default controlProfile.");
            Assert.AreEqual(PlayerControlProfile.Legacy, (PlayerControlProfile)profile.GetValue(player));
            Assert.AreEqual(0, (int)(PlayerControlProfile)profile.GetValue(player), "Existing scenes must deserialize as Legacy.");
            player.transform.position = new Vector3(0f, 1.23f, 0f);

            player.Step(Vector2.one, 100f);

            Assert.AreEqual(5.5f, player.transform.position.x, 1e-4f);
            Assert.AreEqual(0.65f, player.transform.position.z, 1e-4f);
            Assert.AreEqual(1.23f, player.transform.position.y, 1e-4f);
        }

        [Test]
        public void Controller_LegacyDirectStepKeepsExistingTypingBehavior()
        {
            PlayerController player = CreateLegacyPlayer(out _);
            player.transform.position = new Vector3(0f, 1.23f, 0f);
            InputFocus.IsTyping = true;

            player.Step(Vector2.right, 0.25f);

            Assert.AreEqual(0.75f, player.transform.position.x, 1e-4f);
            Assert.AreEqual(1.23f, player.transform.position.y, 1e-4f);
        }

        [Test]
        public void Controller_LegacyLookStillRunsWhileTyping()
        {
            PlayerController player = CreateLegacyPlayer(out Transform camera);
            InputFocus.IsTyping = true;

            player.ApplyLookDelta(new Vector2(10f, -10f));

            Assert.AreEqual(1.2f, player.transform.eulerAngles.y, 1e-4f);
            Assert.AreEqual(1.2f, camera.localEulerAngles.x, 1e-4f);
        }

        [TestCase(0.1f, 0.3f)]
        [TestCase(0.25f, 0.75f)]
        public void ModalStep_UsesRigYawAndDeltaTimeWithoutLegacyRoomClamp(float deltaTime, float distance)
        {
            PlayerController player = CreateModalPlayer(out _, out _);
            player.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Physics.SyncTransforms();
            Vector3 start = player.transform.position;

            player.Step(Vector2.up, deltaTime);

            Assert.AreEqual(start.x + distance, player.transform.position.x, 0.002f);
            Assert.AreEqual(start.z, player.transform.position.z, 0.002f);
        }

        [Test]
        public void ModalStep_DiagonalHasSameHorizontalSpeedAsStraight()
        {
            PlayerController straight = CreateModalPlayer(out _, out _);
            PlayerController diagonal = CreateModalPlayer(out _, out _);
            diagonal.transform.position += Vector3.right * 10f;
            Physics.SyncTransforms();
            Vector3 straightStart = straight.transform.position;
            Vector3 diagonalStart = diagonal.transform.position;

            straight.Step(Vector2.up, 0.25f);
            diagonal.Step(Vector2.one, 0.25f);

            Vector3 straightDelta = straight.transform.position - straightStart;
            Vector3 diagonalDelta = diagonal.transform.position - diagonalStart;
            Assert.AreEqual(0.75f, new Vector2(straightDelta.x, straightDelta.z).magnitude, 0.002f);
            Assert.AreEqual(0.75f, new Vector2(diagonalDelta.x, diagonalDelta.z).magnitude, 0.002f);
        }

        [Test]
        public void ModalStep_CharacterControllerStopsAtWall()
        {
            PlayerController player = CreateModalPlayer(out _, out _);
            Vector3 start = player.transform.position;
            GameObject wall = CreateObject("modal wall");
            wall.transform.position = start + Vector3.forward;
            wall.transform.localScale = new Vector3(5f, 100f, 0.2f);
            wall.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            player.Step(Vector2.up, 0.5f);

            Assert.Greater(player.transform.position.z, start.z + 0.1f);
            Assert.Less(player.transform.position.z, start.z + 0.9f, "Horizontal motion must go through CharacterController collision.");
        }

        [Test]
        public void ModalStep_ZeroHorizontalInputStillAccumulatesGravity()
        {
            PlayerController player = CreateModalPlayer(out _, out _);
            Vector3 start = player.transform.position;

            player.Step(Vector2.zero, 0.1f);
            Assert.AreEqual(start.y - 0.2f, player.transform.position.y, 0.002f);
            player.Step(Vector2.zero, 0.1f);
            Assert.AreEqual(start.y - 0.6f, player.transform.position.y, 0.002f);
            Assert.AreEqual(start.x, player.transform.position.x, 0.002f);
            Assert.AreEqual(start.z, player.transform.position.z, 0.002f);
        }

        [Test]
        public void ModalStep_CharacterControllerKeepsPlayerAboveFloor()
        {
            PlayerController player = CreateModalPlayer(out _, out _);
            Vector3 start = player.transform.position;
            GameObject floor = CreateObject("modal floor");
            floor.transform.position = start - Vector3.up * 0.1f;
            floor.transform.localScale = new Vector3(20f, 0.2f, 20f);
            floor.AddComponent<BoxCollider>();
            Physics.SyncTransforms();

            for (int i = 0; i < 20; i++)
            {
                player.Step(Vector2.zero, 0.05f);
            }

            Assert.GreaterOrEqual(player.transform.position.y, start.y - 0.05f);
            Assert.LessOrEqual(player.transform.position.y, start.y + 0.1f);
        }

        [TestCase(InputContext.Explore, InputMode.Interact, false, true, false)]
        [TestCase(InputContext.UiOnly, InputMode.Interact, false, true, false)]
        [TestCase(InputContext.Drawing, InputMode.Interact, false, true, false)]
        [TestCase(InputContext.Blocked, InputMode.Interact, false, true, false)]
        [TestCase(InputContext.Explore, InputMode.Move, true, true, false)]
        [TestCase(InputContext.Explore, InputMode.Move, false, false, false)]
        [TestCase(InputContext.Explore, InputMode.Move, false, true, true)]
        public void ModalEntryPoints_BlockedPermissionStopsMotionAndLookAndClearsGravity(
            InputContext context, InputMode mode, bool typing, bool focused, bool cursorUnlocked)
        {
            PlayerController player = CreateModalPlayer(out InputModeManager manager, out Transform camera);
            player.Step(Vector2.zero, 0.1f);
            manager.SetContext(context);
            if (context == InputContext.Explore)
            {
                Assert.IsTrue(manager.RequestMode(mode));
            }
            InputFocus.IsTyping = typing;
            InvokeCallback(manager, "OnApplicationFocus", focused);
            if (cursorUnlocked)
            {
                manager.ProcessInput(false, CursorLockMode.None);
            }
            Vector3 stoppedPosition = player.transform.position;
            Quaternion stoppedYaw = player.transform.rotation;
            Quaternion stoppedPitch = camera.localRotation;

            player.Step(Vector2.one, 1f);
            player.ApplyLookDelta(new Vector2(100f, 100f));

            Assert.AreEqual(stoppedPosition, player.transform.position, "The public Step path must not bypass mode permission.");
            Assert.AreEqual(stoppedYaw, player.transform.rotation);
            Assert.AreEqual(stoppedPitch, camera.localRotation);

            InputFocus.IsTyping = false;
            InvokeCallback(manager, "OnApplicationFocus", true);
            manager.SetContext(InputContext.Explore);
            Assert.IsTrue(manager.RequestMode(InputMode.Move));
            player.Step(Vector2.zero, 0.1f);
            Assert.AreEqual(stoppedPosition.y - 0.2f, player.transform.position.y, 0.002f,
                "Resuming Move must not reuse the falling velocity from before the permission boundary.");
        }

        [Test]
        public void ModalLookDelta_UsesPixelsWithoutDeltaTimeAndClampsOnlyCameraPitch()
        {
            PlayerController player = CreateModalPlayer(out _, out Transform camera);
            player.transform.rotation = Quaternion.Euler(0f, 30f, 0f);

            player.ApplyLookDelta(new Vector2(10f, -10f));

            Assert.AreEqual(31.2f, player.transform.eulerAngles.y, 0.001f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, player.transform.eulerAngles.x), 0.001f);
            Assert.AreEqual(1.2f, camera.localEulerAngles.x, 0.001f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, camera.localEulerAngles.y), 0.001f);

            player.ApplyLookDelta(new Vector2(0f, 2000f));
            Assert.AreEqual(-80f, Mathf.DeltaAngle(0f, camera.localEulerAngles.x), 0.001f);
            player.ApplyLookDelta(new Vector2(0f, -2000f));
            Assert.AreEqual(80f, Mathf.DeltaAngle(0f, camera.localEulerAngles.x), 0.001f);
        }

        [TestCase("playerCamera")]
        [TestCase("characterController")]
        [TestCase("inputModeManager")]
        public void ModalReferences_MissingExplicitReferenceLogsOnceAndDisablesLocalInput(string missingReference)
        {
            PlayerController player = CreateModalPlayer(out _, out Transform camera, initialize: false);
            SetField(player, missingReference, null);
            Vector3 start = player.transform.position;
            LogAssert.Expect(LogType.Error, new Regex("PlayerController.*ModalFirstPerson.*" + missingReference));

            InvokeCallback(player, "Awake");

            Assert.IsFalse(player.enabled);
            player.Step(Vector2.one, 1f);
            player.ApplyLookDelta(new Vector2(100f, 100f));
            Assert.AreEqual(start, player.transform.position);
            Assert.AreEqual(Quaternion.identity, player.transform.rotation);
            Assert.AreEqual(Quaternion.identity, camera.localRotation);
            LogAssert.NoUnexpectedReceived();
        }

        private PlayerController CreateLegacyPlayer(out Transform camera)
        {
            GameObject rig = CreateObject("legacy player test");
            rig.SetActive(false);
            camera = new GameObject("player camera").transform;
            camera.SetParent(rig.transform, false);
            var player = rig.AddComponent<PlayerController>();
            SetField(player, "playerCamera", camera);
            return player;
        }

        private PlayerController CreateModalPlayer(out InputModeManager manager, out Transform camera, bool initialize = true)
        {
            PlayerController player = CreateLegacyPlayer(out camera);
            player.transform.position = new Vector3(1000f, 1000f, 1000f);
            manager = player.gameObject.AddComponent<InputModeManager>();
            InvokeCallback(manager, "Awake");
            var characterController = player.gameObject.AddComponent<CharacterController>();
            characterController.height = 1.8f;
            characterController.radius = 0.3f;
            characterController.center = new Vector3(0f, 0.9f, 0f);
            SetField(player, "controlProfile", PlayerControlProfile.ModalFirstPerson);
            SetField(player, "characterController", characterController);
            SetField(player, "inputModeManager", manager);
            if (initialize)
            {
                InvokeCallback(player, "Awake");
                player.gameObject.SetActive(true);
                Physics.SyncTransforms();
            }
            return player;
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
            Assert.IsNotNull(field, component.GetType().Name + " must serialize " + name + ".");
            field.SetValue(component, value);
        }

        private static void InvokeCallback(Component component, string name, params object[] arguments)
        {
            MethodInfo method = component.GetType().GetMethod(name, InstanceFlags);
            Assert.IsNotNull(method, component.GetType().Name + " must implement " + name + ".");
            method.Invoke(component, arguments);
        }

    }
}
