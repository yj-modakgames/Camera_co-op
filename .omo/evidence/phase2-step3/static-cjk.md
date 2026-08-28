# Phase 2 Step 3 — static layout and Korean text review

Scoped verdict: **REVISE evidence**. recommendation: **REJECT an unconditional current-screen CJK pass**; **APPROVE layout in the readable frame only**. This is a visual subreview, not the final code or runtime gate.

## Intent and desired outcome

originalIntent: Provide a usable Unity drawing workspace with a central blank 3:2 canvas, six colors, three-step width slider, three brushes and eraser on the left, five drawing commands below, a read-only saved preview on the right, and camera/mode/tracking controls above.

desiredOutcome: Readable Korean instructions and labels, separated controls without clipping, and honest evidence limited to the camera-off 1920×1080 default state. The user owns Play testing.

userOutcomeReview: The readable first frame visibly supplies the intended groups. Current evidence does not establish stable readable text: subsequent raw frames lose or corrupt every text region. These are observed capture failures, not proof of a Step 3 runtime regression or its root cause. No Play, Unity mutations, execute_code, source edits, or raster edits were performed by this reviewer.

## Directly inspected captures

All three files were opened with view_image. SHA256 was independently recomputed from disk. Coordinates below refer to the 1920×1080 image.

| Artifact in `.omo/evidence/phase2-step3/` | SHA256 | Observation |
|---|---|---|
| step3-static.png | 92362EAE923EFE390DF32472BFF63F7D63DC99FF12A90CD3B1D4BE0EC173C486 | Readable complete default frame. |
| step3-ready-1920.png | 2C6C8CBECD36E2F6AD1FF214B8BF6BD8762767312D5044640DC08EC7F0B9CCB6 | All labels absent; controls and panels remain. |
| step3-ui-1920.png | 671A33196E83CB14A0DD3DC6166D224BC9F756347B548280BA0B7E41F7584CF8 | Glyph fragments/substituted shapes throughout all text regions; not readable Korean. |

`capture.json` records the latest PNG as RGB24, 1920×1080, signature 89504e470d0a1a0a and the matching latest hash. The supplied save/root-toggle chronology is executor context, not independently reproduced; this read-only review cannot identify an atlas mechanism from pixels alone. Earlier Step 2 documentation proves a readable earlier frame, not that these Step 3 failures are harmless.

## Findings

- [product] PASS in `step3-static.png`: left palette x24–296/y132–821 has six labeled swatches, width slider with 얇게/보통/굵게, 펜/마커/세필, and 지우개 · 선 단위. Glyphs are complete, spacing separates groups, and selected black/pen borders are distinguishable. White text on red/purple and dark text on orange/green/blue are visually legible; no numerical contrast compliance claim is made.
- [product] PASS in readable frame: canvas approximately x397–1523/y95–845 is 1126×750 (approximately 3:2). It remains blank below its instruction banner. This proves default layout, not drawable hit bounds or stroke rendering.
- [product] PASS in readable frame: commands x408–1512/y968–1040 are aligned and fully labeled 실행 취소, 전체 지우기, 그림 보관, 보관 복원, 프리뷰 보기. Status above them fits. The right preview x1640–1880/y280–440 is separated from the canvas; its two-line caption explicitly says 그림 보관 후 표시 / 읽기 전용 프리뷰.
- [product] PASS in readable frame: top-left mode, top-center tracking, and top-right camera controls do not collide. 꺼짐 plus the camera-start instruction wrap intentionally into two lines. Lower-left hand instruction wraps naturally without split or missing glyphs. No visible clipping, tofu, or accidental line breaks observed in this one frame.
- [evidence] REVISE: `step3-ready-1920.png`, all text regions, has no text. `step3-ui-1920.png`, all text regions, has corrupted glyphs. Neither can establish a current CJK pass, even though geometry remains consistent with the readable frame.

## Serialized-source corroboration

