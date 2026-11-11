# Restore the three editorial photographs after native geometry initialization failed

- Status: complete; live presentation remains unverified
- Owner: Codex root; independent log/native-runtime review by orbit_reference_review
- Date: 2026-11-12
- Repository/branch: Mizon.Windows, codex/fix-orbit-image-loading
- Base commit: 242d690d5bde98bca874715881d669f770e24b55
- Owned files: StoryOrbitCard.xaml.cs, this note and HANDOFF
- No backend, SDK, asset or layout changes

## Evidence and correction

The real packaged-process log at 00:04:02 on 2026-11-12 records three identical
ArgumentExceptions from `Microsoft.UI.Xaml.Shapes.Path.set_Data`, called by
StoryOrbitCard.CreateArtwork. The source assigned the same PathGeometry to
Outline.Data and MatteShape.Data; WinUI rejected the second owner. The catch
then collapsed PictureFrame, so valid images disappeared.

All three photographs exist in the running app's AppX directory and SHA-256
matches their corresponding source asset. This was not a download or URI error.
The log lives under the package's LocalCache/Local/Mizon/Logs; reparse points
mean a recursive rg search of Packages may omit it.

- Allocate separate PathGeometry/PathFigure/BezierSegment trees from the same
  immutable contour coordinates. Do not share parent-owned XAML geometry.
- Initialize the photograph's clip before optional matte/shadows.
- Isolate decoration and animation exceptions. Decoration cleanup detaches only
  its masks/visuals/path trees; the photo/clip remains visible. A motion failure
  only stops motion. Full unload still releases both image and decorations.
- Preserve design, paths, source images, motion, shadow settings and NativeAOT-
  compatible typed construction. Reuse existing music-home MCP contracts.

## Verification

- Packaged assets/hash inspection and real application failure stack: confirmed.
- Debug x64/.NET 11 build: passed; pre-existing warnings remain.
- Existing isolated MCP protocol/permission/lifecycle checks: 47/47 groups and
  276 service assertions passed. No real account/consent/app access.
- Native WinUI component probe, same Windows App SDK 2.5.4-experimental:
  shared geometry reproduces ArgumentException 0x80070057; independent geometry
  succeeds. DropShadow blur values 60, 72 and 76 all succeed with exact readback;
  the shadow settings therefore need no speculative change. The probe creates
  no Window and does not control the running Mizon instance.
- Extended native probe with the production StoryOrbitGeometry coordinates:
  all three variants pass two create/detach cycles through XAML contours,
  Win2D/Composition geometric clips, shared brushes, alpha masks and shadows.
  This verifies initialization/ownership, not displayed pixels or bitmap decode.
- Independent source/lifecycle review: passed.

The user's running app/Visual Studio session is not restarted or controlled.
Live presentation after rebuilding remains separate from headless checks.

## Publication

Commit the three task-owned files, fast-forward a clean main checkout and push
normally. Verify the remote SHA; the delivering turn reports the commit.
