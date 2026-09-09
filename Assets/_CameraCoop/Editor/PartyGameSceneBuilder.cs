using System;
using System.Collections.Generic;
using System.Linq;
using CameraCoop.Party;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CameraCoop.EditorTools
{
    public static partial class PartyGameSceneBuilder
    {
        private const string MaterialFolder = "Assets/_CameraCoop/Materials/RelayQuizOnline";

        private sealed class Palette
        {
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
            BuildAll(true);
        }

        internal static void BuildAll(bool requireIdleEditor)
        {
            if (requireIdleEditor) RequireIdleEditor();
            foreach (PartyMode mode in Enum.GetValues(typeof(PartyMode)))
            {
                if (!PartySceneCatalog.TryGet(mode, out PartySceneDefinition definition))
                    throw new InvalidOperationException("PartySceneCatalog is missing " + mode + ".");
                Build(definition, LoadPalette());
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void Build(PartySceneDefinition definition, Palette palette)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = new GameObject(definition.SceneName + "SceneRoot");
            PartyGameSceneAdapter adapter = root.AddComponent<PartyGameSceneAdapter>();

            BuildEnvironment(root.transform, definition.Mode, palette);
            Transform[] spawns = new Transform[PartyRoster.Capacity];
            BoxCollider[] zones = new BoxCollider[PartyRoster.Capacity];
            Transform[] docks = new Transform[PartyRoster.Capacity];
            GameObject[] avatars = new GameObject[PartyRoster.Capacity];
            RemoteAvatarPresenter[] presenters = new RemoteAvatarPresenter[PartyRoster.Capacity - 1];
            BuildSlots(root.transform, definition.Mode, palette, spawns, zones, docks, avatars, presenters);

            Transform carryAnchor = Marker("CarryAnchor", root.transform, new Vector3(0f, 1.55f, -1.2f), 0f);
            GameObject writablePaper = BuildWritablePaper(root.transform, definition.Mode, palette,
                out CanvasSurface writableSurface, out HandCanvasInteractable writableInteractable);
            BuildRemotePaperShells(root.transform, definition.Mode, palette);

            BuildTools(root.transform, palette, out Transform toolRack, out PhysicalPaintTool paintTool,
                out PhysicalBrush[] brushes, out HandInteractable rackStation);
            WorldActionInteractable[] actions =
            {
                BuildAction(root.transform, "CARRY PAPER", PartyWorldAction.CarryCanvas,
                    new Vector3(-1.4f, 0.45f, -3.3f), palette.Accent),
                BuildAction(root.transform, "DOCK PAPER", PartyWorldAction.DockCanvas,
                    new Vector3(1.4f, 0.45f, -3.3f), palette.Yellow)
            };

            var bindings = new PartySceneBindings
            {
                Mode = definition.Mode,
                SceneRoot = root,
                SlotSpawns = spawns,
                SlotZones = zones,
                SlotDocks = docks,
                CarryAnchor = carryAnchor,
                Actions = actions,
                AvatarRoots = avatars,
                AvatarPresenters = presenters,
                WritablePaperRoot = writablePaper,
                WritableSurface = writableSurface,
                WritableInteractable = writableInteractable,
                ToolRack = toolRack,
                PhysicalPaintTool = paintTool,
                Brushes = brushes,
                ToolStations = new[] { rackStation }
            };

            if (PartyModeCatalog.Get(definition.Mode).UsesSlotDrawingBoards)
                BuildRelayDrawingBoards(root.transform, palette, bindings);

            WorldActionInteractable returnAction = definition.Mode == PartyMode.CoopMural
                ? BuildMural(root.transform, palette, bindings)
                : BuildPrivateModePresentation(root.transform, definition.Mode, palette, bindings);
            bindings.Actions = new[] { actions[0], actions[1], returnAction };

            adapter.Configure(bindings);
            if (!adapter.ValidateBindings(out string error))
                throw new InvalidOperationException(definition.SceneName + " bindings are invalid: " + error);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, definition.ScenePath))
                throw new InvalidOperationException("Unity failed to save " + definition.ScenePath + ".");
            Debug.Log("[PartyGameSceneBuilder] Built " + definition.ScenePath);
        }

        private static void BuildEnvironment(Transform root, PartyMode mode, Palette palette)
        {
            Material accent = mode == PartyMode.RelayCopy ? palette.Red
                : mode == PartyMode.MemoryCopy ? palette.Blue : mode == PartyMode.CoopMural ? palette.Green
                    : mode == PartyMode.PictureTelephone ? palette.Yellow : palette.Accent;
            Cube("Floor", root, new Vector3(0f, -0.12f, 1f), new Vector3(24f, 0.24f, 16f), palette.Floor);
            Cube("NorthBackdrop", root, new Vector3(0f, 3f, 8.8f), new Vector3(24f, 6f, 0.25f), palette.Wall);
            Cube("WestHull", root, new Vector3(-12f, 3f, 1f), new Vector3(0.3f, 6f, 16f), palette.Wall);
            Cube("EastHull", root, new Vector3(12f, 3f, 1f), new Vector3(0.3f, 6f, 16f), palette.Wall);
            Cube("SouthHull", root, new Vector3(0f, 3f, -7f), new Vector3(24f, 6f, 0.3f), palette.Wall);
            Cube("Ceiling", root, new Vector3(0f, 6f, 1f), new Vector3(24f, 0.25f, 16f), palette.Dark);
            for (int index = 0; index < 5; index++)
            {
                float z = -5.6f + index * 3.4f;
                Cube("CeilingRib_" + index, root, new Vector3(0f, 5.65f, z),
                    new Vector3(23.8f, 0.3f, 0.3f), accent);
                Cube("HullStripWest_" + index, root, new Vector3(-11.75f, 3f, z),
                    new Vector3(0.18f, 5.5f, 0.3f), accent);
                Cube("HullStripEast_" + index, root, new Vector3(11.75f, 3f, z),
                    new Vector3(0.18f, 5.5f, 0.3f), accent);
            }
            if (PartyModeCatalog.Get(mode).UsesSlotDrawingBoards)
            {
                for (int divider = 0; divider < 3; divider++)
                    Cube("PrivacyDivider_" + divider, root, new Vector3(-5.35f + divider * 5.35f, 0.45f, -3f),
                        new Vector3(0.15f, 0.9f, 4.2f), palette.Wall);
            }
            string title = PartyModeCatalog.Get(mode).DisplayName + " · INDOOR DRAWING ROOM";
            Label(title, root, new Vector3(0f, 4.75f, 8.55f), 0.56f, Color.white);
            Label("FIST: DRAW   PINCH RELEASE: SELECT   OPEN HAND: REARM", root,
                new Vector3(0f, 4.05f, 8.5f), 0.22f, Color.white);

            GameObject lightObject = new GameObject("GameWorldLight");
            lightObject.transform.SetParent(root, false);
            lightObject.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.9f, 0.94f, 1f);
            light.intensity = 1.25f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.43f, 0.55f);
        }

        private static void BuildRelayDrawingBoards(Transform root, Palette palette, PartySceneBindings bindings)
        {
            bindings.RelayDrawingRoots = new GameObject[PartyRoster.Capacity];
            bindings.RelayDrawingPresenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
            bindings.RelayDrawingSurfaces = new CanvasSurface[PartyRoster.Capacity];
            Material[] colors = { palette.Red, palette.Blue, palette.Green, palette.Yellow };
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                Transform board = Group("RelayDrawingBoard_" + slot, root);
                Vector3 position = bindings.SlotDocks[slot].position;
                GameObject surface = Quad("RelayDrawingSurface_" + slot, board, position,
                    new Vector3(4.8f, 3f, 1f), palette.Paper, Quaternion.identity);
                FrameAt(board, "RelayDrawingFrame_" + slot, position + new Vector3(0f, 0f, 0.1f),
                    new Vector2(5f, 3.2f), colors[slot], Quaternion.identity);
                bindings.RelayDrawingRoots[slot] = board.gameObject;
                bindings.RelayDrawingSurfaces[slot] = surface.AddComponent<CanvasSurface>();
                bindings.RelayDrawingPresenters[slot] = Presenter("RelayDrawingPresenter_" + slot, board, palette);
                board.gameObject.SetActive(false);
            }
        }

        private static void BuildSlots(Transform root, PartyMode mode, Palette palette, Transform[] spawns,
            BoxCollider[] zones, Transform[] docks, GameObject[] avatars, RemoteAvatarPresenter[] presenters)
        {
            Material[] colors = { palette.Red, palette.Blue, palette.Green, palette.Yellow };
            Vector3[] positions = mode == PartyMode.MemoryCopy
                ? new[] { new Vector3(-7f, 0f, -4.5f), new Vector3(7f, 0f, -4.5f), new Vector3(-7f, 0f, 4.5f), new Vector3(7f, 0f, 4.5f) }
                : new[] { new Vector3(-8f, 0f, -4f), new Vector3(-2.7f, 0f, -4f), new Vector3(2.7f, 0f, -4f), new Vector3(8f, 0f, -4f) };

            Transform slotsRoot = Group("PlayerSlots", root);
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                Transform slotRoot = Group("PlayerSlot_" + slot, slotsRoot);
                slotRoot.position = positions[slot];
                GameObject zoneObject = new GameObject("SlotZone_" + slot);
                zoneObject.transform.SetParent(slotRoot, false);
                BoxCollider zone = zoneObject.AddComponent<BoxCollider>();
                zone.isTrigger = true;
                zone.center = new Vector3(0f, 1.4f, 0f);
                zone.size = new Vector3(4.6f, 2.8f, 5f);
                zones[slot] = zone;

                spawns[slot] = Marker("SlotSpawn_" + slot, slotRoot, slotRoot.position + new Vector3(0f, 0f, -0.7f), 0f);
                docks[slot] = Marker("PaperDock_" + slot, slotRoot, slotRoot.position + new Vector3(0f, 1.65f, 1.4f), 180f);
                Cube("SlotMarker_" + slot, slotRoot, slotRoot.position + new Vector3(0f, 0.05f, 0f),
                    new Vector3(3.8f, 0.1f, 3.8f), colors[slot]);
                Label("PLAYER " + (slot + 1), slotRoot, slotRoot.position + new Vector3(0f, 2.8f, 2.15f),
                    0.3f, Color.white);

                AvatarRig rig = AstronautAvatarFactory.Create("Avatar_" + slot, slotRoot, slotRoot.position, 180f,
                    colors[slot]);
                GameObject avatar = rig.Root;
                if (avatar == null)
                {
                    avatar = Capsule("Avatar_" + slot, slotRoot,
                        slotRoot.position + new Vector3(0f, 1f, 0f), new Vector3(0.6f, 1f, 0.6f), colors[slot]);
                    UnityEngine.Object.DestroyImmediate(avatar.GetComponent<Collider>());
                }
                // slot 0(본인)은 로비의 LocalAvatarBody가 PlayerRig를 따라다니므로 여기서는 세우지 않는다.
                if (slot == 0 && rig.IsValid) avatar.SetActive(false);
                avatars[slot] = avatar;
                if (slot > 0)
                {
                    GameObject presenterObject = new GameObject("RemoteAvatarPresenter_" + slot);
                    presenterObject.transform.SetParent(slotRoot, false);
                    RemoteAvatarPresenter presenter = presenterObject.AddComponent<RemoteAvatarPresenter>();
                    SetField(presenter, "avatarRoot", avatar.transform);
                    presenters[slot - 1] = presenter;
                }
            }
        }


    }
}
