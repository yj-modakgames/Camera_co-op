using System.Collections.Generic;
using System.Reflection;
using CameraCoop.Netplay;
using NUnit.Framework;
using UnityEditor;
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
            Assert.AreEqual(NetProtocol.Version, env.v);
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
        public void CursorSender_UsesPalmCenterForBothHands()
        {
            var rig = new GameObject("network hand aim test");
            rig.SetActive(false);
            try
            {
                var receiver = rig.AddComponent<UdpHandReceiver>();
                var session = rig.AddComponent<NetSession>();
                var transport = new LoopbackTransport(false, "local-client");
                var hands = new[]
                {
                    new HandData { handedness = "Left", landmarks = new float[63], pinch = 0.9f },
                    new HandData { handedness = "Right", landmarks = new float[63], pinch = 0.15f }
                };
                Vector2[] palms = { new Vector2(0.3f, 0.4f), new Vector2(0.7f, 0.6f) };
                for (int hand = 0; hand < hands.Length; hand++)
                {
                    foreach (int index in new[] { 0, 5, 9, 13, 17 })
                    {
                        hands[hand].landmarks[index * 3] = palms[hand].x;
                        hands[hand].landmarks[index * 3 + 1] = palms[hand].y;
                    }
                }
                typeof(UdpHandReceiver).GetProperty("LatestPacket").SetValue(receiver, new HandPacket { v = 1, seq = 1, hands = hands });
                var serialized = new SerializedObject(session);
                serialized.FindProperty("receiver").objectReferenceValue = receiver;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(NetSession).GetField("transport", flags).SetValue(session, transport);
                typeof(NetSession).GetField("lastCursorSendTime", flags).SetValue(session, float.NegativeInfinity);

                typeof(NetSession).GetMethod("SendCursorIfDue", flags).Invoke(session, null);

                Assert.AreEqual(2, transport.SentToHost.Count);
                for (int hand = 0; hand < hands.Length; hand++)
                {
                    var payload = NetProtocol.DecodePayload<CursorPayload>(NetProtocol.Decode(transport.SentToHost[hand]));
                    Assert.AreEqual(hands[hand].handedness, payload.hand);
                    Assert.AreEqual(palms[hand].x, payload.x, 0.0001f);
                    Assert.AreEqual(palms[hand].y, payload.y, 0.0001f);
                }
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
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
            byte[] data = System.Text.Encoding.UTF8.GetBytes("{\"v\":3,\"type\":\"Hello\",\"sender\":\"x\",\"payload\":\"{}\"}"); // v2가 현재 버전 (docs/11 §3)
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

        [Test]
        public void Decode_MissingPayload_ReturnsNull()
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes("{\"v\":1,\"type\":\"Hello\",\"sender\":\"x\"}");
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
            peer.Send(new byte[] { 7 });
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

        // ---- client role (docs/09 3b 진입 조건 — Hello 송신·Welcome 적용·host 이탈·커서 seq 게이트) ----

        private const string HostId = "host";
        private NetSession session;
        private LoopbackTransport client;
        private LoopbackTransport.FakePeer hostPeer;

        // 로컬이 클라, 가짜 host 1명이 연결된 상태를 만든다. cursorController 미할당은 의도적 (로컬 송신 경로 미사용).
        private void StartClient()
        {
            session = new GameObject("NetSessionTest").AddComponent<NetSession>();
            client = new LoopbackTransport(isHost: false, localPlayerId: "cli");
            session.StartSession(client, "Cli");
            hostPeer = client.AddFakePeer(HostId, "Host");
        }

        private void DeliverFromHost<T>(string type, T payload)
        {
            hostPeer.Send(NetProtocol.Encode(type, HostId, payload));
            client.Tick(); // EditMode에는 Update가 없으므로 직접 펌프
        }

        [TearDown]
        public void TearDown()
        {
            if (session != null)
            {
                UnityEngine.Object.DestroyImmediate(session.gameObject);
            }
            session = null;
            client = null;
            hostPeer = null;
        }

        [Test]
        public void Client_HostConnected_SendsHelloWithLocalName()
        {
            StartClient();
            Assert.AreEqual(1, client.SentToHost.Count);
            NetEnvelope env = NetProtocol.Decode(client.SentToHost[0]);
            Assert.AreEqual(NetProtocol.TypeHello, env.type);
            Assert.AreEqual("cli", env.sender);
            Assert.AreEqual("Cli", NetProtocol.DecodePayload<HelloPayload>(env).name);
        }

        [Test]
        public void Client_Welcome_AppliesPlayersAndReplaysSnapshot()
        {
            StartClient();
            var starts = new List<string>();
            var ends = new List<string>();
            Vector2[] rest = null;
            session.OnRemoteStrokeStart += (id, sender, p, style) => starts.Add(id);
            session.OnRemoteStrokePoints += (id, pts) => rest = pts;
            session.OnRemoteStrokeEnd += id => ends.Add(id);

            DeliverFromHost(NetProtocol.TypeWelcome, new WelcomePayload
            {
                players = new[]
                {
                    new PlayerInfo { playerId = HostId, name = "Host", colorIndex = 0 },
                    new PlayerInfo { playerId = "cli", name = "Cli", colorIndex = 1 }
                },
                snapshot = new[]
                {
                    new StrokeSnapshot { strokeId = "host:0", playerId = HostId, xy = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f } }
                }
            });

            Assert.AreEqual(2, session.Players.Count);
            Assert.AreEqual(1, session.Players["cli"].colorIndex);
            Assert.AreEqual(new[] { "host:0" }, starts.ToArray());  // 스냅샷 스트로크 재생
            Assert.AreEqual(2, rest.Length);                        // 첫 점은 Start로 나가고 나머지가 Points
            Assert.AreEqual(new[] { "host:0" }, ends.ToArray());    // 스냅샷은 완결 스트로크
        }

        // ---- v2: 스타일 수신 + StrokeErase (docs/11 §3) ----

        [Test]
        public void Client_StrokeStart_CarriesStyle()
        {
            StartClient();
            StrokeStyle got = default(StrokeStyle);
            session.OnRemoteStrokeStart += (id, sender, p, style) => got = style;

            int packed = ColorPack.ToInt(new Color(1f, 0.6f, 0.1f, 0.55f));
            DeliverFromHost(NetProtocol.TypeStrokeStart, new StrokeStartPayload
            {
                strokeId = "host:2", hand = "Left", x = 0.1f, y = 0.1f,
                color = packed, width = 0.044f, brush = 1
            });

            Assert.AreEqual(packed, got.color);
            Assert.AreEqual(0.044f, got.width, 1e-6f);
            Assert.AreEqual(1, got.brush);
        }

        [Test]
        public void Client_StrokeErase_IsForwarded_AndIdempotent()
        {
            StartClient();
            var erased = new List<string>();
            session.OnRemoteStrokeErased += id => erased.Add(id);

            DeliverFromHost(NetProtocol.TypeStrokeStart, new StrokeStartPayload
            {
                strokeId = "host:1", hand = "Right", x = 0.2f, y = 0.3f, color = 0, width = 0.02f, brush = 0
            });
            DeliverFromHost(NetProtocol.TypeStrokeErase, new StrokeErasePayload { strokeId = "host:1" });
            DeliverFromHost(NetProtocol.TypeStrokeErase, new StrokeErasePayload { strokeId = "host:1" }); // 없는 id 재수신

            Assert.AreEqual(new[] { "host:1", "host:1" }, erased.ToArray()); // 수신 측(RemotePresenter)이 멱등 처리한다
        }

        [Test]
        public void BuildSnapshot_PreservesStyle()
        {
            var strokes = new Dictionary<string, NetStroke>();
            var stroke = new NetStroke
            {
                playerId = "p",
                finished = true,
                style = new StrokeStyle { color = 0x11223344, width = 0.03f, brush = 2 }
            };
            stroke.points.Add(new Vector2(0.1f, 0.2f));
            stroke.points.Add(new Vector2(0.3f, 0.4f));
            strokes["p:0"] = stroke;

            StrokeSnapshot[] snap = SessionLogic.BuildSnapshot(strokes);
            Assert.AreEqual(1, snap.Length);
            Assert.AreEqual(0x11223344, snap[0].color);
            Assert.AreEqual(0.03f, snap[0].width, 1e-6f);
            Assert.AreEqual(2, snap[0].brush);
        }

        [Test]
        public void Client_HostDisconnected_StopsSession()
        {
            StartClient();
            Assert.IsTrue(session.IsRunning);
            client.RemoveFakePeer(HostId);
            Assert.IsFalse(session.IsRunning); // 클라의 직결 피어는 host뿐 (docs/08 §4)
        }

        [Test]
        public void Client_Cursor_StaleSeqIgnored()
        {
            StartClient();
            var xs = new List<float>();
            session.OnRemoteCursor += (id, hand, pos, pinched) => xs.Add(pos.x);
            DeliverFromHost(NetProtocol.TypeCursor, new CursorPayload { hand = "Right", x = 0.5f, y = 0.5f, seq = 5 });
            DeliverFromHost(NetProtocol.TypeCursor, new CursorPayload { hand = "Right", x = 0.9f, y = 0.5f, seq = 4 }); // 역주행 폐기
            DeliverFromHost(NetProtocol.TypeCursor, new CursorPayload { hand = "Right", x = 0.7f, y = 0.5f, seq = 6 });
            Assert.AreEqual(new[] { 0.5f, 0.7f }, xs.ToArray());
        }
    }
}
