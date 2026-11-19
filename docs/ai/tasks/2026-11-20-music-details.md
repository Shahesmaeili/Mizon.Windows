# Track detail responsive layout and input

- Owner: Codex
- Branch: `codex/music-details-modernization`, base `401ea80`
- Owns: Windows track details, timed waveform presentation, transport, cover
  preview, contributor portraits, and MCP track-detail permissions
- API dependencies: existing track details, credits and media contracts

The header measures the title and its action group before placing them. The
four actions share the title row whenever their natural widths fit. At smaller
widths, the group moves below and can wrap internally. The four lower cards
use actual view counts, duration, release metadata and contributor data. They
share the responsive card panel and do not initialize a chart engine.

The transport exposes native Play/Pause and Repeat controls with independent
focus stops. The waveform and the accessible percentage slider use the same
seek operation. D-pad and left stick move focus, A activates native controls,
and B closes an open cover preview before leaving the page. The cover preview
has native zoom buttons, respects the Windows animation setting and gives
focus back to the cover when dismissed. Contributor portraits are decoded from
the local cache at display size, shared between duplicate credits and replaced
by initials when unavailable. The support button opens a share action using the
real track deep link. Payment requires a separate integration.
Endpoint master-volume reads use the documented Core Audio ABI via
source-generated interop. Each interface and COM initialization reference is
released on the calling thread. This path reads the device without changing
its volume.

MCP discovery exposes `mizon_get_music_details` and
`mizon_set_music_details`. Normal navigation remains scoped to the existing
navigation tools. Independent Track details read and control grants cover the
active track only. The control accepts either `repeatEnabled` or `coverOpen`,
each as an explicit boolean. Read status includes whether the cover and
contributor portraits are available. No media files, URLs, credentials or
payment data are returned.

Verification:

- .NET 11 x64 Debug build and full Windows x64 Release NativeAOT publication.
  Existing unrelated trim/AOT diagnostics are still reported for DevWinUI,
  SkiaSharp and existing reflection paths.
- An offscreen WinUI probe passed nine checks in managed and NativeAOT builds:
  the measured row boundary, responsive card layout in both directions, and
  cache-file portrait decoding and cancellation.
- A separate NativeAOT Core Audio probe read the current Windows render
  endpoint and handled a nonexistent endpoint without changing any settings.
- 330 MCP access assertions, 49 isolated companion protocol groups, the
  published NativeAOT companion catalog, 301 controller input/session assertions
  and 70 source-linked focus-navigation assertions passed.
- Packaged-page visuals and animations, physical Xbox hardware and live
  Narrator output were not exercised.
