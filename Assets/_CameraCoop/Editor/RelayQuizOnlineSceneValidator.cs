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
    public static class RelayQuizOnlineSceneValidator
    {
        [MenuItem("Camera Co-op/RelayQuiz Online/Validate Scene")]
        public static void ValidateMenu()
        {
            if (!TryValidate(out string error))
                throw new InvalidOperationException("[RelayQuizOnlineSceneValidator] " + error);
            Debug.Log("[RelayQuizOnlineSceneValidator] PASS: bootstrap ownership, tutorial lobby, practice wall, "
                + "hidden mode selector, and exact four-Scene build catalog are valid.");
        }

        public static bool TryValidate(out string error)
        {
            SceneSetup[] originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                return TryValidateCore(out error);
            }
            finally
            {
                PartyGameSceneValidator.RestoreOriginalSetupOrLobby(originalSetup);
            }
        }

        private static bool TryValidateCore(out string error)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PartySceneCatalog.LobbyScenePath) == null)
                return Fail("Scene asset is missing: " + PartySceneCatalog.LobbyScenePath, out error);
            Scene scene = EditorSceneManager.OpenScene(PartySceneCatalog.LobbyScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded) return Fail("RelayQuizOnline could not be loaded.", out error);
            if (!ValidateMissingScripts(scene, out error)) return false;

            GameObject runtimeRoot = Root(scene, "RuntimeRoot");
            GameObject lobbyRoot = Root(scene, "LobbyWorldRoot");
            if (runtimeRoot == null || lobbyRoot == null)
                return Fail("RelayQuizOnline requires separate RuntimeRoot and LobbyWorldRoot roots.", out error);
            if (FindAll<PartyGameSceneAdapter>(scene).Length != 0)
                return Fail("Lobby must not contain PartyGameSceneAdapter.", out error);
            if (FindAll<OnlineRelayQuizController>(scene).Length != 1
                || FindAll<PartyWorldController>(scene).Length != 1)
                return Fail("RuntimeRoot must own exactly one OnlineRelayQuizController and PartyWorldController.", out error);
            if (FindAll<Camera>(scene).Length != 1 || FindAll<EventSystem>(scene).Length != 1)
                return Fail("Bootstrap must own exactly one Camera and EventSystem.", out error);

            PartyLobbyScenePort port = FindAll<PartyLobbyScenePort>(scene).SingleOrDefault();
            if (port == null) return Fail("PartyLobbyScenePort is missing.", out error);
            if (!port.ValidateBindings(out string portError))
                return Fail("PartyLobbyScenePort is invalid: " + portError, out error);
            if (port.LobbyWorldRoot != lobbyRoot)
                return Fail("PartyLobbyScenePort must reference LobbyWorldRoot exactly.", out error);

            if (FindNamed(scene, "PracticeEasel_").Length != PartyRoster.Capacity)
                return Fail("Lobby requires exactly four public practice easels.", out error);
            if (FindAll<WorldReadyPadInteractable>(scene).Length != PartyRoster.Capacity)
                return Fail("Lobby requires exactly four ReadyPads.", out error);
            if (FindAll<WorldActionInteractable>(scene).Length != (int)PartyWorldAction.ReturnToLobby)
                return Fail("Lobby requires the exact PartyWorldAction catalog.", out error);

            string[] required =
            {
                "CameraStation", "GestureTutorialStation", "BrushRack", "EraserStation",
                "JumpObstaclePath", "Action_Host", "Action_Invite", "Action_Leave",
                "Action_StartSelectedMode", "ModeSelectorRoot", "PublicPracticeEasels"
            };
            foreach (string name in required)
                if (Find(scene, name) == null) return Fail("Lobby is missing " + name + ".", out error);
            if (Find(scene, "ModeSelectorRoot").activeSelf)
                return Fail("ModeSelectorRoot must be hidden until START opens selection.", out error);

            string[] forbidden =
            {
                "ReadOnlyGallery", "CoopMuralBoard", "ResultGalleryRoot", "MuralFinalDisplay",
                "AuthorizedReferenceSurface", "RemoteBlankPaperShells"
            };
            foreach (string name in forbidden)
                if (Find(scene, name) != null) return Fail("Lobby contains game-only object " + name + ".", out error);

            if (!ValidateBuildOrder(out error)) return false;
            error = string.Empty;
            return true;
        }

        internal static bool ValidateBuildOrder(out string error)
        {
            EditorBuildSettingsScene[] settings = EditorBuildSettings.scenes;
            if (settings.Length < PartySceneCatalog.BuildScenePaths.Count)
                return Fail("Build Settings does not contain the four catalog Scenes.", out error);
            for (int index = 0; index < PartySceneCatalog.BuildScenePaths.Count; index++)
            {
                if (!settings[index].enabled || settings[index].path != PartySceneCatalog.BuildScenePaths[index])
                    return Fail("Build Settings index " + index + " must be "
                        + PartySceneCatalog.BuildScenePaths[index] + ".", out error);
            }
            error = string.Empty;
            return true;
        }

        private static bool ValidateMissingScripts(Scene scene, out string error)
        {
            foreach (Transform transform in FindAll<Transform>(scene))
            {
                if (transform.GetComponents<Component>().Any(component => component == null))
                    return Fail("Missing script at " + transform.name + ".", out error);
            }
            error = string.Empty;
            return true;
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

        private static GameObject Root(Scene scene, string name)
        {
            return scene.GetRootGameObjects().FirstOrDefault(item => item.name == name);
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
