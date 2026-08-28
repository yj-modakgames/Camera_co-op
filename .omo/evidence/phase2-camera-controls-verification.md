# Camera controls verification — 2026-08-28

Status: implementation saved; targeted tests PASS; captured Full HD Off-state visual gate PASS; full suite retains 4 known failures. Other resolutions and actual Play/hardware checks remain with the user.

## Scope and approval

User approved docs/07 §10 with “응”: only camera controls receive left-mouse input. A/B/C remain hand-only. No automatic Play/camera startup, no Unity execute_code, no scene text edits, no package/Python/online scene changes, no commit. Scene mutations used dedicated Unity MCP and were saved to RelayQuiz.

## Runtime evidence

Unity 6000.3.15f1, project Camera_co-op@4415d725e059250f. All runs below were EditMode; launcher tests used the fake process boundary only.

| Run | Job | Total/pass/fail | Evidence |
|---|---|---|---|
| Initial RED | 2c1f8c4300144a4e829771d261f64743 | 84/28/56 | red-TestResults.xml; missing new capabilities |
| First implementation | 4d591979cd714e65904a0e1ba20a2bd0 | 84/83/1 | first-green-TestResults.xml; inactive router fixture |
| Fixture diagnostic | 1c76cbbdb5a6433fadbedd3873bbbe1b | 1/0/1 | fixture-diagnostic-TestResults.xml; router active=False, handUI=True, state=External; open rearm passed |
| External packet RED | dd78c79093694d93860e992065f74ea7 | 85/84/1 | external-red-TestResults.xml; Expected External, actual Off; capture regression now passed |
| Targeted GREEN | 18cbdfb6bbae4250981302b16a14b35f | 85/85/0 | targeted-green-TestResults.xml, 06:17:46 UTC |
| Final full suite | 04ca49c16d3c49efbdb65e64fc8fe9b9 | 436/432/4 | phase2-camera-controls-editmode.xml, 06:22:49 UTC |

Temp directory: `C:/Users/yunji/AppData/Local/Temp/CameraCoop-CameraControls-20260828/`.
Targeted groups: CameraControlTests 23, TrackerLauncherTests 18, InputModeTests 44. New amendment cases: 57. No skipped tests.

The four full-suite failures are unchanged `HandInputRouterTests.GraphicRaycast_*` native Canvas render cases: DisabledAdapterStillBlocks, HigherOverlaySortOrderWinsRegardlessOfArrayOrder, NonInteractableButtonStillBlocks, TopNonTargetGraphicBlocksUnderlyingTarget. Their actual depth remains -1 after 30 Editor frames. No assertion was weakened or skipped. Prior investigation and limitations remain in the Step2 evidence.

## Compile and scene

- Latest source refresh → console: no C# compile errors; existing MCP reconnect and pipeline automated-mode warnings remain. Full tests also log expected legacy negative-path warnings.
- Panel refs: Launcher, Receiver, InputModeManager, HandInputRouter, Button, button Text, status Text, all assigned and read back.
- Launcher legacy buttonLabel remains null in RelayQuiz; its existing API/label support remains for old scenes.
- Shared InputSystemUIInputModule individual point/scroll/click/move/submit/cancel/tracked-device action refs all null, existing actionsAsset retained. Camera Button Navigation.None, native onClick empty.
- Scene saved; isDirty=false, roots=6; validation missingScripts=0, brokenPrefabs=0. YAML readback used only for verification, never editing.

## Reviews

- `phase2-camera-controls-spec-review.md`: PASS after new external packet adoption regression/fix.
- `phase2-camera-controls-code-review.md`: APPROVE/WATCH, no blockers. Initial preparation concern was retracted against the approved bootstrap contract. Nonblocking notes concern panel size and reflection-based Unity test setup.
- `phase2-camera-controls-static-integrity.md` and `phase2-camera-controls-static-cjk.md`: PASS/APPROVE, high confidence, no blockers for the one captured static state. Both reviewers directly inspected the latest PNG and checked current source hashes.

## Visual gate and manual limitations

- `static-ui-initial.png`: native PrintWindow capture of Unity, 2028×1607, Full HD Game view. Existing/new panels visible but all Text absent. This is not a readable UI PASS.
- `static-ui-after-full.png`: same native window after full tests and Game menu focus; Overlay absent. Scene remains saved with valid refs.
- The prior Step2 render failure was already investigated through three approaches. No speculative product/render workaround was added here. User was asked to restart Unity without entering Play for a fresh final visual check.
- At 06:26 UTC, current scene was independently read as Netplay3D, with Play reported by the editor state and no CameraControls object. Two Python processes (parent/child) were observed under Unity. This is not evidence of the new RelayQuiz camera path. The agent did not stop Play or those processes, and asked the user to open RelayQuiz after stopping Play themselves.
- At 06:29 UTC the active scene was RelayQuiz, isDirty=false, and fresh editor state showed Play=false. Native capture `static-ui-after-reopen.png` showed all existing labels and the new top-right camera button/status. Root personally viewed it. Durable image: `phase2-camera-controls-static.png`; PNG signature, 2028×1607 dimensions, source timestamps/hashes and scope are in `phase2-camera-controls-visual.json`.
- The latest Full HD Off/preparation state passed both static reviews. All seven panel refs survived reopening; read_console at 06:31:39 UTC returned zero errors. This does not establish why the earlier Overlay disappeared or turn the four failed raycast tests green.
- Responsive render checks, real InputSystem mouse/keyboard delivery, webcam readiness, failure/device handling, on/off and Play-exit process release remain pending. They cannot be inferred from probe tests, one static frame or another scene.
- User steps are in docs/05 §7-3 CAM-01–08. Play/camera activation stays with the user. No Step3 approval or completion is implied.

## Debug cleanup

The investigation was limited to one test-fixture activation and removal of a redundant external-packet latch, both covered by RED→GREEN evidence above. No production debug instrumentation, debugger attachment, or new socket/service was created. The temporary journal is removed after promoting these observations here.
