using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using CameraCoop.Netplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CameraCoop.Tests
{
    // docs/12 §2 표 6건 — NetSession이 게임을 모른 채 여는 "게임 메시지 통로".
    // 타입은 두 집합으로 나뉜다: 자동 중계 6종(RelayTypes) / 코어 Apply 9종(중계 6종 + Welcome·PeerJoined·PeerLeft).
    // 두 집합 밖 = 게임 메시지 → 중계 없이 OnGameMessage로만 나간다.
    public class GameChannelTests
    {
        // 게임 계층을 참조하지 않고도 통로를 검증하기 위한 임의 payload (NetSession은 이 타입을 모른다)
        [Serializable]
        public class TestPayload
        {
            public string text;
            public int n;
        }

        private const string HostId = "host";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        private NetSession session;
        private LoopbackTransport transport;
        private LoopbackTransport.FakePeer peerA;
        private LoopbackTransport.FakePeer peerB;
        private LoopbackTransport.FakePeer hostPeer;

        // 로컬이 host, 가짜 클라 2명이 Hello까지 마친 상태
        private void StartHost()
        {
            session = new GameObject("NetSessionHostTest").AddComponent<NetSession>();
            transport = new LoopbackTransport(); // (true, "local-host")
            session.StartSession(transport, "Host");
            peerA = transport.AddFakePeer("a", "A");
            peerB = transport.AddFakePeer("b", "B");
            Deliver(peerA, NetProtocol.TypeHello, "a", new HelloPayload { name = "A" });
            Deliver(peerB, NetProtocol.TypeHello, "b", new HelloPayload { name = "B" });
        }

        // 로컬이 클라, 가짜 host 1명이 연결된 상태 (NetplayTests.StartClient와 같은 패턴)
        private void StartClient()
        {
            session = new GameObject("NetSessionClientTest").AddComponent<NetSession>();
            transport = new LoopbackTransport(isHost: false, localPlayerId: "cli");
            session.StartSession(transport, "Cli");
            hostPeer = transport.AddFakePeer(HostId, "Host");
        }

        private void Deliver<T>(LoopbackTransport.FakePeer peer, string type, string sender, T payload)
        {
            peer.Send(NetProtocol.Encode(type, sender, payload));
            transport.Tick(); // EditMode에는 Update가 없으므로 직접 펌프
        }

        private static int CountReceived(LoopbackTransport.FakePeer peer, string type)
        {
            int count = 0;
            for (int i = 0; i < peer.Received.Count; i++)
            {
                NetEnvelope env = NetProtocol.Decode(peer.Received[i]);
                if (env != null && env.type == type)
                {
                    count++;
                }
            }
            return count;
        }

        private static NetEnvelope LastReceived(LoopbackTransport.FakePeer peer, string type)
        {
            for (int i = peer.Received.Count - 1; i >= 0; i--)
            {
                NetEnvelope env = NetProtocol.Decode(peer.Received[i]);
                if (env != null && env.type == type)
                {
                    return env;
                }
            }
            return null;
        }

        private static StrokeStartPayload Start(string strokeId)
        {
            return new StrokeStartPayload { strokeId = strokeId, hand = "Right", x = 0.2f, y = 0.3f, color = 0, width = 0.02f, brush = 0 };
        }

        [TearDown]
        public void TearDown()
        {
            if (session != null)
            {
                UnityEngine.Object.DestroyImmediate(session.gameObject);
            }
            session = null;
            transport = null;
            peerA = null;
            peerB = null;
            hostPeer = null;
        }

        // ---- 중계 화이트리스트 (docs/12 §2 표 #1) ----

        [Test]
        public void Host_WhitelistedStrokeStart_IsRelayedAndApplied()
        {
            StartHost();
            var games = new List<string>();
            var starts = new List<string>();
            session.OnGameMessage += (type, sender, json) => games.Add(type);
            session.OnRemoteStrokeStart += (id, sender, p, style) => starts.Add(id);

            Deliver(peerA, NetProtocol.TypeStrokeStart, "a", Start("a:0"));

            Assert.AreEqual(1, CountReceived(peerB, NetProtocol.TypeStrokeStart), "다른 클라에 중계");
            Assert.AreEqual(0, CountReceived(peerA, NetProtocol.TypeStrokeStart), "발신자에게는 되돌리지 않는다");
            Assert.AreEqual(new[] { "a:0" }, starts.ToArray(), "host 자신도 적용");
            Assert.AreEqual(0, games.Count, "코어 타입은 OnGameMessage로 새지 않는다");
        }

        [Test]
        public void Host_UnknownType_IsNotRelayed_AndFiresOnGameMessage()
        {
            StartHost();
            string gotType = null;
            string gotSender = null;
            string gotJson = null;
            session.OnGameMessage += (type, sender, json) => { gotType = type; gotSender = sender; gotJson = json; };

            var payload = new TestPayload { text = "사과", n = 7 };
            Deliver(peerA, "GuessSubmit", "a", payload);

            Assert.AreEqual(0, CountReceived(peerB, "GuessSubmit"), "게임 메시지 자동 중계는 정답 유출 경로다 (docs/12 §2)");
            Assert.AreEqual("GuessSubmit", gotType);
            Assert.AreEqual("a", gotSender);
            Assert.AreEqual(JsonUtility.ToJson(payload), gotJson, "payload JSON 그대로 전달");
        }

        [Test]
        public void Client_UnknownTypeFromHost_FiresOnGameMessage()
        {
            StartClient();
            string gotType = null;
            string gotSender = null;
            string gotJson = null;
            session.OnGameMessage += (type, sender, json) => { gotType = type; gotSender = sender; gotJson = json; };

            var payload = new TestPayload { text = "RoundBegin", n = 3 };
            Deliver(hostPeer, "RoundBegin", HostId, payload);

            Assert.AreEqual("RoundBegin", gotType);
            Assert.AreEqual(HostId, gotSender);
            Assert.AreEqual(JsonUtility.ToJson(payload), gotJson);
        }

        // 회귀 가드: 화이트리스트에 없어도 Welcome·PeerJoined·PeerLeft는 반드시 Apply를 탄다 (빼먹으면 클라 세션이 통째로 깨진다)
        [Test]
        public void Client_SessionCoreTypes_StillApplied_AndNotRoutedToGameMessage()
        {
            StartClient();
            var games = new List<string>();
            session.OnGameMessage += (type, sender, json) => games.Add(type);

            Deliver(hostPeer, NetProtocol.TypeWelcome, HostId, new WelcomePayload
            {
                players = new[] { new PlayerInfo { playerId = HostId, name = "Host", colorIndex = 0 } },
                snapshot = new StrokeSnapshot[0]
            });
            Deliver(hostPeer, NetProtocol.TypePeerJoined, HostId, new PeerPayload { playerId = "p2", name = "P2", colorIndex = 2 });
            Assert.AreEqual(2, session.Players.Count);
            Assert.AreEqual(2, session.Players["p2"].colorIndex);

            Deliver(hostPeer, NetProtocol.TypePeerLeft, HostId, new PeerPayload { playerId = "p2" });
            Assert.AreEqual(1, session.Players.Count);
            Assert.AreEqual(0, games.Count, "세션 관리 3종은 게임 메시지가 아니다");
        }

        // ---- StrokeGate (docs/12 §2 표 #3) ----

        [Test]
        public void Host_StrokeGate_DeniedSender_NoRelayNoApply()
        {
            StartHost();
            var starts = new List<string>();
            session.OnRemoteStrokeStart += (id, sender, p, style) => starts.Add(id);
            session.StrokeGate = playerId => playerId == "allowed";

            Deliver(peerB, NetProtocol.TypeStrokeStart, "b", Start("b:0"));
            Deliver(peerB, NetProtocol.TypeStrokePoints, "b", new StrokePointsPayload { strokeId = "b:0", xy = new[] { 0.4f, 0.4f } });
            Deliver(peerB, NetProtocol.TypeStrokeEnd, "b", new StrokeEndPayload { strokeId = "b:0" });
            Deliver(peerB, NetProtocol.TypeStrokeErase, "b", new StrokeErasePayload { strokeId = "b:0" });

            Assert.AreEqual(0, CountReceived(peerA, NetProtocol.TypeStrokeStart));
            Assert.AreEqual(0, CountReceived(peerA, NetProtocol.TypeStrokePoints));
            Assert.AreEqual(0, CountReceived(peerA, NetProtocol.TypeStrokeEnd));
            Assert.AreEqual(0, CountReceived(peerA, NetProtocol.TypeStrokeErase));
            Assert.AreEqual(0, starts.Count, "거부된 피어의 스트로크는 host에도 반영되지 않는다");
        }

        [Test]
        public void Host_StrokeGate_AllowedSender_Passes()
        {
            StartHost();
            var starts = new List<string>();
            session.OnRemoteStrokeStart += (id, sender, p, style) => starts.Add(id);
            session.StrokeGate = playerId => playerId == "a";

            Deliver(peerA, NetProtocol.TypeStrokeStart, "a", Start("a:0"));

            Assert.AreEqual(1, CountReceived(peerB, NetProtocol.TypeStrokeStart));
            Assert.AreEqual(new[] { "a:0" }, starts.ToArray());
        }

        [Test]
        public void Host_StrokeGate_DoesNotBlockCursor()
        {
            StartHost();
            var cursors = new List<string>();
            session.OnRemoteCursor += (id, hand, pos, pinched) => cursors.Add(id);
            session.StrokeGate = playerId => false; // 전원 거부

            Deliver(peerA, NetProtocol.TypeCursor, "a", new CursorPayload { hand = "Right", x = 0.5f, y = 0.5f, seq = 1 });

            Assert.AreEqual(1, CountReceived(peerB, NetProtocol.TypeCursor), "커서는 게이트 대상이 아니다 (관전자도 손은 보인다)");
            Assert.AreEqual(new[] { "a" }, cursors.ToArray());
        }

        [Test]
        public void Host_StrokeGate_BlocksLocalStrokeStart()
        {
            StartHost();
            session.StrokeGate = playerId => playerId != "local-host";
            int before = peerA.Received.Count;

            typeof(NetSession).GetMethod("HandleLocalStrokeStart", Flags)
                .Invoke(session, new object[] { "Right", new Vector2(0.5f, 0.5f), Vector3.zero });

            var strokes = (Dictionary<string, NetStroke>)typeof(NetSession).GetField("strokes", Flags).GetValue(session);
            Assert.AreEqual(0, strokes.Count, "거부되면 로컬 스트로크 상태도 만들지 않는다");
            Assert.AreEqual(before, peerA.Received.Count, "송신도 없다");
        }

        // ---- HostPlayerId (docs/12 §2 표 #6) ----

        [Test]
        public void HostPlayerId_Host_IsLocalId_AndNullAfterStop()
        {
            StartHost();
            Assert.AreEqual("local-host", session.HostPlayerId);
            session.StopSession();
            Assert.IsNull(session.HostPlayerId);
        }

        [Test]
        public void HostPlayerId_Client_ComesFromWelcomeSender()
        {
            StartClient();
            Assert.IsNull(session.HostPlayerId, "Welcome 전에는 host를 모른다");

            Deliver(hostPeer, NetProtocol.TypeWelcome, HostId, new WelcomePayload
            {
                players = new[] { new PlayerInfo { playerId = HostId, name = "Host", colorIndex = 0 } },
                snapshot = new StrokeSnapshot[0]
            });

            Assert.AreEqual(HostId, session.HostPlayerId);
            session.StopSession();
            Assert.IsNull(session.HostPlayerId);
        }

        // ---- OnPeerJoinedSession (docs/12 §2 표 #4) ----

        [Test]
        public void OnPeerJoinedSession_Host_FiresOnHello()
        {
            session = new GameObject("NetSessionHostTest").AddComponent<NetSession>();
            transport = new LoopbackTransport();
            session.StartSession(transport, "Host");
            var joined = new List<string>();
            session.OnPeerJoinedSession += id => joined.Add(id);

            peerA = transport.AddFakePeer("a", "A");
            Deliver(peerA, NetProtocol.TypeHello, "a", new HelloPayload { name = "A" });

            Assert.AreEqual(new[] { "a" }, joined.ToArray());
        }

        [Test]
        public void OnPeerJoinedSession_Client_FiresOnPeerJoined()
        {
            StartClient();
            var joined = new List<string>();
            session.OnPeerJoinedSession += id => joined.Add(id);

            Deliver(hostPeer, NetProtocol.TypePeerJoined, HostId, new PeerPayload { playerId = "p2", name = "P2", colorIndex = 2 });

            Assert.AreEqual(new[] { "p2" }, joined.ToArray());
        }

        // ---- 게임 송신 3종 (docs/12 §2 표 #2) ----

        [Test]
        public void Host_SendGameTo_DeliversPayloadToTargetOnly()
        {
            StartHost();
            int beforeB = peerB.Received.Count;

            session.SendGameTo("a", "WordAssign", new TestPayload { text = "사과", n = 1 });

            NetEnvelope env = LastReceived(peerA, "WordAssign");
            Assert.IsNotNull(env);
            Assert.AreEqual("local-host", env.sender);
            Assert.AreEqual("사과", NetProtocol.DecodePayload<TestPayload>(env).text);
            Assert.AreEqual(beforeB, peerB.Received.Count, "대상 1명에게만 간다");
        }

        [Test]
        public void Host_BroadcastGameMsg_SkipsExceptId()
        {
            StartHost();
            int beforeA = peerA.Received.Count;

            session.BroadcastGameMsg("GuessFeed", new TestPayload { text = "오답", n = 2 }, exceptId: "a");

            Assert.AreEqual(beforeA, peerA.Received.Count);
            NetEnvelope env = LastReceived(peerB, "GuessFeed");
            Assert.IsNotNull(env);
            Assert.AreEqual(2, NetProtocol.DecodePayload<TestPayload>(env).n);

            session.BroadcastGameMsg("GuessFeed", new TestPayload { text = "정답", n = 3 }); // exceptId 생략 = 전원
            Assert.AreEqual(1, CountReceived(peerA, "GuessFeed"));
        }

        [Test]
        public void Client_SendGameToHost_EncodesToHost()
        {
            StartClient();
            int before = transport.SentToHost.Count;

            session.SendGameToHost("GuessSubmit", new TestPayload { text = "포도", n = 4 });

            Assert.AreEqual(before + 1, transport.SentToHost.Count);
            NetEnvelope env = NetProtocol.Decode(transport.SentToHost[transport.SentToHost.Count - 1]);
            Assert.AreEqual("GuessSubmit", env.type);
            Assert.AreEqual("cli", env.sender);
            Assert.AreEqual("포도", NetProtocol.DecodePayload<TestPayload>(env).text);
        }

        [Test]
        public void Client_HostOnlySenders_WarnAndNoop()
        {
            StartClient();
            LogAssert.Expect(LogType.Warning, new Regex("NetSession.*host")); // 조용한 실패 금지 (docs/12 §2)
            int before = transport.SentToHost.Count;

            session.BroadcastGameMsg("GuessFeed", new TestPayload { text = "x", n = 0 });
            session.SendGameTo(HostId, "WordAssign", new TestPayload { text = "x", n = 0 });

            Assert.AreEqual(before, transport.SentToHost.Count);
            Assert.AreEqual(0, hostPeer.Received.Count, "클라는 게임 메시지를 브로드캐스트하지 않는다");
        }

        [Test]
        public void Host_SendGameToHost_WarnsAndNoop()
        {
            StartHost();
            LogAssert.Expect(LogType.Warning, new Regex("NetSession.*클라"));
            int before = peerA.Received.Count;

            session.SendGameToHost("GuessSubmit", new TestPayload { text = "x", n = 0 });

            Assert.AreEqual(before, peerA.Received.Count);
        }
    }
}
