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
            CreateOrReplaceMaterial("RoomWood", Wood, 0.1f);
            // 격자 텍스처가 거리감을 만든다. 실외로 바뀐 뒤에도 착륙 패드 바닥에는 이 단서를 남긴다.
            // Dark/texture_13은 #333 바탕에 "WALL 1x1 meter" 라벨이 찍혀 있어 어떤 tint를 줘도 검게 죽고
            // 바닥에 글자가 깔린다. Light/texture_13은 흰 격자뿐이라 tint가 그대로 나온다.
            CreateOrReplaceTexturedMaterial("PlanetFloor", PrototypeTextureFolder + "Light/texture_13.png",
                PlanetPad, new Vector2(14f, 8f));
            // 방 밖 평원은 격자를 깔지 않는다. 220 m에 격자를 뿌리면 행성이 모눈종이가 된다.
            CreateOrReplaceMaterial("PlanetGround", PlanetSoil, 0.02f);
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
                Wood = Material("RoomWood"),
                PlanetFloor = Material("PlanetFloor"), PlanetGround = Material("PlanetGround"),
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

            // 벽과 기둥은 없다. 이동 한계는 PlayerMoveLogic.ClampToRoom(±13.5, ±7.5)이 이미 맡고 있어
            // collider로 다시 막으면 중복이고, 실외 지평선을 가린다.
            Cube("Floor", studio.transform, new Vector3(0f, -0.1f, 0f), new Vector3(28f, 0.2f, 16f),
                context.PlanetFloor);
            BuildPlanetTerrain(context, studio.transform);

            GameObject lightObject = new GameObject("RoomKeyLight");
            lightObject.transform.SetParent(studio.transform, false);
            // 실외 저각 태양. 산이 긴 그림자를 드리워 평원이 밋밋해지지 않는다.
            lightObject.transform.rotation = Quaternion.Euler(26f, -38f, 0f);
            Light key = lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.84f, 0.72f);
            key.intensity = 1.6f;
            GameObject fillObject = new GameObject("RoomFillLight");
            fillObject.transform.SetParent(studio.transform, false);
            fillObject.transform.position = new Vector3(0f, 3.4f, 0f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 24f;
            // 천장이 사라져 반사광이 없다. 실내용 6은 과하다 — 절반은 ambient가 채운다.
            fill.intensity = 4f;
            fill.color = new Color(0.66f, 0.78f, 1f);
            // 실내 천장 반사광을 대신하는 자주빛 ambient. 이게 없으면 북쪽을 보는 연습 이젤 면과
            // 산의 그늘면이 통째로 검게 죽는다 (기본 Skybox ambient보다 밝게 잡아야 예전 밝기가 나온다).
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.40f, 0.56f);
        }

        // 지평선을 만드는 능선의 형태. 방(28×16)의 종횡비를 따라 타원으로 돌리고, 한 칸 걸러
        // 뒤 열(RidgeBackRing)에 세워 앞줄 사이의 틈을 메운다. 앞줄과 방 경계의 간격은 축 방향 10 m, 대각선 약 6 m다.
        private const int RidgeCount = 18;
        private const float RidgeStepDegrees = 360f / RidgeCount;
        private const float RidgeRadiusX = 25f;
        private const float RidgeRadiusZ = 18f;
        private const float RidgeBackRing = 1.34f;
        private const float RidgeMinHeight = 8f;
        private const float RidgeHeightStep = 2.2f;
        private const int RidgeHeightCycle = 6;
        // 평원은 능선 바깥까지 덮어야 한다. 220 m면 가장 먼 행성(약 95 m) 아래까지 지면이 이어진다.
        private const float PlainSize = 220f;
        // 방(Floor cube)의 절반 크기. 지형은 이 사각형 밖에 있어야 한다.
        private const float RoomHalfX = 14f;
        private const float RoomHalfZ = 8f;
        // 지형과 방 사이에 남기는 여유. 1.5 m로는 남쪽 산자락이 spawn(z -7.2)에서 2 m 앞에 서서
        // 화면을 파랗게 덮었다. 5 m면 산발치가 보여 능선까지의 거리가 읽힌다.
        private const float TerrainMargin = 5f;
        // 산만 따로 더 민다. 높이가 8~19 m라 5 m 이격으로는 spawn(z -7.2) 뒤 5.8 m에 벽처럼 서서
        // 화면 절반을 파랗게 덮었다. 16 m면 spawn에서 남쪽을 볼 때 능선이 화면 세로의 38%로 내려앉아
        // 위로 하늘, 아래로 평원이 함께 보인다 (12 m에서는 46%로 아직 답답했다).
        private const float MountainMargin = 16f;

        // 산·바위·나무는 prefab마다 실루엣이 제각각이라 상수 반지름으로 두면 자락이 방 안까지 내려온다
        // (RidgeRadius 25/18에서 서쪽 산 세 개가 ART SUPPLIES 작업대를 통째로 삼켰다).
        // 실측 bounds를 보고 바깥 방향으로 필요한 만큼만 민다 — 능선의 형태와 높이는 그대로 둔다.
        private static void PushOutsideRoom(GameObject item, Vector3 outward, float margin = TerrainMargin)
        {
            if (item == null) return;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-6f) return;
            outward.Normalize();
            Bounds bounds = WorldRenderBounds(item);
            if (bounds.max.y <= 0f) return;
            // 판정 사각형은 방이 아니라 방+여유다. 방 밖이기만 하면 통과시키면 경계에 딱 붙은 산이 남는다.
            float limitX = RoomHalfX + margin;
            float limitZ = RoomHalfZ + margin;
            if (bounds.min.x >= limitX || bounds.max.x <= -limitX) return;
            if (bounds.min.z >= limitZ || bounds.max.z <= -limitZ) return;

            float needX = float.MaxValue;
            if (outward.x > 0.001f) needX = (limitX - bounds.min.x) / outward.x;
            else if (outward.x < -0.001f) needX = (-limitX - bounds.max.x) / outward.x;
            float needZ = float.MaxValue;
            if (outward.z > 0.001f) needZ = (limitZ - bounds.min.z) / outward.z;
            else if (outward.z < -0.001f) needZ = (-limitZ - bounds.max.z) / outward.z;

            // x·z 중 하나만 빠져나가면 사각형에서 벗어난다. 덜 움직이는 축을 고른다.
            float push = Mathf.Min(needX, needZ);
            if (push > 0f && push < float.MaxValue) item.transform.position += outward * push;
        }

        // 방 밖은 지평선을 만드는 것이 전부다. 전부 collider 없는 static batching 대상이다.
        private static void BuildPlanetTerrain(Context context, Transform studio)
        {
            Transform terrain = Group("Terrain", studio);

            // Floor cube는 방(28×16)까지만이다. 그 밖이 비면 지평선 아래가 카메라 배경색으로 뚫린다.
            GameObject plain = Cube("PlanetPlain", terrain, new Vector3(0f, -0.13f, 0f),
                new Vector3(PlainSize, 0.2f, PlainSize), context.PlanetGround);
            UnityEngine.Object.DestroyImmediate(plain.GetComponent<Collider>());
            MarkTerrainStatic(plain);

            string[] mountains = { "SP_Mountains/SP_Mountain01", "SP_Mountains/SP_Mountain02", "SP_Mountains/SP_Mountain03" };
            for (int index = 0; index < RidgeCount; index++)
            {
                float angle = index * RidgeStepDegrees;
                float radians = angle * Mathf.Deg2Rad;
                float ring = index % 2 == 0 ? 1f : RidgeBackRing;
                var ground = new Vector3(Mathf.Sin(radians) * RidgeRadiusX * ring, -0.05f,
                    Mathf.Cos(radians) * RidgeRadiusZ * ring);
                PushOutsideRoom(AlienProp(mountains[index % mountains.Length], "Terrain_Mountain_" + index, terrain,
                    ground, RidgeMinHeight + index % RidgeHeightCycle * RidgeHeightStep, PropFit.Height, angle + 25f),
                    ground, MountainMargin);
            }

            // 중경. 능선과 방 사이가 비면 평원이 마분지처럼 보인다.
            float[,] rocks =
            {
                { -17.5f, 6.5f, 2.4f }, { -16.5f, -6f, 1.6f }, { 17f, 5f, 2.8f },
                { 16.2f, -7.5f, 1.8f }, { -5f, 11.5f, 2.2f }, { 7.5f, -11f, 3f }
            };
            for (int index = 0; index < rocks.GetLength(0); index++)
            {
                var ground = new Vector3(rocks[index, 0], 0f, rocks[index, 1]);
                PushOutsideRoom(AlienProp("SP_Rocks/SP_Rock0" + (index + 3), "Terrain_Rock_" + index, terrain,
                    ground, rocks[index, 2], PropFit.Height, index * 57f), ground);
            }

            float[,] trees =
            {
                { -19.5f, 1.5f, 5.5f }, { 18.5f, -1f, 6.2f }, { -9.5f, 12.5f, 4.8f },
                { 3.5f, 13f, 6.6f }, { -3f, -12f, 5f }, { 12f, -12.5f, 5.8f }
            };
            for (int index = 0; index < trees.GetLength(0); index++)
            {
                var ground = new Vector3(trees[index, 0], 0f, trees[index, 1]);
                PushOutsideRoom(AlienProp("SP_Trees/SP_Tree0" + (index % 4 + 1), "Terrain_Tree_" + index, terrain,
                    ground, trees[index, 2], PropFit.Height, index * 41f), ground);
            }

            float[,] patches =
            {
                { -24f, 10f, 16f }, { 22f, 12f, 14f }, { -27f, -15f, 15f }, { 18f, -14f, 13f }
            };
            for (int index = 0; index < patches.GetLength(0); index++)
            {
                var ground = new Vector3(patches[index, 0], -0.06f, patches[index, 1]);
                PushOutsideRoom(AlienProp("SP_Ground/SP_Ground0" + (index + 1), "Terrain_Ground_" + index, terrain,
                    ground, patches[index, 2], PropFit.Footprint, index * 73f), ground);
            }

            // 행성은 낮은 산 너머(방위 22°와 120°, 능선 고도 17°/13°)에 걸리게 둔다. 그래야 능선 위로 뜬다.
            // 방위 0°는 PUBLIC PRACTICE WALL 표지판이 정면으로 가려서 쓸 수 없다.
            AlienProp("SP_Planet", "Terrain_Planet_0", terrain, new Vector3(35.6f, 24f, 88.1f), 34f);
            AlienProp("SP_Planet", "Terrain_Planet_1", terrain, new Vector3(73.6f, 20f, -42.5f), 20f);
            // 안테나는 남서쪽(방위 235°)에 세운다. 그쪽 능선이 가장 낮고(고도 13.5°), 16 m면 탑 끝이
            // 뒷줄 산(고도 30.3°)까지 넘어 검은 하늘을 배경으로 실루엣이 선다.
            AlienProp("SP_Sci-fi_Antenna", "Terrain_Antenna", terrain, new Vector3(-16.5f, 0f, -11.5f), 16f,
                PropFit.Height, 35f);
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
            core.Gallery.Release();
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
    }
}
