using System;
using System.Collections.Generic;
using CameraCoop.Party;
using CameraCoop.Party.SceneFlow;
using NUnit.Framework;
using UnityEngine;

namespace CameraCoop.Tests
{
    public sealed class PartySceneCoordinatorTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        [TearDown]
        public void TearDown()
        {
            foreach (GameObject item in objects)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test]
        public void DuplicateLoadingSnapshotsLoadBindActivateAndAcknowledgeExactlyOnce()
        {
            Fixture fixture = CreateFixture(PartyMode.RelayCopy);
            OnlineRelayQuizView loading = View("session-a", 2, 4, 10, PartyTransitionPhase.LoadingGame, PartyMode.RelayCopy);

            fixture.Coordinator.ApplyView(loading);
            fixture.Coordinator.ApplyView(loading);
            fixture.Coordinator.ApplyView(loading);

            Assert.That(fixture.Loader.LoadCalls, Is.EqualTo(1));
            Assert.That(fixture.Loader.UnloadCalls, Is.Zero);
            Assert.That(fixture.Runner.PendingCount, Is.EqualTo(1));

            fixture.Runner.CompleteNext();
            fixture.Coordinator.ApplyView(View("session-a", 2, 4, 11, PartyTransitionPhase.InGame, PartyMode.RelayCopy));
            fixture.Coordinator.ApplyView(View("session-a", 2, 4, 11, PartyTransitionPhase.InGame, PartyMode.RelayCopy));

            CollectionAssert.AreEqual(
                new[] { "bind", "rebase-game", "scene-ready" },
                fixture.Callbacks.Events);
            Assert.That(fixture.Loader.LoadCalls, Is.EqualTo(1));
            Assert.That(fixture.Loader.ActiveCalls, Is.EqualTo(1));
            Assert.That(fixture.Callbacks.SceneReadyCalls, Is.EqualTo(1));
            Assert.That(fixture.Callbacks.FailureCalls, Is.Zero);
            Assert.That(fixture.Callbacks.LobbyWasHiddenWhenGameRebased, Is.True);
            Assert.That(fixture.LobbyRoot.activeSelf, Is.False);
        }

        [Test]
        public void DuplicateReturnSnapshotsUnbindBeforeUnloadThenRestoreLobbyOnce()
        {
            Fixture fixture = CreateFixture(PartyMode.RelayCopy);
            fixture.Coordinator.ApplyView(View("session-a", 2, 4, 10, PartyTransitionPhase.LoadingGame, PartyMode.RelayCopy));
            fixture.Runner.CompleteNext();
            fixture.Callbacks.Events.Clear();

            OnlineRelayQuizView returning = View("session-a", 3, 5, 12, PartyTransitionPhase.ReturningToLobby, PartyMode.RelayCopy);
            fixture.Coordinator.ApplyView(returning);
            fixture.Coordinator.ApplyView(returning);

            CollectionAssert.AreEqual(new[] { "disable", "unbind" }, fixture.Callbacks.Events);
            Assert.That(fixture.Loader.UnloadCalls, Is.EqualTo(1));
            Assert.That(fixture.Runner.PendingCount, Is.EqualTo(1));

            fixture.Runner.CompleteNext();
            fixture.Coordinator.ApplyView(View("session-a", 3, 5, 13, PartyTransitionPhase.Lobby, PartyMode.RelayCopy));

            CollectionAssert.AreEqual(
                new[] { "disable", "unbind", "activate-lobby", "rebase-lobby", "lobby-ready" },
                fixture.Callbacks.Events);
            Assert.That(fixture.Loader.UnloadCalls, Is.EqualTo(1));
            Assert.That(fixture.Callbacks.LobbyReadyCalls, Is.EqualTo(1));
            Assert.That(fixture.Callbacks.LobbyWasVisibleWhenLobbyRebased, Is.True);
            Assert.That(fixture.LobbyRoot.activeSelf, Is.True);
        }

