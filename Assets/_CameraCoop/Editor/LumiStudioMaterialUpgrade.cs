using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CameraCoop.EditorTools
{
    // LumiStudio Painting Tools는 Built-in Standard shader로 저장돼 있어 URP 프로젝트에서 마젠타로 보인다.
    // Unity 6의 Render Pipeline Converter는 창 조작이 필요하므로, 이 두 material만 코드로 변환한다.
    // ROM(Roughness/Occlusion/Metallic) 팩 텍스처는 URP Lit의 metallic-smoothness 배치와 다르다.
    // URP는 occlusion을 G 채널에서 읽으므로 occlusion만 재사용하고, metallic/smoothness는 상수로 둔다.
    public static class LumiStudioMaterialUpgrade
    {
        private const string MenuPath = "Camera Co-op/Assets/Upgrade LumiStudio Materials to URP";
        private const string MaterialFolder = "Assets/LumiStudio";
        private const string UrpLitShader = "Universal Render Pipeline/Lit";
        private const float DefaultSmoothness = 0.35f;

        [MenuItem(MenuPath)]
        public static void UpgradeAll()
        {
            Shader urpLit = Shader.Find(UrpLitShader);
            if (urpLit == null)
            {
                Debug.LogError("[LumiStudioMaterialUpgrade] " + UrpLitShader + " shader를 찾지 못했습니다.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder });
            var upgraded = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == urpLit) continue;

                Texture albedo = Get(material, "_MainTex");
                Texture normal = Get(material, "_BumpMap");
                Texture occlusion = Get(material, "_OcclusionMap");
                Color baseColor = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;

                material.shader = urpLit;
                material.SetColor("_BaseColor", baseColor);
                if (albedo != null) material.SetTexture("_BaseMap", albedo);
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (occlusion != null)
                {
                    material.SetTexture("_OcclusionMap", occlusion);
                    material.SetFloat("_OcclusionStrength", 1f);
                    material.EnableKeyword("_OCCLUSIONMAP");
                }
                // ROM 팩은 URP의 metallic(R)/smoothness(A) 배치와 맞지 않는다. 잘못 읽느니 상수를 쓴다.
                material.SetTexture("_MetallicGlossMap", null);
                material.DisableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", DefaultSmoothness);
                EditorUtility.SetDirty(material);
                upgraded.Add(path);
            }

            if (upgraded.Count == 0)
            {
                Debug.Log("[LumiStudioMaterialUpgrade] 변환할 Built-in material이 없습니다.");
                return;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[LumiStudioMaterialUpgrade] URP Lit으로 변환: " + string.Join(", ", upgraded));
        }

        private static Texture Get(Material material, string property)
        {
            return material.HasProperty(property) ? material.GetTexture(property) : null;
        }
    }
}
