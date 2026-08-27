using System;
using UnityEngine;

namespace CameraCoop
{
    // docs/02_protocol.md 스키마 v1과 1:1 대응하는 순수 DTO. 로직 없음.
    [Serializable]
    public class HandPacket
    {
        public int v;
        public uint seq;
        public double timestamp;
        public HandData[] hands;
    }

    [Serializable]
    public class HandData
    {
        public string handedness;
        public float[] landmarks; // 21개 랜드마크 x (x,y,z) 평탄화 배열, 길이 63
        public float pinch;

        // index번 랜드마크를 Vector3로 변환. 범위 밖이거나 배열이 부족하면 Vector3.zero.
        public Vector3 GetLandmark(int index)
        {
            if (landmarks == null || index < 0)
            {
                return Vector3.zero;
            }

            int offset = index * 3;
            if (offset + 2 >= landmarks.Length)
            {
                return Vector3.zero;
            }

            return new Vector3(landmarks[offset], landmarks[offset + 1], landmarks[offset + 2]);
        }
    }

    // 패킷 수용 여부 판정 (v 검사 + seq 역전/중복 검사). 순수 함수.
    public static class PacketFilter
    {
        public const int SupportedVersion = 1; // docs/02_protocol.md 현재 스키마 버전

        public static bool ShouldAccept(HandPacket packet, uint lastSeq)
        {
            if (packet == null)
            {
                return false;
            }

            if (packet.v != SupportedVersion)
            {
                return false;
            }

            if (packet.seq <= lastSeq)
            {
                return false;
            }

            return true;
        }

        // 서버 lost 이후의 첫 패킷은 송신 측 재시작으로 간주해 seq 체인을 새로 시작한다.
        // 재시작한 Python은 seq를 0부터 다시 보내므로, lastSeq를 유지하면 ShouldAccept가
        // 그 패킷을 영구히 폐기해 자동 복구가 불가능해진다 (docs/02_protocol.md §4).
        public static bool IsNewSession(uint? lastSeq, float timeSinceLastPacket, float lostTimeout)
        {
            return !lastSeq.HasValue || timeSinceLastPacket >= lostTimeout;
        }
    }

    // 정규화 좌표 [0,1] → 화면 픽셀 좌표 변환 (y 반전). 순수 함수.
    public static class HandScreenMapper
    {
        public static Vector2 ToScreen(float x, float y, float screenW, float screenH)
        {
            return new Vector2(x * screenW, (1f - y) * screenH);
        }

        // 화면 픽셀(좌하단 원점) → 정규화 [0,1](좌상단 원점). ToScreen의 역함수.
        public static Vector2 ToNormalized(Vector2 screenPos, float screenW, float screenH)
        {
            return new Vector2(screenPos.x / screenW, 1f - screenPos.y / screenH);
        }
    }

    // 핀치 히스테리시스 상태 전이. 경계 떨림 방지용 순수 함수.
    public static class PinchStateMachine
    {
        public static bool Next(bool current, float pinch, float startThreshold, float releaseThreshold)
        {
            if (current)
            {
                // 핀치 중: releaseThreshold를 초과해야 해제
                return pinch <= releaseThreshold;
            }

            // 비핀치: startThreshold 미만이어야 시작
            return pinch < startThreshold;
        }
    }
}
