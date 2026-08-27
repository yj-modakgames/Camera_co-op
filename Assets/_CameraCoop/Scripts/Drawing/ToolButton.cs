using UnityEngine;

namespace CameraCoop
{
    // 팔레트 버튼 1개 (docs/11 §2). 데이터만 들고 있고 아무 동작도 하지 않는다 —
    // 클릭 판정은 HandPointer가, 상태 반영은 ToolState가 한다 (참조 단방향, docs/01 §4).
    public enum ToolKind { Color, Width, Brush, Eraser }

    public class ToolButton : MonoBehaviour
    {
        [SerializeField] private ToolKind kind = ToolKind.Color;
        [SerializeField, Min(0)] private int index;

        public ToolKind Kind { get { return kind; } }
        public int Index { get { return index; } }
    }
}
