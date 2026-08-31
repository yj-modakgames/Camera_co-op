using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CameraCoop.Party;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CameraCoop.EditorTools
{
    public static class RelayQuizOnlineSceneValidator
    {
        private const string MenuPath = "Camera Co-op/RelayQuiz Online/Validate Scene";
        private const int PartyCapacity = 4;

        [MenuItem(MenuPath)]
        public static void ValidateMenu()
        {
            string error;
            if (!TryValidate(out error))
                throw new InvalidOperationException("[RelayQuizOnlineSceneValidator] " + error);
            Debug.Log("[RelayQuizOnlineSceneValidator] PASS: 13 unique world actions, top-right Canvas camera toggle, "
                + "4 slot layouts, 3 remote avatars, 4 mural layers, read-only gallery, private shells, "
                + "runtime references, and build settings are valid.");
        }

        public static bool TryValidate(out string error)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RelayQuizOnlineBuild.ScenePath) == null)
                return Fail("Scene asset is missing: " + RelayQuizOnlineBuild.ScenePath, out error);

            Scene scene = EditorSceneManager.OpenScene(RelayQuizOnlineBuild.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !scene.isLoaded || scene.path != RelayQuizOnlineBuild.ScenePath)
                return Fail("Scene path is invalid after load.", out error);

            if (!ValidateMissingScripts(scene, out error)) return false;

            OnlineRelayQuizController online = FindOne<OnlineRelayQuizController>(scene);
            PartyWorldController party = FindOne<PartyWorldController>(scene);
            if (online == null || party == null)
                return Fail("OnlineRelayQuizController and PartyWorldController are required exactly once.", out error);
            if (FindAll<OnlineRelayQuizController>(scene).Length != 1 || FindAll<PartyWorldController>(scene).Length != 1)
                return Fail("Online/Party controller count must be exactly one.", out error);

            if (!ValidateActions(scene, party, out error)) return false;
            if (!ValidateCanvasCameraToggle(scene, out error)) return false;
            if (!ValidateRelaySetupRoot(scene, out error)) return false;
            if (!ValidateSlotArrays(party, out error)) return false;
            if (!ValidatePhysicalTargets(scene, out error)) return false;
            if (!ValidateLegacyUi(scene, out error)) return false;
            if (!ValidatePrivacy(scene, out error)) return false;
            if (!ValidatePersonalCanvas(party, out error)) return false;
            if (!ValidateNamedLayout(scene, out error)) return false;
            if (!ValidateLobbyTitleMount(scene, out error)) return false;
            if (!ValidateOnlineNavigationPoses(online, scene, out error)) return false;

            string runtimeError;
            if (!party.ValidateRuntimeConfiguration(out runtimeError))
                return Fail("PartyWorldController runtime validation failed: " + runtimeError, out error);
            if (!online.ValidateRuntimeConfiguration(out runtimeError))
                return Fail("OnlineRelayQuizController runtime validation failed: " + runtimeError, out error);

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || scenes[0].path != RelayQuizOnlineBuild.ScenePath || !scenes[0].enabled)
                return Fail("RelayQuizOnline must be the first enabled Build Settings scene.", out error);
            string settingsPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "ProjectSettings", "EditorBuildSettings.asset");
            string settingsText = File.ReadAllText(settingsPath);
            int firstScene = settingsText.IndexOf("  - enabled:", StringComparison.Ordinal);
            int secondScene = firstScene >= 0
                ? settingsText.IndexOf("  - enabled:", firstScene + 1, StringComparison.Ordinal) : -1;
            string firstSceneBlock = firstScene >= 0
                ? settingsText.Substring(firstScene, (secondScene >= 0 ? secondScene : settingsText.Length) - firstScene)
                : string.Empty;
            string expectedGuid = AssetDatabase.AssetPathToGUID(RelayQuizOnlineBuild.ScenePath);
            if (!firstSceneBlock.Contains("path: " + RelayQuizOnlineBuild.ScenePath)
                || !firstSceneBlock.Contains("guid: " + expectedGuid))
                return Fail("Build Settings path/GUID does not match the current RelayQuizOnline Scene asset.", out error);

            error = string.Empty;
            return true;
        }

        private static bool ValidateActions(Scene scene, PartyWorldController party, out string error)
        {
            SerializedProperty actions = Field(party, "worldActions");
            const int expectedActionCount = 13;
            const int expectedControlLabelCount = 21;
            Array values = Enum.GetValues(typeof(PartyWorldAction));
            if (values.Length != expectedActionCount || actions == null || actions.arraySize != expectedActionCount)
                return Fail("worldActions and PartyWorldAction must contain exactly 13 entries.", out error);

            var seen = new HashSet<PartyWorldAction>();
            var registered = new HashSet<WorldActionInteractable>();
            for (int index = 0; index < actions.arraySize; index++)
            {
                WorldActionInteractable action = actions.GetArrayElementAtIndex(index).objectReferenceValue
                    as WorldActionInteractable;
                if (action == null || !seen.Add(action.Action))
                    return Fail("worldActions contains a null or duplicate entry at index " + index + ".", out error);
                registered.Add(action);
                if (action.GetComponent<Collider>() == null || !action.UsesWorldHitPosition)
                    return Fail("World action lacks a collider or world-hit routing: " + action.name, out error);
                if (!TryGetBoundControlLabel(action, out _, out _, out error))
                    return Fail("World action requires a bound billboard TextMesh label: " + action.name, out error);
            }
            foreach (PartyWorldAction value in values)
                if (!seen.Contains(value)) return Fail("Missing PartyWorldAction: " + value, out error);
            WorldActionInteractable[] sceneActions = FindAll<WorldActionInteractable>(scene);
            if (sceneActions.Length != expectedActionCount)
                return Fail("RelayQuizOnline must contain exactly 13 registered WorldActionInteractables.", out error);
            foreach (WorldActionInteractable action in sceneActions)
                if (!registered.Contains(action))
                    return Fail("An unregistered 3D WorldActionInteractable remains: " + action.name, out error);

            TextMesh[] labels = FindAll<TextMesh>(scene);
            WorldLabelBillboard[] billboards = FindAll<WorldLabelBillboard>(scene);
            if (billboards.Length != expectedControlLabelCount)
                return Fail("RelayQuizOnline requires exactly 21 WorldLabelBillboard control labels.", out error);

            var expectedLabels = new HashSet<TextMesh>();
            var expectedBillboards = new HashSet<WorldLabelBillboard>();
            var actionLabels = new HashSet<string>();
            foreach (WorldActionInteractable action in sceneActions)
            {
                if (!TryGetBoundControlLabel(action, out TextMesh label, out WorldLabelBillboard billboard, out error))
                    return false;
                expectedLabels.Add(label);
                expectedBillboards.Add(billboard);
                actionLabels.Add(label.text);
            }
            string[] expectedActionLabels =
            {
                "HOST", "INVITE", "LEAVE", "RELAY COPY", "MEMORY COPY", "COOP MURAL", "START",
                "CARRY PAPER", "DOCK PAPER", "REFRESH", "PREV", "NEXT", "PREVIEW"
            };
            if (!actionLabels.SetEquals(expectedActionLabels))
                return Fail("World actions must keep the 13 approved immediate-action labels.", out error);

            WorldReadyPadInteractable[] pads = FindAll<WorldReadyPadInteractable>(scene);
            if (pads.Length != PartyCapacity)
                return Fail("RelayQuizOnline requires exactly four WorldReadyPadInteractables.", out error);
            var readyLabels = new HashSet<string>();
            foreach (WorldReadyPadInteractable pad in pads)
            {
                if (!TryGetBoundControlLabel(pad, out TextMesh label, out WorldLabelBillboard billboard, out error))
                    return false;
                expectedLabels.Add(label);
                expectedBillboards.Add(billboard);
                readyLabels.Add(label.text);
            }
            if (!readyLabels.SetEquals(new[] { "READY 1", "READY 2", "READY 3", "READY 4" }))
                return Fail("Ready pads must keep the four approved billboard labels.", out error);

            PhysicalToolStation[] labeledStations = FindAll<PhysicalToolStation>(scene)
                .Where(item => item.GetComponentInChildren<TextMesh>(true) != null).ToArray();
            if (labeledStations.Length != PartyCapacity)
                return Fail("RelayQuizOnline requires exactly four labeled PhysicalToolStations.", out error);
            var toolLabels = new HashSet<string>();
            foreach (PhysicalToolStation station in labeledStations)
            {
                if (!TryGetBoundControlLabel(station, out TextMesh label, out WorldLabelBillboard billboard, out error))
                    return false;
                expectedLabels.Add(label);
                expectedBillboards.Add(billboard);
                toolLabels.Add(label.text);
            }
            if (!toolLabels.SetEquals(new[] { "THIN", "MID", "WIDE", "ERASER" }))
                return Fail("Tool stations must keep THIN, MID, WIDE, and ERASER billboards.", out error);

            if (expectedLabels.Count != expectedControlLabelCount || !expectedBillboards.SetEquals(billboards))
                return Fail("Only immediate action, ready-pad, and tool-station labels may use WorldLabelBillboard.", out error);

            string[] staticSigns =
            {
                "4 PLAYER CAMERA CO-OP", "PLAYER 1", "PLAYER 2", "PLAYER 3", "PLAYER 4", "CAMERA STATION",
                "BRUSH · PAINT · WIDTH", "REFERENCE / HOW TO", "Pinch release: select   Fist: draw   Open hand: rearm",
                "COOP MURAL · PUBLIC LAYERS", "GALLERY 1", "GALLERY 2", "GALLERY 3"
            };
            foreach (string text in staticSigns)
            {
                TextMesh[] signs = labels.Where(item => item.text == text).ToArray();
                if (signs.Length != 1 || signs[0].GetComponents<WorldLabelBillboard>().Length != 0)
                    return Fail("Static sign must retain its authored orientation without WorldLabelBillboard: " + text, out error);
            }
            return Pass(out error);
        }

        private static bool TryGetBoundControlLabel(Component control, out TextMesh label,
            out WorldLabelBillboard billboard, out string error)
        {
            TextMesh[] labels = control.GetComponentsInChildren<TextMesh>(true);
            WorldLabelBillboard[] billboards = control.GetComponentsInChildren<WorldLabelBillboard>(true);
            label = labels.Length == 1 ? labels[0] : null;
            billboard = billboards.Length == 1 ? billboards[0] : null;
            if (label == null || billboard == null || billboard.TextLabel != label || billboard.PlayerCamera == null)
            {
                error = "Control must have exactly one bound WorldLabelBillboard: " + control.name;
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool ValidateSlotArrays(PartyWorldController party, out string error)
        {
            string[] fourSlotFields =
            {
                "readyPadsBySlot", "playerZoneBounds", "playerSpawnPointsBySlot",
                "canvasDockAnchorsBySlot", "avatarRootsBySlot", "muralLayerRoots",
                "muralLayerPresenters", "muralLayerSurfaces"
            };
            foreach (string name in fourSlotFields)
            {
                SerializedProperty property = Field(party, name);
                if (property == null || property.arraySize != PartyCapacity)
                    return Fail(name + " must contain exactly four entries.", out error);
                for (int index = 0; index < property.arraySize; index++)
                    if (property.GetArrayElementAtIndex(index).objectReferenceValue == null)
                        return Fail(name + " contains null at slot " + index + ".", out error);
            }

            SerializedProperty remotes = Field(party, "remoteAvatarPresenters");
            if (remotes == null || remotes.arraySize != PartyCapacity - 1)
                return Fail("remoteAvatarPresenters must contain exactly three entries.", out error);
            for (int index = 0; index < remotes.arraySize; index++)
                if (remotes.GetArrayElementAtIndex(index).objectReferenceValue == null)
                    return Fail("remoteAvatarPresenters contains null at index " + index + ".", out error);

            SerializedProperty readyPads = Field(party, "readyPadsBySlot");
            for (int index = 0; index < readyPads.arraySize; index++)
            {
                WorldReadyPadInteractable pad = readyPads.GetArrayElementAtIndex(index).objectReferenceValue
                    as WorldReadyPadInteractable;
                if (pad == null || pad.name != "ReadyPad_" + index || pad.GetComponent<Collider>() == null
                    || !pad.UsesWorldHitPosition)
                    return Fail("Ready pad slot/collider/world-hit configuration is invalid at slot " + index + ".", out error);
            }
            return Pass(out error);
        }

        private static bool ValidateCanvasCameraToggle(Scene scene, out string error)
        {
            CameraControlPanel cameraPanel = FindOne<CameraControlPanel>(scene);
            Button cameraButton = Field(cameraPanel, "cameraButton")?.objectReferenceValue as Button;
            Text buttonLabel = Field(cameraPanel, "buttonLabel")?.objectReferenceValue as Text;
            Text statusLabel = Field(cameraPanel, "statusLabel")?.objectReferenceValue as Text;
            RectTransform panelRect = FindByExactName(scene, "CameraPanel")?.GetComponent<RectTransform>();
            Image buttonImage = cameraButton != null ? cameraButton.GetComponent<Image>() : null;
            if (cameraPanel == null || cameraButton == null || buttonLabel == null || statusLabel == null
                || cameraButton.name != "CameraToggle" || buttonLabel.name != "CameraToggleLabel"
                || statusLabel.name != "CameraStatusLabel" || !cameraButton.gameObject.activeInHierarchy
                || !buttonLabel.gameObject.activeInHierarchy || !statusLabel.gameObject.activeInHierarchy
                || !cameraButton.interactable || buttonImage == null || cameraButton.targetGraphic != buttonImage)
                return Fail("CameraControlPanel must reference the active Canvas CameraToggle and CameraToggleLabel.", out error);
            if (FindByExactName(scene, "Action_CameraStartStop") != null
                || FindAll<TextMesh>(scene).Any(item => item.text == "CAMERA ON / OFF"))
                return Fail("The obsolete world CameraStartStop action and label must not exist.", out error);

            RectTransform buttonRect = cameraButton.transform as RectTransform;
            RectTransform labelRect = buttonLabel.transform as RectTransform;
            RectTransform statusRect = statusLabel.transform as RectTransform;
            if (panelRect == null || buttonRect == null || labelRect == null || statusRect == null
                || !At(panelRect.anchorMin, Vector2.one) || !At(panelRect.anchorMax, Vector2.one)
                || !At(panelRect.pivot, Vector2.one) || !At(panelRect.anchoredPosition, new Vector2(-24f, -24f))
                || !At(panelRect.sizeDelta, new Vector2(384f, 168f)))
                return Fail("CameraPanel must use the approved top-right 384x168 layout at (-24,-24).", out error);
            if (buttonRect.parent != panelRect || statusRect.parent != panelRect
                || !At(buttonRect.anchorMin, new Vector2(0.5f, 1f))
                || !At(buttonRect.anchorMax, new Vector2(0.5f, 1f))
                || !At(buttonRect.pivot, new Vector2(0.5f, 1f))
                || !At(buttonRect.anchoredPosition, new Vector2(0f, -16f))
                || !At(buttonRect.sizeDelta, new Vector2(336f, 56f)))
                return Fail("CameraToggle must stay above the status text in the approved top-right layout.", out error);
            if (labelRect.parent != buttonRect || !At(labelRect.anchorMin, new Vector2(0.5f, 0.5f))
                || !At(labelRect.anchorMax, new Vector2(0.5f, 0.5f)) || !At(labelRect.pivot, new Vector2(0.5f, 0.5f))
                || !At(labelRect.anchoredPosition, Vector2.zero) || !At(labelRect.sizeDelta, new Vector2(320f, 48f)))
                return Fail("CameraToggleLabel must use the approved centered layout inside CameraToggle.", out error);
            if (!At(statusRect.anchorMin, new Vector2(0.5f, 1f))
                || !At(statusRect.anchorMax, new Vector2(0.5f, 1f)) || !At(statusRect.pivot, new Vector2(0.5f, 1f))
                || !At(statusRect.anchoredPosition, new Vector2(0f, -88f))
                || !At(statusRect.sizeDelta, new Vector2(344f, 66f)))
                return Fail("CameraStatusLabel must use the approved layout below CameraToggle.", out error);
            Bounds buttonBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(panelRect, buttonRect);
            Bounds statusBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(panelRect, statusRect);
            if (!panelRect.rect.Contains(new Vector2(buttonBounds.min.x, buttonBounds.min.y))
                || !panelRect.rect.Contains(new Vector2(buttonBounds.max.x, buttonBounds.max.y))
                || !panelRect.rect.Contains(new Vector2(statusBounds.min.x, statusBounds.min.y))
                || !panelRect.rect.Contains(new Vector2(statusBounds.max.x, statusBounds.max.y)))
                return Fail("CameraToggle and CameraStatusLabel must remain inside CameraPanel.", out error);
            if (buttonBounds.min.y <= statusBounds.max.y)
                return Fail("CameraToggle must not overlap CameraStatusLabel.", out error);
            return Pass(out error);
        }

        private static bool ValidatePhysicalTargets(Scene scene, out string error)
        {
            PhysicalPaintTool[] paintTools = FindAll<PhysicalPaintTool>(scene);
            PhysicalBrush[] brushes = FindAll<PhysicalBrush>(scene);
            PhysicalToolStation[] stations = FindAll<PhysicalToolStation>(scene);
            if (paintTools.Length != 1 || brushes.Length != 3 || stations.Length < 9)
                return Fail("Expected one PhysicalPaintTool, three PhysicalBrush objects, and at least nine stations.", out error);
            foreach (HandInteractable interactable in brushes.Cast<HandInteractable>().Concat(stations))
                if (interactable.GetComponent<Collider>() == null || !interactable.UsesWorldHitPosition)
                    return Fail("Physical target lacks collider or world-hit routing: " + interactable.name, out error);
            return Pass(out error);
        }

        private static bool ValidateRelaySetupRoot(Scene scene, out string error)
        {
            GameObject setupRoot = FindByExactName(scene, "RelaySetupRoot");
            if (setupRoot == null)
                return Fail("RelaySetupRoot is required for transient lobby notices.", out error);
            if (setupRoot.activeSelf)
                return Fail("RelaySetupRoot must be inactive at scene load.", out error);

            RelayQuizUI quizUi = FindOne<RelayQuizUI>(scene);
            SerializedProperty setupReference = Field(quizUi, "setupRoot");
            if (quizUi == null || setupReference == null || setupReference.objectReferenceValue != setupRoot)
                return Fail("RelayQuizUI.setupRoot must reference RelaySetupRoot exactly.", out error);
            return Pass(out error);
        }

        private static bool ValidateLegacyUi(Scene scene, out string error)
        {
            CameraControlPanel cameraPanel = FindOne<CameraControlPanel>(scene);
            Button cameraButton = Field(cameraPanel, "cameraButton")?.objectReferenceValue as Button;
            foreach (Button button in FindAll<Button>(scene))
            {
                if (button.gameObject.activeInHierarchy && button != cameraButton)
                    return Fail("Legacy 2D action button remains active: " + HierarchyPath(button.transform), out error);
            }
            RelayQuizController legacy = FindOne<RelayQuizController>(scene);
            if (legacy != null)
                return Fail("Legacy RelayQuizController must be removed from RelayQuizOnline.", out error);
            if (FindByExactName(scene, "WorkPose") != null || Field(FindOne<OnlineRelayQuizController>(scene), "workPose") != null)
                return Fail("Obsolete WorkPose/workPose must not exist in RelayQuizOnline.", out error);

            RelayQuizUI quizUi = FindOne<RelayQuizUI>(scene);
            if (quizUi == null || !BoolField(quizUi, "useWorldLobbyActions"))
                return Fail("RelayQuizUI must use world lobby actions in RelayQuizOnline.", out error);
            string[] worldReplacedQuizFields = { "players2Button", "players3Button", "players4Button", "startButton", "readyButton" };
            foreach (string field in worldReplacedQuizFields)
                if (HasObjectReference(quizUi, field))
                    return Fail("RelayQuizUI still serializes legacy 2D action: " + field, out error);

            if (cameraPanel == null || cameraButton == null || !HasObjectReference(cameraPanel, "buttonLabel"))
                return Fail("Camera controls must use the active Canvas CameraToggle references.", out error);
            return Pass(out error);
        }

        private static bool ValidatePrivacy(Scene scene, out string error)
        {
            GameObject[] shells = FindNamed(scene, "RemotePaperShell_");
            if (shells.Length != 3) return Fail("Exactly three remote blank paper shells are required.", out error);
            foreach (GameObject shell in shells)
            {
                if (shell.GetComponentInChildren<CanvasDrawingPresenter>(true) != null
                    || shell.GetComponentInChildren<CanvasSurface>(true) != null
                    || shell.GetComponentInChildren<HandCanvasInteractable>(true) != null
                    || shell.GetComponentInChildren<DrawingController>(true) != null)
                    return Fail("Remote paper shell contains drawing data/presenter components: " + shell.name, out error);
            }

            GameObject[] gallery = FindNamed(scene, "GalleryFrame_");
            if (gallery.Length != 3) return Fail("Exactly three read-only Gallery frames are required.", out error);
            foreach (GameObject frame in gallery)
            {
                CanvasSurface surface = frame.GetComponentInChildren<CanvasSurface>(true);
                if (surface == null || frame.GetComponentInChildren<CanvasDrawingPresenter>(true) == null)
                    return Fail("Gallery frame requires a presenter and CanvasSurface: " + frame.name, out error);
                if (frame.GetComponentInChildren<HandCanvasInteractable>(true) != null)
                    return Fail("Gallery frame is writable: " + frame.name, out error);
            }
            return Pass(out error);
        }

        private static bool ValidatePersonalCanvas(PartyWorldController party, out string error)
        {
            PersonalCanvasPlacement placement = Field(party, "personalCanvas").objectReferenceValue
                as PersonalCanvasPlacement;
            SerializedProperty docks = Field(party, "canvasDockAnchorsBySlot");
            Transform ownDock = docks.GetArrayElementAtIndex(0).objectReferenceValue as Transform;
            SerializedProperty placementDock = Field(placement, "dockAnchor");
            if (placement == null || ownDock == null || placementDock == null
                || placementDock.objectReferenceValue != ownDock || placement.transform.parent != ownDock
                || placement.State != PersonalCanvasPlacementState.Docked)
                return Fail("Personal canvas must default to Docked at its own slot-0 anchor.", out error);
            if (placement.GetComponentInChildren<HandCanvasInteractable>(true) == null
                || placement.GetComponentInChildren<CanvasSurface>(true) == null)
                return Fail("Personal canvas is not writable.", out error);
            return Pass(out error);
        }

        private static bool ValidateNamedLayout(Scene scene, out string error)
        {
            string[] required =
            {
                "RoomBounds", "PlayerBay_0_Red", "PlayerBay_1_Blue", "PlayerBay_2_Green",
                "PlayerBay_3_Yellow", "PrivacyDivider_0", "PrivacyDivider_1", "PrivacyDivider_2",
                "CentralLobby", "ModePedestals", "CameraStation", "BrushRack", "ReferenceHowToPanel",
                "CoopMuralBoard", "QA_Lobby", "QA_PrivateZone", "QA_Gallery"
            };
            foreach (string name in required)
                if (FindByExactName(scene, name) == null) return Fail("Missing required layout object: " + name, out error);

            if (!At(FindByExactName(scene, "QA_Lobby").transform.position, new Vector3(0f, 2.4f, -7.2f))
                || !At(FindByExactName(scene, "QA_PrivateZone").transform.position, new Vector3(-9f, 1.65f, 2.35f))
                || !At(FindByExactName(scene, "QA_Gallery").transform.position, new Vector3(0f, 2.2f, -4.7f)))
                return Fail("QA viewpoints do not match the approved coordinates.", out error);

            Camera camera = FindOne<Camera>(scene);
            if (camera == null || !At(camera.transform.position, new Vector3(0f, 2.4f, -7.2f)))
                return Fail("PlayerCamera must start at the lobby overview pose.", out error);
            return Pass(out error);
        }

        private static bool ValidateLobbyTitleMount(Scene scene, out string error)
        {
            GameObject desk = FindByExactName(scene, "LobbyDesk");
            TextMesh[] titles = FindAll<TextMesh>(scene)
                .Where(item => item.text == "4 PLAYER CAMERA CO-OP").ToArray();
            if (desk == null || titles.Length != 1)
                return Fail("LobbyDesk and exactly one lobby title are required.", out error);

            TextMesh title = titles[0];
            Renderer deskRenderer = desk.GetComponent<Renderer>();
            Renderer titleRenderer = title.GetComponent<Renderer>();
            if (deskRenderer == null || titleRenderer == null || title.GetComponent<WorldLabelBillboard>() != null)
                return Fail("Lobby title must use the authored LobbyDesk facade without WorldLabelBillboard.", out error);

            Bounds deskBounds = deskRenderer.bounds;
            Bounds titleBounds = titleRenderer.bounds;
            float outwardOffset = deskBounds.min.z - title.transform.position.z;
            if (outwardOffset <= 0f || outwardOffset > 0.03f
                || titleBounds.min.x < deskBounds.min.x || titleBounds.max.x > deskBounds.max.x
                || titleBounds.min.y < deskBounds.min.y || titleBounds.max.y > deskBounds.max.y
                || Vector3.Dot(title.transform.forward, Vector3.forward) <= 0.999f)
                return Fail("Lobby title must fit flush on the south LobbyDesk facade and face the central approach.", out error);

            WorldActionInteractable[] lobbyActions = FindAll<WorldActionInteractable>(scene)
                .Where(item => item.Action == PartyWorldAction.Host || item.Action == PartyWorldAction.Invite
                    || item.Action == PartyWorldAction.Leave).ToArray();
            if (lobbyActions.Length != 3 || lobbyActions.Any(item => item.GetComponent<Collider>() == null)
                || titleBounds.max.y >= lobbyActions.Min(item => item.GetComponent<Collider>().bounds.min.y))
                return Fail("Lobby title must remain below the Host, Invite, and Leave controls.", out error);
            return Pass(out error);
        }

        private static bool ValidateOnlineNavigationPoses(OnlineRelayQuizController online, Scene scene,
            out string error)
        {
            Transform expectedLobby = FindByExactName(scene, "QA_Lobby").transform;
            Transform expectedGallery = FindByExactName(scene, "QA_Gallery").transform;
            SerializedProperty lobbyPose = Field(online, "lobbyPose");
            SerializedProperty galleryPose = Field(online, "galleryPose");
            if (lobbyPose == null || lobbyPose.objectReferenceValue != expectedLobby)
                return Fail("OnlineRelayQuizController.lobbyPose must reference QA_Lobby exactly.", out error);
            if (galleryPose == null || galleryPose.objectReferenceValue != expectedGallery)
                return Fail("OnlineRelayQuizController.galleryPose must reference QA_Gallery exactly.", out error);
            return Pass(out error);
        }

        private static bool ValidateMissingScripts(Scene scene, out string error)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                Component[] components = transform.GetComponents<Component>();
                for (int index = 0; index < components.Length; index++)
                    if (components[index] == null)
                        return Fail("Missing script at " + HierarchyPath(transform), out error);
            }
            return Pass(out error);
        }

        private static SerializedProperty Field(UnityEngine.Object target, string name)
        {
            return target == null ? null : new SerializedObject(target).FindProperty(name);
        }

        private static bool BoolField(UnityEngine.Object target, string name)
        {
            SerializedProperty property = Field(target, name);
            return property != null && property.boolValue;
        }

        private static bool HasObjectReference(UnityEngine.Object target, string name)
        {
            SerializedProperty property = Field(target, name);
            return property != null && property.objectReferenceValue != null;
        }

        private static T FindOne<T>(Scene scene) where T : Component
        {
            return FindAll<T>(scene).FirstOrDefault();
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

        private static GameObject FindByExactName(Scene scene, string name)
        {
            Transform item = FindAll<Transform>(scene).FirstOrDefault(candidate => candidate.name == name);
            return item != null ? item.gameObject : null;
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
            return "/" + path;
        }

        private static bool At(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < 0.0001f; }
        private static bool At(Vector2 a, Vector2 b) { return (a - b).sqrMagnitude < 0.0001f; }
        private static bool Pass(out string error) { error = string.Empty; return true; }
        private static bool Fail(string message, out string error) { error = message; return false; }
    }
}
