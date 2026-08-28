# Step 2 code-quality review

## Current re-audit: 2026-08-28T14:20:05+09:00

**Static disposition: R1 and R2 are closed by the current source changes. Runtime verification remains pending.** No new actionable regression was identified in this bounded lifecycle/release re-audit. This is not a runtime PASS or approval of the entire Step 2 deliverable.

The router, base interactable, button adapter, support probe, relevant regression tests, and assembly boundaries were read again from the current working tree. Only this report was edited. No Unity session, tests, Play, execute_code, source edits, or scene edits were performed. The parent reported that Unity was restarted and MCP disconnected before the updated GREEN run; no post-fix GREEN evidence was provided to this re-audit.

### R1 closure: target lifetime is now part of hover/capture identity

- [HandInteractable.cs:13](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInteractable.cs:13) adds a lifecycle revision incremented by OnDisable, and [HandButtonInteractable.cs:81](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandButtonInteractable.cs:81) calls the base callback before resetting its interaction state.
- [HandInputRouter.cs:317](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs:317) rejects old capture/hover revisions even if the target is available again. The same-target hover shortcut checks the revision at line 379; new captures store it at line 291. CancelHand clears state and rearms the hand without delivering old-lifetime events to the re-enabled target.
- The original disable/re-enable-between-ticks trace now revokes capture before the held sample, sends a fresh hover entry, and cannot call Release on the old press. [HandInteractionTests.cs:483](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/HandInteractionTests.cs:483) exercises that exact boundary and checks hover recovery, no click cooldown, and no armed hand.

### R2 closure: selection is guarded and confirmation is explicit

- [HandButtonInteractable.cs:185](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandButtonInteractable.cs:185) snapshots the lifetime, EventSystem, target GameObject, InputField, and press context. It suppresses automatic InputField activation, performs selection separately from native pointer-down, and checks lifetime/availability/current selection after callback boundaries before further native forwarding or explicit activation.
- [HandButtonInteractable.cs:265](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandButtonInteractable.cs:265) aborts without touching a destroyed adapter and clears the canceled selected object after EventSystem's selection guard unwinds. The original InputField setting is restored in finally when the field still exists. The snapshots remove the original post-destruction target lookup.
- Release now returns whether the action was accepted. [HandInputRouter.cs:447](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs:447) rolls back a rejected release's tentative cooldown, cancels unchanged hand state, and exits before success audio. Confirmed release still leaves its reservation in place while callbacks run, preserving the intended reentrancy protection. The support probe returns acceptance under the updated contract; all current Release overrides/callers were checked.
- [HandInteractionTests.cs:507](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/HandInteractionTests.cs:507) covers rejection during native Button selection. The InputField callback cases starting at [HandInteractionTests.cs:1020](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/HandInteractionTests.cs:1020) cover disable, immediate re-enable, target destruction, selected-object cleanup, queued activation, and temporary setting restoration. The successful explicit-activation case remains covered at line 1080.

### Evidence and remaining verification

This re-audit inspected the preserved [lifecycle RED XML](C:/Users/yunji/AppData/Local/Temp/CameraCoop-Step2-20260828/lifecycle-red-TestResults.xml): **377 total, 368 passed, 9 failed** (2026-08-28 05:12:15Z to 05:12:20Z). Its five lifecycle failures reproduce the historical findings: lost hover, unconfirmed cooldown, queued InputField activation, a click after re-enable during selection, and a destroyed-InputField MissingReferenceException. The other four failures are the already known native GraphicRaycast tests. XML SHA-256: `657436ccaac8525c08e8dfb60e8f466ac198341fe2008cfa16dbbe48c29b9760`.

The RED result is evidence for the old defects, not the current fix. The parent still needs the current lifecycle cases and full EditMode suite to run after Unity reconnects. The static acceptance branch excludes rejected-click audio, but actual playback and the native UI lifecycle still require runtime verification. The earlier unpruned-cooldown-key residual risk is unchanged and is not promoted into a new blocker for the fixed three-button panel.

### Current SHA-256 snapshot

Captured at the re-audit timestamp above; the historical snapshot is preserved below. Related unchanged/dependency files are included to identify the working-tree state, not to claim a new full review outside R1/R2.

