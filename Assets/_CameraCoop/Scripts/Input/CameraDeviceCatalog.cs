using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CameraCoop
{
    public static class CameraDeviceCatalog
    {
        private static readonly Regex DevicePattern = new Regex(
            @"\{\s*""index""\s*:\s*(?<index>[0-9]+)\s*,\s*""available""\s*:\s*true\s*\}",
            RegexOptions.CultureInvariant);

        public static bool TryParseIndices(string json, out int[] indices, out string error)
        {
            var found = new List<int>();
            if (string.IsNullOrEmpty(json))
            {
                indices = new int[0];
                error = "카메라 목록이 비어 있습니다";
                return false;
            }
            MatchCollection matches = DevicePattern.Matches(json);
            for (int i = 0; i < matches.Count; i++)
            {
                int index;
                if (!int.TryParse(matches[i].Groups["index"].Value, out index) || found.Contains(index)) continue;
                found.Add(index);
            }
            found.Sort();
            indices = found.ToArray();
            error = indices.Length == 0 ? "사용 가능한 카메라가 없습니다" : string.Empty;
            return indices.Length > 0;
        }

        public static int NextIndex(int[] indices, int selected, int direction)
        {
            if (indices == null || indices.Length == 0 || direction == 0) return -1;
            int current = Array.IndexOf(indices, selected);
            if (current < 0) current = direction > 0 ? -1 : 0;
            int next = (current + (direction > 0 ? 1 : -1) + indices.Length) % indices.Length;
            return indices[next];
        }
    }
}
