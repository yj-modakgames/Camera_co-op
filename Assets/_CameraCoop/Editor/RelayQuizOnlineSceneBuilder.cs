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
        private const string SourceScenePath = "Assets/_CameraCoop/Scenes/RelayQuiz.unity";
        private const string MaterialFolder = "Assets/_CameraCoop/Materials/RelayQuizOnline";
        private const string MenuPath = "Camera Co-op/RelayQuiz Online/Build Playable Scene";
        private static bool buildingAll;

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

        public static void BuildAll()
        {
            if (buildingAll) throw new InvalidOperationException("BuildAll is already running.");
            buildingAll = true;
            try
            {
                BuildMenu();
                PartyGameSceneBuilder.BuildAll(false);
                ConfigureBuildSettings();
                EditorSceneManager.OpenScene(PartySceneCatalog.LobbyScenePath, OpenSceneMode.Single);
                Debug.Log("[RelayQuizOnlineSceneBuilder] BuildAll created the four catalog Scenes.");
            }
            finally
            {
                buildingAll = false;
            }
        }

        [MenuItem(MenuPath)]
        public static void BuildMenu()
        {
            if (!buildingAll)
            {
                BuildAll();
                return;
            }
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
            FinalizeLobbySplit(context, core, party);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, RelayQuizOnlineBuild.ScenePath))
                throw new InvalidOperationException("Unity failed to save RelayQuizOnline Scene.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
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

        private static void ConfigureBuildSettings()
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            var catalogPaths = new HashSet<string>(PartySceneCatalog.BuildScenePaths, StringComparer.Ordinal);
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (string path in PartySceneCatalog.BuildScenePaths)
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid))
                    throw new FileNotFoundException("Catalog Scene is missing.", path);
                scenes.Add(new EditorBuildSettingsScene(new GUID(guid), true));
            }
            scenes.AddRange(existing.Where(item => !catalogPaths.Contains(item.path)));
            EditorBuildSettings.scenes = scenes.ToArray();
            EditorApplication.ExecuteMenuItem("File/Save Project");
        }


    }
}
