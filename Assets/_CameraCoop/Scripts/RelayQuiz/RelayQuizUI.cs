using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace CameraCoop
{
    public enum RelayQuizAction
    {
        None,
        SetPlayers2,
        SetPlayers3,
        SetPlayers4,
        StartGame,
        Ready,
        CompleteDrawing,
        Undo,
        Clear,
        Submit,
        OpenGallery,
        Restart,
        Resume
    }

    // 차폐 복구 단계 (docs/09 §7). Blocked는 focus·손 조건 대기, ResumeReady는 계속 버튼 대기.
    public enum RelayQuizPauseStage
    {
        None,
        Blocked,
        ResumeReady
    }

    public readonly struct RelayQuizActionRequest
    {
        public readonly RelayQuizAction action;
        public readonly int captureGeneration;

        public RelayQuizActionRequest(RelayQuizAction action, int captureGeneration)
        {
            this.action = action;
            this.captureGeneration = captureGeneration;
        }
    }

    // 상태별 overlay·손 전용 버튼·답 편집·비공개 render 전환 (docs/09 §11).
    // 손 버튼의 down 시점 viewGeneration을 그대로 큐에 복사하고 Button.onClick에는 게임 콜백을 걸지 않는다.
    public sealed class RelayQuizUI : MonoBehaviour
    {
        [Header("Overlay roots")]
        [SerializeField] private GameObject setupRoot;
        [SerializeField] private GameObject handoverRoot;
        [SerializeField] private GameObject wordRevealRoot;
        [SerializeField] private GameObject[] drawingHudRoots;
        [SerializeField] private GameObject observeRoot;
        [SerializeField] private GameObject guessRoot;
        [SerializeField] private GameObject revealRoot;
        [SerializeField] private GameObject galleryRoot;
        [SerializeField] private GameObject pauseShieldRoot;
        [SerializeField] private GameObject timerRoot;

        [Header("Hand buttons")]
        [SerializeField] private HandButtonInteractable players2Button;
        [SerializeField] private HandButtonInteractable players3Button;
        [SerializeField] private HandButtonInteractable players4Button;
        [SerializeField] private HandButtonInteractable startButton;
        [SerializeField] private HandButtonInteractable readyButton;
        [SerializeField] private HandButtonInteractable completeDrawingButton;
        [SerializeField] private HandButtonInteractable undoButton;
        [SerializeField] private HandButtonInteractable clearButton;
        [SerializeField] private HandButtonInteractable submitButton;
        [SerializeField] private HandButtonInteractable galleryButton;
        [SerializeField] private HandButtonInteractable restartButton;
        [SerializeField] private HandButtonInteractable resumeButton;

        [Header("Answer")]
        [SerializeField] private InputField answerField;
        [SerializeField] private HandButtonInteractable answerFocusButton;

        [Header("Labels")]
        [SerializeField] private Text setupInfoLabel;
        [SerializeField] private Text handoverLabel;
        [SerializeField] private Text wordLabel;
        [SerializeField] private Text observeLabel;
        [SerializeField] private Text guessHintLabel;
        [SerializeField] private Text revealLabel;
        [SerializeField] private Text pauseLabel;
        [SerializeField] private Text timerLabel;

        private readonly Queue<RelayQuizActionRequest> pending = new Queue<RelayQuizActionRequest>();
        private readonly List<KeyValuePair<HandButtonInteractable, System.Action<HandClickContext>>> bindings =
            new List<KeyValuePair<HandButtonInteractable, System.Action<HandClickContext>>>();
        private Keyboard subscribedKeyboard;
        private int compositionLength;
        private int shownSeconds = -1;
        private bool initialized;
        private bool subscribed;
        private bool answerActive;

        public bool IsReady { get { return initialized; } }
        public bool IsComposing { get { return compositionLength > 0; } }

        // 조합이 끝난 확정 문자열만 판정에 쓴다 (docs/09 §10).
        public string ConfirmedAnswer
        {
            get { return answerField != null && answerField.text != null ? answerField.text : string.Empty; }
        }

        private void Awake()
        {
            initialized = Validate();
            if (!initialized)
            {
                Debug.LogError("[RelayQuizUI] overlay root·손 버튼·답 입력창·라벨 참조를 모두 할당하세요.", this);
                return;
            }
            BuildBindings();
        }

        private bool Validate()
        {
            return setupRoot != null && handoverRoot != null && wordRevealRoot != null
                && drawingHudRoots != null && drawingHudRoots.Length > 0 && !HasNullRoot()
                && observeRoot != null && guessRoot != null && revealRoot != null && galleryRoot != null
                && pauseShieldRoot != null && timerRoot != null
                && players2Button != null && players3Button != null && players4Button != null
                && startButton != null && readyButton != null && completeDrawingButton != null
                && undoButton != null && clearButton != null && submitButton != null
                && galleryButton != null && restartButton != null && resumeButton != null
                && answerField != null && answerFocusButton != null
                && setupInfoLabel != null && handoverLabel != null && wordLabel != null
                && observeLabel != null && guessHintLabel != null && revealLabel != null
                && pauseLabel != null && timerLabel != null;
        }

        private bool HasNullRoot()
        {
            for (int i = 0; i < drawingHudRoots.Length; i++)
            {
                if (drawingHudRoots[i] == null) return true;
            }
            return false;
        }

        private void BuildBindings()
        {
            bindings.Clear();
            Bind(players2Button, RelayQuizAction.SetPlayers2);
            Bind(players3Button, RelayQuizAction.SetPlayers3);
            Bind(players4Button, RelayQuizAction.SetPlayers4);
            Bind(startButton, RelayQuizAction.StartGame);
            Bind(readyButton, RelayQuizAction.Ready);
            Bind(completeDrawingButton, RelayQuizAction.CompleteDrawing);
            Bind(undoButton, RelayQuizAction.Undo);
            Bind(clearButton, RelayQuizAction.Clear);
            Bind(submitButton, RelayQuizAction.Submit);
            Bind(galleryButton, RelayQuizAction.OpenGallery);
            Bind(restartButton, RelayQuizAction.Restart);
            Bind(resumeButton, RelayQuizAction.Resume);
        }

        private void Bind(HandButtonInteractable button, RelayQuizAction action)
        {
            System.Action<HandClickContext> handler = context => Enqueue(action, context);
            bindings.Add(new KeyValuePair<HandButtonInteractable, System.Action<HandClickContext>>(button, handler));
        }

        private void OnEnable()
        {
            if (!initialized || subscribed) return;
            for (int i = 0; i < bindings.Count; i++)
            {
                bindings[i].Key.OnHandClick += bindings[i].Value;
            }
            subscribed = true;
        }

        private void OnDisable()
        {
            if (subscribed)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    if (bindings[i].Key != null) bindings[i].Key.OnHandClick -= bindings[i].Value;
                }
                subscribed = false;
            }
            pending.Clear();
            UnsubscribeKeyboard();
            SetTyping(false);
        }

        private void OnDestroy()
        {
            UnsubscribeKeyboard();
            SetTyping(false);
        }

        // down 시점 세대를 그대로 옮긴다. 검증은 controller가 현재 세대와 대조해서 한다.
        private void Enqueue(RelayQuizAction action, HandClickContext context)
        {
            if (!initialized || !isActiveAndEnabled) return;
            pending.Enqueue(new RelayQuizActionRequest(action, context.viewGeneration));
        }

        public bool TryDequeueAction(out RelayQuizActionRequest request)
        {
            if (pending.Count == 0)
            {
                request = default;
                return false;
            }
            request = pending.Dequeue();
            return true;
        }

        public void ClearActions()
        {
            pending.Clear();
        }

        // 상태 진입마다 controller가 호출한다. 이전 화면의 root·입력을 먼저 모두 끈다.
        public void ApplyState(RelayQuizLogic logic, RelayQuizPauseStage pauseStage)
        {
            if (!initialized || logic == null) return;
            bool paused = pauseStage != RelayQuizPauseStage.None;
            RelayQuizState state = logic.State;

            setupRoot.SetActive(!paused && state == RelayQuizState.Setup);
            handoverRoot.SetActive(!paused && state == RelayQuizState.Handover);
            wordRevealRoot.SetActive(!paused && state == RelayQuizState.WordReveal);
            SetDrawingHud(!paused && state == RelayQuizState.Drawing);
            observeRoot.SetActive(!paused && state == RelayQuizState.ObservePrevious);
            guessRoot.SetActive(!paused && state == RelayQuizState.Guessing);
            revealRoot.SetActive(!paused && state == RelayQuizState.Reveal);
            galleryRoot.SetActive(!paused && state == RelayQuizState.Gallery);
            pauseShieldRoot.SetActive(paused);
            timerRoot.SetActive(!paused && logic.HasTimer);

            if (paused || state != RelayQuizState.Guessing)
            {
                ReleaseAnswerFocus();
            }
            answerActive = !paused && state == RelayQuizState.Guessing;
            if (!answerActive)
            {
                compositionLength = 0;
            }

            resumeButton.SetInteractable(pauseStage == RelayQuizPauseStage.ResumeReady);

            setupInfoLabel.text = "인원 " + logic.PlayerCount + "명 · 그림 " + (logic.PlayerCount - 1) + "장"
                + "\n지목된 플레이어 외에는 화면을 보지 마세요";
            handoverLabel.text = "플레이어 " + (logic.PlayerIndex + 1) + " 차례입니다"
                + "\n다른 사람은 화면에서 눈을 떼고, 준비되면 손으로 준비를 누르세요";
            // 비공개 제시어는 VisibleWord가 막는다. 다른 상태에서는 빈 문자열이다.
            wordLabel.text = logic.VisibleWord;
            observeLabel.text = "직전 그림을 기억하세요 · 곧 빈 캔버스로 바뀝니다";
            guessHintLabel.text = "손으로 입력창을 선택하고 키보드로 답을 적으세요 · 제출은 손 버튼입니다";
            revealLabel.text = BuildRevealText(logic);
            pauseLabel.text = pauseStage == RelayQuizPauseStage.ResumeReady
                ? "일시정지 · 손으로 계속을 눌러 재개하세요"
                : "일시정지 · 창을 다시 활성화하고 손을 카메라에 보여주세요";

            UpdateTimer(logic, paused);
        }

        private void SetDrawingHud(bool active)
        {
            for (int i = 0; i < drawingHudRoots.Length; i++)
            {
                if (drawingHudRoots[i] != null) drawingHudRoots[i].SetActive(active);
            }
        }

        private static string BuildRevealText(RelayQuizLogic logic)
        {
            if (logic.State != RelayQuizState.Reveal) return string.Empty;
            string guess = string.IsNullOrEmpty(logic.SubmittedAnswer) ? "(빈 답)" : logic.SubmittedAnswer;
            return "제시어: " + logic.VisibleWord + "\n제출한 답: " + guess
                + "\n" + (logic.AnswerCorrect ? "정답입니다" : "오답입니다");
        }

        // 프레임마다 호출되므로 표시 초가 바뀔 때만 문자열을 만든다.
        public void UpdateTimer(RelayQuizLogic logic, bool paused)
        {
            if (!initialized || logic == null) return;
            if (paused || !logic.HasTimer)
            {
                if (shownSeconds != -1)
                {
                    shownSeconds = -1;
                    timerLabel.text = string.Empty;
                }
                return;
            }
            int seconds = Mathf.Max(0, Mathf.CeilToInt(logic.RemainingSeconds));
            if (seconds == shownSeconds) return;
            shownSeconds = seconds;
            timerLabel.text = seconds + "초";
        }

        // Guessing 동안만 타이핑 권한과 조합 상태를 관리한다.
        public void UpdateAnswerInput()
        {
            if (!initialized) return;
            if (!answerActive)
            {
                SetTyping(false);
                UnsubscribeKeyboard();
                submitButton.SetInteractable(false);
                return;
            }
            RefreshKeyboardSubscription();
            bool focused = answerField.isFocused;
            SetTyping(focused);
            bool composing = compositionLength > 0;
            submitButton.SetInteractable(!composing);
            guessHintLabel.text = composing
                ? "글자 조합을 마친 뒤 제출하세요"
                : focused
                    ? "키보드로 답을 적고 손으로 제출을 누르세요 · Enter는 제출하지 않습니다"
                    : "손으로 입력창을 선택하고 키보드로 답을 적으세요 · 제출은 손 버튼입니다";
        }

        // pause·상태 이탈에서 미완료 조합을 취소하고 확정 문자열만 남긴다.
        public void ReleaseAnswerFocus()
        {
            if (answerField != null && answerField.isFocused)
            {
                answerField.DeactivateInputField();
            }
            compositionLength = 0;
            SetTyping(false);
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null) keyboard.SetIMEEnabled(false);
        }

        public void ResetAnswerText()
        {
            if (answerField != null) answerField.text = string.Empty;
            compositionLength = 0;
        }

        // 참조 누락 시 secret을 만들지 않은 채 Setup에 오류만 남긴다 (docs/09 §11).
        public void ShowSetupError(string message)
        {
            HideAll();
            if (setupRoot != null) setupRoot.SetActive(true);
            if (setupInfoLabel != null) setupInfoLabel.text = message;
            if (startButton != null) startButton.SetInteractable(false);
            if (players2Button != null) players2Button.SetInteractable(false);
            if (players3Button != null) players3Button.SetInteractable(false);
            if (players4Button != null) players4Button.SetInteractable(false);
        }

        public void HideAll()
        {
            SetRootActive(setupRoot, false);
            SetRootActive(handoverRoot, false);
            SetRootActive(wordRevealRoot, false);
            if (drawingHudRoots != null) SetDrawingHud(false);
            SetRootActive(observeRoot, false);
            SetRootActive(guessRoot, false);
            SetRootActive(revealRoot, false);
            SetRootActive(galleryRoot, false);
            SetRootActive(pauseShieldRoot, false);
            SetRootActive(timerRoot, false);
            if (wordLabel != null) wordLabel.text = string.Empty;
            if (revealLabel != null) revealLabel.text = string.Empty;
            if (timerLabel != null) timerLabel.text = string.Empty;
            answerActive = false;
            ReleaseAnswerFocus();
        }

        private static void SetRootActive(GameObject root, bool active)
        {
            if (root != null) root.SetActive(active);
        }

        private void RefreshKeyboardSubscription()
        {
            Keyboard current = Keyboard.current;
            if (ReferenceEquals(current, subscribedKeyboard)) return;
            UnsubscribeKeyboard();
            subscribedKeyboard = current;
            if (subscribedKeyboard != null)
            {
                subscribedKeyboard.onIMECompositionChange += HandleComposition;
                subscribedKeyboard.SetIMEEnabled(true);
            }
        }

        private void UnsubscribeKeyboard()
        {
            if (subscribedKeyboard == null) return;
            subscribedKeyboard.onIMECompositionChange -= HandleComposition;
            subscribedKeyboard = null;
        }

        private void HandleComposition(IMECompositionString composition)
        {
            compositionLength = composition.Count;
        }

        // 이 씬에서 InputFocus.IsTyping의 유일한 작성자다 (docs/09 §10).
        private static void SetTyping(bool typing)
        {
            InputFocus.IsTyping = typing;
        }
    }
}
