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
