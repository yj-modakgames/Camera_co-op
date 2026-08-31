using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CameraCoop.Netplay;
using CameraCoop.Party;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    public class OnlineRelayQuizSessionTests
    {
        [Serializable]
        private sealed class AckFixture
        {
            public string drawingId;
            public int destinationSlot;
            public int ownerSlot;
            public int revision;
        }

        [Serializable]
        private sealed class ActionFixture
        {
            public RelayQuizAction action;
        }

        private const string HostId = "host-id";
        private const string Secret = "비밀단어";
        private readonly List<LoopbackTransport> clientTransports = new List<LoopbackTransport>();
        private readonly List<OnlineRelayQuizSession> clients = new List<OnlineRelayQuizSession>();
        private readonly List<LoopbackTransport.FakePeer> hostWires = new List<LoopbackTransport.FakePeer>();
        private readonly List<LoopbackTransport.FakePeer> clientWires = new List<LoopbackTransport.FakePeer>();
        private readonly int[] sentToHost = new int[4];
        private readonly int[] sentToClient = new int[4];
        private readonly CanvasDrawingData[] drawings = new CanvasDrawingData[4];
        private readonly string[] answers = new string[4];
        private readonly int[] captures = new int[4];
        private readonly int[] answerCaptures = new int[4];
        private LoopbackTransport hostTransport;
        private OnlineRelayQuizSession host;

        [SetUp]
        public void SetUp()
        {
            for (int i = 0; i < 4; i++)
            {
                int value = i;
                drawings[i] = MakeDrawing(0.1f + value * 0.2f);
                answers[i] = value == 3 ? Secret : "wrong-slot-" + value;
            }
            hostTransport = new LoopbackTransport(true, HostId);
            host = NewSession(hostTransport, 0);
            for (int i = 1; i < 4; i++)
            {
                string id = PlayerId(i);
                var transport = new LoopbackTransport(false, id);
                clientTransports.Add(transport);
                clients.Add(NewSession(transport, i));
                hostWires.Add(hostTransport.AddFakePeer(id, "P" + (i + 1)));
                clientWires.Add(transport.AddFakePeer(HostId, "Host"));
            }
            Pump();
        }

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
            Array.Clear(sentToHost, 0, sentToHost.Length);
            Array.Clear(sentToClient, 0, sentToClient.Length);
            Array.Clear(captures, 0, captures.Length);
            Array.Clear(answerCaptures, 0, answerCaptures.Length);
        }

        [Test]
        public void PacketContractCarriesSessionRosterRoundTurnOwnerAndRevision()
        {
            Type type = typeof(OnlineRelayQuizPacket);
            Assert.That(type.GetField("sessionId"), Is.Not.Null);
            Assert.That(type.GetField("rosterGeneration"), Is.Not.Null);
            Assert.That(type.GetField("roundId"), Is.Not.Null);
            Assert.That(type.GetField("turnId"), Is.Not.Null);
            Assert.That(type.GetField("ownerSlot"), Is.Not.Null);
            Assert.That(type.GetField("revision"), Is.Not.Null);
            Assert.That(type.GetField("selectedMode"), Is.Not.Null);
            Assert.That(type.GetField("modeGeneration"), Is.Not.Null);
            Assert.That(type.GetField("startSignal"), Is.Not.Null);
        }

        [Test]
        public void FourCameraReadyPlayersLockRosterAndStartTogether()
        {
            Assert.That(Field<int>(host.View, "rosterCount"), Is.EqualTo(4));
            Assert.That(Field<bool>(host.View, "rosterLocked"), Is.True);
            for (int i = 0; i < 3; i++) Session(i).SetReady(true);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Setup));
            Session(3).SetReady(true);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Setup));
            Assert.That(Field<bool>(host.View, "allReady"), Is.True);
            Assert.That(SelectMode(host, PartyMode.RelayCopy), Is.True);
            Pump();
            Assert.That(StartSelectedMode(host), Is.True);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
            Assert.That(Field<bool>(host.View, "rosterLocked"), Is.True);
            Assert.That(Field<int>(host.View, "localSlot"), Is.Zero);
            for (int i = 1; i < 4; i++)
            {
                Assert.That(Session(i).View.state, Is.EqualTo(RelayQuizState.Handover));
                Assert.That(Field<int>(Session(i).View, "localSlot"), Is.EqualTo(i));
            }
        }

        [Test]
        public void OnlyHostCanSelectModeAndSelectionBroadcasts()
        {
            Assert.That(SelectMode(clients[0], PartyMode.MemoryCopy), Is.False);
            Pump();
            Assert.That(Field<bool>(host.View, "hasSelectedMode"), Is.False);

            Assert.That(SelectMode(host, PartyMode.MemoryCopy), Is.True);
            Pump();

            int generation = Field<int>(host.View, "modeGeneration");
            for (int slot = 0; slot < 4; slot++)
            {
                Assert.That(Field<bool>(Session(slot).View, "hasSelectedMode"), Is.True);
                Assert.That(Field<PartyMode>(Session(slot).View, "selectedMode"), Is.EqualTo(PartyMode.MemoryCopy));
                Assert.That(Field<int>(Session(slot).View, "modeGeneration"), Is.EqualTo(generation));
            }
        }

        [Test]
        public void StartIsRejectedUntilAllReadyAndOnlyHostCanReleaseGate()
        {
            Assert.That(SelectMode(host, PartyMode.RelayCopy), Is.True);
            for (int slot = 0; slot < 3; slot++) Session(slot).SetReady(true);
            Pump();
            Assert.That(StartSelectedMode(host), Is.False);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Setup));

            Session(3).SetReady(true);
            Pump();
            Assert.That(Field<bool>(host.View, "allReady"), Is.True);
            Assert.That(StartSelectedMode(clients[0]), Is.False);
            Assert.That(StartSelectedMode(host), Is.True);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
        }

        [Test]
        public void UnreadyAndFreshHandLossImmediatelyClearAllReadyBeforeStart()
        {
            ReadyLobby();
            Assert.That(Field<bool>(host.View, "allReady"), Is.True);

            clients[0].SetReady(false);
            Pump();
            Assert.That(Field<bool>(host.View, "allReady"), Is.False);
            Assert.That(clients[0].View.localReady, Is.False);

            clients[0].SetReady(true);
            Pump();
            Assert.That(Field<bool>(host.View, "allReady"), Is.True);
            clients[1].UpdateLocalConditions(true, false);
            Pump();
            Assert.That(Field<bool>(host.View, "allReady"), Is.False);
            Assert.That(clients[1].View.localReady, Is.False);
        }

        [Test]
        public void ReadyAndModeChangesAreRejectedAfterStart()
        {
            StartFour();
            int modeGeneration = Field<int>(host.View, "modeGeneration");
            clients[0].SetReady(false);
            Assert.That(SelectMode(host, PartyMode.MemoryCopy), Is.False);
            Pump();
            Assert.That(clients[0].View.localReady, Is.True);
            Assert.That(Field<PartyMode>(host.View, "selectedMode"), Is.EqualTo(PartyMode.RelayCopy));
            Assert.That(Field<int>(host.View, "modeGeneration"), Is.EqualTo(modeGeneration));
        }

        [Test]
        public void FifthAndLateJoinAreRejectedWithoutSlotReassignment()
        {
            StartFour();
            int rosterGeneration = Field<int>(host.View, "rosterGeneration");
            var lateTransport = new LoopbackTransport(false, "p5");
            using (OnlineRelayQuizSession late = NewSession(lateTransport, 3))
            {
                LoopbackTransport.FakePeer lateHostWire = hostTransport.AddFakePeer("p5", "P5");
                LoopbackTransport.FakePeer lateClientWire = lateTransport.AddFakePeer(HostId, "Host");
                int toHost = 0, toClient = 0;
                for (int i = 0; i < 12; i++)
                {
                    while (toHost < lateTransport.SentToHost.Count) lateHostWire.Send(lateTransport.SentToHost[toHost++]);
                    while (toClient < lateHostWire.Received.Count) lateClientWire.Send(lateHostWire.Received[toClient++]);
                    host.Tick(0f);
                    late.Tick(0f);
                }
                Assert.That(late.View.aborted, Is.True);
            }
            Assert.That(Field<int>(host.View, "rosterCount"), Is.EqualTo(4));
            Assert.That(Field<int>(host.View, "rosterGeneration"), Is.EqualTo(rosterGeneration));
        }

        [Test]
        public void FifthJoinIsRejectedWhileFullRosterIsStillInSetup()
        {
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Setup));
            Assert.That(Field<int>(host.View, "rosterCount"), Is.EqualTo(4));
            int rosterGeneration = Field<int>(host.View, "rosterGeneration");
            var fifthTransport = new LoopbackTransport(false, "setup-p5");
            using (OnlineRelayQuizSession fifth = NewSession(fifthTransport, 3))
            {
                LoopbackTransport.FakePeer fifthHostWire = hostTransport.AddFakePeer("setup-p5", "P5");
                LoopbackTransport.FakePeer fifthClientWire = fifthTransport.AddFakePeer(HostId, "Host");
                int toHost = 0, toClient = 0;
                for (int i = 0; i < 12; i++)
                {
                    while (toHost < fifthTransport.SentToHost.Count) fifthHostWire.Send(fifthTransport.SentToHost[toHost++]);
                    while (toClient < fifthHostWire.Received.Count) fifthClientWire.Send(fifthHostWire.Received[toClient++]);
                    host.Tick(0f);
                    fifth.Tick(0f);
                }
                Assert.That(fifth.View.aborted, Is.True);
            }
            Assert.That(Field<int>(host.View, "rosterGeneration"), Is.EqualTo(rosterGeneration));
        }

        [Test]
        public void UnauthenticatedConnectionFloodCannotAbortLockedRoster()
        {
            Assert.That(Field<bool>(host.View, "rosterLocked"), Is.True);
            int rosterGeneration = Field<int>(host.View, "rosterGeneration");

            for (int i = 0; i < 256; i++)
                hostTransport.AddFakePeer("attacker-" + i, "Attacker");

            host.Tick(0f);

            Assert.That(host.View.aborted, Is.False);
            Assert.That(Field<int>(host.View, "rosterCount"), Is.EqualTo(4));
            Assert.That(Field<int>(host.View, "rosterGeneration"), Is.EqualTo(rosterGeneration));
        }

        [Test]
        public void SecretPayloadIsOnlyEverSentToSlotZero()
        {
            StartDrawing();
            Assert.That(host.View.word, Is.EqualTo(Secret));
            for (int client = 0; client < 3; client++)
            {
                Assert.That(clients[client].View.word, Is.Empty);
                foreach (byte[] bytes in hostWires[client].Received)
                    Assert.That(Encoding.UTF8.GetString(bytes), Does.Not.Contain(Secret));
            }
        }

        [Test]
        public void DeadlineAndStaleManualCompletionCaptureExactlyOnce()
        {
            StartDrawing();
            int staleGeneration = host.View.generation;
            host.Tick(60f);
            host.Execute(RelayQuizAction.CompleteDrawing, staleGeneration);
            Pump();
            Assert.That(captures[0], Is.EqualTo(1));
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
        }

        [Test]
        public void ValidEmptyDrawingIsPreparedAsFrozenReference()
        {
            drawings[0] = new CanvasDrawingData();
            StartDrawing();
            CompleteDrawing(0);
            ReadyAndEnterTurn(1);
            Assert.That(Session(1).View.referenceDrawing, Is.Not.Null);
            Assert.That(Session(1).View.referenceDrawing.strokes, Is.Empty);
        }

        [Test]
        public void PauseResumeKeepsStateSerialAndDoesNotCaptureDrawing()
        {
            StartDrawing();
            int serial = host.View.serial;
            host.UpdateLocalConditions(true, false);
            Assert.That(host.View.paused, Is.True);
            Assert.That(host.View.serial, Is.EqualTo(serial));
            host.UpdateLocalConditions(true, true);
            host.Execute(RelayQuizAction.Resume, host.View.generation);
            Pump();
            Assert.That(host.View.paused, Is.False);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Drawing));
            Assert.That(host.View.serial, Is.EqualTo(serial));
            Assert.That(captures[0], Is.Zero);
        }

        [Test]
        public void CompletedDrawingsRouteOnlyP1ToP2ThenP2ToP3ThenP3ToP4()
        {
            StartDrawing();
            int[] marks = TrafficMarks();
            CompleteDrawing(0);
            AssertPrivateDrawingRecipient(marks, 1);
            ReadyAndEnterTurn(1);
            marks = TrafficMarks();
            CompleteDrawing(1);
            AssertPrivateDrawingRecipient(marks, 2);
            ReadyAndEnterTurn(2);
            marks = TrafficMarks();
            CompleteDrawing(2);
            AssertPrivateDrawingRecipient(marks, 3);
        }

        [Test]
        public void WrongDuplicateAndLatePreparedAckCannotReleaseAnotherTransfer()
        {
            StartDrawing();
            host.Execute(RelayQuizAction.CompleteDrawing, host.View.generation);
            Assert.That(host.View.transferPending, Is.True);
            SendAck(1, "wrong-id", 0, 99);
            host.Tick(0f);
            Assert.That(host.View.transferPending, Is.True);
            Assert.That(host.View.hasTimer, Is.False);
            Pump();
            Assert.That(host.View.transferPending, Is.False);
            string firstId = Session(1).View.drawingId;
            int firstRevision = Field<int>(Session(1).View, "drawingRevision");
            SendAck(1, firstId, 0, firstRevision);
            host.Tick(0f);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
            ReadyAndEnterTurn(1);
            CompleteDrawingWithoutDestinationAck(1, 2);
            Assert.That(host.View.transferPending, Is.True);
            SendAck(1, firstId, 0, firstRevision);
            host.Tick(0f);
            Assert.That(host.View.transferPending, Is.True);
        }

        [Test]
        public void PreparedAckMustArriveBeforeDestinationCanStartTimer()
        {
            StartDrawing();
            host.Execute(RelayQuizAction.CompleteDrawing, host.View.generation);
            clients[0].Execute(RelayQuizAction.Ready, clients[0].View.generation);
            PumpClientToHost(1);
            host.Tick(0f);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
            Assert.That(host.View.hasTimer, Is.False);
            Pump();
            ReadyAndEnterTurn(1);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Drawing));
            Assert.That(host.View.hasTimer, Is.True);
        }

        [Test]
        public void SenderIdentityIsResolvedFromRosterInsteadOfClaimedOwnerSlot()
        {
            StartDrawing();
            CompleteDrawing(0);
            OnlineRelayQuizPacket forged = ClientPacket(2, "action",
                JsonUtility.ToJson(new ActionFixture { action = RelayQuizAction.Ready }), ownerSlot: 1);
            hostWires[1].Send(OnlineRelayQuizProtocol.Encode(forged));
            host.Tick(0f);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
        }

        [Test]
        public void OnlyP4CanSubmitAnswer()
        {
            ReachGuessing();
            clients[1].Execute(RelayQuizAction.Submit, clients[1].View.generation);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Guessing));
            clients[2].Execute(RelayQuizAction.Submit, clients[2].View.generation);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Reveal));
            Assert.That(host.View.answer, Is.EqualTo(Secret));
            Assert.That(answerCaptures[2], Is.Zero);
            Assert.That(answerCaptures[3], Is.EqualTo(1));
        }

        [Test]
        public void MissingP4AnswerTimesOutWithoutCommittingEmptyAnswer()
        {
            ReachGuessing();
            answers[3] = null;
            clients[2].Execute(RelayQuizAction.Submit, clients[2].View.generation);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Guessing));
            Assert.That(host.View.transferPending, Is.True);
            host.Tick(10f);
            Assert.That(host.View.aborted, Is.True);
        }

        [Test]
        public void RestartKeepsLockedSlotsAndRequiresFourFreshCameraReadySignals()
        {
            ReachGuessing();
            clients[2].Execute(RelayQuizAction.Submit, clients[2].View.generation);
            Pump();
            host.Execute(RelayQuizAction.OpenGallery, host.View.generation);
            Pump();
            host.Execute(RelayQuizAction.Restart, host.View.generation);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Setup));
            Assert.That(Field<bool>(host.View, "rosterLocked"), Is.True);
            for (int slot = 0; slot < 4; slot++)
            {
                Assert.That(Field<int>(Session(slot).View, "localSlot"), Is.EqualTo(slot));
                Assert.That(Session(slot).View.localReady, Is.False);
            }
            for (int slot = 0; slot < 3; slot++) Session(slot).SetReady(true);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Setup));
            Session(3).SetReady(true);
            Pump();
            Assert.That(Field<bool>(host.View, "allReady"), Is.True);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Setup));
            Assert.That(SelectMode(host, PartyMode.RelayCopy), Is.True);
            Assert.That(StartSelectedMode(host), Is.True);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
        }

        [Test]
        public void MemoryCopyHidesPreparedReferenceWhenDrawingTurnBegins()
        {
            StartDrawing(PartyMode.MemoryCopy);
            CompleteDrawing(0);
            clients[0].Execute(RelayQuizAction.Ready, clients[0].View.generation);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.ObservePrevious));
            Assert.That(clients[0].View.referenceDrawing, Is.Not.Null);

            host.Tick(5f);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Drawing));
            Assert.That(clients[0].View.referenceDrawing, Is.Null);
            Assert.That(clients[0].View.drawingId, Is.Empty);
        }

        [Test]
        public void CoopMuralEmitsOneShotStartSignalWithoutStartingPrivateRelay()
        {
            ReadyLobby();
            Assert.That(SelectMode(host, PartyMode.CoopMural), Is.True);
            Pump();
            int before = Field<int>(host.View, "startSignal");

            Assert.That(StartSelectedMode(host), Is.True);
            Pump();

            int signal = Field<int>(host.View, "startSignal");
            Assert.That(signal, Is.GreaterThan(before));
            for (int slot = 0; slot < 4; slot++)
            {
                Assert.That(Session(slot).View.state, Is.EqualTo(RelayQuizState.Setup));
                Assert.That(Field<int>(Session(slot).View, "startSignal"), Is.EqualTo(signal));
                Assert.That(Field<bool>(Session(slot).View, "modeStarted"), Is.True);
                Assert.That(Session(slot).View.drawing, Is.Null);
                Assert.That(Session(slot).View.referenceDrawing, Is.Null);
                Assert.That(Field<Array>(Session(slot).View, "gallery").Length, Is.Zero);
            }
            Assert.That(StartSelectedMode(host), Is.False);
            Assert.That(Field<int>(host.View, "startSignal"), Is.EqualTo(signal));
        }

        [Test]
        public void GalleryAlonePublishesAllThreeDrawingsWithOwnerAndRevision()
        {
            ReachGuessing();
            AssertNoGalleryPayload();
            clients[2].Execute(RelayQuizAction.Submit, clients[2].View.generation);
            Pump();
            host.Execute(RelayQuizAction.OpenGallery, host.View.generation);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Gallery));
            for (int slot = 0; slot < 4; slot++)
            {
                Array gallery = Field<Array>(Session(slot).View, "gallery");
                Assert.That(gallery.Length, Is.EqualTo(3));
                for (int index = 0; index < 3; index++)
                {
                    object item = gallery.GetValue(index);
                    Assert.That(Field<int>(item, "ownerSlot"), Is.EqualTo(index));
                    Assert.That(Field<int>(item, "revision"), Is.GreaterThan(0));
                    Assert.That(Field<CanvasDrawingData>(item, "drawing"), Is.Not.Null);
                }
            }
        }

        [Test]
        public void PhaseGenerationChangePurgesUnauthorizedPrivateDrawingCache()
        {
            StartDrawing();
            CompleteDrawing(0);
            ReadyAndEnterTurn(1);
            Assert.That(Session(1).View.drawing, Is.Not.Null);
            Assert.That(Field<CanvasDrawingData>(Session(1).View, "referenceDrawing"), Is.SameAs(Session(1).View.drawing));
            CompleteDrawing(1);
            Assert.That(Session(1).View.drawing, Is.Null);
            Assert.That(Session(1).View.drawingId, Is.Empty);
        }

        [Test]
        public void AnyRosterDisconnectAbortsWithoutMigrationOrReplacement()
        {
            StartFour();
            hostTransport.RemoveFakePeer(PlayerId(2));
            host.Tick(0f);
            Assert.That(host.View.aborted, Is.True);
            Pump();
            Assert.That(Session(1).View.aborted, Is.True);
            Assert.That(Session(3).View.aborted, Is.True);
            Assert.That(host.View.word, Is.Empty);
            Assert.That(host.View.drawing, Is.Null);
            Assert.That(Field<Array>(host.View, "gallery").Length, Is.Zero);
        }

        [Test]
        public void MissingPrivatePayloadTimesOutAndAbortsInsteadOfCommittingEmpty()
        {
            StartDrawing();
            CompleteDrawing(0);
            ReadyAndEnterTurn(1);
            drawings[1] = null;
            clients[0].Execute(RelayQuizAction.CompleteDrawing, clients[0].View.generation);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Drawing));
            Assert.That(host.View.transferPending, Is.True);
            host.Tick(10f);
            Assert.That(host.View.aborted, Is.True);
        }

        [Test]
        public void QueueOverflowAbortsSession()
        {
            for (int i = 0; i < 193; i++) hostWires[0].Send(new byte[] { 1 });
            host.Tick(0f);
            Assert.That(host.View.aborted, Is.True);
        }

        [Test]
        public void MalformedOversizedAndReplayedPacketsCannotAdvanceHost()
        {
            StartDrawing();
            int generation = host.View.generation;
            hostWires[0].Send(new byte[OnlineRelayQuizProtocol.MaxMessageBytes + 1]);
            hostWires[0].Send(Encoding.UTF8.GetBytes("{broken"));
            foreach (byte[] packet in clientTransports[0].SentToHost) hostWires[0].Send(packet);
            host.Tick(0f);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Drawing));
            Assert.That(host.View.generation, Is.EqualTo(generation));
        }

        [Test]
        public void ClientRejectsPacketsFromDifferentClaimedHostIdentity()
        {
            StartFour();
            byte[] validHostPacket = hostWires[0].Received[hostWires[0].Received.Count - 1];
            LoopbackTransport.FakePeer intruder = clientTransports[0].AddFakePeer("different-host", "Intruder");
            intruder.Send(validHostPacket);
            clients[0].Tick(0f);
            Assert.That(clients[0].View.state, Is.EqualTo(RelayQuizState.Handover));
            Assert.That(clients[0].View.aborted, Is.False);
        }

        [Test]
        public void InvalidSnapshotCannotBecomeArchivedDrawing()
        {
            StartDrawing();
            drawings[0] = new CanvasDrawingData
            {
                strokes = new[] { new CanvasStrokeData { strokeId = 1, order = 0,
                    xy = new[] { 0f, 0f, float.NaN, 1f }, widthNormalized = 0.1f, brushId = 0 } }
            };
            host.Execute(RelayQuizAction.CompleteDrawing, host.View.generation);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Drawing));
            Assert.That(host.View.aborted, Is.True);
        }

        private OnlineRelayQuizSession NewSession(LoopbackTransport transport, int sourceSlot)
        {
            return new OnlineRelayQuizSession(transport, HostId, () => Secret,
                () => { captures[sourceSlot]++; return drawings[sourceSlot]; },
                () => { answerCaptures[sourceSlot]++; return answers[sourceSlot]; }, 3);
        }

        private OnlineRelayQuizSession Session(int slot) => slot == 0 ? host : clients[slot - 1];
        private static string PlayerId(int slot) => "p" + (slot + 1);

        private static CanvasDrawingData MakeDrawing(float x)
        {
            return new CanvasDrawingData
            {
                strokes = new[] { new CanvasStrokeData { strokeId = 1, order = 0,
                    xy = new[] { x, 0.2f, x + 0.1f, 0.3f }, widthNormalized = 0.1f, brushId = 0 } }
            };
        }

        private void StartFour()
        {
            ReadyLobby();
            Assert.That(SelectMode(host, PartyMode.RelayCopy), Is.True);
            Pump();
            Assert.That(StartSelectedMode(host), Is.True);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
        }

        private void ReadyLobby()
        {
            for (int slot = 0; slot < 4; slot++) Session(slot).SetReady(true);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Setup));
            Assert.That(Field<bool>(host.View, "allReady"), Is.True);
        }

        private void StartDrawing()
        {
            StartDrawing(PartyMode.RelayCopy);
        }

        private void StartDrawing(PartyMode mode)
        {
            ReadyLobby();
            Assert.That(SelectMode(host, mode), Is.True);
            Pump();
            Assert.That(StartSelectedMode(host), Is.True);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
            host.Execute(RelayQuizAction.Ready, host.View.generation);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.WordReveal));
            host.Tick(5f);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Drawing));
        }

        private void ReachGuessing()
        {
            StartDrawing();
            CompleteDrawing(0);
            ReadyAndEnterTurn(1);
            CompleteDrawing(1);
            ReadyAndEnterTurn(2);
            CompleteDrawing(2);
            ReadyAndEnterTurn(3);
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Guessing));
        }

        private void CompleteDrawing(int slot)
        {
            Session(slot).Execute(RelayQuizAction.CompleteDrawing, Session(slot).View.generation);
            Pump();
            Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Handover));
            Assert.That(host.View.transferPending, Is.False, "matching PreparedAck should release handover");
        }

        private void CompleteDrawingWithoutDestinationAck(int slot, int excludedDestination)
        {
            Session(slot).Execute(RelayQuizAction.CompleteDrawing, Session(slot).View.generation);
            for (int cycle = 0; cycle < 12; cycle++)
            {
                for (int peer = 1; peer < 4; peer++) PumpClientToHost(peer);
                for (int peer = 1; peer < 4; peer++) if (peer != excludedDestination) PumpHostToClient(peer);
                host.Tick(0f);
                for (int peer = 1; peer < 4; peer++) if (peer != excludedDestination) Session(peer).Tick(0f);
            }
        }

        private void ReadyAndEnterTurn(int slot)
        {
            Session(slot).Execute(RelayQuizAction.Ready, Session(slot).View.generation);
            Pump();
            if (slot < 3)
            {
                Assert.That(host.View.state, Is.EqualTo(RelayQuizState.ObservePrevious));
                host.Tick(5f);
                Pump();
                Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Drawing));
            }
            else Assert.That(host.View.state, Is.EqualTo(RelayQuizState.Guessing));
        }

        private int[] TrafficMarks()
        {
            var result = new int[3];
            for (int i = 0; i < 3; i++) result[i] = hostWires[i].Received.Count;
            return result;
        }

        private void AssertPrivateDrawingRecipient(int[] marks, int destinationSlot)
        {
            for (int client = 0; client < 3; client++)
            {
                int count = 0;
                for (int i = marks[client]; i < hostWires[client].Received.Count; i++)
                    if (OnlineRelayQuizProtocol.TryDecode(hostWires[client].Received[i], out OnlineRelayQuizPacket packet)
                        && packet.kind == "prepare-drawing") count++;
                Assert.That(count, client == destinationSlot - 1 ? Is.GreaterThan(0) : Is.Zero,
                    "private drawing leaked to slot " + (client + 1));
            }
        }

        private void AssertNoGalleryPayload()
        {
            for (int slot = 0; slot < 4; slot++)
                Assert.That(Field<Array>(Session(slot).View, "gallery").Length, Is.Zero);
        }

        private void SendAck(int senderSlot, string drawingId, int ownerSlot, int drawingRevision)
        {
            var ack = new AckFixture { drawingId = drawingId, destinationSlot = senderSlot,
                ownerSlot = ownerSlot, revision = drawingRevision };
            hostWires[senderSlot - 1].Send(OnlineRelayQuizProtocol.Encode(
                ClientPacket(senderSlot, "prepared-ack", JsonUtility.ToJson(ack), ownerSlot)));
        }

        private OnlineRelayQuizPacket ClientPacket(int senderSlot, string kind, string payload, int ownerSlot)
        {
            var packet = new OnlineRelayQuizPacket { sequence = NextSequence(senderSlot), kind = kind, payload = payload };
            Set(packet, "sessionId", Field<string>(host.View, "sessionId"));
            Set(packet, "rosterGeneration", Field<int>(host.View, "rosterGeneration"));
            Set(packet, "roundId", Field<int>(host.View, "roundId"));
            Set(packet, "turnId", Field<int>(host.View, "turnId"));
            Set(packet, "ownerSlot", ownerSlot);
            Set(packet, "revision", Field<int>(host.View, "revision"));
            Set(packet, "selectedMode", Field<bool>(host.View, "hasSelectedMode")
                ? (int)Field<PartyMode>(host.View, "selectedMode") : -1);
            Set(packet, "modeGeneration", Field<int>(host.View, "modeGeneration"));
            Set(packet, "startSignal", Field<int>(host.View, "startSignal"));
            return packet;
        }

        private long NextSequence(int senderSlot)
        {
            OnlineRelayQuizSession sender = Session(senderSlot);
            long next = Field<long>(sender, "outgoingSequence") + 1;
            Set(sender, "outgoingSequence", next);
            return next;
        }

        private void Pump(int cycles = 24)
        {
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                for (int slot = 1; slot < 4; slot++) PumpClientToHost(slot);
                for (int slot = 1; slot < 4; slot++) PumpHostToClient(slot);
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

        private static T Field<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(target);
        }

        private static void Set(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static bool SelectMode(OnlineRelayQuizSession session, PartyMode mode)
        {
            MethodInfo method = session.GetType().GetMethod("SelectMode", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, "SelectMode(PartyMode)");
            return (bool)method.Invoke(session, new object[] { mode });
        }

        private static bool StartSelectedMode(OnlineRelayQuizSession session)
        {
            MethodInfo method = session.GetType().GetMethod("StartSelectedMode", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, "StartSelectedMode()");
            return (bool)method.Invoke(session, Array.Empty<object>());
        }
    }
}
