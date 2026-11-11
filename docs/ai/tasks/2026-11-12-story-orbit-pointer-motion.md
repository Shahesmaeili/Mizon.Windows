# 2026-11-12-story-orbit-pointer-motion

- Status: complete
- Owner/agent: Codex root; orbit_reference_review performed read-only review/native diagnostics.
- Date: 2026-11-12
- Repository and branch: Mizon.Windows, codex/story-orbit-pointer-motion
- Base commit: 71307985c9e57b7b951296689849f381636411b9
- Related task: 2026-11-12-story-orbit-image-loading; no backend/SDK changes.
- Owned files: Common/StoryOrbitMotion.cs, UserControls/StoryOrbitCard.xaml and .xaml.cs, this note, docs/ai/HANDOFF.md.

## Request and acceptance criteria

Give the three editorial collages above the stories different smooth reactions
to mouse movement across their photo, frame, underlying shadows and typography.
Keep the reference-derived design, working images and gentle independent idle
motion. Exiting the surface returns pointer offsets to the authored pose.

## Implementation and decisions

- One stationary transparent input canvas per collage, with non-hit-testable
  decorative descendants. Animated text/photo edges cannot switch hover targets.
  Coordinates are relative to this surface, including ancestor Viewbox scaling.
- Artwork follows the pointer, its photo gently counter-moves within existing
  overscan, the contour moves in the opposite direction, and ambient/contact
  shadows follow with separate offsets and easing durations. Edition, headline
  and star use distinct translation, rotation, hover lift and modest scale.
- The existing Composition owner combines pointer input and idle phases in one
  expression per animated property. A cached low-priority dispatcher callback
  coalesces input; each layer reuses its animation/easing objects. Retargeting uses
  this.StartingValue so rapid reversal does not reset to the previous target.
  Shadow-only layers allocate no unused idle clocks. No layout mutations,
  UI-thread frame loop, package or image conversion was added.
- Exit/cancel/lost capture returns smoothly; deactivation, hiding, leaving the
  viewport and coordinate changes clear pending offsets. Windows reduced motion
  stops effects and restores the original pose. Unload unsubscribes input/root/
  window events and releases owned resources. Scale is now restored alongside
  rotation/translation/center. XAML-owned visuals/controllers are not disposed.
- Uses Microsoft.UI.Input.PointerDeviceType and source-generated LibraryImport
  for initial window activity. No reflection/dynamic XAML in production.
- This is decorative interaction, with no new business state/action or fake
  button/Narrator focus. Existing music-home MCP contracts and separate grants
  are reused, with isolated checks rather than artificial hover tools.

## Validation

| Command / environment | Result | Limits |
| --- | --- | --- |
| VS 18 Insiders MSBuild Mizon.Windows.csproj /t:Build /m:2 /p:Configuration=Debug /p:Platform=x64 /p:GenerateAppxPackageOnBuild=false /p:AppxPackageSigningEnabled=false | Passed, exit 0 | Existing preview/nullable/XAML warnings remain; no deployment or full-app NativeAOT publish. |
| External native diagnostic, source-linked StoryOrbitMotion, dotnet build OrbitNativeProbe.csproj -c Debug, then hidden executable | Passed: 0 build warnings/errors; 165 assertions across 18 lifecycle passes | Nine profiles include all eight production layers and pointer-only rotation. Validates native expression acceptance, retarget/reset, animation reuse, zero-idle clocks and restored pose; no visible Window is created. |
| dotnet run --project Mcp/Mizon.Windows.Mcp.Tests -c Release -- --isolated | Passed: 47/47 groups | Includes music-home schema/dispatch and permission/failure/cancellation/lifecycle coverage; synthetic operations only. |
| dotnet run --project Mcp/Mizon.Windows.Mcp.AccessTests -c Release --no-launch-profile | Passed: 276 assertions | Production adapters with synthetic consent and isolated pipes/store; no real app or account accessed. |

Diagnostic output is outside the repository under
`D:/MizonTaskDiagnostics/Presence/verification/` (story-orbit-pointer build/MCP
logs and orbit-native-probe/probe-results.txt under its build output).
No Windows UI automation, real consent/account writes or user app restart.

## Handoff

Live pointer smoothness, rapid boundary crossings, scrolling/resize/DPI,
minimize/reactivation and Narrator output have not been visually tested.
Native checks establish API/lifecycle correctness, not frame-time measurements.
Use the user's normal next Visual Studio build/run to inspect the new behavior.

## Publication

Integrate the reviewed task-owned commit into clean main with a guarded
fast-forward, push normally and verify the remote SHA. The containing commit is
identified by git log for this file.
