using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CameraCoop
{
    // UDP 수신 + 파싱 + lost 판정 담당. 표현/게임 판단 없음 (docs/04_unity_client.md §3).
    public class UdpHandReceiver : MonoBehaviour
    {
        [SerializeField] private int port = 5052;
        [SerializeField] private float lostTimeout = 0.5f;
        [SerializeField] private bool logStats = false; // 수신 간격 통계 로그 토글 (docs/05 §3)

        private const int StatsWindowSize = 100;

        // 수신 스레드 ↔ 메인 스레드 공유 상태. lock으로만 접근.
        private readonly object bufferLock = new object();
        private string pendingJson;

        private UdpClient client;
        private Thread receiveThread;
        private volatile bool running;

        // seq 필터링 상태 (메인 스레드 전용, lock 불필요)
        private uint? lastSeq;
        private bool versionWarningLogged;
        private bool jsonErrorWarningLogged;

        // 수신 간격 통계용 순환 버퍼
        private readonly float[] intervalBuffer = new float[StatsWindowSize];
        private int intervalIndex;
        private int intervalCount;

        private float lastPacketReceivedAt;

        public HandPacket LatestPacket { get; private set; }
        public float TimeSinceLastPacket => lastSeq.HasValue ? Time.realtimeSinceStartup - lastPacketReceivedAt : 0f;
        public bool IsServerLost => lastSeq.HasValue && TimeSinceLastPacket >= lostTimeout;
        public double LastLatencyMs { get; private set; }

        private void Awake()
        {
            client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            running = true;
            receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            receiveThread.Start();
            Debug.Log($"[UdpHandReceiver] listening on 127.0.0.1:{port}");
        }

        private void Update()
        {
            ProcessPendingPacket();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        // 수신 스레드: UdpClient.Receive 블로킹 → UTF-8 디코드 → 최신 문자열 슬롯 덮어쓰기까지만 수행.
        private void ReceiveLoop()
        {
            var remoteEP = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref remoteEP);
                    string json = Encoding.UTF8.GetString(data);
                    lock (bufferLock)
                    {
                        pendingJson = json;
                    }
                }
                catch (SocketException)
                {
                    // 종료 경로(Close 호출)로 인한 예외만 조용히 삼킨다. 그 외는 경고 후 루프 계속.
                    if (!running)
                    {
                        return;
                    }
                    Debug.LogWarning("[UdpHandReceiver] 소켓 오류로 패킷 수신 실패");
                }
                catch (ObjectDisposedException)
                {
                    // 종료 경로(Close 호출)로 소켓이 이미 해제된 경우만 조용히 종료.
                    if (!running)
                    {
                        return;
                    }
                    Debug.LogWarning("[UdpHandReceiver] 소켓이 이미 해제됨");
                    return;
                }
            }
        }

        // 메인 스레드: 슬롯 소비(꺼낸 뒤 null) → 파싱 → 필터 → LatestPacket 갱신.
        private void ProcessPendingPacket()
        {
            string json;
            lock (bufferLock)
            {
                json = pendingJson;
                pendingJson = null;
            }

            if (json == null)
            {
                return;
            }

            HandPacket packet;
            try
            {
                packet = JsonUtility.FromJson<HandPacket>(json);
            }
            catch (Exception e)
            {
                if (!jsonErrorWarningLogged)
                {
                    Debug.LogWarning($"[UdpHandReceiver] JSON 파싱 실패: {e.Message}");
                    jsonErrorWarningLogged = true;
                }
                return;
            }

            if (packet == null)
            {
                return;
            }

            if (packet.v != PacketFilter.SupportedVersion)
            {
                if (!versionWarningLogged)
                {
                    Debug.LogWarning($"[UdpHandReceiver] 지원하지 않는 프로토콜 버전: {packet.v} (지원 버전: {PacketFilter.SupportedVersion})");
                    versionWarningLogged = true;
                }
                return;
            }

            bool isFirstPacket = !lastSeq.HasValue;
            if (!isFirstPacket && !PacketFilter.ShouldAccept(packet, lastSeq.Value))
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (logStats && !isFirstPacket)
            {
                RecordInterval((now - lastPacketReceivedAt) * 1000f);
            }

            lastSeq = packet.seq;
            lastPacketReceivedAt = now;
            LatestPacket = packet;

            // 동일 머신 기준 epoch 직접 비교로 end-to-end 레이턴시 측정 (docs/05 §3)
            double nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            LastLatencyMs = (nowEpoch - packet.timestamp) * 1000.0;
        }

        // 최근 StatsWindowSize개 수신 간격의 평균/최대를 버퍼 한 바퀴마다 로그.
        private void RecordInterval(float intervalMs)
        {
            intervalBuffer[intervalIndex] = intervalMs;
            intervalIndex = (intervalIndex + 1) % StatsWindowSize;
            if (intervalCount < StatsWindowSize)
            {
                intervalCount++;
            }

            if (intervalCount == StatsWindowSize && intervalIndex == 0)
            {
                float sum = 0f;
                float max = 0f;
                for (int i = 0; i < StatsWindowSize; i++)
                {
                    float v = intervalBuffer[i];
                    sum += v;
                    if (v > max)
                    {
                        max = v;
                    }
                }
                Debug.Log($"[UdpHandReceiver] 수신 간격 최근 {StatsWindowSize}개 평균 {sum / StatsWindowSize:F1}ms, 최대 {max:F1}ms");
            }
        }

        private void Shutdown()
        {
            if (!running)
            {
                return; // 이미 종료됨 (OnDestroy/OnApplicationQuit 중복 호출 방지)
            }

            running = false;
            client?.Close(); // Receive 블로킹 해제
            receiveThread?.Join(500);
        }
    }
}
