# Track detail responsive layout and input

- Owner: Codex
- Branch: `codex/music-details-modernization`, base `401ea80`
- Owns: Windows track details, timed waveform presentation, transport, cover
  preview, contributor portraits, and MCP track-detail permissions
- API dependencies: existing track details, credits and media contracts

The header measures the title and its action group before placing them. The
four actions share the title row whenever their natural widths fit. At smaller
widths, the group moves below and can wrap internally. The four lower cards
retain the original sample heart rate, listener feedback, heart-rate trend and
AI summary. The four cards keep the original compact height, colored icons,
rings, arrow edges and line series. Native XAML shapes draw the three rings
and seven original trend samples without the chart engine, load callbacks,
or composition animations. Cards fill the available width and retain the
16-unit gap when wrapping into one, two or four columns. The fullscreen
presenter uses a more compact four-column threshold. In narrow cards only the
middle content scales down. The sample caption remains absent.

The transport exposes native Play/Pause and Repeat controls with independent
focus stops. Only the original waveform is shown for seeking. With focus on
the waveform, LT/RT or Page Up/Page Down move the same playback position by
five seconds. D-pad and left stick move focus, A activates native controls,
and B closes an open cover preview before leaving the page. The cover preview
has native zoom buttons, respects the Windows animation setting and gives
focus back to the cover when dismissed. Contributor portraits are decoded from
the local cache at display size, shared between duplicate credits and replaced
by initials when unavailable. The themed support button opens a native flyout
that shares the real track deep link. Payment requires a separate integration.
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
- The visual follow-up repeated Debug, NativeAOT, MCP access and controller
  session checks. An offscreen WinUI fixture rendered the four original cards
  in one, two and four columns and verified the support template in Light and
  Dark using substitute glyphs and system brush values. A separate 14-case
  probe exercised the waveform's production keyboard and controller handler
  with synthetic focus and audio.
- The four-widget redesign was checked in offscreen WinUI at 260, 725, 1000
  and 1320 pixels. The middle widths use two balanced columns, the widest
  fits four, and the narrow layout shows one. The fixture also verified there
  is no sample caption in Persian or English.
- The original-layout restoration tested the actual compiled control in a
  hidden WinUI scroller. It verified chart creation on visibility, retention
  during scrolling, release on detach and recreation on return. It also
  verified the original card offsets, 16-unit row spacing and fit at a
  240-unit viewport. The existing MCP permission checks and the app's
  Debug build and NativeAOT publication passed again. The separate native
  fixture failed at startup because its linker inputs omitted WinRT.Runtime.
  Chart lifecycle was exercised in the fixture's managed WinUI run.
- The follow-up Windows event reported a native CoreMessagingXP failure at
  track-page entry without a corresponding managed exception. It did not
  identify the exact failing control. The replacement native-shape widgets
  passed an isolated compiled WinUI test across 11 widths or presentation
  combinations and 24 attach, resize and detach cycles. The fixture verified
  that the last card reaches the end of the available row at each width.
- Packaged-page visuals and animations, physical Xbox hardware and live
  Narrator output were not exercised.
