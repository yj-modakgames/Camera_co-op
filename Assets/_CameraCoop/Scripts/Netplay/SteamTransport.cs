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

        // 초대 overlay에 넘길 로비 Id. 로비가 없으면 0.
        public ulong LobbyId { get { return lobby.HasValue ? lobby.Value.Id.Value : 0UL; } }

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
                transport.hostSocket.Close(); // 로비 실패 시 socket 누수 방지
                throw new Exception("Steam lobby 생성 실패");
            }
            var newLobby = created.Value;
            newLobby.SetFriendsOnly();
            newLobby.SetJoinable(true);
            newLobby.SetData("hostId", SteamClient.SteamId.ToString());
            transport.lobby = newLobby;
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
            // 콜백은 SteamClient.Init(asyncCallbacks:true)가 자체 펌프 — 여기서 RunCallbacks 중복 호출 금지
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
                lobby = null;
            }
            if (hostSocket != null)
            {
                hostSocket.Close();
                hostSocket = null;
            }
            if (clientConn != null)
            {
                clientConn.Close();
                clientConn = null;
            }
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
