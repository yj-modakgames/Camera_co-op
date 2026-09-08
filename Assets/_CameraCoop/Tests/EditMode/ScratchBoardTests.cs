using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace CameraCoop.Tests
{
    // 로비 서쪽 벽 낙서판은 WorkCanvas와 다른 CanvasSurface·HandPointer를 쓴다.
    // 같은 HandPointer를 두 면에 물리면 정규좌표가 같아 두 면에 동시에 그려진다.
    public class ScratchBoardTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> roots = new List<GameObject>();
        private readonly List<string> scratchEvents = new List<string>();
        private readonly List<string> workEvents = new List<string>();
        private GameObject rig;
        private InputModeManager modes;
        private ToolState tools;
        private Camera camera;
        private HandInputRouter router;
        private HandPointer workPointer;
        private HandPointer scratchPointer;
        private CanvasSurface workSurface;
        private CanvasSurface scratchSurface;
        private HandCanvasInteractable workCanvas;
        private HandCanvasInteractable scratchCanvas;

        [SetUp]
        public void SetUp()
        {
            InputFocus.IsTyping = false;
            scratchEvents.Clear();
            workEvents.Clear();
            rig = Root("scratch board input");
            rig.SetActive(false);
            modes = rig.AddComponent<InputModeManager>();
            Set(modes, "initialContext", InputContext.Drawing);
            tools = rig.AddComponent<ToolState>();
            camera = rig.AddComponent<Camera>();
            // 앞선 씬 테스트가 실제 로비 씬을 열어 둔 채 끝나므로 원점 근처엔 가구 collider가 남아 있다.
            // PartyWorldControllerTests처럼 원점에서 먼 곳에 조준선을 둔다.
            camera.transform.position = FarPoint + Vector3.back * 5f;
            camera.transform.rotation = Quaternion.identity;

            // 작업 캔버스는 조준선 밖에 둔다. 조준선 위에는 낙서판만 남긴다.
            workSurface = Surface("work canvas", FarPoint + new Vector3(50f, 0f, 0f));
            scratchSurface = Surface("scratch board", Vector3.zero);
            workPointer = Pointer(workSurface);
            scratchPointer = Pointer(scratchSurface);
            // 낙서판은 세션 없이도 그려져야 한다 — 씬 빌더도 같은 필드를 켠다.
            Set(scratchPointer, "practiceBoard", true);
            workCanvas = Canvas(workSurface, workPointer);
            scratchCanvas = Canvas(scratchSurface, scratchPointer);

            router = rig.AddComponent<HandInputRouter>();
            Set(router, "inputModeManager", modes);
            Set(router, "playerCamera", camera);
            Set(router, "eventSystem", Root("scratch events").AddComponent<EventSystem>());
            Set(router, "uiRaycasters", Array.Empty<GraphicRaycaster>());
            Set(router, "activeCanvas", workCanvas);
            Set(router, "handPointer", workPointer);
            RegisterExtraCanvases(scratchCanvas);

            workPointer.OnCanvasStrokeStart += (hand, norm, world) => workEvents.Add("start:" + hand);
            workPointer.OnCanvasStrokeMove += (hand, norm, world) => workEvents.Add("move:" + hand);
            workPointer.OnCanvasStrokeEnd += hand => workEvents.Add("end:" + hand);
            scratchPointer.OnCanvasStrokeStart += (hand, norm, world) => scratchEvents.Add("start:" + hand);
            scratchPointer.OnCanvasStrokeMove += (hand, norm, world) => scratchEvents.Add("move:" + hand);
            scratchPointer.OnCanvasStrokeEnd += hand => scratchEvents.Add("end:" + hand);

            rig.SetActive(true);
            // EditMode 카메라의 pixelRect는 Screen 크기와 다를 수 있어 "화면 중앙 = 원점" 가정이 어긋난다.
            // 기존 world 라우터 fixture처럼 실제 조준선 위에 표적을 놓는다.
            scratchSurface.transform.position = camera.ScreenPointToRay(Center).GetPoint(5f);
            Call(modes, "Awake");
            Call(workPointer, "Awake");
            Call(workPointer, "OnEnable");
            Call(scratchPointer, "Awake");
            Call(scratchPointer, "OnEnable");
            Call(router, "OnEnable");
            workSurface.gameObject.SetActive(true);
            scratchSurface.gameObject.SetActive(true);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (router != null) Call(router, "OnDisable");
            if (workPointer != null) Call(workPointer, "OnDisable");
            if (scratchPointer != null) Call(scratchPointer, "OnDisable");
            for (int index = roots.Count - 1; index >= 0; index--)
                if (roots[index] != null) Object.DestroyImmediate(roots[index]);
            roots.Clear();
            InputFocus.IsTyping = false;
        }

        [Test]
        public void RegisteredExtraCanvas_ResolvesAsWorldTargetAndDrawsOnItsOwnPointer()
        {
            Assert.That(Resolve(), Is.SameAs(scratchCanvas), "등록된 보조 캔버스가 world raycast 대상이어야 한다.");

            SendFist(1, 0f, false);
            SendFist(2, 0.11f, false);
            SendFist(3, 0.12f, true);
            SendFist(4, 0.13f, true);
            // fist 손실은 fistReleaseGraceSeconds를 넘겨야 획을 끝낸다 (잡음 1~2 sample 흡수).
            SendFist(5, 0.14f, false);
            SendFist(6, 0.30f, false);
            SendFist(7, 0.40f, false);

            CollectionAssert.AreEqual(new[] { "start:Left", "move:Left", "end:Left" }, scratchEvents);
            CollectionAssert.IsEmpty(workEvents, "낙서판 스트로크가 작업 캔버스로 새면 안 된다.");
        }

        // 손을 쥐면 pinch 비율이 먼저 무너져 isPinched가 isFist보다 한 sample 앞선다. 그 첫 sample에서
        // rearm을 지우면 뒤따르는 fist edge가 armed=false를 만나 선이 시작되지 않는다 (화면 녹화 2026-09-07).
        [Test]
        public void PinchLeadingIntoFist_StillStartsTheStroke()
        {
            Send(1, 0f, pinched: false, fist: false);
            Send(2, 0.11f, pinched: false, fist: false);
            Send(3, 0.12f, pinched: true, fist: false);
            CollectionAssert.IsEmpty(scratchEvents, "pinch만으로는 캔버스 획이 시작되지 않는다.");

            Send(4, 0.13f, pinched: true, fist: true);
            Send(5, 0.14f, pinched: true, fist: true);
            Send(6, 0.15f, pinched: false, fist: false);
            Send(7, 0.30f, pinched: false, fist: false);
            Send(8, 0.40f, pinched: false, fist: false);

            CollectionAssert.AreEqual(new[] { "start:Left", "move:Left", "end:Left" }, scratchEvents);
        }

        // host도 참가도 하지 않은 로비(Explore + practiceDrawingAllowed=false)에서도 낙서판은 그려져야 한다
        // (사용자 결정 2026-09-08). CanDraw는 세션 전용 gate라 낙서판에는 쓰지 않는다.
        [Test]
        public void LobbyExploreWithoutSession_StillDrawsOnScratchBoard()
        {
            EnterLobbyExploreWithoutSession();

            Assert.That(Resolve(), Is.SameAs(scratchCanvas), "세션이 없어도 낙서판이 조준 대상이어야 한다.");

            SendFist(1, 0f, false);
            SendFist(2, 0.11f, false);
            SendFist(3, 0.12f, true);
            SendFist(4, 0.13f, true);
            SendFist(5, 0.14f, false);
            SendFist(6, 0.30f, false);
            SendFist(7, 0.40f, false);

            CollectionAssert.AreEqual(new[] { "start:Left", "move:Left", "end:Left" }, scratchEvents);
        }

        // 회귀 방지: 낙서판을 열어 주는 것이 내 종이(activeCanvas)까지 열어서는 안 된다.
        [Test]
        public void LobbyExploreWithoutSession_KeepsWorkCanvasBlocked()
        {
            EnterLobbyExploreWithoutSession();

            Assert.That(workPointer.CanUseCanvas(workSurface), Is.False, "내 종이는 세션 없이 열리면 안 된다.");

            SendTo(workCanvas, 1, 0f, false);
            SendTo(workCanvas, 2, 0.11f, false);
            SendTo(workCanvas, 3, 0.12f, true);
            SendTo(workCanvas, 4, 0.13f, true);

            CollectionAssert.IsEmpty(workEvents, "세션 없는 로비에서 내 종이에 획이 생기면 안 된다.");
        }

        [Test]
        public void BlockedContext_RejectsScratchBoardToo()
        {
            modes.SetContext(InputContext.Blocked);
            modes.SetPracticeDrawingAllowed(false);

            Assert.That(Resolve(), Is.Null, "차폐 중에는 낙서판도 조준 대상이 아니다.");

            SendFist(1, 0f, false);
            SendFist(2, 0.11f, false);
            SendFist(3, 0.12f, true);
            SendFist(4, 0.13f, true);

            CollectionAssert.IsEmpty(scratchEvents, "차폐 중에는 낙서판에도 획이 생기면 안 된다.");
        }

        [Test]
        public void TypingIntoAnswerField_RejectsScratchBoard()
        {
            EnterLobbyExploreWithoutSession();
            InputFocus.IsTyping = true;

            Assert.That(Resolve(), Is.Null, "정답 입력 중에는 낙서판이 조준 대상이 아니다.");
        }

        [Test]
        public void UnregisteredCanvas_IsStillRejectedByTheRouter()
        {
            CanvasSurface strangerSurface = Surface("unregistered board", camera.ScreenPointToRay(Center).GetPoint(4f));
            HandPointer strangerPointer = Pointer(strangerSurface);
            HandCanvasInteractable stranger = Canvas(strangerSurface, strangerPointer);
            strangerSurface.gameObject.SetActive(true);
            Call(strangerPointer, "Awake");
            Call(strangerPointer, "OnEnable");
            Physics.SyncTransforms();

            Assert.That(Resolve(), Is.Null, "등록되지 않은 캔버스는 조준 대상이 될 수 없다.");
            Assert.That(stranger.IsAvailable, Is.True, "거부 사유는 캔버스 비활성이 아니라 미등록이어야 한다.");
        }

        [Test]
        public void ScratchBoardClearButton_ReleaseClearsItsOwnDrawing()
        {
            Type buttonType = typeof(HandPointer).Assembly.GetType("CameraCoop.ScratchBoardClearButton");
            Assert.IsNotNull(buttonType, "낙서판 지우기 버튼 컴포넌트가 필요하다.");

            DrawingController drawing = rig.AddComponent<DrawingController>();
            Set(drawing, "handPointer", scratchPointer);
            Set(drawing, "toolState", tools);
            Set(drawing, "canvasSurface", scratchSurface);
            Assert.That(drawing.TryDrawStrokeForTest(new Vector2(0.2f, 0.2f), new Vector2(0.5f, 0.5f)), Is.True);
            uint drawnRevision = drawing.DrawingRevision;

            var button = (HandInteractable)Root("clear button").AddComponent(buttonType);
            Set(button, "drawingController", drawing);
            bool clickConfirmed = button.Release(Sample(1, false, false), Vector3.zero);

            Assert.That(drawing.DrawingRevision, Is.Not.EqualTo(drawnRevision), "지우기 버튼이 스트로크를 지워야 한다.");
            Assert.That(clickConfirmed, Is.True, "지우기 버튼은 클릭으로 확정되어야 한다.");
            Assert.That(button.UsesWorldHitPosition, Is.True, "월드 버튼은 월드 hit 좌표를 써야 한다.");
        }

        private static readonly Vector3 FarPoint = new Vector3(1000f, 1000f, 1000f);
        private static Vector2 Center => new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        private HandInteractable Resolve()
        {
            return router.ResolveTarget(Center, out _, out _);
        }

        private void SendFist(ulong id, float now, bool fist)
        {
            Send(id, now, false, fist);
        }

        // 자리 배정이 없는 로비 상태 — PartyWorldController.UpdateCanvasMovement가 켜 주지 않은 상태다.
        private void EnterLobbyExploreWithoutSession()
        {
            modes.SetContext(InputContext.Explore);
            modes.SetPracticeDrawingAllowed(false);
        }

        // 조준선 밖에 있는 캔버스를 라우터에 직접 물려 gate만 시험한다.
        private void SendTo(HandInteractable target, ulong id, float now, bool fist)
        {
            router.ProcessSample(Sample(id, false, fist), now, target, workSurface.transform.position);
        }

        private void Send(ulong id, float now, bool pinched, bool fist)
        {
            HandInteractable target = router.ResolveTarget(Center, out Vector3 hit, out _);
            router.ProcessSample(Sample(id, pinched, fist), now, target, hit);
        }

        private static HandInputSample Sample(ulong id, bool pinched, bool fist)
        {
            return new HandInputSample("Left", new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), (uint)id, id,
                0f, true, pinched, HandCancelReason.None, fist);
        }

        private CanvasSurface Surface(string name, Vector3 position)
        {
            GameObject item = Root(name);
            item.transform.position = position;
            item.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, 0.01f);
            item.SetActive(false);
            return item.AddComponent<CanvasSurface>();
        }

        private HandPointer Pointer(CanvasSurface target)
        {
            HandPointer pointer = rig.AddComponent<HandPointer>();
            Set(pointer, "inputSource", HandPointerInputSource.HandRouter);
            Set(pointer, "inputModeManager", modes);
            Set(pointer, "canvasSurface", target);
            Set(pointer, "toolState", tools);
            Set(pointer, "aimCamera", camera);
            return pointer;
        }

        private static HandCanvasInteractable Canvas(CanvasSurface target, HandPointer pointer)
        {
            HandCanvasInteractable canvas = target.gameObject.AddComponent<HandCanvasInteractable>();
            Set(canvas, "canvasSurface", target);
            Set(canvas, "handPointer", pointer);
            return canvas;
        }

        private void RegisterExtraCanvases(params HandCanvasInteractable[] extras)
        {
            FieldInfo field = typeof(HandInputRouter).GetField("extraCanvases", Private);
            Assert.IsNotNull(field, "HandInputRouter가 보조 캔버스 목록을 가져야 한다.");
            Array values = Array.CreateInstance(field.FieldType.GetElementType(), extras.Length);
            for (int index = 0; index < extras.Length; index++) values.SetValue(extras[index], index);
            field.SetValue(router, values);
        }

        private GameObject Root(string name)
        {
            var result = new GameObject(name);
            roots.Add(result);
            return result;
        }

        private static void Set(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, Private);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }

        private static void Call(object target, string method)
        {
            target.GetType().GetMethod(method, Private).Invoke(target, null);
        }
    }
}
