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
    public static class RelayQuizOnlineSceneBuilder
    {
        private const string SourceScenePath = "Assets/_CameraCoop/Scenes/RelayQuiz.unity";
        private const string MaterialFolder = "Assets/_CameraCoop/Materials/RelayQuizOnline";
        private const string MenuPath = "Camera Co-op/RelayQuiz Online/Build Playable Scene";

        private static readonly Color Red = Hex("E85D5D");
        private static readonly Color Blue = Hex("4E8FE8");
        private static readonly Color Green = Hex("54B878");
        private static readonly Color Yellow = Hex("E2B84B");
        private static readonly Color Dark = Hex("171B24");
        private static readonly Color Wall = Hex("2A3140");
        private static readonly Color Floor = Hex("11151D");
        private static readonly Color Paper = Hex("F5F2E9");
        private static readonly Color Accent = Hex("77D6C5");

        private sealed class Context
        {
            public Scene Scene;
            public Camera PlayerCamera;
            public Material Red;
            public Material Blue;
            public Material Green;
            public Material Yellow;
            public Material Dark;
            public Material Wall;
            public Material Floor;
            public Material Paper;
            public Material Accent;
            public Material Line;
            public Material SoftLine;
        }

        [MenuItem(MenuPath)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
                throw new InvalidOperationException("RelayQuizOnline Scene build requires an idle Editor in EditMode.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
                throw new FileNotFoundException("Source Scene is missing.", SourceScenePath);

            BuildMaterials();
            Scene sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(RelayQuizOnlineBuild.ScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(SourceScenePath, RelayQuizOnlineBuild.ScenePath))
                    throw new InvalidOperationException("Could not copy the RelayQuiz source Scene.");
            }
            else if (!EditorSceneManager.SaveScene(sourceScene, RelayQuizOnlineBuild.ScenePath, true))
            {
                throw new InvalidOperationException("Could not refresh the RelayQuizOnline Scene from its source.");
            }
            AssetDatabase.ImportAsset(RelayQuizOnlineBuild.ScenePath, ImportAssetOptions.ForceSynchronousImport);

            Scene scene = EditorSceneManager.OpenScene(RelayQuizOnlineBuild.ScenePath, OpenSceneMode.Single);
            var context = LoadContext(scene);
            BuildRoom(context);
            CoreReferences core = PrepareCore(context);
            context.PlayerCamera = core.PlayerCamera;
            PartyLayout party = BuildPartyLayout(context, core);
            ToolLayout tools = BuildPhysicalTools(context, core);
            PresentationLayout presentation = BuildPresentation(context, core);
            BuildRuntime(context, core, party, tools, presentation);
            HideRelaySetupRoot(core.QuizUi);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, RelayQuizOnlineBuild.ScenePath))
                throw new InvalidOperationException("Unity failed to save RelayQuizOnline Scene.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[RelayQuizOnlineSceneBuilder] Built and saved " + RelayQuizOnlineBuild.ScenePath);
        }

        private sealed class CoreReferences
        {
            public GameObject WorldRoot;
            public Transform PlayerRig;
            public Camera PlayerCamera;
            public PlayerController PlayerController;
            public InputModeManager InputModes;
            public HandInputRouter HandRouter;
            public HandPointer HandPointer;
            public DrawingController Drawing;
            public ToolState ToolState;
            public CameraControlPanel CameraPanel;
            public RelayQuizUI QuizUi;
            public RelayQuizGallery Gallery;
            public RelayQuizWordList WordList;
            public PersonalCanvasPlacement PersonalCanvas;
            public GameObject WritableCanvas;
            public CanvasSurface WritableSurface;
        }

        private sealed class PartyLayout
        {
            public WorldReadyPadInteractable[] ReadyPads;
            public WorldActionInteractable[] Actions;
            public BoxCollider[] ZoneBounds;
            public Transform[] Spawns;
            public Transform[] Docks;
            public Transform CarryCanvasAnchor;
            public Transform LeftBrushAnchor;
            public Transform RightBrushAnchor;
            public Transform[] AvatarRoots;
            public RemoteAvatarPresenter[] RemotePresenters;
        }

        private sealed class ToolLayout
        {
            public PhysicalPaintTool PaintTool;
            public PhysicalBrush[] Brushes;
        }

        private sealed class PresentationLayout
        {
            public CanvasDrawingPresenter PreviewPresenter;
            public CanvasSurface PreviewSurface;
            public GameObject[] GalleryRoots;
            public CanvasDrawingPresenter[] GalleryPresenters;
            public CanvasSurface[] GallerySurfaces;
            public GameObject[] MuralRoots;
            public CanvasDrawingPresenter[] MuralPresenters;
            public CanvasSurface[] MuralSurfaces;
            public Transform LobbyPose;
            public Transform GalleryPose;
        }

        private static void BuildMaterials()
        {
            EnsureFolder(MaterialFolder);
            CreateOrReplaceMaterial("PlayerRed", Red, 0.15f);
            CreateOrReplaceMaterial("PlayerBlue", Blue, 0.15f);
            CreateOrReplaceMaterial("PlayerGreen", Green, 0.15f);
            CreateOrReplaceMaterial("PlayerYellow", Yellow, 0.15f);
            CreateOrReplaceMaterial("RoomDark", Dark, 0.1f);
            CreateOrReplaceMaterial("RoomWall", Wall, 0.15f);
            CreateOrReplaceMaterial("RoomFloor", Floor, 0.05f);
            CreateOrReplaceMaterial("WhitePaper", Paper, 0.05f);
            CreateOrReplaceMaterial("ActionAccent", Accent, 0.25f);
            AssetDatabase.SaveAssets();
        }

        private static Context LoadContext(Scene scene)
        {
            return new Context
            {
                Scene = scene,
                Red = Material("PlayerRed"), Blue = Material("PlayerBlue"), Green = Material("PlayerGreen"),
                Yellow = Material("PlayerYellow"), Dark = Material("RoomDark"), Wall = Material("RoomWall"),
                Floor = Material("RoomFloor"), Paper = Material("WhitePaper"), Accent = Material("ActionAccent"),
                Line = AssetDatabase.LoadAssetAtPath<Material>("Assets/_CameraCoop/Materials/StrokeLine.mat"),
                SoftLine = AssetDatabase.LoadAssetAtPath<Material>("Assets/_CameraCoop/Materials/StrokeSoft.mat")
            };
        }

        private static void BuildRoom(Context context)
        {
            GameObject studio = Find(context.Scene, "Studio");
            DestroyChildren(studio.transform);

            GameObject bounds = new GameObject("RoomBounds");
            bounds.transform.SetParent(studio.transform, false);
            BoxCollider boundsCollider = bounds.AddComponent<BoxCollider>();
            boundsCollider.isTrigger = true;
            boundsCollider.center = new Vector3(0f, 2f, 0f);
            boundsCollider.size = new Vector3(28f, 4f, 16f);

            Cube("Floor", studio.transform, new Vector3(0f, -0.1f, 0f), new Vector3(28f, 0.2f, 16f), context.Floor);
            Cube("NorthWall", studio.transform, new Vector3(0f, 4f, 8f), new Vector3(28f, 8f, 0.25f), context.Wall);
            Cube("SouthWall", studio.transform, new Vector3(0f, 4f, -8f), new Vector3(28f, 8f, 0.25f), context.Wall);
            Cube("WestWall", studio.transform, new Vector3(-14f, 4f, 0f), new Vector3(0.25f, 8f, 16f), context.Wall);
            Cube("EastWall", studio.transform, new Vector3(14f, 4f, 0f), new Vector3(0.25f, 8f, 16f), context.Wall);

            GameObject lightObject = new GameObject("RoomKeyLight");
            lightObject.transform.SetParent(studio.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            Light key = lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.9f, 0.94f, 1f);
            key.intensity = 1.2f;
            GameObject fillObject = new GameObject("RoomFillLight");
            fillObject.transform.SetParent(studio.transform, false);
            fillObject.transform.position = new Vector3(0f, 3.4f, 0f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 24f;
            fill.intensity = 6f;
            fill.color = new Color(0.72f, 0.82f, 1f);
        }

        private static CoreReferences PrepareCore(Context context)
        {
            var core = new CoreReferences();
            core.WorldRoot = new GameObject("RelayQuizOnlineWorld");
            core.PlayerRig = Find(context.Scene, "PlayerRig").transform;
            core.PlayerCamera = Find(context.Scene, "PlayerCamera").GetComponent<Camera>();
            core.PlayerController = core.PlayerRig.GetComponent<PlayerController>();
            core.InputModes = Find(context.Scene, "InputRoot").GetComponent<InputModeManager>();
            core.HandRouter = Find(context.Scene, "InputRoot").GetComponent<HandInputRouter>();
            core.HandPointer = Find(context.Scene, "DrawingRoot").GetComponent<HandPointer>();
            core.Drawing = Find(context.Scene, "DrawingRoot").GetComponent<DrawingController>();
            core.ToolState = Find(context.Scene, "PalettePanel").GetComponent<ToolState>();
            core.CameraPanel = Find(context.Scene, "CameraControls").GetComponent<CameraControlPanel>();
            core.QuizUi = Find(context.Scene, "RelayQuizUI").GetComponent<RelayQuizUI>();
            core.Gallery = Find(context.Scene, "RelayQuizGallery").GetComponent<RelayQuizGallery>();
            core.WordList = AssetDatabase.LoadAssetAtPath<RelayQuizWordList>("Assets/_CameraCoop/Data/RelayQuizWords.asset");

            core.PlayerRig.position = new Vector3(0f, 0f, -7.2f);
            core.PlayerRig.rotation = Quaternion.identity;
            core.PlayerCamera.transform.localPosition = new Vector3(0f, 2.4f, 0f);
            core.PlayerCamera.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            core.PlayerCamera.clearFlags = CameraClearFlags.SolidColor;
            core.PlayerCamera.backgroundColor = new Color(0.035f, 0.045f, 0.065f);
            core.PlayerCamera.fieldOfView = 76f;
            SetField(core.PlayerController, "minXZ", new Vector2(-13.5f, -7.5f));
            SetField(core.PlayerController, "maxXZ", new Vector2(13.5f, 7.5f));

            RemoveLegacyRelayQuizRuntime(context.Scene);
            ConfigureActionControls(core);

            GameObject localPaper = Find(context.Scene, "WorkCanvasAnchor");
            localPaper.name = "LocalWritablePaper";
            foreach (Transform child in localPaper.transform.Cast<Transform>().ToArray())
                if (child.name != "WorkCanvas") UnityEngine.Object.DestroyImmediate(child.gameObject);
            core.WritableCanvas = Find(context.Scene, "WorkCanvas");
            core.WritableCanvas.transform.localPosition = Vector3.zero;
            core.WritableCanvas.transform.localRotation = Quaternion.identity;
            core.WritableCanvas.transform.localScale = new Vector3(4.4f, 2.8f, 1f);
            AssignMaterial(core.WritableCanvas, context.Paper);
            core.WritableSurface = core.WritableCanvas.GetComponent<CanvasSurface>();
            core.PersonalCanvas = localPaper.GetComponent<PersonalCanvasPlacement>();
            if (core.PersonalCanvas == null) core.PersonalCanvas = localPaper.AddComponent<PersonalCanvasPlacement>();
            SetField(core.PersonalCanvas, "handInputRouter", core.HandRouter);
            SetField(core.PersonalCanvas, "handPointer", core.HandPointer);
            SetField(core.PersonalCanvas, "drawingController", core.Drawing);
            SetField(core.PersonalCanvas, "carriedLocalPosition", new Vector3(0f, 0.25f, 0.85f));
            SetField(core.PersonalCanvas, "carriedLocalEulerAngles", new Vector3(8f, 180f, 0f));
            Frame(localPaper.transform, "PersonalPaperFrame", new Vector2(4.65f, 3.05f), context.Red);

            Color[] palette = { Red, Blue, Green, Yellow, new Color(0.08f, 0.09f, 0.12f), Paper };
            SetColorArray(core.ToolState, "palette", palette);
            return core;
        }

        private static PartyLayout BuildPartyLayout(Context context, CoreReferences core)
        {
            var layout = new PartyLayout
            {
                ReadyPads = new WorldReadyPadInteractable[4],
                ZoneBounds = new BoxCollider[4], Spawns = new Transform[4], Docks = new Transform[4],
                AvatarRoots = new Transform[4], RemotePresenters = new RemoteAvatarPresenter[3]
            };
            Transform baysRoot = Group("NorthPlayerBays", core.WorldRoot.transform);
            Material[] colors = { context.Red, context.Blue, context.Green, context.Yellow };
            string[] colorNames = { "Red", "Blue", "Green", "Yellow" };
            float[] xs = { -9f, -3f, 3f, 9f };
            for (int slot = 0; slot < 4; slot++)
            {
                Transform bay = Group("PlayerBay_" + slot + "_" + colorNames[slot], baysRoot);
                Cube("BayBack_" + slot, bay, new Vector3(xs[slot], 1.8f, 7.55f), new Vector3(5.4f, 3.4f, 0.2f), context.Dark);
                Cube("BayHeader_" + slot, bay, new Vector3(xs[slot], 3.2f, 7.35f), new Vector3(5.1f, 0.18f, 0.18f), colors[slot]);
                Label("PLAYER " + (slot + 1), bay, new Vector3(xs[slot], 3.12f, 7.2f), 0.34f, Color.white);

                GameObject zone = new GameObject("ZoneBounds_" + slot);
                zone.transform.SetParent(bay, false);
                zone.transform.position = new Vector3(xs[slot], 1.5f, 4.35f);
                BoxCollider zoneCollider = zone.AddComponent<BoxCollider>();
                zoneCollider.isTrigger = true;
                zoneCollider.size = new Vector3(5.25f, 3f, 6.2f);
                layout.ZoneBounds[slot] = zoneCollider;

                layout.Spawns[slot] = Marker("SpawnPoint_" + slot, bay, new Vector3(xs[slot], 0f, 0.7f), 0f);
                layout.Docks[slot] = Marker("CanvasDock_" + slot, bay, new Vector3(xs[slot], 1.65f, 6.95f), 180f);

                GameObject ready = Cylinder("ReadyPad_" + slot, bay, new Vector3(xs[slot], 0.08f, 1.55f),
                    new Vector3(1.25f, 0.08f, 1.25f), colors[slot]);
                WorldReadyPadInteractable pad = ready.AddComponent<WorldReadyPadInteractable>();
                layout.ReadyPads[slot] = pad;
                TextMesh readyLabel = Label("READY " + (slot + 1), ready.transform,
                    new Vector3(0f, 0.15f, 0f), 0.22f, Color.white, true);
                ConfigureControlLabel(readyLabel, core.PlayerCamera);

                Transform avatarRoot = Group("AvatarRoot_" + slot, bay);
                avatarRoot.position = new Vector3(xs[slot], 0f, 0.7f);
                GameObject avatar = Capsule("AvatarBody_" + slot, avatarRoot, new Vector3(0f, 1f, 0f),
                    new Vector3(0.65f, 1f, 0.65f), colors[slot]);
                Collider avatarCollider = avatar.GetComponent<Collider>();
                if (avatarCollider != null) UnityEngine.Object.DestroyImmediate(avatarCollider);
                layout.AvatarRoots[slot] = avatarRoot;

                if (slot > 0)
                {
                    GameObject shell = new GameObject("RemotePaperShell_" + slot);
                    shell.transform.SetParent(layout.Docks[slot], false);
                    Quad("BlankPaper", shell.transform, Vector3.zero, new Vector3(4.4f, 2.8f, 1f), context.Paper);
                    Frame(shell.transform, "RemoteFrame_" + slot, new Vector2(4.65f, 3.05f), colors[slot]);
                    GameObject presenterObject = new GameObject("RemoteAvatarPresenter_" + slot);
                    presenterObject.transform.SetParent(bay, false);
                    layout.RemotePresenters[slot - 1] = presenterObject.AddComponent<RemoteAvatarPresenter>();
                    SetField(layout.RemotePresenters[slot - 1], "avatarRoot", avatarRoot);
                }
            }

            for (int divider = 0; divider < 3; divider++)
            {
                float x = -6f + divider * 6f;
                Cube("PrivacyDivider_" + divider, baysRoot, new Vector3(x, 1.8f, 4.9f),
                    new Vector3(0.22f, 3.6f, 6.1f), context.Dark);
            }

            core.PersonalCanvas.Configure("EditorLocalPlayer", Marker("CanvasCarryAnchor", core.PlayerRig,
                new Vector3(0f, 1.55f, 0.65f), 0f), layout.Docks[0], 2.25f);
            layout.CarryCanvasAnchor = FieldObject<Transform>(core.PersonalCanvas, "avatarAnchor");
            layout.LeftBrushAnchor = Marker("LeftBrushCarryAnchor", core.PlayerRig, new Vector3(-0.35f, 1.35f, 0.7f), 0f);
            layout.RightBrushAnchor = Marker("RightBrushCarryAnchor", core.PlayerRig, new Vector3(0.35f, 1.35f, 0.7f), 0f);

            Transform lobby = Group("CentralLobby", core.WorldRoot.transform);
            Cube("LobbyDesk", lobby, new Vector3(0f, 0.45f, -0.65f), new Vector3(8.5f, 0.9f, 1.2f), context.Dark);
            Label("4 PLAYER CAMERA CO-OP", lobby, new Vector3(0f, 0.45f, -1.256f), 0.62f, Color.white);
            Transform modes = Group("ModePedestals", lobby);

            var actionList = new List<WorldActionInteractable>();
            actionList.Add(Action(context, lobby, "Host", PartyWorldAction.Host, new Vector3(-3f, 1.05f, -0.65f), context.Red));
            actionList.Add(Action(context, lobby, "Invite", PartyWorldAction.Invite, new Vector3(0f, 1.05f, -0.65f), context.Blue));
            actionList.Add(Action(context, lobby, "Leave", PartyWorldAction.Leave, new Vector3(3f, 1.05f, -0.65f), context.Wall));
            actionList.Add(Action(context, modes, "Relay Copy", PartyWorldAction.SelectRelayCopy, new Vector3(-3.2f, 0.55f, -2.35f), context.Red));
            actionList.Add(Action(context, modes, "Memory Copy", PartyWorldAction.SelectMemoryCopy, new Vector3(0f, 0.55f, -2.35f), context.Blue));
            actionList.Add(Action(context, modes, "Coop Mural", PartyWorldAction.SelectCoopMural, new Vector3(3.2f, 0.55f, -2.35f), context.Green));
            actionList.Add(Action(context, lobby, "START", PartyWorldAction.StartSelectedMode, new Vector3(0f, 0.55f, -4f), context.Yellow));
            actionList.Add(Action(context, baysRoot, "Carry Paper", PartyWorldAction.CarryCanvas, new Vector3(-10.4f, 0.55f, 4.35f), context.Red));
            actionList.Add(Action(context, baysRoot, "Dock Paper", PartyWorldAction.DockCanvas, new Vector3(-7.6f, 0.55f, 4.35f), context.Accent));

            Transform cameraStation = Group("CameraStation", core.WorldRoot.transform);
            Cube("CameraConsole", cameraStation, new Vector3(8.7f, 0.55f, -4.9f), new Vector3(8f, 1.1f, 1.25f), context.Dark);
            Label("CAMERA STATION", cameraStation, new Vector3(8.7f, 1.35f, -4.25f), 0.36f, Color.white);
            actionList.Add(Action(context, cameraStation, "Refresh", PartyWorldAction.CameraRefresh, new Vector3(6f, 1.2f, -4.9f), context.Accent));
            actionList.Add(Action(context, cameraStation, "Prev", PartyWorldAction.CameraPrevious, new Vector3(7.35f, 1.2f, -4.9f), context.Blue));
            actionList.Add(Action(context, cameraStation, "Next", PartyWorldAction.CameraNext, new Vector3(8.7f, 1.2f, -4.9f), context.Blue));
            actionList.Add(Action(context, cameraStation, "Preview", PartyWorldAction.CameraPreview, new Vector3(10.05f, 1.2f, -4.9f), context.Green));
            layout.Actions = actionList.OrderBy(item => (int)item.Action).ToArray();
            return layout;
        }

        private static ToolLayout BuildPhysicalTools(Context context, CoreReferences core)
        {
            Transform root = Group("PhysicalTools", core.WorldRoot.transform);
            PhysicalPaintTool paintTool = root.gameObject.AddComponent<PhysicalPaintTool>();
            SetField(paintTool, "toolState", core.ToolState);
            SetField(paintTool, "localPlayerId", "EditorLocalPlayer");
            SetField(paintTool, "maxInteractionDistance", 12f);

            GameObject rack = Cube("BrushRack", root, new Vector3(-10.6f, 0.6f, -4.85f),
                new Vector3(3.6f, 1.2f, 1.2f), context.Dark);
            PhysicalToolStation rackStation = rack.AddComponent<PhysicalToolStation>();
            rackStation.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Rack, 0);
            SetField(paintTool, "rack", rack.transform);
            SetField(paintTool, "dockAnchor", rack.transform);

            var brushes = new PhysicalBrush[3];
            for (int index = 0; index < brushes.Length; index++)
            {
                GameObject brush = Cylinder("PhysicalBrush_" + index, root,
                    new Vector3(-11.5f + index * 0.9f, 1.55f, -4.85f), new Vector3(0.12f, 0.65f, 0.12f),
                    index == 0 ? context.Red : index == 1 ? context.Blue : context.Green);
                brush.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                brushes[index] = brush.AddComponent<PhysicalBrush>();
                SetField(brushes[index], "paintTool", paintTool);
            }

            Material[] paints = { context.Red, context.Blue, context.Green, context.Yellow };
            for (int index = 0; index < paints.Length; index++)
            {
                GameObject pot = Cylinder("PaintPot_" + index, root, new Vector3(-12.2f + index * 1.05f, 0.25f, -2.9f),
                    new Vector3(0.65f, 0.25f, 0.65f), paints[index]);
                PhysicalToolStation station = pot.AddComponent<PhysicalToolStation>();
                station.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Paint, index);
            }
            for (int index = 0; index < 3; index++)
            {
                GameObject width = Cube("WidthControl_" + index, root, new Vector3(-8f + index * 1.15f, 0.35f, -2.9f),
                    new Vector3(0.85f, 0.7f, 0.85f), context.Accent);
                PhysicalToolStation station = width.AddComponent<PhysicalToolStation>();
                station.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Width, index);
                TextMesh widthLabel = Label(index == 0 ? "THIN" : index == 1 ? "MID" : "WIDE", width.transform,
                    new Vector3(0f, 0.58f, 0f), 0.18f, Color.white, true);
                ConfigureControlLabel(widthLabel, core.PlayerCamera);
            }
            GameObject eraser = Cube("EraserStation", root, new Vector3(-4.5f, 0.35f, -2.9f),
                new Vector3(1.25f, 0.7f, 0.85f), context.Paper);
            PhysicalToolStation eraserStation = eraser.AddComponent<PhysicalToolStation>();
            eraserStation.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Eraser, 0);
            TextMesh eraserLabel = Label("ERASER", eraser.transform, new Vector3(0f, 0.58f, 0f), 0.18f, Dark, true);
            ConfigureControlLabel(eraserLabel, core.PlayerCamera);
            Label("BRUSH · PAINT · WIDTH", root, new Vector3(-8.3f, 1.85f, -4.15f), 0.34f, Color.white);

            SetField(paintTool, "leftCarryAnchor", Find(context.Scene, "LeftBrushCarryAnchor").transform);
            SetField(paintTool, "rightCarryAnchor", Find(context.Scene, "RightBrushCarryAnchor").transform);
            SetObjectArray(paintTool, "brushReferences", brushes.Cast<UnityEngine.Object>().ToArray());
            return new ToolLayout { PaintTool = paintTool, Brushes = brushes };
        }

        private static PresentationLayout BuildPresentation(Context context, CoreReferences core)
        {
            var layout = new PresentationLayout();
            Transform reference = Group("ReferenceHowToPanel", core.WorldRoot.transform);
            Cube("ReferencePanelBack", reference, new Vector3(-13.72f, 2f, -0.5f), new Vector3(0.12f, 3.2f, 6.2f), context.Dark);
            Label("REFERENCE / HOW TO", reference, new Vector3(-13.5f, 3.25f, -0.5f), 0.34f, Color.white, false,
                Quaternion.Euler(0f, 90f, 0f));
            GameObject previewSurfaceObject = Quad("ReferenceSurface", reference, new Vector3(-13.58f, 2f, -0.8f),
                new Vector3(4.2f, 2.5f, 1f), context.Paper, Quaternion.Euler(0f, 90f, 0f));
            layout.PreviewSurface = previewSurfaceObject.AddComponent<CanvasSurface>();
            GameObject previewPresenterObject = new GameObject("ReferenceDrawingPresenter");
            previewPresenterObject.transform.SetParent(reference, false);
            layout.PreviewPresenter = previewPresenterObject.AddComponent<CanvasDrawingPresenter>();
            ConfigurePresenter(layout.PreviewPresenter, context);
            Label("Pinch release: select   Fist: draw   Open hand: rearm", reference,
                new Vector3(-13.48f, 0.6f, -0.5f), 0.18f, Color.white, false, Quaternion.Euler(0f, 90f, 0f));

            Transform mural = Group("CoopMuralBoard", core.WorldRoot.transform);
            Cube("MuralBack", mural, new Vector3(13.72f, 2f, -0.2f), new Vector3(0.12f, 3.7f, 7.4f), context.Dark);
            GameObject muralSurfaceObject = Quad("CoopMuralSurface", mural, new Vector3(13.57f, 2f, -0.2f),
                new Vector3(6.4f, 3.2f, 1f), context.Paper, Quaternion.Euler(0f, -90f, 0f));
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
                Color.white, false, Quaternion.Euler(0f, -90f, 0f));

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

        private static void ConfigureBuildSettings()
        {
            string currentGuid = AssetDatabase.AssetPathToGUID(RelayQuizOnlineBuild.ScenePath);
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            EditorBuildSettingsScene target = existing.FirstOrDefault(item =>
                item.path == RelayQuizOnlineBuild.ScenePath && item.guid.ToString() == currentGuid);
            if (target == null)
            {
                EditorBuildSettings.scenes = existing
                    .Where(item => item.path != RelayQuizOnlineBuild.ScenePath).ToArray();
                target = new EditorBuildSettingsScene(new GUID(currentGuid), true);
            }
            target.enabled = true;
            var scenes = new List<EditorBuildSettingsScene> { target };
            scenes.AddRange(EditorBuildSettings.scenes.Where(item => item.path != RelayQuizOnlineBuild.ScenePath));
            EditorBuildSettings.scenes = scenes.ToArray();
            EditorApplication.ExecuteMenuItem("File/Save Project");
        }

        private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            return Primitive(name, PrimitiveType.Cube, parent, position, scale, Quaternion.identity, material);
        }

        private static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            return Primitive(name, PrimitiveType.Cylinder, parent, position, scale, Quaternion.identity, material);
        }

        private static GameObject Capsule(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            return Primitive(name, PrimitiveType.Capsule, parent, position, scale, Quaternion.identity, material);
        }

        private static GameObject Quad(string name, Transform parent, Vector3 position, Vector3 scale, Material material,
            Quaternion? rotation = null)
        {
            return Primitive(name, PrimitiveType.Quad, parent, position, scale, rotation ?? Quaternion.identity, material);
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position,
            Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            item.transform.rotation = rotation;
            item.transform.localScale = scale;
            AssignMaterial(item, material);
            return item;
        }

        private static Transform Group(string name, Transform parent)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            return item.transform;
        }

        private static Transform Marker(string name, Transform parent, Vector3 worldPosition, float yaw)
        {
            Transform marker = Group(name, parent);
            marker.position = worldPosition;
            marker.rotation = Quaternion.Euler(0f, yaw, 0f);
            return marker;
        }

        private static TextMesh Label(string text, Transform parent, Vector3 position, float size, Color color,
            bool local = false, Quaternion? rotation = null)
        {
            GameObject labelObject = new GameObject("Label_" + text.Replace(' ', '_').Replace('/', '_'));
            labelObject.transform.SetParent(parent, false);
            if (local) labelObject.transform.localPosition = position;
            else labelObject.transform.position = position;
            labelObject.transform.rotation = rotation ?? Quaternion.identity;
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = size * 0.08f;
            label.fontSize = 64;
            label.color = color;
            return label;
        }

        private static void Frame(Transform parent, string name, Vector2 size, Material material)
        {
            FrameAt(parent, name, Vector3.zero, size, material, Quaternion.identity, true);
        }

        private static void FrameAt(Transform parent, string name, Vector3 position, Vector2 size, Material material,
            Quaternion rotation, bool local = false)
        {
            Transform frame = Group(name, parent);
            if (local) frame.localPosition = position; else frame.position = position;
            frame.rotation = rotation;
            float edge = 0.12f;
            Cube("Top", frame, frame.TransformPoint(new Vector3(0f, size.y * 0.5f, 0.04f)),
                new Vector3(size.x, edge, edge), material);
            Cube("Bottom", frame, frame.TransformPoint(new Vector3(0f, -size.y * 0.5f, 0.04f)),
                new Vector3(size.x, edge, edge), material);
            Cube("Left", frame, frame.TransformPoint(new Vector3(-size.x * 0.5f, 0f, 0.04f)),
                new Vector3(edge, size.y, edge), material);
            Cube("Right", frame, frame.TransformPoint(new Vector3(size.x * 0.5f, 0f, 0.04f)),
                new Vector3(edge, size.y, edge), material);
        }

        private static void AssignMaterial(GameObject item, Material material)
        {
            MeshRenderer renderer = item.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private static GameObject Find(Scene scene, string exactName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                if (item.name == exactName) return item.gameObject;
            return null;
        }

        private static void DestroyChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
                UnityEngine.Object.DestroyImmediate(parent.GetChild(index).gameObject);
        }

        private static void SetField(UnityEngine.Object target, string name, object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new MissingFieldException(target.GetType().Name, name);
            if (value is UnityEngine.Object unityObject) property.objectReferenceValue = unityObject;
            else if (value is string stringValue) property.stringValue = stringValue;
            else if (value is bool boolValue) property.boolValue = boolValue;
            else if (value is int intValue) property.intValue = intValue;
            else if (value is float floatValue) property.floatValue = floatValue;
            else if (value is Vector2 vector2) property.vector2Value = vector2;
            else if (value is Vector3 vector3) property.vector3Value = vector3;
            else if (value is Enum enumValue) property.enumValueIndex = Convert.ToInt32(enumValue);
            else throw new ArgumentException("Unsupported serialized field type for " + name + ".");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(UnityEngine.Object target, string name, UnityEngine.Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null || !property.isArray) throw new MissingFieldException(target.GetType().Name, name);
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetColorArray(UnityEngine.Object target, string name, Color[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null || !property.isArray) throw new MissingFieldException(target.GetType().Name, name);
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).colorValue = values[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T FieldObject<T>(UnityEngine.Object target, string name) where T : UnityEngine.Object
        {
            SerializedProperty property = new SerializedObject(target).FindProperty(name);
            return property != null ? property.objectReferenceValue as T : null;
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static void CreateOrReplaceMaterial(string name, Color color, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (name == "WhitePaper" && material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(material);
        }

        private static Material Material(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");
        }

        private static Color Hex(string rgb)
        {
            Color color;
            if (!ColorUtility.TryParseHtmlString("#" + rgb, out color)) throw new FormatException(rgb);
            return color;
        }
    }
}
