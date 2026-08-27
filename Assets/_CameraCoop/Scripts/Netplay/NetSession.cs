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
            OnCanvasCleared?.Invoke(); // 이전 세션의 원격 표시·strokeId 재사용 충돌 정리 (최종 리뷰 I-2)

            if (transport.IsHost)
            {
                players[transport.LocalPlayerId] = new PlayerInfo { playerId = transport.LocalPlayerId, name = localName, colorIndex = 0 };
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
                    localActiveStroke.Clear();
                    pendingPoints.Clear();
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

        // 화면 픽셀 -> 정규화 (송신은 항상 정규화 좌표, docs/08 §3). y는 화면 좌하단 원점 -> 좌상단 원점으로 반전.
        private Vector2 ToNormalized(Vector2 screenPos)
        {
            return new Vector2(screenPos.x / Screen.width, 1f - screenPos.y / Screen.height);
        }
    }
}
