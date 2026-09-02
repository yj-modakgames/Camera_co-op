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
            if (value == null) property.objectReferenceValue = null;
            else if (value is UnityEngine.Object unityObject) property.objectReferenceValue = unityObject;
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
