using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CameraCoop.Party.SceneFlow
{
    public sealed class UnityPartySceneLoader : IPartySceneLoader
    {
        private readonly IPartySceneRuntime runtime;
        private IPartySceneLoadOperation inFlight;
        private bool disposed;

        public UnityPartySceneLoader()
            : this(new UnitySceneRuntime())
        {
        }

        public UnityPartySceneLoader(IPartySceneRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            runtime.SceneLoaded += HandleSceneLoaded;
        }

        public event Action<string> SceneLoaded;

        public bool IsOperationInFlight
        {
            get
            {
                ClearCompletedOperation();
                return inFlight != null;
            }
        }

        public PartySceneLoadResult LoadAdditive(PartySceneDefinition target)
        {
            if (!TryValidateGameScene(target, out PartySceneLoadResult failure)) return failure;
            if (!TryReserveOperation(out failure)) return failure;

            try
            {
                IPartySceneLoadOperation operation = runtime.LoadSceneAsync(target.ScenePath, LoadSceneMode.Additive);
                if (operation == null) return PartySceneLoadResult.Failed(PartySceneLoadFailure.LoadFailed, "Scene load operation was unavailable");
                inFlight = operation;
                return PartySceneLoadResult.Success(operation);
            }
            catch (Exception exception)
            {
                return PartySceneLoadResult.Failed(PartySceneLoadFailure.LoadFailed, exception.Message);
            }
        }

        public PartySceneLoadResult SetActive(PartySceneDefinition target)
        {
            if (!TryValidateGameScene(target, out PartySceneLoadResult failure)) return failure;
            if (!TryReserveOperation(out failure)) return failure;
            if (!runtime.IsLoaded(target.ScenePath))
                return PartySceneLoadResult.Failed(PartySceneLoadFailure.ActivationFailed, "Target Scene is not loaded");

            try
            {
                return runtime.SetActiveScene(target.ScenePath)
                    ? PartySceneLoadResult.Success()
                    : PartySceneLoadResult.Failed(PartySceneLoadFailure.ActivationFailed, "Target Scene could not be activated");
            }
            catch (Exception exception)
            {
                return PartySceneLoadResult.Failed(PartySceneLoadFailure.ActivationFailed, exception.Message);
            }
        }

        public PartySceneLoadResult Unload(PartySceneDefinition target)
        {
            if (target != null && string.Equals(target.ScenePath, PartySceneCatalog.LobbyScenePath, StringComparison.Ordinal))
                return PartySceneLoadResult.Failed(PartySceneLoadFailure.InvalidTransition, "Bootstrap Scene cannot be unloaded");
            if (!TryValidateGameScene(target, out PartySceneLoadResult failure)) return failure;
            if (!TryReserveOperation(out failure)) return failure;
            if (!runtime.IsLoaded(target.ScenePath))
                return PartySceneLoadResult.Failed(PartySceneLoadFailure.UnloadFailed, "Target Scene is not loaded");

            try
            {
                IPartySceneLoadOperation operation = runtime.UnloadSceneAsync(target.ScenePath);
                if (operation == null) return PartySceneLoadResult.Failed(PartySceneLoadFailure.UnloadFailed, "Scene unload operation was unavailable");
                inFlight = operation;
                return PartySceneLoadResult.Success(operation);
            }
            catch (Exception exception)
            {
                return PartySceneLoadResult.Failed(PartySceneLoadFailure.UnloadFailed, exception.Message);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            runtime.SceneLoaded -= HandleSceneLoaded;
            runtime.Dispose();
        }

        private bool TryValidateGameScene(PartySceneDefinition target, out PartySceneLoadResult failure)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.ScenePath)
                || !PartySceneCatalog.TryGet(target.Mode, out PartySceneDefinition catalogTarget)
                || !string.Equals(target.SceneName, catalogTarget.SceneName, StringComparison.Ordinal)
                || !string.Equals(target.ScenePath, catalogTarget.ScenePath, StringComparison.Ordinal))
            {
                failure = PartySceneLoadResult.Failed(PartySceneLoadFailure.InvalidScenePath, "A full catalog game Scene path is required");
                return false;
            }

            failure = default;
            return true;
        }

        private bool TryReserveOperation(out PartySceneLoadResult failure)
        {
            ClearCompletedOperation();
            if (inFlight != null)
            {
                failure = PartySceneLoadResult.Failed(PartySceneLoadFailure.InvalidTransition, "A Scene operation is already in progress");
                return false;
            }

            failure = default;
            return true;
        }

        private void ClearCompletedOperation()
        {
            if (inFlight != null && inFlight.IsDone) inFlight = null;
        }

        private void HandleSceneLoaded(string scenePath, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive) SceneLoaded?.Invoke(scenePath);
        }

        private sealed class UnitySceneRuntime : IPartySceneRuntime
        {
            public UnitySceneRuntime()
            {
                SceneManager.sceneLoaded += HandleSceneLoaded;
            }

            public event Action<string, LoadSceneMode> SceneLoaded;

            public IPartySceneLoadOperation LoadSceneAsync(string scenePath, LoadSceneMode mode)
            {
#if UNITY_EDITOR
                // EditMode에서는 runtime SceneManager가 씬을 열지 못하고 null을 돌려준다.
                // 같은 Scene asset을 Editor API로 동기 로드해 EditMode 테스트가 실제 씬 경계를 지나가게 한다.
                if (!Application.isPlaying)
                {
                    Scene opened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,
                        mode == LoadSceneMode.Additive
                            ? UnityEditor.SceneManagement.OpenSceneMode.Additive
                            : UnityEditor.SceneManagement.OpenSceneMode.Single);
                    if (!opened.IsValid()) return null;
                    SceneLoaded?.Invoke(opened.path, mode);
                    return CompletedSceneOperation.Instance;
                }
#endif
                AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath, mode);
                return operation == null ? null : new UnitySceneLoadOperation(operation);
            }

            public IPartySceneLoadOperation UnloadSceneAsync(string scenePath)
            {
                Scene scene = SceneManager.GetSceneByPath(scenePath);
                if (!scene.IsValid() || !scene.isLoaded) return null;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    // Editor는 활성 씬을 닫지 못한다. runtime UnloadSceneAsync처럼 다른 씬을 먼저 활성화한다.
                    if (SceneManager.GetActiveScene() == scene) ActivateAnyOtherScene(scene);
                    return UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true)
                        ? CompletedSceneOperation.Instance : null;
                }
