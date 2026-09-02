using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CameraCoop.Party
{
    public sealed class PartyGameSceneAdapter : MonoBehaviour, IPartyGameScenePort
    {
        [SerializeField] private PartySceneBindings bindings = new PartySceneBindings();

        private static PartyGameSceneAdapter registeredAdapter;
        private static PartyTransitionKey registeredTransitionKey;

        private const int RequiredActionCount = 3;
        private const int RequiredGallerySlotCount = PartyRoster.Capacity - 1;
        private const int RequiredBrushCount = 1;
        private const int RequiredToolStationCount = 1;

        public PartyMode Mode => bindings != null ? bindings.Mode : default;
        public PartySceneBindings Bindings => bindings;
        public bool IsRegistered => registeredAdapter == this;
        public PartyTransitionKey RegisteredTransitionKey => IsRegistered ? registeredTransitionKey : default;

        public void Configure(PartySceneBindings value)
        {
            bindings = value ?? throw new ArgumentNullException(nameof(value));
        }

        public bool ValidateBindings(out string error)
        {
            if (bindings == null) return Fail("PartySceneBindings is required.", out error);
            if (!PartyModeCatalog.TryGet(bindings.Mode, out _)) return Fail("PartyMode is invalid.", out error);
            if (bindings.SceneRoot == null) return Fail("sceneRoot is required.", out error);
            if (!BelongsToRoot(gameObject)) return Fail("PartyGameSceneAdapter must belong to sceneRoot.", out error);
            if (!ValidateNoPersistentOwners(out error)) return false;
            if (!ValidateTransforms(bindings.SlotSpawns, "slotSpawns", PartyRoster.Capacity, out error)) return false;
            if (!ValidateComponents(bindings.SlotZones, "slotZones", PartyRoster.Capacity, out error)) return false;
            if (!ValidateTransforms(bindings.SlotDocks, "slotDocks", PartyRoster.Capacity, out error)) return false;
            if (!ValidateTransform(bindings.CarryAnchor, "carryAnchor", out error)) return false;
            if (!ValidateComponents(bindings.Actions, "actions", RequiredActionCount, out error)) return false;
            PartyWorldAction[] requiredActions =
                { PartyWorldAction.CarryCanvas, PartyWorldAction.DockCanvas, PartyWorldAction.ReturnToLobby };
            for (int index = 0; index < requiredActions.Length; index++)
                if (bindings.Actions[index].Action != requiredActions[index])
                    return Fail("actions must be CarryCanvas, DockCanvas, ReturnToLobby in order.", out error);
            if (!ValidateGameObjects(bindings.AvatarRoots, "avatarRoots", PartyRoster.Capacity, out error)) return false;
            if (!ValidateComponents(bindings.AvatarPresenters, "avatarPresenters", PartyRoster.Capacity - 1, out error)) return false;
            if (!ValidateGameObject(bindings.WritablePaperRoot, "writablePaperRoot", out error)) return false;
            if (!ValidateComponent(bindings.WritableSurface, "writableSurface", out error)) return false;
            if (!ValidateComponent(bindings.WritableInteractable, "writableInteractable", out error)) return false;
            if (!ValidateTransform(bindings.ToolRack, "toolRack", out error)) return false;
            if (!ValidateComponent(bindings.PhysicalPaintTool, "physicalPaintTool", out error)) return false;
            if (!ValidateComponents(bindings.Brushes, "brushes", RequiredBrushCount, out error)) return false;
            if (!ValidateComponents(bindings.ToolStations, "toolStations", RequiredToolStationCount, out error)) return false;

            if (bindings.Mode == PartyMode.CoopMural)
            {
                if (!ValidateGameObject(bindings.ResultRoot, "resultRoot", out error)) return false;
                if (!ValidateGameObjects(bindings.MuralLayerRoots, "muralLayerRoots", PartyRoster.Capacity, out error)) return false;
                if (!ValidateComponents(bindings.MuralLayerPresenters, "muralLayerPresenters", PartyRoster.Capacity, out error)) return false;
                if (!ValidateComponents(bindings.MuralLayerSurfaces, "muralLayerSurfaces", PartyRoster.Capacity, out error)) return false;
            }
            else
            {
                if (!ValidateComponent(bindings.ReferencePresenter, "referencePresenter", out error)) return false;
                if (!ValidateComponent(bindings.ReferenceSurface, "referenceSurface", out error)) return false;
                if (!ValidateGameObject(bindings.ResultRoot, "resultRoot", out error)) return false;
                if (!ValidateTransform(bindings.ResultViewPose, "resultViewPose", out error)) return false;
                if (!ValidateGameObjects(bindings.GalleryRoots, "galleryRoots", RequiredGallerySlotCount, out error)) return false;
                if (!ValidateComponents(bindings.GalleryPresenters, "galleryPresenters", RequiredGallerySlotCount, out error)) return false;
                if (!ValidateComponents(bindings.GallerySurfaces, "gallerySurfaces", RequiredGallerySlotCount, out error)) return false;
            }

            error = string.Empty;
            return true;
        }

        public bool Register(PartyMode expectedMode, PartyTransitionKey transitionKey, out string error)
        {
            if (!PartyModeCatalog.TryGet(expectedMode, out _)) return Fail("Requested PartyMode is invalid.", out error);
            if (!transitionKey.IsValid || transitionKey.TransitionGeneration <= 0)
                return Fail("transitionKey must contain a valid session, roster generation, and positive transition generation.", out error);
            if (bindings == null || bindings.Mode != expectedMode)
                return Fail("Adapter mode does not match the requested PartyMode.", out error);
            if (!ValidateBindings(out error)) return false;
            if (registeredAdapter == null)
            {
                registeredAdapter = this;
                registeredTransitionKey = transitionKey;
                error = string.Empty;
                return true;
            }
            if (registeredAdapter == this && registeredTransitionKey == transitionKey)
            {
                error = string.Empty;
                return true;
            }
            if (registeredAdapter == this)
                return Fail("Adapter is already registered for a different transition.", out error);
            return Fail("Another PartyGameSceneAdapter is already registered.", out error);
        }

        public void Unregister()
        {
            if (registeredAdapter != this) return;
            registeredAdapter = null;
            registeredTransitionKey = default;
        }

        private void OnDestroy()
        {
            Unregister();
        }

        private bool ValidateNoPersistentOwners(out string error)
        {
            if (bindings.SceneRoot.GetComponentInChildren<EventSystem>(true) != null)
                return Fail("Additive game Scene must not own EventSystem.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<Camera>(true) != null)
                return Fail("Additive game Scene must not own Camera.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<AudioListener>(true) != null)
                return Fail("Additive game Scene must not own AudioListener.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<OnlineRelayQuizController>(true) != null)
                return Fail("Additive game Scene must not own OnlineRelayQuizController.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<TrackerLauncher>(true) != null)
                return Fail("Additive game Scene must not own TrackerLauncher.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<UdpHandReceiver>(true) != null)
                return Fail("Additive game Scene must not own UdpHandReceiver.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<HandInputRouter>(true) != null)
                return Fail("Additive game Scene must not own HandInputRouter.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<HandPointer>(true) != null)
                return Fail("Additive game Scene must not own HandPointer.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<HandCursorController>(true) != null)
                return Fail("Additive game Scene must not own HandCursorController.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<InputModeManager>(true) != null)
                return Fail("Additive game Scene must not own InputModeManager.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<PlayerController>(true) != null)
                return Fail("Additive game Scene must not own PlayerController.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<DrawingController>(true) != null)
                return Fail("Additive game Scene must not own DrawingController.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<ToolState>(true) != null)
                return Fail("Additive game Scene must not own ToolState.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<CameraControlPanel>(true) != null)
                return Fail("Additive game Scene must not own CameraControlPanel.", out error);
            if (bindings.SceneRoot.GetComponentInChildren<PartyWorldController>(true) != null)
                return Fail("Additive game Scene must not own PartyWorldController.", out error);
            Canvas[] canvases = bindings.SceneRoot.GetComponentsInChildren<Canvas>(true);
            for (int index = 0; index < canvases.Length; index++)
                if (canvases[index].renderMode != RenderMode.WorldSpace)
                    return Fail("Additive game Scene must not own a global Canvas.", out error);
            error = string.Empty;
            return true;
        }

        private bool ValidateGameObjects(GameObject[] values, string name, int expectedLength, out string error)
        {
            if (values == null || values.Length != expectedLength)
                return Fail(name + "[" + expectedLength + "] is required.", out error);
            for (int index = 0; index < values.Length; index++)
                if (!ValidateGameObject(values[index], name + "[" + index + "]", out error)) return false;
            error = string.Empty;
            return true;
        }

        private bool ValidateTransforms(Transform[] values, string name, int expectedLength, out string error)
        {
            if (values == null || values.Length != expectedLength)
                return Fail(name + "[" + expectedLength + "] is required.", out error);
            for (int index = 0; index < values.Length; index++)
                if (!ValidateTransform(values[index], name + "[" + index + "]", out error)) return false;
            error = string.Empty;
            return true;
        }

        private bool ValidateComponents<T>(T[] values, string name, int expectedLength, out string error) where T : Component
        {
            if (values == null || values.Length != expectedLength)
                return Fail(name + "[" + expectedLength + "] is required.", out error);
            for (int index = 0; index < values.Length; index++)
                if (!ValidateComponent(values[index], name + "[" + index + "]", out error)) return false;
            error = string.Empty;
            return true;
        }

        private bool ValidateGameObject(GameObject value, string name, out string error)
        {
            if (value == null) return Fail(name + " is required.", out error);
            if (!BelongsToRoot(value)) return Fail(name + " must belong to the adapter Scene root.", out error);
            error = string.Empty;
            return true;
        }

        private bool ValidateTransform(Transform value, string name, out string error)
        {
            return ValidateComponent(value, name, out error);
        }

        private bool ValidateComponent(Component value, string name, out string error)
        {
            if (value == null) return Fail(name + " is required.", out error);
            if (!BelongsToRoot(value.gameObject)) return Fail(name + " must belong to the adapter Scene root.", out error);
            error = string.Empty;
            return true;
        }

        private bool BelongsToRoot(GameObject value)
        {
            if (bindings == null || bindings.SceneRoot == null || value == null) return false;
            Transform root = bindings.SceneRoot.transform;
            return value.scene == bindings.SceneRoot.scene
                && (value.transform == root || value.transform.IsChildOf(root));
        }

        private static bool Fail(string value, out string error)
        {
            error = value;
            return false;
        }
    }
}
