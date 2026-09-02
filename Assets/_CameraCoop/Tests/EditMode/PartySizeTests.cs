using System.Collections.Generic;
using CameraCoop.Netplay;
using CameraCoop.Party;
using NUnit.Framework;

namespace CameraCoop.Tests
{
    public class PartySizeTests
    {
        private const string HostId = "p1";

        private readonly List<OnlineRelayQuizSession> clients = new List<OnlineRelayQuizSession>();
        private readonly List<LoopbackTransport> clientTransports = new List<LoopbackTransport>();
        private readonly List<LoopbackTransport.FakePeer> hostWires = new List<LoopbackTransport.FakePeer>();
        private readonly List<LoopbackTransport.FakePeer> clientWires = new List<LoopbackTransport.FakePeer>();
        private readonly List<int> sentToHost = new List<int>();
        private readonly List<int> sentToClient = new List<int>();
        private LoopbackTransport hostTransport;
        private OnlineRelayQuizSession host;

        [TearDown]
        public void TearDown()
        {
            host?.Dispose();
            foreach (OnlineRelayQuizSession client in clients) client.Dispose();
            host = null;
            clients.Clear();
            clientTransports.Clear();
            hostWires.Clear();
            clientWires.Clear();
            sentToHost.Clear();
            sentToClient.Clear();
        }

        [TestCase(new[] { "game.exe", "-partysize", "2" }, 2)]
        [TestCase(new[] { "game.exe", "-PartySize", "3" }, 3)]
        [TestCase(new[] { "game.exe", "-partysize", "4" }, 4)]
        [TestCase(new[] { "game.exe" }, 4)]
        [TestCase(new[] { "game.exe", "-partysize" }, 4)]
        [TestCase(new[] { "game.exe", "-partysize", "1" }, 1)]
        [TestCase(new[] { "game.exe", "-partysize", "0" }, 4)]
        [TestCase(new[] { "game.exe", "-partysize", "5" }, 4)]
        [TestCase(new[] { "game.exe", "-partysize", "two" }, 4)]
        public void ParseClampsToSupportedPartySizes(string[] args, int expected)
        {
            Assert.That(PartySizeOption.Parse(args), Is.EqualTo(expected));
        }

        [Test]
        public void ParseWithoutArgumentsFallsBackToCapacity()
        {
            Assert.That(PartySizeOption.Parse(null), Is.EqualTo(PartyRoster.Capacity));
        }

        [Test]
        public void TwoPlayerPartyLocksRosterAndStartsSelectedMode()
        {
            Build(partySize: 2, peers: 1);
            Assert.That(host.View.rosterCount, Is.EqualTo(2));
            Assert.That(host.View.rosterLocked, Is.True);
            Assert.That(host.View.connected, Is.True);
            Assert.That(clients[0].View.connected, Is.True);

            SetReady(2);
            Assert.That(host.View.allReady, Is.True);
            Assert.That(host.OpenModeSelector(), Is.True);
            Pump();
            Assert.That(host.SelectModeAndBeginLoad(PartyMode.RelayCopy), Is.True);
            Pump();

            int transition = host.View.transitionGeneration;
            Assert.That(host.MarkLocalSceneReady(transition), Is.True);
            Assert.That(clients[0].MarkLocalSceneReady(transition), Is.True);
            Pump();

            Assert.That(host.View.modeStarted, Is.True);
            Assert.That(host.View.transitionPhase, Is.EqualTo(PartyTransitionPhase.InGame));
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
        }

        [Test]
        public void SoloPartyConnectsAloneSoLobbyPracticeDrawingIsWritable()
        {
            Build(partySize: PartySizeOption.SoloTestSize, peers: 0);
            Assert.That(host.View.rosterCount, Is.EqualTo(1));
            Assert.That(host.View.rosterLocked, Is.True);
            // connected는 로비 연습 그리기(PartyWorldController.UpdateCanvasMovement)의 최상위 게이트다.
            Assert.That(host.View.connected, Is.True);
            Assert.That(host.View.localSlot, Is.EqualTo(0));
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Setup));
            Assert.That(host.View.transitionPhase, Is.EqualTo(PartyTransitionPhase.Lobby));
        }

        [Test]
        public void SoloPartyRefusesModeStartBecauseRelayNeedsTwoPlayers()
        {
            Build(partySize: PartySizeOption.SoloTestSize, peers: 0);
            SetReady(1);
            Assert.That(host.OpenModeSelector(), Is.False);
            Assert.That(host.SelectModeAndBeginLoad(PartyMode.RelayCopy), Is.False);
            Assert.That(host.View.modeStarted, Is.False);
        }

        [Test]
        public void TwoPlayerPartyRefusesAThirdPeer()
        {
            Build(partySize: 2, peers: 2);
            Assert.That(host.View.rosterCount, Is.EqualTo(2));
            Assert.That(clients[1].View.connected, Is.False);
            Assert.That(clients[1].View.localSlot, Is.EqualTo(-1));
        }

        [Test]
        public void DefaultPartySizeStillWaitsForFourPlayers()
        {
            Build(partySize: PartyRoster.Capacity, peers: 1);
            Assert.That(host.View.rosterCount, Is.EqualTo(2));
            Assert.That(host.View.rosterLocked, Is.False);
            Assert.That(host.View.connected, Is.False);

            SetReady(2);
            Assert.That(host.View.allReady, Is.False);
            Assert.That(host.OpenModeSelector(), Is.False);
        }

        private void Build(int partySize, int peers)
        {
            hostTransport = new LoopbackTransport(true, HostId);
            host = NewSession(hostTransport, partySize);
            sentToHost.Add(0);
            sentToClient.Add(0);
            for (int i = 1; i <= peers; i++)
            {
                string id = "p" + (i + 1);
                var transport = new LoopbackTransport(false, id);
                clientTransports.Add(transport);
                clients.Add(NewSession(transport, partySize));
                hostWires.Add(hostTransport.AddFakePeer(id, "P" + (i + 1)));
                clientWires.Add(transport.AddFakePeer(HostId, "Host"));
                sentToHost.Add(0);
                sentToClient.Add(0);
            }
            Pump();
        }

        private OnlineRelayQuizSession NewSession(LoopbackTransport transport, int partySize)
        {
            return new OnlineRelayQuizSession(transport, HostId, () => "camera",
                () => new CanvasDrawingData(), () => "answer", 3, partySize);
        }

        private void SetReady(int count)
        {
            host.SetReady(true);
            for (int slot = 1; slot < count; slot++) clients[slot - 1].SetReady(true);
            Pump();
        }

        private void Pump(int cycles = 24)
        {
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                for (int slot = 1; slot <= clients.Count; slot++) PumpClientToHost(slot);
                for (int slot = 1; slot <= clients.Count; slot++) PumpHostToClient(slot);
                host.Tick(0f);
                foreach (OnlineRelayQuizSession client in clients) client.Tick(0f);
            }
        }

        private void PumpClientToHost(int slot)
        {
            LoopbackTransport transport = clientTransports[slot - 1];
            while (sentToHost[slot] < transport.SentToHost.Count)
                hostWires[slot - 1].Send(transport.SentToHost[sentToHost[slot]++]);
        }

        private void PumpHostToClient(int slot)
        {
            LoopbackTransport.FakePeer wire = hostWires[slot - 1];
            while (sentToClient[slot] < wire.Received.Count)
                clientWires[slot - 1].Send(wire.Received[sentToClient[slot]++]);
        }
    }
}
