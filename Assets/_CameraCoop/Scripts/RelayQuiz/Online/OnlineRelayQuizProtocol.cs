using System;
using System.Text;
using UnityEngine;

namespace CameraCoop
{
    [Serializable]
    public sealed class OnlineRelayQuizPacket
    {
        public string game = OnlineRelayQuizProtocol.GameId;
        public int version = OnlineRelayQuizProtocol.Version;
        public string sessionId;
        public int rosterGeneration;
        public int roundId;
        public int turnId;
        public int ownerSlot = -1;
        public int revision;
        public int selectedMode = -1;
        public int modeGeneration;
        public int startSignal;
        public int transitionGeneration;
        public int transitionPhase;
        public int sceneReadyMask;
        public long sequence;
        public string kind;
        public string payload;
    }

    [Serializable]
    internal sealed class OnlineRelayQuizHello
    {
        public string hostId;
        public string playerId;
        public int brushes;
    }

    [Serializable]
    internal sealed class OnlineRelayQuizWelcome
    {
        public string hostId;
        public string playerId;
        public int assignedSlot;
        public int brushes;
    }

    [Serializable]
    internal sealed class OnlineRelayQuizCommand
    {
        public RelayQuizAction action;
        public bool cameraReady;
        public bool focused;
        public bool freshHand;
        public bool complete;
        public string text;
    }

    [Serializable]
    internal sealed class OnlineRelayQuizPreparedAck
    {
        public string drawingId;
        public int destinationSlot;
        public int ownerSlot;
        public int revision;
    }

    [Serializable]
    internal sealed class OnlineRelayQuizCapture
    {
        public string transferId;
        public int ownerSlot;
        public int revision;
    }

    [Serializable]
    internal sealed class OnlineRelayQuizTransitionCommand
    {
        public int transitionGeneration;
        public int failure;
    }

    [Serializable]
    public sealed class OnlineRelayQuizChunk
    {
        public string id;
        public int index;
        public int count;
        public int total;
        public string data;
    }

    public static class OnlineRelayQuizProtocol
    {
        public const string GameId = "camera-coop-relayquiz-4p";
        public const int Version = 4;
        public const int PlayerCount = 4;
        public const int MaxMessageBytes = 64 * 1024;
        public const int MaxDrawingBytes = 512 * 1024;
        public const int ChunkBytes = 12 * 1024;
        public const int MaxChunks = (MaxDrawingBytes + ChunkBytes - 1) / ChunkBytes;
        public const int MaxStrokes = 512;
        public const int MaxPoints = 32768;
        public const int MaxAnswerCharacters = 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(OnlineRelayQuizPacket packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            byte[] bytes = Utf8.GetBytes(JsonUtility.ToJson(packet));
            if (bytes.Length > MaxMessageBytes) throw new ArgumentException("Online message exceeds 64KiB.");
            return bytes;
        }

        public static bool TryDecode(byte[] bytes, out OnlineRelayQuizPacket packet)
        {
            packet = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxMessageBytes) return false;
            try
            {
                packet = JsonUtility.FromJson<OnlineRelayQuizPacket>(Utf8.GetString(bytes));
                return packet != null && packet.game == GameId && packet.version == Version
                    && packet.sequence > 0 && !string.IsNullOrEmpty(packet.kind) && packet.kind.Length <= 24
                    && packet.payload != null && (packet.sessionId == null || packet.sessionId.Length <= 64)
                    && packet.rosterGeneration >= 0 && packet.roundId >= 0 && packet.turnId >= 0
                    && packet.ownerSlot >= -1 && packet.ownerSlot < PlayerCount && packet.revision >= 0
                    && packet.selectedMode >= -1 && packet.selectedMode <= 2
                    && packet.modeGeneration >= 0 && packet.startSignal >= 0
                    && packet.transitionGeneration >= 0
                    && Party.PartyTransitionPhaseRules.IsDefined(
                        (Party.PartyTransitionPhase)packet.transitionPhase)
                    && packet.sceneReadyMask >= 0
                    && packet.sceneReadyMask < 1 << PlayerCount;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is DecoderFallbackException)
            {
                return false;
            }
        }

        internal static T Read<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (ArgumentException) { return null; }
        }

        public static bool TryDrawing(CanvasDrawingData source, int brushes, out CanvasDrawingData copy)
        {
            copy = null;
            if (source == null || source.strokes == null || source.strokes.Length > MaxStrokes) return false;
            int points = 0;
            foreach (CanvasStrokeData stroke in source.strokes)
            {
                if (stroke == null || stroke.xy == null || stroke.xy.Length > MaxPoints * 2
                    || stroke.widthNormalized > 1f) return false;
                points += stroke.xy.Length / 2;
                if (points > MaxPoints) return false;
            }
            return CanvasDrawingData.TryCopy(source, brushes, out copy, out _);
        }

        internal static bool TryDrawingBytes(CanvasDrawingData source, int brushes, out byte[] bytes)
        {
            bytes = null;
            if (!TryDrawing(source, brushes, out CanvasDrawingData copy)) return false;
            bytes = Utf8.GetBytes(JsonUtility.ToJson(copy));
            return bytes.Length <= MaxDrawingBytes;
        }

        internal static bool TryReadDrawing(byte[] bytes, int brushes, out CanvasDrawingData drawing)
        {
            drawing = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxDrawingBytes) return false;
            try { return TryDrawing(Read<CanvasDrawingData>(Utf8.GetString(bytes)), brushes, out drawing); }
            catch (DecoderFallbackException) { return false; }
        }
    }

    public sealed class OnlineRelayQuizDrawingTransfer
    {
        private string id;
        private byte[][] parts;
        private int total;
        private int received;

        public bool Add(OnlineRelayQuizChunk chunk, out byte[] complete)
        {
            complete = null;
            if (chunk == null || string.IsNullOrEmpty(chunk.id) || chunk.id.Length > 96 || chunk.total <= 0
                || chunk.total > OnlineRelayQuizProtocol.MaxDrawingBytes || chunk.count <= 0
                || chunk.count > OnlineRelayQuizProtocol.MaxChunks || chunk.index < 0 || chunk.index >= chunk.count
                || chunk.count != (chunk.total + OnlineRelayQuizProtocol.ChunkBytes - 1) / OnlineRelayQuizProtocol.ChunkBytes
                || chunk.data == null || chunk.data.Length > OnlineRelayQuizProtocol.ChunkBytes * 4 / 3 + 4) return false;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(chunk.data); }
            catch (FormatException) { return false; }
            int expected = Math.Min(OnlineRelayQuizProtocol.ChunkBytes,
                chunk.total - chunk.index * OnlineRelayQuizProtocol.ChunkBytes);
            if (bytes.Length != expected) return false;
            if (parts == null)
            {
                id = chunk.id;
                total = chunk.total;
                parts = new byte[chunk.count][];
            }
            if (chunk.id != id || chunk.total != total || parts.Length != chunk.count) return false;
            if (parts[chunk.index] != null)
            {
                byte[] old = parts[chunk.index];
                for (int i = 0; i < old.Length; i++) if (old[i] != bytes[i]) return false;
                return true;
            }
            parts[chunk.index] = bytes;
            received++;
            if (received != parts.Length) return true;
            complete = new byte[total];
            for (int i = 0; i < parts.Length; i++)
                Buffer.BlockCopy(parts[i], 0, complete, i * OnlineRelayQuizProtocol.ChunkBytes, parts[i].Length);
            return true;
        }
    }
}
