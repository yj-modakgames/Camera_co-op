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
    [Serializable] public class StrokeSnapshot { public string strokeId; public string playerId; public float[] xy; } // (x,y) 쌍 평탄화
    [Serializable] public class WelcomePayload { public PlayerInfo[] players; public StrokeSnapshot[] snapshot; }
    [Serializable] public class CursorPayload { public string hand; public float x; public float y; public bool pinched; public uint seq; }
    [Serializable] public class StrokeStartPayload { public string strokeId; public string hand; public float x; public float y; }
    [Serializable] public class StrokePointsPayload { public string strokeId; public float[] xy; }
    [Serializable] public class StrokeEndPayload { public string strokeId; }
    [Serializable] public class EmptyPayload { }
    [Serializable] public class PeerPayload { public string playerId; public string name; public int colorIndex; }

    // 메시지 직렬화/역직렬화 + 프로토콜 순수 판정 (docs/08 §3, §4)
    public static class NetProtocol
    {
        public const int Version = 1;

        public const string TypeHello = "Hello";
        public const string TypeWelcome = "Welcome";
        public const string TypeCursor = "CursorUpdate";
        public const string TypeStrokeStart = "StrokeStart";
        public const string TypeStrokePoints = "StrokePoints";
        public const string TypeStrokeEnd = "StrokeEnd";
        public const string TypeClear = "ClearCanvas";
        public const string TypePeerJoined = "PeerJoined";
        public const string TypePeerLeft = "PeerLeft";

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
                if (env == null || env.v != Version || string.IsNullOrEmpty(env.type))
                {
                    return null;
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
}
