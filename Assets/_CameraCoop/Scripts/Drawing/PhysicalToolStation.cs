using UnityEngine;

namespace CameraCoop
{
    public class PhysicalToolStation : HandInteractable
    {
        public enum StationKind { Paint, Width, Eraser, Rack }
        [SerializeField] private PhysicalPaintTool paintTool;
        [SerializeField] private StationKind kind;
        [SerializeField, Min(0)] private int index;

        public void SetConfiguration(PhysicalPaintTool tool, StationKind stationKind, int stationIndex)
        {
            paintTool = tool;
            kind = stationKind;
            index = stationIndex;
        }

        public bool TryUse(string playerId, Vector3 interactionPosition)
        {
            if (paintTool == null) return false;
            switch (kind)
            {
                case StationKind.Paint: return paintTool.TrySelectPaint(playerId, index, interactionPosition);
                case StationKind.Width: return paintTool.TrySelectWidth(playerId, index, interactionPosition);
                case StationKind.Eraser: return paintTool.TrySelectEraser(playerId, interactionPosition);
                case StationKind.Rack: return paintTool.TryPutDownBrush(playerId, interactionPosition);
                default: return false;
            }
        }

        public override bool IsAvailable => paintTool != null && (kind == StationKind.Rack || paintTool.Location == PhysicalPaintTool.BrushLocation.Held);
        public override bool Exclusive => true;
        public override bool UsesWorldHitPosition => true;

        public override void Press(HandInputSample sample, Vector3 hitPosition, HandClickContext context)
        {
            TryUse(paintTool != null ? paintTool.LocalPlayerId : null, hitPosition);
        }

        public void PressForPlayer(string playerId, Vector3 hitPosition)
        {
            TryUse(playerId, hitPosition);
        }
    }
}
