using CameraCoop.Game;
using UnityEngine;

namespace CameraCoop
{
    // 로컬 릴레이 씬 orchestration (docs/09 §5·§7·§11).
    // 프레임 순서: pause 판단 → 유효 손 action → timeout → 화면 적용.
    [DefaultExecutionOrder(200)]
    public sealed class RelayQuizController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputModeManager inputModeManager;
        [SerializeField] private HandInputRouter handInputRouter;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private Transform workPose;
        [SerializeField] private Transform galleryPose;

        [Header("Drawing")]
        [SerializeField] private DrawingController drawingController;
        [SerializeField] private GameObject workCanvasRoot;
        [SerializeField] private CanvasDrawingPresenter previewPresenter;
        [SerializeField] private CanvasSurface previewSurface;

        [Header("Game")]
        [SerializeField] private RelayQuizUI relayQuizUI;
        [SerializeField] private RelayQuizGallery relayQuizGallery;
        [SerializeField] private RelayQuizWordList wordList;
        [SerializeField] private RelayQuizTimings timings = new RelayQuizTimings();
        [SerializeField] private int wordDeckSeed;   // 0이면 실행마다 새 seed를 정한다

        private RelayQuizLogic logic;
        private string deckText;
        private WordBank wordBank;
        private bool ready;
        private bool hasFocus = true;
        private int appliedSerial;
        private int appliedGeneration;
        private RelayQuizPauseStage appliedStage = RelayQuizPauseStage.None;

        internal RelayQuizLogic Logic { get { return logic; } }

        private void Awake()
        {
            string error = ResolveSetupError();
            if (error != null)
            {
                // 제시어를 만들지 않은 채 Setup에 오류만 남긴다. 런타임 Find로 대체하지 않는다.
                Debug.LogError("[RelayQuizController] " + error, this);
                if (relayQuizUI != null) relayQuizUI.ShowSetupError("설정 오류\n" + error);
                enabled = false;
                return;
            }

            // 덱은 씬 실행 동안 유지되고 재시작에도 추출 cursor를 이어간다 (docs/09 §9).
            wordBank = new WordBank(deckText, wordDeckSeed != 0 ? wordDeckSeed : System.Environment.TickCount);
            logic = new RelayQuizLogic(timings, DrawWord, ArchiveCurrentDrawing, ReadConfirmedAnswer);
            ready = true;
            appliedSerial = 0;
            appliedGeneration = 0;
            appliedStage = RelayQuizPauseStage.None;
        }

        private string ResolveSetupError()
        {
            if (inputModeManager == null || handInputRouter == null || playerController == null
                || workPose == null || galleryPose == null)
            {
                return "InputModeManager·HandInputRouter·PlayerController·WorkPose·GalleryPose를 할당하세요.";
            }
            if (drawingController == null || workCanvasRoot == null || previewPresenter == null || previewSurface == null)
            {
                return "DrawingController·작업 캔버스·프리뷰 presenter·프리뷰 surface를 할당하세요.";
            }
            if (relayQuizUI == null || relayQuizGallery == null || wordList == null)
            {
                return "RelayQuizUI·RelayQuizGallery·RelayQuizWordList를 할당하세요.";
            }
            if (timings == null || !timings.IsValid)
            {
                return "네 timer 기본값은 모두 0보다 큰 유한값이어야 합니다.";
            }
            string wordError;
            if (!wordList.TryBuildDeckText(out deckText, out wordError))
            {
                return wordError;
            }
            return null;
        }

        private void Start()
        {
            if (!ready) return;
            relayQuizUI.HideAll();
            relayQuizGallery.Clear();
            SyncView(true);
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
        }

        private void LateUpdate()
        {
            if (!ready) return;
            int serialAtFrameStart = logic.StateSerial;

            UpdatePauseRequest();
            DrainActions();
            if (logic.StateSerial == serialAtFrameStart)
            {
                logic.Tick(Time.unscaledDeltaTime);
            }

            SyncView(false);
            relayQuizUI.UpdateAnswerInput();
            relayQuizUI.UpdateTimer(logic, logic.Paused);
        }

        // focus 상실은 모든 상태, 손 추적 상실은 Drawing에서만 pause한다 (docs/09 §7).
        private void UpdatePauseRequest()
        {
            if (logic.Paused) return;
            if (!hasFocus)
            {
                logic.RequestPause();
                return;
            }
            if (logic.State == RelayQuizState.Drawing && !handInputRouter.HasFreshHand)
            {
                logic.RequestPause();
            }
        }

        private void DrainActions()
        {
            RelayQuizActionRequest request;
            while (relayQuizUI.TryDequeueAction(out request))
            {
                Execute(request);
            }
        }

        private void Execute(RelayQuizActionRequest request)
        {
            int generation = request.captureGeneration;
            switch (request.action)
            {
                case RelayQuizAction.SetPlayers2: logic.SetPlayerCount(2, generation); break;
                case RelayQuizAction.SetPlayers3: logic.SetPlayerCount(3, generation); break;
                case RelayQuizAction.SetPlayers4: logic.SetPlayerCount(4, generation); break;
                case RelayQuizAction.StartGame: logic.StartGame(generation); break;
                case RelayQuizAction.Ready: logic.ConfirmReady(generation); break;
                case RelayQuizAction.CompleteDrawing: logic.CompleteDrawing(generation); break;
                case RelayQuizAction.Submit: logic.SubmitAnswer(generation); break;
                case RelayQuizAction.OpenGallery: logic.OpenGallery(generation); break;
                case RelayQuizAction.Restart: logic.Restart(generation); break;
                case RelayQuizAction.Resume: logic.Resume(generation); break;
                case RelayQuizAction.Undo: RunDrawingCommand(generation, true); break;
                case RelayQuizAction.Clear: RunDrawingCommand(generation, false); break;
            }
        }

