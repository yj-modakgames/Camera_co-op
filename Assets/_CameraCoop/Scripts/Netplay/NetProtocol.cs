using System;
using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop.Netplay
{
    // network v1 envelope (docs/08 §3). JsonUtility는 다형성이 없으므로 payload는 JSON 문자열로 2단 직렬화한다.
    [Serializable]
    public class NetEnvelope
    {
        public int v;
        public string type;
        public string sender;
        public string payload;
    }

    [Serializable] public class HelloPayload { public string name; }
    [Serializable] public class PlayerInfo { public string playerId; public string name; public int colorIndex; }
    [Serializable] public class StrokeSnapshot { public string strokeId; public string playerId; public float[] xy; public int color; public float width; public int brush; } // (x,y) 쌍 평탄화 + 스타일 (v2)
    [Serializable] public class WelcomePayload { public PlayerInfo[] players; public StrokeSnapshot[] snapshot; }
    [Serializable] public class CursorPayload { public string hand; public float x; public float y; public bool pinched; public uint seq; }
    [Serializable] public class StrokeStartPayload { public string strokeId; public string hand; public float x; public float y; public int color; public float width; public int brush; } // 스타일 3필드 = v2 추가분
    [Serializable] public class StrokeErasePayload { public string strokeId; }

    // 스트로크 스타일 묶음 (docs/11 §3). 이벤트 인자 수를 줄이기 위한 값 타입 — 와이어에는 3필드로 평탄하게 실린다.
    [Serializable] public struct StrokeStyle
    {
        public int color;   // packed 0xAARRGGBB (ColorPack)
        public float width; // 월드 단위. <= 0 이면 "스타일 없음"(구버전/스냅샷 폴백)
        public int brush;
    }
    [Serializable] public class StrokePointsPayload { public string strokeId; public float[] xy; }
    [Serializable] public class StrokeEndPayload { public string strokeId; }
    [Serializable] public class EmptyPayload { }
    [Serializable] public class PeerPayload { public string playerId; public string name; public int colorIndex; }

    // 메시지 직렬화/역직렬화 + 프로토콜 순수 판정 (docs/08 §3, §4)
    public static class NetProtocol
    {
        // v3: 게임 메시지 타입 도입 (docs/12 §3). 게임을 모르는 v2와 섞이면 게임 진행이 조용히 깨진다 — 거부가 맞다.
        // v2: StrokeStart/StrokeSnapshot에 스타일 3필드 추가 + StrokeErase 신규 (docs/11 §3).
        // 필드만 추가하고 버전을 유지하면 구버전과 섞였을 때 조용히 틀린 색으로 그려진다 — 거부가 맞다.
        public const int Version = 3;

        public const string TypeHello = "Hello";
        public const string TypeWelcome = "Welcome";
        public const string TypeCursor = "CursorUpdate";
        public const string TypeStrokeStart = "StrokeStart";
        public const string TypeStrokePoints = "StrokePoints";
        public const string TypeStrokeEnd = "StrokeEnd";
        public const string TypeClear = "ClearCanvas";
        public const string TypePeerJoined = "PeerJoined";
        public const string TypePeerLeft = "PeerLeft";
        public const string TypeStrokeErase = "StrokeErase";

        // host가 원본 그대로 자동 중계하는 타입 = 기존 드로잉/커서 전부 (docs/12 §3 화이트리스트).
        // 이 집합 밖은 중계하지 않는다 — 게임 메시지(GuessSubmit 등)가 전원에게 퍼지면 정답이 즉시 유출된다.
        private static readonly HashSet<string> RelayTypes = new HashSet<string>
        {
            TypeCursor, TypeStrokeStart, TypeStrokePoints, TypeStrokeEnd, TypeStrokeErase, TypeClear
        };

        public static bool IsRelayType(string type)
        {
            return RelayTypes.Contains(type);
        }

        // NetSession.Apply가 처리하는 코어 타입 = 중계 6종 + 세션 관리 3종.
        // 세션 관리 3종은 중계 대상이 아니지만 Apply는 반드시 태워야 한다 — 빼면 클라 세션이 통째로 깨진다.
        public static bool IsCoreType(string type)
        {
            return IsRelayType(type) || type == TypeWelcome || type == TypePeerJoined || type == TypePeerLeft;
        }

        // StrokeGate 적용 대상 (docs/12 §2 표 #3). 커서는 제외 — 그리지 못하는 사람도 손은 보여야 한다.
        public static bool IsStrokeType(string type)
        {
            return type == TypeStrokeStart || type == TypeStrokePoints || type == TypeStrokeEnd || type == TypeStrokeErase;
        }

        public static byte[] Encode<T>(string type, string sender, T payload)
        {
            var env = new NetEnvelope
            {
                v = Version,
                type = type,
                sender = sender,
                payload = JsonUtility.ToJson(payload)
            };
            return System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(env));
        }

        // 버전 불일치·파싱 실패·type 공백이면 null (수신 측 폐기, docs/02의 v 검사와 같은 방침)
        public static NetEnvelope Decode(byte[] data)
        {
            try
            {
                var env = JsonUtility.FromJson<NetEnvelope>(System.Text.Encoding.UTF8.GetString(data));
                if (env == null || env.v != Version || string.IsNullOrEmpty(env.type) || env.payload == null)
                {
                    return null; // payload 누락 envelope 폐기 — 수신 루프 예외 방지
                }
                return env;
            }
            catch (Exception)
            {
                return null; // 악의적/손상 패킷 방어: 조용히 폐기하되 호출부가 카운트 가능하게 null 반환
            }
        }

        public static T DecodePayload<T>(NetEnvelope env)
        {
            return JsonUtility.FromJson<T>(env.payload);
        }

        public static string MakeStrokeId(string playerId, int counter)
        {
            return playerId + ":" + counter;
        }

        // 커서 unreliable 채널의 역전/중복 폐기 (PacketFilter.ShouldAccept와 같은 규칙)
        public static bool ShouldAcceptCursor(bool hasLast, uint lastSeq, uint seq)
        {
            return !hasLast || seq > lastSeq;
        }

        public static float[] FlattenPoints(List<Vector2> points)
        {
            var xy = new float[points.Count * 2];
            for (int i = 0; i < points.Count; i++)
            {
                xy[i * 2] = points[i].x;
                xy[i * 2 + 1] = points[i].y;
            }
            return xy;
        }

        public static Vector2[] UnflattenPoints(float[] xy)
        {
            int count = xy.Length / 2; // 홀수 길이는 마지막 값 버림
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            }
            return points;
        }
    }

    // 색을 팔레트 인덱스가 아니라 packed 정수로 보낸다 (docs/11 §3) — 클라이언트 간 팔레트 배열 불일치 위험 제거.
    // 알파까지 싣는다(0xAARRGGBB): 브러시 반투명(Marker)이 원격에서도 같게 보여야 한다.
    internal static class ColorPack
    {
        public static int ToInt(Color color)
        {
            int a = Channel(color.a);
            int r = Channel(color.r);
            int g = Channel(color.g);
            int b = Channel(color.b);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        public static Color FromInt(int packed)
        {
            return new Color(
                ((packed >> 16) & 0xFF) / 255f,
                ((packed >> 8) & 0xFF) / 255f,
                (packed & 0xFF) / 255f,
                ((packed >> 24) & 0xFF) / 255f);
        }

        private static int Channel(float value)
        {
            return Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
        }
    }
}
