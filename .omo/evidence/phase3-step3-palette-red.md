# Step 3 palette/slider RED readiness

- Scope owner: `ToolState.cs`, `HandSliderInteractable.cs`, `HandToolPalette.cs`, and `ToolStateTests.cs` only.
- Scenario: the public ToolState selection/brush contract. Invocation: `PaletteReadOnlyApi_ReportsSelectionsAndBrushMaterialLookup`. Observable: missing public properties or material lookup fail through reflection; no type reference prevents the test assembly compiling first.
- Scenario: hand slider clamps a left-hand down/hold to width indices 0 and 2, then follows an external ToolState width update. Invocation: `Slider_DownAndHoldClampWidthAndExternalToolChangeSynchronizesNativeValue`. Observable: the currently missing `CameraCoop.HandSliderInteractable` type fails the required-runtime-type assertion before production code exists.
- Scenario: overlay palette click applies one ToolButton and ToolState.OnChanged refreshes its selected graphics. Invocation: `Palette_HandClickAppliesOnceAndExternalToolChangesRefreshSelectedMarkers`. Observable: the currently missing `CameraCoop.HandToolPalette` type fails the required-runtime-type assertion before production code exists.

No Unity test runner was invoked: the task owner explicitly delegated execution to the parent and required a READY FOR RED handoff before production implementation.