| Source | SHA-256 |
|---|---|
| [HandInputRouter.cs](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs) | 65e452d2e8baeaba428b3daeeb930084274d0e02f2d9b8a65290379c62499801 |
| [HandInteractable.cs](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInteractable.cs) | 5817482725b7f3d2e06b93d981940c86a3908258639f1d7f824b3dc974abf9bc |
| [HandButtonInteractable.cs](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandButtonInteractable.cs) | beb8517e7ad282e2d223cffaca8f1dbbc619e19714ff2a2d9a0fd8ac42407b39 |
| [HandUiTestPanel.cs](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandUiTestPanel.cs) | 95c62edfd6146ddc2c1bc38fdf028e64cf4266298724f3586236cd3c41d10d19 |
| [HandInteractionTests.cs](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/HandInteractionTests.cs) | 1411955808897f8367820763b654aa19be0c413f043d3f32d9b6db297748c570 |
| [HandInteractionProbe.cs](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/Support/HandInteractionProbe.cs) | 4971ca880a12d719d62e7129f06d9e6dbcaeda4c870c677ad57b0216bc0cbab5 |
| [CameraCoop.Tests.Support.asmdef](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/Support/CameraCoop.Tests.Support.asmdef) | c6837be38e597d9e20f01444542fe8caf3e6328e800dd1dc4b8d76b8f76dcc55 |
| [CameraCoop.Tests.EditMode.asmdef](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/CameraCoop.Tests.EditMode.asmdef) | 356bedd1948e49b3283427d714c3221fcc0b39347850756a1e63beade48ae4ca |
| [CameraCoop.Runtime.asmdef](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/CameraCoop.Runtime.asmdef) | a69f6e1839fb886bba9687827c59bbd72f57017565168987861f4bf7915f3b59 |
| [InputModeManager.cs](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/InputModeManager.cs) | 6f5a56b09f520059a728053951bc7cfd23dd521b5bb6d13749e4b51e3d824b9d |
| [HandCursorController.cs](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandCursorController.cs) | 8d316f7a7cfbdb21184ef8ec6ff78d6467aea2d77bee8410c3b019bb89d3914b |
| [HandInputTypes.cs](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputTypes.cs) | d8dac2663a200939b9f5f7be871c34b9e649f98508b6745e30076f32d9275f1e |
| [07_hand_interaction.md](C:/git/Camera_co-op/docs/07_hand_interaction.md) | dc1250298108cce10bb8f65b0761000f8c588a94352c5a5ef75f2c7bb82206c0 |

## Original review (historical)

Reviewed: 2026-08-28T14:02:25+09:00. Original verdict: **REQUEST CHANGES — two P2 findings**. The current static disposition above supersedes this verdict; the original traces and hashes remain for comparison.

This was a read-only source review of HandInputRouter, HandInteractable, HandButtonInteractable, HandUiTestPanel, HandInteractionTests, the support probe and assembly definitions. The docs/07 hand-interaction contract, InputModeManager, HandCursorController sample delivery, and installed uGUI selection implementation were read as dependencies. Only this report was written. No Unity execution, Play, execute_code, scene edits, package changes, or commits were performed.

The findings below are source traces with concrete regression scenarios, not claims of runtime reproduction. The parent owns execution. After the parent clarified the Temp directory, this review read `C:/Users/yunji/AppData/Local/Temp/CameraCoop-Step2-20260828/first-full-TestResults.xml` (363/368 passed, five failures) and `C:/Users/yunji/AppData/Local/Temp/CameraCoop-Step2-20260828/raycast-diagnostics-TestResults.xml` (48/52 passed, four GraphicRaycast failures). Known GraphicRaycast fixture / disabled-color recovery work is not repeated as a finding. The parent subsequently reported the adapter subset green; this review did not independently execute it.

## Findings

### R1 — P2: A target disable/re-enable between router ticks preserves stale hover and capture

Primary location: [HandInputRouter.cs:304](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs:304), with the unchanged-target shortcut at [HandInputRouter.cs:366](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs:366) and reset at [HandButtonInteractable.cs:81](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandButtonInteractable.cs:81).

