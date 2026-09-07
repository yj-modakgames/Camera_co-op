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
        private static PresentationLayout BuildPresentation(Context context, CoreReferences core)
        {
            var layout = new PresentationLayout();
            // 이 판은 FinalizeLobbySplit에서 로비 낙서판(GestureTutorialBoard)이 된다.
            // 서쪽 벽 판은 yaw -90이라야 Quad 정면(local -Z)이 동쪽(플레이어)을 향하고 좌우가 뒤집히지 않는다.
            Transform reference = Group("ReferenceHowToPanel", core.WorldRoot.transform);
            Quaternion westFacing = Quaternion.Euler(0f, -90f, 0f);
            Cube("ReferencePanelBack", reference, new Vector3(-13.72f, 2f, 0f), new Vector3(0.12f, 3.2f, 4.4f), context.Dark);
            GameObject previewSurfaceObject = Quad("ReferenceSurface", reference, new Vector3(-13.55f, 2f, 0f),
                new Vector3(3.6f, 2.4f, 1f), context.Paper, westFacing);
            layout.PreviewSurface = previewSurfaceObject.AddComponent<CanvasSurface>();
            GameObject previewPresenterObject = new GameObject("ReferenceDrawingPresenter");
            previewPresenterObject.transform.SetParent(reference, false);
            layout.PreviewPresenter = previewPresenterObject.AddComponent<CanvasDrawingPresenter>();
            ConfigurePresenter(layout.PreviewPresenter, context);
            Label("Fist: draw   Open hand: stop   Pinch release: press a button", reference,
                new Vector3(-13.45f, 3.45f, 0f), 0.2f, Color.white, false, westFacing);

            Transform mural = Group("CoopMuralBoard", core.WorldRoot.transform);
            Cube("MuralBack", mural, new Vector3(13.72f, 2f, -0.2f), new Vector3(0.12f, 3.7f, 7.4f), context.Dark);
            GameObject muralSurfaceObject = Quad("CoopMuralSurface", mural, new Vector3(13.57f, 2f, -0.2f),
                new Vector3(6.4f, 3.2f, 1f), context.Paper, Quaternion.Euler(0f, 90f, 0f));
            CanvasSurface muralSurface = muralSurfaceObject.AddComponent<CanvasSurface>();
            layout.MuralRoots = new GameObject[4];
            layout.MuralPresenters = new CanvasDrawingPresenter[4];
            layout.MuralSurfaces = new CanvasSurface[4];
            for (int slot = 0; slot < 4; slot++)
            {
                GameObject layer = new GameObject("MuralPresenterLayer_" + slot);
                layer.transform.SetParent(mural, false);
                CanvasDrawingPresenter presenter = layer.AddComponent<CanvasDrawingPresenter>();
                ConfigurePresenter(presenter, context);
                layout.MuralRoots[slot] = layer;
                layout.MuralPresenters[slot] = presenter;
                layout.MuralSurfaces[slot] = muralSurface;
            }
            Label("COOP MURAL · PUBLIC LAYERS", mural, new Vector3(13.48f, 3.45f, -0.2f), 0.34f,
                Color.white, false, Quaternion.Euler(0f, 90f, 0f));

            Transform galleryRoot = Group("ReadOnlyGallery", core.WorldRoot.transform);
            layout.GalleryRoots = new GameObject[3];
            layout.GalleryPresenters = new CanvasDrawingPresenter[3];
            layout.GallerySurfaces = new CanvasSurface[3];
            float[] xs = { -3f, 0f, 3f };
            for (int index = 0; index < 3; index++)
            {
                GameObject frame = new GameObject("GalleryFrame_" + index);
                frame.transform.SetParent(galleryRoot, false);
                GameObject surfaceObject = Quad("GallerySurface_" + index, frame.transform,
                    new Vector3(xs[index], 2f, -7.72f), new Vector3(2.45f, 1.65f, 1f), context.Paper,
                    Quaternion.Euler(0f, 180f, 0f));
                layout.GallerySurfaces[index] = surfaceObject.AddComponent<CanvasSurface>();
                layout.GalleryPresenters[index] = frame.AddComponent<CanvasDrawingPresenter>();
                ConfigurePresenter(layout.GalleryPresenters[index], context);
                FrameAt(frame.transform, "GalleryBorder_" + index, new Vector3(xs[index], 2f, -7.6f),
                    new Vector2(2.7f, 1.9f), index == 0 ? context.Red : index == 1 ? context.Blue : context.Green,
                    Quaternion.Euler(0f, 180f, 0f));
                Label("GALLERY " + (index + 1), frame.transform, new Vector3(xs[index], 3.1f, -7.45f), 0.24f, Color.white,
                    false, Quaternion.Euler(0f, 180f, 0f));
                layout.GalleryRoots[index] = frame;
            }

            Transform qaRoot = Group("QAViewpoints", core.WorldRoot.transform);
            Transform lobby = Marker("QA_Lobby", qaRoot, new Vector3(0f, 2.4f, -7.2f), 0f);
            Transform privateView = Marker("QA_PrivateZone", qaRoot, new Vector3(-9f, 1.65f, 2.35f), 0f);
            Transform galleryView = Marker("QA_Gallery", qaRoot, new Vector3(0f, 2.2f, -4.7f), 180f);
            layout.LobbyPose = lobby;
            layout.GalleryPose = galleryView;
            lobby.gameObject.SetActive(false);
            privateView.gameObject.SetActive(false);
            galleryView.gameObject.SetActive(false);
            return layout;
        }

        private static void FinalizeLobbySplit(Context context, CoreReferences core, PartyLayout party)
        {
            core.WorldRoot.name = "LobbyWorldRoot";
            GameObject lobbyRoot = core.WorldRoot;

            GameObject modeSelector = Find(context.Scene, "ModePedestals");
            if (modeSelector == null) throw new InvalidOperationException("ModePedestals is required.");
            modeSelector.name = "ModeSelectorRoot";
            modeSelector.SetActive(false);

            GameObject tutorial = Find(context.Scene, "ReferenceHowToPanel");
            if (tutorial == null) throw new InvalidOperationException("ReferenceHowToPanel is required.");
            tutorial.name = "GestureTutorialStation";
            CanvasDrawingPresenter tutorialPresenter = tutorial.GetComponentInChildren<CanvasDrawingPresenter>(true);
            if (tutorialPresenter != null) UnityEngine.Object.DestroyImmediate(tutorialPresenter.gameObject);
            CanvasSurface tutorialBoard = tutorial.GetComponentInChildren<CanvasSurface>(true);
            if (tutorialBoard == null) throw new InvalidOperationException("GestureTutorialBoard surface is required.");
            tutorialBoard.gameObject.name = "GestureTutorialBoard";

            DestroyNamed(context.Scene, "ReadOnlyGallery");
            DestroyNamed(context.Scene, "CoopMuralBoard");
            core.Gallery.Release();
            for (int index = 0; index < 3; index++)
            {
                DestroyNamed(context.Scene, "PrivacyDivider_" + index);
                DestroyNamed(context.Scene, "RemotePaperShell_" + (index + 1));
            }

            BuildLobbyPracticeWall(context, lobbyRoot.transform, out GameObject[] practiceRoots,
                out CanvasDrawingPresenter[] practicePresenters, out CanvasSurface[] practiceSurfaces);
            BuildJumpTutorial(context, lobbyRoot.transform);
            BuildLobbyDecor(lobbyRoot.transform);

            GameObject studio = Find(context.Scene, "Studio");
            if (studio != null) studio.transform.SetParent(lobbyRoot.transform, true);

            GameObject runtimeRoot = new GameObject("RuntimeRoot");
            GameObject onlineRuntime = Find(context.Scene, "OnlinePartyRuntime");
            if (onlineRuntime == null) throw new InvalidOperationException("OnlinePartyRuntime is required.");
            onlineRuntime.transform.SetParent(runtimeRoot.transform, true);

            GameObject[] roots = context.Scene.GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                if (root == lobbyRoot || root == runtimeRoot) continue;
                root.transform.SetParent(runtimeRoot.transform, true);
            }

            BuildScratchBoard(context, core, runtimeRoot.transform, tutorial.transform, tutorialBoard);

            PartyLobbyScenePort lobbyPort = onlineRuntime.GetComponent<PartyLobbyScenePort>();
            if (lobbyPort == null) lobbyPort = onlineRuntime.AddComponent<PartyLobbyScenePort>();
            lobbyPort.Configure(lobbyRoot, party.Spawns, practiceRoots, practicePresenters, practiceSurfaces,
                party.AvatarRoots.Select(item => item.gameObject).ToArray(), party.RemotePresenters);

            OnlineRelayQuizController online = onlineRuntime.GetComponent<OnlineRelayQuizController>();
            SetField(online, "lobbyScenePort", lobbyPort);
            SetField(online, "workCanvasRoot", null);
            SetField(online, "previewPresenter", null);
            SetField(online, "previewSurface", null);
        }

        // 낙서판은 WorkCanvas와 완전히 따로 논다 — 자기 HandPointer·DrawingController를 갖고,
        // 네트워크 동기화도 PartyWorldController의 rebind도 받지 않는다.
        private static void BuildScratchBoard(Context context, CoreReferences core, Transform runtimeRoot,
            Transform station, CanvasSurface board)
        {
            var drawingObject = new GameObject("ScratchBoardDrawing");
            drawingObject.transform.SetParent(runtimeRoot, false);
            HandPointer pointer = drawingObject.AddComponent<HandPointer>();
            SetField(pointer, "inputSource", HandPointerInputSource.HandRouter);
            SetField(pointer, "inputModeManager", core.InputModes);
            SetField(pointer, "canvasSurface", board);
            SetField(pointer, "toolState", core.ToolState);
            SetField(pointer, "aimCamera", core.PlayerCamera);
            DrawingController drawing = drawingObject.AddComponent<DrawingController>();
            SetField(drawing, "handPointer", pointer);
            SetField(drawing, "toolState", core.ToolState);
            SetField(drawing, "canvasSurface", board);
            SetField(drawing, "lineMaterial", context.Line);
            // C키는 작업 캔버스 전용이다. 낙서판까지 같이 지워지면 사고가 된다 — 여긴 CLEAR 버튼만.
            SetField(drawing, "clearKey", UnityEngine.InputSystem.Key.None);

            HandCanvasInteractable interactable = board.gameObject.AddComponent<HandCanvasInteractable>();
            SetField(interactable, "canvasSurface", board);
            SetField(interactable, "handPointer", pointer);
            SetObjectArray(core.HandRouter, "extraCanvases", new UnityEngine.Object[] { interactable });

            GameObject clear = PedestalButton("ScratchBoardClear", station, new Vector3(-12.9f, 0f, 2.5f),
                "SM_Gen_Prop_Button_02", context.Yellow, context.Dark);
            ScratchBoardClearButton clearButton = clear.AddComponent<ScratchBoardClearButton>();
            SetField(clearButton, "drawingController", drawing);
            TextMesh clearLabel = Label("CLEAR", clear.transform, new Vector3(0f, 0.44f, 0f), 0.22f, Color.white, true);
            ConfigureControlLabel(clearLabel, core.PlayerCamera);

            ZoneSign(station, "Practice", "PRACTICE BOARD · DRAW HERE", new Vector3(-13.4f, 3.95f, 0f), -90f,
                context.Accent);
        }

        private static void BuildLobbyPracticeWall(Context context, Transform parent, out GameObject[] layerRoots,
            out CanvasDrawingPresenter[] presenters, out CanvasSurface[] surfaces)
        {
            Transform root = Group("PublicPracticeEasels", parent);
            ZoneSign(root, "PracticeWall", "PUBLIC PRACTICE WALL", new Vector3(0f, 5.1f, 7.4f), 0f, context.Dark);
            layerRoots = new GameObject[PartyRoster.Capacity];
            presenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
            surfaces = new CanvasSurface[PartyRoster.Capacity];
            Material[] colors = { context.Red, context.Blue, context.Green, context.Yellow };
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                // 자리(x -9/-3/3/9)와 같은 x에 세워야 어느 이젤이 누구 것인지 읽힌다.
                float x = -9f + slot * 6f;
                GameObject easel = new GameObject("PracticeEasel_" + slot);
                easel.transform.SetParent(root, false);
                GameObject surfaceObject = Quad("PracticeSurface_" + slot, easel.transform,
                    new Vector3(x, 2.25f, 7.6f), new Vector3(4.2f, 2.6f, 1f), context.Paper,
                    Quaternion.Euler(0f, 180f, 0f));
                surfaces[slot] = surfaceObject.AddComponent<CanvasSurface>();
                presenters[slot] = easel.AddComponent<CanvasDrawingPresenter>();
                ConfigurePresenter(presenters[slot], context);
                FrameAt(easel.transform, "PracticeFrame_" + slot, new Vector3(x, 2.25f, 7.47f),
                    new Vector2(4.45f, 2.85f), colors[slot], Quaternion.Euler(0f, 180f, 0f));
                layerRoots[slot] = easel;
            }
        }

        private static void BuildJumpTutorial(Context context, Transform parent)
        {
            Transform root = Group("JumpObstaclePath", parent);
            ZoneSign(root, "Jump", "JUMP PRACTICE", new Vector3(8.2f, 3.4f, 0.6f), 0f, context.Accent);
            string[] crates = { "SM_Gen_Prop_Crate_01", "SM_Gen_Prop_Crate_02", "SM_Gen_Prop_Crate_03" };
            for (int index = 0; index < 6; index++)
            {
                float height = 0.3f + index % 3 * 0.25f;
                var ground = new Vector3(5.6f + index * 1.15f, 0f, -0.4f + index * 0.6f);
                GameObject step = SyntyProp(SyntyPropFolder, crates[index % crates.Length], "JumpStep_" + index,
                    root, ground, 0.9f, PropFit.Footprint);
                if (step == null)
                {
                    Cube("JumpStep_" + index, root, ground + Vector3.up * (height * 0.5f),
                        new Vector3(0.9f, height, 0.9f), index % 2 == 0 ? context.Accent : context.Blue);
                    continue;
                }
                // 상자를 바닥에 묻어 밟는 면만 3단계로 만든다. 높이로 정규화하면 0.3 m 상자가 되어 발판이 못 된다.
                float sink = WorldRenderBounds(step).size.y - height;
                if (sink > 0f) step.transform.position -= Vector3.up * sink;
                EnsureCollider(step);
            }
        }

        // 구역 사이 빈 벽을 채워 방의 크기를 읽히게 한다. Kenney 모델에는 collider가 없어 통행을 막지 않는다.
        private static void BuildLobbyDecor(Transform parent)
        {
            Transform root = Group("LobbyDecor", parent);
            KenneyProp("pottedPlant", "Decor_Plant_00", root, new Vector3(-13f, 0f, 7f), 1.6f);
            KenneyProp("pottedPlant", "Decor_Plant_01", root, new Vector3(13f, 0f, 7f), 1.6f);
            KenneyProp("plantSmall1", "Decor_Plant_02", root, new Vector3(13.2f, 0f, 2f), 0.7f);
            KenneyProp("lampSquareFloor", "Decor_Lamp_00", root, new Vector3(-13.2f, 0f, 3.4f), 2.2f);
            KenneyProp("bench", "Decor_Bench_00", root, new Vector3(-6.4f, 0f, -6.6f), 0.9f, PropFit.Height, 180f);
            KenneyProp("coatRackStanding", "Decor_CoatRack_00", root, new Vector3(6.4f, 0f, -6.9f), 1.9f);
            KenneyProp("trashcan", "Decor_Trashcan_00", root, new Vector3(-10.6f, 0f, -0.6f), 0.8f);
        }

        private static void DestroyNamed(Scene scene, string name)
        {
            GameObject item = Find(scene, name);
            if (item != null) UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
