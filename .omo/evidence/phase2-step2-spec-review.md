# Phase 2 / Step 2 specification review

Status: **SPEC_PASS (static). All three P2 findings are CLOSED. Full runtime/test gate: NOT PASS. User Play: PENDING.**

Final narrow recheck: 2026-08-28, after the 05:27 UTC EditMode run. This report supersedes the earlier open findings. Static approval binds to the exact source hashes below and the approved Step 2 scope; it does not certify later edits or clear the remaining runtime validation gap. This reviewer read source, documentation, scene serialization, and test-result XML, changed only this report, and did not run Unity, Play mode, execute_code, compilation, tests, scene mutations, package changes, or commits.

## Approved boundary

- Step 2 covers the sample contract, router, common interactable, button/input-field adapter, feedback, three-button panel, and EditMode coverage in `C:/git/Camera_co-op/docs/07_hand_interaction.md`.
- The original user phase boundary is at `C:/Users/yunji/.codex/attachments/86669a4c-468b-47df-af97-ac8012a5b0f7/pasted-text.txt:161`.
- Actual Slider, Canvas, and HandPointer integration remains Step 3. Its absence is not a finding. Step 1 and existing online input must remain compatible.

## Findings: all CLOSED

No remaining actionable Step 2 specification finding was identified in the reviewed scope.

### Required status-label references: CLOSED

`C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs:90` now checks both `trackingStatusLabel` and `hoverStatusLabel`, names the missing field in an error, disables the router, and returns. This satisfies `C:/git/Camera_co-op/docs/07_hand_interaction.md:164` and the mandatory-reference rule at line 172. `C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/HandInteractionTests.cs:601` exercises each missing label. Both cases are Passed in the current 05:27 UTC XML.

### Missing receiver: CLOSED

`C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandCursorController.cs:56` checks `receiver`, logs a field-specific error, disables the component, and returns. The regression at `C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/HandInteractionTests.cs:1544` is Passed in the current XML.

### Null entries in a nonempty raycaster list: CLOSED

`C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs:97` checks each configured entry, logs the missing index, disables the component, and returns. `C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/HandInteractionTests.cs:578` covers an entirely unassigned list and a valid entry followed by a null entry. Both cases are Passed in the current XML.

## Lifecycle and interaction re-audit

- `HandInteractable.cs:13` adds a lifecycle revision advanced on disable. Router hover/capture records compare that revision, preventing disable/re-enable between observations from restoring an old capture or suppressing a necessary new hover.
- `HandButtonInteractable.cs:173` returns whether release actually confirmed. It preserves the original press context, checks lifecycle/availability after native callbacks, temporarily suppresses automatic InputField activation, and aborts safely on selection-time disable, re-enable, or destruction.
- `HandInputRouter.cs:428` reserves cooldown before callbacks, rolls it back after rejected release, and plays confirmation audio only for accepted clicks. Cancellation is still distinct from release.
- Samples remain deduplicated by local ID derived from accepted packet references, with Left-before-Right delivery and strict pinch hysteresis. Stale/invalid/lost observations cannot become normal up.
- Freshness, two-observation 0.10-second rearm, gap reset, exclusive target ownership, independent hands, fixed press generation, view/mode/focus cancellation, and blocked-context observation remain implemented. No additional scoped specification defect was found in those paths.
- Native pointerClick/Button.onClick is not used for hand confirmation. The panel subscribes only to OnHandClick. Existing legacy pinch events remain available; real drawing integration is deferred as approved.
- The current XML now records all sample, adapter, and non-render router tests as Passed, including the lifecycle regressions. This reviewer verified that result by reading the artifact, not by running the tests.

## Serialized scene checks

`C:/git/Camera_co-op/Assets/_CameraCoop/Scenes/RelayQuiz.unity` assigns the receiver, two cursors, router dependencies, both status labels, audio clips, and distinct A/B/C button adapters. Its router uses freshness 0.20, rearm 0.10, and cooldown 0.15. A/B/C use distinct click pitches 0.9/1.1/1.3.

The Overlay is Screen Space Overlay with sorting order 100 and CanvasScaler 1920×1080 / match 0.5. Button rectangles are 240×104 and cursors 32×32. Cursor and label graphics do not block raycasts. The three native Button.onClick lists are empty. The EventSystem retains the shared actions asset, clears all individual UI action references, and has navigation events disabled. AudioSource has playOnAwake false, volume 0.2, and a zero spatial-blend curve.

