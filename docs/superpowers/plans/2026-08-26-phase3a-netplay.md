# Phase 3a 네트워킹 기반 (Netplay) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Steam 로비로 모인 최대 4인이 한 캔버스에서 서로의 손 커서와 스트로크를 실시간으로 보는 네트워킹 기반 (게임 규칙 없음).

**Architecture:** host 중계 star topology. transport 추상화(`INetTransport`) 아래 `SteamTransport`(Facepunch.Steamworks)와 `LoopbackTransport`(가짜 피어, 단일 기기 검증)를 둔다. 메시지는 JSON 이벤트 프로토콜(network v1, docs/08 §3). 로컬 입력 파이프라인과 로컬 드로잉은 무수정 — `NetSession`이 기존 이벤트를 추가 구독해 송신하고, `RemotePresenter`가 원격 표시를 담당한다.

**Tech Stack:** Unity 6000.3.15f1 · Facepunch.Steamworks (Steam Lobby + Steam Sockets) · JsonUtility · NUnit EditMode · Unity CLI

**Spec:** `docs/08_netplay.md`

## Global Constraints

- Unity **6000.3.15f1**. 에셋/씬 조작은 `unity cmd`. **recompile·run_tests 전에 반드시 `unity cmd editor_status`로 playMode가 stopped인지 확인하고, playing이면 `unity cmd editor_stop` 먼저** (Play 중 domain reload는 비직렬화 필드를 null로 만들어 NRE burst — 이 프로젝트에서 실증된 함정).
- .cs 변경 후: `unity cmd recompile` → `unity cmd recompile_status` 폴링 (completed/up_to_date) → `unity cmd run_tests --mode EditMode --timeout 120`. 에러 확인: `unity cmd console --tail 20 --level error` (stale 에러는 timestamp로 구분).
- **새 Input System 전용** (`activeInputHandler: 1`): legacy `UnityEngine.Input`·`StandaloneInputModule` 사용 금지. uGUI 입력은 `InputSystemUIInputModule`.
- 좌표는 네트워크에서 **정규화 [0,1], 원점 좌상단** (docs/02 §3와 동일). 화면 픽셀 좌표를 네트워크에 싣지 마라.
- 식별자 English, 주석 짧은 한국어. 프레임 경로 LINQ/`GetComponent`/`Find` 금지. 참조는 `[SerializeField]` 직접 할당.
- 신규 코드는 `Assets/_CameraCoop/Scripts/Netplay/`, namespace `CameraCoop.Netplay`, 기존 `CameraCoop.Runtime` asmdef 소속. `[assembly: InternalsVisibleTo("CameraCoop.Tests.EditMode")]`는 HandCursorController.cs:7에 이미 선언됨.
- 기존 파일 수정은 계획이 명시한 것만: `DrawingController.ClearAll` 접근 제한자 1건 (Task 4), `docs/04_unity_client.md` 1건 (Task 4). 기존 씬 3개 무수정.
- `.meta` 파일 (폴더 포함) 커밋 누락 금지 — 커밋 전 `git status` 확인.
- 커밋 메시지 끝 2줄 필수:
  `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
  `Claude-Session: https://claude.ai/code/session_01G2qzFDtBXdJwNyvJ3C33ia`
- branch `phase3a-netplay`에서 작업 (controller가 생성). main 직접 커밋 금지.

---

### Task 1: NetProtocol 순수 로직 + 테스트

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Netplay/NetProtocol.cs`
- Test: `Assets/_CameraCoop/Tests/EditMode/NetplayTests.cs` (신규 파일)

**Interfaces:**
- Consumes: 없음 (UnityEngine.Vector2, JsonUtility만)
- Produces (Task 3·5·7이 이 시그니처 그대로 호출):
  `NetEnvelope { int v; string type; string sender; string payload; }` ·
  payload 클래스들 (아래 코드) ·
  `byte[] NetProtocol.Encode<T>(string type, string sender, T payload)` ·
  `NetEnvelope NetProtocol.Decode(byte[] data)` (버전 불일치/파싱 실패 시 null) ·
  `T NetProtocol.DecodePayload<T>(NetEnvelope env)` ·
  `string NetProtocol.MakeStrokeId(string playerId, int counter)` ·
  `bool NetProtocol.ShouldAcceptCursor(bool hasLast, uint lastSeq, uint seq)` ·
  `float[] NetProtocol.FlattenPoints(List<Vector2>)` / `Vector2[] NetProtocol.UnflattenPoints(float[])` ·
  타입 상수 `TypeHello/TypeWelcome/TypeCursor/TypeStrokeStart/TypeStrokePoints/TypeStrokeEnd/TypeClear/TypePeerJoined/TypePeerLeft`

- [ ] **Step 1: 실패하는 테스트 작성**

`Assets/_CameraCoop/Tests/EditMode/NetplayTests.cs` 생성:

```csharp
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
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: playMode stopped 확인 → `unity cmd recompile` → status 폴링.
Expected: 컴파일 에러 (CS0246 — `CameraCoop.Netplay` 미존재). `unity cmd console --tail 20 --level error`로 확인.

- [ ] **Step 3: 구현 작성**

`Assets/_CameraCoop/Scripts/Netplay/NetProtocol.cs` 생성:

```csharp
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
```

- [ ] **Step 4: 테스트 통과 확인**

Run: recompile → status 폴링 → `unity cmd run_tests --mode EditMode --timeout 120`
Expected: Total 60, Failed 0 (기존 48 + 신규 12)

- [ ] **Step 5: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Netplay Assets/_CameraCoop/Tests/EditMode/NetplayTests.cs*
git commit -m "feat: network v1 프로토콜 직렬화 + 순수 판정 (docs/08 §3)"
```
(`Netplay.meta`·`NetProtocol.cs.meta`·`NetplayTests.cs.meta` 포함 확인)

---

### Task 2: INetTransport + LoopbackTransport + 테스트

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Netplay/INetTransport.cs`
- Create: `Assets/_CameraCoop/Scripts/Netplay/LoopbackTransport.cs`
- Modify: `Assets/_CameraCoop/Tests/EditMode/NetplayTests.cs` (테스트 추가)

**Interfaces:**
- Consumes: 없음
- Produces (Task 3·5·7이 그대로 사용):

```csharp
public interface INetTransport
{
    bool IsHost { get; }
    string LocalPlayerId { get; }
    event Action<string> OnPeerConnected;     // 직결 피어 playerId
    event Action<string> OnPeerDisconnected;
    event Action<string, byte[]> OnMessage;   // (직결 senderId, data)
    void SendToHost(byte[] data, bool reliable);              // 클라 전용
    void SendTo(string playerId, byte[] data, bool reliable); // host 전용
    void Tick();      // 매 프레임 호출 (수신 펌프)
    void Shutdown();
}
```

`LoopbackTransport : INetTransport` — 항상 host (`LocalPlayerId = "local-host"`). `FakePeer AddFakePeer(string id, string name)` / `RemoveFakePeer(string id)`. `FakePeer.SendToHost(byte[])`는 내부 큐에 쌓이고 **다음 `Tick()`에서** `OnMessage` 발화 (동기 재진입 방지). host의 `SendTo(id, ...)`는 `FakePeer.Received` 리스트에 쌓인다.

- [ ] **Step 1: 실패하는 테스트 추가**

`NetplayTests.cs` 끝에 추가:

```csharp
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
```

- [ ] **Step 2: 실패 확인**

Run: recompile → 컴파일 에러 (LoopbackTransport 미존재) 확인.

- [ ] **Step 3: 구현 작성**

`INetTransport.cs`:

```csharp
using System;

namespace CameraCoop.Netplay
{
    // transport 추상화 (docs/08 §2). star topology: 클라는 host에만, host는 각 클라에게.
    public interface INetTransport
    {
        bool IsHost { get; }
        string LocalPlayerId { get; }
        event Action<string> OnPeerConnected;
        event Action<string> OnPeerDisconnected;
        event Action<string, byte[]> OnMessage;
        void SendToHost(byte[] data, bool reliable);
        void SendTo(string playerId, byte[] data, bool reliable);
        void Tick();
        void Shutdown();
    }
}
```

