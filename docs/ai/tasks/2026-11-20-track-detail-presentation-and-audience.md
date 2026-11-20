# Track details presentation and audience panels

- Owner: Codex
- Windows base: `c683003`
- Database dependency: `20261119232657_AddTrackMusicVideos`, verified locally
  in [the database repair](https://github.com/Shahesmaeili/Mizon.Database/blob/main/docs/ai/tasks/2026-11-20-track-details-schema-repair.md)

The playback-device picker uses the playlist picker’s native dropdown and
MenuFlyout chrome, with text-only device rows, native radio selection and the
existing volume slider. The support action is a circular heart button with the
shared hover, press and focus animation. The four header actions use a
four-unit gap and still wrap according to measured width. While a track is
active, the native window caption uses that track’s localized display title.

The loading presentation covers artwork, metadata and the waveform. Track
metadata crossfades from its placeholder when ready and replaces it with a
localized error on failure. Composition reveals bars across the viewport and
then sweeps in the comments. Navigation and reduced motion release animations;
no per-bar timer runs. Comment avatars keep their stroke during hover, use a
hand cursor, and have their stroke and screen-reader annotation determined
from the author ID. Stacked markers stay below the two-unit waveform gap.

The combined audience control has separate buttons for views and live
listeners. The live pane uses the existing SignalR listener roster, positions
and play state. Platform, historical audience and per-account counts are
clearly labeled samples. The current profile is shown above the history
scroller. A one-second visible-popup timer refreshes real listeners and is
released on close or navigation.

The existing Track details MCP scopes cover loading stages, bounded audience
counts and opening or closing the two panes. No listener names or sample
history leave the application through these tools.

## Verification

- Debug x64 and Release win-x64 NativeAOT builds passed.
- MCP access checks covered the loading state, audience authorization,
  validated panel choices, author matching and waveform marker placement.
- Controller input/session checks passed. A separate hidden WinUI fixture
  checked the production support template, passive audio menu host, loading
  failure and cleanup of both waveform reveal layers.
- No LiveCharts reference or package appears in the current source or the
  clean NuGet restore graph.
- Live screen appearance, physical Xbox input, audible Narrator output and
  a real multi-device listener session were not exercised.
