using System.Collections.Generic;
using System.Reflection;
using CameraCoop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CameraCoop.Tests
{
    public class PhysicalPaintToolTests
    {
        private sealed class PositionProbe : HandInteractable
        {
            public bool world;
            public Vector3 LastPress;
            public override bool UsesWorldHitPosition => world;
            public override void Press(HandInputSample sample, Vector3 hitPosition, HandClickContext context) { LastPress = hitPosition; }
        }
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < spawned.Count; i++)
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            spawned.Clear();
        }

        private GameObject New(string name)
        {
            var go = new GameObject(name);
            spawned.Add(go);
            return go;
        }

        private PhysicalPaintTool Make(out PhysicalBrush brush, out ToolState state)
        {
            state = New("ToolState").AddComponent<ToolState>();
            var manager = New("PhysicalPaintTool").AddComponent<PhysicalPaintTool>();
            brush = New("Brush").AddComponent<PhysicalBrush>();
            manager.SetToolStateForTests(state);
            manager.SetLocalPlayerId("left");
            manager.RegisterBrush(brush);
            return manager;
        }

        [Test]
        public void BrushRelease_DoesNotDockUntilExplicitPutDown()
        {
            PhysicalBrush brush;
            ToolState state;
            PhysicalPaintTool tool = Make(out brush, out state);
            tool.SetLocalPlayerId("player");
            Assert.IsTrue(tool.TryPickupBrush("player", brush, Vector3.zero, "Left"));
            brush.Release(new HandInputSample("Left", Vector2.zero, 1, 1, 0f, true, false), Vector3.zero);
            Assert.AreEqual(PhysicalPaintTool.BrushLocation.Held, tool.Location);
            Assert.IsTrue(tool.TryPutDownBrush("player", Vector3.zero));
        }

        [Test]
        public void OppositeHandStation_UsesPlayerIdentityAndAttachesToCarryAnchor()
        {
            PhysicalBrush brush;
            ToolState state;
            PhysicalPaintTool tool = Make(out brush, out state);
            var anchor = New("right carry").transform;
            tool.SetLocalPlayerId("player");
            tool.SetCarryAnchor("Right", anchor);
            Assert.IsTrue(tool.TryPickupBrush("player", brush, Vector3.zero, "Right"));
            Assert.AreEqual(anchor, brush.transform.parent);
            Assert.AreEqual(Vector3.zero, brush.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, brush.transform.localRotation);
            var station = New("station").AddComponent<PhysicalToolStation>();
            station.SetConfiguration(tool, PhysicalToolStation.StationKind.Paint, 2);
            station.Press(new HandInputSample("Right", Vector2.zero, 1, 1, 0f, true, true), Vector3.zero,
                new HandClickContext("Right", 0, 1));
            Assert.AreEqual(2, state.CurrentColorIndex);
        }

        [Test]
        public void OtherPlayerAndOutOfRangeStation_AreRejectedThroughPressPath()
        {
            PhysicalBrush brush;
            ToolState state;
            PhysicalPaintTool tool = Make(out brush, out state);
            tool.SetLocalPlayerId("player");
            Assert.IsTrue(tool.TryPickupBrush("player", brush, Vector3.zero, "Left"));
            var station = New("station").AddComponent<PhysicalToolStation>();
            station.SetConfiguration(tool, PhysicalToolStation.StationKind.Width, 2);
            station.transform.position = new Vector3(5f, 0f, 0f);
            station.PressForPlayer("other", station.transform.position);
            Assert.AreEqual(1, state.CurrentWidthIndex);
        }

        [Test]
        public void PinchPickupAndRelease_LatchesOwnerAndReturnsBrushToRack()
        {
            PhysicalBrush brush;
            ToolState state;
            PhysicalPaintTool tool = Make(out brush, out state);
            var dock = New("dock").transform;
            tool.SetDockAnchor(dock);

            Assert.IsTrue(tool.TryPickupBrush("left", brush, Vector3.zero));
            Assert.AreEqual(PhysicalPaintTool.BrushLocation.Held, tool.Location);
            Assert.AreEqual("left", tool.HeldOwner);
            Assert.IsFalse(tool.TryPickupBrush("right", brush, Vector3.zero));
            Assert.IsTrue(tool.TryPutDownBrush("left", Vector3.zero));
            Assert.AreEqual(PhysicalPaintTool.BrushLocation.Docked, tool.Location);
            Assert.IsNull(tool.HeldOwner);
            Assert.AreEqual(dock, brush.transform.parent);
            Assert.AreEqual(Vector3.zero, brush.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, brush.transform.localRotation);
        }

        [Test]
        public void Stations_ChangeTheExistingToolStateOnlyWhenOwnedBrushIsHeld()
        {
            PhysicalBrush brush;
            ToolState state;
            PhysicalPaintTool tool = Make(out brush, out state);

            Assert.IsFalse(tool.TrySelectPaint("left", 3, Vector3.zero));
            Assert.IsTrue(tool.TryPickupBrush("left", brush, Vector3.zero));
            Assert.IsTrue(tool.TrySelectPaint("left", 3, Vector3.zero));
            Assert.AreEqual(3, state.CurrentColorIndex);
            Assert.IsTrue(tool.TrySelectWidth("left", 2, Vector3.zero));
            Assert.AreEqual(2, state.CurrentWidthIndex);
            Assert.IsTrue(tool.TrySelectEraser("left", Vector3.zero));
            Assert.AreEqual(ToolState.Mode.Erase, state.CurrentMode);
        }

        [Test]
        public void InvalidOwnerAndOutOfRangeInteractions_AreRejectedWithoutStateChanges()
        {
            PhysicalBrush brush;
            ToolState state;
            PhysicalPaintTool tool = Make(out brush, out state);
            tool.MaxInteractionDistance = 1f;
            tool.SetLocalPlayerId(string.Empty);
            Assert.IsFalse(tool.TryPickupBrush("left", brush, Vector3.zero));
            tool.SetLocalPlayerId("left");
            Assert.IsTrue(tool.TryPickupBrush("left", brush, Vector3.zero));
            Assert.IsFalse(tool.TrySelectPaint("right", 2, Vector3.zero));
            Assert.IsFalse(tool.TrySelectPaint("left", 99, Vector3.zero));
            Assert.AreEqual(0, state.CurrentColorIndex);
            Assert.IsFalse(tool.TrySelectWidth("left", 1, new Vector3(2f, 0f, 0f)));
            Assert.AreEqual(1, state.CurrentWidthIndex);
        }

        [Test]
        public void ResetAndDisconnect_ReturnHeldBrushToRackAndClearOwnership()
        {
            PhysicalBrush brush;
            ToolState state;
            PhysicalPaintTool tool = Make(out brush, out state);
            Assert.IsTrue(tool.TryPickupBrush("left", brush, Vector3.zero));
            tool.HandleDisconnect("left");
            Assert.AreEqual(PhysicalPaintTool.BrushLocation.Docked, tool.Location);
            Assert.IsNull(tool.HeldOwner);
            Assert.IsTrue(tool.TryPickupBrush("left", brush, Vector3.zero));
            tool.ResetToRack();
            Assert.AreEqual(PhysicalPaintTool.BrushLocation.Docked, tool.Location);
        }

        [Test]
        public void ProcessSample_DeliversWorldHitToWorldTargetAndScreenToRegularTarget()
        {
            var routerObject = New("router");
            routerObject.SetActive(false);
            var modes = routerObject.AddComponent<InputModeManager>();
            InvokePrivate(modes, "Awake");
            modes.SetContext(InputContext.UiOnly);
            var router = routerObject.AddComponent<HandInputRouter>();
            SetPrivate(router, "inputModeManager", modes);
            routerObject.SetActive(true);
            InvokePrivate(router, "OnEnable");

            var worldObject = New("world target");
            var screenObject = New("screen target");
            var world = worldObject.AddComponent<PositionProbe>();
            world.world = true;
            var screen = screenObject.AddComponent<PositionProbe>();
            Vector2 screenPosition = new Vector2(10f, 20f);
            Vector3 worldHit = new Vector3(7f, 8f, 9f);
            Arm(router, "Left");
            Arm(router, "Right");
            router.ProcessSample(Sample("Left", screenPosition, 3, true), 0.2f, world, worldHit);
            router.ProcessSample(Sample("Right", screenPosition, 3, true), 0.2f, screen, worldHit);

            Assert.AreEqual(worldHit, world.LastPress);
            Assert.AreEqual((Vector3)screenPosition, screen.LastPress);
        }

        [Test]
        public void ResolveTarget_ResolvesWorldBrushWithoutCanvasOrDrawPermission()
        {
            var brushCenter = new Vector3(10000f, 10000f, 10000f);
            var routerObject = New("resolve router");
            var router = routerObject.AddComponent<HandInputRouter>();
            var camera = New("resolve camera").AddComponent<Camera>();
            camera.transform.position = brushCenter + Vector3.back * 5f;
            var events = New("resolve events").AddComponent<EventSystem>();
            var modes = New("resolve modes").AddComponent<InputModeManager>();
            InvokePrivate(modes, "Awake");
            modes.SetContext(InputContext.UiOnly);
            SetPrivate(router, "playerCamera", camera);
            SetPrivate(router, "eventSystem", events);
            SetPrivate(router, "uiRaycasters", new UnityEngine.UI.GraphicRaycaster[0]);
            var state = New("resolve state").AddComponent<ToolState>();
            var tool = New("resolve tool").AddComponent<PhysicalPaintTool>();
            tool.SetToolStateForTests(state);
            tool.SetLocalPlayerId("player");
            var brushObject = New("resolve brush");
            brushObject.transform.position = brushCenter;
            brushObject.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            var brush = brushObject.AddComponent<PhysicalBrush>();
            tool.RegisterBrush(brush);

            HandInteractable target = router.ResolveTarget(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out Vector3 hit, out bool blocked);

            Assert.IsFalse(blocked);
            Assert.AreSame(brush, target);
            Assert.That(hit.z, Is.EqualTo(brushCenter.z - 0.5f).Within(0.1f));
        }

        private static void Arm(HandInputRouter router, string hand)
        {
            router.ProcessSample(Sample(hand, new Vector2(10f, 20f), 1, false), 0f, null, Vector3.zero);
            router.ProcessSample(Sample(hand, new Vector2(10f, 20f), 2, false), 0.11f, null, Vector3.zero);
        }

        private static HandInputSample Sample(string hand, Vector2 screen, ulong id, bool pinched)
        {
            return new HandInputSample(hand, screen, (uint)id, id, 0f, true, pinched);
        }

        private static void SetPrivate(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static void InvokePrivate(object target, string name)
        {
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        }
    }
}
