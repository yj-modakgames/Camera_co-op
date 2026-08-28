# Camera controls — static integrity review (Pass A)

- VERDICT: **PASS**
- recommendation: **APPROVE for this captured static state only**
- CONFIDENCE: **HIGH** for visible structure and saved scene correspondence; no runtime verdict.
- blockers: []

## Original intent and desired outcome

Expose the missing camera start/stop control at the top right, with clear connection status, using existing Unity uGUI and Nanum typography. Camera controls alone receive the approved mouse exception; A/B/C remain hand-only. Initial preparation must not autostart the camera. The current review concerns only the visible Off/preparation surface, not execution of these input/lifecycle contracts.

## Scope and user outcome review

Directly opened `.omo/evidence/phase2-camera-controls-static.png` with view_image. This is the 2028×1607 native Unity PrintWindow capture identified by metadata as 2026-08-28 06:29:48.254 UTC, showing RelayQuiz, EditMode, Full HD (1920×1080) Game view. The viewer scaled its display to 1776×1408; the source artifact remains 2028×1607.

The visible top-right button reads `캠 켜기`; the separate status reads `꺼짐` and `버튼을 눌러 카메라를 켜세요`. The preparation label remains at upper left and the original hand instructions and A/B/C panel remain readable. No visible text clipping, overlap, missing glyphs, or blocked camera-button surface was found. The button is visually distinct from its surrounding status container. This satisfies the requested static discoverability outcome without a new design system or screenshot-clone requirement.

## Direct integrity checks

| Criterion | Evidence and assessment |
|---|---|
| Native structure, no raster fake | RelayQuiz.unity:3000–3102 defines CameraToggle with RectTransform, native Button, Image, CanvasRenderer and a separate label. CameraPanel:3821–3884 is a native Image with null sprite and separate button/status children. No screenshot is substituted for the reviewed control. |
| Reference-resolution layout | Scene:3843–3847 gives top-right anchors/pivot, position (-24,-24), size 384×168. Button:3021–3025 is 336×56, top inset 16. Status:3215–3218 is 344×66 at y=-88. Overlay scaler:4678 references 1920×1080. These correspond to the visible right-top spacing; other sizes are untested. |
| Existing type/palette | Label:781–782 and status:3241–3242 use shared font GUID a69e6e17347686b4e85869360f390107, resolved through NanumGothic-Regular.ttf.meta, at 26 and 20. Dark blue button and muted cool status colors are consistent with the existing hand-test UI. |
| Alpha/compositing | Panel Image alpha is 0.82 (3861); button and text are opaque. The capture visibly composites panel translucency over the scene. No unexpected opaque replacement or transparency artifact is apparent. PNG alpha-channel preservation itself is not claimed from this desktop composite. |
| Source/state correspondence | CameraControlPanel.cs Off presentation matches the visible camera copy. Its seven serialized references at scene:1036–1042 are nonzero. Scene Button navigation is None and native onClick is empty (3038–3069), consistent with the separately implemented camera-only pointer path. Actual input delivery is not proven. |
| Evidence freshness | Independently computed SHA256 for RelayQuiz.unity, CameraControlPanel.cs, InputModeManager.cs and TrackerLauncher.cs; all four match phase2-camera-controls-visual.json. Metadata places capture after listed saved-file modifications. This establishes artifact consistency, not an independent inspection of live editor memory. |

## Findings

- [product] None blocking this static state's criteria.
- [evidence] NOTE — the native screenshot contains no reliable proof of runtime cursor visibility, mouse/keyboard routing, status transitions, device readiness or process ownership. Keep those checks pending; do not promote this PASS into an interaction/hardware PASS.
- [evidence] NOTE — earlier text/Overlay disappearance remains unresolved. The current image contains the Overlay and text, so earlier deficient captures do not invalidate this specific capture or demonstrate that their underlying cause was fixed.
- [evidence] NOTE — docs/07 §11 and verification.md still describe the pre-recapture visual gate as pending. This report supplies the later static Pass A only; it does not supersede their dynamic limitations.

## Programming / remove-ai-slops perspective

Consulted both skills and directly inspected CameraControlPanel production implementation and CameraControlTests behavioral cases. No needless parsing/normalization, raster imitation, new visual framework, or speculative production extraction is needed for the visible UI. The panel uses Inspector references and existing uGUI primitives. The reviewed tests exercise click boundaries, startup ownership, fresh packets, failure persistence and mode restoration, not just removals. Reflection/private-name coupling and some exact display-copy assertions can create maintenance burden; they do not prove real input delivery. The unrelated-button zero-click assertion is narrower than a real EventSystem regression because the test drives the panel pointer seam, not global input. This is a NOTE, not evidence that the captured static layout fails.

The inspected code-review report explicitly covers programming/remove-ai-slops, deletion-only and tautological tests, needless parsing/normalization, reflection coupling, and panel size. Its 266-pure-line observation is nonblocking for this bounded static review. Its test success prose was not used as proof of visible UI or runtime behavior. No tests or production files were changed or executed during this review.

## Checked artifact paths

- `.omo/evidence/phase2-camera-controls-static.png`
- `.omo/evidence/phase2-camera-controls-visual.json`
- `.omo/evidence/phase2-camera-controls-verification.md`
- `.omo/evidence/phase2-camera-controls-code-review.md`
- `Assets/_CameraCoop/Scenes/RelayQuiz.unity`
- `Assets/_CameraCoop/Scripts/Input/CameraControlPanel.cs`
- `Assets/_CameraCoop/Scripts/Input/InputModeManager.cs` (targeted cursor/preparation inspection and hash)
- `Assets/_CameraCoop/Scripts/Input/TrackerLauncher.cs` (targeted startup inspection and hash)
- `Assets/_CameraCoop/Tests/EditMode/CameraControlTests.cs` (behavioral-case inspection)
- `Assets/_CameraCoop/Fonts/NanumGothic-Regular.ttf.meta`
- `docs/07_hand_interaction.md` §10–11

## Exact remaining gaps

No new screenshots for other resolutions; no runtime Starting/Receiving/External/Failed, hover or pressed captures; no actual mouse, Enter/Space, A/B/C hand-only, initial cursor or mode-restoration execution; no webcam/device-failure/on-off/Play-exit release evidence reviewed. All remain user-owned and pending, outside this static PASS. The known four native GraphicRaycaster failures were not rerun or declared fixed. No Unity MCP, Play action, camera/process startup, scene mutation, or product edit was performed.
