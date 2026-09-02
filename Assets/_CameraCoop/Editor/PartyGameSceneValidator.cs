using System;
using System.Linq;
using CameraCoop.Party;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace CameraCoop.EditorTools
{
    public static class PartyGameSceneValidator
    {
        [MenuItem("Camera Co-op/RelayQuiz Online/Validate All Party Scenes")]
        public static void ValidateAll()
        {
            SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                if (!RelayQuizOnlineSceneValidator.TryValidate(out string lobbyError))
                    throw new InvalidOperationException("RelayQuizOnline: " + lobbyError);
                foreach (PartyMode mode in Enum.GetValues(typeof(PartyMode)))
                {
                    if (!PartySceneCatalog.TryGet(mode, out PartySceneDefinition definition))
                        throw new InvalidOperationException("PartySceneCatalog is missing " + mode + ".");
                    if (!TryValidateScene(definition.ScenePath, mode, out string error))
                        throw new InvalidOperationException(mode + ": " + error);
                }
                Debug.Log("[PartyGameSceneValidator] PASS: all four catalog Scenes and additive ownership rules are valid.");
            }
            finally
            {
                RestoreOriginalSetupOrLobby(originalSetup);
            }
        }

        public static bool TryValidateScene(string scenePath, PartyMode expectedMode, out string error)
        {
            SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                return TryValidateSceneCore(scenePath, expectedMode, out error);
            }
            finally
            {
                RestoreOriginalSetupOrLobby(originalSetup);
            }
        }

        internal static void RestoreOriginalSetupOrLobby(SceneSetup[] originalSetup)
        {
            if (originalSetup != null && originalSetup.Any(item => item.isLoaded))
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                return;
            }

            Scene lobby = EditorSceneManager.OpenScene(PartySceneCatalog.LobbyScenePath, OpenSceneMode.Single);
            if (!lobby.IsValid() || !lobby.isLoaded)
                throw new InvalidOperationException("Validator could not restore RelayQuizOnline after an empty startup setup.");
            if (SceneManager.GetActiveScene() != lobby && !SceneManager.SetActiveScene(lobby))
                throw new InvalidOperationException("Validator could not activate RelayQuizOnline after an empty startup setup.");
        }

        private static bool TryValidateSceneCore(string scenePath, PartyMode expectedMode, out string error)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                return Fail("Scene asset is missing: " + scenePath, out error);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded || scene.path != scenePath)
                return Fail("Scene could not be loaded at " + scenePath + ".", out error);
            foreach (Transform transform in FindAll<Transform>(scene))
                if (transform.GetComponents<Component>().Any(component => component == null))
                    return Fail("Missing script at " + transform.name + ".", out error);

            PartyGameSceneAdapter[] adapters = FindAll<PartyGameSceneAdapter>(scene);
            if (adapters.Length != 1)
                return Fail(expectedMode + " must contain exactly one PartyGameSceneAdapter.", out error);
            PartyGameSceneAdapter adapter = adapters[0];
            if (adapter.Mode != expectedMode)
                return Fail(expectedMode + " adapter mode is " + adapter.Mode + ".", out error);
            if (!adapter.ValidateBindings(out string bindingError))
                return Fail(expectedMode + " bindings: " + bindingError, out error);

            if (!ValidateNoPersistentOwners(scene, expectedMode, out error)) return false;
            if (!ValidateLabels(scene, expectedMode, out error)) return false;
            if (!ValidateCommonLayout(scene, adapter.Bindings, expectedMode, out error)) return false;
            if (expectedMode == PartyMode.CoopMural)
            {
                if (!ValidateMural(scene, adapter.Bindings, out error)) return false;
            }
            else if (!ValidatePrivateMode(scene, adapter.Bindings, expectedMode, out error)) return false;

            error = string.Empty;
            return true;
        }

        private static bool ValidateNoPersistentOwners(Scene scene, PartyMode mode, out string error)
        {
            if (FindAll<Camera>(scene).Length != 0) return Fail(mode + " must not contain Camera.", out error);
            if (FindAll<EventSystem>(scene).Length != 0) return Fail(mode + " must not contain EventSystem.", out error);
            if (FindAll<AudioListener>(scene).Length != 0) return Fail(mode + " must not contain AudioListener.", out error);
            if (FindAll<OnlineRelayQuizController>(scene).Length != 0)
                return Fail(mode + " must not contain OnlineRelayQuizController.", out error);
            if (FindAll<PartyWorldController>(scene).Length != 0)
                return Fail(mode + " must not contain PartyWorldController.", out error);
            if (FindAll<TrackerLauncher>(scene).Length != 0)
                return Fail(mode + " must not contain TrackerLauncher.", out error);
            if (FindAll<UdpHandReceiver>(scene).Length != 0)
                return Fail(mode + " must not contain UdpHandReceiver.", out error);
            if (FindAll<InputModeManager>(scene).Length != 0 || FindAll<HandInputRouter>(scene).Length != 0
                || FindAll<HandPointer>(scene).Length != 0 || FindAll<PlayerController>(scene).Length != 0)
                return Fail(mode + " must not contain input/player owners.", out error);
            if (FindAll<DrawingController>(scene).Length != 0 || FindAll<ToolState>(scene).Length != 0)
                return Fail(mode + " must not contain drawing/tool state owners.", out error);
            if (FindAll<Canvas>(scene).Any(canvas => canvas.renderMode != RenderMode.WorldSpace))
                return Fail(mode + " must not contain global Canvas.", out error);
            return Pass(out error);
        }

        private static bool ValidateLabels(Scene scene, PartyMode mode, out string error)
        {
            TextMesh[] labels = FindAll<TextMesh>(scene);
            if (labels.Length == 0) return Fail(mode + " has no player-facing labels.", out error);
            foreach (TextMesh label in labels)
            {
                WorldLabelBillboard billboard = label.GetComponent<WorldLabelBillboard>();
                if (billboard == null || billboard.TextLabel != label)
                    return Fail(mode + " label is not billboard-bound: " + label.text, out error);
            }
            return Pass(out error);
        }

        private static bool ValidateCommonLayout(Scene scene, PartySceneBindings bindings, PartyMode mode,
            out string error)
        {
            if (FindNamed(scene, "PlayerSlot_").Length != PartyRoster.Capacity
                || FindNamed(scene, "SlotSpawn_").Length != PartyRoster.Capacity
                || FindNamed(scene, "SlotZone_").Length != PartyRoster.Capacity
                || FindNamed(scene, "PaperDock_").Length != PartyRoster.Capacity)
                return Fail(mode + " requires exact four-slot spawn/zone/dock geometry.", out error);
            if (FindAll<PhysicalBrush>(scene).Length < 1 || FindAll<PhysicalToolStation>(scene).Length < 5)
                return Fail(mode + " requires brush, paint, width, eraser, and rack stations.", out error);
            if (Find(scene, "ReturnToLobby_ResultOnly") == null)
                return Fail(mode + " requires a result-only Host return object.", out error);
            if (bindings.Actions.Length != 3
                || !bindings.Actions.Select(action => action.Action)
                    .ToArray().SequenceEqual(new[] { PartyWorldAction.CarryCanvas, PartyWorldAction.DockCanvas,
                        PartyWorldAction.ReturnToLobby }))
                return Fail(mode + " must bind carry, dock, and return actions exactly.", out error);
            return Pass(out error);
        }

        private static bool ValidatePrivateMode(Scene scene, PartySceneBindings bindings, PartyMode mode,
            out string error)
        {
            GameObject[] shells = FindNamed(scene, "RemotePaperShell_");
            if (shells.Length != PartyRoster.Capacity - 1)
                return Fail(mode + " requires three remote blank paper shells.", out error);
            foreach (GameObject shell in shells)
            {
                if (shell.GetComponentInChildren<CanvasDrawingPresenter>(true) != null
                    || shell.GetComponentInChildren<CanvasSurface>(true) != null
                    || shell.GetComponentInChildren<HandCanvasInteractable>(true) != null
                    || shell.GetComponentInChildren<DrawingController>(true) != null)
                    return Fail(mode + " remote paper shell is not blank: " + shell.name, out error);
            }
            if (bindings.ReferencePresenter == null || bindings.ReferenceSurface == null
                || bindings.ResultRoot == null || bindings.ResultViewPose == null
                || bindings.GalleryRoots == null || bindings.GalleryRoots.Length != PartyRoster.Capacity - 1
                || bindings.GalleryPresenters == null || bindings.GalleryPresenters.Length != PartyRoster.Capacity - 1
                || bindings.GallerySurfaces == null || bindings.GallerySurfaces.Length != PartyRoster.Capacity - 1)
                return Fail(mode + " private reference/result bindings are incomplete.", out error);
            if (bindings.ResultRoot.activeSelf)
                return Fail(mode + " result gallery must be hidden at Scene load.", out error);
            TextMesh[] resultLabels = bindings.ResultRoot.GetComponentsInChildren<TextMesh>(true)
                .Where(label => label.text.StartsWith("PLAYER ", StringComparison.Ordinal)).ToArray();
            if (resultLabels.Length != PartyRoster.Capacity - 1
                || resultLabels.Any(label => label.characterSize < 0.024f))
                return Fail(mode + " result gallery requires three readable player result labels.", out error);
            WorldActionInteractable returnAction = bindings.ResultRoot
                .GetComponentInChildren<WorldActionInteractable>(true);
            if (returnAction == null || returnAction.Action != PartyWorldAction.ReturnToLobby
                || returnAction.GetComponent<Collider>() == null)
                return Fail(mode + " result gallery requires a physical ReturnToLobby control.", out error);
            string expectedReference = mode == PartyMode.MemoryCopy
                ? "FiveSecondObservationPedestal" : "ContinuousReferencePedestal";
            if (Find(scene, expectedReference) == null)
                return Fail(mode + " is missing " + expectedReference + ".", out error);
            if (mode == PartyMode.MemoryCopy && Find(scene, "ContinuousReferencePedestal") != null)
                return Fail("MemoryCopy must not retain continuous reference policy.", out error);
            return Pass(out error);
        }

        private static bool ValidateMural(Scene scene, PartySceneBindings bindings, out string error)
        {
            if (bindings.MuralLayerRoots == null || bindings.MuralLayerRoots.Length != PartyRoster.Capacity
                || FindNamed(scene, "PublicOwnerLayer_").Length != PartyRoster.Capacity)
                return Fail("CoopMural requires exactly four public owner layers.", out error);
            if (Find(scene, "AuthorizedReferenceSurface") != null || Find(scene, "ResultGalleryRoot") != null
                || FindNamed(scene, "RemotePaperShell_").Length != 0)
                return Fail("CoopMural must not contain reference, answer gallery, or private paper shells.", out error);
            GameObject final = Find(scene, "MuralFinalDisplay");
            if (final == null || final.activeSelf)
                return Fail("CoopMural final display must exist and start hidden.", out error);
            return Pass(out error);
        }

        private static T[] FindAll<T>(Scene scene) where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>()
                .Where(item => item != null && item.gameObject.scene == scene).ToArray();
        }

        private static GameObject[] FindNamed(Scene scene, string prefix)
        {
            return FindAll<Transform>(scene).Where(item => item.name.StartsWith(prefix, StringComparison.Ordinal))
                .Select(item => item.gameObject).ToArray();
        }

        private static GameObject Find(Scene scene, string name)
        {
            Transform transform = FindAll<Transform>(scene).FirstOrDefault(item => item.name == name);
            return transform != null ? transform.gameObject : null;
        }

        private static bool Pass(out string error)
        {
            error = string.Empty;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
