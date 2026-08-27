using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop
{
    // 지우개 판정 (docs/11 §2). LineRenderer는 벡터라 픽셀 단위 지우기가 원리적으로 불가능해
    // 닿은 스트로크를 통째로 지운다 — 그 "닿음"의 정의가 이 함수다.
    internal static class EraseLogic
    {
        // 점열(스트로크)의 선분 중 하나라도 닿으면 hit. 점 1개짜리는 선분이 없어 hit이 아니다.
        public static bool HitsStroke(List<Vector3> points, Vector3 point, float radius)
        {
            if (points == null)
            {
                return false;
            }
            for (int i = 0; i + 1 < points.Count; i++)
            {
                if (HitsSegment(point, points[i], points[i + 1], radius))
                {
                    return true;
                }
            }
            return false;
        }

        // 점-선분 최소거리 <= radius (월드 단위). 끝점 너머는 수직거리가 아니라 끝점 거리로 판정한다.
        public static bool HitsSegment(Vector3 point, Vector3 a, Vector3 b, float radius)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            Vector3 closest;
            if (lengthSq <= 1e-12f)
            {
                closest = a; // 길이 0 선분 = 점 하나. 0으로 나누지 않는다
            }
            else
            {
                float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
                closest = a + ab * t;
            }
            return (point - closest).sqrMagnitude <= radius * radius;
        }
    }
}
