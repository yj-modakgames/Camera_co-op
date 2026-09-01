using System;

namespace CameraCoop.Party
{
    public enum PartyTransitionPhase
    {
        Lobby = 0,
        SelectingMode = 1,
        LoadingGame = 2,
        InGame = 3,
        ReturningToLobby = 4
    }

    public enum PartySceneLoadFailure
    {
        None = 0,
        InvalidScenePath = 1,
        LoadFailed = 2,
        MissingAdapter = 3,
        ModeMismatch = 4,
        ActivationFailed = 5,
        UnloadFailed = 6,
        Timeout = 7,
        InvalidTransition = 8
    }

    public static class PartyTransitionPhaseRules
    {
        public static bool IsDefined(PartyTransitionPhase phase)
        {
            switch (phase)
            {
                case PartyTransitionPhase.Lobby:
                case PartyTransitionPhase.SelectingMode:
                case PartyTransitionPhase.LoadingGame:
                case PartyTransitionPhase.InGame:
                case PartyTransitionPhase.ReturningToLobby:
                    return true;
                default:
                    return false;
            }
        }
    }

    public readonly struct PartyTransitionKey : IEquatable<PartyTransitionKey>
    {
        public PartyTransitionKey(string sessionId, int rosterGeneration, int transitionGeneration)
        {
            SessionId = sessionId;
            RosterGeneration = rosterGeneration;
            TransitionGeneration = transitionGeneration;
        }

        public string SessionId { get; }
        public int RosterGeneration { get; }
        public int TransitionGeneration { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(SessionId)
            && RosterGeneration >= 0
            && TransitionGeneration >= 0;

        public static bool TryCreate(
            string sessionId,
            int rosterGeneration,
            int transitionGeneration,
            out PartyTransitionKey key)
        {
            key = new PartyTransitionKey(sessionId, rosterGeneration, transitionGeneration);
            if (!key.IsValid)
            {
                key = default;
                return false;
            }

            return true;
        }

        public bool Equals(PartyTransitionKey other)
        {
            return string.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
                && RosterGeneration == other.RosterGeneration
                && TransitionGeneration == other.TransitionGeneration;
        }

        public override bool Equals(object obj)
        {
            return obj is PartyTransitionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SessionId == null ? 0 : StringComparer.Ordinal.GetHashCode(SessionId);
                hash = (hash * 397) ^ RosterGeneration;
                return (hash * 397) ^ TransitionGeneration;
            }
        }

        public static bool operator ==(PartyTransitionKey left, PartyTransitionKey right) => left.Equals(right);
        public static bool operator !=(PartyTransitionKey left, PartyTransitionKey right) => !left.Equals(right);
    }
}
