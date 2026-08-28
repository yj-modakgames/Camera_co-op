using System;
using UnityEngine;

namespace CameraCoop.Tests
{
    public sealed class HandInteractionProbe : HandInteractable
    {
        public bool available = true;
        public bool canvas;
        public int enters;
        public int exits;
        public int presses;
        public int holds;
        public int releases;
        public int cancels;
        public HandCancelReason lastCancelReason;
        public HandClickContext lastPress;
        public Action released;

        public override bool IsAvailable => available && base.IsAvailable;
        public override bool IsCanvas => canvas;
        public override void HoverEnter(HandInputSample sample, Vector3 hitPosition) { enters++; }
        public override void HoverExit(HandInputSample sample, Vector3 hitPosition) { exits++; }
        public override void Press(HandInputSample sample, Vector3 hitPosition, HandClickContext context)
        {
            presses++;
            lastPress = context;
        }
        public override void Hold(HandInputSample sample, Vector3 hitPosition) { holds++; }
        public override bool Release(HandInputSample sample, Vector3 hitPosition)
        {
            releases++;
            released?.Invoke();
            return true;
        }
        public override void Cancel(HandInputSample sample, Vector3 hitPosition)
        {
            cancels++;
            lastCancelReason = sample.cancelReason;
        }
    }
}
