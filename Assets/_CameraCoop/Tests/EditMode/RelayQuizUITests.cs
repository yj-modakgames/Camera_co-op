using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CameraCoop.Tests
{
    public sealed class RelayQuizUITests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private RelayQuizUI ui;
        private GameObject setupRoot;
        private GameObject handoverRoot;
        private Text setupInfoLabel;
        private Text revealLabel;
        private HandButtonInteractable startButton;
        private EventSystem eventSystem;
        private List<EventSystem> previousEventSystems;

        [SetUp]
        public void SetUp()
        {
            List<EventSystem> systems = GlobalEventSystems();
            for (int index = systems.Count - 1; index >= 0; index--)
                if (systems[index] == null) systems.RemoveAt(index);
            previousEventSystems = new List<EventSystem>(systems);
            GameObject eventObject = CreateObject("EventSystem");
            eventSystem = eventObject.AddComponent<EventSystem>();
            systems.Clear();
            systems.Add(eventSystem);
            GameObject uiObject = CreateObject("RelayQuizUI");
            uiObject.SetActive(false);
            ui = uiObject.AddComponent<RelayQuizUI>();

            setupRoot = CreateObject("Setup");
            Set(ui, "setupRoot", setupRoot);
            handoverRoot = CreateObject("Handover");
            Set(ui, "handoverRoot", handoverRoot);
            Set(ui, "wordRevealRoot", CreateObject("WordReveal"));
            Set(ui, "drawingHudRoots", new[] { CreateObject("DrawingHud") });
            Set(ui, "observeRoot", CreateObject("Observe"));
            Set(ui, "guessRoot", CreateObject("Guess"));
            Set(ui, "revealRoot", CreateObject("Reveal"));
            Set(ui, "galleryRoot", CreateObject("Gallery"));
            Set(ui, "pauseShieldRoot", CreateObject("Pause"));
            Set(ui, "timerRoot", CreateObject("Timer"));

            Set(ui, "players2Button", CreateButton("Players2"));
            Set(ui, "players3Button", CreateButton("Players3"));
            Set(ui, "players4Button", CreateButton("Players4"));
            startButton = CreateButton("LegacySetupAction");
            Set(ui, "startButton", startButton);
            Set(ui, "readyButton", CreateButton("TurnReady"));
            Set(ui, "completeDrawingButton", CreateButton("Complete"));
            Set(ui, "undoButton", CreateButton("Undo"));
            Set(ui, "clearButton", CreateButton("Clear"));
            Set(ui, "submitButton", CreateButton("Submit"));
            Set(ui, "galleryButton", CreateButton("OpenGallery"));
            Set(ui, "restartButton", CreateButton("Restart"));
            Set(ui, "resumeButton", CreateButton("Resume"));

            Set(ui, "answerField", CreateObject("Answer").AddComponent<InputField>());
            Set(ui, "answerFocusButton", CreateButton("AnswerFocus"));
            setupInfoLabel = CreateText("SetupInfo");
            Set(ui, "setupInfoLabel", setupInfoLabel);
            Set(ui, "handoverLabel", CreateText("HandoverLabel"));
            Set(ui, "wordLabel", CreateText("WordLabel"));
            Set(ui, "observeLabel", CreateText("ObserveLabel"));
            Set(ui, "guessHintLabel", CreateText("GuessHintLabel"));
            revealLabel = CreateText("RevealLabel");
            Set(ui, "revealLabel", revealLabel);
            Set(ui, "pauseLabel", CreateText("PauseLabel"));
            Set(ui, "timerLabel", CreateText("TimerLabel"));

            Invoke(ui, "Awake");
            Assert.That(ui.IsReady, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
            List<EventSystem> systems = GlobalEventSystems();
            systems.Clear();
            if (previousEventSystems != null) systems.AddRange(previousEventSystems);
        }

        [Test]
        public void OnlineWorldActions_AllowMissingLegacyLobbyButtons()
        {
            Set(ui, "useWorldLobbyActions", true);
            Set(ui, "players2Button", null);
            Set(ui, "players3Button", null);
            Set(ui, "players4Button", null);
            Set(ui, "startButton", null);
            Set(ui, "readyButton", null);

            Invoke(ui, "Awake");

            Assert.That(ui.IsReady, Is.True);
        }

        [Test]
        public void OnlineSetupUsesFourPlayerRosterStatus()
        {
            ui.ApplyOnlineView(new OnlineRelayQuizView { state = RelayQuizState.Setup, rosterCount = 0 },
                RelayQuizPauseStage.None, false, true);
            var view = new OnlineRelayQuizView
            {
                state = RelayQuizState.Setup,
                rosterCount = 3,
                localReady = true,
                remoteReady = false,
                allReady = false
            };

            ui.ApplyOnlineView(view, RelayQuizPauseStage.None, false, true);
            Invoke(ui, "UpdateOnlineSetupNotice", float.MaxValue);
            ui.ApplyOnlineView(view, RelayQuizPauseStage.None, false, true);

            Assert.That(setupInfoLabel.text, Does.Contain("3/" + OnlineRelayQuizProtocol.PlayerCount));
            Assert.That(setupInfoLabel.text, Does.Not.Contain("Steam 두 사람"));
            Assert.That(setupInfoLabel.text, Does.Not.Contain("상대 준비"));
        }

        [Test]
        public void OnlineSetupRemainsHiddenWithoutSession()
        {
            var view = new OnlineRelayQuizView { state = RelayQuizState.Setup, rosterCount = 0 };

            ui.ApplyOnlineView(view, RelayQuizPauseStage.None, false, true);

            Assert.That(setupRoot.activeSelf, Is.False);
        }

        [Test]
        public void OnlineRosterJoinShowsBriefNoticeThenStableSetupStaysHidden()
        {
            var empty = new OnlineRelayQuizView { state = RelayQuizState.Setup, rosterCount = 0 };
            var joined = new OnlineRelayQuizView { state = RelayQuizState.Setup, rosterCount = 1 };
            ui.ApplyOnlineView(empty, RelayQuizPauseStage.None, false, true);

            ui.ApplyOnlineView(joined, RelayQuizPauseStage.None, false, true);

            Assert.That(setupRoot.activeSelf, Is.True);
            Assert.That(setupInfoLabel.text, Does.Contain("입장"));
            Assert.That(setupInfoLabel.text, Does.Contain("1명"));

            Invoke(ui, "UpdateOnlineSetupNotice", float.MaxValue);

            Assert.That(setupRoot.activeSelf, Is.False);

            ui.ApplyOnlineView(joined, RelayQuizPauseStage.None, false, true);

            Assert.That(setupRoot.activeSelf, Is.False);
        }

        [Test]
        public void OnlineGameStartShowsBriefNotice()
        {
            var setup = new OnlineRelayQuizView { state = RelayQuizState.Setup, rosterCount = 4 };
            var started = new OnlineRelayQuizView
            {
                state = RelayQuizState.Handover,
                rosterCount = 4,
                modeStarted = true,
                startSignal = 1
            };
            ui.ApplyOnlineView(setup, RelayQuizPauseStage.None, false, true);

            ui.ApplyOnlineView(started, RelayQuizPauseStage.None, false, true);

            Assert.That(setupRoot.activeSelf, Is.True);
            Assert.That(setupInfoLabel.text, Does.Contain("게임 시작"));
        }

        [Test]
        public void OnlineGameStartNoticeSuppressesHandoverThenRestoresItOnExpiry()
        {
            var setup = new OnlineRelayQuizView { state = RelayQuizState.Setup, rosterCount = 4 };
            var started = new OnlineRelayQuizView
            {
                state = RelayQuizState.Handover,
                rosterCount = 4,
                modeStarted = true,
                startSignal = 1,
                active = true
            };
            ui.ApplyOnlineView(setup, RelayQuizPauseStage.None, false, true);
            Invoke(ui, "UpdateOnlineSetupNotice", float.MaxValue);

            ui.ApplyOnlineView(started, RelayQuizPauseStage.None, false, true);

            Assert.That(setupRoot.activeSelf, Is.True);
            Assert.That(handoverRoot.activeSelf, Is.False);

            Invoke(ui, "UpdateOnlineSetupNotice", float.MaxValue);

            Assert.That(setupRoot.activeSelf, Is.False);
            Assert.That(handoverRoot.activeSelf, Is.True);
        }

        [Test]
        public void OnlineShieldHidesActiveSetupNotice()
        {
            var joined = new OnlineRelayQuizView { state = RelayQuizState.Setup, rosterCount = 1 };
            ui.ApplyOnlineView(joined, RelayQuizPauseStage.None, false, true);
            Assert.That(setupRoot.activeSelf, Is.True);

            ui.ApplyOnlineView(joined, RelayQuizPauseStage.Blocked, true, true);

            Assert.That(setupRoot.activeSelf, Is.False);
        }

        [Test]
        public void SetupErrorRemainsVisibleAfterTransientNoticeExpiry()
        {
            ui.ShowSetupError("연결 오류");

            Invoke(ui, "UpdateOnlineSetupNotice", float.MaxValue);

            Assert.That(setupRoot.activeSelf, Is.True);
            Assert.That(setupInfoLabel.text, Is.EqualTo("연결 오류"));
        }

        [Test]
        public void OnlineSetupHidesLegacyTwoDimensionalReadyAction()
        {
            var view = new OnlineRelayQuizView
            {
                state = RelayQuizState.Setup,
                rosterCount = OnlineRelayQuizProtocol.PlayerCount,
                connected = true
            };
            Assert.That(startButton.gameObject.activeSelf, Is.True);

            ui.ApplyOnlineView(view, RelayQuizPauseStage.None, false, true);

            Assert.That(startButton.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void LocalSetupKeepsLegacyStartActionVisible()
        {
            var logic = new RelayQuizLogic(new RelayQuizTimings(), () => "word",
                () => new CanvasDrawingData(), () => string.Empty);
            Assert.That(startButton.gameObject.activeSelf, Is.True);

            ui.ApplyState(logic, RelayQuizPauseStage.None);

            Assert.That(startButton.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void RevealAnswerDisablesRichTextAndPreservesMarkupLikeAnswer()
        {
            const string markupLikeAnswer = "<color=#ff0000>숨은답</color>";
            revealLabel.supportRichText = true;

            Invoke(ui, "Awake");

            Assert.That(revealLabel.supportRichText, Is.False);

            var view = new OnlineRelayQuizView
            {
                state = RelayQuizState.Reveal,
                word = "제시어",
                answer = markupLikeAnswer,
                correct = false,
                rosterCount = OnlineRelayQuizProtocol.PlayerCount
            };

            ui.ApplyOnlineView(view, RelayQuizPauseStage.None, false, true);

            Assert.That(revealLabel.supportRichText, Is.False);
            Assert.That(revealLabel.text, Is.EqualTo("제시어: 제시어\n제출한 답: " + markupLikeAnswer + "\n오답입니다"));
        }

        [Test]
        public void ReleaseAnswerFocus_ClearsSelectedAnswerFieldAndItsDescendant()
        {
            InputField answerField = Get<InputField>(ui, "answerField");
            GameObject child = CreateObject("Answer Text");
            child.transform.SetParent(answerField.transform, false);
            EventSystem current = EventSystem.current;
            Assert.That(current, Is.Not.Null);
            current.SetSelectedGameObject(child);
            Assert.That(child.transform.IsChildOf(answerField.transform), Is.True);

            ui.ReleaseAnswerFocus();

            Assert.That(current.currentSelectedGameObject, Is.Null);
        }

        [Test]
        public void ReleaseAnswerFocus_PreservesSelectionOwnedByAnotherControl()
        {
            GameObject otherControl = CreateObject("Other Control");
            EventSystem current = EventSystem.current;
            Assert.That(current, Is.Not.Null);
            current.SetSelectedGameObject(otherControl);

            ui.ReleaseAnswerFocus();

            Assert.That(current.currentSelectedGameObject, Is.SameAs(otherControl));
        }

        [Test]
        public void OnlineSetupNoticeExpiry_RestoresPhaseRootsFromNewestView()
        {
            var setup = new OnlineRelayQuizView { state = RelayQuizState.Setup, rosterCount = 4 };
            var handover = new OnlineRelayQuizView
            {
                state = RelayQuizState.Handover,
                rosterCount = 4,
                modeStarted = true,
                startSignal = 1,
                active = true
            };
            var newestReveal = new OnlineRelayQuizView
            {
                state = RelayQuizState.Reveal,
                rosterCount = 4,
                active = true,
                word = "나무"
            };
            ui.ApplyOnlineView(setup, RelayQuizPauseStage.None, false, true);
            ui.ApplyOnlineView(handover, RelayQuizPauseStage.None, false, true);
            Assert.That(setupRoot.activeSelf, Is.True);

            ui.ApplyOnlineView(newestReveal, RelayQuizPauseStage.None, false, true);
            Invoke(ui, "UpdateOnlineSetupNotice", float.MaxValue);

            Assert.That(setupRoot.activeSelf, Is.False);
            Assert.That(handoverRoot.activeSelf, Is.False);
            Assert.That(Get<GameObject>(ui, "revealRoot").activeSelf, Is.True);
        }

        private GameObject CreateObject(string name)
        {
            var result = new GameObject(name);
            objects.Add(result);
            return result;
        }

        private Text CreateText(string name)
        {
            return CreateObject(name).AddComponent<Text>();
        }

        private HandButtonInteractable CreateButton(string name)
        {
            GameObject root = CreateObject(name);
            root.SetActive(false);
            Image background = root.AddComponent<Image>();
            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;
            HandButtonInteractable handButton = root.AddComponent<HandButtonInteractable>();
            Image hover = CreateObject(name + " Hover").AddComponent<Image>();
            hover.transform.SetParent(root.transform, false);
            Image pressed = CreateObject(name + " Pressed").AddComponent<Image>();
            pressed.transform.SetParent(root.transform, false);
            Set(handButton, "targetButton", button);
            Set(handButton, "hoverGraphic", hover);
            Set(handButton, "pressedGraphic", pressed);
            Set(handButton, "eventSystem", eventSystem);
            root.SetActive(true);
            return handButton;
        }

        private static void Set(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }

        private static T Get<T>(object target, string name) where T : class
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return field.GetValue(target) as T;
        }

        private static List<EventSystem> GlobalEventSystems()
        {
            FieldInfo field = typeof(EventSystem).GetField("m_EventSystems",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(null) as List<EventSystem>;
        }

        private static void Invoke(object target, string name, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
                null, Array.ConvertAll(arguments, value => value.GetType()), null);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(target, arguments);
        }
    }
}
