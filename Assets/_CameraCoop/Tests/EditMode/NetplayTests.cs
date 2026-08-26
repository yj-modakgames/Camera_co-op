using System.Collections.Generic;
using CameraCoop.Netplay;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    // docs/08 §3 network v1 프로토콜 + §6 순수 로직 테스트.
    public class NetplayTests
    {
        // ---- NetProtocol Encode/Decode 왕복 ----

        [Test]
        public void EncodeDecode_CursorRoundtrip()
        {
            var payload = new CursorPayload { hand = "Right", x = 0.25f, y = 0.75f, pinched = true, seq = 42 };
            byte[] data = NetProtocol.Encode(NetProtocol.TypeCursor, "p1", payload);
            NetEnvelope env = NetProtocol.Decode(data);
            Assert.IsNotNull(env);
            Assert.AreEqual(1, env.v);
            Assert.AreEqual(NetProtocol.TypeCursor, env.type);
            Assert.AreEqual("p1", env.sender);
            var back = NetProtocol.DecodePayload<CursorPayload>(env);
            Assert.AreEqual("Right", back.hand);
            Assert.AreEqual(0.25f, back.x);
            Assert.AreEqual(0.75f, back.y);
            Assert.IsTrue(back.pinched);
            Assert.AreEqual(42u, back.seq);
        }

        [Test]
        public void EncodeDecode_WelcomeWithSnapshotRoundtrip()
        {
            var payload = new WelcomePayload
            {
                players = new[] { new PlayerInfo { playerId = "h", name = "Host", colorIndex = 0 } },
                snapshot = new[] { new StrokeSnapshot { strokeId = "h:0", playerId = "h", xy = new[] { 0.1f, 0.2f, 0.3f, 0.4f } } }
            };
            var env = NetProtocol.Decode(NetProtocol.Encode(NetProtocol.TypeWelcome, "h", payload));
            var back = NetProtocol.DecodePayload<WelcomePayload>(env);
            Assert.AreEqual(1, back.players.Length);
            Assert.AreEqual(0, back.players[0].colorIndex);
            Assert.AreEqual("h:0", back.snapshot[0].strokeId);
            Assert.AreEqual(4, back.snapshot[0].xy.Length);
        }

        [Test]
        public void Decode_VersionMismatch_ReturnsNull()
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes("{\"v\":2,\"type\":\"Hello\",\"sender\":\"x\",\"payload\":\"{}\"}");
            Assert.IsNull(NetProtocol.Decode(data));
        }

        [Test]
        public void Decode_MalformedJson_ReturnsNull()
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes("not json at all");
            Assert.IsNull(NetProtocol.Decode(data));
        }

        [Test]
        public void Decode_EmptyType_ReturnsNull()
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes("{\"v\":1,\"type\":\"\",\"sender\":\"x\",\"payload\":\"{}\"}");
            Assert.IsNull(NetProtocol.Decode(data));
        }

        // ---- 커서 seq 폐기 (docs/08 §4) ----

        [Test]
        public void ShouldAcceptCursor_FirstAlwaysAccepts()
        {
            Assert.IsTrue(NetProtocol.ShouldAcceptCursor(hasLast: false, lastSeq: 0, seq: 0));
        }

        [Test]
        public void ShouldAcceptCursor_HigherAccepts()
        {
            Assert.IsTrue(NetProtocol.ShouldAcceptCursor(hasLast: true, lastSeq: 5, seq: 6));
        }

        [Test]
        public void ShouldAcceptCursor_EqualRejects()
        {
            Assert.IsFalse(NetProtocol.ShouldAcceptCursor(hasLast: true, lastSeq: 5, seq: 5));
        }

        [Test]
        public void ShouldAcceptCursor_LowerRejects()
        {
            Assert.IsFalse(NetProtocol.ShouldAcceptCursor(hasLast: true, lastSeq: 5, seq: 4));
        }

        // ---- strokeId / 점 평탄화 ----

        [Test]
        public void MakeStrokeId_Format()
        {
            Assert.AreEqual("p1:7", NetProtocol.MakeStrokeId("p1", 7));
        }

        [Test]
        public void FlattenUnflatten_Roundtrip()
        {
            var pts = new List<Vector2> { new Vector2(0.1f, 0.2f), new Vector2(0.3f, 0.4f) };
            float[] xy = NetProtocol.FlattenPoints(pts);
            Assert.AreEqual(new[] { 0.1f, 0.2f, 0.3f, 0.4f }, xy);
            Vector2[] back = NetProtocol.UnflattenPoints(xy);
            Assert.AreEqual(2, back.Length);
            Assert.AreEqual(pts[1], back[1]);
        }

        [Test]
        public void UnflattenPoints_OddLength_DropsTrailing()
        {
            Vector2[] back = NetProtocol.UnflattenPoints(new[] { 0.1f, 0.2f, 0.9f });
            Assert.AreEqual(1, back.Length);
        }

        // ---- LoopbackTransport (docs/08 §2 — 단일 기기 검증용) ----

        [Test]
        public void Loopback_AddFakePeer_FiresConnected()
        {
            var t = new LoopbackTransport();
            string connected = null;
            t.OnPeerConnected += id => connected = id;
            t.AddFakePeer("fake-1", "P1");
            Assert.AreEqual("fake-1", connected);
            Assert.IsTrue(t.IsHost);
        }

        [Test]
        public void Loopback_FakeSend_DeliveredOnTickOnly()
        {
            var t = new LoopbackTransport();
            var peer = t.AddFakePeer("fake-1", "P1");
            byte[] got = null;
            string from = null;
            t.OnMessage += (id, data) => { from = id; got = data; };
            peer.SendToHost(new byte[] { 7 });
            Assert.IsNull(got); // Tick 전에는 미발화 (큐잉)
            t.Tick();
            Assert.AreEqual("fake-1", from);
            Assert.AreEqual(7, got[0]);
        }

        [Test]
        public void Loopback_SendTo_AppendsToFakeReceived()
        {
            var t = new LoopbackTransport();
            var peer = t.AddFakePeer("fake-1", "P1");
            t.SendTo("fake-1", new byte[] { 9 }, reliable: true);
            Assert.AreEqual(1, peer.Received.Count);
            Assert.AreEqual(9, peer.Received[0][0]);
        }

        [Test]
        public void Loopback_RemoveFakePeer_FiresDisconnected()
        {
            var t = new LoopbackTransport();
            t.AddFakePeer("fake-1", "P1");
            string gone = null;
            t.OnPeerDisconnected += id => gone = id;
            t.RemoveFakePeer("fake-1");
            Assert.AreEqual("fake-1", gone);
        }

        // ---- SessionLogic (docs/08 §2, §3) ----

        [Test]
        public void AssignColorIndex_PicksSmallestFree()
        {
            Assert.AreEqual(0, SessionLogic.AssignColorIndex(new List<int>()));
            Assert.AreEqual(1, SessionLogic.AssignColorIndex(new List<int> { 0 }));
            Assert.AreEqual(1, SessionLogic.AssignColorIndex(new List<int> { 0, 2 }));
            Assert.AreEqual(3, SessionLogic.AssignColorIndex(new List<int> { 0, 1, 2 }));
        }

        [Test]
        public void AssignColorIndex_FullReturnsMinusOne()
        {
            Assert.AreEqual(-1, SessionLogic.AssignColorIndex(new List<int> { 0, 1, 2, 3 }));
        }

        [Test]
        public void BuildSnapshot_IncludesOnlyFinishedStrokes()
        {
            var strokes = new Dictionary<string, NetStroke>
            {
                { "a:0", new NetStroke { playerId = "a", points = new List<Vector2> { Vector2.zero, Vector2.one }, finished = true } },
                { "a:1", new NetStroke { playerId = "a", points = new List<Vector2> { Vector2.zero }, finished = false } }
            };
            StrokeSnapshot[] snap = SessionLogic.BuildSnapshot(strokes);
            Assert.AreEqual(1, snap.Length);
            Assert.AreEqual("a:0", snap[0].strokeId);
            Assert.AreEqual(4, snap[0].xy.Length);
        }
    }
}
