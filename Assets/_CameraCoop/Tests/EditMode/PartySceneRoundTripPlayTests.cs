using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CameraCoop.Netplay;
using CameraCoop.Party;
using CameraCoop.Party.SceneFlow;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CameraCoop.Tests
{
    public sealed class PartySceneRoundTripPlayTests
    {
        private const string HostId = "host-id";
        private const string Secret = "비밀단어";
        private readonly List<LoopbackTransport> clientTransports = new List<LoopbackTransport>();
        private readonly List<OnlineRelayQuizSession> clients = new List<OnlineRelayQuizSession>();
        private readonly List<LoopbackTransport.FakePeer> hostWires = new List<LoopbackTransport.FakePeer>();
        private readonly List<LoopbackTransport.FakePeer> clientWires = new List<LoopbackTransport.FakePeer>();
        private readonly int[] sentToHost = new int[PartyRoster.Capacity];
        private readonly int[] sentToClient = new int[PartyRoster.Capacity];
        private readonly CanvasDrawingData[] drawings = new CanvasDrawingData[PartyRoster.Capacity];
        private LoopbackTransport hostTransport;
        private OnlineRelayQuizSession host;
        private GameObject fixtureRoot;
        private GameObject lobbyRoot;
        private OnlineRelayQuizController controller;
        private PartySceneCoordinator coordinator;

        [SetUp]
        public void SetUp()
        {
            // 앞선 Scene 테스트가 production Scene을 열어둔 채 끝날 수 있다. 빈 Scene에서 시작한다.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                drawings[slot] = Drawing(0.1f + slot * 0.2f);

            hostTransport = new LoopbackTransport(true, HostId);
            host = NewSession(hostTransport, 0);
            for (int slot = 1; slot < PartyRoster.Capacity; slot++)
            {
                string id = PlayerId(slot);
                var transport = new LoopbackTransport(false, id);
                clientTransports.Add(transport);
                clients.Add(NewSession(transport, slot));
                hostWires.Add(hostTransport.AddFakePeer(id, "P" + (slot + 1)));
                clientWires.Add(transport.AddFakePeer(HostId, "Host"));
            }
            Pump();
            ConfigureProductionSceneBoundary();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (coordinator != null && (coordinator.BoundAdapter != null || coordinator.IsOperationInFlight))
            {
                coordinator.ShutdownSceneBoundary();
                yield return WaitForSceneOperation();
            }
            foreach (PartyMode mode in new[] { PartyMode.RelayCopy, PartyMode.MemoryCopy, PartyMode.CoopMural })
            {
                PartySceneCatalog.TryGet(mode, out PartySceneDefinition target);
                Scene scene = SceneManager.GetSceneByPath(target.ScenePath);
                if (scene.IsValid() && scene.isLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    if (unload != null) yield return unload;
                }
            }
            if (fixtureRoot != null) UnityEngine.Object.DestroyImmediate(fixtureRoot);
            host?.Dispose();
            foreach (OnlineRelayQuizSession client in clients) client.Dispose();
            clients.Clear();
            clientTransports.Clear();
            hostWires.Clear();
            clientWires.Clear();
            Array.Clear(sentToHost, 0, sentToHost.Length);
            Array.Clear(sentToClient, 0, sentToClient.Length);
        }

        [UnityTest]
        public IEnumerator FourPeersRoundTripAllThreeModesThroughProductionScenes()
        {
            foreach (PartyMode mode in new[] { PartyMode.RelayCopy, PartyMode.MemoryCopy, PartyMode.CoopMural })
            {
                ReadyLobby();
                BeginModeLoad(mode);

                coordinator.ApplyView(host.View);
                yield return WaitForSceneOperation();
                Pump();

                PartySceneDefinition target = Target(mode);
                Scene gameScene = SceneManager.GetSceneByPath(target.ScenePath);
                Assert.That(gameScene.IsValid() && gameScene.isLoaded, Is.True, target.ScenePath);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(target.ScenePath));
                Assert.That(coordinator.BoundAdapter, Is.TypeOf<PartyGameSceneAdapter>());
                Assert.That(coordinator.BoundAdapter.Mode, Is.EqualTo(mode));

                CompleteRemoteLoadBarrier();
                Assert.That(host.View.transitionPhase, Is.EqualTo(PartyTransitionPhase.InGame));
                BeginReturnFromResult(mode);
                Pump();

                coordinator.ApplyView(host.View);
                yield return WaitForSceneOperation();
                CompleteRemoteLobbyBarrier();

                Assert.That(host.View.transitionPhase, Is.EqualTo(PartyTransitionPhase.Lobby));
                Assert.That(SceneManager.GetSceneByPath(target.ScenePath).isLoaded, Is.False);
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(lobbyRoot.scene));
                Assert.That(coordinator.BoundAdapter, Is.Null);
            }
        }

        [UnityTest]
        public IEnumerator LoadBarrierTimeoutUnloadsTheRealSceneAndRestoresLobby()
        {
            ReadyLobby();
            BeginModeLoad(PartyMode.RelayCopy);
            PartySceneDefinition target = Target(PartyMode.RelayCopy);

            coordinator.ApplyView(host.View);
            yield return WaitForSceneOperation();
            Pump();
            Assert.That(SceneManager.GetSceneByPath(target.ScenePath).isLoaded, Is.True);
            Assert.That(host.View.sceneReadyMask, Is.EqualTo(1));

            host.Tick(15f);
            Pump();
            Assert.That(host.View.transitionPhase, Is.EqualTo(PartyTransitionPhase.ReturningToLobby));
            coordinator.ApplyView(host.View);
            yield return WaitForSceneOperation();
            CompleteRemoteLobbyBarrier();

            Assert.That(host.View.transitionPhase, Is.EqualTo(PartyTransitionPhase.Lobby));
            Assert.That(SceneManager.GetSceneByPath(target.ScenePath).isLoaded, Is.False);
            Assert.That(coordinator.BoundAdapter, Is.Null);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(lobbyRoot.scene));
        }

        [UnityTest]
        public IEnumerator RosterDisconnectClosesTheLoadedProductionSceneBoundary()
        {
            ReadyLobby();
            BeginModeLoad(PartyMode.CoopMural);
            PartySceneDefinition target = Target(PartyMode.CoopMural);
            coordinator.ApplyView(host.View);
            yield return WaitForSceneOperation();
            Pump();
            CompleteRemoteLoadBarrier();
            Assert.That(SceneManager.GetSceneByPath(target.ScenePath).isLoaded, Is.True);

            hostTransport.RemoveFakePeer(PlayerId(2));
            host.Tick(0f);
            Pump();
            Assert.That(host.View.aborted, Is.True);
            Assert.That(host.View.connected, Is.False);

            coordinator.ApplyView(host.View);
            yield return WaitForSceneOperation();

            Assert.That(SceneManager.GetSceneByPath(target.ScenePath).isLoaded, Is.False,
                "an aborted session must not leave its additive game Scene open");
            Assert.That(coordinator.BoundAdapter, Is.Null);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(lobbyRoot.scene));
        }

        private void ConfigureProductionSceneBoundary()
        {
            fixtureRoot = new GameObject("Party roundtrip fixture");
            controller = fixtureRoot.AddComponent<OnlineRelayQuizController>();
            controller.enabled = false;
            lobbyRoot = new GameObject("Lobby world");
            lobbyRoot.transform.SetParent(fixtureRoot.transform, false);
            PartyLobbyScenePort lobbyPort = fixtureRoot.AddComponent<PartyLobbyScenePort>();
            var spawns = new Transform[PartyRoster.Capacity];
            var practiceRoots = new GameObject[PartyRoster.Capacity];
            var practicePresenters = new CanvasDrawingPresenter[PartyRoster.Capacity];
            var practiceSurfaces = new CanvasSurface[PartyRoster.Capacity];
            var avatarRoots = new GameObject[PartyRoster.Capacity];
            var avatarPresenters = new RemoteAvatarPresenter[PartyRoster.Capacity - 1];
            for (int slot = 0; slot < PartyRoster.Capacity; slot++)
            {
                spawns[slot] = Child("Spawn " + slot).transform;
                practiceRoots[slot] = Child("Practice " + slot);
                practicePresenters[slot] = Child("Practice presenter " + slot).AddComponent<CanvasDrawingPresenter>();
                practiceSurfaces[slot] = Child("Practice surface " + slot).AddComponent<CanvasSurface>();
                avatarRoots[slot] = Child("Avatar " + slot);
                if (slot < avatarPresenters.Length)
                    avatarPresenters[slot] = Child("Avatar presenter " + slot).AddComponent<RemoteAvatarPresenter>();
            }
            lobbyPort.Configure(lobbyRoot, spawns, practiceRoots, practicePresenters, practiceSurfaces,
                avatarRoots, avatarPresenters);
            SetField(controller, "session", host);
            SetField(controller, "lobbyScenePort", lobbyPort);
            MethodInfo initialize = typeof(OnlineRelayQuizController).GetMethod("TryInitializeSceneCoordinator",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(initialize, Is.Not.Null);
            object[] arguments = { null };
            Assert.That((bool)initialize.Invoke(controller, arguments), Is.True, arguments[0]?.ToString());
            coordinator = Field<PartySceneCoordinator>(controller, "sceneCoordinator");
        }

        private GameObject Child(string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(lobbyRoot.transform, false);
            return child;
        }

        private IEnumerator WaitForSceneOperation()
        {
            int frames = 0;
            while (coordinator.IsOperationInFlight && frames++ < 600) yield return null;
            Assert.That(coordinator.IsOperationInFlight, Is.False, "production Scene operation timed out");
            yield return null;
        }

        private void ReadyLobby()
        {
            for (int slot = 0; slot < PartyRoster.Capacity; slot++) Session(slot).SetReady(true);
            Pump();
            Assert.That(host.View.allReady, Is.True);
        }

        private void BeginModeLoad(PartyMode mode)
        {
            Assert.That(host.OpenModeSelector(), Is.True);
            Pump();
            Assert.That(host.SelectModeAndBeginLoad(mode), Is.True);
            Pump();
            Assert.That(host.View.transitionPhase, Is.EqualTo(PartyTransitionPhase.LoadingGame));
        }

        private void CompleteRemoteLoadBarrier()
        {
            int transition = host.View.transitionGeneration;
            for (int slot = 1; slot < PartyRoster.Capacity; slot++)
                Assert.That(Session(slot).MarkLocalSceneReady(transition), Is.True);
            Pump();
        }

        private void BeginReturnFromResult(PartyMode mode)
        {
            if (mode == PartyMode.CoopMural)
            {
                Assert.That(host.MarkCoopMuralFinalDisplay(), Is.True);
            }
            else
            {
                host.Execute(RelayQuizAction.Ready, host.View.generation);
                Pump();
                host.Tick(5f);
                Pump();
                for (int slot = 0; slot < PartyRoster.Capacity; slot++)
                {
                    Session(slot).Execute(RelayQuizAction.CompleteDrawing, Session(slot).View.generation);
                    Pump();
                    if (slot == PartyRoster.Capacity - 1) break;
                    Session(slot + 1).Execute(RelayQuizAction.Ready, Session(slot + 1).View.generation);
                    Pump();
                    host.Tick(5f);
                    Pump();
                }
                clients[2].Execute(RelayQuizAction.Submit, clients[2].View.generation);
                Pump();
            }
            Assert.That(host.RequestReturnToLobby(), Is.True);
        }

        private void CompleteRemoteLobbyBarrier()
        {
            Pump();
            int transition = host.View.transitionGeneration;
            for (int slot = 1; slot < PartyRoster.Capacity; slot++)
                Assert.That(Session(slot).MarkLocalLobbyReady(transition), Is.True);
            Pump();
        }

        private OnlineRelayQuizSession NewSession(LoopbackTransport transport, int slot)
        {
            return new OnlineRelayQuizSession(transport, HostId, () => Secret, () => drawings[slot],
                () => slot == PartyRoster.Capacity - 1 ? Secret : "wrong-" + slot, 3);
        }

        private OnlineRelayQuizSession Session(int slot) => slot == 0 ? host : clients[slot - 1];
        private static string PlayerId(int slot) => "p" + (slot + 1);

        private void Pump(int cycles = 24)
        {
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                for (int slot = 1; slot < PartyRoster.Capacity; slot++)
                {
                    LoopbackTransport transport = clientTransports[slot - 1];
                    while (sentToHost[slot] < transport.SentToHost.Count)
                        hostWires[slot - 1].Send(transport.SentToHost[sentToHost[slot]++]);
                    LoopbackTransport.FakePeer wire = hostWires[slot - 1];
                    while (sentToClient[slot] < wire.Received.Count)
                        clientWires[slot - 1].Send(wire.Received[sentToClient[slot]++]);
                }
                host.Tick(0f);
                foreach (OnlineRelayQuizSession client in clients) client.Tick(0f);
            }
        }

        private static PartySceneDefinition Target(PartyMode mode)
        {
            Assert.That(PartySceneCatalog.TryGet(mode, out PartySceneDefinition target), Is.True);
            return target;
        }

        private static CanvasDrawingData Drawing(float x)
        {
            return new CanvasDrawingData
            {
                strokes = new[]
                {
                    new CanvasStrokeData
                    {
                        strokeId = 1,
                        order = 0,
                        xy = new[] { x, 0.2f, x + 0.1f, 0.3f },
                        widthNormalized = 0.1f,
                        brushId = 0
                    }
                }
            };
        }

        private static T Field<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
