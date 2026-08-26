using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop.Netplay
{
    // 세션의 canonical 스트로크 모델 (로컬 포함 — 늦은 참가 스냅샷의 원본, docs/08 §3)
    public class NetStroke
    {
        public string playerId;
        public List<Vector2> points = new List<Vector2>();
        public bool finished;
    }

    // NetSession의 프레임 무관 판정을 분리한 순수 함수 (docs/04 §5 패턴)
    public static class SessionLogic
    {
        public const int MaxPlayers = 4;

        // 사용 중인 색 인덱스를 피해 가장 작은 값 배정. 꽉 차면 -1 (참가 거절)
        public static int AssignColorIndex(List<int> used)
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (!used.Contains(i))
                {
                    return i;
                }
            }
            return -1;
        }

        // 확정 스트로크만 스냅샷으로 변환 (진행 중 스트로크는 이후 실시간 스트림이 담당)
        public static StrokeSnapshot[] BuildSnapshot(Dictionary<string, NetStroke> strokes)
        {
            var list = new List<StrokeSnapshot>();
            foreach (KeyValuePair<string, NetStroke> pair in strokes)
            {
                if (!pair.Value.finished)
                {
                    continue;
                }
                list.Add(new StrokeSnapshot
                {
                    strokeId = pair.Key,
                    playerId = pair.Value.playerId,
                    xy = NetProtocol.FlattenPoints(pair.Value.points)
                });
            }
            return list.ToArray();
        }
    }
}