Inspected `Assets/_CameraCoop/Scenes/RelayQuiz.unity` and `Assets/_CameraCoop/Fonts/NanumGothic-Regular.ttf.meta`; corresponding TTF exists. Scene UI.Text references consistently use GUID a69e6e17347686b4e85869360f390107, matched by the NanumGothic importer with includeFontData 1. These are real text widgets, not raster substitutes. Source text matches the readable Korean labels. Relevant scene locations: command text 1524/6338/7544/8236/12872; preview caption 5541; brush labels 3616/4426/7465; width labels 9005/10578/15366; camera copy 8925; drawing hint 10291.

WidthSlider RectTransform near 12124 is 240×72, top-centered in its parent; native slider configuration and HandSliderInteractable enforce three whole-number values. DrawingCommands near 7756 is 1144×144, bottom-center anchored with a 24-unit inset. OverlayRoot CanvasScaler at 14406 uses 1920×1080 reference and match 0.5, ScreenSpaceOverlay. Fixed-width panels and world-space canvas versus scaled overlay require actual alternate-aspect review; configuration alone does not establish responsive safety.

Read `HandDrawingWorkspace.cs`, `HandSliderInteractable.cs`, and `HandToolPalette.cs`. Runtime workspace messages are grammatical Korean, and the preview label switches between 프리뷰 보기 and 프리뷰 숨김. Long save/error/restore status strings are not present in the default capture, so their actual fit remains unverified.

## Skill-perspective checks and boundaries

Consulted programming and remove-ai-slops. Direct scoped source/scene pass found native widget reuse, no screenshot replacement, and no added parsing or normalization to manufacture the visual result. Serialized reference validation is a Unity configuration boundary; repeated native scene records alone are not an unnecessary abstraction finding. No code-style preference is promoted to a visual blocker.

This subtask does not certify the entire branch's tests or code review coverage. Test-overfit categories (excessive/useless, deletion-only, requested-removal, tautological, implementation-mirroring), broader production extraction, and the full code review's matching skill check remain the code review/final gate owner's responsibility. No separate full code review report was present in this evidence directory when listed. `palette-verification.md` describes behavior checks but is not a complete slop review.

Read `editmode.xml` directly: 484 total, 480 passed, four failures, matching `test-summary.json`. Failures are GraphicRaycast_DisabledAdapterStillBlocks, GraphicRaycast_HigherOverlaySortOrderWinsRegardlessOfArrayOrder, GraphicRaycast_NonInteractableButtonStillBlocks, and GraphicRaycast_TopNonTargetGraphicBlocksUnderlyingTarget. Their pre-existing status and 48-new-test count are executor attribution, not independently proven by a baseline in this subreview. Unit-test totals cannot establish CJK rendering.

## Blockers and exact evidence gaps

blockers:
- violatedCriterion: CJK-READABLE-CURRENT (scoped requirement: readable Korean labels in the current captured workspace).
  observation: Latest two supplied captures have missing or corrupted text, so unconditional current-screen approval is unsupported.
  evidencePointer: `.omo/evidence/phase2-step3/step3-ready-1920.png` and `.omo/evidence/phase2-step3/step3-ui-1920.png`, every text region.

Required to close this evidence finding: a fresh unchanged-scene capture with all Korean labels readable, with capture provenance, or user-observed Play evidence that establishes the actual current UI and clearly distinguishes the editor capture failure. Do not mark existing corrupted frames as passes or silently discard them.

Manual pending: Step 3 Play behavior (new drawing, colors, widths, brushes, erase, undo, clear, save/restore and read-only preview), dynamic Korean statuses, hover/press/cancel/disabled feedback, camera/tracking transitions, two-hand drawing, and actual 1280×720 and 16:10 layout. Previous camera/A-B-C user results are not Step 3 results. No pixel-reference target was requested, so a reference-image diff is not required. No Step 4 or redesign is requested.

`omo ulw-loop status --json` returned exit 1, “The syntax of the command is incorrect”; no currentAttemptDir was available. This artifact is intentionally the assigned static-CJK subreview, not a replacement for the session final gate report.
