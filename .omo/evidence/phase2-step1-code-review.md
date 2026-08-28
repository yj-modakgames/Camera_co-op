# Phase 2 Step 1 code-quality review

## Scope and method

Reviewed the requested Step 1 sources and tests only:

- `Assets/_CameraCoop/Scripts/Input/InputModeManager.cs`
- `Assets/_CameraCoop/Scripts/Input/PlayerController.cs`
- `Assets/_CameraCoop/Tests/EditMode/InputModeTests.cs`
- `Assets/_CameraCoop/Tests/EditMode/PlayerMoveTests.cs`

Also compared `PlayerController.cs` to `HEAD`, read `InputFocus.cs`, `PlayerMoveLogic.cs`, both asmdefs, and the approved Step 1 contract in `docs/06_player_controller.md`. This was a read-only review. Unity and Play mode were not run by this reviewer.

Skill-perspective check: **ran**. I read and applied `omo:programming` and `omo:remove-ai-slops`. The programming reference has no C#-specific subreference, so its shared principles were applied. The production diff has no untyped escape hatch, unnecessary parsing/normalization, or needless abstraction under those perspectives. The test diff has the maintainability concerns recorded below.

## Findings

### CRITICAL

None.

### HIGH

None.

### MEDIUM

None.

### LOW

1. `InputModeTests.cs` (252 pure lines) and `PlayerMoveTests.cs` (320) remain over the remove-ai-slops size guideline. The owner explicitly accepted keeping the user-approved existing-suite layout, and the final cleanup removed reflection from public/enumerated/internal API paths. This is an accepted maintainability trade-off, not a correctness or approval blocker. Relevant files: `Assets/_CameraCoop/Tests/EditMode/InputModeTests.cs:12`, `Assets/_CameraCoop/Tests/EditMode/PlayerMoveTests.cs:13`.

## Correctness and scope notes

- The `Legacy = 0` default and the legacy `Step`/right-mouse look paths preserve the prior implementation's movement clamp and typing behavior.
- The Modal profile checks explicit scene references, disables local input on missing references, uses the existing yaw/normalization math with `CharacterController.Move`, and resets vertical velocity whenever modal permissions are absent. These align with the approved Step 1 contract.
- `InputModeManager` has a single permission calculation with forced Interact contexts, safe focus/cursor-unlock recovery, and an earlier execution order. The cursor policy matches the design table (Interact is unlocked but invisible; focus loss makes it visible).
- No deletion-only tests, implementation-constant-only tests, unnecessary production parsing, or production normalization beyond the required movement normalization were found. The HUD strings are an explicit user-facing requirement, so their assertions are not treated as brittle prompt tests. Final tests now call public/enumerated/internal contracts directly; remaining reflection is narrowly limited to private Unity Inspector fields and private lifecycle callbacks.

## Residual risks

- User Play verification remains pending by design; this review did not run Play mode.
- `C:/Users/yunji/AppData/Local/Temp/CameraCoop-Step1-20260828/final-TestResults.xml` records **287 total / 287 passed / 0 failed**, result `Passed`, at `2026-08-28 04:30:53Z`. The final manifest binds job `d7e89f61cc5e4a5bb7075924934f5d0c` to all four reviewed current SHA-256 values. I independently recomputed and matched those hashes. `2026-08-28T04-30-52-605Z-mcp.json` records a fresh forced refresh and a console read with zero errors; its sole warning is the pre-existing Unity pipeline automated-mode warning. The Editor was idle and not playing when the test job was started.

## Verdict

- `codeQualityStatus`: **CLEAR**
- `recommendation`: **APPROVE**
- `reportPath`: `C:/git/Camera_co-op/.omo/evidence/phase2-step1-code-review.md`
- `blockers`: none.
