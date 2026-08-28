# Step 3 hand canvas evidence

## Scope and implementation
Owned only HandPointer.cs, HandInputRouter.cs, new HandCanvasInteractable.cs, and additions to existing HandInteractionTests.cs. No Unity operations, scene edits, package changes, or legacy session edits performed by this worker. Parent owns Unity compiler/tests, scene wiring and manual Play.

- CameraCoop.HandPointerInputSource: LegacyCursorEvents=0, HandRouter=1; serialized inputSource, read-only InputSource.
- HandPointer adds serialized inputModeManager. Local BeginCanvasStroke/MoveCanvasStroke/EndCanvasStroke forward existing events with ink-plane coordinates, current surface/CanDraw/StrokesEnabled gates and fixed per-hand draw/erase mode.
- HandInputRouter adds serialized activeCanvas (HandCanvasInteractable), handPointer. Native overlay sorting/blocking stays first; only an absent UI hit permits gated physics fallback, requiring both the actual hit surface and registered adapter identity. Captured noncanvas UI receives screen coordinates even outside UI.
- HandCanvasInteractable has serialized canvasSurface, handPointer; read-only Surface/Pointer; normal release returns false, cancellation/disable end owned strokes.

## RED directly verified
Invocation: parent Unity EditMode run, job 6ecb8445b9014dafabd4c9b80ab74b04.
Artifact: red-TestResults.xml copied from C:/Users/yunji/AppData/Local/Temp/CameraCoop-Step3-20260828/red-TestResults.xml (2026-08-28T06:42:30Z).
Observable: all 10 HandCanvasRoutingTests failed on missing CameraCoop.HandPointerInputSource before production edits. Exact messages are extracted in red-results.json. Overall parent run: 68 tests, 26 passed, 42 failed (includes other workers' RED scenarios).
Source snapshot: tests-red-source.cs. Before-production source: pointer-before.cs, router-before.cs.

## GREEN verification status
Fresh compiled parent Unity EditMode run e6c930dc2ef54c0da0fe6a0ea176d765 at 2026-08-28T06:50:26Z: all 10 HandCanvasRoutingTests Passed. Directly re-read fresh-green-TestResults.xml and asserted exactly 10 matching cases with no non-Passed results; per-case results are in green-results.json. Overall targeted run contained 73 tests, 72 passed and one unrelated palette failure. The four pre-existing GraphicRaycast tests were NOT in this targeted run; no passing claim is made for them. Manual scene/Play verification remains parent-owned and is not claimed here.

## Scenarios and binary observables
| Exact HandCanvasRoutingTests scenario | Binary assertion |
| --- | --- |
| LocalPointer_EmitsInkPlaneCoordinatesAndEndsIdempotently | start/move/end exactly once; world equals surface.NormToWorld(norm) |
| LocalPointer_RejectsUnregisteredSurfaceAndInvalidCoordinates | wrong surface/NaN/out-of-bounds do not start; mismatched move ends and cannot resume |
| LocalPointer_PermissionLossEndsBothHandsAndHeldMoveCannotRestart | context change emits both ends and no subsequent start/move |
| LocalPointer_StrokesDisabledBlocksStartAndCancelsBothHands | false gate emits both ends and blocks further input |
| LocalPointer_FreezesDrawEraseModeForCapturedHand | existing draw stays draw, existing erase stays erase after style changes |
| LocalCanvas_ReleaseIsNotClickAndDisableEndsBothHands | Release returns false; disable ends owned hands once |
| LocalRouter_OnlyRegisteredActualSurfaceMayReceivePhysicsHit | registered canvas resolves; nearer unrelated collider and mismatched surface resolve null |
| LocalRouter_DrawingGateBlocksPhysicsCanvas | UiOnly and StrokesEnabled=false resolve null |
| LocalRouter_GapThenHeldReentryRequiresFreshOpenSamples | gap ends; held reentry does not start; new open samples+pinch rearm; command cancel stops held resume |
| LegacyPointer_RejectsLocalCanvasApi | legacy source emits no event from local BeginCanvasStroke |

Invocation for these scenarios: parent Unity EditMode runner filtered by CameraCoop.Tests.HandCanvasRoutingTests. Result artifacts: fresh-green-TestResults.xml (raw runner result) and green-results.json (all 10 exact scenario names/results).

## Existing-test preservation
existing-tests-integrity.txt records direct exact-content comparison of the complete pre-existing HandInteractionTests suffix against the pre-production snapshot. All four GraphicRaycast_* tests and WaitForCanvasRender assertions/awaits are unchanged.

## Static/source artifacts
changed-files.json and changed-file-hashes.json identify the four owned final sources. LSP C# is unavailable and installation was previously declined; parent Unity compiler is the compiler verification surface. No installation attempted.

legacy-route-integrity.txt confirms the original legacy Route/Emit/EndStroke/IsDrawing suffix unchanged. ui-sorting-integrity.txt confirms native CompareRaycasts unchanged.

