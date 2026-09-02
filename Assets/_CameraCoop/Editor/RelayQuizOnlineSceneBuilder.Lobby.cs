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


    }
}
