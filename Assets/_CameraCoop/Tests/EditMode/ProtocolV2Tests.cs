using CameraCoop.Netplay;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/11 §3 — network v2: 스타일 3필드 + StrokeErase + packed 색. v1은 폐기돼야 한다.
    public class ProtocolV2Tests
    {
        [Test]
        public void Version_IsTwo()
        {
            Assert.AreEqual(2, NetProtocol.Version);
        }

        [Test]
        public void V1Envelope_IsDiscarded()
        {
            // 구버전 클라이언트가 보낸 v1 envelope — 조용히 틀린 색으로 그리는 대신 폐기 (docs/11 §3)
            var old = new NetEnvelope { v = 1, type = NetProtocol.TypeStrokeStart, sender = "old", payload = "{}" };
            byte[] data = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(old));
            Assert.IsNull(NetProtocol.Decode(data));
        }

        [Test]
        public void ColorPack_RoundTrips_IncludingEndpoints()
        {
            AssertColorRoundTrip(new Color(0f, 0f, 0f, 1f));        // 0xFF000000
            AssertColorRoundTrip(new Color(1f, 1f, 1f, 1f));        // 0xFFFFFFFF
            AssertColorRoundTrip(new Color(0f, 0f, 0f, 0f));        // 0x00000000
            AssertColorRoundTrip(new Color(0.85f, 0.20f, 0.20f, 1f));
            AssertColorRoundTrip(new Color(1f, 0.60f, 0.10f, 0.55f)); // Marker 반투명
        }

        private static void AssertColorRoundTrip(Color color)
        {
            Color back = ColorPack.FromInt(ColorPack.ToInt(color));
            const float tolerance = 1f / 255f; // 8bit 양자화 오차
            Assert.AreEqual(color.r, back.r, tolerance, "r");
            Assert.AreEqual(color.g, back.g, tolerance, "g");
            Assert.AreEqual(color.b, back.b, tolerance, "b");
            Assert.AreEqual(color.a, back.a, tolerance, "a");
        }

        [Test]
        public void ColorPack_PacksChannelsInArgbOrder()
        {
            int packed = ColorPack.ToInt(new Color(1f, 0f, 0f, 1f)); // 불투명 빨강
            Assert.AreEqual(0xFF, (packed >> 24) & 0xFF, "alpha");
            Assert.AreEqual(0xFF, (packed >> 16) & 0xFF, "red");
            Assert.AreEqual(0x00, (packed >> 8) & 0xFF, "green");
            Assert.AreEqual(0x00, packed & 0xFF, "blue");
        }

        [Test]
        public void StrokeStart_V2_CarriesStyleThroughSerialization()
        {
            int packed = ColorPack.ToInt(new Color(0.2f, 0.5f, 0.95f, 0.55f));
            byte[] data = NetProtocol.Encode(NetProtocol.TypeStrokeStart, "p1", new StrokeStartPayload
            {
                strokeId = "p1:3", hand = "Right", x = 0.25f, y = 0.75f,
                color = packed, width = 0.044f, brush = 1
            });
            NetEnvelope env = NetProtocol.Decode(data);
            Assert.IsNotNull(env);
            StrokeStartPayload payload = NetProtocol.DecodePayload<StrokeStartPayload>(env);
            Assert.AreEqual("p1:3", payload.strokeId);
            Assert.AreEqual(0.25f, payload.x, 1e-5f);
            Assert.AreEqual(packed, payload.color);
            Assert.AreEqual(0.044f, payload.width, 1e-6f);
            Assert.AreEqual(1, payload.brush);
        }

        [Test]
        public void StrokeErase_RoundTrips()
        {
            byte[] data = NetProtocol.Encode(NetProtocol.TypeStrokeErase, "p1", new StrokeErasePayload { strokeId = "p1:7" });
            NetEnvelope env = NetProtocol.Decode(data);
            Assert.IsNotNull(env);
            Assert.AreEqual(NetProtocol.TypeStrokeErase, env.type);
            Assert.AreEqual("p1:7", NetProtocol.DecodePayload<StrokeErasePayload>(env).strokeId);
        }

        [Test]
        public void StrokeErase_IsNotTheUnreliableCursorType()
        {
            // host 중계(RelayRaw)는 CursorUpdate만 unreliable로 내린다 — StrokeErase는 reliable ordered로 가야 한다.
            // 유실되면 지운 선이 남는다 (ClearCanvas와 같은 취급, docs/11 §3)
            Assert.AreNotEqual(NetProtocol.TypeCursor, NetProtocol.TypeStrokeErase);
        }

        [Test]
        public void Snapshot_V2_CarriesStyle()
        {
            byte[] data = NetProtocol.Encode(NetProtocol.TypeWelcome, "host", new WelcomePayload
            {
                players = new PlayerInfo[0],
                snapshot = new[]
                {
                    new StrokeSnapshot { strokeId = "host:0", playerId = "host", xy = new[] { 0.1f, 0.2f, 0.3f, 0.4f }, color = 0x11223344, width = 0.03f, brush = 2 }
                }
            });
            WelcomePayload payload = NetProtocol.DecodePayload<WelcomePayload>(NetProtocol.Decode(data));
            Assert.AreEqual(1, payload.snapshot.Length);
            Assert.AreEqual(0x11223344, payload.snapshot[0].color);
            Assert.AreEqual(0.03f, payload.snapshot[0].width, 1e-6f);
            Assert.AreEqual(2, payload.snapshot[0].brush);
        }
    }
}
