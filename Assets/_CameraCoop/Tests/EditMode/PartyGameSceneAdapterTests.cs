using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using CameraCoop.Party;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CameraCoop.Tests
{
    public sealed class PartyGameSceneAdapterTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in objects)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void ValidRelayCopyAdapterExposesOnlySceneLocalPresentationPorts()
        {
            Component adapter = CreateAdapter(CreateValidBindings(PartyMode.RelayCopy));

            Assert.That(Validate(adapter, out string error), Is.True, error);
        }

        [Test]
        public void AdapterContainingEventSystemIsRejected()
        {
            object bindings = CreateValidBindings(PartyMode.RelayCopy);
            GameObject sceneRoot = (GameObject)GetProperty(bindings, "SceneRoot");
            CreateObject("EventSystem", sceneRoot.transform).AddComponent<EventSystem>();
            Component adapter = CreateAdapter(bindings);

            Assert.That(Validate(adapter, out string error), Is.False);
            Assert.That(error, Is.EqualTo("Additive game Scene must not own EventSystem."));
        }

        [TestCase(typeof(DrawingController), "DrawingController")]
        [TestCase(typeof(ToolState), "ToolState")]
        [TestCase(typeof(CameraControlPanel), "CameraControlPanel")]
        [TestCase(typeof(HandCursorController), "HandCursorController")]
        public void AdapterContainingPersistentRuntimeOwnerIsRejected(Type ownerType, string ownerName)
        {
            object bindings = CreateValidBindings(PartyMode.RelayCopy);
            GameObject sceneRoot = (GameObject)GetProperty(bindings, "SceneRoot");
            sceneRoot.AddComponent(ownerType);
            Component adapter = CreateAdapter(bindings);

            Assert.That(Validate(adapter, out string error), Is.False);
            Assert.That(error, Is.EqualTo("Additive game Scene must not own " + ownerName + "."));
        }

        [Test]
        public void CoopMuralRequiresFourPublicLayersWithoutReferenceOrResultPorts()
        {
            object bindings = CreateValidBindings(PartyMode.CoopMural);
            Component adapter = CreateAdapter(bindings);

            Assert.That(Validate(adapter, out string validError), Is.True, validError);

            SetProperty(bindings, "MuralLayerRoots", new GameObject[PartyRoster.Capacity - 1]);

            Assert.That(Validate(adapter, out string error), Is.False);
            Assert.That(error, Is.EqualTo("muralLayerRoots[4] is required."));
        }

        [Test]
        public void SurfaceOutsideTheAdapterSceneRootIsRejected()
        {
            object bindings = CreateValidBindings(PartyMode.RelayCopy);
            GameObject foreignSurface = new GameObject("Foreign writable surface");
            objects.Add(foreignSurface);
            SetProperty(bindings, "WritableSurface", foreignSurface.AddComponent<CanvasSurface>());
            Component adapter = CreateAdapter(bindings);

            Assert.That(Validate(adapter, out string error), Is.False);
            Assert.That(error, Is.EqualTo("writableSurface must belong to the adapter Scene root."));
        }

        [Test]
        public void RegistrationIsIdempotentForSameTransitionAndRejectsAnotherAdapter()
        {
            Component first = CreateAdapter(CreateValidBindings(PartyMode.RelayCopy));
            Component second = CreateAdapter(CreateValidBindings(PartyMode.RelayCopy));
            PartyTransitionKey key = Key("session-a", 3, 7);

            Assert.That(Register(first, PartyMode.RelayCopy, key, out string firstError), Is.True, firstError);
            Assert.That(Register(first, PartyMode.RelayCopy, key, out string repeatError), Is.True, repeatError);
            Assert.That(Register(second, PartyMode.RelayCopy, key, out string secondError), Is.False);
            Assert.That(secondError, Is.EqualTo("Another PartyGameSceneAdapter is already registered."));

            Unregister(first);

            Assert.That(Register(second, PartyMode.RelayCopy, key, out string afterUnregisterError), Is.True, afterUnregisterError);
            Unregister(second);
        }

        [Test]
        public void RegistrationRejectsSameGenerationFromAnotherSessionOrRoster()
        {
            Component adapter = CreateAdapter(CreateValidBindings(PartyMode.RelayCopy));

            Assert.That(Register(adapter, PartyMode.RelayCopy, Key("session-a", 3, 7), out string initialError), Is.True, initialError);
            Assert.That(Register(adapter, PartyMode.RelayCopy, Key("session-b", 3, 7), out string sessionError), Is.False);
            Assert.That(sessionError, Is.EqualTo("Adapter is already registered for a different transition."));
            Assert.That(Register(adapter, PartyMode.RelayCopy, Key("session-a", 4, 7), out string rosterError), Is.False);
            Assert.That(rosterError, Is.EqualTo("Adapter is already registered for a different transition."));

            Unregister(adapter);
        }

        [Test]
        public void RegistrationRejectsMismatchedPartyMode()
        {
            Component adapter = CreateAdapter(CreateValidBindings(PartyMode.RelayCopy));

            Assert.That(Register(adapter, PartyMode.MemoryCopy, Key("session-a", 3, 7), out string error), Is.False);
            Assert.That(error, Is.EqualTo("Adapter mode does not match the requested PartyMode."));
        }

        [Test]
        public void TypedAdapterFixtureValidatesAndRegistersWithItsFullTransitionKey()
        {
            PartySceneBindings bindings = CreateValidBindings(PartyMode.RelayCopy);
            PartyGameSceneAdapter adapter = CreateAdapter(bindings);
            PartyTransitionKey key = Key("typed-session", 3, 7);

            Assert.That(adapter.ValidateBindings(out string validationError), Is.True, validationError);
            Assert.That(adapter.Register(PartyMode.RelayCopy, key, out string registrationError), Is.True, registrationError);
            Assert.That(adapter.RegisteredTransitionKey, Is.EqualTo(key));

            adapter.Unregister();
        }

        [Test]
        public void DestroyingRegisteredTypedAdapterReleasesStaticRegistration()
        {
            PartyGameSceneAdapter first = CreateAdapter(CreateValidBindings(PartyMode.RelayCopy));
            PartyGameSceneAdapter second = CreateAdapter(CreateValidBindings(PartyMode.RelayCopy));
            PartyTransitionKey key = Key("destroy-session", 3, 7);

            Assert.That(first.Register(PartyMode.RelayCopy, key, out string firstError), Is.True, firstError);

            UnityEngine.Object.DestroyImmediate(first.gameObject);

            Assert.That(second.Register(PartyMode.RelayCopy, key, out string secondError), Is.True, secondError);
            second.Unregister();
        }

        [Test]
        public void OversizedSlotBindingsAreRejected()
        {
            object bindings = CreateValidBindings(PartyMode.RelayCopy);
            GameObject sceneRoot = (GameObject)GetProperty(bindings, "SceneRoot");
            Transform[] oversized = Append(
                (Transform[])GetProperty(bindings, "SlotSpawns"),
                CreateObject("Extra spawn", sceneRoot.transform).transform);
            SetProperty(bindings, "SlotSpawns", oversized);

            Assert.That(Validate(CreateAdapter(bindings), out string error), Is.False);
            Assert.That(error, Is.EqualTo("slotSpawns[4] is required."));
        }

        [Test]
        public void OversizedPresenterBindingsAreRejected()
        {
            object bindings = CreateValidBindings(PartyMode.RelayCopy);
            GameObject sceneRoot = (GameObject)GetProperty(bindings, "SceneRoot");
            RemoteAvatarPresenter[] oversized = Append(
                (RemoteAvatarPresenter[])GetProperty(bindings, "AvatarPresenters"),
                CreateObject("Extra avatar presenter", sceneRoot.transform).AddComponent<RemoteAvatarPresenter>());
            SetProperty(bindings, "AvatarPresenters", oversized);

            Assert.That(Validate(CreateAdapter(bindings), out string error), Is.False);
            Assert.That(error, Is.EqualTo("avatarPresenters[3] is required."));
        }

        [Test]
        public void OversizedActionBindingsAreRejected()
        {
            object bindings = CreateValidBindings(PartyMode.RelayCopy);
            GameObject sceneRoot = (GameObject)GetProperty(bindings, "SceneRoot");
            WorldActionInteractable[] oversized = Append(
                (WorldActionInteractable[])GetProperty(bindings, "Actions"),
                CreateObject("Extra action", sceneRoot.transform).AddComponent<WorldActionInteractable>());
            SetProperty(bindings, "Actions", oversized);

            Assert.That(Validate(CreateAdapter(bindings), out string error), Is.False);
            Assert.That(error, Is.EqualTo("actions[3] is required."));
        }

        [Test]
        public void OversizedBrushBindingsAreRejected()
        {
            object bindings = CreateValidBindings(PartyMode.RelayCopy);
            GameObject sceneRoot = (GameObject)GetProperty(bindings, "SceneRoot");
            PhysicalBrush[] oversized = Append(
                (PhysicalBrush[])GetProperty(bindings, "Brushes"),
                CreateObject("Extra brush", sceneRoot.transform).AddComponent<PhysicalBrush>());
            SetProperty(bindings, "Brushes", oversized);

            Assert.That(Validate(CreateAdapter(bindings), out string error), Is.False);
            Assert.That(error, Is.EqualTo("brushes[1] is required."));
        }

        [Test]
        public void OversizedToolStationBindingsAreRejected()
        {
            object bindings = CreateValidBindings(PartyMode.RelayCopy);
            GameObject sceneRoot = (GameObject)GetProperty(bindings, "SceneRoot");
            HandInteractable[] oversized = Append(
                (HandInteractable[])GetProperty(bindings, "ToolStations"),
                CreateObject("Extra tool station", sceneRoot.transform).AddComponent<WorldActionInteractable>());
            SetProperty(bindings, "ToolStations", oversized);

            Assert.That(Validate(CreateAdapter(bindings), out string error), Is.False);
            Assert.That(error, Is.EqualTo("toolStations[1] is required."));
        }

        [Test]
        public void PrivateModeRejectsAnyGallerySlotCountOtherThanThree()
        {
            PartySceneBindings bindings = CreateValidBindings(PartyMode.RelayCopy);
            bindings.GalleryRoots = bindings.GalleryRoots.Take(2).ToArray();

            Assert.That(Validate(CreateAdapter(bindings), out string error), Is.False);
            Assert.That(error, Is.EqualTo("galleryRoots[3] is required."));
        }

        [Test]
        public void LobbyPortTogglesOnlyItsConfiguredLobbyWorldRoot()
        {
            Type portType = RequireRuntimeType("CameraCoop.Party.PartyLobbyScenePort");
            GameObject runtimeRoot = CreateObject("Runtime root");
            GameObject lobbyRoot = CreateObject("Lobby world root");
            Component port = runtimeRoot.AddComponent(portType);
            var spawns = new Transform[PartyRoster.Capacity];
            var practiceRoots = new GameObject[PartyRoster.Capacity];
            var practicePresenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
            var practiceSurfaces = new CanvasSurface[PartyRoster.Capacity];
            var avatarRoots = new GameObject[PartyRoster.Capacity];
            var avatarPresenters = new RemoteAvatarPresenter[PartyRoster.Capacity - 1];
            for (int slot = 0; slot < spawns.Length; slot++)
            {
                spawns[slot] = CreateObject("Lobby spawn " + slot, lobbyRoot.transform).transform;
                practiceRoots[slot] = CreateObject("Practice root " + slot, lobbyRoot.transform);
                practicePresenters[slot] = CreateObject("Practice presenter " + slot, lobbyRoot.transform)
                    .AddComponent<CanvasDrawingPresenter>();
                practiceSurfaces[slot] = CreateObject("Practice surface " + slot, lobbyRoot.transform)
                    .AddComponent<CanvasSurface>();
                avatarRoots[slot] = CreateObject("Avatar root " + slot, lobbyRoot.transform);
                if (slot < avatarPresenters.Length)
                    avatarPresenters[slot] = CreateObject("Avatar presenter " + slot, lobbyRoot.transform)
                        .AddComponent<RemoteAvatarPresenter>();
            }

            Invoke(port, "Configure", lobbyRoot, spawns, practiceRoots, practicePresenters,
                practiceSurfaces, avatarRoots, avatarPresenters);
            Assert.That(InvokeBoolWithError(port, "ValidateBindings", out string error), Is.True, error);

            Invoke(port, "SetLobbyVisible", false);

            Assert.That(lobbyRoot.activeSelf, Is.False);
        }

        private PartySceneBindings CreateValidBindings(PartyMode mode)
        {
            var bindings = new PartySceneBindings();
            GameObject sceneRoot = CreateObject(mode + " scene root");
            bindings.Mode = mode;
            bindings.SceneRoot = sceneRoot;

            var spawns = new Transform[PartyRoster.Capacity];
            var zones = new BoxCollider[PartyRoster.Capacity];
            var docks = new Transform[PartyRoster.Capacity];
            var avatars = new GameObject[PartyRoster.Capacity];
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                spawns[slot] = CreateObject("Spawn " + slot, sceneRoot.transform).transform;
                zones[slot] = CreateObject("Zone " + slot, sceneRoot.transform).AddComponent<BoxCollider>();
                docks[slot] = CreateObject("Dock " + slot, sceneRoot.transform).transform;
                avatars[slot] = CreateObject("Avatar " + slot, sceneRoot.transform);
            }
            bindings.SlotSpawns = spawns;
            bindings.SlotZones = zones;
            bindings.SlotDocks = docks;
            bindings.CarryAnchor = CreateObject("Carry anchor", sceneRoot.transform).transform;
            bindings.Actions = new[]
            {
                CreateAction("Carry action", sceneRoot.transform, PartyWorldAction.CarryCanvas),
                CreateAction("Dock action", sceneRoot.transform, PartyWorldAction.DockCanvas),
                CreateAction("Return action", sceneRoot.transform, PartyWorldAction.ReturnToLobby)
            };
            bindings.AvatarRoots = avatars;
            bindings.AvatarPresenters = new[]
            {
                CreateObject("Avatar presenter 1", sceneRoot.transform).AddComponent<RemoteAvatarPresenter>(),
                CreateObject("Avatar presenter 2", sceneRoot.transform).AddComponent<RemoteAvatarPresenter>(),
                CreateObject("Avatar presenter 3", sceneRoot.transform).AddComponent<RemoteAvatarPresenter>()
            };
            bindings.WritablePaperRoot = CreateObject("Writable paper", sceneRoot.transform);
            bindings.WritableSurface = CreateObject("Writable surface", sceneRoot.transform).AddComponent<CanvasSurface>();
            bindings.WritableInteractable = bindings.WritableSurface.gameObject.AddComponent<HandCanvasInteractable>();
            bindings.ReferencePresenter = CreateObject("Reference presenter", sceneRoot.transform).AddComponent<CanvasDrawingPresenter>();
            bindings.ReferenceSurface = CreateObject("Reference surface", sceneRoot.transform).AddComponent<CanvasSurface>();
            bindings.ResultRoot = CreateObject("Result root", sceneRoot.transform);
            bindings.ResultViewPose = CreateObject("Result view pose", sceneRoot.transform).transform;
            bindings.GalleryRoots = new GameObject[PartyRoster.Capacity - 1];
            bindings.GalleryPresenters = new CanvasDrawingPresenter[PartyRoster.Capacity - 1];
            bindings.GallerySurfaces = new CanvasSurface[PartyRoster.Capacity - 1];
            for (int slot = 0; slot < bindings.GalleryRoots.Length; slot++)
            {
                bindings.GalleryRoots[slot] = CreateObject("Gallery root " + slot, bindings.ResultRoot.transform);
                bindings.GalleryPresenters[slot] = CreateObject("Gallery presenter " + slot,
                    bindings.GalleryRoots[slot].transform).AddComponent<CanvasDrawingPresenter>();
                bindings.GallerySurfaces[slot] = CreateObject("Gallery surface " + slot,
                    bindings.GalleryRoots[slot].transform).AddComponent<CanvasSurface>();
            }
            bindings.ToolRack = CreateObject("Tool rack", sceneRoot.transform).transform;
            bindings.PhysicalPaintTool = CreateObject("Physical paint tool", sceneRoot.transform).AddComponent<PhysicalPaintTool>();
            bindings.Brushes = new[] { CreateObject("Brush", sceneRoot.transform).AddComponent<PhysicalBrush>() };
            bindings.ToolStations = new HandInteractable[]
            {
                CreateObject("Tool station", sceneRoot.transform).AddComponent<WorldActionInteractable>()
            };

            if (mode == PartyMode.CoopMural)
            {
                bindings.ReferencePresenter = null;
                bindings.ReferenceSurface = null;
                bindings.ResultRoot = CreateObject("Mural final result", sceneRoot.transform);
                bindings.ResultViewPose = null;
                bindings.GalleryRoots = null;
                bindings.GalleryPresenters = null;
                bindings.GallerySurfaces = null;
                var layerRoots = new GameObject[PartyRoster.Capacity];
                var layerPresenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
                var layerSurfaces = new CanvasSurface[PartyRoster.Capacity];
                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                {
                    layerRoots[slot] = CreateObject("Mural layer " + slot, sceneRoot.transform);
                    layerPresenters[slot] = CreateObject("Mural presenter " + slot, sceneRoot.transform).AddComponent<CanvasDrawingPresenter>();
                    layerSurfaces[slot] = CreateObject("Mural surface " + slot, sceneRoot.transform).AddComponent<CanvasSurface>();
                }
                bindings.MuralLayerRoots = layerRoots;
                bindings.MuralLayerPresenters = layerPresenters;
                bindings.MuralLayerSurfaces = layerSurfaces;
            }

            return bindings;
        }

        private WorldActionInteractable CreateAction(string name, Transform parent, PartyWorldAction action)
        {
            WorldActionInteractable result = CreateObject(name, parent).AddComponent<WorldActionInteractable>();
            typeof(WorldActionInteractable).GetField("action", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(result, action);
            return result;
        }

        private PartyGameSceneAdapter CreateAdapter(PartySceneBindings bindings)
        {
            PartyGameSceneAdapter adapter = CreateObject("Party game adapter", bindings.SceneRoot.transform)
                .AddComponent<PartyGameSceneAdapter>();
            adapter.Configure(bindings);
            return adapter;
        }

        private Component CreateAdapter(object bindings)
        {
            Type adapterType = RequireRuntimeType("CameraCoop.Party.PartyGameSceneAdapter");
            Component adapter = CreateObject("Party game adapter", ((GameObject)GetProperty(bindings, "SceneRoot")).transform)
                .AddComponent(adapterType);
            Invoke(adapter, "Configure", bindings);
            return adapter;
        }

        private static bool Validate(Component adapter, out string error)
        {
            return InvokeBoolWithError(adapter, "ValidateBindings", out error);
        }

        private static bool Register(Component adapter, PartyMode mode, PartyTransitionKey key, out string error)
        {
            return InvokeBoolWithError(adapter, "Register", out error, mode, key);
        }

        private static PartyTransitionKey Key(string sessionId, int rosterGeneration, int transitionGeneration)
        {
            return new PartyTransitionKey(sessionId, rosterGeneration, transitionGeneration);
        }

        private static T[] Append<T>(T[] values, T value)
        {
            var result = new T[values.Length + 1];
            Array.Copy(values, result, values.Length);
            result[values.Length] = value;
            return result;
        }

        private static void Unregister(Component adapter)
        {
            Invoke(adapter, "Unregister");
        }

        private static Type RequireRuntimeType(string fullName)
        {
            Type type = Type.GetType(fullName + ", CameraCoop.Runtime");
            Assert.That(type, Is.Not.Null, fullName + " must be supplied by CameraCoop.Runtime.");
            return type;
        }

        private static object GetProperty(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name);
            Assert.That(property, Is.Not.Null, name + " property is required.");
            return property.GetValue(target);
        }

        private static void SetProperty(object target, string name, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(name);
            Assert.That(property, Is.Not.Null, name + " property is required.");
            Assert.That(property.CanWrite, Is.True, name + " must be configurable.");
            property.SetValue(target, value);
        }

        private static void Invoke(Component target, string name, params object[] arguments)
        {
            MethodInfo method = FindMethod(target.GetType(), name, arguments.Length);
            Assert.That(method, Is.Not.Null, name + " method is required.");
            method.Invoke(target, arguments);
        }

        private static bool InvokeBoolWithError(Component target, string name, out string error, params object[] arguments)
        {
            MethodInfo method = FindMethod(target.GetType(), name, arguments.Length + 1);
            Assert.That(method, Is.Not.Null, name + " method is required.");
            object[] invocation = new object[arguments.Length + 1];
            Array.Copy(arguments, invocation, arguments.Length);
            invocation[invocation.Length - 1] = null;
            bool result = (bool)method.Invoke(target, invocation);
            error = invocation[invocation.Length - 1] as string;
            return result;
        }

        private static MethodInfo FindMethod(Type type, string name, int parameterCount)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                if (method.Name == name && method.GetParameters().Length == parameterCount) return method;
            return null;
        }

        private GameObject CreateObject(string name, Transform parent = null)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            objects.Add(item);
            return item;
        }
    }
}