Scene serialization is not proof of rendered layout, actual hit testing, audible feedback, or native-input behavior in Play.

## Runtime evidence: full gate NOT PASS

Read artifact: `C:/git/Camera_co-op/.omo/evidence/phase2-step2-editmode.xml`. Run time: 2026-08-28 05:27:11Z–05:27:16Z.

| Scope | Passed | Failed |
|---|---:|---:|
| Full EditMode suite | 375 | 4 |
| HandSampleTests | 30 | 0 |
| HandButtonTests | 16 | 0 |
| HandInputRouterTests | 42 | 4 |

The full run contains 379 tests, zero skipped. All five missing-reference regression cases passed. The only failures are:

- `GraphicRaycast_DisabledAdapterStillBlocks`
- `GraphicRaycast_HigherOverlaySortOrderWinsRegardlessOfArrayOrder`
- `GraphicRaycast_NonInteractableButtonStillBlocks`
- `GraphicRaycast_TopNonTargetGraphicBlocksUnderlyingTarget`

Each failure message reports that a fixture Graphic did not render within 30 Editor frames and had native depth -1. These failures remain failures; this review does not waive, delete, or reinterpret them as a passing runtime result.

The parent separately reports actual scene HandTestPanel native Graphic.depth 2 via MCP, while fixture depth remained -1 despite 31 Game camera renders and a four-vertex mesh. That diagnostic context was not independently reproduced by this reviewer and does not establish that the four UI raycast scenarios pass. The parent has stopped further speculative fixture edits after three probes.

Current XML SHA256: `9217BEAD78677AC3942397614AD1042AB146AAF840061410101A0C246DF0CDE2`.

## Remaining user Play scope

User Play is still pending: ten pinches must yield ten confirmations; held-hand loss and release over another target must yield zero; simultaneous hands must not confirm one button twice; mouse/Enter/Space must not activate buttons; A/B/C text and audio must differ; hover/press/cancel/focus behavior must work; and 1280×720 plus 16:10 must not clip or overlap. Step 3 will verify overlay occlusion against actual drawing.

SPEC_PASS here is a static specification review only. With four EditMode failures and user Play outstanding, it is not Step 2 completion approval.

## Exact reviewed source hashes (SHA256)

Hashes captured for the final narrow guard recheck. The scene and all previously reviewed non-router implementation hashes remain unchanged from the preceding audit.

| Absolute path | SHA256 |
|---|---|
| `C:/git/Camera_co-op/docs/07_hand_interaction.md` | `DC1250298108CCE10BB8F65B0761000F8C588A94352C5A5EF75F2C7BB82206C0` |
| `C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputTypes.cs` | `D8DAC2663A200939B9F5F7BE871C34B9E649F98508B6745E30076F32D9275F1E` |
| `C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandCursorController.cs` | `8D316F7A7CFBDB21184EF8EC6FF78D6467AEA2D77BEE8410C3B019BB89D3914B` |
| `C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs` | `CB1A1146FF838DC0B9F8790C0CE06E9E3F2D2AC803847004160E9D59E1E717A2` |
| `C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInteractable.cs` | `5817482725B7F3D2E06B93D981940C86A3908258639F1D7F824B3DC974ABF9BC` |
| `C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandButtonInteractable.cs` | `BEB8517E7AD282E2D223CFFACA8F1DBBC619E19714FF2A2D9A0FD8AC42407B39` |
| `C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandUiTestPanel.cs` | `95C62EDFD6146DDC2C1BC38FDF028E64CF4266298724F3586236CD3C41D10D19` |
| `C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/HandInteractionTests.cs` | `A45AB43378CF4E0C83ABCF3AAE51D72A1290F5087E907EFD4AE2539A260FD03F` |
| `C:/git/Camera_co-op/Assets/_CameraCoop/Tests/Support/HandInteractionProbe.cs` | `4971CA880A12D719D62E7129F06D9E6DBCAEDA4C870C677AD57B0216BC0CBAB5` |
| `C:/git/Camera_co-op/Assets/_CameraCoop/Scenes/RelayQuiz.unity` | `A48512D0084650E36F11F226F06608A08C9D8AEE19DEFD7FD6425A7D11A7038C` |
