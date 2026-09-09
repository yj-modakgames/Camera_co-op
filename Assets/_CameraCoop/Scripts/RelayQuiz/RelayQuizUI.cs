using System.Collections.Generic;
using CameraCoop.Party;
using UnityEngine;
using UnityEngine.EventSystems;
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

        [Header("Online World Actions")]
        [SerializeField] private bool useWorldLobbyActions;

        [Header("Answer")]
        [SerializeField] private InputField answerField;
        [SerializeField] private HandButtonInteractable answerFocusButton;

        [Header("Labels")]
        [SerializeField] private Text setupInfoLabel;
        [SerializeField] private Text handoverLabel;
        [SerializeField] private Text wordLabel;
        [SerializeField] private Text drawingInstructionLabel;
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
        private bool onlineResumeDown;
        private int onlineResumeGeneration;
        private bool hasOnlineView;
        private int onlineRosterCount;
        private bool onlineModeStarted;
        private int onlineStartSignal;
        private RelayQuizState onlineState;
        private RelayQuizTextRole onlineTextRole;
        private bool onlineSetupNoticeActive;
        private float onlineSetupNoticeUntil;
        private string onlineSetupNotice = string.Empty;
        private string onlineStatus = string.Empty;
        private OnlineRelayQuizView cachedOnlineView;
        private RelayQuizPauseStage cachedOnlinePauseStage;
        private bool cachedOnlineHidden;
        private bool cachedOnlineCanReady;

        private const float OnlineSetupNoticeDuration = 2.5f;

        public bool IsReady { get { return initialized; } }
        public bool IsComposing { get { return compositionLength > 0; } }

        // 조합이 끝난 확정 문자열만 판정에 쓴다 (docs/09 §10).
        public string ConfirmedAnswer
        {
            get { return answerField != null && answerField.text != null ? answerField.text : string.Empty; }
        }

        private void Awake()
        {
            DisableRevealRichText();
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
                && (useWorldLobbyActions || players2Button != null && players3Button != null
                    && players4Button != null && startButton != null && readyButton != null)
                && completeDrawingButton != null
                && undoButton != null && clearButton != null && submitButton != null
                && galleryButton != null && restartButton != null && resumeButton != null
                && answerField != null && answerFocusButton != null
                && setupInfoLabel != null && handoverLabel != null && wordLabel != null
                && drawingInstructionLabel != null
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
            if (button == null) return;
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

        private void Update()
        {
            UpdateOnlineSetupNotice(Time.unscaledTime);
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
            CancelOnlineSetupNotice();
            ClearCachedOnlineView();
            DisableRevealRichText();
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
            drawingInstructionLabel.text = "캔버스 위에서 핀치를 유지해 그려보세요";
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

        // 연결·참가 실패 문구는 controller의 status에만 남아 화면에 안 보였다.
        // SyncView는 view가 그대로면 조기 return하므로, 문구가 바뀌면 여기서 캐시된 view로 다시 그린다.
        public void SetOnlineStatus(string text)
        {
            string next = string.IsNullOrEmpty(text) ? string.Empty : text;
            if (onlineStatus == next) return;
            onlineStatus = next;
            if (initialized && cachedOnlineView != null)
                ApplyOnlineView(cachedOnlineView, cachedOnlinePauseStage, cachedOnlineHidden, cachedOnlineCanReady);
        }

        public void ApplyOnlineView(OnlineRelayQuizView view, RelayQuizPauseStage pauseStage, bool hidden, bool canReady)
        {
            if (!initialized || view == null) return;
            CacheOnlineView(view, pauseStage, hidden, canReady);
            DisableRevealRichText();
            bool shield = hidden || view.paused;
            bool available = !shield && !view.aborted;
            bool acting = available && view.active && !view.transferPending;
            bool waiting = available && (view.transferPending || !view.active
                && view.state != RelayQuizState.Setup && view.state != RelayQuizState.Reveal && view.state != RelayQuizState.Gallery);
            UpdateOnlineSetupNotice(view, available);
            bool showingNotice = available && onlineSetupNoticeActive;
            // abort되면 available이 false라 모든 root가 꺼져 빈 로비만 남았다. 최소한 사유는 남겨둔다.
            string abortNotice = !view.aborted ? string.Empty
                : string.IsNullOrEmpty(view.status) ? onlineStatus : view.status;
            bool showingStatus = !showingNotice && (view.aborted ? abortNotice.Length > 0
                : available && view.state == RelayQuizState.Setup && onlineStatus.Length > 0);
            setupRoot.SetActive(showingNotice || showingStatus);
            handoverRoot.SetActive(!showingNotice && (waiting || acting && view.state == RelayQuizState.Handover));
            bool promptText = acting && view.referenceKind == RelayQuizReferenceKind.PromptText
                && !string.IsNullOrEmpty(view.privatePrompt);
            wordRevealRoot.SetActive(!showingNotice && acting && view.state == RelayQuizState.WordReveal);
            bool drawing = !showingNotice && acting && view.state == RelayQuizState.Drawing;
            SetDrawingHud(drawing);
            observeRoot.SetActive(false);
            bool textEntry = acting && view.state == RelayQuizState.Guessing
                && view.textRole != RelayQuizTextRole.None && !view.localAnswerSubmitted;
            guessRoot.SetActive(!showingNotice && textEntry);
            revealRoot.SetActive(!showingNotice && available && view.state == RelayQuizState.Reveal);
            galleryRoot.SetActive(!showingNotice && available && view.state == RelayQuizState.Gallery);
            pauseShieldRoot.SetActive(shield);
            timerRoot.SetActive(!showingNotice && available && view.hasTimer && !view.transferPending);
            SetActionActive(players2Button, false);
            SetActionActive(players3Button, false);
            SetActionActive(players4Button, false);
            SetActionActive(startButton, false);
            if (readyButton != null) readyButton.SetInteractable(acting && view.state == RelayQuizState.Handover);
            completeDrawingButton.SetInteractable(acting && view.state == RelayQuizState.Drawing);
            undoButton.SetInteractable(acting && view.state == RelayQuizState.Drawing);
            clearButton.SetInteractable(acting && view.state == RelayQuizState.Drawing);
            galleryButton.SetInteractable(available && view.isHost && !view.transferPending);
            restartButton.SetInteractable(available && view.isHost && !view.transferPending);
            resumeButton.SetInteractable(pauseStage == RelayQuizPauseStage.ResumeReady);
            answerActive = textEntry;
            onlineTextRole = textEntry ? view.textRole : RelayQuizTextRole.None;
            if (!answerActive) ReleaseAnswerFocus();
            string rosterInfo = "Steam 4인 · " + view.rosterCount + "/" + OnlineRelayQuizProtocol.PlayerCount + "명 연결"
                + (onlineStatus.Length > 0 ? " · " + onlineStatus : string.Empty)
                + "\n" + (view.allReady ? "4명 모두 준비 완료"
                    : (view.localReady ? "내 ReadyPad 준비 완료" : "내 ReadyPad 준비 대기")
                        + " · " + (view.remoteReady ? "나머지 3명 준비 완료" : "나머지 플레이어 준비 대기"))
                + (canReady ? "\n각자 자기 ReadyPad에 손을 올려 준비하세요" : "\n카메라 연결을 기다리는 중");
            setupInfoLabel.text = onlineSetupNoticeActive ? onlineSetupNotice
                : view.aborted ? abortNotice : rosterInfo;
            handoverLabel.text = view.transferPending ? "최종 데이터를 전송하는 중입니다"
                : view.active ? HandoverInstruction(view) + "\n준비되면 "
                    + (useWorldLobbyActions ? "내 ReadyPad에서 준비하세요" : "손으로 준비를 눌러주세요")
                    : "다른 플레이어 차례입니다 · 잠시 기다려주세요";
            wordLabel.text = promptText ? view.privatePrompt
                : available && view.state == RelayQuizState.WordReveal ? view.word : string.Empty;
            drawingInstructionLabel.text = drawing && promptText
                ? "비공개 전달 문구: " + view.privatePrompt + " · 이 글만 보고 그리세요"
                : "캔버스 위에서 핀치를 유지해 그려보세요";
            revealLabel.text = available && view.state == RelayQuizState.Reveal ? RevealInstruction(view) : string.Empty;
            pauseLabel.text = hidden ? "창을 다시 활성화해주세요"
                : !view.active ? "다른 플레이어가 일시정지했습니다"
                : pauseStage == RelayQuizPauseStage.ResumeReady ? "일시정지 · 손 또는 mouse로 계속을 눌러주세요"
                : "일시정지 · 창을 활성화하고 손을 카메라에 보여주세요";
            UpdateOnlineTimer(view, shield);
        }

        private void UpdateOnlineSetupNotice(OnlineRelayQuizView view, bool canShow)
        {
            string notice = string.Empty;
            if (!hasOnlineView)
            {
                if (view.rosterCount > 0) notice = RosterNotice(true, view.rosterCount);
            }
            else
            {
                if (!onlineModeStarted && view.modeStarted || view.startSignal > onlineStartSignal
                    || onlineState == RelayQuizState.Setup && view.state != RelayQuizState.Setup)
                {
                    notice = "게임 시작\n릴레이 퀴즈를 시작합니다";
                }
                else if (view.rosterCount != onlineRosterCount)
                {
                    notice = RosterNotice(view.rosterCount > onlineRosterCount, view.rosterCount);
                }
            }

            hasOnlineView = true;
            onlineRosterCount = view.rosterCount;
            onlineModeStarted = view.modeStarted;
            onlineStartSignal = view.startSignal;
            onlineState = view.state;
            if (!canShow)
            {
                CancelOnlineSetupNotice();
                return;
            }
            if (string.IsNullOrEmpty(notice)) return;
            onlineSetupNotice = notice;
            onlineSetupNoticeUntil = Time.unscaledTime + OnlineSetupNoticeDuration;
            onlineSetupNoticeActive = true;
        }

        private void UpdateOnlineSetupNotice(float now)
        {
            if (!onlineSetupNoticeActive || now < onlineSetupNoticeUntil) return;
            CancelOnlineSetupNotice();
            if (cachedOnlineView != null)
                ApplyOnlineView(cachedOnlineView, cachedOnlinePauseStage, cachedOnlineHidden, cachedOnlineCanReady);
            else SetRootActive(setupRoot, false);
        }

        private static string RosterNotice(bool joined, int rosterCount)
        {
            return "플레이어 " + (joined ? "입장" : "퇴장") + " · 현재 " + rosterCount + "명";
        }

        private void CancelOnlineSetupNotice()
        {
            onlineSetupNoticeActive = false;
            onlineSetupNotice = string.Empty;
            onlineSetupNoticeUntil = 0f;
        }

        private void CacheOnlineView(OnlineRelayQuizView view, RelayQuizPauseStage pauseStage, bool hidden, bool canReady)
        {
            cachedOnlineView = view;
            cachedOnlinePauseStage = pauseStage;
            cachedOnlineHidden = hidden;
            cachedOnlineCanReady = canReady;
        }

        private void ClearCachedOnlineView()
        {
            cachedOnlineView = null;
            cachedOnlinePauseStage = RelayQuizPauseStage.None;
            cachedOnlineHidden = false;
            cachedOnlineCanReady = false;
        }

        public void UpdateOnlineTimer(OnlineRelayQuizView view, bool hidden)
        {
            if (!initialized || view == null) return;
            int seconds = hidden || view.paused || view.transferPending || !view.hasTimer
                ? -1 : Mathf.Max(0, Mathf.CeilToInt(view.remaining));
            if (seconds == shownSeconds) return;
            shownSeconds = seconds;
            timerLabel.text = seconds < 0 ? string.Empty : seconds + "초";
        }

        public bool ProcessOnlineResumePointer(Vector2 position, bool pressed, bool released, int generation, bool canResume)
        {
            if (!initialized || !canResume || !pauseShieldRoot.activeInHierarchy || resumeButton == null)
            { onlineResumeDown = false; return false; }
            Canvas canvas = resumeButton.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            bool inside = RectTransformUtility.RectangleContainsScreenPoint(resumeButton.transform as RectTransform, position, camera);
            if (!inside) onlineResumeDown = false;
            if (pressed) { onlineResumeDown = inside; onlineResumeGeneration = generation; }
            bool clicked = released && inside && onlineResumeDown && onlineResumeGeneration == generation;
            if (released) onlineResumeDown = false;
            return clicked;
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
            guessHintLabel.text = TextInstruction(onlineTextRole, focused, composing);
        }

        private static string HandoverInstruction(OnlineRelayQuizView view)
        {
            if (!view.hasSelectedMode) return "내 차례입니다";
            if (view.selectedMode == PartyMode.PictureTelephone)
                return view.textRole == RelayQuizTextRole.PictureDescription ? "앞 그림을 글로 설명할 차례입니다"
                    : view.textRole == RelayQuizTextRole.FinalGuess ? "마지막 그림의 답을 적을 차례입니다"
                    : "비공개 제시문을 그림으로 표현할 차례입니다";
            if (view.selectedMode == PartyMode.DrawingWordChain)
                return view.textRole == RelayQuizTextRole.ChainLabel ? "내 그림의 단어를 비공개로 적을 차례입니다"
                    : "직전 그림에서 이어지는 단어를 그림으로 표현할 차례입니다";
            return "내 차례입니다";
        }

        private static string TextInstruction(RelayQuizTextRole role, bool focused, bool composing)
        {
            if (composing) return "글자 조합을 마친 뒤 제출하세요";
            string task = role == RelayQuizTextRole.PictureDescription ? "그림을 설명하는 글"
                : role == RelayQuizTextRole.FinalGuess ? "마지막 답"
                : role == RelayQuizTextRole.ChainLabel ? "내 그림의 단어" : "답";
            return focused ? "키보드 입력: " + task + " · 손으로 제출하세요 · Enter는 제출하지 않습니다"
                : "입력할 내용: " + task + " · 다른 사람에게는 보이지 않습니다";
        }

        private static string RevealInstruction(OnlineRelayQuizView view)
        {
            if (view.hasSelectedMode && view.selectedMode == PartyMode.PictureTelephone)
            {
                string description = view.revealedTexts != null && view.revealedTexts.Length > 1
                    && !string.IsNullOrEmpty(view.revealedTexts[1]) ? view.revealedTexts[1] : "(빈 설명)";
                string answer = string.IsNullOrEmpty(view.answer) ? "(빈 답)" : view.answer;
                return "그림·글 릴레이 완료\n제시어: " + view.word + "\n그림 설명: " + description
                    + "\n최종 답: " + answer + "\n" + (view.correct ? "정답입니다" : "오답입니다");
            }
            if (view.hasSelectedMode && view.selectedMode == PartyMode.DrawingWordChain)
                return "그림 끝말잇기 완료\n" + (view.chainSucceeded ? "모든 단어가 이어졌습니다" : "이어지지 않은 단어가 있습니다");
            return "제시어: " + view.word + "\n제출한 답: "
                + (string.IsNullOrEmpty(view.answer) ? "(빈 답)" : view.answer)
                + "\n" + (view.correct ? "정답입니다" : "오답입니다");
        }

        // pause·상태 이탈에서 미완료 조합을 취소하고 확정 문자열만 남긴다.
        public void ReleaseAnswerFocus()
        {
            if (answerField != null && answerField.isFocused)
            {
                answerField.DeactivateInputField();
            }
            EventSystem eventSystem = EventSystem.current;
            GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (answerField != null && selected != null
                && (selected == answerField.gameObject || selected.transform.IsChildOf(answerField.transform)))
            {
                eventSystem.SetSelectedGameObject(null);
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
            CancelOnlineSetupNotice();
            ClearCachedOnlineView();
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

        private static void SetActionActive(HandButtonInteractable button, bool active)
        {
            if (button != null) button.gameObject.SetActive(active);
        }

        private void DisableRevealRichText()
        {
            if (revealLabel != null) revealLabel.supportRichText = false;
            if (wordLabel != null) wordLabel.supportRichText = false;
            if (drawingInstructionLabel != null) drawingInstructionLabel.supportRichText = false;
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
