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
        private static WorldActionInteractable BuildAction(Transform root, string text, PartyWorldAction action,
            Vector3 position, Material material)
        {
            GameObject item = Cube("Action_" + action, root, position, new Vector3(2.2f, 0.6f, 1f), material);
            WorldActionInteractable interactable = item.AddComponent<WorldActionInteractable>();
            SetField(interactable, "action", action);
            Label(text, item.transform, new Vector3(0f, 0.65f, 0f), 0.19f, Color.white, true);
            return interactable;
        }

        private static CanvasDrawingPresenter Presenter(string name, Transform parent, Palette palette)
        {
            GameObject item = new GameObject(name);
            item.transform.SetParent(parent, false);
            CanvasDrawingPresenter presenter = item.AddComponent<CanvasDrawingPresenter>();
            ConfigurePresenter(presenter, palette);
            return presenter;
        }

        private static void ConfigurePresenter(CanvasDrawingPresenter presenter, Palette palette)
        {
            SetField(presenter, "lineMaterial", palette.Line);
            SetObjectArray(presenter, "brushMaterials", new UnityEngine.Object[]
                { palette.Line, palette.SoftLine, palette.Line });
        }

        private static Palette LoadPalette()
        {
            return new Palette
            {
                Red = Material("PlayerRed"), Blue = Material("PlayerBlue"), Green = Material("PlayerGreen"),
                Yellow = Material("PlayerYellow"), Dark = Material("RoomDark"), Wall = Material("RoomWall"),
                Floor = Material("RoomFloor"), Paper = Material("WhitePaper"), Accent = Material("ActionAccent"),
                Line = AssetDatabase.LoadAssetAtPath<Material>("Assets/_CameraCoop/Materials/StrokeLine.mat"),
                SoftLine = AssetDatabase.LoadAssetAtPath<Material>("Assets/_CameraCoop/Materials/StrokeSoft.mat")
            };
        }

        private static Material Material(string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");
            if (material == null) throw new InvalidOperationException("Build lobby materials before game Scenes: " + name);
            return material;
        }

        private static void RequireIdleEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Party game Scene build requires an idle Editor in EditMode.");
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
            Quaternion rotation)
        {
            return Primitive(name, PrimitiveType.Quad, parent, position, scale, rotation, material);
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
            MeshRenderer renderer = item.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            return item;
        }

        private static Transform Group(string name, Transform parent)
        {
            GameObject item = new GameObject(name);
            item.transform.SetParent(parent, false);
            return item.transform;
        }

        private static Transform Marker(string name, Transform parent, Vector3 position, float yaw)
        {
            Transform item = Group(name, parent);
            item.position = position;
            item.rotation = Quaternion.Euler(0f, yaw, 0f);
            return item;
        }

        private static TextMesh Label(string text, Transform parent, Vector3 position, float size, Color color,
            bool local = false)
        {
            GameObject item = new GameObject("Label_" + text.Replace(' ', '_').Replace('·', '_').Replace(':', '_'));
            item.transform.SetParent(parent, false);
            if (local) item.transform.localPosition = position;
            else item.transform.position = position;
            TextMesh label = item.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = size * 0.08f;
            label.fontSize = 64;
            label.color = color;
            item.AddComponent<WorldLabelBillboard>().Configure(label, null);
            return label;
        }

        private static void FrameAt(Transform parent, string name, Vector3 position, Vector2 size,
            Material material, Quaternion rotation)
        {
            Transform frame = Group(name, parent);
            frame.position = position;
            frame.rotation = rotation;
            const float edge = 0.12f;
            Cube("Top", frame, frame.TransformPoint(new Vector3(0f, size.y * 0.5f, 0f)),
                new Vector3(size.x, edge, edge), material);
            Cube("Bottom", frame, frame.TransformPoint(new Vector3(0f, -size.y * 0.5f, 0f)),
                new Vector3(size.x, edge, edge), material);
            Cube("Left", frame, frame.TransformPoint(new Vector3(-size.x * 0.5f, 0f, 0f)),
                new Vector3(edge, size.y, edge), material);
            Cube("Right", frame, frame.TransformPoint(new Vector3(size.x * 0.5f, 0f, 0f)),
                new Vector3(edge, size.y, edge), material);
        }

        private static void SetField(UnityEngine.Object target, string name, object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null) throw new MissingFieldException(target.GetType().Name, name);
            if (value is UnityEngine.Object unityObject) property.objectReferenceValue = unityObject;
            else if (value is float floatValue) property.floatValue = floatValue;
            else if (value is Enum enumValue) property.enumValueIndex = Convert.ToInt32(enumValue);
            else throw new ArgumentException("Unsupported field " + name + ".");
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
    }

    internal static class PartyGameSceneBuilderStationExtensions
    {
        internal static PhysicalToolStation SetConfigurationAndReturn(this PhysicalToolStation station,
            PhysicalPaintTool tool, PhysicalToolStation.StationKind kind, int index)
        {
            station.SetConfiguration(tool, kind, index);
            return station;
        }
    }
}