        [Test]
        public void WrongModeAdapterReportsOneFailureAndDoesNotBindOrAcknowledge()
        {
            Fixture fixture = CreateFixture(PartyMode.RelayCopy);
            OnlineRelayQuizView loading = View("session-a", 2, 4, 10, PartyTransitionPhase.LoadingGame, PartyMode.MemoryCopy);

            fixture.Coordinator.ApplyView(loading);
            fixture.Runner.CompleteNext();
            fixture.Coordinator.ApplyView(loading);

            Assert.That(fixture.Callbacks.FailureCalls, Is.EqualTo(1));
            Assert.That(fixture.Callbacks.LastFailure, Is.EqualTo(PartySceneLoadFailure.ModeMismatch));
            Assert.That(fixture.Callbacks.SceneReadyCalls, Is.Zero);
            Assert.That(fixture.Callbacks.Events, Is.Empty);
            Assert.That(fixture.LobbyRoot.activeSelf, Is.True);
        }

        [TestCase(0)]
        [TestCase(2)]
        public void MissingOrDuplicateAdaptersReportOneFailureAndLeaveLobbyVisible(int adapterCount)
        {
            Fixture fixture = CreateFixture(PartyMode.RelayCopy);
            fixture.Resolver.Adapters.Clear();
            for (int index = 0; index < adapterCount; index++)
                fixture.Resolver.Adapters.Add(CreateAdapter(PartyMode.RelayCopy));

            OnlineRelayQuizView loading = View("session-a", 2, 4, 10, PartyTransitionPhase.LoadingGame, PartyMode.RelayCopy);
            fixture.Coordinator.ApplyView(loading);
            fixture.Runner.CompleteNext();
            fixture.Coordinator.ApplyView(loading);

            Assert.That(fixture.Callbacks.FailureCalls, Is.EqualTo(1));
            Assert.That(fixture.Callbacks.LastFailure, Is.EqualTo(PartySceneLoadFailure.MissingAdapter));
            Assert.That(fixture.Callbacks.SceneReadyCalls, Is.Zero);
            Assert.That(fixture.LobbyRoot.activeSelf, Is.True);
        }

        [Test]
        public void LoadAndActivationFailuresAreReportedOnceWithoutReadyAck()
        {
            Fixture loadFailure = CreateFixture(PartyMode.RelayCopy);
            loadFailure.Loader.LoadFailure = PartySceneLoadFailure.LoadFailed;
            OnlineRelayQuizView loading = View("session-a", 2, 4, 10, PartyTransitionPhase.LoadingGame, PartyMode.RelayCopy);

            loadFailure.Coordinator.ApplyView(loading);
            loadFailure.Coordinator.ApplyView(loading);

            Assert.That(loadFailure.Callbacks.FailureCalls, Is.EqualTo(1));
            Assert.That(loadFailure.Callbacks.LastFailure, Is.EqualTo(PartySceneLoadFailure.LoadFailed));
            Assert.That(loadFailure.Callbacks.SceneReadyCalls, Is.Zero);

            Fixture activationFailure = CreateFixture(PartyMode.MemoryCopy);
            activationFailure.Loader.ActiveFailure = PartySceneLoadFailure.ActivationFailed;
            activationFailure.Coordinator.ApplyView(View("session-b", 2, 4, 10, PartyTransitionPhase.LoadingGame, PartyMode.MemoryCopy));
            activationFailure.Runner.CompleteNext();

            Assert.That(activationFailure.Callbacks.FailureCalls, Is.EqualTo(1));
            Assert.That(activationFailure.Callbacks.LastFailure, Is.EqualTo(PartySceneLoadFailure.ActivationFailed));
            Assert.That(activationFailure.Callbacks.SceneReadyCalls, Is.Zero);
            Assert.That(activationFailure.Callbacks.Events, Is.EqualTo(new[] { "bind", "disable", "unbind" }));
            Assert.That(activationFailure.LobbyRoot.activeSelf, Is.True);
        }

        [Test]
        public void UnloadFailureReportsOnceAfterInteractionsAreDisabledAndDoesNotAckLobbyReady()
        {
            Fixture fixture = CreateFixture(PartyMode.CoopMural);
            fixture.Coordinator.ApplyView(View("session-a", 2, 4, 10, PartyTransitionPhase.LoadingGame, PartyMode.CoopMural));
            fixture.Runner.CompleteNext();
            fixture.Callbacks.Events.Clear();
            fixture.Loader.UnloadFailure = PartySceneLoadFailure.UnloadFailed;

            OnlineRelayQuizView returning = View("session-a", 3, 5, 12, PartyTransitionPhase.ReturningToLobby, PartyMode.CoopMural);
            fixture.Coordinator.ApplyView(returning);
            fixture.Coordinator.ApplyView(returning);

            CollectionAssert.AreEqual(new[] { "disable", "unbind" }, fixture.Callbacks.Events);
            Assert.That(fixture.Callbacks.FailureCalls, Is.EqualTo(1));
            Assert.That(fixture.Callbacks.LastFailure, Is.EqualTo(PartySceneLoadFailure.UnloadFailed));
            Assert.That(fixture.Callbacks.LobbyReadyCalls, Is.Zero);
        }

