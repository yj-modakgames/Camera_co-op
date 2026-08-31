using CameraCoop;
using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CameraCoop.Tests
{
    // docs/07_phase2_drawing.md §8 표의 StrokeLogic 순수 함수 테스트.
    public class DrawingTests
    {
        // ---- StrokeLogic.ShouldAppendPoint ----

        [Test]
        public void ShouldAppendPoint_FirstPointAlwaysAppends()
        {
            Assert.IsTrue(StrokeLogic.ShouldAppendPoint(hasLastPoint: false, Vector3.zero, Vector3.zero, minDistance: 0.01f));
        }

        [Test]
        public void ShouldAppendPoint_BelowMinDistanceRejects()
        {
            var last = Vector3.zero;
            var next = new Vector3(0.005f, 0f, 0f);
            Assert.IsFalse(StrokeLogic.ShouldAppendPoint(hasLastPoint: true, last, next, minDistance: 0.01f));
        }

        [Test]
        public void ShouldAppendPoint_AtExactMinDistanceAppends()
        {
            var last = Vector3.zero;
            var next = new Vector3(0.01f, 0f, 0f);
            Assert.IsTrue(StrokeLogic.ShouldAppendPoint(hasLastPoint: true, last, next, minDistance: 0.01f));
        }

        [Test]
        public void ShouldAppendPoint_AboveMinDistanceAppends()
        {
            var last = Vector3.zero;
            var next = new Vector3(0.5f, 0f, 0f);
            Assert.IsTrue(StrokeLogic.ShouldAppendPoint(hasLastPoint: true, last, next, minDistance: 0.01f));
        }

        // ---- StrokeLogic.Decide (docs/07 §6 엣지 케이스 표) ----

        [Test]
        public void Decide_StartWithoutActive_StartsNew()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.StartNew, StrokeLogic.Decide(hasActiveStroke: false, StrokeLogic.PinchKind.Start));
        }

        [Test]
        public void Decide_StartWithActive_EndsThenStartsNew()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.EndThenStartNew, StrokeLogic.Decide(hasActiveStroke: true, StrokeLogic.PinchKind.Start));
        }

        [Test]
        public void Decide_MoveWithActive_Appends()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.Append, StrokeLogic.Decide(hasActiveStroke: true, StrokeLogic.PinchKind.Move));
        }

        [Test]
        public void Decide_MoveWithoutActive_None()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.None, StrokeLogic.Decide(hasActiveStroke: false, StrokeLogic.PinchKind.Move));
        }

        [Test]
        public void Decide_EndWithActive_Ends()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.End, StrokeLogic.Decide(hasActiveStroke: true, StrokeLogic.PinchKind.End));
        }

        [Test]
        public void Decide_EndWithoutActive_None()
        {
            Assert.AreEqual(StrokeLogic.StrokeAction.None, StrokeLogic.Decide(hasActiveStroke: false, StrokeLogic.PinchKind.End));
        }

        // ---- StrokeLogic.ShouldSplitStroke (재검출 스냅 방어, docs/07 §6) ----

        [Test]
        public void ShouldSplitStroke_WithinThreshold_False()
        {
            Assert.IsFalse(StrokeLogic.ShouldSplitStroke(new Vector2(100f, 100f), new Vector2(150f, 100f), maxSegmentDistance: 100f));
        }

        [Test]
        public void ShouldSplitStroke_AtExactThreshold_False()
        {
            Assert.IsFalse(StrokeLogic.ShouldSplitStroke(new Vector2(0f, 0f), new Vector2(100f, 0f), maxSegmentDistance: 100f));
        }

        [Test]
        public void ShouldSplitStroke_BeyondThreshold_True()
        {
            Assert.IsTrue(StrokeLogic.ShouldSplitStroke(new Vector2(0f, 0f), new Vector2(300f, 0f), maxSegmentDistance: 100f));
        }

        // ---- StrokeLogic.ShouldDiscardOnEnd ----

        [Test]
        public void ShouldDiscardOnEnd_ZeroPoints_True()
        {
            Assert.IsTrue(StrokeLogic.ShouldDiscardOnEnd(0));
        }

        [Test]
        public void ShouldDiscardOnEnd_OnePoint_True()
        {
            Assert.IsTrue(StrokeLogic.ShouldDiscardOnEnd(1));
        }

        [Test]
        public void ShouldDiscardOnEnd_TwoPoints_False()
        {
            Assert.IsFalse(StrokeLogic.ShouldDiscardOnEnd(2));
        }

        [Test]
        public void Export_FinalizesBothHandsInStartOrderAndCopiesEveryArray()
        {
            using (var drawing = new DrawingFixture())
            {
                drawing.Start("Left", .1f, .2f);
                drawing.Start("Right", .4f, .5f);
                drawing.Move("Right", .5f, .5f);
                drawing.Move("Left", .2f, .2f);
                object archive = Call(drawing.Controller, "ExportDrawing");
                Array strokes = Strokes(archive);
                Assert.AreEqual(2, strokes.Length);
                CollectionAssert.AreEqual(new[] { .1f, .2f, .2f, .2f }, Points(strokes.GetValue(0)));
                Assert.Less(Get<int>(strokes.GetValue(0), "order"), Get<int>(strokes.GetValue(1), "order"));
                Points(strokes.GetValue(0))[0] = .9f;
                strokes.SetValue(null, 1);
                Array second = Strokes(Call(drawing.Controller, "ExportDrawing"));
                Assert.AreEqual(.1f, Points(second.GetValue(0))[0]);
                Assert.IsNotNull(second.GetValue(1));
                drawing.Move("Left", .3f, .2f);
                Assert.AreEqual(2, drawing.Lines.Length);
                Assert.AreEqual(2, drawing.Lines[0].positionCount);
            }
        }

        [Test]
        public void Export_ContainsOnlyAcceptedSamplesAndSplitStyles()
        {
            using (var drawing = new DrawingFixture())
            {
                drawing.Start("Left", .1f, .2f);
                drawing.Move("Left", .101f, .2f);
                drawing.Move("Left", .2f, .2f);
                Set(drawing.Tools, "colorIndex", 1);
                Set(drawing.Tools, "widthIndex", 2);
                Set(drawing.Tools, "brushIndex", 1);
                drawing.Move("Left", .8f, .2f);
                drawing.Move("Left", .9f, .2f);
                Array strokes = Strokes(Call(drawing.Controller, "ExportDrawing"));
                Assert.AreEqual(2, strokes.Length);
                CollectionAssert.AreEqual(new[] { .1f, .2f, .2f, .2f }, Points(strokes.GetValue(0)));
                CollectionAssert.AreEqual(new[] { .8f, .2f, .9f, .2f }, Points(strokes.GetValue(1)));
                Assert.AreEqual(.01f, Get<float>(strokes.GetValue(0), "widthNormalized"), 1e-6f);
                Assert.AreEqual(.0495f, Get<float>(strokes.GetValue(1), "widthNormalized"), 1e-6f);
                Assert.AreEqual(0, Get<int>(strokes.GetValue(0), "brushId"));
                Assert.AreEqual(1, Get<int>(strokes.GetValue(1), "brushId"));
                Assert.AreNotEqual(Get<int>(strokes.GetValue(0), "colorArgb"), Get<int>(strokes.GetValue(1), "colorArgb"));
                Assert.Less(Get<int>(strokes.GetValue(0), "strokeId"), Get<int>(strokes.GetValue(1), "strokeId"));
                float lineScale = Mathf.Min(
                    drawing.Lines[0].transform.TransformVector(Vector3.right).magnitude,
                    drawing.Lines[0].transform.TransformVector(Vector3.up).magnitude);
                Assert.AreEqual(.02f, drawing.Lines[0].widthMultiplier * lineScale, 1e-6f);
            }
        }

        [Test]
        public void Export_DiscardsOnePointWithoutReusingItsIdOrOrder()
        {
            using (var drawing = new DrawingFixture())
            {
                drawing.Start("Left", .1f, .2f);
                Call(drawing.Controller, "FinalizeActiveStrokes");
                Call(drawing.Controller, "FinalizeActiveStrokes");
                Assert.AreEqual(0, drawing.Lines.Length);
                drawing.Stroke("Right", .4f);
                object stroke = Strokes(Call(drawing.Controller, "ExportDrawing")).GetValue(0);
                Assert.Greater(Get<int>(stroke, "strokeId"), 1);
                Assert.Greater(Get<int>(stroke, "order"), 0);
            }
        }

        [Test]
        public void Undo_FinalizesBothHandsThenRemovesLatestStartedStroke()
        {
            using (var drawing = new DrawingFixture())
            {
                drawing.Start("Left", .1f, .2f);
                drawing.Start("Right", .4f, .5f);
                drawing.Move("Right", .5f, .5f);
                drawing.Move("Left", .2f, .2f);
                Assert.IsTrue((bool)Call(drawing.Controller, "UndoLastStroke"));
                Array strokes = Strokes(Call(drawing.Controller, "ExportDrawing"));
                Assert.AreEqual(1, strokes.Length);
                Assert.AreEqual(.1f, Points(strokes.GetValue(0))[0]);
                Assert.AreEqual(1, drawing.Lines.Length);
                Assert.IsTrue((bool)Call(drawing.Controller, "UndoLastStroke"));
                Assert.IsFalse((bool)Call(drawing.Controller, "UndoLastStroke"));
            }
        }

        [Test]
        public void ClearAndErase_KeepArchiveIndependentAndRenderCountSynchronized()
        {
            using (var drawing = new DrawingFixture())
            {
                drawing.Stroke("Left", .1f);
                drawing.Stroke("Right", .4f);
                object archive = Call(drawing.Controller, "ExportDrawing");
                int lastId = Get<int>(Strokes(archive).GetValue(1), "strokeId");
                int lastOrder = Get<int>(Strokes(archive).GetValue(1), "order");
                int erasedId = 0;
                drawing.Controller.OnLocalStrokeErased += id => erasedId = id;
                Call(drawing.Controller, "HandleErase", drawing.Surface.NormToWorld(new Vector2(.45f, .2f)));
                Assert.AreEqual(lastId, erasedId);
                Assert.AreEqual(1, Strokes(Call(drawing.Controller, "ExportDrawing")).Length);
                Assert.AreEqual(1, drawing.Lines.Length);
                drawing.Start("Left", .7f, .2f);
                drawing.Move("Left", .8f, .2f);
                drawing.Controller.ClearAll();
                Assert.AreEqual(0, drawing.Lines.Length);
                Assert.AreEqual(0, Strokes(Call(drawing.Controller, "ExportDrawing")).Length);
                Assert.AreEqual(2, Strokes(archive).Length);
                drawing.Stroke("Left", .1f);
                object next = Strokes(Call(drawing.Controller, "ExportDrawing")).GetValue(0);
                Assert.Greater(Get<int>(next, "strokeId"), lastId);
                Assert.Greater(Get<int>(next, "order"), lastOrder);
            }
        }

        [Test]
        public void Load_SortsCopiesAndScalesWidthByTransformedShortAxis()
        {
            using (var drawing = new DrawingFixture())
            {
                object archive = Data(Stroke(8, 40, .2f), Stroke(3, 5, .6f));
                drawing.Surface.transform.localScale = new Vector3(6f, 3f, 1f);
                drawing.Root.transform.localScale = new Vector3(2f, 1f, 1f);
                drawing.Surface.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
                Assert.IsTrue((bool)Call(drawing.Controller, "LoadDrawing", archive));
                Array exported = Strokes(Call(drawing.Controller, "ExportDrawing"));
                Assert.AreEqual(5, Get<int>(exported.GetValue(0), "order"));
                Assert.AreEqual(40, Get<int>(exported.GetValue(1), "order"));
                float shortSide = Mathf.Min(drawing.Surface.transform.TransformVector(Vector3.right).magnitude,
                    drawing.Surface.transform.TransformVector(Vector3.up).magnitude);
                float actualWorldWidth = drawing.Lines[0].widthMultiplier * Mathf.Min(
                    drawing.Lines[0].transform.TransformVector(Vector3.right).magnitude,
                    drawing.Lines[0].transform.TransformVector(Vector3.up).magnitude);
                Assert.AreEqual(.02f * shortSide, actualWorldWidth, 1e-6f);
                Assert.AreEqual(drawing.Surface.NormToWorld(new Vector2(.6f, .2f)),
                    drawing.Lines[0].transform.TransformPoint(drawing.Lines[0].GetPosition(0)));
                Assert.Less(drawing.Lines[0].sortingOrder, drawing.Lines[1].sortingOrder);
                Points(Strokes(archive).GetValue(0))[0] = .99f;
                Assert.AreEqual(.2f, Points(Strokes(Call(drawing.Controller, "ExportDrawing")).GetValue(1))[0]);
                drawing.Controller.ClearAll();
                drawing.Surface.transform.localScale = new Vector3(2f, 4f, 1f);
                drawing.Root.transform.localScale = Vector3.one;
                drawing.Surface.transform.localRotation = Quaternion.identity;
                drawing.Stroke("Left", .1f);
                object next = Strokes(Call(drawing.Controller, "ExportDrawing")).GetValue(0);
                Assert.Greater(Get<int>(next, "strokeId"), 8);
                Assert.Greater(Get<int>(next, "order"), 40);
                Assert.IsTrue((bool)Call(drawing.Controller, "LoadDrawing", Data(Stroke(1, 0, .3f))));
                drawing.Stroke("Right", .6f);
                object afterLoad = Strokes(Call(drawing.Controller, "ExportDrawing")).GetValue(1);
                Assert.Greater(Get<int>(afterLoad, "strokeId"), Get<int>(next, "strokeId"));
                Assert.Greater(Get<int>(afterLoad, "order"), Get<int>(next, "order"));
            }
        }

        [TestCase("version")]
        [TestCase("nullStrokes")]
        [TestCase("nullStroke")]
        [TestCase("nullPoints")]
        [TestCase("shortPoints")]
        [TestCase("oddPoints")]
        [TestCase("nanPoint")]
        [TestCase("infinitePoint")]
        [TestCase("outOfBounds")]
        [TestCase("zeroWidth")]
        [TestCase("nanWidth")]
        [TestCase("infiniteWidth")]
        [TestCase("zeroId")]
        [TestCase("negativeOrder")]
        [TestCase("duplicateId")]
        [TestCase("duplicateOrder")]
        [TestCase("negativeBrush")]
        [TestCase("largeBrush")]
        [TestCase("nullData")]
        public void Load_InvalidArchiveLeavesActiveWorkAndRenderUntouched(string invalid)
        {
            using (var drawing = new DrawingFixture())
            {
                drawing.Start("Left", .1f, .2f);
                drawing.Move("Left", .2f, .2f);
                LineRenderer original = drawing.Lines[0];
                object first = Stroke(1, 0, .5f);
                object second = Stroke(2, 1, .6f);
                object archive = Data(first, second);
                switch (invalid)
                {
                    case "version": Set(archive, "version", 2); break;
                    case "nullStrokes": Set(archive, "strokes", null); break;
                    case "nullStroke": Strokes(archive).SetValue(null, 1); break;
                    case "nullPoints": Set(second, "xy", null); break;
                    case "shortPoints": Set(second, "xy", new[] { .1f, .2f }); break;
                    case "oddPoints": Set(second, "xy", new[] { .1f, .2f, .3f, .4f, .5f }); break;
                    case "nanPoint": Points(second)[0] = float.NaN; break;
                    case "infinitePoint": Points(second)[0] = float.PositiveInfinity; break;
                    case "outOfBounds": Points(second)[0] = 1.1f; break;
                    case "zeroWidth": Set(second, "widthNormalized", 0f); break;
                    case "nanWidth": Set(second, "widthNormalized", float.NaN); break;
                    case "infiniteWidth": Set(second, "widthNormalized", float.PositiveInfinity); break;
                    case "zeroId": Set(second, "strokeId", 0); break;
                    case "negativeOrder": Set(second, "order", -1); break;
                    case "duplicateId": Set(second, "strokeId", 1); break;
                    case "duplicateOrder": Set(second, "order", 0); break;
                    case "negativeBrush": Set(second, "brushId", -1); break;
                    case "largeBrush": Set(second, "brushId", 3); break;
                    case "nullData": archive = null; break;
                }
                LogAssert.Expect(LogType.Error, new Regex("\\[DrawingController\\].*"));
                Assert.IsFalse((bool)Call(drawing.Controller, "LoadDrawing", new[] { archive }));
                Assert.AreSame(original, drawing.Lines[0]);
                drawing.Move("Left", .3f, .2f);
                Assert.AreEqual(3, original.positionCount, "Rejected load must not finalize active work.");
                Assert.AreEqual(1, Strokes(Call(drawing.Controller, "ExportDrawing")).Length);
            }
        }

        [Test]
        public void TryCopy_ValidatesAndDeepCopiesWithoutUnityReferences()
        {
            object source = Data(Stroke(4, 7, .2f));
            MethodInfo method = source.GetType().GetMethod("TryCopy", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, "CanvasDrawingData.TryCopy is required.");
            object[] arguments = { source, 3, null, null };
            Assert.IsTrue((bool)method.Invoke(null, arguments));
            Assert.IsNull(arguments[3]);
            Points(Strokes(source).GetValue(0))[0] = .8f;
            Assert.AreEqual(.2f, Points(Strokes(arguments[2]).GetValue(0))[0]);
            foreach (Type type in new[] { source.GetType(), Strokes(source).GetValue(0).GetType() })
            {
                Assert.IsTrue(type.IsSerializable);
                foreach (FieldInfo field in type.GetFields())
                    Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType));
            }
        }

        [Test]
        public void Presenter_ShowHideClearAreIndependentAndDestroyOnlyOwnedObjects()
        {
            using (var drawing = new DrawingFixture())
            {
                var presenterRoot = new GameObject("Presentation");
                presenterRoot.transform.SetParent(drawing.Root.transform);
                Type presenterType = typeof(DrawingController).Assembly.GetType("CameraCoop.CanvasDrawingPresenter");
                Assert.IsNotNull(presenterType, "CanvasDrawingPresenter is required.");
                Component presenter = presenterRoot.AddComponent(presenterType);
                Set(presenter, "brushMaterials", new Material[3]);
                var unrelated = new GameObject("Unrelated");
                unrelated.transform.SetParent(presenterRoot.transform);
                object archive = Data(Stroke(2, 1, .4f), Stroke(1, 0, .1f));
                Call(presenter, "Show", archive, drawing.Surface);
                LineRenderer[] lines = presenterRoot.GetComponentsInChildren<LineRenderer>(true);
                Assert.AreEqual(2, lines.Length);
                Assert.AreEqual(drawing.Surface.NormToWorld(new Vector2(.1f, .2f)), lines[0].GetPosition(0));
                Assert.AreEqual(.04f, lines[0].widthMultiplier, 1e-6f);
                Call(presenter, "Hide");
                Assert.IsFalse(lines[0].gameObject.activeSelf);
                drawing.Controller.ClearAll();
                Assert.AreEqual(2, Strokes(archive).Length);
                Call(presenter, "Show", archive, drawing.Surface);
                Assert.AreEqual(2, presenterRoot.GetComponentsInChildren<LineRenderer>(true).Length);
                Assert.AreEqual(0, presenterRoot.GetComponentsInChildren<Collider>(true).Length);
                Assert.AreEqual(.4f, Points(Strokes(archive).GetValue(0))[0]);
                Call(presenter, "ClearPresentation");
                Assert.AreEqual(0, presenterRoot.GetComponentsInChildren<LineRenderer>(true).Length);
                Assert.IsTrue(unrelated != null);
                Assert.AreEqual(2, Strokes(archive).Length);
            }
        }

        [Test]
        public void Disable_FinalizesActiveHandsWithoutDestroyingCompletedWork()
        {
            using (var drawing = new DrawingFixture())
            {
                drawing.Stroke("Left", .1f);
                drawing.Start("Right", .5f, .2f);
                Call(drawing.Controller, "OnDisable");
                Assert.AreEqual(1, drawing.Lines.Length);
                Assert.AreEqual(1, Strokes(Call(drawing.Controller, "ExportDrawing")).Length);
            }
        }

        [Test]
        public void Legacy_WorldRenderingAndLocalEventsRemainIndependentOfArchive()
        {
            using (var drawing = new DrawingFixture(false))
            {
                int started = 0;
                drawing.Controller.OnLocalStrokeStarted += (id, hand) => started = id;
                Call(drawing.Controller, "HandleStrokeStart", "Left", Vector2.zero, new Vector3(7f, 8f, 9f));
                Call(drawing.Controller, "HandleStrokeMove", "Left", Vector2.one, new Vector3(7.1f, 8f, 9f));
                Call(drawing.Controller, "HandleStrokeEnd", "Left");
                Assert.AreEqual(1, started);
                Assert.AreEqual(new Vector3(7f, 8f, 9f), drawing.Lines[0].GetPosition(0));
                drawing.Controller.ClearAll();
                Assert.AreEqual(0, drawing.Lines.Length);
            }
        }

        [TestCase("undoButton", 1)]
        [TestCase("clearButton", 0)]
        [TestCase("saveButton", 2)]
        public void Workspace_CommandEndsBothCapturesBeforeChangingWork(string button, int expectedStrokes)
        {
            using (var workspace = new WorkspaceFixture())
            {
                workspace.CaptureBothHands();
                workspace.Drawing.Start("Left", .1f, .2f);
                workspace.Drawing.Move("Left", .2f, .2f);
                workspace.Drawing.Start("Right", .5f, .2f);
                workspace.Drawing.Move("Right", .6f, .2f);

                workspace.Click(button);

                Assert.AreEqual(2, workspace.Probe.cancels);
                Assert.AreEqual(HandCancelReason.DrawingCommand, workspace.Probe.lastCancelReason);
                Assert.AreEqual(expectedStrokes, workspace.Drawing.Controller.ExportDrawing().strokes.Length);
                foreach (string hand in new[] { "Left", "Right" })
                {
                    workspace.Router.TryGetHandState(hand, out HandInputState state);
                    Assert.IsFalse(state.isArmed);
                    workspace.Send(hand, 4, .13f, true);
                }
                Assert.AreEqual(2, workspace.Probe.presses, "Held samples cannot start another stroke after a command.");
            }
        }

        [Test]
        public void Workspace_SaveHideClearRestorePreservesIndependentDrawing()
        {
            using (var workspace = new WorkspaceFixture())
            {
                workspace.Drawing.Stroke("Left", .1f);
                workspace.Click("saveButton");
                Assert.IsTrue(workspace.Preview.gameObject.activeSelf);
                Assert.AreEqual(1, workspace.Presenter.GetComponentsInChildren<LineRenderer>(true).Length);

                workspace.Click("previewButton");
                Assert.IsFalse(workspace.Preview.gameObject.activeSelf);
                workspace.Click("clearButton");
                Assert.AreEqual(0, workspace.Drawing.Lines.Length);
                workspace.Click("loadButton");
                Assert.AreEqual(1, workspace.Drawing.Lines.Length);
                Assert.AreEqual(.1f, workspace.Drawing.Controller.ExportDrawing().strokes[0].xy[0], .0001f);

                workspace.Click("undoButton");
                workspace.Click("previewButton");
                Assert.IsTrue(workspace.Preview.gameObject.activeSelf);
                Assert.AreEqual(1, workspace.Presenter.GetComponentsInChildren<LineRenderer>(true).Length);
                workspace.Click("loadButton");
                Assert.AreEqual(1, workspace.Drawing.Lines.Length, "Restoring never aliases the stored archive to mutable work.");
            }
        }

        [Test]
        public void Workspace_DisableUnsubscribesCommandsAndHidesOnlyPreview()
        {
            using (var workspace = new WorkspaceFixture())
            {
                workspace.Drawing.Stroke("Left", .1f);
                workspace.Click("saveButton");
                Call(workspace.Workspace, "OnDisable");
                workspace.Click("clearButton");
                Assert.IsFalse(workspace.Preview.gameObject.activeSelf);
                Assert.AreEqual(1, workspace.Drawing.Lines.Length);
                Assert.AreEqual(1, workspace.Presenter.GetComponentsInChildren<LineRenderer>(true).Length);
            }
        }

        [Test]
        public void Workspace_ResizedPreviewReprojectsInkWithoutChangingSavedDrawing()
        {
            using (var workspace = new WorkspaceFixture())
            {
                workspace.Drawing.Stroke("Left", .1f);
                workspace.Click("saveButton");
                LineRenderer original = workspace.Presenter.GetComponentInChildren<LineRenderer>();
                float originalWidth = original.startWidth;
                float originalLength = Vector3.Distance(original.GetPosition(0), original.GetPosition(1));

                workspace.Viewport.sizeDelta *= 2f;
                Call(workspace.Workspace, "LateUpdate");

                LineRenderer resized = workspace.Presenter.GetComponentInChildren<LineRenderer>();
                Assert.AreEqual(originalWidth * 2f, resized.startWidth, .0001f);
                Assert.AreEqual(originalLength * 2f, Vector3.Distance(resized.GetPosition(0), resized.GetPosition(1)), .0001f);
                workspace.Click("clearButton");
                workspace.Click("loadButton");
                Assert.AreEqual(.1f, workspace.Drawing.Controller.ExportDrawing().strokes[0].xy[0], .0001f);
            }
        }

        private sealed class WorkspaceFixture : IDisposable
        {
            public readonly DrawingFixture Drawing = new DrawingFixture();
            public readonly GameObject Root;
            public readonly HandDrawingWorkspace Workspace;
            public readonly HandInputRouter Router;
            public readonly HandInteractionProbe Probe;
            public readonly CanvasSurface Preview;
            public readonly RectTransform Viewport;
            public readonly CanvasDrawingPresenter Presenter;
            private readonly System.Collections.Generic.Dictionary<string, HandButtonInteractable> buttons
                = new System.Collections.Generic.Dictionary<string, HandButtonInteractable>();

            public WorkspaceFixture()
            {
                Root = new GameObject("Drawing workspace test");
                Root.SetActive(false);
                InputModeManager modes = Root.AddComponent<InputModeManager>();
                Call(modes, "Awake");
                modes.SetContext(InputContext.Drawing);
                Router = Root.AddComponent<HandInputRouter>();
                Set(Router, "inputModeManager", modes);
                Probe = Child("Canvas capture").AddComponent<HandInteractionProbe>();
                Probe.canvas = true;
                Preview = Child("Read only preview").AddComponent<CanvasSurface>();
                Preview.transform.position = new Vector3(0f, 0f, 3f);
                Camera previewCamera = Child("Preview camera").AddComponent<Camera>();
                previewCamera.enabled = false;
                previewCamera.orthographic = true;
                previewCamera.orthographicSize = 3f;
                previewCamera.pixelRect = new Rect(0f, 0f, 800f, 600f);
                var viewportObject = new GameObject("Preview viewport", typeof(RectTransform));
                viewportObject.transform.SetParent(Root.transform);
                Viewport = viewportObject.GetComponent<RectTransform>();
                Viewport.position = new Vector3(400f, 300f, 0f);
                Viewport.sizeDelta = new Vector2(240f, 160f);
                Presenter = Child("Saved presenter").AddComponent<CanvasDrawingPresenter>();
                Set(Presenter, "brushMaterials", new Material[3]);
                Workspace = Root.AddComponent<HandDrawingWorkspace>();
                Set(Workspace, "handInputRouter", Router);
                Set(Workspace, "drawingController", Drawing.Controller);
                Set(Workspace, "savedPresenter", Presenter);
                Set(Workspace, "previewSurface", Preview);
                Set(Workspace, "previewCamera", previewCamera);
                Set(Workspace, "previewViewport", Viewport);
                foreach (string name in new[] { "undoButton", "clearButton", "saveButton", "loadButton", "previewButton" })
                {
                    HandButtonInteractable button = Child(name).AddComponent<HandButtonInteractable>();
                    buttons.Add(name, button);
                    Set(Workspace, name, button);
                }
                Set(Workspace, "statusLabel", Text("Status"));
                Set(Workspace, "previewButtonLabel", Text("Preview label"));
                Call(Workspace, "Awake");
                Root.SetActive(true);
                Call(Router, "OnEnable");
                Call(Workspace, "OnEnable");
            }

            private GameObject Child(string name)
            {
                var child = new GameObject(name);
                child.transform.SetParent(Root.transform);
                return child;
            }

            private UnityEngine.UI.Text Text(string name)
            {
                var child = new GameObject(name, typeof(RectTransform));
                child.transform.SetParent(Root.transform);
                return child.AddComponent<UnityEngine.UI.Text>();
            }

            public void Click(string name)
            {
                var callback = (Action<HandClickContext>)typeof(HandButtonInteractable)
                    .GetField("OnHandClick", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(buttons[name]);
                callback?.Invoke(new HandClickContext("Left", 0, 1));
            }

            public void CaptureBothHands()
            {
                foreach (string hand in new[] { "Left", "Right" })
                {
                    Send(hand, 1, 0f, false);
                    Send(hand, 2, .11f, false);
                    Send(hand, 3, .12f, true);
                }
                Assert.AreEqual(2, Probe.presses);
            }

            public void Send(string hand, ulong id, float now, bool fisted)
            {
                Router.ProcessSample(new HandInputSample(hand, Vector2.zero, (uint)id, id, 0f, true, false,
                    HandCancelReason.None, fisted), now, Probe, Vector3.zero);
            }

            public void Dispose()
            {
                Call(Workspace, "OnDisable");
                Call(Router, "OnDisable");
                UnityEngine.Object.DestroyImmediate(Root);
                Drawing.Dispose();
            }
        }

        // ---- docs/09 §8 릴레이 archive 계약 (별도 DrawingArchiveTests를 만들지 않는다) ----

        [Test]
        public void RelayArchive_IsUnaffectedByLaterUndoClearAndNewStrokes()
        {
            using (var fixture = new DrawingFixture())
            {
                fixture.Stroke("Left", .1f);
                fixture.Stroke("Right", .4f);

                var logic = new RelayQuizLogic(new RelayQuizTimings(), () => "사과",
                    fixture.Controller.ExportDrawing, () => string.Empty);
                logic.SetPlayerCount(2, logic.PhaseGeneration);
                logic.StartGame(logic.PhaseGeneration);
                logic.ConfirmReady(logic.PhaseGeneration);
                logic.Tick(5f);
                Assert.IsTrue(logic.CompleteDrawing(logic.PhaseGeneration));

                CanvasDrawingData archived = logic.Records[0].drawing;
                Assert.AreEqual(2, archived.strokes.Length);
                float[] before = (float[])archived.strokes[0].xy.Clone();

                Assert.IsTrue(fixture.Controller.UndoLastStroke());
                fixture.Stroke("Left", .7f);
                fixture.Controller.ClearAll();

                Assert.AreEqual(2, archived.strokes.Length, "archive는 이후 작업 그림 변경에 영향받지 않는다.");
                CollectionAssert.AreEqual(before, archived.strokes[0].xy);
            }
        }

        [Test]
        public void RelayArchive_DeepCopiesEveryStrokeArray()
        {
            using (var fixture = new DrawingFixture())
            {
                fixture.Stroke("Left", .1f);
                CanvasDrawingData exported = fixture.Controller.ExportDrawing();

                var logic = new RelayQuizLogic(new RelayQuizTimings(), () => "사과",
                    () => exported, () => string.Empty);
                logic.SetPlayerCount(2, logic.PhaseGeneration);
                logic.StartGame(logic.PhaseGeneration);
                logic.ConfirmReady(logic.PhaseGeneration);
                logic.Tick(5f);
                logic.CompleteDrawing(logic.PhaseGeneration);

                CanvasDrawingData archived = logic.Records[0].drawing;
                Assert.AreNotSame(exported, archived);
                Assert.AreNotSame(exported.strokes, archived.strokes);
                Assert.AreNotSame(exported.strokes[0], archived.strokes[0]);
                Assert.AreNotSame(exported.strokes[0].xy, archived.strokes[0].xy);
            }
        }

        [Test]
        public void SetStrokesVisible_TogglesRenderObjectsWithoutLosingData()
        {
            using (var fixture = new DrawingFixture())
            {
                fixture.Stroke("Left", .1f);
                fixture.Stroke("Right", .4f);
                Assert.AreEqual(2, fixture.Lines.Length);

                fixture.Controller.SetStrokesVisible(false);
                foreach (LineRenderer line in fixture.Lines)
                {
                    Assert.IsFalse(line.gameObject.activeSelf, "차폐 중에는 실제 선 렌더를 끈다.");
                }

                CanvasDrawingData exported = fixture.Controller.ExportDrawing();
                Assert.AreEqual(2, exported.strokes.Length, "숨김은 데이터를 지우지 않는다.");

                fixture.Controller.SetStrokesVisible(true);
                foreach (LineRenderer line in fixture.Lines)
                {
                    Assert.IsTrue(line.gameObject.activeSelf);
                }
            }
        }

        [Test]
        public void SetStrokesVisible_AppliesToStrokesStartedWhileHidden()
        {
            using (var fixture = new DrawingFixture())
            {
                fixture.Controller.SetStrokesVisible(false);
                fixture.Stroke("Left", .1f);
                Assert.AreEqual(1, fixture.Lines.Length);
                Assert.IsFalse(fixture.Lines[0].gameObject.activeSelf);
            }
        }

        [Test]
        public void LocalStrokeRenderer_FollowsCanvasSurfaceAfterBoardMoves()
        {
            using (var fixture = new DrawingFixture())
            {
                fixture.Stroke("Left", .1f);
                LineRenderer line = fixture.Lines[0];
                Assert.IsFalse(line.useWorldSpace, "Personal-canvas ink must be stored relative to its moving surface.");

                fixture.Surface.transform.position = new Vector3(3f, -2f, 5f);
                fixture.Surface.transform.rotation = Quaternion.Euler(0f, 35f, 12f);
                Vector3 renderedStart = line.transform.TransformPoint(line.GetPosition(0));
                Vector3 expectedStart = fixture.Surface.NormToWorld(new Vector2(.1f, .2f));

                Assert.AreEqual(expectedStart.x, renderedStart.x, 1e-4f);
                Assert.AreEqual(expectedStart.y, renderedStart.y, 1e-4f);
                Assert.AreEqual(expectedStart.z, renderedStart.z, 1e-4f);
                Assert.AreSame(fixture.Surface.transform, line.transform.parent);
            }
        }

        [Test]
        public void LocalStrokeWidth_TracksCanvasScaleAndMatchesLoadedReplay()
        {
            using (var fixture = new DrawingFixture())
            {
                fixture.Surface.transform.localScale = Vector3.one * 3f;
                fixture.Stroke("Left", .1f);
                CanvasDrawingData drawing = fixture.Controller.ExportDrawing();
                float shortSide = Mathf.Min(fixture.Surface.transform.TransformVector(Vector3.right).magnitude,
                    fixture.Surface.transform.TransformVector(Vector3.up).magnitude);
                float expectedWidth = drawing.strokes[0].widthNormalized * shortSide;
                float actualWidth = fixture.Lines[0].widthMultiplier * shortSide;
                Assert.AreEqual(expectedWidth, actualWidth, 1e-6f,
                    "The local renderer parent scale must supply the world scale exactly once.");

                fixture.Surface.transform.position = new Vector3(2f, -1f, 4f);
                fixture.Surface.transform.rotation = Quaternion.Euler(10f, 25f, 35f);
                fixture.Surface.transform.localScale = Vector3.one * 5f;
                shortSide = Mathf.Min(fixture.Surface.transform.TransformVector(Vector3.right).magnitude,
                    fixture.Surface.transform.TransformVector(Vector3.up).magnitude);
                expectedWidth = drawing.strokes[0].widthNormalized * shortSide;
                actualWidth = fixture.Lines[0].widthMultiplier * shortSide;
                Assert.AreEqual(expectedWidth, actualWidth, 1e-6f);

                Assert.IsTrue(fixture.Controller.LoadDrawing(drawing));
                actualWidth = fixture.Lines[0].widthMultiplier * shortSide;
                Assert.AreEqual(expectedWidth, actualWidth, 1e-6f);
            }
        }

        [Test]
        public void PersonalCanvasCarryDock_PreservesNormalizedDrawingAndRevision()
        {
            using (var fixture = new DrawingFixture())
            {
                fixture.Stroke("Left", .1f);
                CanvasDrawingData before = fixture.Controller.ExportDrawing();
                uint revision = fixture.Controller.DrawingRevision;
                var avatar = new GameObject("revision avatar");
                var dock = new GameObject("revision dock");
                try
                {
                    var placement = fixture.Root.AddComponent<PersonalCanvasPlacement>();
                    placement.Configure("owner", avatar.transform, dock.transform, .5f);
                    Assert.IsTrue(placement.TryCarry("owner"));
                    avatar.transform.position = dock.transform.position;
                    MethodInfo tryDock = typeof(PersonalCanvasPlacement).GetMethod("TryDock", new[] { typeof(string) });
                    Assert.IsNotNull(tryDock, "Docking must validate the controlled canvas position instead of caller coordinates.");
                    Assert.IsTrue((bool)tryDock.Invoke(placement, new object[] { "owner" }));

                    CanvasDrawingData after = fixture.Controller.ExportDrawing();
                    Assert.AreEqual(revision, fixture.Controller.DrawingRevision);
                    Assert.AreEqual(before.version, after.version);
                    CollectionAssert.AreEqual(before.strokes[0].xy, after.strokes[0].xy);
                }
                finally
                {
                    if (fixture.Root != null) fixture.Root.transform.SetParent(null, true);
                    UnityEngine.Object.DestroyImmediate(avatar);
                    UnityEngine.Object.DestroyImmediate(dock);
                }
            }
        }

        private static object Call(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, name + " is required.");
            return method.Invoke(target, args);
        }

        private static void Set(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name + " is required.");
            field.SetValue(target, value);
        }

        private static T Get<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name);
            Assert.IsNotNull(field, name + " is required.");
            return (T)field.GetValue(target);
        }

        private static Array Strokes(object drawing) { return Get<Array>(drawing, "strokes"); }
        private static float[] Points(object stroke) { return Get<float[]>(stroke, "xy"); }

        private static object Stroke(int id, int order, float x)
        {
            Type type = typeof(DrawingController).Assembly.GetType("CameraCoop.CanvasStrokeData");
            Assert.IsNotNull(type, "CanvasStrokeData is required.");
            object stroke = Activator.CreateInstance(type);
            Set(stroke, "strokeId", id);
            Set(stroke, "order", order);
            Set(stroke, "xy", new[] { x, .2f, x + .1f, .2f });
            Set(stroke, "colorArgb", unchecked((int)0xFF336699));
            Set(stroke, "widthNormalized", .02f);
            Set(stroke, "brushId", 0);
            return stroke;
        }

        private static object Data(params object[] strokes)
        {
            Type type = typeof(DrawingController).Assembly.GetType("CameraCoop.CanvasDrawingData");
            Assert.IsNotNull(type, "CanvasDrawingData is required.");
            object data = Activator.CreateInstance(type);
            Array array = Array.CreateInstance(typeof(DrawingController).Assembly.GetType("CameraCoop.CanvasStrokeData"), strokes.Length);
            for (int i = 0; i < strokes.Length; i++) array.SetValue(strokes[i], i);
            Set(data, "version", 1);
            Set(data, "strokes", array);
            return data;
        }

        private sealed class DrawingFixture : IDisposable
        {
            public readonly GameObject Root;
            public readonly DrawingController Controller;
            public readonly ToolState Tools;
            public readonly CanvasSurface Surface;
            public LineRenderer[] Lines { get { return Controller.GetComponentsInChildren<LineRenderer>(true); } }

            public DrawingFixture(bool local = true)
            {
                FieldInfo source = typeof(HandPointer).GetField("inputSource", BindingFlags.Instance | BindingFlags.NonPublic);
                if (local)
                {
                    Assert.IsNotNull(source, "HandPointer.inputSource is required.");
                    Assert.IsNotNull(typeof(DrawingController).GetField("canvasSurface", BindingFlags.Instance | BindingFlags.NonPublic),
                        "DrawingController.canvasSurface is required.");
                }
                Root = new GameObject("DrawingTest");
                Root.SetActive(false);
                Tools = Root.AddComponent<ToolState>();
                var surfaceObject = new GameObject("Surface");
                surfaceObject.transform.SetParent(Root.transform);
                Surface = surfaceObject.AddComponent<CanvasSurface>();
                Surface.transform.localScale = new Vector3(2f, 4f, 1f);
                HandPointer pointer = Root.AddComponent<HandPointer>();
                if (local)
                {
                    source.SetValue(pointer, Enum.Parse(source.FieldType, "HandRouter"));
                }
                Controller = Root.AddComponent<DrawingController>();
                Set(Controller, "handPointer", pointer);
                Set(Controller, "toolState", Tools);
                if (local) Set(Controller, "canvasSurface", Surface);
            }

            public void Start(string hand, float x, float y)
            {
                Vector2 norm = new Vector2(x, y);
                Call(Controller, "HandleStrokeStart", hand, norm, Surface.NormToWorld(norm));
            }

            public void Move(string hand, float x, float y)
            {
                Vector2 norm = new Vector2(x, y);
                Call(Controller, "HandleStrokeMove", hand, norm, Surface.NormToWorld(norm));
            }

            public void Stroke(string hand, float x)
            {
                Start(hand, x, .2f);
                Move(hand, x + .1f, .2f);
                Call(Controller, "HandleStrokeEnd", hand);
            }

            public void Dispose() { UnityEngine.Object.DestroyImmediate(Root); }
        }
    }
}
