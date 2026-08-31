using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop
{
    // N-1개 기록을 작성자 순서로 나란히 보여 주는 읽기 전용 갤러리 (docs/09 §8·§11).
    // 슬롯 surface에는 collider·HandCanvasInteractable을 붙이지 않는다.
    public sealed class RelayQuizGallery : MonoBehaviour
    {
        [SerializeField] private GameObject[] slotRoots;
        [SerializeField] private CanvasDrawingPresenter[] slotPresenters;
        [SerializeField] private CanvasSurface[] slotSurfaces;
        [SerializeField] private Text captionLabel;

        private bool initialized;

        public int SlotCount { get { return slotRoots != null ? slotRoots.Length : 0; } }
        public bool IsReady { get { return initialized; } }

        private void Awake()
        {
            initialized = ValidateRuntimeConfiguration(out _);
            if (!initialized)
            {
                Debug.LogError("[RelayQuizGallery] 슬롯 root·presenter·surface 배열 길이가 같아야 하고 빈 원소가 없어야 합니다.", this);
                return;
            }
            Clear();
        }

        public void Configure(GameObject[] roots, CanvasDrawingPresenter[] presenters, CanvasSurface[] surfaces, Text caption = null)
        {
            slotRoots = roots;
            slotPresenters = presenters;
            slotSurfaces = surfaces;
            captionLabel = caption;
            initialized = ValidateRuntimeConfiguration(out string error);
            if (!initialized) throw new System.ArgumentException(error);
            Clear();
        }

        public bool ValidateRuntimeConfiguration(out string error)
        {
            error = "RelayQuizGallery requires matching non-empty root, presenter and read-only surface arrays.";
            if (slotRoots == null || slotPresenters == null || slotSurfaces == null) return false;
            if (slotRoots.Length == 0) return false;
            if (slotRoots.Length != slotPresenters.Length || slotRoots.Length != slotSurfaces.Length) return false;
            for (int i = 0; i < slotRoots.Length; i++)
            {
                if (slotRoots[i] == null || slotPresenters[i] == null || slotSurfaces[i] == null) return false;
                // 갤러리 표면은 쓰기 대상이 아니다.
                if (slotSurfaces[i].GetComponentInParent<HandCanvasInteractable>() != null) return false;
            }
            error = string.Empty;
            return true;
        }

        public void Show(IReadOnlyList<RelayTurnRecord> records)
        {
            if (!initialized) return;
            int shown = 0;
            for (int i = 0; i < slotRoots.Length; i++)
            {
                bool hasRecord = records != null && i < records.Count && records[i] != null && records[i].drawing != null;
                slotRoots[i].SetActive(hasRecord);
                if (!hasRecord)
                {
                    slotPresenters[i].ClearPresentation();
                    continue;
                }
                slotPresenters[i].Show(records[i].drawing, slotSurfaces[i]);
                shown++;
            }
            UpdateCaption(records, shown);
        }

        public void Clear()
        {
            if (slotRoots == null) return;
            for (int i = 0; i < slotRoots.Length; i++)
            {
                if (slotPresenters != null && i < slotPresenters.Length && slotPresenters[i] != null)
                {
                    slotPresenters[i].ClearPresentation();
                }
                if (slotRoots[i] != null) slotRoots[i].SetActive(false);
            }
            if (captionLabel != null) captionLabel.text = string.Empty;
        }

        private void UpdateCaption(IReadOnlyList<RelayTurnRecord> records, int shown)
        {
            if (captionLabel == null) return;
            if (records == null || shown == 0)
            {
                captionLabel.text = "표시할 그림이 없습니다";
                return;
            }
            var builder = new StringBuilder("왼쪽부터 ");
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) builder.Append(" → ");
                builder.Append("플레이어 ").Append(records[i].playerIndex + 1);
            }
            if (records.Count > shown)
            {
                builder.Append(" (슬롯 부족으로 ").Append(records.Count - shown).Append("개는 표시하지 못했습니다)");
            }
            // 갤러리는 Explore·Move로 들어온다. 손 버튼을 쓰려면 Interact가 필요하다 (docs/06 §3).
            builder.Append("\nWASD로 둘러보고, Tab으로 손 조작으로 바꾼 뒤 다시 시작을 누르세요");
            captionLabel.text = builder.ToString();
        }
    }
}
