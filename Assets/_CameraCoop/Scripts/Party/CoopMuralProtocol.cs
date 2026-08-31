using System;
using System.Text;
using UnityEngine;

namespace CameraCoop.Party
{
    [Serializable]
    public sealed class CoopMuralPacket
    {
        public string game = CoopMuralProtocol.GameId;
        public int version = CoopMuralProtocol.Version;
        public string sessionId;
        public int rosterGeneration;
        public long sequence;
        public string kind;
        public int ownerSlot = -1;
        public int revision;
        public string payload;
    }

    [Serializable]
    public sealed class CoopMuralChunk
    {
        public string transferId;
        public int index;
        public int count;
        public int total;
        public string data;
    }

    public static class CoopMuralProtocol
    {
        public const string GameId = "camera-coop-mural-4p";
        public const int Version = 1;
        public const int MaxMessageBytes = 64 * 1024;
        public const int MaxDrawingBytes = 512 * 1024;
        public const int ChunkBytes = 12 * 1024;
        public const int MaxChunks = (MaxDrawingBytes + ChunkBytes - 1) / ChunkBytes;
        public const int MaxStrokes = 512;
        public const int MaxPoints = 32768;
        public const string KindSubmit = "submit";
        public const string KindRelay = "relay";
        public const string KindTurnComplete = "turn-complete";
        public const string KindTurnAdvanced = "turn-advanced";
        public const string KindAbort = "abort";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(CoopMuralPacket packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            byte[] bytes = Utf8.GetBytes(JsonUtility.ToJson(packet));
            if (bytes.Length > MaxMessageBytes) throw new ArgumentException("Coop mural message exceeds 64KiB.", nameof(packet));
            return bytes;
        }

        public static bool TryDecode(byte[] bytes, out CoopMuralPacket packet)
        {
            packet = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxMessageBytes) return false;
            try
            {
                packet = JsonUtility.FromJson<CoopMuralPacket>(Utf8.GetString(bytes));
                if (packet == null || packet.game != GameId || packet.version != Version
                    || string.IsNullOrEmpty(packet.sessionId) || packet.sessionId.Length > 64
                    || packet.rosterGeneration <= 0 || packet.sequence <= 0
                    || packet.payload == null)
                {
                    packet = null;
                    return false;
                }

                bool snapshot = packet.kind == KindSubmit || packet.kind == KindRelay;
                bool turn = packet.kind == KindTurnComplete || packet.kind == KindTurnAdvanced;
                bool abort = packet.kind == KindAbort;
                if (!snapshot && !turn && !abort
                    || (snapshot || turn) && (packet.ownerSlot < 0 || packet.ownerSlot >= PartyRoster.Capacity || packet.revision <= 0)
                    || abort && (packet.ownerSlot != -1 || packet.revision != 0))
                {
                    packet = null;
                    return false;
                }
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is DecoderFallbackException)
            {
                packet = null;
                return false;
            }
        }

        public static bool TryDrawingBytes(CanvasDrawingData source, int brushCount, out byte[] bytes)
        {
            bytes = null;
            if (!TryCopyDrawing(source, brushCount, out CanvasDrawingData copy)) return false;
            bytes = Utf8.GetBytes(JsonUtility.ToJson(copy));
            if (bytes.Length <= MaxDrawingBytes) return true;
            bytes = null;
            return false;
        }

        public static bool TryReadDrawing(byte[] bytes, int brushCount, out CanvasDrawingData drawing)
        {
            drawing = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxDrawingBytes) return false;
            try
            {
                CanvasDrawingData source = JsonUtility.FromJson<CanvasDrawingData>(Utf8.GetString(bytes));
                return TryCopyDrawing(source, brushCount, out drawing);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is DecoderFallbackException)
            {
                return false;
            }
        }

        internal static bool TryReadChunk(string payload, out CoopMuralChunk chunk)
        {
            chunk = null;
            if (string.IsNullOrEmpty(payload)) return false;
            try
            {
                chunk = JsonUtility.FromJson<CoopMuralChunk>(payload);
                return chunk != null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryCopyDrawing(CanvasDrawingData source, int brushCount, out CanvasDrawingData copy)
        {
            copy = null;
            if (brushCount <= 0 || source == null || source.strokes == null || source.strokes.Length > MaxStrokes)
                return false;

            int points = 0;
            for (int index = 0; index < source.strokes.Length; index++)
            {
                CanvasStrokeData stroke = source.strokes[index];
                if (stroke == null || stroke.xy == null || stroke.widthNormalized > 1f) return false;
                points += stroke.xy.Length / 2;
                if (points > MaxPoints) return false;
            }
            return CanvasDrawingData.TryCopy(source, brushCount, out copy, out _);
        }
    }

    internal sealed class CoopMuralChunkTransfer
    {
        private string transferId;
        private byte[] assembled;
        private int chunkCount;
        private int nextIndex;
        private int total;

        internal bool TryAdd(CoopMuralChunk chunk, out byte[] complete)
        {
            complete = null;
            if (chunk == null || string.IsNullOrEmpty(chunk.transferId) || chunk.transferId.Length > 96
                || chunk.total <= 0 || chunk.total > CoopMuralProtocol.MaxDrawingBytes
                || chunk.count <= 0 || chunk.count > CoopMuralProtocol.MaxChunks
                || chunk.count != (chunk.total + CoopMuralProtocol.ChunkBytes - 1) / CoopMuralProtocol.ChunkBytes
                || chunk.index < 0 || chunk.index >= chunk.count || chunk.data == null
                || chunk.data.Length > CoopMuralProtocol.ChunkBytes * 4 / 3 + 4)
                return false;

            if (assembled == null)
            {
                if (chunk.index != 0) return false;
                transferId = chunk.transferId;
                chunkCount = chunk.count;
                total = chunk.total;
                assembled = new byte[total];
            }
            if (chunk.transferId != transferId || chunk.count != chunkCount || chunk.total != total
                || chunk.index != nextIndex)
                return false;

            byte[] part;
            try { part = Convert.FromBase64String(chunk.data); }
            catch (FormatException) { return false; }
            int expected = Math.Min(CoopMuralProtocol.ChunkBytes,
                total - chunk.index * CoopMuralProtocol.ChunkBytes);
            if (part.Length != expected) return false;

            Buffer.BlockCopy(part, 0, assembled, chunk.index * CoopMuralProtocol.ChunkBytes, expected);
            nextIndex++;
            if (nextIndex == chunkCount) complete = assembled;
            return true;
        }
    }
}
