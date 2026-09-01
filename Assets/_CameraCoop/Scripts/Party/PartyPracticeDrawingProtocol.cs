using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace CameraCoop.Party
{
    [Serializable]
    public sealed class PartyPracticeDrawingPacket
    {
        public string game = PartyPracticeDrawingProtocol.GameId;
        public int version = PartyPracticeDrawingProtocol.Version;
        public string sessionId;
        public int rosterGeneration;
        public int transitionGeneration;
        public long sequence;
        public string kind;
        public int ownerSlot = -1;
        public int revision;
        public string payload;
    }

    [Serializable]
    public sealed class PartyPracticeDrawingChunk
    {
        public string transferId;
        public int index;
        public int count;
        public int total;
        public string data;
    }

    public static class PartyPracticeDrawingProtocol
    {
        public const string GameId = "camera-coop-party-practice-drawing";
        public const int Version = 1;
        public const int MaxMessageBytes = 64 * 1024;
        public const int MaxDrawingBytes = 512 * 1024;
        public const int ChunkBytes = 12 * 1024;
        public const int MaxChunks = (MaxDrawingBytes + ChunkBytes - 1) / ChunkBytes;
        public const int MaxStrokes = 512;
        public const int MaxPoints = 32768;
        public const string KindSnapshot = "snapshot";
        public const string KindRelay = "relay";
        public const string KindRemove = "remove";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly string[] ForbiddenFields =
            { "word", "answer", "correct", "secret", "private", "reference" };

        public static byte[] Encode(PartyPracticeDrawingPacket packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            byte[] bytes = Utf8.GetBytes(JsonUtility.ToJson(packet));
            if (bytes.Length > MaxMessageBytes)
                throw new ArgumentException("Party practice drawing message exceeds 64KiB.", nameof(packet));
            return bytes;
        }

        public static bool TryDecode(byte[] bytes, out PartyPracticeDrawingPacket packet)
        {
            packet = null;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxMessageBytes) return false;
            try
            {
                string json = Utf8.GetString(bytes);
                if (!HasSafePropertyNames(json)) return false;
                packet = JsonUtility.FromJson<PartyPracticeDrawingPacket>(json);
                if (packet == null || packet.game != GameId || packet.version != Version
                    || string.IsNullOrEmpty(packet.sessionId) || packet.sessionId.Length > 64
                    || packet.rosterGeneration <= 0 || packet.transitionGeneration <= 0
                    || packet.sequence <= 0 || packet.payload == null
                    || packet.ownerSlot < 0 || packet.ownerSlot >= PartyRoster.Capacity)
                {
                    packet = null;
                    return false;
                }

                bool drawing = packet.kind == KindSnapshot || packet.kind == KindRelay;
                bool remove = packet.kind == KindRemove;
                if (!drawing && !remove || drawing && packet.revision <= 0
                    || remove && (packet.revision != 0 || packet.payload != "{}"))
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

        private static bool HasSafePropertyNames(string json)
        {
            try
            {
                using (var text = new StringReader(json))
                using (var reader = new JsonTextReader(text)
                {
                    DateParseHandling = DateParseHandling.None,
                    MaxDepth = 64
                })
                {
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.PropertyName || !(reader.Value is string name))
                            continue;
                        for (int index = 0; index < ForbiddenFields.Length; index++)
                        {
                            if (string.Equals(name, ForbiddenFields[index], StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                    }
                }
                return true;
            }
            catch (JsonReaderException)
            {
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
                string json = Utf8.GetString(bytes);
                if (!HasSafePropertyNames(json)) return false;
                CanvasDrawingData source = JsonUtility.FromJson<CanvasDrawingData>(json);
                return TryCopyDrawing(source, brushCount, out drawing);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is DecoderFallbackException)
            {
                return false;
            }
        }

        internal static bool TryReadChunk(string payload, out PartyPracticeDrawingChunk chunk)
        {
            chunk = null;
            if (string.IsNullOrEmpty(payload)) return false;
            try
            {
                if (!HasSafePropertyNames(payload)) return false;
                chunk = JsonUtility.FromJson<PartyPracticeDrawingChunk>(payload);
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
            if (brushCount <= 0 || source == null || source.strokes == null
                || source.strokes.Length > MaxStrokes)
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

    internal sealed class PartyPracticeDrawingChunkTransfer
    {
        private string transferId;
        private byte[] assembled;
        private int chunkCount;
        private int nextIndex;
        private int total;

        internal bool TryAdd(PartyPracticeDrawingChunk chunk, out byte[] complete)
        {
            complete = null;
            if (chunk == null || string.IsNullOrEmpty(chunk.transferId) || chunk.transferId.Length > 96
                || chunk.total <= 0 || chunk.total > PartyPracticeDrawingProtocol.MaxDrawingBytes
                || chunk.count <= 0 || chunk.count > PartyPracticeDrawingProtocol.MaxChunks
                || chunk.count != (chunk.total + PartyPracticeDrawingProtocol.ChunkBytes - 1)
                    / PartyPracticeDrawingProtocol.ChunkBytes
                || chunk.index < 0 || chunk.index >= chunk.count || chunk.data == null
                || chunk.data.Length > PartyPracticeDrawingProtocol.ChunkBytes * 4 / 3 + 4)
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
            int expected = Math.Min(PartyPracticeDrawingProtocol.ChunkBytes,
                total - chunk.index * PartyPracticeDrawingProtocol.ChunkBytes);
            if (part.Length != expected) return false;

            Buffer.BlockCopy(part, 0, assembled, chunk.index * PartyPracticeDrawingProtocol.ChunkBytes, expected);
            nextIndex++;
            if (nextIndex == chunkCount) complete = assembled;
            return true;
        }
    }
}
