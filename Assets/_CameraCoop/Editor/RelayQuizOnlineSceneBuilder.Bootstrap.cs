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
