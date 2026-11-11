# 2026-11-12-story-orbit-smooth-motion

- Status: complete
- Owner/agent: Codex root; orbit_reference_review owns read-only review and external native diagnostics.
- Date: 2026-11-12
- Repository and branch: Mizon.Windows, codex/story-orbit-smooth-motion
- Base commit: 32852b5d2c49a1d0af2ce6a468bab631ab7be056
- Related task: 2026-11-12-story-orbit-pointer-motion; no backend/SDK changes.
- Owned files: Common/StoryOrbitMotion.cs, this note and docs/ai/HANDOFF.md.

## Request and acceptance criteria

The user reported that the new collage mouse animations stutter. Remove the
per-sample animation churn while preserving every layer's independent motion,
reference appearance, idle animation and resource lifecycle. Fast reversal,
exit/re-entry and recovery after resize/deactivation must not leave stuck hover.

## Implementation and decisions

- The previous handler queued low-priority UI callbacks and restarted eight
  cubic keyframe animations for each processed mouse sample. Preserving the
  current position did not preserve velocity. This is a concrete source of
  unnecessary work and discontinuous motion; it is not a measured GPU diagnosis.
- Use WinUI's GetPointerPositionPropertySet on the stationary input canvas.
  One shared expression normalizes/clamps native coordinates, including Viewbox
  scaling, and retained per-layer expressions consume this compositor input.
- Ordinary mouse movement starts **zero** keyframe animations and queues **zero**
  callbacks. The recovery-only PointerMoved handler returns on one managed bool
  while hover is active; it re-arms once after lifecycle/coordinate resets.
- Each layer eases entry from its actual animated Pointer value to the live
  source with Lerp(this.StartingValue, live, Blend). Once Blend reaches one,
  tracking stays native. Exit replaces tracking with a cached return-to-zero
  animation starting at the displayed pose. This also handles re-entering from
  another edge during an unfinished exit without resetting position.
- A zero native position retains the last direction instead of jumping to the
  top-left. Entry/return clocks explicitly leave their current values when
  interrupted. No autonomous expression feedback loop is used for smoothing.
- No per-move timer, layout mutation, image/shadow/geometry changes or package.
  Existing response durations now govern entry/exit rather than repeated
  mouse-sample retargets. All travel, rotation, scale and idle settings remain.
- Release per-layer expressions/clocks before releasing the shared owned state.
  The pointer property set is borrowed from XAML: retain but never Dispose it.
  Reduced motion releases the source; enabling motion recreates it. Unload,
  viewport, minimize, window activity and invalid-coordinate resets remain.
- Cosmetic motion reuses the music-home MCP contract and existing checks; it
  does not create a new permission or synthetic hover tool.

Official references:
[pointer input](https://learn.microsoft.com/en-us/windows/apps/develop/composition/pointer-input-animations),
[WinUI pointer property set](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.hosting.elementcompositionpreview.getpointerpositionpropertyset),
[expression keywords](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.expressionanimation).

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| Source-linked native Composition diagnostic against WindowsAppSDK 2.5.4-experimental | Passed: build 0 warnings/errors; runtime exit 0; 370 assertions across 18 lifecycle passes | Nine profiles, including 72 synthetic pointer updates (18 zero-position samples), unchanged clocks during ordinary updates/repeated hover, rapid re-entry, immediate reset, source recreation and rest-pose cleanup. No Window or user-app interaction. |
| VS 18 Insiders MSBuild Mizon.Windows.csproj /t:Build /m:2 /p:Configuration=Debug /p:Platform=x64 /p:GenerateAppxPackageOnBuild=false /p:AppxPackageSigningEnabled=false | Passed, exit 0 | Existing preview/nullable/XAML warnings remain; not deployment/full-app NativeAOT publish. |
| dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release --no-build -- --isolated | Passed: 47/47 groups | Existing unchanged MCP binaries; synthetic protocol/schema/permissions/failure/cancellation/lifecycle coverage. |
| dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release --no-build --no-launch-profile | Passed: 276 assertions | Existing unchanged MCP adapter binaries; synthetic consent and isolated pipes/store. |

Diagnostics remain outside the repository under
`D:/MizonTaskDiagnostics/Presence/verification/`.
No Windows UI automation or user-app/Visual Studio restart.

## Handoff

Visible frame pacing and GPU/frame-time measurements still require a real
interactive run; native API checks alone cannot establish those results.
The removal of per-move animation starts is established from the code path.

## Publication

Integrate only reviewed task-owned files into clean main, push normally and
verify remote SHA. The containing commit is identified by git log for this file.