`LoopbackTransport.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace CameraCoop.Netplay
{
    // 가짜 피어 시뮬레이션 (docs/08 §2, §6). Steam 없이 단일 기기에서 4인 검증.
    public class LoopbackTransport : INetTransport
    {
        public class FakePeer
        {
            public string Id;
            public string Name;
            public readonly List<byte[]> Received = new List<byte[]>(); // host가 이 피어에게 보낸 것
            private readonly LoopbackTransport owner;

            internal FakePeer(string id, string name, LoopbackTransport owner)
            {
                Id = id;
                Name = name;
                this.owner = owner;
            }

            // 가짜 피어 -> host 송신. 다음 Tick에서 전달 (동기 재진입 방지)
            public void SendToHost(byte[] data)
            {
                owner.pending.Enqueue((Id, data));
            }
        }

        public bool IsHost { get { return true; } }
        public string LocalPlayerId { get { return "local-host"; } }

        public event Action<string> OnPeerConnected;
        public event Action<string> OnPeerDisconnected;
        public event Action<string, byte[]> OnMessage;

        private readonly Dictionary<string, FakePeer> peers = new Dictionary<string, FakePeer>();
        private readonly Queue<(string senderId, byte[] data)> pending = new Queue<(string, byte[])>();

        public FakePeer AddFakePeer(string id, string name)
        {
            var peer = new FakePeer(id, name, this);
            peers[id] = peer;
            OnPeerConnected?.Invoke(id);
            return peer;
        }

        public void RemoveFakePeer(string id)
        {
            if (peers.Remove(id))
            {
                OnPeerDisconnected?.Invoke(id);
            }
        }

        public void SendToHost(byte[] data, bool reliable)
        {
            // 로컬이 host이므로 사용되지 않는다 (클라 전용 API)
        }

        public void SendTo(string playerId, byte[] data, bool reliable)
        {
            FakePeer peer;
            if (peers.TryGetValue(playerId, out peer))
            {
                peer.Received.Add(data);
            }
        }

        public void Tick()
        {
            while (pending.Count > 0)
            {
                var item = pending.Dequeue();
                OnMessage?.Invoke(item.senderId, item.data);
            }
        }

        public void Shutdown()
        {
            peers.Clear();
            pending.Clear();
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: recompile → `unity cmd run_tests --mode EditMode --timeout 120`
Expected: Total 64, Failed 0 (60 + 4)

- [ ] **Step 5: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Netplay Assets/_CameraCoop/Tests/EditMode/NetplayTests.cs
git commit -m "feat: INetTransport 추상화 + LoopbackTransport (docs/08 §2)"
```

---

### Task 3: SessionLogic 순수 로직 + NetSession

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Netplay/SessionLogic.cs`
- Create: `Assets/_CameraCoop/Scripts/Netplay/NetSession.cs`
- Modify: `Assets/_CameraCoop/Tests/EditMode/NetplayTests.cs` (SessionLogic 테스트 추가)

**Interfaces:**
- Consumes: Task 1의 `NetProtocol`/payload 클래스, Task 2의 `INetTransport` · 기존 `HandCursorController` 이벤트(`OnPinchStart/Move(string,Vector2)`, `OnPinchEnd(string)`), `UdpHandReceiver.LatestPacket`·`IsServerLost`, `HandData.GetLandmark(8)`, `PinchStateMachine.Next(bool,float,float,float)`, `HandScreenMapper.ToScreen`
- Produces (Task 4·5가 사용):

```csharp
public class NetStroke { public string playerId; public List<Vector2> points; public bool finished; }
public static class SessionLogic
{
    public static int AssignColorIndex(List<int> used);          // 0~3 중 가장 작은 빈 값, 꽉 차면 -1
    public static StrokeSnapshot[] BuildSnapshot(Dictionary<string, NetStroke> strokes); // finished만
}
public class NetSession : MonoBehaviour
{
    public void StartSession(INetTransport transport, string localName);
    public void StopSession();
    public void SendClear();                                  // host 전용 (UI 버튼)
    public bool IsRunning { get; }
    public bool IsHost { get; }
    public string LocalPlayerId { get; }
    public IReadOnlyDictionary<string, PlayerInfo> Players { get; }
    public event Action OnPlayersChanged;
    public event Action<string, string, Vector2, bool> OnRemoteCursor;     // playerId, hand, 정규화 좌표, pinched
    public event Action<string, string, Vector2> OnRemoteStrokeStart;      // strokeId, playerId, 정규화 좌표
    public event Action<string, Vector2[]> OnRemoteStrokePoints;           // strokeId, 정규화 좌표들
    public event Action<string> OnRemoteStrokeEnd;
    public event Action OnCanvasCleared;
}
```

- [ ] **Step 1: SessionLogic 실패하는 테스트 추가**

`NetplayTests.cs` 끝에 추가:

```csharp
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
```

- [ ] **Step 2: 실패 확인**

Run: recompile → 컴파일 에러 확인 (SessionLogic 미존재).

- [ ] **Step 3: SessionLogic.cs 작성**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop.Netplay
{
    // 세션의 canonical 스트로크 모델 (로컬 포함 — 늦은 참가 스냅샷의 원본, docs/08 §3)
    public class NetStroke
    {
        public string playerId;
        public List<Vector2> points = new List<Vector2>();
        public bool finished;
    }

    // NetSession의 프레임 무관 판정을 분리한 순수 함수 (docs/04 §5 패턴)
    public static class SessionLogic
    {
        public const int MaxPlayers = 4;

        // 사용 중인 색 인덱스를 피해 가장 작은 값 배정. 꽉 차면 -1 (참가 거절)
        public static int AssignColorIndex(List<int> used)
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                if (!used.Contains(i))
                {
                    return i;
                }
            }
            return -1;
        }

        // 확정 스트로크만 스냅샷으로 변환 (진행 중 스트로크는 이후 실시간 스트림이 담당)
        public static StrokeSnapshot[] BuildSnapshot(Dictionary<string, NetStroke> strokes)
        {
            var list = new List<StrokeSnapshot>();
            foreach (KeyValuePair<string, NetStroke> pair in strokes)
            {
                if (!pair.Value.finished)
                {
                    continue;
                }
                list.Add(new StrokeSnapshot
                {
                    strokeId = pair.Key,
                    playerId = pair.Value.playerId,
                    xy = NetProtocol.FlattenPoints(pair.Value.points)
                });
            }
            return list.ToArray();
        }
    }
}
```

