using System;
using System.Text;
using UnityEngine;

namespace CameraCoop.Party
{
    public enum PartyMoveState
    {
        Idle = 0,
        Walking = 1,
        Running = 2
    }

    [Serializable]
    public sealed class PartyPosePacket
    {
        public string game = PartyPoseProtocol.GameId;
        public int version = PartyPoseProtocol.Version;
        public string sessionId;
        public int rosterGeneration;
        public int transitionGeneration;
        public long sequence;
        public string kind;
        public int slot = -1;
        public float positionX;
        public float positionY;
        public float positionZ;
        public float yawDegrees;
        public int moveState;
    }

    public readonly struct PartyPoseSample
    {
        public PartyPoseSample(int slot, Vector3 position, float yawDegrees, PartyMoveState moveState, long sequence)
        {
            Slot = slot;
            Position = position;
            YawDegrees = yawDegrees;
            MoveState = moveState;
            Sequence = sequence;
        }

        public int Slot { get; }
        public Vector3 Position { get; }
        public float YawDegrees { get; }
        public PartyMoveState MoveState { get; }
        public long Sequence { get; }
    }

    public static class PartyPoseProtocol
    {
        public const string GameId = "camera-coop-party-pose";
        public const int Version = 2;
        public const int MaxMessageBytes = 1024;
        public const string KindSubmit = "submit";
        public const string KindRelay = "relay";
        public const string KindRemove = "remove";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(PartyPosePacket packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            byte[] bytes = Utf8.GetBytes(JsonUtility.ToJson(packet));
            if (bytes.Length > MaxMessageBytes) throw new ArgumentException("Party pose message exceeds 1KiB.", nameof(packet));
            return bytes;
        }

        public static bool TryDecode(byte[] bytes, out PartyPosePacket packet)
        {
            packet = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxMessageBytes) return false;
            try
            {
                packet = JsonUtility.FromJson<PartyPosePacket>(Utf8.GetString(bytes));
                return packet != null
                    && packet.game == GameId
                    && packet.version == Version
                    && !string.IsNullOrEmpty(packet.sessionId)
                    && packet.sessionId.Length <= 64
                    && packet.rosterGeneration > 0
                    && packet.transitionGeneration >= 0
                    && packet.sequence > 0
                    && (packet.kind == KindSubmit || packet.kind == KindRelay || packet.kind == KindRemove)
                    && packet.slot >= -1
                    && packet.slot < PartyRoster.Capacity;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is DecoderFallbackException)
            {
                return false;
            }
        }
    }
}
