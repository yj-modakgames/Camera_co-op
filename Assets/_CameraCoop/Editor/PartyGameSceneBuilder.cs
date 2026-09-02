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
                : mode == PartyMode.MemoryCopy ? palette.Blue : palette.Green;
            Cube("Floor", root, new Vector3(0f, -0.12f, 1f), new Vector3(24f, 0.24f, 16f), palette.Floor);
            Cube("NorthBackdrop", root, new Vector3(0f, 3f, 8.8f), new Vector3(24f, 6f, 0.25f), palette.Wall);
            Cube("WestRail", root, new Vector3(-12f, 0.55f, 1f), new Vector3(0.3f, 1.1f, 16f), accent);
            Cube("EastRail", root, new Vector3(12f, 0.55f, 1f), new Vector3(0.3f, 1.1f, 16f), accent);
            string title = mode == PartyMode.RelayCopy ? "RELAY COPY · PRIVATE HANDOFF"
                : mode == PartyMode.MemoryCopy ? "MEMORY COPY · 5 SECOND LOOK"
                : "COOP MURAL · FOUR PUBLIC LAYERS";
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

                GameObject avatar = Capsule("Avatar_" + slot, slotRoot,
                    slotRoot.position + new Vector3(0f, 1f, 0f), new Vector3(0.6f, 1f, 0.6f), colors[slot]);
                UnityEngine.Object.DestroyImmediate(avatar.GetComponent<Collider>());
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