- [ ] **Step 4: NetSession.cs 작성 (전체 코드)**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop.Netplay
{
    // 세션 중심: 로컬 입력 -> 네트워크 송신, 수신 -> 이벤트 발행, host면 중계 + 스냅샷 (docs/08 §2~§4)
    public class NetSession : MonoBehaviour
    {
        [SerializeField] private HandCursorController cursorController;
        [SerializeField] private UdpHandReceiver receiver;
        [SerializeField, Min(1f)] private float cursorSendHz = 15f;
        [SerializeField, Min(0.01f)] private float pointsFlushInterval = 0.1f; // StrokePoints 100ms 배치 (docs/08 §3)
        [SerializeField] private float pinchThreshold = 0.40f;         // Phase 1 실측값과 동일 유지
        [SerializeField] private float pinchReleaseThreshold = 0.60f;

        public bool IsRunning { get { return transport != null; } }
        public bool IsHost { get { return transport != null && transport.IsHost; } }
        public string LocalPlayerId { get { return transport != null ? transport.LocalPlayerId : null; } }
        public IReadOnlyDictionary<string, PlayerInfo> Players { get { return players; } }

        public event Action OnPlayersChanged;
        public event Action<string, string, Vector2, bool> OnRemoteCursor;
        public event Action<string, string, Vector2> OnRemoteStrokeStart;
        public event Action<string, Vector2[]> OnRemoteStrokePoints;
        public event Action<string> OnRemoteStrokeEnd;
        public event Action OnCanvasCleared;

        private INetTransport transport;
        private string localName;
        private readonly Dictionary<string, PlayerInfo> players = new Dictionary<string, PlayerInfo>();
        private readonly Dictionary<string, NetStroke> strokes = new Dictionary<string, NetStroke>();
        private readonly Dictionary<string, uint> lastCursorSeq = new Dictionary<string, uint>(); // key: playerId|hand

        // 로컬 송신 상태
        private int localStrokeCounter;
        private uint localCursorSeq;
        private float lastCursorSendTime;
        private float lastFlushTime;
        private readonly Dictionary<string, string> localActiveStroke = new Dictionary<string, string>();      // hand -> strokeId
        private readonly Dictionary<string, List<Vector2>> pendingPoints = new Dictionary<string, List<Vector2>>(); // strokeId -> 미전송 점
        private readonly Dictionary<string, bool> localPinch = new Dictionary<string, bool>(); // hand -> 핀치 상태 (커서 표시용 재판정)

        public void StartSession(INetTransport newTransport, string name)
        {
            if (transport != null)
            {
                StopSession();
            }
            transport = newTransport;
            localName = name;
            transport.OnPeerConnected += HandlePeerConnected;
            transport.OnPeerDisconnected += HandlePeerDisconnected;
            transport.OnMessage += HandleMessage;

            players.Clear();
            strokes.Clear();
            lastCursorSeq.Clear();
            localActiveStroke.Clear();
            pendingPoints.Clear();
            localPinch.Clear();
            localStrokeCounter = 0;
            localCursorSeq = 0;

            if (transport.IsHost)
            {
                players[transport.LocalPlayerId] = new PlayerInfo { playerId = transport.LocalPlayerId, name = localName, colorIndex = 0 };
            }
            else
            {
                SendToHostMsg(NetProtocol.TypeHello, new HelloPayload { name = localName }, reliable: true);
            }

            cursorController.OnPinchStart += HandleLocalPinchStart;
            cursorController.OnPinchMove += HandleLocalPinchMove;
            cursorController.OnPinchEnd += HandleLocalPinchEnd;

            OnPlayersChanged?.Invoke();
        }

        public void StopSession()
        {
            if (transport == null)
            {
                return;
            }
            cursorController.OnPinchStart -= HandleLocalPinchStart;
            cursorController.OnPinchMove -= HandleLocalPinchMove;
            cursorController.OnPinchEnd -= HandleLocalPinchEnd;
            transport.OnPeerConnected -= HandlePeerConnected;
            transport.OnPeerDisconnected -= HandlePeerDisconnected;
            transport.OnMessage -= HandleMessage;
            transport.Shutdown();
            transport = null;
            players.Clear();
            OnPlayersChanged?.Invoke();
        }

        public void SendClear()
        {
            if (!IsHost)
            {
                return; // 3a에서 Clear는 host 전용 (docs/08 §3)
            }
            strokes.Clear();
            Broadcast(NetProtocol.TypeClear, new EmptyPayload(), reliable: true, exceptId: null);
            OnCanvasCleared?.Invoke();
        }

        private void Update()
        {
            if (transport == null)
            {
                return;
            }
            transport.Tick();
            SendCursorIfDue();
            FlushPendingPointsIfDue();
        }

        private void OnDestroy()
        {
            StopSession();
        }

        // ---- 로컬 커서 송신 (~15Hz, unreliable) ----

        private void SendCursorIfDue()
        {
            if (Time.unscaledTime - lastCursorSendTime < 1f / cursorSendHz)
            {
                return;
            }
            HandPacket packet = receiver != null ? receiver.LatestPacket : null;
            if (packet == null || receiver.IsServerLost || packet.hands == null)
            {
                return; // 손 lost: 송신 정지 -> 수신 측 fade (docs/08 §4)
            }
            lastCursorSendTime = Time.unscaledTime;
            for (int i = 0; i < packet.hands.Length; i++)
            {
                HandData hand = packet.hands[i];
                if (hand == null)
                {
                    continue;
                }
                Vector3 tip = hand.GetLandmark(8);
                bool was;
                localPinch.TryGetValue(hand.handedness, out was);
                bool now = PinchStateMachine.Next(was, hand.pinch, pinchThreshold, pinchReleaseThreshold);
                localPinch[hand.handedness] = now;
                localCursorSeq++;
                var payload = new CursorPayload { hand = hand.handedness, x = tip.x, y = tip.y, pinched = now, seq = localCursorSeq };
                if (IsHost)
                {
                    Broadcast(NetProtocol.TypeCursor, payload, reliable: false, exceptId: null);
                }
                else
                {
                    SendToHostMsg(NetProtocol.TypeCursor, payload, reliable: false);
                }
            }
        }

        // ---- 로컬 스트로크 송신 (이벤트 구독, reliable) ----

        private void HandleLocalPinchStart(string hand, Vector2 screenPos)
        {
            string strokeId = NetProtocol.MakeStrokeId(transport.LocalPlayerId, localStrokeCounter++);
            localActiveStroke[hand] = strokeId;
            Vector2 norm = ToNormalized(screenPos);
            strokes[strokeId] = new NetStroke { playerId = transport.LocalPlayerId, finished = false };
            strokes[strokeId].points.Add(norm);
            pendingPoints[strokeId] = new List<Vector2>();
            SendStrokeMsg(NetProtocol.TypeStrokeStart, new StrokeStartPayload { strokeId = strokeId, hand = hand, x = norm.x, y = norm.y });
        }

        private void HandleLocalPinchMove(string hand, Vector2 screenPos)
        {
            string strokeId;
            if (!localActiveStroke.TryGetValue(hand, out strokeId))
            {
                return;
            }
            Vector2 norm = ToNormalized(screenPos);
            strokes[strokeId].points.Add(norm);
            pendingPoints[strokeId].Add(norm);
        }

        private void HandleLocalPinchEnd(string hand)
        {
            string strokeId;
            if (!localActiveStroke.TryGetValue(hand, out strokeId))
            {
                return;
            }
            localActiveStroke.Remove(hand);
            FlushStroke(strokeId);
            pendingPoints.Remove(strokeId);
            NetStroke stroke = strokes[strokeId];
            if (stroke.points.Count < 2)
            {
                strokes.Remove(strokeId); // 점 찍기 미지원 규칙 동일 적용 (docs/08 §3)
            }
            else
            {
                stroke.finished = true;
            }
            SendStrokeMsg(NetProtocol.TypeStrokeEnd, new StrokeEndPayload { strokeId = strokeId });
        }

        private void FlushPendingPointsIfDue()
        {
            if (Time.unscaledTime - lastFlushTime < pointsFlushInterval)
            {
                return;
            }
            lastFlushTime = Time.unscaledTime;
            foreach (KeyValuePair<string, string> pair in localActiveStroke)
            {
                FlushStroke(pair.Value);
            }
        }

        private void FlushStroke(string strokeId)
        {
            List<Vector2> pending;
            if (!pendingPoints.TryGetValue(strokeId, out pending) || pending.Count == 0)
            {
                return;
            }
            SendStrokeMsg(NetProtocol.TypeStrokePoints, new StrokePointsPayload { strokeId = strokeId, xy = NetProtocol.FlattenPoints(pending) });
            pending.Clear();
        }

        private void SendStrokeMsg<T>(string type, T payload)
        {
            if (IsHost)
            {
                Broadcast(type, payload, reliable: true, exceptId: null);
            }
            else
            {
                SendToHostMsg(type, payload, reliable: true);
            }
        }

        // ---- 수신/중계 ----

        private void HandleMessage(string directSender, byte[] data)
        {
            NetEnvelope env = NetProtocol.Decode(data);
            if (env == null)
            {
                return; // 버전 불일치/손상 폐기
            }
            if (IsHost && env.sender != directSender)
            {
                return; // 위조 방지: star에서 클라의 envelope.sender는 직결 id와 일치해야 한다
            }

            if (env.type == NetProtocol.TypeHello)
            {
                if (IsHost)
                {
                    HandleHello(directSender, NetProtocol.DecodePayload<HelloPayload>(env));
                }
                return;
            }

            // host: 발신자 제외 전원에게 중계 (정본 순서 = host의 중계 순서, docs/08 §1)
            if (IsHost)
            {
                RelayRaw(data, env, exceptId: directSender);
            }
            Apply(env);
        }

        private void HandleHello(string peerId, HelloPayload hello)
        {
            var used = new List<int>();
            foreach (KeyValuePair<string, PlayerInfo> pair in players)
            {
                used.Add(pair.Value.colorIndex);
            }
            int colorIndex = SessionLogic.AssignColorIndex(used);
            if (colorIndex < 0)
            {
                return; // 4인 초과: 무시 (로비가 4인 제한이라 정상 경로에선 발생 안 함)
            }
            var info = new PlayerInfo { playerId = peerId, name = hello.name, colorIndex = colorIndex };
            players[peerId] = info;

            var playerList = new List<PlayerInfo>(players.Values);
            var welcome = new WelcomePayload { players = playerList.ToArray(), snapshot = SessionLogic.BuildSnapshot(strokes) };
            transport.SendTo(peerId, NetProtocol.Encode(NetProtocol.TypeWelcome, transport.LocalPlayerId, welcome), true);
            Broadcast(NetProtocol.TypePeerJoined, new PeerPayload { playerId = peerId, name = hello.name, colorIndex = colorIndex }, reliable: true, exceptId: peerId);
            OnPlayersChanged?.Invoke();
        }

        private void Apply(NetEnvelope env)
        {
            if (env.sender == transport.LocalPlayerId)
            {
                return; // 자기 것 에코 무시
            }
            switch (env.type)
            {
                case NetProtocol.TypeWelcome:
                    var welcome = NetProtocol.DecodePayload<WelcomePayload>(env);
                    players.Clear();
                    for (int i = 0; i < welcome.players.Length; i++)
                    {
                        players[welcome.players[i].playerId] = welcome.players[i];
                    }
                    for (int i = 0; i < welcome.snapshot.Length; i++)
                    {
                        ApplySnapshotStroke(welcome.snapshot[i]);
                    }
                    OnPlayersChanged?.Invoke();
                    break;
                case NetProtocol.TypePeerJoined:
                    var joined = NetProtocol.DecodePayload<PeerPayload>(env);
                    players[joined.playerId] = new PlayerInfo { playerId = joined.playerId, name = joined.name, colorIndex = joined.colorIndex };
                    OnPlayersChanged?.Invoke();
                    break;
                case NetProtocol.TypePeerLeft:
                    var left = NetProtocol.DecodePayload<PeerPayload>(env);
                    players.Remove(left.playerId);
                    OnPlayersChanged?.Invoke();
                    break;
                case NetProtocol.TypeCursor:
                    var cursor = NetProtocol.DecodePayload<CursorPayload>(env);
                    string key = env.sender + "|" + cursor.hand;
                    uint last;
                    bool hasLast = lastCursorSeq.TryGetValue(key, out last);
                    if (!NetProtocol.ShouldAcceptCursor(hasLast, last, cursor.seq))
                    {
                        return;
                    }
                    lastCursorSeq[key] = cursor.seq;
                    OnRemoteCursor?.Invoke(env.sender, cursor.hand, new Vector2(cursor.x, cursor.y), cursor.pinched);
                    break;
                case NetProtocol.TypeStrokeStart:
                    var start = NetProtocol.DecodePayload<StrokeStartPayload>(env);
                    if (strokes.ContainsKey(start.strokeId))
                    {
                        return; // 중복 Start 멱등 (docs/08 §4)
                    }
                    var stroke = new NetStroke { playerId = env.sender, finished = false };
                    stroke.points.Add(new Vector2(start.x, start.y));
                    strokes[start.strokeId] = stroke;
                    OnRemoteStrokeStart?.Invoke(start.strokeId, env.sender, new Vector2(start.x, start.y));
                    break;
                case NetProtocol.TypeStrokePoints:
                    var pts = NetProtocol.DecodePayload<StrokePointsPayload>(env);
                    NetStroke target;
                    if (!strokes.TryGetValue(pts.strokeId, out target) || target.finished)
                    {
                        return; // 고아/종료 후 점 무시
                    }
                    Vector2[] points = NetProtocol.UnflattenPoints(pts.xy);
                    for (int i = 0; i < points.Length; i++)
                    {
                        target.points.Add(points[i]);
                    }
                    OnRemoteStrokePoints?.Invoke(pts.strokeId, points);
                    break;
                case NetProtocol.TypeStrokeEnd:
                    var end = NetProtocol.DecodePayload<StrokeEndPayload>(env);
                    NetStroke ending;
                    if (!strokes.TryGetValue(end.strokeId, out ending))
                    {
                        return;
                    }
                    if (ending.points.Count < 2)
                    {
                        strokes.Remove(end.strokeId);
                    }
                    else
                    {
                        ending.finished = true;
                    }
                    OnRemoteStrokeEnd?.Invoke(end.strokeId);
                    break;
                case NetProtocol.TypeClear:
                    strokes.Clear();
                    OnCanvasCleared?.Invoke();
                    break;
            }
        }

        private void ApplySnapshotStroke(StrokeSnapshot snap)
        {
            if (strokes.ContainsKey(snap.strokeId))
            {
                return; // 멱등
            }
            var stroke = new NetStroke { playerId = snap.playerId, finished = true };
            Vector2[] points = NetProtocol.UnflattenPoints(snap.xy);
            for (int i = 0; i < points.Length; i++)
            {
                stroke.points.Add(points[i]);
            }
            strokes[snap.strokeId] = stroke;
            OnRemoteStrokeStart?.Invoke(snap.strokeId, snap.playerId, points.Length > 0 ? points[0] : Vector2.zero);
            if (points.Length > 1)
            {
                var rest = new Vector2[points.Length - 1];
                Array.Copy(points, 1, rest, 0, rest.Length);
                OnRemoteStrokePoints?.Invoke(snap.strokeId, rest);
            }
            OnRemoteStrokeEnd?.Invoke(snap.strokeId);
        }

        private void HandlePeerConnected(string peerId)
        {
            // Hello 수신 시 players에 등록하므로 여기서는 대기만 (Steam/Loopback 공통)
        }

        private void HandlePeerDisconnected(string peerId)
        {
            if (!IsHost)
            {
                // 클라의 직결 피어는 host뿐 — host 이탈이면 세션 종료 (docs/08 §4)
                StopSession();
                return;
            }
            if (!players.Remove(peerId))
            {
                return;
            }
            // 이탈 피어의 진행 중 스트로크 강제 End (docs/08 §4)
            var toEnd = new List<string>();
            foreach (KeyValuePair<string, NetStroke> pair in strokes)
            {
                if (!pair.Value.finished && pair.Value.playerId == peerId)
                {
                    toEnd.Add(pair.Key);
                }
            }
            for (int i = 0; i < toEnd.Count; i++)
            {
                NetStroke stroke = strokes[toEnd[i]];
                if (stroke.points.Count < 2)
                {
                    strokes.Remove(toEnd[i]);
                }
                else
                {
                    stroke.finished = true;
                }
                OnRemoteStrokeEnd?.Invoke(toEnd[i]);
                Broadcast(NetProtocol.TypeStrokeEnd, new StrokeEndPayload { strokeId = toEnd[i] }, reliable: true, exceptId: peerId);
            }
            Broadcast(NetProtocol.TypePeerLeft, new PeerPayload { playerId = peerId }, reliable: true, exceptId: peerId);
            OnPlayersChanged?.Invoke();
        }

        // ---- 송신 헬퍼 ----

        private void SendToHostMsg<T>(string type, T payload, bool reliable)
        {
            transport.SendToHost(NetProtocol.Encode(type, transport.LocalPlayerId, payload), reliable);
        }

        private void Broadcast<T>(string type, T payload, bool reliable, string exceptId)
        {
            byte[] data = NetProtocol.Encode(type, transport.LocalPlayerId, payload);
            foreach (KeyValuePair<string, PlayerInfo> pair in players)
            {
                if (pair.Key == transport.LocalPlayerId || pair.Key == exceptId)
                {
                    continue;
                }
                transport.SendTo(pair.Key, data, reliable);
            }
        }

        private void RelayRaw(byte[] data, NetEnvelope env, string exceptId)
        {
            foreach (KeyValuePair<string, PlayerInfo> pair in players)
            {
                if (pair.Key == transport.LocalPlayerId || pair.Key == exceptId)
                {
                    continue;
                }
                bool reliable = env.type != NetProtocol.TypeCursor;
                transport.SendTo(pair.Key, data, reliable);
            }
        }

        // 화면 픽셀 -> 정규화 (송신은 항상 정규화 좌표, docs/08 §3). y는 화면 좌하단 원점 -> 좌상단 원점으로 반전.
        private Vector2 ToNormalized(Vector2 screenPos)
        {
            return new Vector2(screenPos.x / Screen.width, 1f - screenPos.y / Screen.height);
        }
    }
}
```

- [ ] **Step 5: 컴파일 + 테스트 통과 확인**

Run: recompile → `unity cmd run_tests --mode EditMode --timeout 120`
Expected: Total 67, Failed 0 (64 + 3)

- [ ] **Step 6: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Netplay Assets/_CameraCoop/Tests/EditMode/NetplayTests.cs
git commit -m "feat: NetSession 세션·중계·스냅샷 + SessionLogic 순수 로직 (docs/08 §2~§4)"
```

