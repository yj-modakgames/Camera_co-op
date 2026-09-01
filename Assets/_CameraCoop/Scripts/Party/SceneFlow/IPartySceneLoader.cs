using System;
using UnityEngine.SceneManagement;

namespace CameraCoop.Party.SceneFlow
{
    public interface IPartySceneLoadOperation
    {
        bool IsDone { get; }
    }

    public interface IPartySceneRuntime : IDisposable
    {
        event Action<string, LoadSceneMode> SceneLoaded;

        IPartySceneLoadOperation LoadSceneAsync(string scenePath, LoadSceneMode mode);
        IPartySceneLoadOperation UnloadSceneAsync(string scenePath);
        bool IsLoaded(string scenePath);
        bool SetActiveScene(string scenePath);
    }

    public interface IPartySceneLoader : IDisposable
    {
        bool IsOperationInFlight { get; }

        PartySceneLoadResult LoadAdditive(PartySceneDefinition target);
        PartySceneLoadResult SetActive(PartySceneDefinition target);
        PartySceneLoadResult Unload(PartySceneDefinition target);
    }

    public readonly struct PartySceneLoadResult
    {
        private PartySceneLoadResult(
            bool isSuccess,
            PartySceneLoadFailure failure,
            string message,
            IPartySceneLoadOperation operation)
        {
            IsSuccess = isSuccess;
            Failure = failure;
            Message = message;
            Operation = operation;
        }

        public bool IsSuccess { get; }
        public PartySceneLoadFailure Failure { get; }
        public string Message { get; }
        public IPartySceneLoadOperation Operation { get; }

        public static PartySceneLoadResult Success(IPartySceneLoadOperation operation = null)
        {
            return new PartySceneLoadResult(true, PartySceneLoadFailure.None, string.Empty, operation);
        }

        public static PartySceneLoadResult Failed(PartySceneLoadFailure failure, string message)
        {
            return new PartySceneLoadResult(false, failure, message ?? string.Empty, null);
        }
    }
}
