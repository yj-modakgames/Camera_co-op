using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop
{
    public class CanvasDrawingPresenter : MonoBehaviour
    {
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Material[] brushMaterials;

        private readonly List<GameObject> generated = new List<GameObject>();

        public void Show(CanvasDrawingData data, CanvasSurface surface)
        {
            CanvasDrawingData copy;
            string error;
            if (surface == null || !CanvasDrawingRender.HasValidSize(surface))
            {
                Debug.LogError("[CanvasDrawingPresenter] A non-degenerate target surface is required.");
                return;
            }
            if (!CanvasDrawingData.TryCopy(data, brushMaterials != null ? brushMaterials.Length : 0, out copy, out error))
            {
                Debug.LogError("[CanvasDrawingPresenter] " + error);
                return;
            }

            ClearPresentation();
            for (int i = 0; i < copy.strokes.Length; i++)
            {
                CanvasStrokeData stroke = copy.strokes[i];
                Material material = brushMaterials[stroke.brushId];
                LineRenderer line = CanvasDrawingRender.Create(stroke, surface, transform, material != null ? material : lineMaterial);
                line.sortingOrder = i;
                generated.Add(line.gameObject);
            }
        }

        public void Hide()
        {
            foreach (GameObject item in generated)
                if (item != null) item.SetActive(false);
        }

        public void ClearPresentation()
        {
            foreach (GameObject item in generated) CanvasDrawingRender.DestroyOwned(item);
            generated.Clear();
        }

        private void OnDestroy() { ClearPresentation(); }
    }

    internal static class CanvasDrawingRender
    {
        internal static float ShortSide(CanvasSurface surface)
        {
            return Mathf.Min(surface.transform.TransformVector(Vector3.right).magnitude,
                surface.transform.TransformVector(Vector3.up).magnitude);
        }

        internal static bool HasValidSize(CanvasSurface surface)
        {
            float size = ShortSide(surface);
            return CanvasDrawingData.IsFinite(size) && size > 0f;
        }

        internal static LineRenderer Create(CanvasStrokeData stroke, CanvasSurface surface, Transform parent, Material material)
        {
            var item = new GameObject("Stroke_" + stroke.strokeId);
            item.transform.SetParent(parent, worldPositionStays: true);
            LineRenderer line = item.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.widthMultiplier = stroke.widthNormalized * ShortSide(surface);
            Color32 color = new Color32((byte)(stroke.colorArgb >> 16), (byte)(stroke.colorArgb >> 8),
                (byte)stroke.colorArgb, (byte)(stroke.colorArgb >> 24));
            line.startColor = color;
            line.endColor = color;
            line.positionCount = stroke.xy.Length / 2;
            for (int i = 0; i < line.positionCount; i++)
                line.SetPosition(i, surface.NormToWorld(new Vector2(stroke.xy[i * 2], stroke.xy[i * 2 + 1])));
            return line;
        }

        internal static void DestroyOwned(GameObject item)
        {
            if (item == null) return;
            if (Application.isPlaying) Object.Destroy(item);
            else Object.DestroyImmediate(item);
        }
    }
}