The adapter clears `hoveringHands`, `hasPress`, and its native pointer state in OnDisable, but the router only notices unavailability when it next polls `IsAvailable`. If the adapter or its panel is disabled and enabled again before the next Tick/sample, the router sees an available target and retains its old hover/capture. UpdateHover then returns early because the reference is unchanged, so it never restores the adapter's hover state. A held capture also remains in the router even though the adapter has forgotten its press. The next release is routed to that stale capture and can produce the router's click sound despite no OnHandClick.

Repro / missing regression:

1. Route fresh open samples at t=0 and t=0.11 over a real adapter, then down at t=0.12.
2. Set `adapter.enabled=false`, then `adapter.enabled=true` without calling router.Tick/ProcessSample between them. The equivalent panel SetActive(false)/SetActive(true) has the same issue.
3. Send a new held sample and then a normal up while still over the adapter.
4. Verify that the old capture was canceled, the hand must rearm, no success feedback is emitted, and subsequent hover over the available adapter is represented correctly. The current code retains the router capture and loses adapter hover until the hand leaves/re-enters.

The existing `DisabledAdapter_ClearsCaptureWithoutClick` test calls the adapter directly; `DisableAndReenable_CannotRestoreAnOldCapture` toggles the router, not a target. Neither covers this split ownership. Give target lifetime changes an observable invalidation signal/version and revoke the router's associated hover/capture even if the target is available again by the next sample.

### R2 — P2: Selection callbacks can cancel an adapter but release still forwards native input and success feedback

Primary location: [HandButtonInteractable.cs:201](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandButtonInteractable.cs:201). Related confirmation side effects: [HandInputRouter.cs:414](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs:414), especially the unconditional success audio at [HandInputRouter.cs:419](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs:419).

InputField.OnPointerDown synchronously selects the field. A real EventTrigger Select handler can therefore disable the HandButtonInteractable during this call. Release still invokes native pointer-up, SetSelectedGameObject, and ActivateInputField (lines 202-207) before rechecking availability at line 209. OnDisable also skips its selection cleanup because EventSystem is already selecting (line 84), with no deferred cleanup once that guard unwinds. If only the adapter was disabled, the still-active InputField can remain selected and queued for keyboard activation even though the hand interaction was canceled. Destroying the target in that callback also leaves the subsequent `target.gameObject` access at line 204 unguarded. This violates the no-more-events-to-disabled/ended-target contract.

The late availability check suppresses OnHandClick, but ReleaseCapture decides `confirmsClick` only from `!target.IsCanvas`, writes the cooldown before Release, and plays the success clip after it returns. Thus a selection callback that disables the adapter/panel also produces success sound and consumes cooldown for an unconfirmed click.

Repro / missing regression:

1. Add an EventTrigger Select callback on a real InputField target that disables its HandButtonInteractable while leaving the native InputField active.
2. Arm, down, and up through the router with the target available before up.
3. Selection runs the callback, OnDisable resets the adapter but skips deselection under the selection guard, and Release continues native pointer-up/activation before returning with zero OnHandClick.
4. Verify no native events/activation after cancellation, no canceled field left selected, zero success audio, and no confirmed-click cooldown entry. Add a target-destruction variant to protect the subsequent dereference. A Button Select callback that disables its adapter is a smaller independent repro for the false-success-audio path.

Installed uGUI confirms the native selection boundary at [InputField.cs:1777](C:/git/Camera_co-op/Library/PackageCache/com.unity.ugui@23e8d17bfaf9/Runtime/UGUI/UI/Core/InputField.cs:1777) and synchronous callbacks at [EventSystem.cs:174](C:/git/Camera_co-op/Library/PackageCache/com.unity.ugui@23e8d17bfaf9/Runtime/UGUI/EventSystem/EventSystem.cs:174). The current tests only exercise availability changes before Release, so they miss this callback boundary. Revalidate the interaction after callbacks and clear aborted selection once the guard unwinds; source success audio/cooldown from an actual confirmation result/event rather than the release attempt. This is independent of R1: it occurs even when the target stays active until the release selection callback.

