using UnityEngine;
using UnityEngine.UI;

namespace CameraCoop
{
    public sealed class HandDrawingWorkspace : MonoBehaviour
    {
        [SerializeField] private HandInputRouter handInputRouter;
        [SerializeField] private DrawingController drawingController;
        [SerializeField] private CanvasDrawingPresenter savedPresenter;
        [SerializeField] private CanvasSurface previewSurface;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private RectTransform previewViewport;
        [SerializeField] private HandButtonInteractable undoButton;
        [SerializeField] private HandButtonInteractable clearButton;
        [SerializeField] private HandButtonInteractable saveButton;
        [SerializeField] private HandButtonInteractable loadButton;
        [SerializeField] private HandButtonInteractable previewButton;
        [SerializeField] private Text statusLabel;
        [SerializeField] private Text previewButtonLabel;

        private CanvasDrawingData savedDrawing;
        private bool initialized;
        private bool previewVisible;
        private readonly Vector3[] previewCorners = new Vector3[4];

        private void Awake()
        {
            if (handInputRouter == null || drawingController == null || savedPresenter == null
                || previewSurface == null || previewCamera == null || previewViewport == null
                || undoButton == null || clearButton == null
                || saveButton == null || loadButton == null || previewButton == null
                || statusLabel == null || previewButtonLabel == null)
            {
                Debug.LogError("[HandDrawingWorkspace] 캔버스·손 버튼·프리뷰·상태 참조를 모두 할당하세요.", this);
                enabled = false;
                return;
            }
            initialized = true;
            statusLabel.text = "캔버스 위에서 핀치를 유지해 그려보세요";
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                return;
            }
            undoButton.OnHandClick += Undo;
            clearButton.OnHandClick += Clear;
            saveButton.OnHandClick += SaveDrawing;
            loadButton.OnHandClick += RestoreDrawing;
            previewButton.OnHandClick += TogglePreview;
            HidePreview();
        }

        private void OnDisable()
        {
            if (undoButton != null) undoButton.OnHandClick -= Undo;
            if (clearButton != null) clearButton.OnHandClick -= Clear;
            if (saveButton != null) saveButton.OnHandClick -= SaveDrawing;
            if (loadButton != null) loadButton.OnHandClick -= RestoreDrawing;
            if (previewButton != null) previewButton.OnHandClick -= TogglePreview;
            if (initialized)
            {
                EndCanvasInput();
                HidePreview();
            }
        }

        private void EndCanvasInput()
        {
            if (handInputRouter != null)
            {
                handInputRouter.CancelCanvasCaptures(HandCancelReason.DrawingCommand);
            }
            if (drawingController != null)
            {
                drawingController.FinalizeActiveStrokes();
            }
        }

        private bool CanExecute => initialized && isActiveAndEnabled;

        private void LateUpdate()
        {
            if (CanExecute && UpdatePreviewLayout() && previewVisible && savedDrawing != null)
            {
                savedPresenter.Show(savedDrawing, previewSurface);
            }
        }

        private bool UpdatePreviewLayout()
        {
            Transform surface = previewSurface.transform;
            float depth = Vector3.Dot(surface.position - previewCamera.transform.position, previewCamera.transform.forward);
            if (depth <= 0f) return false;
            previewViewport.GetWorldCorners(previewCorners);
            Vector3 lowerLeft = previewCamera.ScreenToWorldPoint(new Vector3(previewCorners[0].x, previewCorners[0].y, depth));
            Vector3 upperRight = previewCamera.ScreenToWorldPoint(new Vector3(previewCorners[2].x, previewCorners[2].y, depth));
            float width = Vector3.Distance(lowerLeft, previewCamera.ScreenToWorldPoint(new Vector3(previewCorners[3].x, previewCorners[3].y, depth)));
            float height = Vector3.Distance(lowerLeft, previewCamera.ScreenToWorldPoint(new Vector3(previewCorners[1].x, previewCorners[1].y, depth)));
            float currentWidth = surface.TransformVector(Vector3.right).magnitude;
            float currentHeight = surface.TransformVector(Vector3.up).magnitude;
            if (width <= 0f || height <= 0f || currentWidth <= 0f || currentHeight <= 0f) return false;
            Vector3 scale = surface.localScale;
            scale.x *= width / currentWidth;
            scale.y *= height / currentHeight;
            Vector3 position = (lowerLeft + upperRight) * .5f;
            Quaternion rotation = previewCamera.transform.rotation;
            if ((surface.position - position).sqrMagnitude < .00000001f
                && (surface.localScale - scale).sqrMagnitude < .00000001f
                && Quaternion.Angle(surface.rotation, rotation) < .001f) return false;
            surface.SetPositionAndRotation(position, rotation);
            surface.localScale = scale;
            return true;
        }

        private void Undo(HandClickContext context)
        {
            if (!CanExecute) return;
            EndCanvasInput();
            statusLabel.text = drawingController.UndoLastStroke()
                ? "마지막으로 시작한 선을 취소했습니다"
                : "취소할 선이 없습니다";
        }

        private void Clear(HandClickContext context)
        {
            if (!CanExecute) return;
            EndCanvasInput();
            drawingController.ClearAll();
            statusLabel.text = "작업 그림을 지웠습니다 · 보관 그림은 유지됩니다";
        }

        private void SaveDrawing(HandClickContext context)
        {
            if (!CanExecute) return;
            EndCanvasInput();
            CanvasDrawingData drawing = drawingController.ExportDrawing();
            if (drawing == null)
            {
                statusLabel.text = "그림을 보관하지 못했습니다 · 캔버스 연결을 확인하세요";
                return;
            }
            savedDrawing = drawing;
            ShowPreview();
            statusLabel.text = savedDrawing.strokes.Length + "개 선을 보관했습니다 · 작업은 계속할 수 있어요";
        }

        private void RestoreDrawing(HandClickContext context)
        {
            if (!CanExecute) return;
            if (savedDrawing == null)
            {
                statusLabel.text = "먼저 그림 보관 버튼을 눌러주세요";
                return;
            }
            EndCanvasInput();
            statusLabel.text = drawingController.LoadDrawing(savedDrawing)
                ? "보관한 그림을 작업 캔버스에 복원했습니다"
                : "그림을 복원하지 못했습니다 · 보관 데이터는 유지됩니다";
        }

        private void TogglePreview(HandClickContext context)
        {
            if (!CanExecute) return;
            if (savedDrawing == null)
            {
                statusLabel.text = "그림을 보관하면 오른쪽에서 미리 볼 수 있어요";
                return;
            }
            if (previewVisible)
            {
                HidePreview();
                statusLabel.text = "프리뷰만 숨겼습니다 · 보관 그림은 유지됩니다";
            }
            else
            {
                ShowPreview();
                statusLabel.text = "보관한 그림을 표시합니다 · 프리뷰에는 그릴 수 없어요";
            }
        }

        private void ShowPreview()
        {
            UpdatePreviewLayout();
            previewSurface.gameObject.SetActive(true);
            savedPresenter.Show(savedDrawing, previewSurface);
            previewVisible = true;
            previewButtonLabel.text = "프리뷰 숨김";
        }

        private void HidePreview()
        {
            if (savedPresenter != null) savedPresenter.Hide();
            if (previewSurface != null) previewSurface.gameObject.SetActive(false);
            previewVisible = false;
            if (previewButtonLabel != null) previewButtonLabel.text = "프리뷰 보기";
        }
    }
}