---

### Task 4: RemotePresenter + NetplayUI + NetplayTest 씬

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Netplay/RemotePresenter.cs`
- Create: `Assets/_CameraCoop/Scripts/Netplay/NetplayUI.cs`
- Modify: `Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs` (딱 1줄: `private void ClearAll()` → `public void ClearAll()` — 네트워크 Clear가 로컬 표시도 청소해야 캔버스가 일관됨)
- Modify: `docs/04_unity_client.md` (§1에 한 줄 추가: "NetplayTest.unity는 로비 UI가 있어 EventSystem + InputSystemUIInputModule을 사용한다 (docs/08 §5). 커서 전용 씬의 EventSystem 금지는 유지")
- Create: `Assets/_CameraCoop/Scenes/NetplayTest.unity` (`DrawingTest.unity` 복사 후 확장)

**Interfaces:**
- Consumes: Task 3의 `NetSession` 이벤트·프로퍼티 전부 · Task 2의 `LoopbackTransport` · 기존 `HandScreenMapper.ToScreen(x,y,w,h)`, `CursorStateLogic.Scale/StepAlpha`, `DrawingController.ClearAll()`(이 Task에서 public화)
- Produces: Play 가능한 `NetplayTest.unity` — Task 5·7의 검증 대상. `NetplayUI` public 메서드: `OnClickHostLoopback()`, `OnClickClear()` (버튼 배선용)

- [ ] **Step 1: RemotePresenter.cs 작성 (전체 코드)**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop.Netplay
{
    // 원격 플레이어의 커서(uGUI)와 스트로크(LineRenderer)를 표시한다 (docs/08 §2).
    // 로컬 플레이어의 표시는 기존 HandCursorController/DrawingController가 담당 — 여기서는 원격만.
    public class RemotePresenter : MonoBehaviour
    {
        [SerializeField] private NetSession session;
        [SerializeField] private RectTransform cursorPrefab;   // HandCursor.prefab 재사용
        [SerializeField] private Canvas canvas;
        [SerializeField] private Camera drawCamera;
        [SerializeField, Min(0f)] private float planeDistance = 5.0f;   // DrawingController와 동일 값 유지
        [SerializeField, Min(0f)] private float lineWidth = 0.02f;
        [SerializeField] private Material lineMaterial;                  // StrokeLine.mat 공유
        [SerializeField] private Color[] playerPalette = new Color[]     // colorIndex 0~3 (docs/08 §3)
        {
            new Color(0.2f, 0.6f, 1f), new Color(1f, 0.6f, 0.1f),
            new Color(0.3f, 0.9f, 0.4f), new Color(0.9f, 0.3f, 0.8f)
        };
        [SerializeField, Min(0.05f)] private float cursorLostTimeout = 0.5f; // 무수신 fade (docs/08 §4)
        [SerializeField, Min(0f)] private float fadeDuration = 0.2f;

        private class RemoteCursor
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Image image;
            public float lastSeen;
        }

        private readonly Dictionary<string, RemoteCursor> cursors = new Dictionary<string, RemoteCursor>();      // playerId|hand
        private readonly Dictionary<string, LineRenderer> strokeLines = new Dictionary<string, LineRenderer>();  // strokeId

        private void OnEnable()
        {
            session.OnRemoteCursor += HandleCursor;
            session.OnRemoteStrokeStart += HandleStrokeStart;
            session.OnRemoteStrokePoints += HandleStrokePoints;
            session.OnRemoteStrokeEnd += HandleStrokeEnd;
            session.OnCanvasCleared += HandleCleared;
        }

        private void OnDisable()
        {
            session.OnRemoteCursor -= HandleCursor;
            session.OnRemoteStrokeStart -= HandleStrokeStart;
            session.OnRemoteStrokePoints -= HandleStrokePoints;
            session.OnRemoteStrokeEnd -= HandleStrokeEnd;
            session.OnCanvasCleared -= HandleCleared;
        }

        private void Update()
        {
            // 무수신 커서 fade (기존 lostTimeout 패턴)
            foreach (KeyValuePair<string, RemoteCursor> pair in cursors)
            {
                RemoteCursor cursor = pair.Value;
                float target = (Time.unscaledTime - cursor.lastSeen) > cursorLostTimeout ? 0f : 1f;
                cursor.group.alpha = CursorStateLogic.StepAlpha(cursor.group.alpha, target, Time.deltaTime, fadeDuration);
            }
        }

        private Color ColorOf(string playerId)
        {
            PlayerInfo info;
            if (session.Players.TryGetValue(playerId, out info) && info.colorIndex >= 0 && info.colorIndex < playerPalette.Length)
            {
                return playerPalette[info.colorIndex];
            }
            return Color.white;
        }

        private void HandleCursor(string playerId, string hand, Vector2 norm, bool pinched)
        {
            string key = playerId + "|" + hand;
            RemoteCursor cursor;
            if (!cursors.TryGetValue(key, out cursor))
            {
                RectTransform rect = Instantiate(cursorPrefab, canvas.transform);
                rect.name = "RemoteCursor_" + key;
                cursor = new RemoteCursor
                {
                    rect = rect,
                    group = rect.GetComponent<CanvasGroup>(),
                    image = rect.GetComponent<Image>()
                };
                cursors[key] = cursor;
            }
            cursor.lastSeen = Time.unscaledTime;
            cursor.image.color = ColorOf(playerId);
            cursor.rect.position = HandScreenMapper.ToScreen(norm.x, norm.y, Screen.width, Screen.height);
            float scale = CursorStateLogic.Scale(pinched, 0.7f);
            cursor.rect.localScale = new Vector3(scale, scale, scale);
        }

        private void HandleStrokeStart(string strokeId, string playerId, Vector2 norm)
        {
            if (strokeLines.ContainsKey(strokeId))
            {
                return; // 멱등
            }
            var strokeObject = new GameObject("RemoteStroke_" + strokeId);
            strokeObject.transform.SetParent(transform, worldPositionStays: true);
            LineRenderer line = strokeObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = lineWidth;
            line.sharedMaterial = lineMaterial;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            Color color = ColorOf(playerId);
            line.startColor = color;
            line.endColor = color;
            line.positionCount = 1;
            line.SetPosition(0, ToWorld(norm));
            strokeLines[strokeId] = line;
        }

        private void HandleStrokePoints(string strokeId, Vector2[] points)
        {
            LineRenderer line;
            if (!strokeLines.TryGetValue(strokeId, out line))
            {
                return;
            }
            int baseCount = line.positionCount;
            line.positionCount = baseCount + points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                line.SetPosition(baseCount + i, ToWorld(points[i]));
            }
        }

        private void HandleStrokeEnd(string strokeId)
        {
            LineRenderer line;
            if (!strokeLines.TryGetValue(strokeId, out line))
            {
                return;
            }
            if (line.positionCount < 2)
            {
                strokeLines.Remove(strokeId);
                Destroy(line.gameObject); // 점 찍기 폐기 규칙 동일 (docs/08 §3)
            }
        }

        private void HandleCleared()
        {
            foreach (KeyValuePair<string, LineRenderer> pair in strokeLines)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }
            strokeLines.Clear();
        }

        // 정규화 [0,1] (좌상단 원점) -> 화면 -> 드로잉 평면 월드 좌표
        private Vector3 ToWorld(Vector2 norm)
        {
            Vector2 screen = HandScreenMapper.ToScreen(norm.x, norm.y, Screen.width, Screen.height);
            return drawCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, planeDistance));
        }
    }
}
```

