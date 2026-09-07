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

        private const string LumiPropFolder = "Assets/LumiStudio/Painting Tools/Prefabs/";
        private static readonly string[] BrushPropPaths =
        {
            LumiPropFolder + "SM_Brush_01a.prefab",
            LumiPropFolder + "SM_Brush_02a.prefab",
            LumiPropFolder + "SM_Brush_03a.prefab"
        };
        private const string PalettePropPath = LumiPropFolder + "SM_Palette_02a.prefab";
        private const float BrushLength = 0.9f;
        // 손에 드는 팔레트 크기. 1.1 m는 물감통 위를 덮어 조준을 가렸다.
        private const float PaletteWidth = 0.6f;

        // 손 raycast는 collider를 맞춰야 한다. 모델 prefab에 collider가 없으면 bounds 기준 box를 붙인다.
        private static void EnsureCollider(GameObject item)
        {
            if (item.GetComponentInChildren<Collider>(true) != null) return;
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            var collider = item.AddComponent<BoxCollider>();
            collider.center = item.transform.InverseTransformPoint(bounds.center);
            collider.size = item.transform.InverseTransformVector(bounds.size);
        }

        // LumiStudio Painting Tools prefab을 배치한다. FBX import 배율을 신뢰할 수 없으므로 renderer bounds로
        // 가장 긴 축을 targetLongestSize에 맞춰 정규화한다. 에셋이 없으면 null을 돌려 호출부가 primitive로 되돌린다.
        private static GameObject PropInstance(string assetPath, string name, Transform parent, Vector3 position,
            float targetLongestSize, Quaternion rotation)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogWarning("[RelayQuizOnlineSceneBuilder] prop asset이 없어 primitive를 사용합니다: " + assetPath);
                return null;
            }
            var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            PrefabUtility.UnpackPrefabInstance(item, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            item.name = name;
            item.transform.localScale = Vector3.one;
            item.transform.rotation = Quaternion.identity;
            item.transform.position = Vector3.zero;

            Bounds bounds = LocalRenderBounds(item);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = longest > Mathf.Epsilon ? targetLongestSize / longest : 1f;
            item.transform.localScale = Vector3.one * scale;
            item.transform.rotation = rotation;
            // bounds 중심이 pivot과 다를 수 있다. 요청한 위치가 실제 물체의 중심이 되게 보정한다.
            item.transform.position = position - item.transform.rotation * (bounds.center * scale);
            return item;
        }

        private static Bounds LocalRenderBounds(GameObject item)
        {
            // item이 원점·무회전·단위배율이므로 world bounds가 곧 local bounds다.
            return WorldRenderBounds(item);
        }

        private static Bounds WorldRenderBounds(GameObject item)
        {
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(item.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private const string KenneyModelFolder = "Assets/Kenney/FurnitureKit/Models/";
        private const string SyntyPropFolder = "Assets/Synty/PolygonGeneric/Prefabs/Props/";
        private const string SyntyBaseFolder = "Assets/Synty/PolygonGeneric/Prefabs/Base/";
        private const string PrototypeTextureFolder = "Assets/Kenney/PrototypeTextures/";

        private enum PropFit { Height, Footprint }

        // 바닥에 세우는 소품. 에셋의 단위를 믿지 않고 renderer bounds로 목표 크기에 맞춘 뒤
        // bounds 바닥 중심이 groundCenter에 오게 놓는다. 에셋이 없으면 null — 호출부가 primitive로 되돌린다.
        private static GameObject FitProp(string assetPath, string name, Transform parent, Vector3 groundCenter,
            float targetSize, PropFit fit, float yaw)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
            {
                Debug.LogWarning("[RelayQuizOnlineSceneBuilder] prop asset이 없어 primitive를 사용합니다: " + assetPath);
                return null;
            }
            var item = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            PrefabUtility.UnpackPrefabInstance(item, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            item.name = name;
            item.transform.localScale = Vector3.one;
            item.transform.rotation = Quaternion.identity;
            item.transform.position = Vector3.zero;

            Bounds bounds = LocalRenderBounds(item);
            float source = fit == PropFit.Height ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = source > Mathf.Epsilon ? targetSize / source : 1f;
            item.transform.localScale = Vector3.one * scale;
            item.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            item.transform.position = groundCenter - item.transform.rotation * foot;
            return item;
        }

        private static GameObject KenneyProp(string model, string name, Transform parent, Vector3 groundCenter,
            float targetSize, PropFit fit = PropFit.Height, float yaw = 0f)
        {
            return FitProp(KenneyModelFolder + model + ".fbx", name, parent, groundCenter, targetSize, fit, yaw);
        }

        private static GameObject SyntyProp(string folder, string prefab, string name, Transform parent,
            Vector3 groundCenter, float targetSize, PropFit fit = PropFit.Height, float yaw = 0f)
        {
            return FitProp(folder + prefab + ".prefab", name, parent, groundCenter, targetSize, fit, yaw);
        }

        // 손 조준 표적의 크기·위치는 prop pivot이 아니라 이 빈 루트의 BoxCollider가 정한다.
        // prop 모델마다 pivot이 제각각이라 collider를 모델에 맡기면 조준 표적이 어긋난다.
        private static GameObject ControlBody(string name, Transform parent, Vector3 groundCenter, Vector3 size,
            string propPath, Material material, PropFit fit = PropFit.Height)
        {
            var body = new GameObject(name);
            body.transform.SetParent(parent, false);
            body.transform.position = groundCenter + Vector3.up * (size.y * 0.5f);
            body.AddComponent<BoxCollider>().size = size;
            GameObject prop = FitProp(propPath, name + "_Model", body.transform, groundCenter,
                fit == PropFit.Height ? size.y : Mathf.Max(size.x, size.z), fit, 0f);
            if (prop == null) Cube(name + "_Model", body.transform, body.transform.position, size, material);
            else PaintAll(prop, material);
            return body;
        }

        // 받침과 버튼을 collider 하나가 통째로 덮는다. 받침에 따로 collider를 두면 조준이 조금만 낮아도
        // HandInteractable이 없는 받침이 "가장 가까운 hit"이 되어 버튼이 아예 눌리지 않는다.
        private static GameObject PedestalButton(string name, Transform parent, Vector3 baseGround,
            string buttonPrefab, Material buttonMaterial, Material plinthMaterial)
        {
            const float plinthHeight = 0.35f;
            const float buttonHeight = 0.4f;
            var body = new GameObject(name);
            body.transform.SetParent(parent, false);
            body.transform.position = baseGround + Vector3.up * ((plinthHeight + buttonHeight) * 0.5f);
            body.AddComponent<BoxCollider>().size = new Vector3(0.56f, plinthHeight + buttonHeight, 0.56f);

            GameObject plinth = SyntyProp(SyntyPropFolder, "SM_Gen_Prop_Plinth_01", name + "_Plinth", body.transform,
                baseGround, plinthHeight);
            if (plinth != null) PaintAll(plinth, plinthMaterial);
            else Cube(name + "_Plinth", body.transform, baseGround + Vector3.up * (plinthHeight * 0.5f),
                new Vector3(0.55f, plinthHeight, 0.55f), plinthMaterial);

            GameObject button = SyntyProp(SyntyPropFolder, buttonPrefab, name + "_Button", body.transform,
                baseGround + Vector3.up * plinthHeight, buttonHeight);
            if (button != null) PaintAll(button, buttonMaterial);
            else Cube(name + "_Button", body.transform,
                baseGround + Vector3.up * (plinthHeight + buttonHeight * 0.5f),
                new Vector3(0.5f, buttonHeight, 0.5f), buttonMaterial);
            return body;
        }

        // Synty Generic_Standard는 _BaseColor가 흰색이라 HandInteractable.ApplyHighlight의 hover 밝기가 보이지 않는다.
        // 상호작용 물체는 빌더가 만든 URP Lit 단색으로 덮는다 (장식은 원래 재질 유지).
        private static void PaintAll(GameObject item, Material material)
        {
            if (item == null || material == null) return;
            foreach (MeshRenderer renderer in item.GetComponentsInChildren<MeshRenderer>(true))
            {
                var slots = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int index = 0; index < slots.Length; index++) slots[index] = material;
                renderer.sharedMaterials = slots;
            }
        }

        // 장식 소품의 collider는 지운다. HandInputRouter.ResolveWorldTarget은 가장 가까운 hit 하나만 후보로
        // 삼으므로, 상호작용이 없는 소품이 앞에 있으면 뒤의 station을 통째로 가린다 (사용자 보고 2026-09-04:
        // PaintPalette가 PaintPot_3을 가려 조준 자체가 되지 않았다).
        private static void StripColliders(GameObject item)
        {
            if (item == null) return;
            foreach (Collider blocker in item.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(blocker);
        }

        // 구역 머리 위 표지판. 손 조준 대상이 아니므로 billboard 없이 읽는 쪽(local -Z)으로 고정한다.
        private static void ZoneSign(Transform parent, string name, string text, Vector3 position, float yaw,
            Material board)
        {
            Transform sign = Group("Sign_" + name, parent);
            sign.position = position;
            sign.rotation = Quaternion.Euler(0f, yaw, 0f);
            float width = text.Length * 0.155f + 0.5f;
            GameObject signBoard = Cube("SignBoard", sign, position, new Vector3(width, 0.72f, 0.08f), board);
            signBoard.transform.rotation = sign.rotation;
            // 표지판은 조준 대상도 장애물도 아니다. collider를 남기면 손 레이를 가리고 통행도 막는다.
            UnityEngine.Object.DestroyImmediate(signBoard.GetComponent<Collider>());
            Label(text, sign, position + sign.rotation * new Vector3(0f, 0f, -0.07f), 0.5f, Color.white, false,
                sign.rotation);
        }

        private static void CreateOrReplaceTexturedMaterial(string name, string texturePath, Color tint, Vector2 tiling)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            CreateOrReplaceMaterial(name, tint, 0.05f);
            Material material = Material(name);
            if (texture == null)
            {
                Debug.LogWarning("[RelayQuizOnlineSceneBuilder] grid texture가 없어 단색을 씁니다: " + texturePath);
                return;
            }
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", tiling);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
                material.SetTextureScale("_MainTex", tiling);
            }
            EditorUtility.SetDirty(material);
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

        // 손에 든 물건이 붙는 anchor는 player를 따라다녀야 하므로 부모 기준 offset이어야 한다.
        // Marker()의 world 좌표를 쓰면 PlayerRig의 spawn 위치만큼 앞쪽 허공에 고정된다.
        private static Transform LocalMarker(string name, Transform parent, Vector3 localPosition, float yaw)
        {
            Transform marker = Group(name, parent);
            marker.localPosition = localPosition;
            marker.localRotation = Quaternion.Euler(0f, yaw, 0f);
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
