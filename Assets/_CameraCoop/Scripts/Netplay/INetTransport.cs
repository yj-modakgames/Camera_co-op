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