        [Test]
        public void OlderViewsAndNewerViewWhileOperationIsInFlightDoNotStartASecondOperation()
        {
            Fixture fixture = CreateFixture(PartyMode.RelayCopy);
            fixture.Coordinator.ApplyView(View("session-a", 3, 8, 20, PartyTransitionPhase.LoadingGame, PartyMode.RelayCopy));

            fixture.Coordinator.ApplyView(View("session-a", 2, 99, 30, PartyTransitionPhase.ReturningToLobby, PartyMode.RelayCopy));
            fixture.Coordinator.ApplyView(View("session-a", 3, 7, 31, PartyTransitionPhase.ReturningToLobby, PartyMode.RelayCopy));
            fixture.Coordinator.ApplyView(View("session-a", 4, 9, 32, PartyTransitionPhase.ReturningToLobby, PartyMode.RelayCopy));

            Assert.That(fixture.Loader.LoadCalls, Is.EqualTo(1));
            Assert.That(fixture.Loader.UnloadCalls, Is.Zero);
            Assert.That(fixture.Runner.PendingCount, Is.EqualTo(1));

            fixture.Runner.CompleteNext();

            Assert.That(fixture.Loader.UnloadCalls, Is.EqualTo(1));
            Assert.That(fixture.Runner.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void DisposeUnbindsRegisteredAdapterAndIgnoresLateOperationCompletion()
        {
            Fixture fixture = CreateFixture(PartyMode.RelayCopy);
            fixture.Coordinator.ApplyView(View("session-a", 2, 4, 10, PartyTransitionPhase.LoadingGame, PartyMode.RelayCopy));
            fixture.Runner.CompleteNext();
            fixture.Callbacks.Events.Clear();

            fixture.Coordinator.Dispose();
            fixture.Coordinator.Dispose();

            CollectionAssert.AreEqual(new[] { "disable", "unbind" }, fixture.Callbacks.Events);
            Assert.That(fixture.Callbacks.SceneReadyCalls, Is.EqualTo(1));
            Assert.That(fixture.Loader.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public void DelayedForeignSessionViewIsIgnoredWhileCoordinatorIsIdle()
        {
            Fixture fixture = CreateFixture(PartyMode.RelayCopy);
            fixture.Coordinator.ApplyView(View("trusted-session", 2, 4, 10, PartyTransitionPhase.Lobby, PartyMode.RelayCopy));

            fixture.Coordinator.ApplyView(View("foreign-session", 9, 99, 100, PartyTransitionPhase.LoadingGame, PartyMode.RelayCopy));

            Assert.That(fixture.Loader.LoadCalls, Is.Zero);
            Assert.That(fixture.Runner.PendingCount, Is.Zero);
            Assert.That(fixture.Callbacks.SceneReadyCalls, Is.Zero);
            Assert.That(fixture.Callbacks.FailureCalls, Is.Zero);
            Assert.That(fixture.Coordinator.AppliedTransitionKey.SessionId, Is.EqualTo("trusted-session"));
        }

        [Test]
        public void DelayedForeignSessionViewIsNotQueuedWhileTrustedLoadIsInFlight()
        {
            Fixture fixture = CreateFixture(PartyMode.RelayCopy);
            fixture.Coordinator.ApplyView(View("trusted-session", 2, 4, 10, PartyTransitionPhase.LoadingGame, PartyMode.RelayCopy));

            fixture.Coordinator.ApplyView(View("foreign-session", 9, 99, 100, PartyTransitionPhase.ReturningToLobby, PartyMode.RelayCopy));
            fixture.Runner.CompleteNext();

            Assert.That(fixture.Loader.LoadCalls, Is.EqualTo(1));
            Assert.That(fixture.Loader.UnloadCalls, Is.Zero);
            Assert.That(fixture.Callbacks.SceneReadyCalls, Is.EqualTo(1));
            Assert.That(fixture.Callbacks.LobbyReadyCalls, Is.Zero);
            Assert.That(fixture.Coordinator.AppliedTransitionKey.SessionId, Is.EqualTo("trusted-session"));
        }

        [Test]
        public void ExplicitTrustedSessionResetAllowsNewSessionAfterIdleLeaveBoundary()
        {
            Fixture fixture = CreateFixture(PartyMode.MemoryCopy);
            fixture.Coordinator.ApplyView(View("old-session", 2, 4, 10, PartyTransitionPhase.Lobby, PartyMode.MemoryCopy));

            fixture.Coordinator.ResetForSession("new-session");
            fixture.Coordinator.ApplyView(View("old-session", 9, 99, 100, PartyTransitionPhase.LoadingGame, PartyMode.MemoryCopy));
            fixture.Coordinator.ApplyView(View("new-session", 0, 1, 1, PartyTransitionPhase.LoadingGame, PartyMode.MemoryCopy));

            Assert.That(fixture.Loader.LoadCalls, Is.EqualTo(1));
            Assert.That(fixture.Runner.PendingCount, Is.EqualTo(1));
            Assert.That(fixture.Coordinator.AppliedTransitionKey.SessionId, Is.EqualTo("new-session"));
        }

        private Fixture CreateFixture(PartyMode adapterMode)
        {
            GameObject coordinatorObject = CreateObject("Coordinator");
            PartySceneCoordinator coordinator = coordinatorObject.AddComponent<PartySceneCoordinator>();
            GameObject lobbyRoot = CreateObject("Lobby world");
            GameObject lobbyPortObject = CreateObject("Lobby port");
            PartyLobbyScenePort lobbyPort = lobbyPortObject.AddComponent<PartyLobbyScenePort>();
            var spawns = new Transform[PartyRoster.Capacity];
            for (int slot = 0; slot < spawns.Length; slot++)
                spawns[slot] = CreateObject("Lobby spawn " + slot, lobbyRoot.transform).transform;
            lobbyPort.Configure(lobbyRoot, spawns);

            var loader = new FakeLoader();
            var resolver = new FakeResolver();
            resolver.Adapters.Add(CreateAdapter(adapterMode));
            var callbacks = new FakeCallbacks(lobbyRoot);
            var runner = new FakeRunner();
            coordinator.Configure(loader, resolver, lobbyPort, callbacks, runner);
            return new Fixture(coordinator, loader, resolver, callbacks, runner, lobbyRoot);
        }

        private IPartyGameScenePort CreateAdapter(PartyMode mode)
        {
            return new FakeAdapter(mode);
        }

        private static OnlineRelayQuizView View(
            string sessionId,
            int rosterGeneration,
            int transitionGeneration,
            int serial,
            PartyTransitionPhase phase,
            PartyMode mode)
        {
            return new OnlineRelayQuizView
            {
                connected = true,
                sessionId = sessionId,
                rosterGeneration = rosterGeneration,
                transitionGeneration = transitionGeneration,
                serial = serial,
                transitionPhase = phase,
                hasSelectedMode = phase != PartyTransitionPhase.Lobby && phase != PartyTransitionPhase.SelectingMode,
                selectedMode = mode
            };
        }

        private GameObject CreateObject(string name, Transform parent = null)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            objects.Add(item);
            return item;
        }

        private sealed class Fixture
        {
            internal Fixture(PartySceneCoordinator coordinator, FakeLoader loader, FakeResolver resolver, FakeCallbacks callbacks, FakeRunner runner, GameObject lobbyRoot)
            {
                Coordinator = coordinator;
                Loader = loader;
                Resolver = resolver;
                Callbacks = callbacks;
                Runner = runner;
                LobbyRoot = lobbyRoot;
            }

            internal PartySceneCoordinator Coordinator { get; }
            internal FakeLoader Loader { get; }
            internal FakeResolver Resolver { get; }
            internal FakeCallbacks Callbacks { get; }
            internal FakeRunner Runner { get; }
            internal GameObject LobbyRoot { get; }
        }

        private sealed class FakeOperation : IPartySceneLoadOperation
        {
            public bool IsDone { get; private set; }
            internal void Complete() => IsDone = true;
        }

        private sealed class FakeLoader : IPartySceneLoader
        {
            internal int LoadCalls { get; private set; }
            internal int ActiveCalls { get; private set; }
            internal int UnloadCalls { get; private set; }
            internal int DisposeCalls { get; private set; }
            internal PartySceneLoadFailure LoadFailure { get; set; }
            internal PartySceneLoadFailure ActiveFailure { get; set; }
            internal PartySceneLoadFailure UnloadFailure { get; set; }
            public bool IsOperationInFlight => false;

            public PartySceneLoadResult LoadAdditive(PartySceneDefinition target)
            {
                LoadCalls++;
                return LoadFailure == PartySceneLoadFailure.None
                    ? PartySceneLoadResult.Success(new FakeOperation())
                    : PartySceneLoadResult.Failed(LoadFailure, "load failed");
            }

            public PartySceneLoadResult SetActive(PartySceneDefinition target)
            {
                ActiveCalls++;
                return ActiveFailure == PartySceneLoadFailure.None
                    ? PartySceneLoadResult.Success()
                    : PartySceneLoadResult.Failed(ActiveFailure, "activation failed");
            }

            public PartySceneLoadResult Unload(PartySceneDefinition target)
            {
                UnloadCalls++;
                return UnloadFailure == PartySceneLoadFailure.None
                    ? PartySceneLoadResult.Success(new FakeOperation())
                    : PartySceneLoadResult.Failed(UnloadFailure, "unload failed");
            }

            public void Dispose() => DisposeCalls++;
        }

        private sealed class FakeResolver : IPartyGameSceneResolver
        {
            internal List<IPartyGameScenePort> Adapters { get; } = new List<IPartyGameScenePort>();
            public IReadOnlyList<IPartyGameScenePort> Resolve(string scenePath) => Adapters;
        }

        private sealed class FakeAdapter : IPartyGameScenePort
        {
            internal FakeAdapter(PartyMode mode) => Mode = mode;
            public PartyMode Mode { get; }
            public PartySceneBindings Bindings => null;
            public bool IsRegistered { get; private set; }
            public bool ValidateBindings(out string error) { error = string.Empty; return true; }
            public bool Register(PartyMode expectedMode, PartyTransitionKey transitionKey, out string error)
            {
                if (expectedMode != Mode) { error = "mode mismatch"; return false; }
                IsRegistered = true;
                error = string.Empty;
                return true;
            }
            public void Unregister() => IsRegistered = false;
        }

        private sealed class FakeCallbacks : IPartySceneCoordinatorCallbacks
        {
            private readonly GameObject lobbyRoot;
            internal FakeCallbacks(GameObject lobbyRoot) => this.lobbyRoot = lobbyRoot;
            internal List<string> Events { get; } = new List<string>();
            internal int SceneReadyCalls { get; private set; }
            internal int LobbyReadyCalls { get; private set; }
            internal int FailureCalls { get; private set; }
            internal PartySceneLoadFailure LastFailure { get; private set; }
            internal bool LobbyWasHiddenWhenGameRebased { get; private set; }
            internal bool LobbyWasVisibleWhenLobbyRebased { get; private set; }

            public void BindGameScene(IPartyGameScenePort adapter) => Events.Add("bind");
            public void DisableGameSceneInteractions(IPartyGameScenePort adapter) => Events.Add("disable");
            public void UnbindGameScene(IPartyGameScenePort adapter) => Events.Add("unbind");
            public bool ActivateLobbyScene() { Events.Add("activate-lobby"); return true; }
            public void RebaseToGame(IPartyGameScenePort adapter)
            {
                LobbyWasHiddenWhenGameRebased = !lobbyRoot.activeSelf;
                Events.Add("rebase-game");
            }
            public void RebaseToLobby(PartyLobbyScenePort lobby)
            {
                LobbyWasVisibleWhenLobbyRebased = lobbyRoot.activeSelf;
                Events.Add("rebase-lobby");
            }
            public bool MarkLocalSceneReady(int generation) { SceneReadyCalls++; Events.Add("scene-ready"); return true; }
            public bool MarkLocalLobbyReady(int generation) { LobbyReadyCalls++; Events.Add("lobby-ready"); return true; }
            public void ReportSceneLoadFailure(int generation, PartySceneLoadFailure failure) { FailureCalls++; LastFailure = failure; }
        }

        private sealed class FakeRunner : IPartySceneOperationRunner
        {
            private readonly Queue<Action> completions = new Queue<Action>();
            internal int PendingCount => completions.Count;

            public void Run(IPartySceneLoadOperation operation, Action completed)
            {
                completions.Enqueue(() =>
                {
                    ((FakeOperation)operation).Complete();
                    completed();
                });
            }

            internal void CompleteNext() => completions.Dequeue().Invoke();
        }
    }
}