- [ ] **Step 2: NetplayUI.cs 작성 (전체 코드)**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop.Netplay
{
    // 최소 로비 UI (docs/08 §5): Loopback host 시작, Clear, 피어 목록 표시.
    // Steam host/join 버튼은 Task 7에서 SteamTransport 연결 후 활성화한다.
    public class NetplayUI : MonoBehaviour
    {
        [SerializeField] private NetSession session;
        [SerializeField] private DrawingController drawingController;
        [SerializeField] private Text statusText;

        private void OnEnable()
        {
            session.OnPlayersChanged += Refresh;
            session.OnCanvasCleared += HandleCleared;
            Refresh();
        }

        private void OnDisable()
        {
            session.OnPlayersChanged -= Refresh;
            session.OnCanvasCleared -= HandleCleared;
        }

        // Loopback 세션 시작 (버튼 배선). 가짜 피어는 Task 5의 검증 스크립트가 붙인다.
        public void OnClickHostLoopback()
        {
            if (session.IsRunning)
            {
                return;
            }
            session.StartSession(new LoopbackTransport(), "LocalHost");
            Refresh();
        }

        public void OnClickClear()
        {
            session.SendClear(); // host 전용. 수신/발신 공통 정리는 HandleCleared에서
        }

        private void HandleCleared()
        {
            drawingController.ClearAll(); // 로컬 표시분도 함께 청소 (docs/08 §3 Clear 일관성)
        }

        private void Refresh()
        {
            if (statusText == null)
            {
                return;
            }
            if (!session.IsRunning)
            {
                statusText.text = "세션 없음 — Host Loopback을 누르세요";
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append(session.IsHost ? "[HOST] " : "[CLIENT] ").Append("players: ").Append(session.Players.Count).AppendLine();
            foreach (var pair in session.Players)
            {
                sb.Append("  ").Append(pair.Value.name).Append(" (#").Append(pair.Value.colorIndex).Append(")").AppendLine();
            }
            statusText.text = sb.ToString();
        }
    }
}
```

- [ ] **Step 3: DrawingController.ClearAll public화 + docs/04 갱신**

- `DrawingController.cs`: `private void ClearAll()` → `public void ClearAll()` (그 외 무수정)
- `docs/04_unity_client.md` §1 관련 문단에 한 줄 추가: `NetplayTest.unity는 로비 UI가 있어 EventSystem + InputSystemUIInputModule을 사용한다 (docs/08 §5). 커서 전용 씬(HandTrackingTest/DrawingTest)의 EventSystem 금지는 유지.`

- [ ] **Step 4: 컴파일 + 기존 테스트 유지 확인**

Run: recompile → `unity cmd run_tests --mode EditMode --timeout 120`
Expected: Total 67, Failed 0. 신규 컴파일 에러 0.

- [ ] **Step 5: 씬 구성**

```bash
unity cmd copy_asset --asset Assets/_CameraCoop/Scenes/DrawingTest.unity --destination Assets/_CameraCoop/Scenes/NetplayTest.unity --confirm true
unity cmd open_scene --path Assets/_CameraCoop/Scenes/NetplayTest.unity
```

scratchpad에 배선 스크립트 `wire_netplay_scene.cs`를 만들어 `unity cmd eval_file --file <절대경로>`로 실행 (**프로젝트에 커밋 금지**):

```csharp
// NetplayTest.unity 배선: NetSession/RemotePresenter/NetplayUI + EventSystem + 로비 UI + 저장
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.name != "NetplayTest") { return "WRONG SCENE: " + scene.name; }

var handTracking = GameObject.Find("/HandTracking");
var canvasGo = GameObject.Find("/Canvas");
var camera = GameObject.Find("/Camera").GetComponent<Camera>();
var cursorController = handTracking.GetComponent<CameraCoop.HandCursorController>();
var receiver = handTracking.GetComponent<CameraCoop.UdpHandReceiver>();
var drawing = handTracking.GetComponent<CameraCoop.DrawingController>();
var lineMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_CameraCoop/Materials/StrokeLine.mat");
var cursorPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_CameraCoop/Prefabs/HandCursor.prefab");

