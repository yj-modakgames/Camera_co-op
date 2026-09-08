using System.Linq;
using CameraCoop.Party;
using UnityEditor;
using UnityEngine;

namespace CameraCoop.EditorTools
{
    internal readonly struct AvatarRig
    {
        public AvatarRig(GameObject root, Animator animator, Transform leftHand, Transform rightHand)
        {
            Root = root;
            Animator = animator;
            LeftHand = leftHand;
            RightHand = rightHand;
        }

        public GameObject Root { get; }
        public Animator Animator { get; }
        public Transform LeftHand { get; }
        public Transform RightHand { get; }
        public bool IsValid => Root != null;
    }

    // 로비와 game Scene이 같은 캐릭터를 쓰도록 아바타 생성을 한곳에 둔다.
    internal static class AstronautAvatarFactory
    {
        public const float AvatarHeight = 2f;

        private const string PrefabPath = "Assets/Stylized_Astronaut/Stylized Astronaut.prefab";
        private const string ControllerPath =
            "Assets/Stylized_Astronaut/Character/AstronautCharacterController.controller";
        // Generic rig라 humanoid bone 매핑이 없다. 팔 끝 본을 손으로 쓴다.
        private const string LeftHandBone = "Arm_2_Left_end";
        private const string RightHandBone = "Arm_2_Right_end";

        public static AvatarRig Create(string name, Transform parent, Vector3 position, float yaw, Material brushMaterial)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[AstronautAvatarFactory] prefab이 없어 아바타를 건너뜁니다: " + PrefabPath);
                return default;
            }

            var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            PrefabUtility.UnpackPrefabInstance(item, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            item.name = name;
            item.transform.localScale = Vector3.one;
            item.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // demo용 카메라만 지운다. 몸통 SkinnedMeshRenderer가 "Player" 오브젝트에 붙어 있으므로 남겨야 한다.
            foreach (Transform child in item.transform.Cast<Transform>().ToArray())
                if (child.name == "Player_Camera")
                    Object.DestroyImmediate(child.gameObject);
            // demo 조작 스크립트(AstronautPlayer 등)는 우리 이동·애니메이션과 충돌하므로 제거한다.
            foreach (MonoBehaviour script in item.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script == null) continue;
                string ns = script.GetType().Namespace ?? string.Empty;
                if (ns.StartsWith("CameraCoop", System.StringComparison.Ordinal)) continue;
                Object.DestroyImmediate(script);
            }

            float scale = NormalizeHeight(item);
            item.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            Animator animator = item.GetComponentInChildren<Animator>(true) ?? item.AddComponent<Animator>();
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (controller != null) animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            Transform left = FindDescendant(item.transform, LeftHandBone);
            Transform right = FindDescendant(item.transform, RightHandBone);
            AttachHandBrush(left, RemoteAvatarPresenter.LeftHandBrushName, brushMaterial, scale);
            AttachHandBrush(right, RemoteAvatarPresenter.RightHandBrushName, brushMaterial, scale);
            return new AvatarRig(item, animator, left, right);
        }

        private static float NormalizeHeight(GameObject item)
        {
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[AstronautAvatarFactory] renderer가 없어 키를 맞추지 못했습니다: " + item.name);
                return 1f;
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            float scale = bounds.size.y > Mathf.Epsilon ? AvatarHeight / bounds.size.y : 1f;
            item.transform.localScale = Vector3.one * scale;
            return scale;
        }

        // 원격 아바타가 붓을 든 모습. 평소에는 꺼져 있고 pose의 carriedHand에 따라 RemoteAvatarPresenter가 켠다.
        private static void AttachHandBrush(Transform hand, string name, Material material, float avatarScale)
        {
            if (hand == null) return;
            GameObject brush = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            brush.name = name;
            Object.DestroyImmediate(brush.GetComponent<Collider>());
            brush.transform.SetParent(hand, false);
            brush.transform.localPosition = Vector3.zero;
            brush.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // 아바타 배율이 곱해지므로 손 기준 크기를 그만큼 되돌린다.
            float inverse = avatarScale > Mathf.Epsilon ? 1f / avatarScale : 1f;
            brush.transform.localScale = new Vector3(0.06f, 0.35f, 0.06f) * inverse;
            if (material != null)
            {
                var renderer = brush.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = material;
            }
            brush.SetActive(false);
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                if (item.name == name) return item;
            return null;
        }
    }
}
