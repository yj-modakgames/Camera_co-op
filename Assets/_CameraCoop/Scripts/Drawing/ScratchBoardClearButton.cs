using UnityEngine;

namespace CameraCoop
{
    // 낙서판 전용 지우개. WorldActionInteractable은 PartyWorldAction 개수를 씬 검증기가 세므로 쓸 수 없다.
    public sealed class ScratchBoardClearButton : HandInteractable
    {
        [SerializeField] private DrawingController drawingController;

        public override bool UsesWorldHitPosition => true;
        public override bool IsAvailable => base.IsAvailable && drawingController != null;

        public override bool Release(HandInputSample sample, Vector3 hitPosition)
        {
            base.Release(sample, hitPosition);
            if (drawingController == null) return false;
            drawingController.ClearAll();
            return true;
        }
    }
}