## Other reviewed boundaries

- Sample deduplication, same-ID terminal cancellation, timeout without events, open-sample rearm, per-target exclusive ownership, and release-triggered generation cancellation have dedicated tests and consistent source paths. This is not a substitute for the parent's current test run.
- UI target resolution preserves blocking when the top graphic is not available as an input target; the known GraphicRaycast fixture issue remains with the parent.
- HandUiTestPanel subscription/unsubscription is symmetric for its Inspector-fixed references. No native Button.onClick / pointerClick dispatch is added by the adapter.
- The support assembly has `autoReferenced=false` and a `UNITY_INCLUDE_TESTS` constraint. The EditMode consumer is Editor-only with the same constraint; ProjectSettings has no custom scripting define for that symbol. Static configuration excludes support from ordinary player compilation; no build was executed here.
- HandPointer / online routing and CameraCoop.Runtime.asmdef have no working-tree diff. Step 1 source was read only as a dependency, not reimplemented or edited.
- Residual lifecycle/performance risk: `lastClicks` never prunes destroyed targets, so recreating buttons under one long-lived router retains dictionary keys. The current fixed three-button panel does not grow this collection; dynamic-panel stress remains outside this report's blocking findings. Raycast list/pointer data are reused; no large per-frame allocation issue was established.

## SHA-256 snapshot

Paths below are relative to `C:/git/Camera_co-op/`; hashes were captured at the review timestamp. They matched the previously read snapshot.

| Source | SHA-256 |
|---|---|
| Assets/_CameraCoop/Scripts/Input/HandInputRouter.cs | 1DBBB7252B85F7B2B472C4CB6D9DCFA729D836CF4BE51D012C11966BC0E9DF3D |
| Assets/_CameraCoop/Scripts/Input/HandInteractable.cs | 713C15298DC9D4D33BC51FE7F59D032E2C67670BE7C16C02C0D9A84B150F0729 |
| Assets/_CameraCoop/Scripts/Input/HandButtonInteractable.cs | 3101B9AE1D02D1D6AD50FAE70F01E20DFBF37E50225304CCA205D243C933E38E |
| Assets/_CameraCoop/Scripts/Input/HandUiTestPanel.cs | 95C62EDFD6146DDC2C1BC38FDF028E64CF4266298724F3586236CD3C41D10D19 |
| Assets/_CameraCoop/Tests/EditMode/HandInteractionTests.cs | AC8ACFB06A088252091F656661FA79F8505FDCEE14776077AD93D10281F62916 |
| Assets/_CameraCoop/Tests/Support/HandInteractionProbe.cs | CB5D2A2036C88F2618D06A3BA4F8815D2636AA2104EA2ADE3A5EA05E433F4E59 |
| Assets/_CameraCoop/Tests/Support/CameraCoop.Tests.Support.asmdef | C6837BE38E597D9E20F01444542FE8CAF3E6328E800DD1DC4B8D76B8F76DCC55 |
| Assets/_CameraCoop/Tests/EditMode/CameraCoop.Tests.EditMode.asmdef | 356BEDD1948E49B3283427D714C3221FCC0B39347850756A1E63BEADE48AE4CA |
| Assets/_CameraCoop/Scripts/CameraCoop.Runtime.asmdef | A69F6E1839FB886BBA9687827C59BBD72F57017565168987861F4BF7915F3B59 |
| Assets/_CameraCoop/Scripts/Input/InputModeManager.cs | 6F5A56B09F520059A728053951BC7CFD23DD521B5BB6D13749E4B51E3D824B9D |
| Assets/_CameraCoop/Scripts/Input/HandCursorController.cs | 90B4E1B6638BF0C2FD92DC7C8382C4566E9C8481A4CE3C911FEB90670DE90BFF |
| Assets/_CameraCoop/Scripts/Input/HandInputTypes.cs | D8DAC2663A200939B9F5F7BE871C34B9E649F98508B6745E30076F32D9275F1E |
| docs/07_hand_interaction.md | A2F56BC9F76CAC08EEA71F6F28E87A102146ACC7B2FB4166D3CA417A07E0334A |
