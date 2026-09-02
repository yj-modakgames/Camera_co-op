using System;
using UnityEngine;

namespace CameraCoop.Party
{
    public enum PartyWorldAction
    {
        Host = 0,
        Invite = 1,
        Leave = 2,
        SelectRelayCopy = 3,
        SelectMemoryCopy = 4,
        SelectCoopMural = 5,
        StartSelectedMode = 6,
        CarryCanvas = 7,
        DockCanvas = 8,
        CameraRefresh = 9,
        CameraPrevious = 10,
        CameraNext = 11,
        CameraPreview = 12,
        ReturnToLobby = 13
    }

    public sealed class WorldActionInteractable : HandInteractable
    {
        [SerializeField] private PartyWorldController partyWorld;
        [SerializeField] private PartyWorldAction action;

        public PartyWorldAction Action => action;
        public override bool UsesWorldHitPosition => true;

        public void Configure(PartyWorldController controller, PartyWorldAction worldAction)
        {
            partyWorld = controller != null ? controller : throw new ArgumentNullException(nameof(controller));
            action = worldAction;
        }

        public override bool IsAvailable => base.IsAvailable && partyWorld != null && partyWorld.CanExecute(action);

        public override bool Release(HandInputSample sample, Vector3 hitPosition)
        {
            return partyWorld != null && partyWorld.TryExecute(action);
        }
    }
}