// Netplay 루트
var netGo = GameObject.Find("/Netplay");
if (netGo == null) { netGo = new GameObject("Netplay"); }
var session = netGo.GetComponent<CameraCoop.Netplay.NetSession>() ?? netGo.AddComponent<CameraCoop.Netplay.NetSession>();
var presenter = netGo.GetComponent<CameraCoop.Netplay.RemotePresenter>() ?? netGo.AddComponent<CameraCoop.Netplay.RemotePresenter>();
var ui = netGo.GetComponent<CameraCoop.Netplay.NetplayUI>() ?? netGo.AddComponent<CameraCoop.Netplay.NetplayUI>();

// EventSystem (새 Input System 전용 -> InputSystemUIInputModule 필수, docs/08 §5)
var esGo = GameObject.Find("/EventSystem");
if (esGo == null)
{
    esGo = new GameObject("EventSystem");
    esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
    esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
}

// 로비 UI: 상태 텍스트 + 버튼 2개
UnityEngine.UI.Text MakeText(string name, Vector2 anchoredPos, Vector2 size)
{
    var go = new GameObject(name);
    go.transform.SetParent(canvasGo.transform, false);
    var text = go.AddComponent<UnityEngine.UI.Text>();
    text.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    text.fontSize = 20; text.color = Color.white;
    var rt = go.GetComponent<RectTransform>();
    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
    rt.pivot = new Vector2(0f, 1f);
    rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
    return text;
}
UnityEngine.UI.Button MakeButton(string name, string label, Vector2 anchoredPos)
{
    var go = new GameObject(name);
    go.transform.SetParent(canvasGo.transform, false);
    var img = go.AddComponent<UnityEngine.UI.Image>();
    img.color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
    var btn = go.AddComponent<UnityEngine.UI.Button>();
    var rt = go.GetComponent<RectTransform>();
    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
    rt.pivot = new Vector2(0f, 1f);
    rt.anchoredPosition = anchoredPos; rt.sizeDelta = new Vector2(180f, 40f);
    var text = MakeText(name + "Label", Vector2.zero, new Vector2(180f, 40f));
    text.transform.SetParent(go.transform, false);
    text.alignment = TextAnchor.MiddleCenter; text.text = label;
    return btn;
}
var status = GameObject.Find("/Canvas/StatusText") != null
    ? GameObject.Find("/Canvas/StatusText").GetComponent<UnityEngine.UI.Text>()
    : MakeText("StatusText", new Vector2(10f, -10f), new Vector2(400f, 160f));
UnityEngine.UI.Button hostBtn = GameObject.Find("/Canvas/HostLoopbackButton") == null ? MakeButton("HostLoopbackButton", "Host Loopback", new Vector2(10f, -180f)) : GameObject.Find("/Canvas/HostLoopbackButton").GetComponent<UnityEngine.UI.Button>();
UnityEngine.UI.Button clearBtn = GameObject.Find("/Canvas/ClearButton") == null ? MakeButton("ClearButton", "Clear (host)", new Vector2(10f, -230f)) : GameObject.Find("/Canvas/ClearButton").GetComponent<UnityEngine.UI.Button>();

// SerializedObject 배선
void Wire(Component target, string field, UnityEngine.Object value)
{
    var so = new UnityEditor.SerializedObject(target);
    so.FindProperty(field).objectReferenceValue = value;
    so.ApplyModifiedPropertiesWithoutUndo();
}
Wire(session, "cursorController", cursorController);
Wire(session, "receiver", receiver);
Wire(presenter, "session", session);
Wire(presenter, "cursorPrefab", cursorPrefab.GetComponent<RectTransform>());
Wire(presenter, "canvas", canvasGo.GetComponent<Canvas>());
Wire(presenter, "drawCamera", camera);
Wire(presenter, "lineMaterial", lineMat);
Wire(ui, "session", session);
Wire(ui, "drawingController", drawing);
Wire(ui, "statusText", status);

