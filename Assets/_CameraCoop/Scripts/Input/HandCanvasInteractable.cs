using System.Collections.Generic;
using UnityEngine;

namespace CameraCoop
{
    public sealed class HandCanvasInteractable : HandInteractable
    {
        [SerializeField] private CanvasSurface canvasSurface;
        [SerializeField] private HandPointer handPointer;
        private readonly HashSet<string> drawingHands = new HashSet<string>();

        public CanvasSurface Surface => canvasSurface;
        public HandPointer Pointer => handPointer;
        public override string DisplayName => "작업 캔버스";
        public override bool IsCanvas => true;
        public override bool IsAvailable => base.IsAvailable && canvasSurface != null && canvasSurface.isActiveAndEnabled && handPointer != null && handPointer.isActiveAndEnabled;

        private void Awake()
        {
            if (canvasSurface == null || handPointer == null || handPointer.InputSource != HandPointerInputSource.HandRouter)
            {
                Debug.LogError("HandCanvasInteractable: assign canvasSurface and a HandRouter handPointer.", this);
                enabled = false;
            }
        }

        public override void Press(HandInputSample sample, Vector3 hitPosition, HandClickContext context)
        {
            if (!IsAvailable || !handPointer.CanUseCanvas(canvasSurface) || !drawingHands.Add(sample.handedness)) return;
            handPointer.BeginCanvasStroke(sample.handedness, canvasSurface, canvasSurface.WorldToNorm(hitPosition));
        }

        public override void Hold(HandInputSample sample, Vector3 hitPosition)
        {
            if (!drawingHands.Contains(sample.handedness)) return;
            if (!IsAvailable)
            {
                End(sample.handedness);
                return;
            }
            handPointer.MoveCanvasStroke(sample.handedness, canvasSurface, canvasSurface.WorldToNorm(hitPosition));
        }

        public override bool Release(HandInputSample sample, Vector3 hitPosition)
        {
            End(sample.handedness);
            return false;
        }

        public override void Cancel(HandInputSample sample, Vector3 hitPosition) => End(sample.handedness);

        protected override void OnDisable()
        {
            base.OnDisable();
            var hands = new List<string>(drawingHands);
            foreach (string hand in hands) End(hand);
        }

        private void End(string hand)
        {
            if (!drawingHands.Remove(hand)) return;
            if (handPointer != null) handPointer.EndCanvasStroke(hand);
        }
    }
}
