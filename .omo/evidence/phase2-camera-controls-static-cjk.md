# Camera controls — static visual QA Pass B

VERDICT: PASS
CONFIDENCE: HIGH for the single captured state
recommendation: APPROVE (static Off/preparation appearance only)
blockers: []

## Original intent and desired outcome

`docs/07_hand_interaction.md:206-232` requests a visible top-right camera start/stop control and connection status, with camera activation reserved for the user's click. This pass checks the visual affordance and Korean typography in the supplied RelayQuiz EditMode Off/preparation frame; it does not approve runtime behavior.

## Directly checked artifacts

- `.omo/evidence/phase2-camera-controls-static.png`: opened directly with view_image, including original 2028×1607 resolution. Full HD 1920×1080 Game view is inside Unity editor chrome; the screenshot is not a 2028×1607 game viewport.
- `.omo/evidence/phase2-camera-controls-visual.json`: capture time 2026-08-28T06:29:48.254Z, dimensions, surface, capture path, all four source entries, and all three remaining-coverage entries read. Independently recomputed SHA256 for every listed source: all match. Listed source modification times precede capture time.
- `Assets/_CameraCoop/Scripts/Input/CameraControlPanel.cs`: Off display copy and visual colors inspected.
- `Assets/_CameraCoop/Scenes/RelayQuiz.unity`: camera button/status references, panel anchors/dimensions, status typography/color, and panel alpha inspected.
- `docs/07_hand_interaction.md`, sections 10–11.

## User outcome review

The requested camera affordance is clearly visible at the top right. `캠 켜기` is centered on an opaque dark-blue rectangular button. Below it, `꺼짐` and `버튼을 눌러 카메라를 켜세요` are readable as two intentional lines. All Korean syllables render normally; no tofu, clipped glyphs, ellipsis, orphan syllable, or awkward wrap is visible. The full instruction remains on one line.

At native screenshot coordinates, the game frame begins near (54,341). The camera panel occupies approximately (1566,365)–(1950,533), consistent with the scene's 384×168 panel and 24-pixel top/right margins. The button occupies (1590,381)–(1926,437), consistent with 336×56 and balanced horizontal inset. The status has comfortable space beneath the button and above the panel bottom.

The top-center tracking panel ends near x=1274, leaving about 292 pixels before the camera panel. The left `캠 준비 · 이동 잠금` panel and the central A/B/C panel do not overlap the new control. Their Korean labels are also intact and readable. There is no claim of baseline pixel equivalence because no exact reference screenshot is supplied.

Bright button text contrasts clearly with its dark-blue fill. The quieter gray-blue status copy remains readable over the dark translucent panel, including where the world background changes from light board to blue wall. The visible background variation is consistent with the panel Image alpha 0.82; it is not a missing compositor region. This is a visual readability observation, not a measured accessibility contrast certification.

## Findings

- [product] None within this captured static state.
- [evidence] NOTE: only one state and resolution were captured. Starting, Receiving, External, Failed/retry, hover, pressed, disabled, mode transitions, camera hardware, Play exit cleanup, and other aspect ratios/resolutions remain unverified. No PASS or FAIL is inferred for them.
- [evidence] NOTE: metadata is a capture/source record, not image-diff output; diffRatio, similarityScore, alphaChannelIntact and hotspots are absent. No exact-pixel reference was requested, so there are no reference-diff hotspots to adjudicate.
- [evidence] NOTE: section 11's earlier “final screen verification pending” record describes preceding defective captures. This report evaluates only the subsequently captured PNG named above, not those earlier frames.

## Scope of review

Applied the visual-qa focused CJK and layout criteria. Programming/remove-ai-slops criteria were consulted, but this is not the full implementation/test gate: no claim is made that a test overfit audit, runtime verification, or code-review-report reconciliation was completed here. No product files, scenes, Play state, camera state, or Unity MCP actions were changed.

BLOCKING: none for the one enumerated static state.
