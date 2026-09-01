using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CameraCoop.Party.SceneFlow
{
    public interface IPartyGameSceneResolver
    {
        IReadOnlyList<IPartyGameScenePort> Resolve(string scenePath);
    }

    public interface IPartySceneOperationRunner
    {
        void Run(IPartySceneLoadOperation operation, Action completed);
    }

    public interface IPartySceneCoordinatorCallbacks
    {
        void BindGameScene(IPartyGameScenePort adapter);
        void DisableGameSceneInteractions(IPartyGameScenePort adapter);
        void UnbindGameScene(IPartyGameScenePort adapter);
        bool ActivateLobbyScene();
        void RebaseToGame(IPartyGameScenePort adapter);
        void RebaseToLobby(PartyLobbyScenePort lobby);
        bool MarkLocalSceneReady(int generation);
        bool MarkLocalLobbyReady(int generation);
        void ReportSceneLoadFailure(int generation, PartySceneLoadFailure failure);
    }

    public sealed class UnityPartyGameSceneResolver : IPartyGameSceneResolver
    {
        public IReadOnlyList<IPartyGameScenePort> Resolve(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded) return Array.Empty<IPartyGameScenePort>();

            var result = new List<IPartyGameScenePort>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                PartyGameSceneAdapter[] adapters = roots[rootIndex].GetComponentsInChildren<PartyGameSceneAdapter>(true);
                for (int adapterIndex = 0; adapterIndex < adapters.Length; adapterIndex++)
                    result.Add(adapters[adapterIndex]);
            }
            return result;
        }
    }

    public sealed class PartySceneCoordinator : MonoBehaviour, IDisposable
    {
        private IPartySceneLoader loader;
        private IPartyGameSceneResolver resolver;
        private PartyLobbyScenePort lobby;
        private IPartySceneCoordinatorCallbacks callbacks;
        private IPartySceneOperationRunner runner;
        private bool ownsLoader;
        private bool configured;
        private bool disposed;
        private bool operationInFlight;
        private bool hasAppliedView;
        private PartyTransitionKey appliedKey;
        private PartyTransitionPhase appliedPhase;
        private int appliedSerial;
        private PartySceneDefinition loadedScene;
        private IPartyGameScenePort boundAdapter;
        private PendingTransition pending;
        private bool hasPending;
        private PartyTransitionKey failureKey;
        private bool failureReported;
        private string trustedSessionId;

        public bool IsOperationInFlight => operationInFlight;
        public PartyTransitionKey AppliedTransitionKey => hasAppliedView ? appliedKey : default;
        public IPartyGameScenePort BoundAdapter => boundAdapter;

        public void Configure(
            IPartySceneLoader sceneLoader,
            IPartyGameSceneResolver sceneResolver,
            PartyLobbyScenePort lobbyPort,
            IPartySceneCoordinatorCallbacks transitionCallbacks,
            IPartySceneOperationRunner operationRunner = null,
            bool disposeLoader = true)
        {
            if (configured) throw new InvalidOperationException("PartySceneCoordinator is already configured.");
            loader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
            resolver = sceneResolver ?? throw new ArgumentNullException(nameof(sceneResolver));
            lobby = lobbyPort ?? throw new ArgumentNullException(nameof(lobbyPort));
            callbacks = transitionCallbacks ?? throw new ArgumentNullException(nameof(transitionCallbacks));
            if (!lobby.ValidateBindings(out string error)) throw new ArgumentException(error, nameof(lobbyPort));
            runner = operationRunner ?? new CoroutineOperationRunner(this);
            ownsLoader = disposeLoader;
            configured = true;
        }

        public void ApplyView(OnlineRelayQuizView view)
        {
            if (disposed) return;
            if (!configured) throw new InvalidOperationException("PartySceneCoordinator must be configured before applying a view.");
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (!view.connected) return;
            if (!PartyTransitionKey.TryCreate(view.sessionId, view.rosterGeneration, view.transitionGeneration, out PartyTransitionKey key)
                || !PartyTransitionPhaseRules.IsDefined(view.transitionPhase))
            {
                ReportFailureOnce(key, view.transitionGeneration, PartySceneLoadFailure.InvalidTransition);
                return;
            }
            if (trustedSessionId == null)
                trustedSessionId = key.SessionId;
            else if (!string.Equals(trustedSessionId, key.SessionId, StringComparison.Ordinal))
                return;

            var transition = new PendingTransition(key, view.serial, view.transitionPhase, view.hasSelectedMode, view.selectedMode);
            if (IsOlderOrDuplicate(transition)) return;

            if (operationInFlight)
            {
                if (!hasPending || IsNewer(transition, pending))
                {
                    pending = transition;
                    hasPending = true;
                }
                return;
            }

            Apply(transition);
        }

        public void ResetForSession(string sessionId)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PartySceneCoordinator));
            if (!configured) throw new InvalidOperationException("PartySceneCoordinator must be configured before resetting its session.");
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A trusted session id is required.", nameof(sessionId));
            if (operationInFlight || loadedScene != null || boundAdapter != null)
                throw new InvalidOperationException("The coordinator must be idle in the lobby before resetting its trusted session.");

            trustedSessionId = sessionId;
            hasAppliedView = false;
            appliedKey = default;
            appliedPhase = default;
            appliedSerial = 0;
            hasPending = false;
            pending = default;
            failureReported = false;
            failureKey = default;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            hasPending = false;
            if (boundAdapter != null)
            {
                callbacks.DisableGameSceneInteractions(boundAdapter);
                callbacks.UnbindGameScene(boundAdapter);
                boundAdapter.Unregister();
                boundAdapter = null;
            }
            if (ownsLoader) loader?.Dispose();
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private void Apply(PendingTransition transition)
        {
            Remember(transition);
            switch (transition.Phase)
            {
                case PartyTransitionPhase.LoadingGame:
                    BeginLoad(transition);
                    break;
                case PartyTransitionPhase.ReturningToLobby:
                    BeginReturn(transition);
                    break;
            }
        }

        private void BeginLoad(PendingTransition transition)
        {
            if (!transition.HasSelectedMode || !PartySceneCatalog.TryGet(transition.Mode, out PartySceneDefinition target))
            {
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration, PartySceneLoadFailure.InvalidScenePath);
                return;
            }
            if (loadedScene != null)
            {
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration, PartySceneLoadFailure.InvalidTransition);
                return;
            }

            PartySceneLoadResult result = loader.LoadAdditive(target);
            if (!result.IsSuccess || result.Operation == null)
            {
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration,
                    result.IsSuccess ? PartySceneLoadFailure.LoadFailed : result.Failure);
                return;
            }

            operationInFlight = true;
            runner.Run(result.Operation, () => CompleteLoad(transition, target));
        }

        private void CompleteLoad(PendingTransition transition, PartySceneDefinition target)
        {
            if (disposed) return;
            operationInFlight = false;
            loadedScene = target;

            if (TryConsumePending(out PendingTransition next) && next.Phase == PartyTransitionPhase.ReturningToLobby)
            {
                Apply(next);
                return;
            }

            IReadOnlyList<IPartyGameScenePort> adapters = resolver.Resolve(target.ScenePath);
            if (adapters == null || adapters.Count != 1)
            {
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration, PartySceneLoadFailure.MissingAdapter);
                ApplyPending();
                return;
            }

            IPartyGameScenePort adapter = adapters[0];
            if (adapter == null || adapter.Mode != transition.Mode)
            {
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration, PartySceneLoadFailure.ModeMismatch);
                ApplyPending();
                return;
            }
            if (!adapter.Register(transition.Mode, transition.Key, out _))
            {
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration, PartySceneLoadFailure.MissingAdapter);
                ApplyPending();
                return;
            }

            try
            {
                callbacks.BindGameScene(adapter);
                boundAdapter = adapter;
            }
            catch
            {
                adapter.Unregister();
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration, PartySceneLoadFailure.MissingAdapter);
                ApplyPending();
                return;
            }

            PartySceneLoadResult activation = loader.SetActive(target);
            if (!activation.IsSuccess)
            {
                UnbindCurrentAdapter();
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration, activation.Failure);
                ApplyPending();
                return;
            }

            lobby.SetLobbyVisible(false);
            callbacks.RebaseToGame(adapter);
            callbacks.MarkLocalSceneReady(transition.Key.TransitionGeneration);
            ApplyPending();
        }

        private void BeginReturn(PendingTransition transition)
        {
            UnbindCurrentAdapter();
            if (loadedScene == null)
            {
                CompleteReturn(transition);
                return;
            }

            PartySceneDefinition target = loadedScene;
            PartySceneLoadResult result = loader.Unload(target);
            if (!result.IsSuccess || result.Operation == null)
            {
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration,
                    result.IsSuccess ? PartySceneLoadFailure.UnloadFailed : result.Failure);
                return;
            }

            operationInFlight = true;
            runner.Run(result.Operation, () =>
            {
                if (disposed) return;
                operationInFlight = false;
                loadedScene = null;
                CompleteReturn(transition);
                ApplyPending();
            });
        }

        private void CompleteReturn(PendingTransition transition)
        {
            if (!callbacks.ActivateLobbyScene())
            {
                ReportFailureOnce(transition.Key, transition.Key.TransitionGeneration, PartySceneLoadFailure.ActivationFailed);
                return;
            }
            lobby.SetLobbyVisible(true);
            callbacks.RebaseToLobby(lobby);
            callbacks.MarkLocalLobbyReady(transition.Key.TransitionGeneration);
        }

        private void UnbindCurrentAdapter()
        {
            if (boundAdapter == null) return;
            IPartyGameScenePort adapter = boundAdapter;
            boundAdapter = null;
            callbacks.DisableGameSceneInteractions(adapter);
            callbacks.UnbindGameScene(adapter);
            adapter.Unregister();
        }

        private void ApplyPending()
        {
            if (!operationInFlight && TryConsumePending(out PendingTransition next)) Apply(next);
        }

        private bool TryConsumePending(out PendingTransition value)
        {
            if (!hasPending)
            {
                value = default;
                return false;
            }
            value = pending;
            hasPending = false;
            return true;
        }

        private bool IsOlderOrDuplicate(PendingTransition transition)
        {
            if (!hasAppliedView) return false;
            if (!string.Equals(transition.Key.SessionId, appliedKey.SessionId, StringComparison.Ordinal)) return false;
            if (transition.Key.RosterGeneration < appliedKey.RosterGeneration) return true;
            if (transition.Key.RosterGeneration > appliedKey.RosterGeneration) return false;
            if (transition.Key.TransitionGeneration < appliedKey.TransitionGeneration) return true;
            if (transition.Key.TransitionGeneration > appliedKey.TransitionGeneration) return false;
            return transition.Serial <= appliedSerial || transition.Phase == appliedPhase;
        }

        private static bool IsNewer(PendingTransition left, PendingTransition right)
        {
            if (!string.Equals(left.Key.SessionId, right.Key.SessionId, StringComparison.Ordinal)) return true;
            if (left.Key.RosterGeneration != right.Key.RosterGeneration)
                return left.Key.RosterGeneration > right.Key.RosterGeneration;
            if (left.Key.TransitionGeneration != right.Key.TransitionGeneration)
                return left.Key.TransitionGeneration > right.Key.TransitionGeneration;
            return left.Serial > right.Serial;
        }

        private void Remember(PendingTransition transition)
        {
            if (!hasAppliedView || transition.Key != appliedKey)
            {
                failureReported = false;
                failureKey = transition.Key;
            }
            hasAppliedView = true;
            appliedKey = transition.Key;
            appliedPhase = transition.Phase;
            appliedSerial = transition.Serial;
        }

        private void ReportFailureOnce(PartyTransitionKey key, int generation, PartySceneLoadFailure failure)
        {
            if (failure <= PartySceneLoadFailure.None) return;
            if (failureReported && key == failureKey) return;
            failureKey = key;
            failureReported = true;
            callbacks.ReportSceneLoadFailure(generation, failure);
        }

        private readonly struct PendingTransition
        {
            internal PendingTransition(PartyTransitionKey key, int serial, PartyTransitionPhase phase, bool hasSelectedMode, PartyMode mode)
            {
                Key = key;
                Serial = serial;
                Phase = phase;
                HasSelectedMode = hasSelectedMode;
                Mode = mode;
            }

            internal PartyTransitionKey Key { get; }
            internal int Serial { get; }
            internal PartyTransitionPhase Phase { get; }
            internal bool HasSelectedMode { get; }
            internal PartyMode Mode { get; }
        }

        private sealed class CoroutineOperationRunner : IPartySceneOperationRunner
        {
            private readonly PartySceneCoordinator owner;
            internal CoroutineOperationRunner(PartySceneCoordinator owner) => this.owner = owner;
            public void Run(IPartySceneLoadOperation operation, Action completed)
            {
                owner.StartCoroutine(WaitFor(operation, completed));
            }

            private static IEnumerator WaitFor(IPartySceneLoadOperation operation, Action completed)
            {
                while (!operation.IsDone) yield return null;
                completed();
            }
        }
    }
}
