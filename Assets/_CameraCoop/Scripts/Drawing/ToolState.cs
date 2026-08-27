using System;
using UnityEngine;

namespace CameraCoop
{
    // 브러시 1종 정의 (docs/11 §2). material은 씬에서 배선 — 미할당이면 DrawingController가 폴백한다.
    [Serializable]
    public class BrushDef
    {
        public string name = "Pen";
        public Material material;
        [Min(0.05f)] public float widthScale = 1f;
        [Range(0f, 1f)] public float alpha = 1f;
    }

    // 현재 선택된 색·두께·브러시·모드를 보유한다 (docs/11 §2). 팔레트 클릭의 유일한 반영 지점.
    public class ToolState : MonoBehaviour
    {
        public enum Mode { Draw, Erase }

        [SerializeField] private Color[] palette = new Color[]
        {
            new Color(0.10f, 0.10f, 0.12f), new Color(0.85f, 0.20f, 0.20f), new Color(1.00f, 0.60f, 0.10f),
            new Color(0.20f, 0.70f, 0.30f), new Color(0.20f, 0.50f, 0.95f), new Color(0.60f, 0.30f, 0.85f)
        };
        [SerializeField] private float[] widths = new float[] { 0.01f, 0.02f, 0.045f };
        [SerializeField] private BrushDef[] brushes = new BrushDef[]
        {
            new BrushDef { name = "Pen", widthScale = 1f, alpha = 1f },
            new BrushDef { name = "Marker", widthScale = 2.2f, alpha = 0.55f },
            new BrushDef { name = "Fine", widthScale = 0.5f, alpha = 1f }
        };
        [SerializeField, Min(0.001f)] private float eraseRadius = 0.05f;
        [SerializeField, Min(0)] private int colorIndex;
        [SerializeField, Min(0)] private int widthIndex = 1;
        [SerializeField, Min(0)] private int brushIndex;
        [SerializeField] private HandPointer handPointer;
        [SerializeField] private ToolButton[] buttons;
        [SerializeField] private float selectedLocalZOffset = -0.02f;

        public event Action OnChanged;

        private Mode mode = Mode.Draw;
        private bool warnedOutOfRange; // 범위 밖 인덱스 경고는 1회만 (로그 폭주 방지)
        private Vector3[] baseButtonPositions;

        public Mode CurrentMode { get { return mode; } }
        public int CurrentBrushIndex { get { return brushIndex; } }
        public float EraseRadius { get { return eraseRadius; } }

        // 팔레트 색에 브러시 알파를 적용한 최종 색
        public Color CurrentColor
        {
            get
            {
                Color color = (palette != null && colorIndex >= 0 && colorIndex < palette.Length) ? palette[colorIndex] : Color.black;
                BrushDef brush = CurrentBrush;
                color.a = brush != null ? brush.alpha : 1f;
                return color;
            }
        }

        // 두께 × 브러시 배율 (월드 단위)
        public float CurrentWidth
        {
            get
            {
                float width = (widths != null && widthIndex >= 0 && widthIndex < widths.Length) ? widths[widthIndex] : 0.02f;
                BrushDef brush = CurrentBrush;
                return width * (brush != null ? brush.widthScale : 1f);
            }
        }

        public Material CurrentMaterial
        {
            get
            {
                BrushDef brush = CurrentBrush;
                return brush != null ? brush.material : null;
            }
        }

        private BrushDef CurrentBrush
        {
            get { return (brushes != null && brushIndex >= 0 && brushIndex < brushes.Length) ? brushes[brushIndex] : null; }
        }

        private void OnEnable()
        {
            if (handPointer != null)
            {
                handPointer.OnToolClicked += Apply;
            }
            RefreshButtonPositions();
        }

        private void OnDisable()
        {
            if (handPointer != null)
            {
                handPointer.OnToolClicked -= Apply;
            }
            RestoreButtonPositions();
        }

        // 버튼 클릭 반영. 실제로 바뀔 때만 OnChanged를 발행한다.
        public void Apply(ToolButton button)
        {
            if (button == null)
            {
                return;
            }
            bool changed = false;
            switch (button.Kind)
            {
                case ToolKind.Color:
                    changed |= SetIndex(ref colorIndex, button.Index, palette != null ? palette.Length : 0, "palette");
                    changed |= SetMode(Mode.Draw); // 색을 고르면 지우개에서 그리기로 돌아온다
                    break;
                case ToolKind.Width:
                    changed |= SetIndex(ref widthIndex, button.Index, widths != null ? widths.Length : 0, "widths");
                    break;
                case ToolKind.Brush:
                    changed |= SetIndex(ref brushIndex, button.Index, brushes != null ? brushes.Length : 0, "brushes");
                    changed |= SetMode(Mode.Draw);
                    break;
                case ToolKind.Eraser:
                    changed |= SetMode(Mode.Erase);
                    break;
            }
            if (changed)
            {
                RefreshButtonPositions();
                OnChanged?.Invoke();
            }
        }

        // 선택 표시는 local z 위치만 바꾼다 — renderer.material 접근 없이 씬 공유 머티리얼을 보존한다 (docs/11 §4).
        private void RefreshButtonPositions()
        {
            if (buttons == null)
            {
                return;
            }
            if (baseButtonPositions == null || baseButtonPositions.Length != buttons.Length)
            {
                baseButtonPositions = new Vector3[buttons.Length];
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i] != null)
                    {
                        baseButtonPositions[i] = buttons[i].transform.localPosition;
                    }
                }
            }
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null)
                {
                    continue;
                }
                Vector3 position = baseButtonPositions[i];
                if (IsSelected(buttons[i]))
                {
                    position.z += selectedLocalZOffset;
                }
                buttons[i].transform.localPosition = position;
            }
        }

        private void RestoreButtonPositions()
        {
            if (buttons == null || baseButtonPositions == null)
            {
                return;
            }
            int count = Mathf.Min(buttons.Length, baseButtonPositions.Length);
            for (int i = 0; i < count; i++)
            {
                if (buttons[i] != null)
                {
                    buttons[i].transform.localPosition = baseButtonPositions[i];
                }
            }
        }

        private bool IsSelected(ToolButton button)
        {
            switch (button.Kind)
            {
                case ToolKind.Color:
                    return button.Index == colorIndex;
                case ToolKind.Width:
                    return button.Index == widthIndex;
                case ToolKind.Brush:
                    return button.Index == brushIndex;
                case ToolKind.Eraser:
                    return mode == Mode.Erase;
                default:
                    return false;
            }
        }

        // 범위 밖 인덱스는 예외 대신 무시 (씬 배선 실수가 세션을 죽이지 않게)
        private bool SetIndex(ref int current, int next, int length, string what)
        {
            if (next < 0 || next >= length)
            {
                if (!warnedOutOfRange)
                {
                    warnedOutOfRange = true;
                    Debug.LogWarning("[ToolState] " + what + " 인덱스 " + next + "가 범위(0.." + (length - 1) + ") 밖 — 무시합니다. ToolButton 배선을 확인하세요 (docs/11 §4)");
                }
                return false;
            }
            if (current == next)
            {
                return false;
            }
            current = next;
            return true;
        }

        private bool SetMode(Mode next)
        {
            if (mode == next)
            {
                return false;
            }
            mode = next;
            return true;
        }
    }
}
