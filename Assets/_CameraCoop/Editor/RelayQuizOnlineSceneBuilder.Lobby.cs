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
        private const float CounterZ = -0.9f;
        private const float CounterHeight = 1.0f;

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
                // 바닥 러그가 각 자리를 표시한다. 예전 BayBack cube는 공용 연습 이젤을 통째로 삼켜 지웠다.
                var rugGround = new Vector3(xs[slot], 0.01f, 4f);
                GameObject rug = KenneyProp("rugSquare", "BayRug_" + slot, bay, rugGround, 5.4f, PropFit.Footprint);
                if (rug != null) PaintAll(rug, colors[slot]);
                else Cube("BayRug_" + slot, bay, rugGround, new Vector3(5.4f, 0.02f, 5.4f), colors[slot]);
                FrameAt(bay, "BayEaselFrame_" + slot, new Vector3(xs[slot], 1.65f, 7.08f), new Vector2(4.65f, 3.05f),
                    colors[slot], Quaternion.Euler(0f, 180f, 0f));
                ZoneSign(bay, "Player" + slot, "PLAYER " + (slot + 1) + " · " + colorNames[slot].ToUpperInvariant(),
                    new Vector3(xs[slot], 4.25f, 7.3f), 0f, colors[slot]);

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

            core.PersonalCanvas.Configure("EditorLocalPlayer", LocalMarker("CanvasCarryAnchor", core.PlayerRig,
                new Vector3(0f, 1.55f, 0.65f), 0f), layout.Docks[0], 2.25f);
            layout.CarryCanvasAnchor = FieldObject<Transform>(core.PersonalCanvas, "avatarAnchor");
            layout.LeftBrushAnchor = LocalMarker("LeftBrushCarryAnchor", core.PlayerRig, new Vector3(-0.35f, 1.35f, 0.7f), 0f);
            layout.RightBrushAnchor = LocalMarker("RightBrushCarryAnchor", core.PlayerRig, new Vector3(0.35f, 1.35f, 0.7f), 0f);

            var actionList = new List<WorldActionInteractable>();
            Transform lobby = Group("CentralLobby", core.WorldRoot.transform);
            Transform modes = Group("ModePedestals", lobby);
            BuildLobbyCounter(context, lobby, out float counterTop, out float counterFront, out float counterHalfWidth);

            actionList.Add(Action(context, lobby, "Host", PartyWorldAction.Host,
                new Vector3(-2.2f, counterTop, CounterZ), context.Red));
            actionList.Add(Action(context, lobby, "Invite", PartyWorldAction.Invite,
                new Vector3(0f, counterTop, CounterZ), context.Blue));
            actionList.Add(Action(context, lobby, "Leave", PartyWorldAction.Leave,
                new Vector3(2.2f, counterTop, CounterZ), context.Wall));
            actionList.Add(Action(context, modes, "Relay Copy", PartyWorldAction.SelectRelayCopy,
                new Vector3(-3.2f, 0f, -2.7f), context.Red));
            actionList.Add(Action(context, modes, "Memory Copy", PartyWorldAction.SelectMemoryCopy,
                new Vector3(0f, 0f, -2.7f), context.Blue));
            actionList.Add(Action(context, modes, "Coop Mural", PartyWorldAction.SelectCoopMural,
                new Vector3(3.2f, 0f, -2.7f), context.Green));
            actionList.Add(Action(context, lobby, "START", PartyWorldAction.StartSelectedMode,
                new Vector3(0f, 0f, -4.4f), context.Yellow));
            actionList.Add(Action(context, baysRoot, "Carry Paper", PartyWorldAction.CarryCanvas,
                new Vector3(-11.8f, 0f, 5.4f), context.Red));
            actionList.Add(Action(context, baysRoot, "Dock Paper", PartyWorldAction.DockCanvas,
                new Vector3(-11.8f, 0f, 4f), context.Accent));

            ZoneSign(lobby, "Lobby", "LOBBY · HOST / INVITE / START", new Vector3(0f, 3.1f, CounterZ - 0.4f), 0f,
                context.Dark);
            // 모드 표지판은 ModeSelectorRoot 안에 둔다. 선택이 닫혀 있을 때 혼자 남으면 안내가 거짓말이 된다.
            ZoneSign(modes, "Mode", "PICK A MODE, THEN START", new Vector3(0f, 3.2f, -3.6f), 0f, context.Dark);
            BuildLobbyCounterProps(context, lobby, counterTop, counterFront, counterHalfWidth);

            actionList.AddRange(BuildCameraStation(context, core));
            layout.Actions = actionList.OrderBy(item => (int)item.Action).ToArray();
            return layout;
        }

        // kitchenBar 조각을 실측 폭만큼 이어 붙인다. 상수 간격은 에셋이 바뀌면 틈이 생긴다.
        private static void BuildLobbyCounter(Context context, Transform lobby, out float counterTop,
            out float counterFront, out float counterHalfWidth)
        {
            GameObject probe = KenneyProp("kitchenBar", "LobbyCounter_0", lobby,
                new Vector3(0f, 0f, CounterZ), CounterHeight);
            if (probe == null)
            {
                Cube("LobbyCounter_0", lobby, new Vector3(0f, CounterHeight * 0.5f, CounterZ),
                    new Vector3(7.2f, CounterHeight, 0.9f), context.Wood);
                counterTop = CounterHeight;
                counterFront = CounterZ - 0.45f;
                counterHalfWidth = 3.6f;
            }
            else
            {
                Bounds segment = WorldRenderBounds(probe);
                float width = segment.size.x;
                for (int index = 1; index <= 6; index++)
                {
                    float offset = (index + 1) / 2 * width * (index % 2 == 0 ? 1f : -1f);
                    KenneyProp("kitchenBar", "LobbyCounter_" + index, lobby,
                        new Vector3(offset, 0f, CounterZ), CounterHeight);
                }
                counterTop = segment.max.y;
                counterFront = segment.min.z;
                counterHalfWidth = width * 3.5f;
            }
            // LobbyDesk는 카운터 앞판이다. 방 이름이 여기 붙고, 카운터 몸통은 Kenney 모델이 맡는다.
            Cube("LobbyDesk", lobby, new Vector3(0f, 0.45f, counterFront - 0.05f),
                new Vector3(counterHalfWidth * 2f - 0.2f, 0.8f, 0.06f), context.Dark);
            Label("4 PLAYER CAMERA CO-OP", lobby, new Vector3(0f, 0.45f, counterFront - 0.086f), 0.62f, Color.white);
        }

        private static void BuildLobbyCounterProps(Context context, Transform lobby, float counterTop,
            float counterFront, float counterHalfWidth)
        {
            Transform props = Group("LobbyDeskProps", lobby);
            for (int index = 0; index < 3; index++)
            {
                float x = -2.2f + index * 2.2f;
                KenneyProp("stoolBar", "LobbyStool_" + index, props, new Vector3(x, 0f, counterFront - 0.85f), 0.85f);
            }
            KenneyProp("books", "LobbyBooks", props,
                new Vector3(-counterHalfWidth + 0.6f, counterTop, CounterZ), 0.24f, PropFit.Height, 20f);
            SyntyProp(SyntyPropFolder, "SM_Gen_Prop_Mug_01", "LobbyMug", props,
                new Vector3(counterHalfWidth - 0.6f, counterTop, CounterZ), 0.14f);
        }

        private static WorldActionInteractable[] BuildCameraStation(Context context, CoreReferences core)
        {
            Transform station = Group("CameraStation", core.WorldRoot.transform);
            float deskTop = 0.78f;
            for (int index = 0; index < 2; index++)
            {
                float z = -5.4f + index * 1.5f;
                GameObject desk = KenneyProp("desk", "CameraDesk_" + index, station,
                    new Vector3(12.4f, 0f, z), deskTop, PropFit.Height, -90f);
                if (desk == null)
                    Cube("CameraDesk_" + index, station, new Vector3(12.4f, deskTop * 0.5f, z),
                        new Vector3(1.2f, deskTop, 1.5f), context.Dark);
            }
            SyntyProp(SyntyPropFolder, "SM_Gen_Prop_Screen_01", "CameraMonitor", station,
                new Vector3(12.85f, deskTop, -4.65f), 0.7f, PropFit.Height, -90f);
            KenneyProp("chairDesk", "CameraChair", station, new Vector3(11.4f, 0f, -2.6f), 0.9f, PropFit.Height, -90f);
            ZoneSign(station, "Camera", "CAMERA · REFRESH / PREV / NEXT / PREVIEW",
                new Vector3(12.9f, 3.3f, -4.2f), 90f, context.Dark);

            var actions = new WorldActionInteractable[4];
            PartyWorldAction[] catalog =
            {
                PartyWorldAction.CameraRefresh, PartyWorldAction.CameraPrevious,
                PartyWorldAction.CameraNext, PartyWorldAction.CameraPreview
            };
            string[] labels = { "Refresh", "Prev", "Next", "Preview" };
            Material[] tints = { context.Accent, context.Blue, context.Blue, context.Green };
            for (int index = 0; index < actions.Length; index++)
            {
                actions[index] = Action(context, station, labels[index], catalog[index],
                    new Vector3(12.2f, deskTop, -5.8f + index * 0.8f), tints[index]);
            }
            return actions;
        }

        private static ToolLayout BuildPhysicalTools(Context context, CoreReferences core)
        {
            Transform root = Group("PhysicalTools", core.WorldRoot.transform);
            PhysicalPaintTool paintTool = root.gameObject.AddComponent<PhysicalPaintTool>();
            SetField(paintTool, "toolState", core.ToolState);
            SetField(paintTool, "localPlayerId", "EditorLocalPlayer");
            SetField(paintTool, "maxInteractionDistance", 12f);

            const float benchTop = 0.8f;
            float[] benchZ = { -5.8f, -3.4f };
            for (int index = 0; index < benchZ.Length; index++)
            {
                GameObject bench = KenneyProp("table", "SupplyBench_" + index, root,
                    new Vector3(-13f, 0f, benchZ[index]), benchTop, PropFit.Height, 90f);
                if (bench == null)
                    Cube("SupplyBench_" + index, root, new Vector3(-13f, benchTop * 0.5f, benchZ[index]),
                        new Vector3(1.1f, benchTop, 2f), context.Wood);
            }

            // 붓 세 자루가 얹히는 받침. 손 조준 표적이 되도록 collider를 상판보다 크게 잡되,
            // 붓보다 벽 쪽(서쪽)에 둔다 — 앞을 가리면 가장 가까운 hit만 보는 라우터가 붓을 못 집는다.
            GameObject rack = Cube("BrushRack", root, new Vector3(-13.02f, benchTop + 0.06f, benchZ[0]),
                new Vector3(0.44f, 0.12f, 2f), context.Wood);
            rack.GetComponent<BoxCollider>().size = new Vector3(1f, 2.5f, 1.025f);
            PhysicalToolStation rackStation = rack.AddComponent<PhysicalToolStation>();
            rackStation.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Rack, 0);
            SetField(paintTool, "rack", rack.transform);
            // dockAnchor를 지정하면 붓 셋이 그 한 점에 겹쳐 docking된다. 각자 놓인 자리로 돌아가게 비워 둔다.

            var brushes = new PhysicalBrush[3];
            for (int index = 0; index < brushes.Length; index++)
            {
                // 간격은 붓 길이(0.9)보다 넓게. 0.22 m 잡기 collider끼리 겹치면 조준이 갈린다.
                var position = new Vector3(-12.62f, benchTop + 0.12f, benchZ[0] - 0.95f + index * 0.95f);
                Quaternion lying = Quaternion.Euler(0f, 90f, 90f);
                GameObject brush = PropInstance(BrushPropPaths[index], "PhysicalBrush_" + index, root,
                    position, BrushLength, lying);
                if (brush == null)
                {
                    brush = Cylinder("PhysicalBrush_" + index, root, position, new Vector3(0.12f, 0.65f, 0.12f),
                        index == 0 ? context.Red : index == 1 ? context.Blue : context.Green);
                    brush.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                }
                EnsureCollider(brush);
                brushes[index] = brush.AddComponent<PhysicalBrush>();
                SetField(brushes[index], "paintTool", paintTool);
            }

            // 물감 받침. 상호작용은 아래 PaintPot이 담당하는 장식이므로 collider를 지운다 —
            // 남겨 두면 PaintPot_3 앞을 가려 조준이 아예 안 된다 (사용자 보고 2026-09-04).
            StripColliders(PropInstance(PalettePropPath, "PaintPalette", root,
                new Vector3(-13.2f, benchTop + 0.05f, -2.0f), PaletteWidth, Quaternion.Euler(0f, 20f, 0f)));

            Material[] paints = { context.Red, context.Blue, context.Green, context.Yellow };
            string[] potModels =
                { "SM_Gen_Prop_Pot_01", "SM_Gen_Prop_Pot_02", "SM_Gen_Prop_Pot_03", "SM_Gen_Prop_Pot_04" };
            for (int index = 0; index < paints.Length; index++)
            {
                // 높이로 맞추면 납작한 항아리(Pot_04)가 폭 0.51 m로 부풀어 옆 통까지 넘본다. 폭 기준으로 맞춘다.
                GameObject pot = ControlBody("PaintPot_" + index, root,
                    new Vector3(-12.85f, benchTop, benchZ[1] - 0.75f + index * 0.5f), new Vector3(0.34f, 0.34f, 0.34f),
                    SyntyPropFolder + potModels[index] + ".prefab", paints[index], PropFit.Footprint);
                PhysicalToolStation station = pot.AddComponent<PhysicalToolStation>();
                station.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Paint, index);
            }

            string[] widthNames = { "THIN", "MID", "WIDE" };
            float[] widthHeights = { 0.45f, 0.62f, 0.8f };
            for (int index = 0; index < 3; index++)
            {
                GameObject width = ControlBody("WidthControl_" + index, root,
                    new Vector3(-11.5f, 0f, -5.8f + index * 1.2f),
                    new Vector3(0.55f, widthHeights[index], 0.55f),
                    SyntyPropFolder + "SM_Gen_Prop_Plinth_02.prefab", context.Accent);
                PhysicalToolStation station = width.AddComponent<PhysicalToolStation>();
                station.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Width, index);
                TextMesh widthLabel = Label(widthNames[index], width.transform,
                    new Vector3(0f, widthHeights[index] * 0.5f + 0.22f, 0f), 0.18f, Color.white, true);
                ConfigureControlLabel(widthLabel, core.PlayerCamera);
            }

            GameObject eraser = ControlBody("EraserStation", root, new Vector3(-11.5f, 0f, -2.2f),
                new Vector3(0.7f, 0.7f, 0.7f), SyntyPropFolder + "SM_Gen_Prop_Crate_01.prefab", context.Paper);
            PhysicalToolStation eraserStation = eraser.AddComponent<PhysicalToolStation>();
            eraserStation.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Eraser, 0);
            TextMesh eraserLabel = Label("ERASER", eraser.transform, new Vector3(0f, 0.57f, 0f), 0.18f, Dark, true);
            ConfigureControlLabel(eraserLabel, core.PlayerCamera);
            // 선택한 색을 되돌려 주는 칩. 물감통을 눌러도 바뀐 걸 볼 데가 없었다 (사용자 보고 2026-09-04).
            GameObject chipStand = Cube("CurrentInkStand", root, new Vector3(-12.85f, benchTop + 0.15f, -2.45f),
                new Vector3(0.1f, 0.3f, 0.1f), context.Dark);
            UnityEngine.Object.DestroyImmediate(chipStand.GetComponent<Collider>());
            GameObject chip = Cube("CurrentInkChip", root, new Vector3(-12.85f, benchTop + 0.36f, -2.45f),
                new Vector3(0.26f, 0.12f, 0.26f), context.Paper);
            UnityEngine.Object.DestroyImmediate(chip.GetComponent<Collider>());
            ToolStatusIndicator indicator = chip.AddComponent<ToolStatusIndicator>();
            SetField(indicator, "toolState", core.ToolState);
            SetField(indicator, "chip", chip.GetComponent<MeshRenderer>());
            TextMesh chipLabel = Label("INK", chip.transform, new Vector3(0f, 0.22f, 0f), 0.16f, Color.white, true);
            ConfigureControlLabel(chipLabel, core.PlayerCamera);

            ZoneSign(root, "ArtSupplies", "ART SUPPLIES · PICK UP A BRUSH", new Vector3(-13.3f, 3.3f, -4.2f), -90f,
                context.Dark);

            SetField(paintTool, "leftCarryAnchor", Find(context.Scene, "LeftBrushCarryAnchor").transform);
            SetField(paintTool, "rightCarryAnchor", Find(context.Scene, "RightBrushCarryAnchor").transform);
            SetObjectArray(paintTool, "brushReferences", brushes.Cast<UnityEngine.Object>().ToArray());
            return new ToolLayout { PaintTool = paintTool, Brushes = brushes };
        }
    }
}
