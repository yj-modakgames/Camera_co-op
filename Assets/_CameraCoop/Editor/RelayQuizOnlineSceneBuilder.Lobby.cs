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
                AvatarRoots = new Transform[4], RemotePresenters = new RemoteAvatarPresenter[3],
                AvatarRigs = new AvatarRig[4]
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
                AvatarRig rig = AstronautAvatarFactory.Create("AvatarBody_" + slot, avatarRoot, avatarRoot.position,
                    180f, colors[slot]);
                if (!rig.IsValid)
                {
                    GameObject avatar = Capsule("AvatarBody_" + slot, avatarRoot, new Vector3(0f, 1f, 0f),
                        new Vector3(0.65f, 1f, 0.65f), colors[slot]);
                    Collider avatarCollider = avatar.GetComponent<Collider>();
                    if (avatarCollider != null) UnityEngine.Object.DestroyImmediate(avatarCollider);
                }
                // slot 0은 본인이다. 본인 캐릭터는 PlayerRig를 따라다니므로 bay에 세워두면 분신이 하나 더 생긴다.
                // binding 계약을 위해 root는 남기고 몸만 끈다.
                if (slot == 0 && rig.IsValid) rig.Root.SetActive(false);
                layout.AvatarRoots[slot] = avatarRoot;
                layout.AvatarRigs[slot] = rig;

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

            // 본인 캐릭터. PlayerRig 아래에 두어 이동·회전을 그대로 따라간다. 카메라는 머리 위(2.4)에 있어
            // 고개를 숙이면 자기 몸과 손에 든 붓이 보인다.
            AvatarRig localRig = AstronautAvatarFactory.Create("LocalAvatarBody", core.PlayerRig,
                core.PlayerRig.position, 0f, context.Red);
            layout.LocalAvatarRig = localRig;
            if (localRig.IsValid && localRig.Animator != null)
                localRig.Animator.gameObject.AddComponent<LocalAvatarAnimator>().Configure(core.PlayerRig);
            // 붓은 캐릭터 손 본에 쥐어진다. 아바타가 없으면 예전처럼 카메라 앞 marker로 되돌아간다.
            layout.LeftBrushAnchor = localRig.LeftHand != null ? localRig.LeftHand
                : LocalMarker("LeftBrushCarryAnchor", core.PlayerRig, new Vector3(-0.35f, 1.35f, 0.7f), 0f);
            layout.RightBrushAnchor = localRig.RightHand != null ? localRig.RightHand
                : LocalMarker("RightBrushCarryAnchor", core.PlayerRig, new Vector3(0.35f, 1.35f, 0.7f), 0f);

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
            // 예전 자리(-11.8)는 BayRug_0 위였다. 러그 서쪽 끝(-11.65) 바깥으로 빼 자리 표식을 침범하지 않게 한다.
            actionList.Add(Action(context, baysRoot, "Carry Paper", PartyWorldAction.CarryCanvas,
                new Vector3(-12.7f, 0f, 6.6f), context.Red));
            actionList.Add(Action(context, baysRoot, "Dock Paper", PartyWorldAction.DockCanvas,
                new Vector3(-12.7f, 0f, 5.2f), context.Accent));

            // 높이 3.1은 15 m 뒤 PLAYER 2·3 표지판과 같은 시선 각도(7°)에 걸려 글자가 겹쳤다.
            // spawn에서 26° 위로 올려 4.9~10.5°에 몰려 있는 이젤·자리·연습벽 표지판 위로 완전히 뺀다.
            ZoneSign(lobby, "Lobby", "LOBBY · HOST / INVITE / START", new Vector3(0f, 5f, CounterZ - 1f), 0f,
                context.Dark);
            // 모드 표지판은 ModeSelectorRoot 안에 둔다. 선택이 닫혀 있을 때 혼자 남으면 안내가 거짓말이 된다.
            ZoneSign(modes, "Mode", "PICK A MODE, THEN START", new Vector3(0f, 3.2f, -3.6f), 0f, context.Dark);
            BuildLobbyCounterProps(context, lobby, counterTop, counterFront, counterHalfWidth);

            actionList.AddRange(BuildCameraStation(context, core));
            layout.Actions = actionList.OrderBy(item => (int)item.Action).ToArray();
            return layout;
        }

        // 주방 카운터(Kenney kitchenBar)는 실내 가구다. 외계 기지 컨셉에 맞춰 보급 상자를 이어 붙인
        // 바리케이드로 바꾼다.
        //
        // **한 줄에는 한 모델만 쓴다.** 세 상자는 높이 대비 폭이 1.137 / 1.022 / 1.000으로 최대 14% 다르다.
        // 높이 기준으로 맞춰 놓고 조각마다 모델을 바꾸면, 첫 조각 하나로 잰 폭으로 깐 격자에 0.1 m 틈이 벌어진다.
        // 변화는 줄끼리 다른 모델을 써서 준다 — 카운터 01, 카메라 콘솔 02, 보급 작업대 03.
        private static readonly string[] CrateModels =
            { "SM_Gen_Prop_Crate_01", "SM_Gen_Prop_Crate_02", "SM_Gen_Prop_Crate_03" };

        private static void BuildLobbyCounter(Context context, Transform lobby, out float counterTop,
            out float counterFront, out float counterHalfWidth)
        {
            GameObject probe = CrateProp(CrateModels[0], "LobbyCounter_0", lobby,
                new Vector3(0f, 0f, CounterZ), CounterHeight, context.Wood);
            // 조각 폭은 실측한다 — 상수 간격은 에셋이 바뀌면 틈이 생긴다.
            Bounds segment = WorldRenderBounds(probe);
            float width = segment.size.x;
            for (int index = 1; index <= 6; index++)
            {
                float offset = (index + 1) / 2 * width * (index % 2 == 0 ? 1f : -1f);
                CrateProp(CrateModels[0], "LobbyCounter_" + index, lobby,
                    new Vector3(offset, 0f, CounterZ), CounterHeight, context.Wood);
            }
            counterTop = segment.max.y;
            counterFront = segment.min.z;
            counterHalfWidth = width * 3.5f;
            // LobbyDesk는 카운터 앞판이다. 방 이름이 여기 붙고, 카운터 몸통은 상자들이 맡는다.
            Cube("LobbyDesk", lobby, new Vector3(0f, 0.45f, counterFront - 0.05f),
                new Vector3(counterHalfWidth * 2f - 0.2f, 0.8f, 0.06f), context.Dark);
            Label("4 PLAYER CAMERA CO-OP", lobby, new Vector3(0f, 0.45f, counterFront - 0.086f), 0.62f, Color.white);
        }

        // 예전엔 바 스툴 3개·책·머그였다. 셋 다 실내 소품이고, 스툴은 MEMORY COPY 받침과 겹쳐 있었다.
        // 바리케이드 양 끝을 막는 금속 드럼 두 개로 대체한다 — 카운터에서 0.9 m 떨어뜨려 겹치지 않는다.
        private static void BuildLobbyCounterProps(Context context, Transform lobby, float counterTop,
            float counterFront, float counterHalfWidth)
        {
            Transform props = Group("LobbyDeskProps", lobby);
            string[] barrels = { "SM_Gen_Prop_Barrel_Metal_01", "SM_Gen_Prop_Barrel_Metal_03" };
            for (int index = 0; index < barrels.Length; index++)
            {
                float x = (counterHalfWidth + 0.9f) * (index == 0 ? -1f : 1f);
                CrateProp(barrels[index], "LobbyBarrel_" + index, props,
                    new Vector3(x, 0f, CounterZ), 0.95f, context.Wood);
            }
        }

        // 사무용 책상 두 개 + 회전의자였다. 의자는 PREVIEW 버튼과 겹쳐 있었고 셋 다 실내 가구다.
        // 버튼 하나당 보급 상자 하나를 깔아 콘솔 열을 만든다 — 버튼이 허공에 뜨지 않는다.
        private static WorldActionInteractable[] BuildCameraStation(Context context, CoreReferences core)
        {
            Transform station = Group("CameraStation", core.WorldRoot.transform);
            const float consoleX = 12.2f;
            const float firstZ = -5.8f;
            const float deskFit = 0.8f;
            float deskTop = deskFit;
            float pitch = deskFit;
            for (int index = 0; index < 4; index++)
            {
                GameObject crate = CrateProp(CrateModels[1], "CameraDesk_" + index, station,
                    new Vector3(consoleX, 0f, firstZ + index * pitch), deskFit, context.Dark);
                if (index > 0) continue;
                // 상판 높이와 상자 간격은 첫 조각을 실측해 정한다. 상수를 믿으면 버튼이 상자 속에 박히거나 뜬다.
                Bounds bounds = WorldRenderBounds(crate);
                deskTop = bounds.max.y;
                pitch = bounds.size.z;
            }
            // 모니터는 콘솔 뒤(동쪽) 바닥에 세운다. 상자 위에 얹으면 버튼 조준선을 가린다.
            CrateProp("SM_Gen_Prop_Screen_01", "CameraMonitor", station,
                new Vector3(12.95f, 0f, firstZ + 1.5f * pitch), 1.6f, context.Dark, PropFit.Height, -90f);
            ZoneSign(station, "Camera", "CAMERA · REFRESH / PREV / NEXT / PREVIEW",
                new Vector3(12.9f, 3.3f, firstZ + 1.5f * pitch), 90f, context.Dark);

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
                    new Vector3(consoleX, deskTop, firstZ + index * pitch), tints[index]);
            }
            return actions;
        }

        // 보급 상자 2×2로 짠 작업대. 상자 실측 크기로 격자를 만드므로 에셋이 바뀌어도 틈이 없다.
        // 상자는 정육면체가 아니다 (높이 0.8로 맞추면 1.14 × 1.56). 2×2가 서쪽 벽면에서 두 작업대가
        // 겹치지 않고 들어가는 최대 크기다 — 3행으로 짜면 z로 3.7 m가 되어 서로 물린다.
        // 돌려주는 bounds가 위에 얹는 물건들의 유일한 기준점이다.
        private static Bounds SupplyBench(Context context, Transform root, int index, Vector3 center)
        {
            const float benchTop = 0.8f;
            Transform bench = Group("SupplyBench_" + index, root);
            // 첫 상자를 놓아 칸 크기를 재고, 같은 공식으로 자기 자리(0,0 칸)로 옮긴다.
            GameObject probe = CrateProp(CrateModels[2], "BenchCrate_00", bench, center, benchTop, context.Wood);
            Vector3 cell = WorldRenderBounds(probe).size;
            probe.transform.position += CrateCell(0, 0, cell);
            for (int column = 0; column < 2; column++)
            for (int row = 0; row < 2; row++)
            {
                if (column == 0 && row == 0) continue;
                CrateProp(CrateModels[2], "BenchCrate_" + column + row, bench,
                    center + CrateCell(column, row, cell), benchTop, context.Wood);
            }
            return WorldRenderBounds(bench.gameObject);
        }

        private static Vector3 CrateCell(int column, int row, Vector3 cell)
        {
            return new Vector3((column - 0.5f) * cell.x, 0f, (row - 0.5f) * cell.z);
        }

        private static ToolLayout BuildPhysicalTools(Context context, CoreReferences core,
            Transform leftBrushAnchor, Transform rightBrushAnchor)
        {
            Transform root = Group("PhysicalTools", core.WorldRoot.transform);
            PhysicalPaintTool paintTool = root.gameObject.AddComponent<PhysicalPaintTool>();
            SetField(paintTool, "toolState", core.ToolState);
            SetField(paintTool, "localPlayerId", "EditorLocalPlayer");
            SetField(paintTool, "maxInteractionDistance", 12f);

            // 작업대는 Kenney 실내 테이블이었다. 보급 상자를 2×2로 깔아 기지 작업대로 바꾼다.
            // 위에 얹는 물건은 전부 여기서 돌려주는 실측 bounds로 자리를 잡는다 — 상수 높이를 믿지 않는다.
            Bounds brushBench = SupplyBench(context, root, 0, new Vector3(-12.6f, 0f, -6.25f));
            Bounds paintBench = SupplyBench(context, root, 1, new Vector3(-12.6f, 0f, -3.38f));
            float benchTop = brushBench.max.y;

            // 붓 세 자루가 얹히는 받침. 손 조준 표적이 되도록 collider를 상판보다 크게 잡되,
            // 붓보다 벽 쪽(서쪽)에 둔다 — 앞을 가리면 가장 가까운 hit만 보는 라우터가 붓을 못 집는다.
            GameObject rack = Cube("BrushRack", root,
                new Vector3(brushBench.center.x - 0.4f, benchTop + 0.06f, brushBench.center.z),
                new Vector3(0.44f, 0.12f, brushBench.size.z - 0.2f), context.Wood);
            rack.GetComponent<BoxCollider>().size = new Vector3(1f, 2.5f, 1.025f);
            PhysicalToolStation rackStation = rack.AddComponent<PhysicalToolStation>();
            rackStation.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Rack, 0);
            SetField(paintTool, "rack", rack.transform);
            // dockAnchor를 지정하면 붓 셋이 그 한 점에 겹쳐 docking된다. 각자 놓인 자리로 돌아가게 비워 둔다.

            var brushes = new PhysicalBrush[3];
            for (int index = 0; index < brushes.Length; index++)
            {
                // 간격은 붓 길이(0.9)보다 넓게. 0.22 m 잡기 collider끼리 겹치면 조준이 갈린다.
                var position = new Vector3(brushBench.center.x, benchTop + 0.12f,
                    brushBench.center.z + (index - 1) * 0.95f);
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

            // PaintPalette(LumiStudio)는 지웠다. 작업대 위에 물감통 넉 대가 이미 꽉 차 놓을 자리가 없어
            // 상판 밖 허공에 떠 있었고, 조준을 가려 2026-09-04 결함의 원인이 됐던 소품이다.

            Material[] paints = { context.Red, context.Blue, context.Green, context.Yellow };
            string[] potModels =
                { "SM_Gen_Prop_Pot_01", "SM_Gen_Prop_Pot_02", "SM_Gen_Prop_Pot_03", "SM_Gen_Prop_Pot_04" };
            float paintTop = paintBench.max.y;
            for (int index = 0; index < paints.Length; index++)
            {
                // 높이로 맞추면 납작한 항아리(Pot_04)가 폭 0.51 m로 부풀어 옆 통까지 넘본다. 폭 기준으로 맞춘다.
                // 통은 상판 동쪽 절반에 둔다 — 플레이어가 동쪽에서 조준하므로 앞을 가리는 것이 없어야 한다.
                GameObject pot = ControlBody("PaintPot_" + index, root,
                    new Vector3(paintBench.center.x + 0.15f, paintTop,
                        paintBench.center.z + (index - 1.5f) * 0.5f),
                    new Vector3(0.34f, 0.34f, 0.34f),
                    SyntyPropFolder + potModels[index] + ".prefab", paints[index], PropFit.Footprint);
                PhysicalToolStation station = pot.AddComponent<PhysicalToolStation>();
                station.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Paint, index);
            }

            string[] widthNames = { "THIN", "MID", "WIDE" };
            float[] widthHeights = { 0.45f, 0.62f, 0.8f };
            for (int index = 0; index < 3; index++)
            {
                GameObject width = ControlBody("WidthControl_" + index, root,
                    new Vector3(-11f, 0f, -5.8f + index * 1.2f),
                    new Vector3(0.55f, widthHeights[index], 0.55f),
                    SyntyPropFolder + "SM_Gen_Prop_Plinth_02.prefab", context.Accent);
                PhysicalToolStation station = width.AddComponent<PhysicalToolStation>();
                station.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Width, index);
                TextMesh widthLabel = Label(widthNames[index], width.transform,
                    new Vector3(0f, widthHeights[index] * 0.5f + 0.22f, 0f), 0.18f, Color.white, true);
                ConfigureControlLabel(widthLabel, core.PlayerCamera);
            }

            GameObject eraser = ControlBody("EraserStation", root, new Vector3(-11f, 0f, -2f),
                new Vector3(0.7f, 0.7f, 0.7f), SyntyPropFolder + "SM_Gen_Prop_Crate_01.prefab", context.Paper);
            PhysicalToolStation eraserStation = eraser.AddComponent<PhysicalToolStation>();
            eraserStation.SetConfiguration(paintTool, PhysicalToolStation.StationKind.Eraser, 0);
            TextMesh eraserLabel = Label("ERASER", eraser.transform, new Vector3(0f, 0.57f, 0f), 0.18f, Dark, true);
            ConfigureControlLabel(eraserLabel, core.PlayerCamera);
            // 선택한 색을 되돌려 주는 칩. 물감통을 눌러도 바뀐 걸 볼 데가 없었다 (사용자 보고 2026-09-04).
            // 통 반대쪽(서쪽) 끝에 세워 조준선에서 비켜 둔다.
            var chipGround = new Vector3(paintBench.center.x - 0.4f, paintTop, paintBench.max.z - 0.4f);
            GameObject chipStand = Cube("CurrentInkStand", root, chipGround + new Vector3(0f, 0.15f, 0f),
                new Vector3(0.1f, 0.3f, 0.1f), context.Dark);
            UnityEngine.Object.DestroyImmediate(chipStand.GetComponent<Collider>());
            GameObject chip = Cube("CurrentInkChip", root, chipGround + new Vector3(0f, 0.36f, 0f),
                new Vector3(0.26f, 0.12f, 0.26f), context.Paper);
            UnityEngine.Object.DestroyImmediate(chip.GetComponent<Collider>());
            ToolStatusIndicator indicator = chip.AddComponent<ToolStatusIndicator>();
            SetField(indicator, "toolState", core.ToolState);
            SetField(indicator, "chip", chip.GetComponent<MeshRenderer>());
            TextMesh chipLabel = Label("INK", chip.transform, new Vector3(0f, 0.22f, 0f), 0.16f, Color.white, true);
            ConfigureControlLabel(chipLabel, core.PlayerCamera);

            ZoneSign(root, "ArtSupplies", "ART SUPPLIES · PICK UP A BRUSH", new Vector3(-13.3f, 3.3f, -5f), -90f,
                context.Dark);

            SetField(paintTool, "leftCarryAnchor", leftBrushAnchor);
            SetField(paintTool, "rightCarryAnchor", rightBrushAnchor);
            SetObjectArray(paintTool, "brushReferences", brushes.Cast<UnityEngine.Object>().ToArray());
            return new ToolLayout { PaintTool = paintTool, Brushes = brushes };
        }
    }
}
