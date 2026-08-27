using System;
using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop.Netplay
{
    // 세션 중심: 로컬 입력 -> 네트워크 송신, 수신 -> 이벤트 발행, host면 중계 + 스냅샷 (docs/08 §2~§4)
    public class NetSession : MonoBehaviour
    {
        [SerializeField] private HandPointer handPointer;          // 스트로크 입력원 (norm을 직접 준다, docs/11 §2)
        [SerializeField] private ToolState toolState;               // 송신 스타일 원본
        [SerializeField] private DrawingController drawingController; // localId 발급자 — 지우개 매핑용
        [SerializeField] private UdpHandReceiver receiver;          // 커서 송신은 여전히 손 패킷을 직접 읽는다
        [SerializeField, Min(1f)] private float cursorSendHz = 15f;
        [SerializeField, Min(0.01f)] private float pointsFlushInterval = 0.1f; // StrokePoints 100ms 배치 (docs/08 §3)
        [SerializeField] private float pinchThreshold = 0.40f;         // Phase 1 실측값과 동일 유지
        [SerializeField] private float pinchReleaseThreshold = 0.60f;

        public bool IsRunning { get { return transport != null; } }
        public bool IsHost { get { return transport != null && transport.IsHost; } }
        public string LocalPlayerId { get { return transport != null ? transport.LocalPlayerId : null; } }
        public IReadOnlyDictionary<string, PlayerInfo> Players { get { return players; } }

        // host: 자기 id / 클라: Welcome sender(star에서 Welcome을 보내는 건 host뿐, docs/08 §1) / 세션 없음: null.
        // 게임 계층이 "host가 보낸 게임 메시지만 적용"을 판정하는 기준 (docs/12 §2 표 #6)
        public string HostPlayerId { get; private set; }

        // playerId -> 스트로크 허용. null이면 전원 허용. 스트로크 4종에만 적용된다 (docs/12 §2 표 #3)
        public Func<string, bool> StrokeGate { get; set; }

        public event Action OnPlayersChanged;
        public event Action<string, string, string> OnGameMessage;  // (type, senderPlayerId, payloadJson) — 코어 타입 밖 전부 (docs/12 §2 표 #1)
        public event Action<string> OnPeerJoinedSession;            // host: Hello 처리 직후 / 클라: PeerJoined 적용 직후 (docs/12 §2 표 #4)
        public event Action<string, string, Vector2, bool> OnRemoteCursor;
        public event Action<string, string, Vector2, StrokeStyle> OnRemoteStrokeStart;
        public event Action<string> OnRemoteStrokeErased;
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
        private readonly Dictionary<string, int> pendingLocalId = new Dictionary<string, int>(2);  // hand -> DrawingController가 발급한 localId
        private readonly Dictionary<int, string> localToStroke = new Dictionary<int, string>();    // localId -> 전역 strokeId (지우개)
        private bool warnedMissingMapping;
        private bool warnedGameRole;

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
            pendingLocalId.Clear();
            localToStroke.Clear();
            localStrokeCounter = 0;
            localCursorSeq = 0;
            OnCanvasCleared?.Invoke(); // 이전 세션의 원격 표시·strokeId 재사용 충돌 정리 (최종 리뷰 I-2)

            HostPlayerId = transport.IsHost ? transport.LocalPlayerId : null; // 클라는 Welcome을 받아야 안다
            if (transport.IsHost)
            {
                players[transport.LocalPlayerId] = new PlayerInfo { playerId = transport.LocalPlayerId, name = localName, colorIndex = 0 };
            }

            if (handPointer != null)
            {
                handPointer.OnCanvasStrokeStart += HandleLocalStrokeStart;
                handPointer.OnCanvasStrokeMove += HandleLocalStrokeMove;
                handPointer.OnCanvasStrokeEnd += HandleLocalStrokeEnd;
            }
            else
            {
                Debug.LogWarning("[NetSession] handPointer 미할당 — 로컬 드로잉이 송신되지 않습니다.");
            }
            if (drawingController != null)
            {
                // strokeId 소유권 (docs/11 §2): localId는 DrawingController가 발급하고, 전역 strokeId는 여기서 만든다
                drawingController.OnLocalStrokeStarted += HandleLocalStrokeIdAssigned;
                drawingController.OnLocalStrokeErased += HandleLocalStrokeErased;
            }

            OnPlayersChanged?.Invoke();
        }

        public void StopSession()
        {
            if (transport == null)
            {
                return;
            }
            if (handPointer != null)
            {
                handPointer.OnCanvasStrokeStart -= HandleLocalStrokeStart;
                handPointer.OnCanvasStrokeMove -= HandleLocalStrokeMove;
                handPointer.OnCanvasStrokeEnd -= HandleLocalStrokeEnd;
            }
            if (drawingController != null)
            {
                drawingController.OnLocalStrokeStarted -= HandleLocalStrokeIdAssigned;
                drawingController.OnLocalStrokeErased -= HandleLocalStrokeErased;
            }
            transport.OnPeerConnected -= HandlePeerConnected;
            transport.OnPeerDisconnected -= HandlePeerDisconnected;
            transport.OnMessage -= HandleMessage;
            transport.Shutdown();
            transport = null;
            HostPlayerId = null;
            players.Clear();
            OnCanvasCleared?.Invoke(); // 이전 세션의 원격 표시·strokeId 재사용 충돌 정리 (최종 리뷰 I-2)
            OnPlayersChanged?.Invoke();
        }

        public void SendClear()
        {
            if (!IsHost)
            {
                return; // 3a에서 Clear는 host 전용 (docs/08 §3)
            }
            strokes.Clear();
            localActiveStroke.Clear(); // Clear 중 진행 스트로크의 고아 참조 방지 (불변식: 이 둘은 strokes와 함께 리셋)
            pendingPoints.Clear();
            pendingLocalId.Clear();
            localToStroke.Clear();     // 로컬 스트로크가 전부 사라졌으므로 매핑도 함께 리셋
            Broadcast(NetProtocol.TypeClear, new EmptyPayload(), reliable: true, exceptId: null);
            OnCanvasCleared?.Invoke();
        }

        // ---- 게임 메시지 송신 (docs/12 §2 표 #2). 전부 reliable, 내용은 보지 않는다 ----

        public void BroadcastGameMsg<T>(string type, T payload, string exceptId = null)
        {
            if (!CanSendGame(needHost: true, api: "BroadcastGameMsg"))
            {
                return;
            }
            Broadcast(type, payload, reliable: true, exceptId: exceptId);
        }

        public void SendGameTo<T>(string playerId, string type, T payload)
        {
            if (!CanSendGame(needHost: true, api: "SendGameTo"))
            {
                return;
            }
            transport.SendTo(playerId, NetProtocol.Encode(type, transport.LocalPlayerId, payload), true);
        }

        public void SendGameToHost<T>(string type, T payload)
        {
            if (!CanSendGame(needHost: false, api: "SendGameToHost"))
            {
                return;
            }
            SendToHostMsg(type, payload, reliable: true);
        }

        // 역할이 안 맞는 송신을 조용히 삼키지 않는다. 다만 게임 루프에서 반복 호출될 수 있어 경고는 1회만 남긴다.
        private bool CanSendGame(bool needHost, string api)
        {
            if (transport != null && IsHost == needHost)
            {
                return true;
            }
            if (!warnedGameRole)
            {
                warnedGameRole = true;
                Debug.LogWarning("[NetSession] " + api + "는 " + (needHost ? "host" : "클라") + " 전용입니다 — 호출을 무시합니다 (docs/12 §2)");
            }
            return false;
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
                Vector3 palm = hand.GetPalmCenter();
                bool was;
                localPinch.TryGetValue(hand.handedness, out was);
                bool now = PinchStateMachine.Next(was, hand.pinch, pinchThreshold, pinchReleaseThreshold);
                localPinch[hand.handedness] = now;
                localCursorSeq++;
                var payload = new CursorPayload { hand = hand.handedness, x = palm.x, y = palm.y, pinched = now, seq = localCursorSeq };
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

        // HandPointer가 norm을 직접 준다 — 내부 screen->norm 변환은 삭제됐다 (docs/11 §2)
        private void HandleLocalStrokeStart(string hand, Vector2 norm, Vector3 world)
        {
            if (StrokeGate != null && !StrokeGate(transport.LocalPlayerId))
            {
                return; // host 자신도 게이트 대상 (docs/12 §2). 1차 방어는 HandPointer라 정상 경로에선 도달하지 않는다
            }
            string strokeId = NetProtocol.MakeStrokeId(transport.LocalPlayerId, localStrokeCounter++);
            localActiveStroke[hand] = strokeId;
            StrokeStyle style = CurrentStyle();
            var stroke = new NetStroke { playerId = transport.LocalPlayerId, finished = false, style = style };
            stroke.points.Add(norm);
            strokes[strokeId] = stroke;
            pendingPoints[strokeId] = new List<Vector2>();

            // DrawingController가 같은 이벤트를 먼저 구독한다(씬 로드 시 OnEnable, 세션 시작은 그 뒤) →
            // 여기 도달했을 때 이번 스트로크의 localId가 이미 pendingLocalId에 있다.
            int localId;
            if (pendingLocalId.TryGetValue(hand, out localId))
            {
                localToStroke[localId] = strokeId;
                pendingLocalId.Remove(hand);
            }

            SendStrokeMsg(NetProtocol.TypeStrokeStart, new StrokeStartPayload
            {
                strokeId = strokeId, hand = hand, x = norm.x, y = norm.y,
                color = style.color, width = style.width, brush = style.brush
            });
        }

        private void HandleLocalStrokeMove(string hand, Vector2 norm, Vector3 world)
        {
            string strokeId;
            if (!localActiveStroke.TryGetValue(hand, out strokeId))
            {
                return;
            }
            strokes[strokeId].points.Add(norm);
            pendingPoints[strokeId].Add(norm);
        }

        // 송신 스타일은 ToolState가 단일 출처. 미할당이면 width=0으로 보내 수신 측 폴백을 태운다.
        private StrokeStyle CurrentStyle()
        {
            if (toolState == null)
            {
                return default(StrokeStyle);
            }
            return new StrokeStyle
            {
                color = ColorPack.ToInt(toolState.CurrentColor),
                width = toolState.CurrentWidth,
                brush = toolState.CurrentBrushIndex
            };
        }

        // DrawingController가 발급한 localId 수신 (같은 핀치 이벤트에서 먼저 발행된다)
        private void HandleLocalStrokeIdAssigned(int localId, string hand)
        {
            pendingLocalId[hand] = localId;
        }

        // 로컬에서 지운 스트로크를 전역 strokeId로 변환해 송신 (docs/11 §2 — 판정을 원격에서 재실행하지 않는다)
        private void HandleLocalStrokeErased(int localId)
        {
            if (transport == null)
            {
                return;
            }
            string strokeId;
            if (!localToStroke.TryGetValue(localId, out strokeId))
            {
                if (!warnedMissingMapping)
                {
                    warnedMissingMapping = true;
                    Debug.LogWarning("[NetSession] localId " + localId + "의 strokeId 매핑이 없어 StrokeErase를 보내지 못했습니다 (docs/11 §2)");
                }
                return;
            }
            localToStroke.Remove(localId);
            strokes.Remove(strokeId);
            SendStrokeMsg(NetProtocol.TypeStrokeErase, new StrokeErasePayload { strokeId = strokeId });
        }

        private void HandleLocalStrokeEnd(string hand)
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

            // 스트로크 4종은 중계·Apply 전에 권위 게이트 (docs/12 §2 표 #3).
            // 거부는 로그를 남기지 않는다 — 라운드 내내 대량으로 발생해 콘솔 스팸(원격 유발 로그 폭탄)이 된다.
            if (NetProtocol.IsStrokeType(env.type) && StrokeGate != null && !StrokeGate(env.sender))
            {
                return;
            }

            // host: 화이트리스트 타입만 발신자 제외 전원에게 중계 (정본 순서 = host의 중계 순서, docs/08 §1).
            // 게임 메시지는 중계 대상이 아니다 — 중계 여부는 host의 게임 계층이 결정한다 (docs/12 §2 표 #1)
            if (IsHost && NetProtocol.IsRelayType(env.type))
            {
                RelayRaw(data, env, exceptId: directSender);
            }

            // 코어 타입 밖 = 게임 메시지. Apply의 switch에 넣지 않고 통로 이벤트로만 내보낸다 (NetSession은 게임을 모른다)
            if (!NetProtocol.IsCoreType(env.type))
            {
                OnGameMessage?.Invoke(env.type, env.sender, env.payload);
                return;
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
            OnPeerJoinedSession?.Invoke(peerId); // 늦은 참가자에게 게임 상태를 보낼 트리거 (docs/12 §2 표 #4)
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
                    HostPlayerId = env.sender; // star 토폴로지에서 Welcome을 보내는 건 host뿐 (docs/08 §1)
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
                    OnPeerJoinedSession?.Invoke(joined.playerId);
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
                    var startStyle = new StrokeStyle { color = start.color, width = start.width, brush = start.brush };
                    var stroke = new NetStroke { playerId = env.sender, finished = false, style = startStyle };
                    stroke.points.Add(new Vector2(start.x, start.y));
                    strokes[start.strokeId] = stroke;
                    OnRemoteStrokeStart?.Invoke(start.strokeId, env.sender, new Vector2(start.x, start.y), startStyle);
                    break;
                case NetProtocol.TypeStrokeErase:
                    var erase = NetProtocol.DecodePayload<StrokeErasePayload>(env);
                    strokes.Remove(erase.strokeId); // 없으면 무시 — 멱등 (docs/11 §5)
                    OnRemoteStrokeErased?.Invoke(erase.strokeId);
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
                    localActiveStroke.Clear();
                    pendingPoints.Clear();
                    pendingLocalId.Clear();
                    localToStroke.Clear();
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
            var style = new StrokeStyle { color = snap.color, width = snap.width, brush = snap.brush };
            var stroke = new NetStroke { playerId = snap.playerId, finished = true, style = style };
            Vector2[] points = NetProtocol.UnflattenPoints(snap.xy);
            for (int i = 0; i < points.Length; i++)
            {
                stroke.points.Add(points[i]);
            }
            strokes[snap.strokeId] = stroke;
            OnRemoteStrokeStart?.Invoke(snap.strokeId, snap.playerId, points.Length > 0 ? points[0] : Vector2.zero, style);
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
            if (!IsHost)
            {
                // 연결 수립 후에만 Hello — connecting 상태 송신 유실 방지 (docs/08 §3)
                SendToHostMsg(NetProtocol.TypeHello, new HelloPayload { name = localName }, reliable: true);
            }
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
    }
}
