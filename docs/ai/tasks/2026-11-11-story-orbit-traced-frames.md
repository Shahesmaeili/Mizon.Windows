# Trace the editorial photo cuts, paper rims and shadows from the reference

- Status: complete; live UI validation remains separate as recorded below
- Owner: Codex root; read-only geometry/lifecycle review by orbit_reference_review,
  photo registration by orbit_photo_source
- Date: 2026-11-11
- Repository/branch: Mizon.Windows, codex/trace-story-orbit-frames
- Base commit: 64b7a35baf39ffc4de4b6fecc1092b737eac2871
- Related task: 2026-11-11-story-orbit-reference-match; no backend/SDK change
- Owned files: StoryOrbitCard XAML/code, StoryOrbitShowcase XAML,
  Common/StoryOrbitGeometry.cs, source credits, this note and HANDOFF

## Request and acceptance criteria

The user rejected approximate blobs and requested the reference's exact photo
cutouts, outer boundaries, variable rim spacing and shadows. Similar photography
is acceptable. Preserve the three editorial labels, gentle independent motion,
glass background and placement immediately above the stories.

## Implementation and decisions

- Six closed cubic paths are traced separately from visible image/paper edges
  in the 1600x1200 source. The left waist, middle wedge and right concave scoop
  are no longer approximated by a generic blob and scaled duplicate outlines.
  Hidden edges under text/badges are interpolated. The extra echo line is removed.
- A single parent Viewbox scales the entire layout. Each local canvas maps to
  the same original reference coordinates, preserving unequal shape proportions,
  vertical staggering, badges, editions and overlapping white serif lettering.
- Middle/right source photographs are registered by face/hair landmarks against
  the reference; full image rectangles preserve their aspect ratio and are
  cropped only by native Composition clips. Source image bytes are unchanged.
  The first image remains the user-approved similar fictional portrait.
- Each paper edge has a faint translucent matte, a single thin outline and
  two soft native alpha shadows. Per-card tint/opacity avoids a dark grey rim
  on the blue/gold artwork. Reserved horizontal/bottom space keeps shadows from
  being cut off by the horizontal ScrollViewer. No opaque section background.
- Gentle, independently phased Composition motion remains for photo, portrait,
  rim, edition, badge and title. Small excursions preserve the authored spacing;
  no per-frame layout updates, UI timer, reflection, XamlReader or new package.
- Existing viewport/minimize/reduced-motion handling is unchanged. Unload stops
  subscriptions/animations, detaches clips and child visuals, clears pictures and
  path data, and disposes only owned compositor objects. Live XAML alpha masks
  remain XAML-owned. Decorative composition errors cannot block page navigation.
- This is visual-only. Existing music-home MCP contracts and independent consent
  remain unchanged; no synthetic style-only tool/test was added. The decorative
  English text remains excluded from the Narrator focus tree.

## Validation

| Check | Result | Limits |
| --- | --- | --- |
| Offline reference trace overlay | Six paths visually inspected; sampled visible landmarks within about 1.1–1.6 source pixels | Occluded boundaries interpolated; not original author vectors |
| Offline geometry/crop composition | Compared reference and native path data, including full photo transforms and approximate shadows/type | Diagnostic raster renderer is not a WinUI screenshot |
| Independent native/lifecycle review | Mask/clip coordinates, closure, source coverage and ownership passed; shadow margin increased | No live app interaction |
| Visual Studio 18 Insiders MSBuild, Debug x64, .NET 11 | Final build passed after the shadow-spacing correction | Existing warnings remain; no full-app NativeAOT publish |
| MCP protocol/dispatch tests, Release --isolated | 47/47 groups passed | Synthetic IPC/consent; no real app/account |
| MCP access/production adapter tests, Release | 276 assertions passed | Isolated stores and test doubles |

No Windows UI automation or Visual Studio/session restart was performed. Actual
WinUI font rasterization, shadow rendering over the user's material, DPI resizing
and animation frame pacing still need visual confirmation in the next app run.
The first photo and open Bodoni Moda font are substitutes, as documented in
SOURCES.md; do not claim literal pixel identity with the flattened reference.

## Publication

Commit only these reviewed task files. Integrate by a guarded fast-forward into
the clean main checkout, push normally and verify the remote SHA. The delivering
turn reports that commit; Git history identifies this note without a circular
self-referential hash.