#endif
                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
                return operation == null ? null : new UnitySceneLoadOperation(operation);
            }

            public bool IsLoaded(string scenePath)
            {
                Scene scene = SceneManager.GetSceneByPath(scenePath);
                return scene.IsValid() && scene.isLoaded;
            }

#if UNITY_EDITOR
            private static void ActivateAnyOtherScene(Scene closing)
            {
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    Scene other = SceneManager.GetSceneAt(index);
                    if (other == closing || !other.IsValid() || !other.isLoaded) continue;
                    SceneManager.SetActiveScene(other);
                    return;
                }
            }
#endif

            public bool SetActiveScene(string scenePath)
            {
                Scene scene = SceneManager.GetSceneByPath(scenePath);
                return scene.IsValid() && scene.isLoaded && SceneManager.SetActiveScene(scene);
            }

            public void Dispose()
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
            }

            private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                SceneLoaded?.Invoke(scene.path, mode);
            }
        }

        private sealed class CompletedSceneOperation : IPartySceneLoadOperation
        {
            public static readonly CompletedSceneOperation Instance = new CompletedSceneOperation();

            public bool IsDone => true;
        }

        private sealed class UnitySceneLoadOperation : IPartySceneLoadOperation
        {
            private readonly AsyncOperation operation;

            public UnitySceneLoadOperation(AsyncOperation operation)
            {
                this.operation = operation;
            }

            public bool IsDone => operation.isDone;
        }
    }
}
