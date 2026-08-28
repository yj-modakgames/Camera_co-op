# Camera controls code-quality review — 2026-08-28

## Verdict

- `codeQualityStatus`: **WATCH**
- `recommendation`: **APPROVE**
- `blockers`: none.

This was a read-only source and artifact review. No Unity MCP, Play-mode action, camera/process startup, scene change, product/test edit, or commit was performed.

## Scope and evidence inspected

- `Assets/_CameraCoop/Scripts/Input/CameraControlPanel.cs`
- `Assets/_CameraCoop/Scripts/Input/InputModeManager.cs`
- `Assets/_CameraCoop/Scripts/Input/TrackerLauncher.cs`
- `Assets/_CameraCoop/Tests/EditMode/CameraControlTests.cs`
- `Assets/_CameraCoop/Tests/EditMode/InputModeTests.cs`
- `Assets/_CameraCoop/Tests/EditMode/TrackerLauncherTests.cs`
- `Assets/_CameraCoop/Tests/Support/TrackerLauncherProbe.cs`
- Approved contract: `docs/07_hand_interaction.md` §10, especially lines 220–228.
- Targeted test artifact: `C:/Users/yunji/AppData/Local/Temp/CameraCoop-CameraControls-20260828/targeted-green-TestResults.xml`.

The XML is internally consistent: `result=Passed`, `total=85`, `passed=85`, `failed=0`; it includes CameraControlTests 23/23, InputModeTests 44/44, and TrackerLauncherTests 18/18. This does not cover the known four native GraphicRaycaster failures or user-owned Play/hardware validation.

`omo ulw-loop status --json` was unavailable in this shell (it returned a command syntax error), so this report uses the task-provided fallback path rather than an attempt directory.

## Findings

### CRITICAL

None.

### HIGH

None.

#### Retracted finding

The first review read `Off` and persistent retry `Failed` as optional-camera gameplay states. The clarified, user-approved bootstrap contract instead defines them as part of the initial camera-preparation surface: it must expose a mouse pointer and force Interact until actual fresh UDP connection, without starting the camera automatically. Under that contract, [CameraControlPanel.cs:205–206](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/CameraControlPanel.cs:205) is intentional. It preserves `InputModeManager`'s stored context and requested mode; [InputModeManager.cs:38](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/InputModeManager.cs:38) merely overlays effective Interact during bootstrap, and Receiving/External clear preparation and restore the stored policy. The initial-state assertions in [CameraControlTests.cs:87–96](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/CameraControlTests.cs:87) therefore exercise the required bootstrap behavior rather than conceal a defect.

### MEDIUM

1. **The new panel is above the generic pure-line threshold, but remains cohesive enough to defer splitting.**

   - `CameraControlPanel.cs` measures 266 pure lines (nonblank, non-comment), exceeding the `programming` / `remove-ai-slops` 250-line guard.
   - The file contains connection state/ownership, pointer hit testing and pressed-state rendering, UI presentation, reference validation, and lifecycle subscription/cleanup ([CameraControlPanel.cs:45–285](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/CameraControlPanel.cs:45)).
   - This is a nonblocking observation only. The panel owns one bootstrap-control state machine; extracting code solely to satisfy a generic threshold would risk the needless abstraction that the same skill perspective prohibits. Do not expand this task to split it.

2. **Tests use Unity-specific reflection seams and will be sensitive to private-name refactors.**

   - [CameraControlTests.cs:31–70](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/CameraControlTests.cs:31) discovers the production type dynamically and injects private fields by name. [CameraControlTests.cs:326–374](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/CameraControlTests.cs:326) invokes private methods and writes receiver backing fields. [InputModeTests.cs:472–491](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/InputModeTests.cs:472) reflects public methods and private Unity callbacks instead of calling the public seam directly.
   - These tests cover meaningful lifecycle and state behavior; they are not deletion-only or tautological. But field/method name strings and enum-name assertions make benign internal refactors fail without proving a user-visible regression.
   - This is nonblocking in the established Unity test style: reflection is used to drive serialized Inspector references and private Unity lifecycle callbacks without launching a real tracker. No logic is deletion-only or tautological, and no change is requested for this task.

### LOW

None.

## Contract, ownership, and regression assessment

The source supports the bootstrap and lifecycle contracts: no automatic `StartTracker` call was found; initial Off/retry bootstrap forces Interact with a visible camera pointer while retaining stored context/requested mode; start is guarded while Starting; receiving requires a fresh UDP packet even with empty hands; cached packets are discarded after owned stop; new external packets can be adopted without a launch/stop; launcher cleanup is bounded to its owned process handle; and failure text is not overwritten by passive refresh. The targeted green artifact includes narrow tests for these paths.

I did not treat scene wiring, InputSystem action-reference configuration, keyboard routing in a real EventSystem, or camera/device shutdown as verified, because those require the in-progress scene work or user-owned Play/hardware evidence.

## Required skill-perspective check

The check **ran**: I read `omo:remove-ai-slops` and `omo:programming` before assessing test relevance and maintainability.

- `remove-ai-slops`: no deletion-only tests, mere-removal tests, tautologies, or unjustified production parsing/normalization were found in this scope. It notes the 266-pure-line panel measurement; cohesion and scope make that a deferred, nonblocking observation.
- `programming`: C# is outside this skill's mandatory language trigger. Its general quality perspective was nevertheless applied: no untyped escape hatches or needless production abstraction were added; the Unity reflection test seam is a nonblocking established-pattern tradeoff.

## Verification limitations

No full-suite run was represented as passing. The supplied targeted XML is evidence only for its 85 selected tests. C# LSP was unavailable by stated user choice; no replacement static-analysis result was supplied. `git diff --check` produced only existing line-ending warnings and no whitespace error.
