using UnityEngine;

namespace CameraCoop
{
    // 로비에서 물감통·지우개를 눌러도 선택이 바뀌었는지 볼 방법이 없었다 (사용자 보고 2026-09-04).
    // 색 선택을 보여주던 PalettePanel은 2D UI라 로비에서 꺼져 있으므로, 현재 색을 3D 칩 하나로 되돌려 준다.
    public sealed class ToolStatusIndicator : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private ToolState toolState;
        [SerializeField] private Renderer chip;
        private MaterialPropertyBlock block;

        private void OnEnable()
        {
            if (toolState == null || chip == null)
            {
                Debug.LogError("ToolStatusIndicator: assign toolState and chip.", this);
                enabled = false;
                return;
            }
            toolState.OnChanged += Apply;
            Apply();
        }

        private void OnDisable()
        {
            if (toolState != null) toolState.OnChanged -= Apply;
        }

        private void Apply()
        {
            // 지우개는 색이 없다. 종이색으로 되돌려 "지우는 중"을 보여준다.
            Color color = toolState.CurrentMode == ToolState.Mode.Erase ? Color.white : toolState.CurrentColor;
            if (block == null) block = new MaterialPropertyBlock();
            chip.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            chip.SetPropertyBlock(block);
        }
    }
}