// 버튼 onClick 영속 배선
UnityEditor.Events.UnityEventTools.AddPersistentListener(hostBtn.onClick, ui.OnClickHostLoopback);
UnityEditor.Events.UnityEventTools.AddPersistentListener(clearBtn.onClick, ui.OnClickClear);

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return "OK";
```

주의: `HandCursor.prefab` 경로가 다르면 `unity cmd find_assets --type GameObject --name HandCursor`로 실제 경로 확인 후 스크립트 수정. `AddPersistentListener` 중복 방지를 위해 스크립트는 1회만 실행 (재실행 필요 시 기존 리스너 수 확인).

- [ ] **Step 6: 배선 검증 + 기존 씬 무결성**

```bash
unity cmd get_serialized_fields --target /Netplay --component NetSession
unity cmd get_serialized_fields --target /Netplay --component RemotePresenter
```
Expected: 참조 필드 전부 non-null. (`--target`이 경로를 거부하면 find_gameobjects로 instanceId 사용)

`git status`로 `DrawingTest.unity`·`HandTrackingTest.unity` 무변경 확인 (변경 시 STOP).

- [ ] **Step 7: 전체 테스트 + Commit**

Run: `unity cmd run_tests --mode EditMode --timeout 120` → 67 pass.

```bash
git add Assets/_CameraCoop/Scripts/Netplay Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs Assets/_CameraCoop/Scenes/NetplayTest.unity* docs/04_unity_client.md
git commit -m "feat: RemotePresenter + NetplayUI + NetplayTest 씬 (docs/08 §2, §5)"
```

---

### Task 5: Loopback 통합 검증 (N-1 ~ N-3)

**Files:**
- 없음 (검증 전용. eval 스크립트는 scratchpad에만 — 커밋 금지)

**Interfaces:**
- Consumes: Task 4의 NetplayTest.unity, `NetSession`/`LoopbackTransport`/`NetProtocol` 전부
- Produces: N-1/N-2/N-3 검증 보고 + 스크린샷

- [ ] **Step 1: Play 진입 + Loopback 세션 시작 (버튼 대신 eval)**

```bash
unity cmd open_scene --path Assets/_CameraCoop/Scenes/NetplayTest.unity
unity cmd editor_play
```

eval로 세션 시작 + 가짜 피어 3명 추가, transport 참조를 static에 보관:

```csharp
var ui = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetplayUI>();
ui.OnClickHostLoopback();
var session = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
// private transport에 리플렉션 접근해 가짜 피어 부착
var tf = session.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var loopback = (CameraCoop.Netplay.LoopbackTransport)tf.GetValue(session);
var p1 = loopback.AddFakePeer("fake-1", "P1");
var p2 = loopback.AddFakePeer("fake-2", "P2");
p1.SendToHost(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-1", new CameraCoop.Netplay.HelloPayload { name = "P1" }));
p2.SendToHost(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-2", new CameraCoop.Netplay.HelloPayload { name = "P2" }));
return "peers added";
```

이후 별도 eval에서 fake 피어의 커서·스트로크 이벤트를 시간차로 재생한다 (eval은 1회 실행이므로, 점진 재생은 `p1.SendToHost(...)`를 프레임 지연 없이 순서대로 여러 번 — StrokeStart → StrokePoints(4점) → StrokeEnd 순서. reliable ordered 큐라 순서 보장):

```csharp
// fake-1: 스트로크 1개 (좌상단 대각선)
string sid = "fake-1:0";
var s = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
var tf2 = s.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var lb = (CameraCoop.Netplay.LoopbackTransport)tf2.GetValue(s);
// AddFakePeer는 이미 됨 — 기존 피어 참조는 다시 만들 수 없으므로 새 피어로 진행해도 무방
var p3 = lb.AddFakePeer("fake-3", "P3");
p3.SendToHost(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-3", new CameraCoop.Netplay.HelloPayload { name = "P3" }));
p3.SendToHost(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokeStart, "fake-3", new CameraCoop.Netplay.StrokeStartPayload { strokeId = "fake-3:0", hand = "Right", x = 0.2f, y = 0.2f }));
p3.SendToHost(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokePoints, "fake-3", new CameraCoop.Netplay.StrokePointsPayload { strokeId = "fake-3:0", xy = new float[] { 0.3f, 0.3f, 0.4f, 0.35f, 0.5f, 0.4f } }));
p3.SendToHost(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeStrokeEnd, "fake-3", new CameraCoop.Netplay.StrokeEndPayload { strokeId = "fake-3:0" }));
p3.SendToHost(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeCursor, "fake-3", new CameraCoop.Netplay.CursorPayload { hand = "Right", x = 0.5f, y = 0.4f, pinched = false, seq = 1 }));
return "fake-3 stroke sent";
```

- [ ] **Step 2: N-1 검증 (원격 스트로크·커서 표시)**

```bash
sleep 2
unity cmd eval --code "
var lines = UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
int remote = 0; foreach (var l in lines) { if (l.gameObject.name.StartsWith(\"RemoteStroke_\")) remote++; }
var cursors = 0; foreach (var img in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Image>(FindObjectsSortMode.None)) { if (img.gameObject.name.StartsWith(\"RemoteCursor_\")) cursors++; }
var s = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
return $\"remoteStrokes={remote} remoteCursors={cursors} players={s.Players.Count}\";
"
```
Expected: `remoteStrokes>=1 remoteCursors>=1 players=4` (host + fake 3). 미달 시 STOP — `unity cmd console --tail 20 --level error` 확인 후 보고.

- [ ] **Step 3: N-2 검증 (늦은 참가 스냅샷)**

새 fake 피어를 추가하고 Hello만 보낸 뒤, 그 피어의 `Received`에서 Welcome을 디코드:

```csharp
var s = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
var tf3 = s.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var lb2 = (CameraCoop.Netplay.LoopbackTransport)tf3.GetValue(s);
var late = lb2.AddFakePeer("fake-late", "Late");
late.SendToHost(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-late", new CameraCoop.Netplay.HelloPayload { name = "Late" }));
return "late joined";
```

```csharp
// 다음 eval (Tick이 돈 뒤): Welcome 수신 확인
var s2 = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
var tf4 = s2.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var lb3 = (CameraCoop.Netplay.LoopbackTransport)tf4.GetValue(s2);
// AddFakePeer로 만든 인스턴스 참조가 없으므로 SendTo 큐를 새 피어로 다시 검증하는 대신,
// host의 strokes 모델과 players 수로 간접 검증 + fake-late가 받은 메시지 수 확인은 Step 1의 p1/p2로 수행
return "see report";
```
(주의: eval 간 변수 공유 불가 — Step 1에서 만든 `p1.Received`를 다시 읽을 수 없다. 따라서 N-2는 다음 방식으로 검증한다: Step 3의 첫 eval에서 `late` 추가와 Hello 송신 후 **같은 eval 안에서** `System.Threading.Thread.Sleep`은 금지(메인 스레드)이므로, Hello 송신 → return 전에 `lb2.Tick()`을 직접 1회 호출해 즉시 처리시키고 → `late.Received.Count`와 첫 메시지의 Decode 결과(type=Welcome, snapshot 길이)를 return 문자열로 뽑는다. Tick 직접 호출은 같은 프레임 내 처리라 안전하다.)

최종 N-2 eval (위 두 개를 합친 완성본 — 이것을 실행):

```csharp
var s = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
var tf = s.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var lb = (CameraCoop.Netplay.LoopbackTransport)tf.GetValue(s);
var late = lb.AddFakePeer("fake-late", "Late");
late.SendToHost(CameraCoop.Netplay.NetProtocol.Encode(CameraCoop.Netplay.NetProtocol.TypeHello, "fake-late", new CameraCoop.Netplay.HelloPayload { name = "Late" }));
lb.Tick(); // 즉시 처리 -> host가 Welcome을 late.Received에 넣는다
if (late.Received.Count == 0) { return "FAIL: no welcome"; }
var env = CameraCoop.Netplay.NetProtocol.Decode(late.Received[0]);
var welcome = CameraCoop.Netplay.NetProtocol.DecodePayload<CameraCoop.Netplay.WelcomePayload>(env);
return $"welcome type={env.type} players={welcome.players.Length} snapshot={welcome.snapshot.Length}";
```
Expected: `type=Welcome players>=5 snapshot>=1` (fake-3의 확정 스트로크 포함)

- [ ] **Step 4: N-3 검증 (피어 이탈)**

```csharp
var s = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
var tf = s.GetType().GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
var lb = (CameraCoop.Netplay.LoopbackTransport)tf.GetValue(s);
int before = s.Players.Count;
lb.RemoveFakePeer("fake-3");
int after = s.Players.Count;
var lines = UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None);
int remote = 0; foreach (var l in lines) { if (l.gameObject.name.StartsWith("RemoteStroke_")) remote++; }
return $"players {before}->{after} remoteStrokesPreserved={remote}";
```
Expected: players 감소 1, `remoteStrokesPreserved>=1` (스트로크 보존)

- [ ] **Step 5: 스크린샷 + 정리 + 보고**

```bash
unity cmd capture_game_view --source screen --save_path Temp/netplay_loopback.png
unity cmd get_console_logs --severity error --limit 10
unity cmd editor_stop
rm -f Assets/Temp/netplay_loopback.png Assets/Temp/netplay_loopback.png.meta; rmdir Assets/Temp 2>/dev/null; rm -f Assets/Temp.meta
```
Expected: 신규 에러 0. 결과 수치 전부 보고. **커밋 없음.**

---

### Task 6: Facepunch.Steamworks 의존성 도입 + Steam Init smoke

**Files:**
- Create: `Assets/Plugins/Facepunch/` (Facepunch.Steamworks DLL + native 라이브러리)
- Create: `steam_appid.txt` (프로젝트 루트, 내용: `480`)
- Create: `Assets/_CameraCoop/Scripts/Netplay/SteamBootstrap.cs`

**Interfaces:**
- Consumes: 없음
- Produces: `static class SteamBootstrap` — `bool TryInit()` (Init 480, 이미 초기화면 true), `void Shutdown()`, `bool IsValid { get; }`, `string LocalSteamId { get; }`, `string LocalName { get; }`. Task 7의 `SteamTransport`가 사용.

이 Task는 외부 의존성 도입이라 불확실성이 있다 — **정확한 다운로드 절차가 문서와 다르면 적응하고, 적응 내역을 보고서에 기록하라.** Steam 클라이언트가 이 Mac에 설치·로그인돼 있지 않으면 smoke 단계에서 BLOCKED로 보고 (설치는 사용자 결정).

- [ ] **Step 1: Facepunch.Steamworks 획득**

GitHub `Facepunch/Facepunch.Steamworks` releases에서 최신 2.3.x 릴리스 zip을 받아 (`curl -L -o` 사용):
- `Facepunch.Steamworks.Win64.dll` + `Facepunch.Steamworks.Posix.dll` (netstandard2.1 빌드)
- 릴리스에 포함된 native 재배포 바이너리: `steam_api64.dll`(Win), `libsteam_api.dylib`(macOS), `libsteam_api.so`(Linux)

배치:
```
Assets/Plugins/Facepunch/Facepunch.Steamworks.Win64.dll   (Import: Windows x86_64 전용)
Assets/Plugins/Facepunch/Facepunch.Steamworks.Posix.dll   (Import: macOS/Linux + Editor 전용)
Assets/Plugins/Facepunch/steam_api64.dll                  (Windows)
Assets/Plugins/Facepunch/libsteam_api.dylib               (macOS + Editor)
```
플랫폼 제한은 각 DLL의 `.meta` PluginImporter 설정으로 — `unity cmd`에 없으면 eval로 `PluginImporter.GetAtPath(...).SetCompatibleWithPlatform/SetCompatibleWithEditor` 호출 후 `AssetDatabase.ImportAsset`. **Win64와 Posix DLL이 같은 어셈블리명이므로 둘 다 Editor 호환이면 충돌한다 — Posix만 Editor 호환으로 설정 (이 개발 머신은 macOS).**

`steam_appid.txt` 생성 (프로젝트 루트, 내용 `480` 한 줄).

- [ ] **Step 2: SteamBootstrap.cs 작성**

```csharp
using System;
using UnityEngine;

namespace CameraCoop.Netplay
{
    // SteamClient 수명 관리 (docs/08 §7). 개발 AppID 480. 출시 시 AppID만 교체.
    public static class SteamBootstrap
    {
        public const uint DevAppId = 480;

        public static bool IsValid { get { return Steamworks.SteamClient.IsValid; } }
        public static string LocalSteamId { get { return Steamworks.SteamClient.SteamId.ToString(); } }
        public static string LocalName { get { return Steamworks.SteamClient.Name; } }

        public static bool TryInit()
        {
            if (Steamworks.SteamClient.IsValid)
            {
                return true;
            }
            try
            {
                Steamworks.SteamClient.Init(DevAppId, asyncCallbacks: true);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SteamBootstrap] Steam init 실패 (클라이언트 미실행/미로그인?): " + e.Message);
                return false;
            }
        }

        public static void Shutdown()
        {
            if (Steamworks.SteamClient.IsValid)
            {
                Steamworks.SteamClient.Shutdown();
            }
        }
    }
}
```

- [ ] **Step 3: 컴파일 + 기존 테스트 유지**

Run: recompile → `unity cmd run_tests --mode EditMode --timeout 120`
Expected: 67 pass, 컴파일 에러 0 (DLL 참조 성공 확인)

- [ ] **Step 4: Steam Init smoke (Steam 클라이언트 필요)**

```bash
unity cmd eval --code "
bool ok = CameraCoop.Netplay.SteamBootstrap.TryInit();
string result = ok ? $\"OK id={CameraCoop.Netplay.SteamBootstrap.LocalSteamId} name={CameraCoop.Netplay.SteamBootstrap.LocalName}\" : \"INIT FAILED\";
CameraCoop.Netplay.SteamBootstrap.Shutdown();
return result;
"
```
Expected: `OK id=<SteamID64> name=<계정명>`. `INIT FAILED`면 Steam 실행 여부 확인 후, 그래도 실패면 BLOCKED 보고 (Task 7 진행 불가 — 단 커밋은 하고 멈춘다).

- [ ] **Step 5: Commit**

```bash
git add Assets/Plugins steam_appid.txt Assets/_CameraCoop/Scripts/Netplay/SteamBootstrap.cs*
git commit -m "feat: Facepunch.Steamworks 도입 + Steam init smoke (docs/08 §7, AppID 480)"
```

---

### Task 7: SteamTransport + 단일 기기 host 경로 확인

**Files:**
- Create: `Assets/_CameraCoop/Scripts/Netplay/SteamTransport.cs`
- Modify: `Assets/_CameraCoop/Scripts/Netplay/NetplayUI.cs` (Steam Host 버튼 핸들러 추가)
- Modify: `Assets/_CameraCoop/Scenes/NetplayTest.unity` (Steam Host 버튼 추가 — Task 4와 같은 eval 방식)

**Interfaces:**
- Consumes: Task 2 `INetTransport`, Task 6 `SteamBootstrap`, Facepunch API (`SteamMatchmaking`, `SteamNetworkingSockets`, `SocketManager`/`ConnectionManager`)
- Produces: `SteamTransport : INetTransport` — `static async Task<SteamTransport> HostAsync(int maxPlayers)` (로비 생성 + relay socket), `static SteamTransport ConnectTo(SteamId hostId)` (초대 수락 경로), lobby join 콜백에서 자동 접속. `NetplayUI.OnClickHostSteam()`.

아래 코드는 Facepunch 2.3.x 기준 reference 구현이다. **실제 DLL의 API 시그니처와 다르면 적응하고 (IntelliSense 대신 `unity cmd console` 컴파일 에러로 확인), 적응 내역을 보고서에 기록하라.** 구조(INetTransport 계약·이벤트 발화 시점)는 유지해야 한다.

- [ ] **Step 1: SteamTransport.cs 작성 (reference 구현)**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace CameraCoop.Netplay
{
    // Steam Sockets(SDR relay) 기반 transport (docs/08 §2). host = relay listen socket, 클라 = ConnectRelay.
    public class SteamTransport : INetTransport
    {
        public bool IsHost { get; private set; }
        public string LocalPlayerId { get { return SteamClient.SteamId.ToString(); } }

        public event Action<string> OnPeerConnected;
        public event Action<string> OnPeerDisconnected;
        public event Action<string, byte[]> OnMessage;

        private Lobby? lobby;
        private HostSocketManager hostSocket;      // host 전용
        private ClientConnectionManager clientConn; // 클라 전용
        private readonly Dictionary<string, Connection> peerConnections = new Dictionary<string, Connection>();

        // ---- host ----
        public static async Task<SteamTransport> HostAsync(int maxPlayers)
        {
            var transport = new SteamTransport { IsHost = true };
            transport.hostSocket = SteamNetworkingSockets.CreateRelaySocket<HostSocketManager>();
            transport.hostSocket.owner = transport;
            Lobby? created = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
            if (!created.HasValue)
            {
                throw new Exception("Steam lobby 생성 실패");
            }
            var lobby = created.Value;
            lobby.SetFriendsOnly();
            lobby.SetJoinable(true);
            lobby.SetData("hostId", SteamClient.SteamId.ToString());
            transport.lobby = lobby;
            return transport;
        }

        // ---- 클라 (로비 참가 완료 후 호출) ----
        public static SteamTransport ConnectTo(SteamId hostId)
        {
            var transport = new SteamTransport { IsHost = false };
            transport.clientConn = SteamNetworkingSockets.ConnectRelay<ClientConnectionManager>(hostId);
            transport.clientConn.owner = transport;
            return transport;
        }

        public void SendToHost(byte[] data, bool reliable)
        {
            if (clientConn != null)
            {
                clientConn.Connection.SendMessage(data, reliable ? SendType.Reliable : SendType.Unreliable);
            }
        }

        public void SendTo(string playerId, byte[] data, bool reliable)
        {
            Connection conn;
            if (peerConnections.TryGetValue(playerId, out conn))
            {
                conn.SendMessage(data, reliable ? SendType.Reliable : SendType.Unreliable);
            }
        }

        public void Tick()
        {
            SteamClient.RunCallbacks();
            if (hostSocket != null)
            {
                hostSocket.Receive();
            }
            if (clientConn != null)
            {
                clientConn.Receive();
            }
        }

        public void Shutdown()
        {
            if (lobby.HasValue)
            {
                lobby.Value.Leave();
            }
            hostSocket?.Close();
            clientConn?.Close();
            peerConnections.Clear();
        }

        // ---- 내부: host socket 콜백 ----
        private class HostSocketManager : SocketManager
        {
            public SteamTransport owner;

            public override void OnConnecting(Connection connection, ConnectionInfo info)
            {
                base.OnConnecting(connection, info);
                connection.Accept();
            }

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                base.OnConnected(connection, info);
                string id = info.Identity.SteamId.ToString();
                owner.peerConnections[id] = connection;
                owner.OnPeerConnected?.Invoke(id);
            }

            public override void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                base.OnDisconnected(connection, info);
                string id = info.Identity.SteamId.ToString();
                owner.peerConnections.Remove(id);
                owner.OnPeerDisconnected?.Invoke(id);
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                var bytes = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(data, bytes, 0, size);
                owner.OnMessage?.Invoke(identity.SteamId.ToString(), bytes);
            }
        }

        // ---- 내부: 클라 connection 콜백 ----
        private class ClientConnectionManager : ConnectionManager
        {
            public SteamTransport owner;

            public override void OnConnected(ConnectionInfo info)
            {
                base.OnConnected(info);
                owner.OnPeerConnected?.Invoke(info.Identity.SteamId.ToString());
            }

            public override void OnDisconnected(ConnectionInfo info)
            {
                base.OnDisconnected(info);
                owner.OnPeerDisconnected?.Invoke(info.Identity.SteamId.ToString());
            }

            public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                var bytes = new byte[size];
                System.Runtime.InteropServices.Marshal.Copy(data, bytes, 0, size);
                owner.OnMessage?.Invoke("host", bytes); // 직결 상대는 항상 host — envelope.sender가 원 발신자
            }
        }
    }
}
```

- [ ] **Step 2: NetplayUI에 Steam Host 핸들러 추가**

`NetplayUI.cs`에 메서드 추가 (기존 코드 무변경, 추가만):

```csharp
        // Steam 세션 시작 (버튼 배선). 친구는 Steam overlay 초대로 참가 (docs/08 §5).
        public async void OnClickHostSteam()
        {
            if (session.IsRunning)
            {
                return;
            }
            if (!SteamBootstrap.TryInit())
            {
                if (statusText != null) { statusText.text = "Steam 미실행 — Steam 로그인 후 재시도"; }
                return;
            }
            try
            {
                SteamTransport transport = await SteamTransport.HostAsync(4);
                session.StartSession(transport, SteamBootstrap.LocalName);
                Steamworks.SteamFriends.OpenGameInviteOverlay(0); // 로비 초대 overlay (lobby id 인자 시그니처는 DLL에 맞춰 적응)
            }
            catch (System.Exception e)
            {
                if (statusText != null) { statusText.text = "Steam host 실패: " + e.Message; }
            }
        }
```
(파일 상단 `using CameraCoop.Netplay;`는 동일 namespace라 불필요. `OpenGameInviteOverlay` 인자는 lobby Id — SteamTransport가 lobby를 노출하도록 `public ulong LobbyId` 프로퍼티를 추가해 전달하는 식으로 적응 가능.)

씬에 "Host Steam" 버튼 추가 — Task 4의 eval MakeButton 패턴으로 `HostSteamButton` 생성 + `AddPersistentListener(btn.onClick, ui.OnClickHostSteam)` + SaveScene.

- [ ] **Step 3: 컴파일 + 전체 테스트**

Run: recompile → `unity cmd run_tests --mode EditMode --timeout 120`
Expected: 67 pass, 컴파일 에러 0

- [ ] **Step 4: 단일 기기 host 경로 확인 (Steam 필요)**

Play 진입 → eval로 `OnClickHostSteam()` 호출 → 3초 후:

```bash
unity cmd eval --code "
var s = UnityEngine.Object.FindFirstObjectByType<CameraCoop.Netplay.NetSession>();
return $\"running={s.IsRunning} host={s.IsHost} players={s.Players.Count} localId={s.LocalPlayerId}\";
"
```
Expected: `running=True host=True players=1 localId=<SteamID64>` (로비 생성 + relay socket까지 확인 — 2인 상호 검증(N-5)은 두 번째 기기 확보 시 수동). editor_stop으로 종료.

- [ ] **Step 5: Commit**

```bash
git add Assets/_CameraCoop/Scripts/Netplay Assets/_CameraCoop/Scenes/NetplayTest.unity
git commit -m "feat: SteamTransport (Facepunch relay socket + lobby) + Steam Host UI (docs/08 §2)"
```

---

## 계획 외 잔여 작업 (메인 세션 담당)

- N-5: 실 Steam 2인 상호 드로잉 — 두 번째 기기 + 별도 Steam 계정 확보 시 사용자와 수동 검증
- N-7: 웹캠 입력 포함 10분 Loopback 세션 (사용자 협조)
- N-6: QUALITY_CHECKLIST 채점 + docs/08 §8 DoD 결과 기록