        private void RunDrawingCommand(int generation, bool undo)
        {
            if (logic.Paused || generation != logic.PhaseGeneration || logic.State != RelayQuizState.Drawing) return;
            handInputRouter.CancelCanvasCaptures(HandCancelReason.DrawingCommand);
            drawingController.FinalizeActiveStrokes();
            if (undo) drawingController.UndoLastStroke();
            else drawingController.ClearAll();
        }

        private string DrawWord()
        {
            string word = wordBank.Next();
            return word ?? string.Empty;
        }

        // 완료 gate가 딱 한 번 호출한다: capture 취소 → 활성 선 종료 → export.
        private CanvasDrawingData ArchiveCurrentDrawing()
        {
            handInputRouter.CancelCanvasCaptures(HandCancelReason.ViewChanged);
            drawingController.FinalizeActiveStrokes();
            return drawingController.ExportDrawing();
        }

        private string ReadConfirmedAnswer()
        {
            return relayQuizUI.ConfirmedAnswer;
        }

        private RelayQuizPauseStage ResolveStage()
        {
            if (!logic.Paused) return RelayQuizPauseStage.None;
            return hasFocus && handInputRouter.HasFreshHand
                ? RelayQuizPauseStage.ResumeReady
                : RelayQuizPauseStage.Blocked;
        }

        private void SyncView(bool force)
        {
            RelayQuizPauseStage stage = ResolveStage();
            bool stateChanged = logic.StateSerial != appliedSerial;
            bool viewChanged = logic.PhaseGeneration != appliedGeneration || stage != appliedStage;
            if (!force && !stateChanged && !viewChanged) return;

            // 새 화면을 열기 전에 이전 입력·capture를 모두 끊는다.
            inputModeManager.SetContext(InputContext.Blocked);
            handInputRouter.CancelCanvasCaptures(HandCancelReason.ViewChanged);
            drawingController.FinalizeActiveStrokes();
            handInputRouter.SetViewGeneration(logic.PhaseGeneration);
            relayQuizUI.ClearActions();

            if (stateChanged || force)
            {
                ApplyStateEntry();
            }
            ApplyContentVisibility(stage);
            relayQuizUI.ApplyState(logic, stage);
            inputModeManager.SetContext(ResolveContext(stage));
            if (stage == RelayQuizPauseStage.None && logic.State == RelayQuizState.Gallery)
            {
                inputModeManager.RequestMode(InputMode.Move);
            }

            appliedSerial = logic.StateSerial;
            appliedGeneration = logic.PhaseGeneration;
            appliedStage = stage;
        }

        private void ApplyStateEntry()
        {
            switch (logic.State)
            {
                case RelayQuizState.Setup:
                    // 재시작 정리: 그림·presentation·답·타이머·capture를 모두 지운다. 단어 덱은 유지한다.
                    drawingController.ClearAll();
                    previewPresenter.ClearPresentation();
                    relayQuizGallery.Clear();
                    relayQuizUI.ResetAnswerText();
                    playerController.PlaceAt(workPose);
                    break;
                case RelayQuizState.Handover:
                    playerController.PlaceAt(workPose);
                    break;
                case RelayQuizState.Drawing:
                    // 이전 사람의 그림을 남기지 않은 빈 캔버스에서 시작한다.
                    drawingController.ClearAll();
                    break;
                case RelayQuizState.Guessing:
                    relayQuizUI.ResetAnswerText();
                    break;
                case RelayQuizState.Gallery:
                    playerController.PlaceAt(galleryPose);
                    break;
            }
        }

        // 패널로 덮는 데 그치지 않고 실제 renderer·presenter·쓰기 표면을 끈다 (docs/09 §7).
        private void ApplyContentVisibility(RelayQuizPauseStage stage)
        {
            bool visible = stage == RelayQuizPauseStage.None;
            RelayQuizState state = logic.State;

            bool drawing = visible && state == RelayQuizState.Drawing;
            workCanvasRoot.SetActive(drawing);
            drawingController.SetStrokesVisible(drawing);

            bool previewVisible = visible && (state == RelayQuizState.ObservePrevious || state == RelayQuizState.Guessing)
                && logic.PreviousDrawing != null;
            if (previewVisible)
            {
                previewSurface.gameObject.SetActive(true);
                previewPresenter.Show(logic.PreviousDrawing, previewSurface);
            }
            else
            {
                previewPresenter.ClearPresentation();
                previewSurface.gameObject.SetActive(false);
            }

            if (visible && state == RelayQuizState.Gallery) relayQuizGallery.Show(logic.Records);
            else relayQuizGallery.Clear();
        }

        private InputContext ResolveContext(RelayQuizPauseStage stage)
        {
            if (stage == RelayQuizPauseStage.Blocked) return InputContext.Blocked;
            if (stage == RelayQuizPauseStage.ResumeReady) return InputContext.UiOnly;
            switch (logic.State)
            {
                case RelayQuizState.Drawing: return InputContext.Drawing;
                case RelayQuizState.Gallery: return InputContext.Explore;
                default: return InputContext.UiOnly;
            }
        }
    }
}
