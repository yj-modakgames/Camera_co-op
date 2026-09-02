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
    public static partial class RelayQuizOnlineSceneBuilder
    {
        private static void BuildRuntime(Context context, CoreReferences core, PartyLayout party,
            ToolLayout tools, PresentationLayout presentation)
        {
            GameObject runtime = new GameObject("OnlinePartyRuntime");
            runtime.transform.SetParent(core.WorldRoot.transform, false);
            OnlineRelayQuizController online = runtime.AddComponent<OnlineRelayQuizController>();
            PartyWorldController world = runtime.AddComponent<PartyWorldController>();

            for (int slot = 0; slot < party.ReadyPads.Length; slot++) party.ReadyPads[slot].Configure(world, slot, 1f);
            foreach (WorldActionInteractable action in party.Actions) action.Configure(world, action.Action);
            SetField(world, "relayController", online);
            SetField(world, "handPointer", core.HandPointer);
            SetField(world, "modeSelectorRoot", Find(context.Scene, "ModePedestals"));
            SetObjectArray(world, "readyPadsBySlot", party.ReadyPads.Cast<UnityEngine.Object>().ToArray());
            SetObjectArray(world, "worldActions", party.Actions.Cast<UnityEngine.Object>().ToArray());
            SetField(world, "inputModeManager", core.InputModes);
            SetField(world, "playerController", core.PlayerController);
            SetField(world, "localPlayerRoot", core.PlayerRig);
            SetObjectArray(world, "playerZoneBounds", party.ZoneBounds.Cast<UnityEngine.Object>().ToArray());
            SetObjectArray(world, "playerSpawnPointsBySlot", party.Spawns.Cast<UnityEngine.Object>().ToArray());
            SetField(world, "personalCanvas", core.PersonalCanvas);
            SetField(world, "carriedCanvasAnchor", party.CarryCanvasAnchor);
            SetObjectArray(world, "canvasDockAnchorsBySlot", party.Docks.Cast<UnityEngine.Object>().ToArray());
            SetField(world, "canvasDockRadius", 2.25f);
            SetField(world, "physicalPaintTool", tools.PaintTool);
            SetField(world, "drawingController", core.Drawing);
            SetField(world, "toolState", core.ToolState);
            SetField(world, "localWritableCanvasRoot", core.WritableCanvas);
            SetObjectArray(world, "remoteAvatarPresenters", party.RemotePresenters.Cast<UnityEngine.Object>().ToArray());
            SetObjectArray(world, "avatarRootsBySlot", party.AvatarRoots.Cast<UnityEngine.Object>().ToArray());
            SetObjectArray(world, "avatarAnimatorsBySlot", new UnityEngine.Object[4]);
            SetObjectArray(world, "muralLayerRoots", presentation.MuralRoots.Cast<UnityEngine.Object>().ToArray());
            SetObjectArray(world, "muralLayerPresenters", presentation.MuralPresenters.Cast<UnityEngine.Object>().ToArray());
            SetObjectArray(world, "muralLayerSurfaces", presentation.MuralSurfaces.Cast<UnityEngine.Object>().ToArray());

            SetField(online, "inputModeManager", core.InputModes);
            SetField(online, "handInputRouter", core.HandRouter);
            SetField(online, "playerController", core.PlayerController);
            SetField(online, "lobbyPose", presentation.LobbyPose);
            SetField(online, "galleryPose", presentation.GalleryPose);
            SetField(online, "cameraControlPanel", core.CameraPanel);
            SetField(online, "drawingController", core.Drawing);
            SetField(online, "toolState", core.ToolState);
            SetField(online, "workCanvasRoot", core.WritableCanvas);
            SetField(online, "previewPresenter", presentation.PreviewPresenter);
            SetField(online, "previewSurface", presentation.PreviewSurface);
            SetField(online, "relayQuizUI", core.QuizUi);
            SetField(online, "relayQuizGallery", core.Gallery);
            SetField(online, "wordList", core.WordList);
            SetField(online, "partyWorldController", world);

            core.Gallery.Configure(presentation.GalleryRoots, presentation.GalleryPresenters,
                presentation.GallerySurfaces, null);
        }

        private static WorldActionInteractable Action(Context context, Transform parent, string label,
            PartyWorldAction action, Vector3 position, Material material)
        {
            GameObject target = Cube("Action_" + action, parent, position, new Vector3(1.15f, 0.45f, 0.85f), material);
            WorldActionInteractable interactable = target.AddComponent<WorldActionInteractable>();
            SetField(interactable, "action", action);
            TextMesh actionLabel = Label(label.ToUpperInvariant(), target.transform,
                new Vector3(0f, 0.42f, 0f), 0.24f, Color.white, true);
            ConfigureControlLabel(actionLabel, context.PlayerCamera);
            return interactable;
        }

        private static void ConfigureControlLabel(TextMesh label, Camera playerCamera)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (playerCamera == null) throw new ArgumentNullException(nameof(playerCamera));
            WorldLabelBillboard[] billboards = label.GetComponents<WorldLabelBillboard>();
            for (int index = 1; index < billboards.Length; index++)
                UnityEngine.Object.DestroyImmediate(billboards[index]);
            (billboards.Length > 0 ? billboards[0] : label.gameObject.AddComponent<WorldLabelBillboard>())
                .Configure(label, playerCamera);
        }

        private static void ConfigurePresenter(CanvasDrawingPresenter presenter, Context context)
        {
            SetField(presenter, "lineMaterial", context.Line);
            SetObjectArray(presenter, "brushMaterials", new UnityEngine.Object[] { context.Line, context.SoftLine, context.Line });
        }

        private static void RemoveLegacyRelayQuizRuntime(Scene scene)
        {
            GameObject legacyController = Find(scene, "RelayQuizController");
            if (legacyController != null) UnityEngine.Object.DestroyImmediate(legacyController);
            GameObject workPose = Find(scene, "WorkPose");
            if (workPose != null) UnityEngine.Object.DestroyImmediate(workPose);
        }

        private static void ConfigureActionControls(CoreReferences core)
        {
            SetField(core.QuizUi, "useWorldLobbyActions", true);
            string[] relayQuizFields = { "players2Button", "players3Button", "players4Button", "startButton", "readyButton" };
            foreach (string field in relayQuizFields) RemoveSerializedActionControl(core.QuizUi, field);

            Button cameraButton = FieldObject<Button>(core.CameraPanel, "cameraButton");
            Text buttonLabel = FieldObject<Text>(core.CameraPanel, "buttonLabel");
            Text statusLabel = FieldObject<Text>(core.CameraPanel, "statusLabel");
            if (cameraButton == null || buttonLabel == null || statusLabel == null)
                throw new InvalidOperationException("CameraPanel requires CameraToggle, CameraToggleLabel, and CameraStatusLabel.");

            RectTransform panelRect = cameraButton.transform.parent as RectTransform;
            RectTransform buttonRect = cameraButton.transform as RectTransform;
            RectTransform labelRect = buttonLabel.transform as RectTransform;
            RectTransform statusRect = statusLabel.transform as RectTransform;
            if (panelRect == null || panelRect.name != "CameraPanel" || buttonRect == null || labelRect == null || statusRect == null)
                throw new InvalidOperationException("Camera Canvas controls require their approved RectTransform hierarchy.");

            cameraButton.gameObject.SetActive(true);
            panelRect.anchorMin = Vector2.one;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            panelRect.anchoredPosition = new Vector2(-24f, -24f);
            panelRect.sizeDelta = new Vector2(384f, 168f);
            buttonRect.anchorMin = new Vector2(0.5f, 1f);
            buttonRect.anchorMax = new Vector2(0.5f, 1f);
            buttonRect.pivot = new Vector2(0.5f, 1f);
            buttonRect.anchoredPosition = new Vector2(0f, -16f);
            buttonRect.sizeDelta = new Vector2(336f, 56f);
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(320f, 48f);
            statusRect.anchorMin = new Vector2(0.5f, 1f);
            statusRect.anchorMax = new Vector2(0.5f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(0f, -88f);
            statusRect.sizeDelta = new Vector2(344f, 66f);
        }

        private static void HideRelaySetupRoot(RelayQuizUI relayQuizUi)
        {
            GameObject setupRoot = FieldObject<GameObject>(relayQuizUi, "setupRoot");
            if (setupRoot == null) throw new InvalidOperationException("RelayQuizUI requires RelaySetupRoot.");
            setupRoot.SetActive(false);
        }

        private static void RemoveSerializedActionControl(UnityEngine.Object target, string field)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            UnityEngine.Object reference = property.objectReferenceValue;
            property.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (reference is Component component) UnityEngine.Object.DestroyImmediate(component.gameObject);
            else if (reference is GameObject gameObject) UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }
}
