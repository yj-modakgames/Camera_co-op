using System;
using CameraCoop.Party;
using CameraCoop.Party.SceneFlow;
using NUnit.Framework;
using UnityEngine.SceneManagement;

namespace CameraCoop.Tests
{
    public sealed class PartySceneLoaderTests
    {
        [Test]
        public void LoadAdditive_CompletedOperationCanSetTargetActive()
        {
            var runtime = new FakeSceneRuntime();
            using (var loader = new UnityPartySceneLoader(runtime))
            {
                PartySceneDefinition target = Target(PartyMode.RelayCopy);

                PartySceneLoadResult load = loader.LoadAdditive(target);

                Assert.That(load.IsSuccess, Is.True);
                Assert.That(runtime.LoadPath, Is.EqualTo(target.ScenePath));
                Assert.That(runtime.LoadMode, Is.EqualTo(LoadSceneMode.Additive));
                Assert.That(runtime.UnloadCalls, Is.Zero);

                runtime.LastOperation.Complete();
                PartySceneLoadResult activation = loader.SetActive(target);

                Assert.That(activation.IsSuccess, Is.True);
                Assert.That(runtime.ActivePath, Is.EqualTo(target.ScenePath));
            }
        }

        [Test]
        public void LoadAdditive_InvalidPathReturnsFailureWithoutRuntimeLoad()
        {
            var runtime = new FakeSceneRuntime();
            using (var loader = new UnityPartySceneLoader(runtime))
            {
                var invalid = new PartySceneDefinition(
                    PartyMode.RelayCopy,
                    "RelayCopy",
                    "Assets/_CameraCoop/Scenes/UnregisteredRelayCopy.unity");

                PartySceneLoadResult result = loader.LoadAdditive(invalid);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Failure, Is.EqualTo(PartySceneLoadFailure.InvalidScenePath));
                Assert.That(runtime.LoadCalls, Is.Zero);
            }
        }

