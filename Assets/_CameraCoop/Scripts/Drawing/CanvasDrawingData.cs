using System;
using System.Collections.Generic;

namespace CameraCoop
{
    [Serializable]
    public class CanvasStrokeData
    {
        public int strokeId;
        public int order;
        public float[] xy;
        public int colorArgb;
        public float widthNormalized;
        public int brushId;

        internal CanvasStrokeData Copy()
        {
            return new CanvasStrokeData
            {
                strokeId = strokeId,
                order = order,
                xy = (float[])xy.Clone(),
                colorArgb = colorArgb,
                widthNormalized = widthNormalized,
                brushId = brushId
            };
        }
    }

    [Serializable]
    public class CanvasDrawingData
    {
        public int version = 1;
        public CanvasStrokeData[] strokes = Array.Empty<CanvasStrokeData>();

        public static bool TryCopy(CanvasDrawingData source, int brushCount, out CanvasDrawingData copy, out string error)
        {
            copy = null;
            error = null;
            if (source == null || source.version != 1 || source.strokes == null)
            {
                error = "Drawing must have version 1 and a non-null strokes array.";
                return false;
            }

            var ids = new HashSet<int>();
            var orders = new HashSet<int>();
            var strokes = new CanvasStrokeData[source.strokes.Length];
            for (int i = 0; i < strokes.Length; i++)
            {
                CanvasStrokeData stroke = source.strokes[i];
                if (stroke == null || stroke.xy == null || stroke.xy.Length < 4 || stroke.xy.Length % 2 != 0)
                {
                    error = "Stroke " + i + " must contain at least two coordinate pairs.";
                    return false;
                }
                if (stroke.strokeId <= 0 || stroke.order < 0 || !ids.Add(stroke.strokeId) || !orders.Add(stroke.order))
                {
                    error = "Stroke " + i + " has an invalid or duplicate id/order.";
                    return false;
                }
                if (!IsFinite(stroke.widthNormalized) || stroke.widthNormalized <= 0f ||
                    stroke.brushId < 0 || stroke.brushId >= brushCount)
                {
                    error = "Stroke " + i + " has an invalid width or brush index.";
                    return false;
                }
                for (int point = 0; point < stroke.xy.Length; point++)
                {
                    float value = stroke.xy[point];
                    if (!IsFinite(value) || value < 0f || value > 1f)
                    {
                        error = "Stroke " + i + " has a coordinate outside [0,1].";
                        return false;
                    }
                }
                strokes[i] = stroke.Copy();
            }
            Array.Sort(strokes, (left, right) => left.order.CompareTo(right.order));
            copy = new CanvasDrawingData { strokes = strokes };
            return true;
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
