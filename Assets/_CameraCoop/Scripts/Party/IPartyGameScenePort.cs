namespace CameraCoop.Party
{
    public interface IPartyGameScenePort
    {
        PartyMode Mode { get; }
        PartySceneBindings Bindings { get; }
        bool IsRegistered { get; }
        bool ValidateBindings(out string error);
        bool Register(PartyMode expectedMode, PartyTransitionKey transitionKey, out string error);
        void Unregister();
    }
}
