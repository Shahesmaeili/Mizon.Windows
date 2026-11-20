# Restore track details title, playlist menu and waveform hover

- Owner: Codex
- Date: 2026-11-20
- Repository: Mizon.Windows, based on `b1bc2fd`
- Owned areas: track details heading, playlist and device picker templates, waveform
  transitions and hover boundaries
- Related work: [track details modernization](2026-11-20-music-details.md)

The heading updates from the track view model on load and on title or language
changes. A translated title is optional and never gates the original title.
The playlist trigger and the create row use the original button template and
spacing with localized text. The audio picker uses the same thumbnail and
two-line row layout, with native radio selection and the existing volume slider.
Creation uses the existing dialog and operation from either the button or the
menu item's native activation. Duplicate activation and stale dialogs are guarded.

Comment markers put their translation on the wrapper and their opacity transition
on the button. Detached markers restore opacity without starting animation.
The waveform restores brush transitions for the bars affected by
playback or hover and follows the Windows animation preference. Hover in both
rows changes the highlight at the stroke's leading edge. The fixed six-unit slots
skip layout rounding so the hover boundary does not drift at fractional DPI.
Seek and comment timestamps still use the full timeline coordinate.

No server, SDK or MCP contract changes were required. The existing music-detail
permissions and state continue to apply.

## Verification

- Debug x64 WinUI build and Release win-x64 NativeAOT publication passed.
- The headless suites passed 344 MCP access assertions, 49 isolated MCP protocol
  groups and 301 controller input/session assertions. The access suite checks
  rapid hover movement across stroke edges in both directions.
- A hidden WinUI fixture reproduced the exact access-denied failure and exercised
  the split animation layers, detach cleanup and both bar transitions. It used
  the relevant XAML fragments with a substitute portrait and no user input.
- The existing warnings remain visible. Packaged-page visuals, physical
  controller operation and live Narrator output were not exercised.

## Publication

Target branch: `main`. No dependency changes or deployment needed.
