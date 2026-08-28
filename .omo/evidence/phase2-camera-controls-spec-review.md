# Camera controls specification review — 2026-08-28

## Verdict: PASS (source/spec review; R1 closed)

Read-only review against approved `docs/07_hand_interaction.md` §10 and `docs/superpowers/plans/2026-08-28-camera-controls.md`. Reviewed CameraControlPanel, InputModeManager, TrackerLauncher, HandInputRouter, the three camera/input/launcher EditMode suites, and TrackerLauncherProbe. No product, test, or design-document edits. No Unity MCP, Play, camera, or process launch; this report is source evidence, not a runtime pass.

## R1 re-review

**Closed: a stop/failure no longer permanently suppresses new external senders.** No remaining source/spec finding from this review.

- Freshly read [CameraControlPanel.cs:117](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/CameraControlPanel.cs:117): external adoption now depends on `HasFreshPacket()` without the permanent acceptance latch. `acceptExternalPackets` is absent from the file.
- [HasFreshPacket, line 180](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/CameraControlPanel.cs:180) still requires a non-null packet, a reference different from `discardedPacket`, and `!receiver.IsServerLost`. [DiscardCurrentPacket, line 193](C:/git/Camera_co-op/Assets/_CameraCoop/Scripts/Input/CameraControlPanel.cs:193) now saves only the cached reference. Thus the old cached packet cannot restore connection, while a new fresh external packet can.
- This satisfies the approved external adoption contract in [§10:226](C:/git/Camera_co-op/docs/07_hand_interaction.md:226) without adding any launch or stop call to external adoption.
- [CameraControlTests.cs:216](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/CameraControlTests.cs:216) adds `NewExternalPacketAfterOwnedStop_IsAdoptedWithoutAnotherLaunch`: after owned stop, a new packet must select External, preserve one start/one stop total, and restore hand UI permission. The separate [cached-packet regression at line 201](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/CameraControlTests.cs:201) remains unchanged.
- Parent reports the new regression's intended RED: 85 total, 84 passed, one failure (`Expected External but Off`). GREEN is being run by the parent and was not independently observed by this reviewer. PASS here is a source/spec verdict, not a test or hardware verdict.

Re-reviewed CameraControlPanel SHA-256: `1A56ADBCA8B7992AEB2FFD2CCE48FA6F942638B58B756CDA5E6152345B004F1A`. Initial rejected snapshot: `E01BB4AFDA3D36B83DEABB42B0621FCEE4E7102C2F18286685E4DEC5247AB447`.

## Other reviewed contracts

No additional source violation found: no automatic StartTracker invocation; camera rectangle receives only left press/release; preparation does not snapshot/overwrite the current context; Move, Interact, focus, and Blocked permissions are consistent; process running is not UDP readiness; empty hands can be receiving; duplicate starts are guarded; launcher labels retain errors; launch/quit cleanup is limited to owned process handles; CancelAll resets hand rearm on connection boundaries. Existing online OnClickToggle and optional legacy buttonLabel remain available.

## Verification boundaries

- Scene placement, explicit references, null launcher legacy label, camera Navigation.None/onClick, and all ten shared UI action references remain pending scene integration. They are not source defects in this review.
- The fixture now activates its rig at [CameraControlTests.cs:69](C:/git/Camera_co-op/Assets/_CameraCoop/Tests/EditMode/CameraControlTests.cs:69), while Receiver remains separately inactive and Launcher remains the process probe. Product `CanDeliver` permissions and SenderLoss assertions were not weakened. Parent reports SenderLoss passed in the regression RED run.
- CameraControlTests drives the internal pointer seam; it does not establish a real InputSystem Enter/Space or A/B/C bypass test. Actual keyboard routing, Play, camera start/stop, and post-Play device release remain unverified here.
- Existing Step 2 native GraphicRaycaster failures remain four unresolved failures, not waived or reclassified by this amendment.