        [Test]
        public void SetActive_RuntimeFailureReturnsExplicitActivationFailure()
        {
            var runtime = new FakeSceneRuntime { SetActiveSucceeds = false };
            using (var loader = new UnityPartySceneLoader(runtime))
            {
                PartySceneDefinition target = Target(PartyMode.MemoryCopy);
                runtime.MarkLoaded(target.ScenePath);

                PartySceneLoadResult result = loader.SetActive(target);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Failure, Is.EqualTo(PartySceneLoadFailure.ActivationFailed));
                Assert.That(runtime.ActiveCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public void LoadAdditive_NullRuntimeOperationReturnsExplicitLoadFailure()
        {
            var runtime = new FakeSceneRuntime { ReturnNullLoadOperation = true };
            using (var loader = new UnityPartySceneLoader(runtime))
            {
                PartySceneLoadResult result = loader.LoadAdditive(Target(PartyMode.RelayCopy));

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Failure, Is.EqualTo(PartySceneLoadFailure.LoadFailed));
                Assert.That(result.Message, Is.EqualTo("Scene load operation was unavailable"));
            }
        }

        [Test]
        public void Unload_CompletedOperationUnloadsOnlyGameScene()
        {
            var runtime = new FakeSceneRuntime();
            using (var loader = new UnityPartySceneLoader(runtime))
            {
                PartySceneDefinition target = Target(PartyMode.CoopMural);
                runtime.MarkLoaded(target.ScenePath);

                PartySceneLoadResult unload = loader.Unload(target);

                Assert.That(unload.IsSuccess, Is.True);
                Assert.That(runtime.UnloadPath, Is.EqualTo(target.ScenePath));
                Assert.That(runtime.UnloadCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public void Unload_TargetThatIsNotLoadedReturnsFailureWithoutRuntimeUnload()
        {
            var runtime = new FakeSceneRuntime();
            using (var loader = new UnityPartySceneLoader(runtime))
            {
                PartySceneLoadResult result = loader.Unload(Target(PartyMode.MemoryCopy));

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Failure, Is.EqualTo(PartySceneLoadFailure.UnloadFailed));
                Assert.That(runtime.UnloadCalls, Is.Zero);
            }
        }

        [Test]
        public void Unload_BootstrapSceneIsRefusedWithoutRuntimeCall()
        {
            var runtime = new FakeSceneRuntime();
            using (var loader = new UnityPartySceneLoader(runtime))
            {
                var lobby = new PartySceneDefinition(default, PartySceneCatalog.LobbySceneName, PartySceneCatalog.LobbyScenePath);

                PartySceneLoadResult result = loader.Unload(lobby);

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Failure, Is.EqualTo(PartySceneLoadFailure.InvalidTransition));
                Assert.That(result.Message, Is.EqualTo("Bootstrap Scene cannot be unloaded"));
                Assert.That(runtime.UnloadCalls, Is.Zero);
            }
        }

        [Test]
        public void Loader_RejectsSecondOperationUntilFirstOperationCompletes()
        {
            var runtime = new FakeSceneRuntime();
            using (var loader = new UnityPartySceneLoader(runtime))
            {
                PartySceneDefinition target = Target(PartyMode.RelayCopy);
                PartySceneLoadResult first = loader.LoadAdditive(target);
                PartySceneLoadResult second = loader.Unload(target);

                Assert.That(first.IsSuccess, Is.True);
                Assert.That(second.IsSuccess, Is.False);
                Assert.That(second.Failure, Is.EqualTo(PartySceneLoadFailure.InvalidTransition));
                Assert.That(runtime.UnloadCalls, Is.Zero);

                runtime.LastOperation.Complete();
                PartySceneLoadResult retry = loader.Unload(target);

                Assert.That(retry.IsSuccess, Is.True);
                Assert.That(runtime.UnloadCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public void Dispose_UnsubscribesFromSceneEvents()
        {
            var runtime = new FakeSceneRuntime();
            var loader = new UnityPartySceneLoader(runtime);

            Assert.That(runtime.SceneLoadedSubscriberCount, Is.EqualTo(1));

            loader.Dispose();

            Assert.That(runtime.SceneLoadedSubscriberCount, Is.Zero);
        }

        private static PartySceneDefinition Target(PartyMode mode)
        {
            Assert.That(PartySceneCatalog.TryGet(mode, out PartySceneDefinition target), Is.True);
            return target;
        }

        private sealed class FakeSceneRuntime : IPartySceneRuntime
        {
            internal int LoadCalls { get; private set; }
            internal int UnloadCalls { get; private set; }
            internal int ActiveCalls { get; private set; }
            internal int SceneLoadedSubscriberCount { get; private set; }
            internal string LoadPath { get; private set; }
            internal string UnloadPath { get; private set; }
            internal string ActivePath { get; private set; }
            internal LoadSceneMode LoadMode { get; private set; }
            internal bool SetActiveSucceeds { get; set; } = true;
            internal bool ReturnNullLoadOperation { get; set; }
            internal FakeOperation LastOperation { get; private set; }

            public event Action<string, LoadSceneMode> SceneLoaded
            {
                add
                {
                    SceneLoadedSubscriberCount++;
                }
                remove
                {
                    SceneLoadedSubscriberCount--;
                }
            }

            public IPartySceneLoadOperation LoadSceneAsync(string scenePath, LoadSceneMode mode)
            {
                LoadCalls++;
                LoadPath = scenePath;
                LoadMode = mode;
                if (ReturnNullLoadOperation) return null;
                LastOperation = new FakeOperation();
                return LastOperation;
            }

            public IPartySceneLoadOperation UnloadSceneAsync(string scenePath)
            {
                UnloadCalls++;
                UnloadPath = scenePath;
                LastOperation = new FakeOperation();
                return LastOperation;
            }

            public bool IsLoaded(string scenePath)
            {
                return string.Equals(LoadPath, scenePath, StringComparison.Ordinal)
                    || string.Equals(UnloadPath, scenePath, StringComparison.Ordinal);
            }

            public bool SetActiveScene(string scenePath)
            {
                ActiveCalls++;
                ActivePath = scenePath;
                return SetActiveSucceeds;
            }

            internal void MarkLoaded(string scenePath)
            {
                LoadPath = scenePath;
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeOperation : IPartySceneLoadOperation
        {
            public bool IsDone { get; private set; }

            internal void Complete()
            {
                IsDone = true;
            }
        }
    }
}
