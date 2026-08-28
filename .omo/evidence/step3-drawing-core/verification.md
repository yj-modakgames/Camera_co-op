# Step 3 drawing core evidence

## Ownership

Changed only `Assets/_CameraCoop/Scripts/Drawing/CanvasDrawingData.cs`, `CanvasDrawingPresenter.cs`, `DrawingController.cs`, and appended 29 cases to the existing `Tests/EditMode/DrawingTests.cs` (16 existing assertions preserved). Parent now owns additional workspace-command tests in that same file. No Unity execution, scene edit, commit, camera invocation, package edit, or network/game-session edit was performed by this worker.

## Executed checks

- RED scenario: parent invoked EditMode tests including `CameraCoop.Tests.DrawingTests`, job `6ecb8445b9014dafabd4c9b80ab74b04`, 2026-08-28 06:42:29–30 UTC. Worker directly parsed copied XML: 16 existing cases Passed; 29 new cases Failed. Artifact: `red-TestResults.xml` (73,972 bytes).
- Static whitespace scenario: `git diff --check -- Assets/_CameraCoop/Scripts/Drawing/DrawingController.cs Assets/_CameraCoop/Tests/EditMode/DrawingTests.cs`; binary observable `exit_code=0`; artifact: `diff-check.txt`. CRLF conversion warnings recorded, no whitespace errors.
- Production identity: direct SHA256 calculation; artifact: `production-sha256.json`.
- GREEN: parent invoked fresh compiled EditMode tests, job `e6c930dc2ef54c0da0fe6a0ea176d765`, 2026-08-28 06:50:26 UTC. Worker directly parsed `fresh-green-TestResults.xml`: all 50 DrawingTests Passed (original 16 + worker 29 + parent workspace 5). Entire selected run: 73 total, 72 Passed, 1 unrelated palette test Failed. Exact case names/results and failure identity are in `green-summary.json`. C# LSP remains unavailable; no LSP-success claim.
- Manual Play/visual verification: not performed; explicitly reserved to user and parent workflow. No manual-success claim.

## Scenario mapping — observed GREEN

| Criteria | Exact DrawingTests scenario | Binary observable |
|---|---|---|
| D-1 accepted normalized points / split | Export_ContainsOnlyAcceptedSamplesAndSplitStyles | Passed, two expected xy arrays and two distinct IDs |
| D-2 style freeze / transformed short axis | Export_ContainsOnlyAcceptedSamplesAndSplitStyles; Load_SortsCopiesAndScalesWidthByTransformedShortAxis | Passed, original widths/styles fixed; target shorter transformed axis determines restored width |
| D-3 both hands / independent arrays | Export_FinalizesBothHandsInStartOrderAndCopiesEveryArray; Export_DiscardsOnePointWithoutReusingItsIdOrOrder | Passed, two independent nested arrays; one-point discard; orphan Move ignored |
| D-4 load copy, sort, highwater, atomic rejection | Load_SortsCopiesAndScalesWidthByTransformedShortAxis; Load_InvalidArchiveLeavesActiveWorkAndRenderUntouched (19 cases) | Passed, sorted/deep-copied restoration; same active renderer identity and later Move accepted after every rejection |
| D-5 latest start order Undo | Undo_FinalizesBothHandsThenRemovesLatestStartedStroke | Passed, two active hands finalized, newer start removed, empty undo false |
| D-6/7 clear, erase and archive independence | ClearAndErase_KeepArchiveIndependentAndRenderCountSynchronized | Passed, matching erase ID/render count, saved archive unchanged, new highwater IDs/order |
| D-8 presenter isolation | Presenter_ShowHideClearAreIndependentAndDestroyOnlyOwnedObjects | Passed, own renders replaced/hidden/cleared; unrelated object and archive preserved; no colliders |
| D-9 legacy events/world rendering | Legacy_WorldRenderingAndLocalEventsRemainIndependentOfArchive | Passed, original world point and started ID retained; Clear removes own render |
| Lifecycle | Disable_FinalizesActiveHandsWithoutDestroyingCompletedWork | Passed, one-point discarded and completed work retained |
| Data boundary | TryCopy_ValidatesAndDeepCopiesWithoutUnityReferences | Passed, serialized data contains no Unity object fields and nested copy is independent |

Parent workspace command tests additionally passed: `Workspace_CommandEndsBothCapturesBeforeChangingWork` for undo/clear/save observed two DrawingCommand cancellations, both hands unarmed, held samples unable to begin again, and expected 1/0/2 completed strokes. `Workspace_SaveHideClearRestorePreservesIndependentDrawing` observed saved preview survives hide/work-clear/undo and restores the independent drawing. `Workspace_DisableUnsubscribesCommandsAndHidesOnlyPreview` observed no clear callback after disable, work and generated preview retained, preview surface hidden. These tests invoke actual workspace OnHandClick subscriptions with a router capture probe; they do not prove physical hand input or visual quality. Scene gallery raycast exclusion, C-key wiring, full legacy regression, and real Play visuals remain parent/input-worker/user verification.

## Public API/schema

- `[Serializable] CanvasStrokeData`: `int strokeId`, `int order`, `float[] xy`, `int colorArgb`, `float widthNormalized`, `int brushId`.
- `[Serializable] CanvasDrawingData`: `int version = 1`, `CanvasStrokeData[] strokes = Array.Empty<CanvasStrokeData>()`.
- `public static bool CanvasDrawingData.TryCopy(CanvasDrawingData source, int brushCount, out CanvasDrawingData copy, out string error)`; rejects invalid input with null copy/non-null error, success returns sorted deep copy/null error.
- DrawingController: serialized optional `CanvasSurface canvasSurface`; local archive gate is exactly `handPointer.InputSource == HandPointerInputSource.HandRouter`; public `void FinalizeActiveStrokes()`, `CanvasDrawingData ExportDrawing()`, `bool LoadDrawing(CanvasDrawingData)`, `bool UndoLastStroke()`, existing `void ClearAll()`.
- CanvasDrawingPresenter: serialized `Material lineMaterial`, `Material[] brushMaterials`; public `void Show(CanvasDrawingData, CanvasSurface)`, `void Hide()`, `void ClearPresentation()`.
- Shared internal renderer consumes only stroke data, target surface, parent Transform, Material; presenter has no ToolState/controller/input references.
- Render cleanup calls `Object.Destroy` in Play (legacy timing unchanged), `Object.DestroyImmediate` in EditMode; only objects retained in owner collections are destroyed.

## GREEN artifact integrity

`fresh-green-TestResults.xml` is a direct copy of the parent Unity result, not a fabricated report. `green-summary.json` is parsed from that XML and lists every DrawingTests case. `green-production-hash-check.json` confirms all three owned production files exactly match their previously recorded SHA256 hashes at observation time. No source changes were made for this GREEN evidence update.

Remaining selected-suite failure: `CameraCoop.Tests.ToolStateTests.Palette_HandClickAppliesOnceAndExternalToolChangesRefreshSelectedMarkers`, Expected True / Actual False; outside drawing-core ownership and left to parent/palette worker. This report claims drawing-core and workspace-case GREEN only, not full-suite completion.
